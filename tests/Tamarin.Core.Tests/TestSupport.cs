using Tamarin.Core.Db;
using Tamarin.Core.Notes;
using Tamarin.Core.Settings;

namespace Tamarin.Core.Tests;

/// <summary>A database in its own temp folder, removed afterwards.</summary>
internal sealed class TestDb : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tamarin-tests-" + Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Dir, "notes.db");
    public long Now { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public Database Db { get; private set; }
    public NoteRepository Notes { get; private set; }
    public SettingsStore Settings { get; private set; }

    public TestDb()
    {
        Directory.CreateDirectory(Dir);
        Db = Database.Open(DbPath);
        Notes = new NoteRepository(Db, () => Now);
        Settings = new SettingsStore(Db);
    }

    /// <summary>Closes and opens the file again, like quitting and restarting the app.</summary>
    public void Reopen()
    {
        Db.Dispose();
        Db = Database.Open(DbPath);
        Notes = new NoteRepository(Db, () => Now);
        Settings = new SettingsStore(Db);
    }

    /// <summary>Sets updated_at directly so ordering tests do not depend on the clock. (Not a trigger column.)</summary>
    public void SetUpdated(Note note, long updatedAt) =>
        Db.Exec($"UPDATE notes SET updated_at = {updatedAt} WHERE id = '{note.Id}';");

    public void Dispose()
    {
        Db.Dispose();
        try { Directory.Delete(Dir, true); } catch (IOException) { /* temp folder; the OS cleans it eventually */ }
    }
}

internal static class Wait
{
    public static bool Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }
}
