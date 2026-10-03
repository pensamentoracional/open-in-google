using Microsoft.Data.Sqlite;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record BackupRecord(Guid Id, DateTimeOffset CapturedAt, string Hash, long Bytes, bool CopyCompleted, int State);
// Cleanup tombstones retain identity/history. 0=stored, 1=deletion intent, 2=deleted.
public sealed class BackupCatalog
{
    public static string PathFor(LocalStorage storage) => Path.Combine(storage.Root, "backup-lifecycle.db");
    private readonly string connection;
    public BackupCatalog(LocalStorage storage)
    {
        PrivateDirectory.Create(storage.Root); var path = PathFor(storage);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid backup catalog.");
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "PRAGMA user_version"; if (Convert.ToInt32(cmd.ExecuteScalar()) > 1) throw new NotSupportedException("Newer backup catalog.");
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS backups(id TEXT PRIMARY KEY,captured TEXT NOT NULL,hash TEXT NOT NULL,bytes INTEGER NOT NULL CHECK(bytes>=0),copy_completed INTEGER NOT NULL DEFAULT 0 CHECK(copy_completed IN (0,1)),state INTEGER NOT NULL DEFAULT 0 CHECK(state IN (0,1,2))); CREATE TABLE IF NOT EXISTS events(seq INTEGER PRIMARY KEY,id TEXT NOT NULL,kind TEXT NOT NULL,at TEXT NOT NULL); PRAGMA user_version=1;";
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connection); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA synchronous=FULL"; cmd.ExecuteNonQuery(); return db; }
    private static SqliteCommand Cmd(SqliteConnection db, string sql, params (string Key, object Value)[] values)
    { var cmd = db.CreateCommand(); cmd.CommandText = sql; foreach (var (k,v) in values) cmd.Parameters.AddWithValue(k,v); return cmd; }
    public Dictionary<Guid, BackupRecord> All()
    {
        var result=new Dictionary<Guid,BackupRecord>();using var db=Open();using var cmd=Cmd(db,"SELECT id,captured,hash,bytes,copy_completed,state FROM backups");using var r=cmd.ExecuteReader();while(r.Read()){var id=Guid.Parse(r.GetString(0));result.Add(id,new(id,DateTimeOffset.Parse(r.GetString(1),System.Globalization.CultureInfo.InvariantCulture),r.GetString(2),r.GetInt64(3),r.GetBoolean(4),r.GetInt32(5)));}return result;
    }
    public BackupRecord? Get(Guid id)
    {
        using var db = Open(); using var cmd = Cmd(db, "SELECT captured,hash,bytes,copy_completed,state FROM backups WHERE id=$id", ("$id", id.ToString("N")));
        using var r = cmd.ExecuteReader(); return r.Read() ? new(id, DateTimeOffset.Parse(r.GetString(0), System.Globalization.CultureInfo.InvariantCulture), r.GetString(1), r.GetInt64(2), r.GetBoolean(3), r.GetInt32(4)) : null;
    }
    public void Register(Guid id, Snapshot snapshot, DateTimeOffset captured)
    {
        if (Get(id) is { } old) { if (old.Hash != snapshot.Sha256 || old.Bytes != snapshot.Length) throw new LocalConflictException("Backup catalog binding changed."); return; }
        using var db = Open(); using var cmd = Cmd(db,"INSERT INTO backups(id,captured,hash,bytes) VALUES($id,$at,$hash,$bytes)",("$id",id.ToString("N")),("$at",captured.ToUniversalTime().ToString("O")),("$hash",snapshot.Sha256),("$bytes",snapshot.Length)); cmd.ExecuteNonQuery();
    }
    public void CopyComplete(Guid id, DateTimeOffset at) => Transition(id, "copy_completed=1", "state=0", "CopyCompleted", at);
    public void BeginDelete(Guid id, DateTimeOffset at) => Transition(id, "state=1", "state=0", "DeleteIntent", at);
    public void FinishDelete(Guid id, DateTimeOffset at) => Transition(id, "state=2", "state=1", "Deleted", at);
    private void Transition(Guid id, string assignment, string condition, string kind, DateTimeOffset at)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using var update = Cmd(db,$"UPDATE backups SET {assignment} WHERE id=$id AND {condition}",("$id",id.ToString("N"))); update.Transaction=tx;
        if (update.ExecuteNonQuery()!=1) throw new BackupRemovedException();
        using var evt = Cmd(db,"INSERT INTO events(id,kind,at) VALUES($id,$kind,$at)",("$id",id.ToString("N")),("$kind",kind),("$at",at.ToUniversalTime().ToString("O"))); evt.Transaction=tx;evt.ExecuteNonQuery();tx.Commit();
    }
}
