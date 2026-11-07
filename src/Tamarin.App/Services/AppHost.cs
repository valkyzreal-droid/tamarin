using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Tamarin.App.Native;
using Tamarin.App.Views;
using Tamarin.Core;
using Tamarin.Core.Backup;
using Tamarin.Core.Capture;
using Tamarin.Core.Db;
using Tamarin.Core.Hotkeys;
using Tamarin.Core.Notes;
using Tamarin.Core.Settings;

namespace Tamarin.App.Services;

/// <summary>
/// Composition root and owner of everything long-lived: database, session, hotkey, tray, windows.
/// Windows ask it for things; it never reaches into their controls.
/// </summary>
internal sealed class AppHost : IDisposable
{
    public AppPaths Paths { get; }
    public Database Db { get; private set; }
    public NoteRepository Notes { get; private set; }
    public SettingsStore Settings { get; private set; }
    public CaptureSession Session { get; private set; }
    public BackupService Backups { get; private set; }
    public ThemeManager Theme { get; } = new();

    /// <summary>Set if the notes file had to be recovered; shown once after start-up.</summary>
    public string? RecoveryMessage { get; private set; }

    private MainWindow? _main;
    private HotkeyManager? _hotkey;
    private TrayService? _tray;
    private SettingsWindow? _settingsWindow;
    private TrashWindow? _trashWindow;
    private bool _recorderPaused;
    private bool _disposed;

    private AppHost(AppPaths paths, Database db, string? recovery)
    {
        Paths = paths;
        RecoveryMessage = recovery;
        Db = db;
        (Notes, Settings, Session, Backups) = Build(db, paths);
    }

    private static (NoteRepository, SettingsStore, CaptureSession, BackupService) Build(Database db, AppPaths paths)
    {
        var notes = new NoteRepository(db);
        return (notes, new SettingsStore(db), new CaptureSession(notes), new BackupService(db, paths.BackupDirectory));
    }

    // ---- creation ------------------------------------------------------------------------------

    /// <summary>
    /// Opens the database. A damaged file is set aside and the newest backup restored; a file written
    /// by a newer version is never touched (DatabaseTooNewException goes to the caller).
    /// </summary>
    public static AppHost Create()
    {
        var exeDir = AppContext.BaseDirectory;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        try { AppPaths.MigrateLegacyFolder(appData); } catch (IOException) { /* in use or no rights: start fresh */ }
        var paths = AppPaths.Resolve(exeDir, appData);
        Directory.CreateDirectory(paths.DataDirectory);

        string? recovery = null;
        Database? db = null;
        try
        {
            db = Database.Open(paths.DatabasePath);
            if (!string.Equals(db.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("quick_check failed");
        }
        catch (DatabaseTooNewException) { db?.Dispose(); throw; }
        catch (Exception)
        {
            db?.Dispose();
            db = null;
            SetAside(paths.DatabasePath);
            var backup = BackupService.ListIn(paths.BackupDirectory).FirstOrDefault();
            if (backup is not null)
            {
                File.Copy(backup, paths.DatabasePath, overwrite: false);
                db = Database.Open(paths.DatabasePath);
                recovery = Strings.DbRecovered(Path.GetFileName(backup));
            }
            else
            {
                db = Database.Open(paths.DatabasePath);
                recovery = Strings.DbUnrecoverable;
            }
        }
        return new AppHost(paths, db, recovery);
    }

    private static void SetAside(string dbPath)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = dbPath + suffix;
            if (!File.Exists(f)) continue;
            try { File.Move(f, Path.Combine(Path.GetDirectoryName(f)!, $"damaged-{stamp}-{Path.GetFileName(f)}")); }
            catch (IOException) { }
        }
    }

    // ---- start ---------------------------------------------------------------------------------

    public void Start()
    {
        Theme.Initialize(Settings.Theme);

        _main = new MainWindow(this);
        _main.WarmUp();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += ToggleCapture;
        var hotkeyOk = ApplyHotkeyFromSettings();

        _tray = new TrayService(ShowCapture, ShowSettings, ShowTrash, Quit);
        StartupRegistry.Apply(Settings.StartWithWindows);

        Theme.Changed += () => { if (_main is not null) WindowFocus.ApplyChrome(_main, Theme.IsDark); };

        if (RecoveryMessage is not null)
            MessageBox.Show(RecoveryMessage, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);

        Task.Run(Maintenance);

        if (!Settings.FirstRunDone)
            new FirstRunWindow(this).Show();
        else if (!hotkeyOk)
            ShowSettings();
    }

    /// <summary>Runs off the UI thread; every call here is a short SQL statement or a file copy.</summary>
    private void Maintenance()
    {
        try
        {
            Notes.SweepExpired();
            Notes.PurgeTrash(Settings.TrashRetentionDays);
            if (Settings.BackupEnabled)
                Backups.RunIfDue(DateTime.UtcNow, TimeSpan.FromHours(20), Settings.BackupKeep);
        }
        catch (Exception ex) { LogError("maintenance", ex); }
    }

    // ---- hotkey --------------------------------------------------------------------------------

    private bool ApplyHotkeyFromSettings()
    {
        var text = Settings.Hotkey;
        if (!HotkeyParser.TryParse(text, out var hk) || hk is null || !HotkeyParser.Validate(hk).IsValid)
        {
            text = SettingsStore.DefaultHotkey;
            HotkeyParser.TryParse(text, out hk);
            Settings.Hotkey = text;
        }
        return hk is not null && _hotkey!.Register(hk);
    }

    /// <summary>Validates and registers a new hotkey. Returns a user-facing error, or null on success.</summary>
    public string? TryApplyHotkey(string text)
    {
        var check = HotkeyParser.Validate(text);
        if (!check.IsValid || check.Hotkey is null)
            return check.Problem switch
            {
                HotkeyProblem.NeedsModifier => Strings.HotkeyNeedsModifier,
                HotkeyProblem.AltGrConflict => Strings.HotkeyAltGr,
                HotkeyProblem.TooCommon => Strings.HotkeyTooCommon,
                _ => Strings.HotkeyUnknown,
            };

        var previous = Settings.Hotkey;
        if (!_hotkey!.Register(check.Hotkey))
        {
            // Put the old one back so the app is never left without a hotkey by a failed attempt.
            if (HotkeyParser.TryParse(previous, out var old) && old is not null) _hotkey.Register(old);
            return Strings.HotkeyInUse;
        }
        Settings.Hotkey = check.Hotkey.ToString();
        return null;
    }

    /// <summary>While the recorder captures keys, our own hotkey must not fire (it would hide/show the window).</summary>
    public void SetRecorderPaused(bool paused)
    {
        _recorderPaused = paused;
        SyncHotkeyState();
    }

    private void SyncHotkeyState()
    {
        if (_hotkey is null) return;
        var shouldRun = !_recorderPaused;
        if (shouldRun && !_hotkey.IsRegistered && _hotkey.Current is { } hk) _hotkey.Register(hk);
        else if (!shouldRun && _hotkey.IsRegistered) _hotkey.Unregister();
    }

    // ---- windows -------------------------------------------------------------------------------

    public void ToggleCapture()
    {
        if (_main is null) return;
        if (_main.IsVisible && _main.IsActive) _main.HideCapture();
        else ShowCapture();
    }

    public void ShowCapture() => _main?.ShowCapture();

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowTrash()
    {
        if (_trashWindow is { IsVisible: true }) { _trashWindow.Activate(); return; }
        _trashWindow = new TrashWindow(this);
        _trashWindow.Closed += (_, _) => _trashWindow = null;
        _trashWindow.Show();
        _trashWindow.Activate();
    }

    // ---- shutdown ------------------------------------------------------------------------------

    /// <summary>Saves anything pending. Safe to call repeatedly and from session-ending handlers.</summary>
    public void FlushAll()
    {
        try { Session.Flush(); } catch (Exception ex) { LogError("flush", ex); }
    }

    /// <summary>The capture window normally hides instead of closing; on exit it must be allowed to close.</summary>
    public void PrepareForExit()
    {
        FlushAll();
        _main?.AllowClose();
    }

    public void Quit()
    {
        PrepareForExit();
        Application.Current.Shutdown();
    }

    public void LogError(string where, Exception ex)
    {
        try
        {
            var line = $"{DateTime.Now:O} [{where}] {ex}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(Paths.DataDirectory, "errors.log"), line);
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FlushAll();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Theme.Dispose();
        Session.Dispose();
        Db.Dispose();
    }
}
