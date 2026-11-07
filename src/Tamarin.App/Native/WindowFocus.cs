using System;
using System.Windows;
using System.Windows.Interop;

namespace Tamarin.App.Native;

internal static class WindowFocus
{
    /// <summary>
    /// Windows refuses SetForegroundWindow from a background process. Attaching to the input queue of
    /// the current foreground window for a moment is the standard way around it, and is what a hotkey
    /// launcher is entitled to do: the user just pressed our key.
    /// </summary>
    public static void ForceForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var fg = NativeMethods.GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(fg, out _);
        var me = NativeMethods.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && NativeMethods.AttachThreadInput(me, fgThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
            window.Activate();
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(me, fgThread, false);
        }
    }

    /// <summary>Dark title/borders and rounded corners where Windows supports them (Windows 11); harmless elsewhere.</summary>
    public static void ApplyChrome(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var d = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
        var r = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref r, sizeof(int));
    }
}
