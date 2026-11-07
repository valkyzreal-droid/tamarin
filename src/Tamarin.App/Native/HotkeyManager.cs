using System;
using System.Windows.Interop;
using Tamarin.Core.Hotkeys;

namespace Tamarin.App.Native;

/// <summary>
/// Owns the one global hotkey. Messages arrive on a message-only window, so no visible window is needed
/// and the hotkey works while the capture window is hidden.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int Id = 0x51_4E; // "QN"
    private readonly HwndSource _source;
    private Hotkey? _current;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("TamarinHotkeySink")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool IsRegistered => _registered;
    public Hotkey? Current => _current;

    /// <summary>Registers the hotkey, replacing any previous one. False if Windows refused (already taken).</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _current = hotkey;
        _registered = NativeMethods.RegisterHotKey(_source.Handle, Id,
            (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, Id);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
