namespace Tamarin.Core;

/// <summary>
/// Where the data lives. Normal mode: %APPDATA%\Tamarin (no admin rights needed).
/// Portable mode: if a file named portable.flag sits next to the .exe, everything is kept in a
/// "data" folder beside it, so the whole app runs from a pendrive and leaves nothing on the PC.
/// </summary>
public sealed record AppPaths(string DataDirectory, bool Portable)
{
    public const string PortableFlag = "portable.flag";

    public string DatabasePath => Path.Combine(DataDirectory, "notes.db");
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static AppPaths Resolve(string exeDirectory, string appDataDirectory)
    {
        if (File.Exists(Path.Combine(exeDirectory, PortableFlag)))
            return new AppPaths(Path.Combine(exeDirectory, "data"), true);
        return new AppPaths(Path.Combine(appDataDirectory, "Tamarin"), false);
    }

    /// <summary>
    /// The app was called Quicknote while it was being built. If that data folder exists and the new
    /// one does not, move it over so nobody loses notes by the rename. Returns true if it moved.
    /// </summary>
    public static bool MigrateLegacyFolder(string appDataDirectory)
    {
        var old = Path.Combine(appDataDirectory, "Quicknote");
        var now = Path.Combine(appDataDirectory, "Tamarin");
        if (!Directory.Exists(old) || Directory.Exists(now)) return false;
        Directory.Move(old, now);
        return true;
    }
}
