using Microsoft.Data.Sqlite;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class GoogleRemoteRegistry : IRemoteRegistry
{
    private readonly string connection;
    public GoogleRemoteRegistry(string path)
    {
        PrivateDirectory.Create(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Pooling = false, DefaultTimeout = 10 }.ToString();
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using var v = Cmd(db, tx, "PRAGMA user_version");
        var version = Convert.ToInt32(v.ExecuteScalar());
        if (version > 1) throw new NotSupportedException("Remote registry is newer than this application.");
        if (version == 0)
        {
            using var cmd = Cmd(db, tx, """
                CREATE TABLE attempts(key TEXT PRIMARY KEY, account TEXT NOT NULL, kind TEXT NOT NULL CHECK(kind IN ('sheet','folder')),
                  hash TEXT NOT NULL, marker TEXT NOT NULL UNIQUE, file_id TEXT, verified INTEGER NOT NULL DEFAULT 0 CHECK(verified IN (0,1)),
                  CHECK(verified=0 OR file_id IS NOT NULL));
                CREATE TABLE events(seq INTEGER PRIMARY KEY, key TEXT NOT NULL REFERENCES attempts(key), kind TEXT NOT NULL, at TEXT NOT NULL);
                PRAGMA user_version=1;
                """); cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        try { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL"; cmd.ExecuteNonQuery(); return db; }
        catch { db.Dispose(); throw; }
    }
    private static SqliteCommand Cmd(SqliteConnection db, SqliteTransaction? tx, string text, params (string, object?)[] args)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = text;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value); return cmd;
    }
    private static RemoteAttempt? Read(SqliteConnection db, SqliteTransaction? tx, string key)
    {
        using var cmd = Cmd(db, tx, "SELECT key,account,kind,hash,marker,file_id,verified FROM attempts WHERE key=$k", ("$k", key));
        using var r = cmd.ExecuteReader(); return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6)) : null;
    }
    public Dictionary<string, RemoteAttempt> All()
    {
        var result=new Dictionary<string,RemoteAttempt>();using var db=Open();using var cmd=Cmd(db,null,"SELECT key,account,kind,hash,marker,file_id,verified FROM attempts");using var r=cmd.ExecuteReader();while(r.Read()){var key=r.GetString(0);result.Add(key,new(key,r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetBoolean(6)));}return result;
    }
    public RemoteAttempt? Get(string key) { using var db = Open(); return Read(db, null, key); }
    public RemoteAttempt Begin(string key, string accountId, string kind, string hash)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var old = Read(db, tx, key);
        if (old is not null) throw new InvalidOperationException("Creation intent already exists.");
        var attempt = new RemoteAttempt(key, accountId, kind, hash, Guid.NewGuid().ToString("N"), null, false);
        using var cmd = Cmd(db, tx, "INSERT INTO attempts(key,account,kind,hash,marker) VALUES($k,$a,$t,$h,$m)", ("$k", key), ("$a", accountId), ("$t", kind), ("$h", hash), ("$m", attempt.Marker)); cmd.ExecuteNonQuery();
        Event(db, tx, key, "Intent"); tx.Commit(); return attempt;
    }
    private static void Event(SqliteConnection db, SqliteTransaction tx, string key, string kind)
    { using var cmd = Cmd(db, tx, "INSERT INTO events(key,kind,at) VALUES($k,$t,$a)", ("$k", key), ("$t", kind), ("$a", DateTimeOffset.UtcNow.ToString("O"))); cmd.ExecuteNonQuery(); }
    private void Set(string key, string id, bool verified)
    {
        GoogleDriveClient.ValidateId(id);
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var previous = Read(db, tx, key) ?? throw new InvalidOperationException("Missing creation intent.");
        if (previous.FileId == id && previous.Verified)
        {
            if (!verified) throw new InvalidOperationException("Cannot downgrade a verified association.");
            tx.Commit(); return;
        }
        using var cmd = Cmd(db, tx, "UPDATE attempts SET file_id=$id,verified=$v WHERE key=$k AND (file_id IS NULL OR file_id=$id)", ("$id", id), ("$v", verified ? 1 : 0), ("$k", key));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Conflicting remote association.");
        Event(db, tx, key, verified ? "Verified" : "Candidate"); tx.Commit();
    }
    public void Candidate(string key, string id) => Set(key, id, false);
    public void Verify(string key, string id) => Set(key, id, true);
}
