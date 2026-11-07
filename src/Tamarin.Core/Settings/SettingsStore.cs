using Tamarin.Core.Db;

namespace Tamarin.Core.Settings;

/// <summary>
/// Settings live in the same database file as the notes, so one backup covers both.
/// Values are plain text; the typed properties below validate and clamp on the way in and out.
/// </summary>
public sealed class SettingsStore
{
    public const string DefaultHotkey = "Ctrl+Shift+Space";
    public const int MinTextSize = 11;
    public const int MaxTextSize = 28;

    private readonly Database _db;

    public SettingsStore(Database db) => _db = db;

    public string? Get(string key) => _db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    });

    public string Get(string key, string fallback) => Get(key) ?? fallback;

    public void Set(string key, string value) => _db.Run(c => Database.Execute(c,
        "INSERT INTO settings (key, value) VALUES ($k, $v) " +
        $"ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = {Database.NowMsSql};",
        ("$k", key), ("$v", value)));

    public bool GetBool(string key, bool fallback) => Get(key) switch
    {
        "1" => true,
        "0" => false,
        _ => fallback,
    };

    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    public int GetInt(string key, int fallback, int min, int max) =>
        int.TryParse(Get(key), out var n) ? Math.Clamp(n, min, max) : fallback;

    // ---- typed settings ------------------------------------------------------------------------

    public string Hotkey
    {
        get => Get("hotkey", DefaultHotkey);
        set => Set("hotkey", value);
    }

    public bool StartWithWindows
    {
        get => GetBool("start_with_windows", false);
        set => SetBool("start_with_windows", value);
    }

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme
    {
        get => Get("theme", "system") is "light" or "dark" ? Get("theme", "system") : "system";
        set => Set("theme", value is "light" or "dark" ? value : "system");
    }

    public int TextSize
    {
        get => GetInt("text_size", 14, MinTextSize, MaxTextSize);
        set => Set("text_size", Math.Clamp(value, MinTextSize, MaxTextSize).ToString());
    }

    public bool WindowPinned
    {
        get => GetBool("window_pinned", false);
        set => SetBool("window_pinned", value);
    }

    public int TrashRetentionDays
    {
        get => GetInt("trash_retention_days", 30, 1, 365);
        set => Set("trash_retention_days", Math.Clamp(value, 1, 365).ToString());
    }

    public bool BackupEnabled
    {
        get => GetBool("backup_enabled", true);
        set => SetBool("backup_enabled", value);
    }

    public int BackupKeep
    {
        get => GetInt("backup_keep", 7, 1, 60);
        set => Set("backup_keep", Math.Clamp(value, 1, 60).ToString());
    }

    /// <summary>Opt-in switch for anything that touches the network (live rates, update checks). Off by default.</summary>
    public bool NetworkFeatures
    {
        get => GetBool("network_features", false);
        set => SetBool("network_features", value);
    }

    public bool FirstRunDone
    {
        get => GetBool("first_run_done", false);
        set => SetBool("first_run_done", value);
    }

    // ---- window position, remembered per monitor -----------------------------------------------

    public WindowRect? GetWindowRect(string monitorId) =>
        WindowRect.TryParse(Get("window_rect:" + monitorId), out var r) ? r : null;

    public void SetWindowRect(string monitorId, WindowRect rect) => Set("window_rect:" + monitorId, rect.ToString());
}

/// <summary>Position and size in device pixels.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"{X},{Y},{Width},{Height}";

    public static bool TryParse(string? text, out WindowRect rect)
    {
        rect = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var p = text.Split(',');
        if (p.Length != 4) return false;
        if (!int.TryParse(p[0], out var x) || !int.TryParse(p[1], out var y) ||
            !int.TryParse(p[2], out var w) || !int.TryParse(p[3], out var h)) return false;
        if (w < 50 || h < 50) return false;
        rect = new WindowRect(x, y, w, h);
        return true;
    }

    public bool Intersects(WindowRect other) =>
        X < other.X + other.Width && other.X < X + Width &&
        Y < other.Y + other.Height && other.Y < Y + Height;
}
