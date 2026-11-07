using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Tamarin.App.Native;
using Tamarin.App.Services;
using Tamarin.Core.Export;
using Tamarin.Core.Import;
using Tamarin.Core.Settings;

namespace Tamarin.App.Views;

internal partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private bool _loading = true;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = Strings.SettingsTitle;
        SourceInitialized += (_, _) => WindowFocus.ApplyChrome(this, host.Theme.IsDark);
        Closed += (_, _) => _host.SetRecorderPaused(false);

        GeneralTitle.Text = Strings.General;
        HotkeyLabel.Text = Strings.Hotkey;
        HotkeyHelp.Text = Strings.HotkeyHelp;
        ResetHotkey.Content = Strings.HotkeyReset;
        StartLabel.Text = Strings.StartWithWindows;
        ThemeLabel.Text = Strings.Theme;
        ThemeSystem.Content = Strings.ThemeSystem;
        ThemeLight.Content = Strings.ThemeLight;
        ThemeDark.Content = Strings.ThemeDark;
        SizeLabel.Text = Strings.TextSize;
        DataTitle.Text = Strings.Data;
        FolderLabel.Text = Strings.DataFolder;
        OpenFolderButton.Content = Strings.OpenFolder;
        BackupLabel.Text = Strings.Backup;
        BackupHelp.Text = Strings.BackupHelp;
        BackupNowButton.Content = Strings.BackupNow;
        RetentionLabel.Text = Strings.TrashRetention;
        Keep7.Content = Strings.Days(7);
        Keep30.Content = Strings.Days(30);
        Keep90.Content = Strings.Days(90);
        ExportButton.Content = Strings.ExportAll;
        ImportButton.Content = Strings.Import;
        PrivacyTitle.Text = Strings.Privacy;
        PrivacyText.Text = Strings.PrivacyText;
        CloseButton.Content = Strings.Close;

        Recorder.Chosen += OnHotkeyChosen;
        Recorder.RecordingChanged += on => _host.SetRecorderPaused(on);

        Load();
        _loading = false;
    }

    private void Load()
    {
        var s = _host.Settings;
        Recorder.Value = s.Hotkey;
        StartToggle.IsChecked = s.StartWithWindows;
        (s.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        SizeSlider.Value = s.TextSize;
        SizeValue.Text = $"{s.TextSize} pt";
        FolderPath.Text = _host.Paths.DataDirectory;
        BackupToggle.IsChecked = s.BackupEnabled;
        (s.TrashRetentionDays switch { <= 7 => Keep7, <= 30 => Keep30, _ => Keep90 }).IsChecked = true;
    }

    private void ShowStatus(string text) => Status.Text = text;

    // ---- hotkey --------------------------------------------------------------------------------

    private void OnHotkeyChosen(string text)
    {
        var error = _host.TryApplyHotkey(text);
        if (error is null)
        {
            Recorder.Value = _host.Settings.Hotkey;
            HotkeyError.Visibility = Visibility.Collapsed;
        }
        else
        {
            HotkeyError.Text = error;
            HotkeyError.Visibility = Visibility.Visible;
        }
    }

    private void OnResetHotkey(object sender, RoutedEventArgs e) => OnHotkeyChosen(SettingsStore.DefaultHotkey);

    // ---- toggles and choices -------------------------------------------------------------------

    private void OnStartToggle(object sender, RoutedEventArgs e)
    {
        var on = StartToggle.IsChecked == true;
        _host.Settings.StartWithWindows = on;
        StartupRegistry.Apply(on);
    }

    private void OnTheme(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string theme }) return;
        _host.Settings.Theme = theme;
        _host.Theme.Apply(theme);
        WindowFocus.ApplyChrome(this, _host.Theme.IsDark);
    }

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)Math.Round(e.NewValue);
        _host.Settings.TextSize = v;
        SizeValue.Text = $"{v} pt";
    }

    private void OnBackupToggle(object sender, RoutedEventArgs e) =>
        _host.Settings.BackupEnabled = BackupToggle.IsChecked == true;

    private void OnRetention(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string days } || !int.TryParse(days, out var n)) return;
        _host.Settings.TrashRetentionDays = n;
    }

    // ---- data ----------------------------------------------------------------------------------

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_host.Paths.DataDirectory}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _host.LogError("open-folder", ex); }
    }

    private void OnBackupNow(object sender, RoutedEventArgs e)
    {
        try
        {
            _host.Session.Flush();
            var path = _host.Backups.Run(DateTime.UtcNow);
            _host.Backups.Prune(_host.Settings.BackupKeep);
            ShowStatus(Strings.BackupDone(Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _host.LogError("backup", ex);
            ShowStatus(ex.Message);
        }
    }

    private void OnExportAll(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Strings.ExportAll };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _host.Session.Flush();
            var files = NoteExporter.ExportAll(_host.Notes.ListAll(), dlg.FolderName, ExportFormat.Markdown);
            ShowStatus(Strings.ExportDone(files.Count));
        }
        catch (Exception ex)
        {
            _host.LogError("export", ex);
            ShowStatus(ex.Message);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Strings.Import,
            Multiselect = true,
            Filter = "Markdown / text (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var result = new NoteImporter(_host.Db, _host.Notes).ImportFiles(dlg.FileNames);
            ShowStatus(Strings.ImportDone(result.Imported, result.Duplicates));
        }
        catch (Exception ex)
        {
            _host.LogError("import", ex);
            ShowStatus(ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
