using System;
using System.Windows;
using Tamarin.App.Native;
using Tamarin.App.Services;

namespace Tamarin.App.Views;

internal partial class FirstRunWindow : Window
{
    private readonly AppHost _host;
    private bool _finished;

    public FirstRunWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = Strings.WelcomeTitle;
        Heading.Text = Strings.WelcomeTitle;
        var hk = host.Settings.Hotkey;
        Body.Text = Strings.WelcomeBody(hk);
        KeyText.Keys = hk;
        TrayText.Text = Strings.WelcomeTray;
        ChangeButton.Content = Strings.WelcomeChange;
        StartButton.Content = Strings.WelcomeStart;
        SourceInitialized += (_, _) => WindowFocus.ApplyChrome(this, host.Theme.IsDark);
        Closed += (_, _) => Finish();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _host.Settings.FirstRunDone = true;
    }

    private void OnStart(object sender, RoutedEventArgs e) { Finish(); Close(); }

    private void OnChange(object sender, RoutedEventArgs e)
    {
        Finish();
        Close();
        _host.ShowSettings();
    }
}
