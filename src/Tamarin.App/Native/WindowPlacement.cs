using System;
using System.Windows;
using System.Windows.Interop;
using Tamarin.Core.Settings;

namespace Tamarin.App.Native;

/// <summary>Puts the capture window on the monitor the mouse is on, remembering a position per monitor.</summary>
internal static class WindowPlacement
{
    public static string MonitorId(System.Windows.Forms.Screen screen) => screen.DeviceName;

    public static System.Windows.Forms.Screen ScreenUnderCursor() =>
        System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);

    /// <summary>Scale factor (1.0 = 96 dpi) of the monitor the screen refers to.</summary>
    public static double ScaleOf(System.Windows.Forms.Screen screen)
    {
        var pt = new System.Drawing.Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        var hMon = MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMon != IntPtr.Zero && NativeMethods.GetDpiForMonitor(hMon, NativeMethods.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0)
            return dx / 96.0;
        return 1.0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);

    /// <summary>
    /// Moves/sizes the window in physical pixels on the cursor's monitor. The saved rectangle is used
    /// only if it still lies on that monitor (the user may have unplugged or rearranged screens).
    /// Sizes saved and computed here are physical pixels, so they survive DPI changes between sessions
    /// only on the same monitor, which is the case they are keyed by.
    /// </summary>
    public static void Place(Window window, SettingsStore settings, double defaultWidthDip, double defaultHeightDip)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var screen = ScreenUnderCursor();
        var wa = screen.WorkingArea;
        var scale = ScaleOf(screen);

        var w = (int)Math.Round(defaultWidthDip * scale);
        var h = (int)Math.Round(defaultHeightDip * scale);
        int x = wa.Left + (wa.Width - w) / 2;
        int y = wa.Top + (int)((wa.Height - h) * 0.32);

        var saved = settings.GetWindowRect(MonitorId(screen));
        if (saved is { } r && r.Width >= 200 && r.Height >= 120
            && r.Intersects(new WindowRect(wa.Left, wa.Top, wa.Width, wa.Height)))
        {
            w = r.Width; h = r.Height; x = r.X; y = r.Y;
        }

        // Keep the whole window inside the work area.
        w = Math.Min(w, wa.Width);
        h = Math.Min(h, wa.Height);
        x = Math.Clamp(x, wa.Left, wa.Right - w);
        y = Math.Clamp(y, wa.Top, wa.Bottom - h);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Remembers the current rectangle for the monitor the window is on.</summary>
    public static void Remember(Window window, SettingsStore settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var r)) return;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        settings.SetWindowRect(MonitorId(screen), new WindowRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
    }
}
