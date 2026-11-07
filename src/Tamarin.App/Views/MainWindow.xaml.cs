using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using Tamarin.App.Controls;
using Tamarin.App.Native;
using Tamarin.App.Services;
using Tamarin.Core.Capture;
using Tamarin.Core.Notes;
using Tamarin.Core.Text;

namespace Tamarin.App.Views;

internal partial class MainWindow : Window
{
    private sealed record ResultRow(string Id, string Snippet, string When);

    private readonly AppHost _host;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _loadingText;
    private bool _allowClose;
    private bool _pinned;
    private Action? _toastAction;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        Title = Strings.AppName;
        Hint.Text = Strings.EditorHint;
        RetryButton.Content = Strings.Retry;
        SearchBox.SetValue(AutomationProperties.NameProperty, Strings.Search);
        NoResults.Text = Strings.NoResults;
        SetTip(OlderButton, Strings.Older);
        SetTip(NewerButton, Strings.Newer);
        SetTip(NewButton, $"{Strings.NewNote} (Ctrl+N)");
        SetTip(SearchButton, $"{Strings.Search} (Ctrl+F)");
        SetTip(PinToggle, Strings.Pin);
        SetTip(MenuButton, Strings.Menu);
        BuildMenu();
        BuildHintTips();

        SourceInitialized += (_, _) => WindowFocus.ApplyChrome(this, host.Theme.IsDark);
        _toastTimer.Tick += (_, _) => HideToast();

        host.Session.CurrentChanged += () => Dispatcher.BeginInvoke(new Action(OnCurrentChanged));
        host.Session.StateChanged += s => Dispatcher.BeginInvoke(new Action(() => OnSaveState(s)));
    }

    private void BuildHintTips()
    {
        void Row(string keys, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new KeyCaps { Keys = keys, VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(StyleProperty, "MutedText");
            row.Children.Add(t);
            HintTips.Children.Add(row);
        }
        Row("Esc", Strings.TipHide);
        Row("Ctrl+F", Strings.TipSearch);
        Row("Ctrl+N", Strings.TipNew);
    }

    private static void SetTip(FrameworkElement e, string text)
    {
        e.ToolTip = text;
        AutomationProperties.SetName(e, text);
    }

    private void BuildMenu()
    {
        MainMenu.Items.Clear();
        void Add(string header, Action act, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            mi.Click += (_, _) => act();
            MainMenu.Items.Add(mi);
        }
        Add(Strings.MenuCopy, CopyNote, "Ctrl+Shift+C");
        Add(Strings.MenuExportThis, ExportCurrent);
        Add(Strings.MenuDelete, DeleteCurrent, "Ctrl+D");
        MainMenu.Items.Add(new Separator());
        Add(Strings.MenuTrash, () => { HideCapture(); _host.ShowTrash(); });
        Add(Strings.MenuSettings, () => { HideCapture(); _host.ShowSettings(); }, "Ctrl+,");
        MainMenu.Items.Add(new Separator());
        Add(Strings.MenuQuit, _host.Quit);
    }

    // ---- show / hide ---------------------------------------------------------------------------

    /// <summary>
    /// Creates the native window once, off-screen, so the first real hotkey press only has to show it
    /// (no layout or font loading delay).
    /// </summary>
    public void WarmUp()
    {
        Left = -32000; Top = -32000;
        ShowActivated = false;
        Show();
        Hide();
        ShowActivated = true;
    }

    public void AllowClose() => _allowClose = true;

    public void ShowCapture()
    {
        var note = _host.Session.Open();
        _pinned = _host.Settings.WindowPinned;
        PinToggle.IsChecked = _pinned;
        Editor.FontSize = _host.Settings.TextSize;
        Editor.SetValue(System.Windows.Controls.TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        Editor.SetValue(System.Windows.Controls.TextBlock.LineHeightProperty, Math.Round(Editor.FontSize * 1.55));
        LoadEditor(note.Body);
        CloseSearch();
        HideToast();
        UpdateCounter();
        OnSaveState(_host.Session.State);

        WindowPlacement.Place(this, _host.Settings, ActualOrDefault(Width), ActualOrDefault(Height));
        Show();
        PlayEntrance();
        // Topmost toggle: makes Windows raise the window above the app the user was in.
        Topmost = true;
        Topmost = _pinned;
        WindowFocus.ForceForeground(this);
        Editor.Focus();
        Editor.CaretIndex = Editor.Text.Length;
        Editor.ScrollToEnd();
    }

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void Fade(UIElement e, double from, double to, int ms, int delayMs = 0)
    {
        if (!SystemParameters.ClientAreaAnimation) { e.BeginAnimation(OpacityProperty, null); e.Opacity = to; return; }
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = EaseOut });
    }

    private static void Rise(System.Windows.Media.TranslateTransform t, double fromY, int ms)
    {
        if (!SystemParameters.ClientAreaAnimation) { t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); t.Y = 0; return; }
        t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut });
    }

    /// <summary>The one orchestrated moment: the content rises 8 px and fades in. Skipped if Windows animations are off.</summary>
    private void PlayEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Fade(Body, 0, 1, 140);
        Rise(BodyShift, 8, 190);
    }

    private static double ActualOrDefault(double v) => double.IsNaN(v) || v <= 0 ? 440 : v;

    public void HideCapture()
    {
        CloseSearch();
        _host.Session.Flush();
        if (IsVisible) WindowPlacement.Remember(this, _host.Settings);
        Hide();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        HideCapture();
    }

    // ---- editor <-> session --------------------------------------------------------------------

    private void LoadEditor(string body)
    {
        _loadingText = true;
        try
        {
            // Switching notes must also drop the undo history: otherwise Ctrl+Z would paste the previous
            // note's text into this one. Turning undo off and on again clears it.
            Editor.IsUndoEnabled = false;
            Editor.Text = body;
            Editor.IsUndoEnabled = true;
            Editor.CaretIndex = Editor.Text.Length;
            Editor.ScrollToEnd();
        }
        finally { _loadingText = false; }
        UpdateHintAndCount();
    }

    private string EditorBody => Editor.Text.Replace("\r\n", "\n");

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingText) return;
        _host.Session.OnTextChanged(EditorBody);
        UpdateHintAndCount();
    }

    private void UpdateHintAndCount()
    {
        HintPanel.Visibility = Editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        WordCountText.Text = Strings.Words(LineFormatter.WordCount(Editor.Text));
    }

    private void OnCurrentChanged()
    {
        var body = _host.Session.Current?.Body ?? "";
        LoadEditor(body);
        if (IsVisible) Fade(Editor, 0.35, 1, 160);
        UpdateCounter();
        if (IsVisible && SearchOverlay.Visibility != Visibility.Visible) Editor.Focus();
    }

    private void UpdateCounter()
    {
        var (i, n) = _host.Session.Position();
        Counter.Text = Strings.Position(i, n);
        OlderButton.IsEnabled = i < n;
        NewerButton.IsEnabled = i > 1;

        var cur = _host.Session.Current;
        var fresh = cur is null ? null : _host.Notes.Get(cur.Id);
        AgeText.Text = fresh is null || string.IsNullOrWhiteSpace(fresh.Body)
            ? string.Empty
            : Strings.EditedAt(RelativeTime.Format(fresh.UpdatedAt));
    }

    private void OnSaveState(SaveState state)
    {
        switch (state)
        {
            case SaveState.Saved:
                SetSaveLine("Brush.Accent", 0, 700, 250);
                StatusText.Text = Strings.Saved;
                ErrorStrip.Visibility = Visibility.Collapsed;
                UpdateCounter();
                break;
            case SaveState.Saving:
                SetSaveLine("Brush.Accent", 0.9, 90, 0);
                StatusText.Text = Strings.Saving;
                break;
            case SaveState.Failed:
                SetSaveLine("Brush.Danger", 1, 0, 0);
                StatusText.Text = Strings.SaveFailed;
                ErrorText.Text = Strings.SaveFailed;
                ErrorStrip.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SetSaveLine(string brushKey, double opacity, int ms, int delayMs)
    {
        SaveLine.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        SaveLine.BeginAnimation(OpacityProperty, null);
        if (ms == 0 || !SystemParameters.ClientAreaAnimation)
        {
            SaveLine.Opacity = opacity;
            return;
        }
        SaveLine.BeginAnimation(OpacityProperty,
            new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
    }

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        // Hands the drag to Windows as if the bottom-right edge was grabbed.
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_SYSCOMMAND, (IntPtr)(NativeMethods.SC_SIZE + NativeMethods.WMSZ_BOTTOMRIGHT), IntPtr.Zero);
        e.Handled = true;
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        _host.Session.Flush();
        Editor.Focus();
    }

    // ---- top strip -----------------------------------------------------------------------------

    private void OnOlder(object sender, RoutedEventArgs e) => _host.Session.GoOlder();
    private void OnNewer(object sender, RoutedEventArgs e) => _host.Session.GoNewer();
    private void OnNew(object sender, RoutedEventArgs e) => NewNote();
    private void OnSearchClick(object sender, RoutedEventArgs e) => OpenSearch();

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _pinned = PinToggle.IsChecked == true;
        _host.Settings.WindowPinned = _pinned;
        Topmost = _pinned;
        Editor.Focus();
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        MainMenu.PlacementTarget = MenuButton;
        MainMenu.IsOpen = true;
    }

    private void NewNote()
    {
        _host.Session.NewNote();
        Editor.Focus();
    }

    // ---- actions -------------------------------------------------------------------------------

    private void CopyNote()
    {
        try
        {
            Clipboard.SetText(Editor.Text);
            ShowToast(Strings.Copied);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another program holds the clipboard for a moment. Nothing was lost; try again.
        }
    }

    private void DeleteCurrent()
    {
        var id = _host.Session.Current?.Id;
        var wasEmpty = string.IsNullOrWhiteSpace(Editor.Text);
        _host.Session.TrashCurrent();
        if (id is null || wasEmpty) return; // blank notes skip the trash, so there is nothing to undo
        ShowToast(Strings.MovedToTrash, Strings.Undo, () =>
        {
            _host.Notes.Restore(id);
            _host.Session.OpenNote(id);
        });
    }

    private void ExportCurrent()
    {
        var note = _host.Session.Current;
        if (note is null) return;
        _host.Session.Flush();
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Strings.MenuExportThis };
        // The picker is a normal window; the capture window must not hide behind it.
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var fresh = _host.Notes.Get(note.Id) ?? note;
            Tamarin.Core.Export.NoteExporter.ExportOne(fresh, dlg.FolderName, Tamarin.Core.Export.ExportFormat.Markdown);
        }
        catch (Exception ex) { _host.LogError("export-one", ex); }
    }

    // ---- toast ---------------------------------------------------------------------------------

    private void ShowToast(string text, string? actionText = null, Action? action = null)
    {
        ToastText.Text = text;
        _toastAction = action;
        ToastAction.Visibility = actionText is null ? Visibility.Collapsed : Visibility.Visible;
        ToastAction.Content = actionText;
        Toast.Visibility = Visibility.Visible;
        Fade(Toast, 0, 1, 160);
        Rise(ToastShift, 10, 200);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
        _toastAction = null;
    }

    private void OnToastAction(object sender, RoutedEventArgs e)
    {
        var act = _toastAction;
        HideToast();
        act?.Invoke();
    }

    // ---- search --------------------------------------------------------------------------------

    private void OpenSearch()
    {
        SearchOverlay.Visibility = Visibility.Visible;
        Fade(SearchOverlay, 0, 1, 130);
        SearchBox.Text = string.Empty;
        RunSearch();
        SearchBox.Focus();
    }

    private void CloseSearch()
    {
        if (SearchOverlay.Visibility != Visibility.Visible) return;
        SearchOverlay.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        IReadOnlyList<SearchHit> hits;
        try { hits = _host.Notes.Search(SearchBox.Text, 50); }
        catch (Exception ex) { _host.LogError("search", ex); hits = Array.Empty<SearchHit>(); }

        Results.ItemsSource = hits
            .Select(h => new ResultRow(h.Note.Id, h.Snippet, RelativeTime.Format(h.Note.UpdatedAt)))
            .ToList();
        NoResults.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hits.Count > 0) Results.SelectedIndex = 0;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.Enter: OpenSelectedResult(); e.Handled = true; break;
            case Key.Escape: CloseSearch(); e.Handled = true; break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (Results.Items.Count == 0) return;
        var i = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
        Results.SelectedIndex = i;
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedResult();

    private void OpenSelectedResult()
    {
        if (Results.SelectedItem is not ResultRow row) return;
        if (_host.Session.OpenNote(row.Id)) CloseSearch();
    }

    // ---- keyboard ------------------------------------------------------------------------------

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && !ctrl && !alt)
        {
            if (SearchOverlay.Visibility == Visibility.Visible) CloseSearch();
            else if (Toast.Visibility == Visibility.Visible) HideToast();
            else HideCapture();
            e.Handled = true;
            return;
        }
        if (alt && !ctrl && key == Key.Left) { _host.Session.GoOlder(); e.Handled = true; return; }
        if (alt && !ctrl && key == Key.Right) { _host.Session.GoNewer(); e.Handled = true; return; }
        if (!ctrl || alt) return;

        switch (key)
        {
            case Key.N when !shift: NewNote(); break;
            case Key.F when !shift: OpenSearch(); break;
            case Key.D when !shift: DeleteCurrent(); break;
            case Key.C when shift: CopyNote(); break;
            case Key.P when !shift: PinToggle.IsChecked = PinToggle.IsChecked != true; OnPinClick(PinToggle, e); break;
            case Key.OemComma: HideCapture(); _host.ShowSettings(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (alt) return;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.B when !shift: Wrap("**"); e.Handled = true; return;
                case Key.I when !shift: Wrap("*"); e.Handled = true; return;
                case Key.T when !shift: ToggleCheckbox(); e.Handled = true; return;
                case Key.L when shift: CycleLine(); e.Handled = true; return;
                case Key.Enter when !shift: OpenLinkAtCaret(); e.Handled = true; return;
            }
            return;
        }

        if (e.Key == Key.Enter && !shift) { if (ContinueList()) e.Handled = true; return; }
        if (e.Key == Key.Tab) { if (IndentLine(outdent: shift)) e.Handled = true; }
    }

    // ---- editor helpers ------------------------------------------------------------------------

    /// <summary>The logical line (between line breaks, not wrapped rows) holding <paramref name="index"/>.</summary>
    private (int Start, int End, string Text) LineAt(int index)
    {
        var t = Editor.Text;
        index = Math.Clamp(index, 0, t.Length);
        var start = index == 0 ? 0 : t.LastIndexOf('\n', index - 1) + 1;
        var end = t.IndexOf('\n', index);
        if (end < 0) end = t.Length;
        var lineEnd = end > start && t[end - 1] == '\r' ? end - 1 : end;
        return (start, lineEnd, t[start..lineEnd]);
    }

    /// <summary>Replaces a range through the TextBox (not by assigning Text) so Ctrl+Z keeps working.</summary>
    private void Replace(int start, int length, string replacement, int caretAfter)
    {
        Editor.Select(start, length);
        Editor.SelectedText = replacement;
        Editor.Select(Math.Clamp(caretAfter, 0, Editor.Text.Length), 0);
    }

    /// <summary>Applies a whole new text with the smallest edit (common prefix/suffix), keeping undo useful.</summary>
    private void ApplyTextEdit(string newText, int selStart, int selLength)
    {
        var old = Editor.Text;
        var p = 0;
        while (p < old.Length && p < newText.Length && old[p] == newText[p]) p++;
        var s = 0;
        while (s < old.Length - p && s < newText.Length - p && old[old.Length - 1 - s] == newText[newText.Length - 1 - s]) s++;
        Editor.Select(p, old.Length - p - s);
        Editor.SelectedText = newText.Substring(p, newText.Length - p - s);
        Editor.Select(Math.Clamp(selStart, 0, Editor.Text.Length), Math.Max(0, selLength));
    }

    private void Wrap(string marker)
    {
        var r = LineFormatter.ToggleWrap(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, marker);
        ApplyTextEdit(r.Text, r.SelStart, r.SelLength);
    }

    private void ToggleCheckbox()
    {
        var (start, end, line) = LineAt(Editor.CaretIndex);
        if (!LineFormatter.TryToggleCheckbox(line, out var result)) return;
        var caret = Math.Min(Editor.CaretIndex, start + result.Length);
        Replace(start, end - start, result, caret);
    }

    private void CycleLine()
    {
        var (start, end, line) = LineAt(Editor.CaretIndex);
        var result = LineFormatter.CycleLine(line);
        Replace(start, end - start, result, start + result.Length);
    }

    /// <summary>Enter on a list line: continue the list; on an empty list item: end the list.</summary>
    private bool ContinueList()
    {
        if (Editor.SelectionLength > 0) return false;
        var caret = Editor.CaretIndex;
        var (start, end, line) = LineAt(caret);
        if (!LineFormatter.IsList(line)) return false;

        if (LineFormatter.IsEmptyListItem(line))
        {
            Replace(start, end - start, string.Empty, start);
            return true;
        }
        // Splitting inside the marker would make a mess; let the default Enter handle it.
        if (caret - start < LineFormatter.MarkerLength(line)) return false;
        var prefix = LineFormatter.NextLinePrefix(line);
        if (prefix is null) return false;
        var insert = Environment.NewLine + prefix;
        Replace(caret, 0, insert, caret + insert.Length);
        return true;
    }

    private bool IndentLine(bool outdent)
    {
        var (start, end, line) = LineAt(Editor.CaretIndex);
        if (!LineFormatter.IsList(line)) return false;
        var result = outdent ? LineFormatter.Outdent(line) : LineFormatter.Indent(line);
        if (result == line) return true;
        var caret = Math.Max(start, Editor.CaretIndex + (result.Length - line.Length));
        Replace(start, end - start, result, caret);
        return true;
    }

    private void OpenLinkAtCaret()
    {
        var url = LineFormatter.FindUrlAt(Editor.Text, Editor.CaretIndex);
        if (url is null) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _host.LogError("open-link", ex); }
    }
}
