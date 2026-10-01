using Microsoft.Data.Sqlite;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

/// <summary>Short immediate transactions: state and event commit together; no transaction spans IO/network.</summary>
public sealed class SqliteOperationRegistry : IOperationRegistry
{
    private readonly string connectionString;
    public SqliteOperationRegistry(string databasePath)
    {
        var full = Path.GetFullPath(databasePath);
        PrivateDirectory.Create(Path.GetDirectoryName(full)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        using var version = Command(db, tx, "PRAGMA user_version;");
        var current = Convert.ToInt32(version.ExecuteScalar());
        if (current > 2) throw new NotSupportedException("Database schema is newer than this application.");
        if (current == 0)
        {
            using var schema = Command(db, tx, """
                CREATE TABLE operations (
                    id TEXT PRIMARY KEY, account_id TEXT NOT NULL, source_key TEXT NOT NULL,
                    source_path TEXT NOT NULL, format TEXT NOT NULL CHECK(format IN ('xlsx','ods','xls','csv','tsv')),
                    state TEXT NOT NULL CHECK(state IN ('Prepared','SnapshotReady')),
                    backup_path TEXT, sha256 TEXT, length INTEGER, version INTEGER NOT NULL,
                    CHECK((state='Prepared' AND backup_path IS NULL AND sha256 IS NULL AND length IS NULL)
                       OR (state='SnapshotReady' AND backup_path IS NOT NULL AND sha256 IS NOT NULL AND length IS NOT NULL AND length>=0 AND length(sha256)=64)),
                    UNIQUE(account_id,source_key));
                CREATE TABLE source_aliases (
                    account_id TEXT NOT NULL, source_key TEXT NOT NULL, path TEXT NOT NULL,
                    PRIMARY KEY(account_id,source_key,path));
                CREATE TABLE journal (
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL REFERENCES operations(id),
                    kind TEXT NOT NULL, code TEXT, created_at TEXT NOT NULL);
                PRAGMA user_version=2;
                """);
            schema.ExecuteNonQuery();
        }
        if (current == 1)
        {
            using var migrate = Command(db, tx, """
                CREATE TABLE operations_v2 (
                    id TEXT PRIMARY KEY, account_id TEXT NOT NULL, source_key TEXT NOT NULL,
                    source_path TEXT NOT NULL, format TEXT NOT NULL CHECK(format IN ('xlsx','ods','xls','csv','tsv')),
                    state TEXT NOT NULL CHECK(state IN ('Prepared','SnapshotReady')),
                    backup_path TEXT, sha256 TEXT, length INTEGER, version INTEGER NOT NULL,
                    CHECK((state='Prepared' AND backup_path IS NULL AND sha256 IS NULL AND length IS NULL)
                       OR (state='SnapshotReady' AND backup_path IS NOT NULL AND sha256 IS NOT NULL AND length IS NOT NULL AND length>=0 AND length(sha256)=64)),
                    UNIQUE(account_id,source_key));
                INSERT INTO operations_v2 SELECT * FROM operations;
                CREATE TABLE journal_v2 (
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL REFERENCES operations_v2(id),
                    kind TEXT NOT NULL, code TEXT, created_at TEXT NOT NULL);
                INSERT INTO journal_v2 SELECT * FROM journal;
                DROP TABLE journal;
                DROP TABLE operations;
                ALTER TABLE operations_v2 RENAME TO operations;
                ALTER TABLE journal_v2 RENAME TO journal;
                PRAGMA user_version=2;
                """);
            migrate.ExecuteNonQuery();
        }
        tx.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        try
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;";
            cmd.ExecuteNonQuery();
            return db;
        }
        catch { db.Dispose(); throw; }
    }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql,
        params (string Key, object? Value)[] parameters)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static ImportOperation Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1),
        r.GetString(2), r.GetString(3), r.GetString(4), Enum.Parse<OperationState>(r.GetString(5)),
        r.IsDBNull(6) ? null : new Snapshot(r.GetString(6), r.GetString(7), r.GetInt64(8)), r.GetInt64(9));
    private const string Columns = "id,account_id,source_key,source_path,format,state,backup_path,sha256,length,version";
    private static void Event(SqliteConnection db, SqliteTransaction tx, Guid id, string kind, string? code = null)
    {
        using var cmd = Command(db, tx,
            "INSERT INTO journal(operation_id,kind,code,created_at) VALUES($id,$kind,$code,$time)",
            ("$id", id.ToString("N")), ("$kind", kind), ("$code", code), ("$time", DateTimeOffset.UtcNow.ToString("O")));
        cmd.ExecuteNonQuery();
    }
    public ImportOperation GetOrCreate(string accountId, SourceDescriptor source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.IdentityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Path);
        if (!SpreadsheetFormats.Extensions.Contains("." + source.Format)) throw new NotSupportedException("Unsupported format.");
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        ImportOperation? op;
        using (var find = Command(db, tx, $"SELECT {Columns} FROM operations WHERE account_id=$a AND source_key=$s",
            ("$a", accountId), ("$s", source.IdentityKey)))
        using (var read = find.ExecuteReader()) op = read.Read() ? Read(read) : null;
        if (op is null)
        {
            op = new(Guid.NewGuid(), accountId, source.IdentityKey, source.Path, source.Format, OperationState.Prepared, null, 0);
            using var insert = Command(db, tx,
                "INSERT INTO operations(id,account_id,source_key,source_path,format,state,version) VALUES($id,$a,$s,$p,$f,'Prepared',0)",
                ("$id", op.Id.ToString("N")), ("$a", accountId), ("$s", source.IdentityKey), ("$p", source.Path), ("$f", source.Format));
            insert.ExecuteNonQuery(); Event(db, tx, op.Id, "Prepared");
        }
        if (op.Format != source.Format) throw new LocalConflictException("Source extension changed its interpretation.");
        using (var alias = Command(db, tx, "INSERT OR IGNORE INTO source_aliases(account_id,source_key,path) VALUES($a,$s,$p)",
            ("$a", accountId), ("$s", source.IdentityKey), ("$p", source.Path))) alias.ExecuteNonQuery();
        tx.Commit(); return op;
    }
    public ImportOperation? Get(Guid id)
    {
        using var db = Open(); using var cmd = Command(db, null, $"SELECT {Columns} FROM operations WHERE id=$id", ("$id", id.ToString("N")));
        using var read = cmd.ExecuteReader(); return read.Read() ? Read(read) : null;
    }
    public ImportOperation CommitSnapshot(Guid id, long expectedVersion, Snapshot snapshot)
    {
        if (snapshot.Length < 0 || snapshot.Sha256.Length != 64 || !snapshot.Sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid snapshot metadata.");
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.BackupPath);
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using var update = Command(db, tx,
            "UPDATE operations SET state='SnapshotReady',backup_path=$p,sha256=$h,length=$l,version=version+1 WHERE id=$id AND version=$v AND state='Prepared'",
            ("$p", snapshot.BackupPath), ("$h", snapshot.Sha256), ("$l", snapshot.Length), ("$id", id.ToString("N")), ("$v", expectedVersion));
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Stale or invalid operation transition.");
        Event(db, tx, id, "SnapshotReady"); tx.Commit(); return Get(id)!;
    }
    public void RecordFailure(Guid id, string code)
    {
        if (code is not ("source_conflict" or "cancelled" or "local_preparation_failed"))
            throw new ArgumentException("Failure code must be a supported non-sensitive code.");
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        Event(db, tx, id, "Failure", code); tx.Commit();
    }
    public IReadOnlyList<ImportOperation> Pending()
    {
        var result = new List<ImportOperation>();
        using var db = Open(); using var cmd = Command(db, null, $"SELECT {Columns} FROM operations ORDER BY id");
        using var read = cmd.ExecuteReader(); while (read.Read()) result.Add(Read(read)); return result;
    }
    public IReadOnlyList<JournalEvent> Events(Guid id)
    {
        var result = new List<JournalEvent>();
        using var db = Open(); using var cmd = Command(db, null,
            "SELECT sequence,kind,code FROM journal WHERE operation_id=$id ORDER BY sequence", ("$id", id.ToString("N")));
        using var read = cmd.ExecuteReader();
        while (read.Read()) result.Add(new(read.GetInt64(0), id, read.GetString(1), read.IsDBNull(2) ? null : read.GetString(2)));
        return result;
    }
}
