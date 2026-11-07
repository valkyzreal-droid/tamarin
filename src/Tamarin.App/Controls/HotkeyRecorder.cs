using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tamarin.Core.Hotkeys;

namespace Tamarin.App.Controls;

/// <summary>
/// A button that records a key combination. While it has focus it is "recording": the owner is told
/// (so the global hotkey can be released) and the next valid combination is raised as <see cref="Chosen"/>.
/// </summary>
internal sealed class HotkeyRecorder : Button
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(HotkeyRecorder),
        new PropertyMetadata(string.Empty, (d, _) => ((HotkeyRecorder)d).Refresh()));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Raised with the combination text, e.g. "Ctrl+Shift+Space". The owner validates and may reject it.</summary>
    public event Action<string>? Chosen;

    /// <summary>True when recording starts, false when it ends.</summary>
    public event Action<bool>? RecordingChanged;

    private bool _recording;

    public HotkeyRecorder()
    {
        Focusable = true;
        MinWidth = 200;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh() => Content = _recording ? Strings.HotkeyRecording : (string.IsNullOrEmpty(Value) ? Strings.HotkeyRecord : Value);

    protected override void OnClick()
    {
        base.OnClick();
        SetRecording(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SetRecording(false);
    }

    private void SetRecording(bool on)
    {
        if (_recording == on) return;
        _recording = on;
        Refresh();
        RecordingChanged?.Invoke(on);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { SetRecording(false); return; }
        // Pure modifier presses: keep waiting for the real key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        var mods = HotkeyModifiers.None;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (HotkeyParser.TryFromVirtualKey(mods, vk, out var hk) && hk is not null)
        {
            SetRecording(false);
            Chosen?.Invoke(hk.ToString());
        }
        else
        {
            // Unknown key: raise the raw text so the owner shows the matching error.
            SetRecording(false);
            Chosen?.Invoke(key.ToString());
        }
    }
}
