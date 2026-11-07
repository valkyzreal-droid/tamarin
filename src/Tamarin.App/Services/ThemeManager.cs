using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tamarin.App.Services;

/// <summary>
/// Swaps the colour dictionary (always MergedDictionaries[0]) between light, dark and a high-contrast
/// set built from the Windows system colours. Everything else uses DynamicResource, so it updates live.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private static readonly Uri Light = new("Themes/Colors.Light.xaml", UriKind.Relative);
    private static readonly Uri Dark = new("Themes/Colors.Dark.xaml", UriKind.Relative);

    private string _setting = "system";

    public bool IsDark { get; private set; }
    public event Action? Changed;

    public void Initialize(string setting)
    {
        SystemEvents.UserPreferenceChanged += OnSystemChanged;
        Apply(setting);
    }

    public void Apply(string setting)
    {
        _setting = setting is "light" or "dark" ? setting : "system";
        var dicts = Application.Current.Resources.MergedDictionaries;

        ResourceDictionary next;
        if (SystemParameters.HighContrast)
        {
            next = BuildHighContrast();
            IsDark = false;
        }
        else
        {
            IsDark = _setting == "dark" || (_setting == "system" && SystemPrefersDark());
            next = new ResourceDictionary { Source = IsDark ? Dark : Light };
        }
        dicts[0] = next;
        Changed?.Invoke();
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    private static ResourceDictionary BuildHighContrast()
    {
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        var d = new ResourceDictionary();
        d["Brush.Bg"] = B(SystemColors.WindowColor);
        d["Brush.Surface"] = B(SystemColors.WindowColor);
        d["Brush.SurfaceHover"] = B(SystemColors.ControlLightColor);
        d["Brush.Border"] = B(SystemColors.WindowTextColor);
        d["Brush.BorderInput"] = B(SystemColors.WindowTextColor);
        d["Brush.Text"] = B(SystemColors.WindowTextColor);
        d["Brush.TextMuted"] = B(SystemColors.GrayTextColor);
        d["Brush.Accent"] = B(SystemColors.HotTrackColor);
        d["Brush.OnAccent"] = B(SystemColors.WindowColor);
        d["Brush.AccentSubtle"] = B(SystemColors.ControlLightColor);
        d["Brush.Danger"] = B(SystemColors.WindowTextColor);
        d["Brush.Success"] = B(SystemColors.WindowTextColor);
        d["Brush.Warning"] = B(SystemColors.WindowTextColor);
        return d;
    }

    private void OnSystemChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(_setting)));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnSystemChanged;
}
