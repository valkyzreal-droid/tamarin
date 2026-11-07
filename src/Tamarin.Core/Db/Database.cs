using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Tamarin.Core.Db;

/// <summary>The database file is newer than this build of the app understands.</summary>
public sealed class DatabaseTooNewException : Exception
{
    public int FileVersion { get; }
    public DatabaseTooNewException(int fileVersion)
        : base($"The notes database is version {fileVersion}, but this app only understands version {Database.CurrentVersion}. Update the app.")
        => FileVersion = fileVersion;
}

/// <summary>
/// One SQLite file, one connection, one lock. The app writes a few tiny rows (autosave) and reads on
/// demand, so a single serialized connection is simpler and safer than a pool. WAL keeps readers from
/// blocking the autosave writer if a second process ever opens the file.
/// </summary>
public sealed class Database : IDisposable
{
    public const int CurrentVersion = 1;

    /// <summary>SQL expression for "now" in unix milliseconds (UTC). Matches the schema defaults.</summary>
    public const string NowMsSql = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";

    private readonly SqliteConnection _conn;
    private readonly object _gate = new();
    private bool _disposed;

    public string Path { get; }

    private Database(string path)
    {
        Path = path;
        // Pooling off: a pooled connection keeps the file open after Dispose, which blocks
        // moving, deleting or restoring the database on Windows.
        _conn = new SqliteConnection($"Data Source={path};Pooling=False");
    }

    public static Database Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var db = new Database(full);
        try
        {
            db._conn.Open();
            db.ApplyPragmas();
            db.Migrate();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private void ApplyPragmas()
    {
        // Per-connection settings; they are not stored in the file.
        Exec("PRAGMA journal_mode = WAL;");
        Exec("PRAGMA foreign_keys = ON;");
        Exec("PRAGMA synchronous = FULL;");   // tiny writes; losing text is worse than speed
        Exec("PRAGMA secure_delete = ON;");   // deleted note text is overwritten, not left in free pages
        Exec("PRAGMA busy_timeout = 3000;");
    }

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        if (version > CurrentVersion) throw new DatabaseTooNewException(version);
        if (version < 1) Exec(ReadEmbedded("Tamarin.Core.Db.001_initial.sql"));
        // Future migrations: if (version < 2) Exec(ReadEmbedded("...002_....sql"));
    }

    private static string ReadEmbedded(string name)
    {
        var asm = typeof(Database).Assembly;
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Runs work on the connection while holding the lock. Keep the work short.</summary>
    public T Run<T>(Func<SqliteConnection, T> work)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return work(_conn);
        }
    }

    public void Run(Action<SqliteConnection> work) => Run<object?>(c => { work(c); return null; });

    /// <summary>Runs work inside BEGIN IMMEDIATE ... COMMIT, rolling back if it throws.</summary>
    public T InTransaction<T>(Func<SqliteConnection, T> work) => Run(c =>
    {
        Execute(c, "BEGIN IMMEDIATE;");
        try
        {
            var result = work(c);
            Execute(c, "COMMIT;");
            return result;
        }
        catch
        {
            try { Execute(c, "ROLLBACK;"); } catch { /* the original error matters more */ }
            throw;
        }
    });

    public int UserVersion => Convert.ToInt32(Scalar("PRAGMA user_version;"));

    /// <summary>PRAGMA quick_check. Returns "ok" when the file looks healthy.</summary>
    public string QuickCheck() => Convert.ToString(Scalar("PRAGMA quick_check;")) ?? "";

    public object? Scalar(string sql) => Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    });

    public void Exec(string sql) => Run(c => Execute(c, sql));

    internal static int Execute(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _conn.Dispose();
        }
    }
}
