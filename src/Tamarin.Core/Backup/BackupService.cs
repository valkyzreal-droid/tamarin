using System.Globalization;
using Tamarin.Core.Db;

namespace Tamarin.Core.Backup;

/// <summary>
/// Daily copies of the database, made with VACUUM INTO (a consistent snapshot even while the app is
/// running). Backups hold the same data as the live file, including notes still in the trash.
/// </summary>
public sealed class BackupService
{
    private const string Prefix = "notes-";
    private const string Suffix = ".db";
    private const string Stamp = "yyyyMMdd-HHmmss";

    private readonly Database _db;
    private readonly string _folder;

    public BackupService(Database db, string folder)
    {
        _db = db;
        _folder = folder;
    }

    public string Folder => _folder;

    public IReadOnlyList<string> List() => ListIn(_folder);

    /// <summary>Lists backups without needing an open database, which is what recovery from a damaged file needs.</summary>
    public static IReadOnlyList<string> ListIn(string folder) =>
        !Directory.Exists(folder)
            ? Array.Empty<string>()
            : Directory.GetFiles(folder, Prefix + "*" + Suffix)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();

    public string? Latest() => List().FirstOrDefault();

    public static DateTime? TimeOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return DateTime.TryParseExact(name[Prefix.Length..], Stamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }

    /// <summary>Makes a backup if the newest one is older than <paramref name="maxAge"/>. Returns its path, or null if none was needed.</summary>
    public string? RunIfDue(DateTime utcNow, TimeSpan maxAge, int keep)
    {
        var latest = Latest();
        if (latest is not null && TimeOf(latest) is { } t && utcNow - t < maxAge) return null;
        var path = Run(utcNow);
        Prune(keep);
        return path;
    }

    public string Run(DateTime utcNow)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, Prefix + utcNow.ToString(Stamp, CultureInfo.InvariantCulture) + Suffix);
        if (File.Exists(path)) return path; // two runs in the same second
        _db.Run(c => Database.Execute(c, "VACUUM INTO $path;", ("$path", path)));
        return path;
    }

    /// <summary>Keeps the newest <paramref name="keep"/> backups and deletes the rest.</summary>
    public int Prune(int keep)
    {
        var removed = 0;
        foreach (var old in List().Skip(Math.Max(1, keep)))
        {
            try { File.Delete(old); removed++; }
            catch (IOException) { /* in use or locked; the next run tries again */ }
        }
        return removed;
    }
}
