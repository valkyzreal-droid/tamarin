using System;
using Microsoft.Win32;

namespace Tamarin.App.Services;

/// <summary>"Start with Windows" through the per-user Run key (no admin rights, nothing installed).</summary>
internal static class StartupRegistry
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tamarin";
    public const string BackgroundArg = "--background";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key is null) return;
            key.DeleteValue("Quicknote", throwOnMissingValue: false); // entry from before the rename
            var exe = Environment.ProcessPath;
            if (enabled && !string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArg}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception) { /* a locked-down PC may forbid it; the setting simply has no effect */ }
    }
}
