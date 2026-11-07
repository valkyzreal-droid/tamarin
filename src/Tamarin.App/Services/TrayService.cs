using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tamarin.App.Services;

/// <summary>Notification-area icon. WinForms NotifyIcon: ships with .NET, no extra dependency.</summary>
internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayService(Action open, Action settings, Action trash, Action quit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.TrayOpen, null, (_, _) => open());
        menu.Items.Add(Strings.MenuTrash, null, (_, _) => trash());
        menu.Items.Add(Strings.MenuSettings, null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuQuit, null, (_, _) => quit());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Strings.TrayTooltip,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null && Icon.ExtractAssociatedIcon(exe) is { } ico) return ico;
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
