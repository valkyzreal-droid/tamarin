using System;
using System.Linq;
using System.Windows;
using Tamarin.App.Services;
using Tamarin.Core.Db;

namespace Tamarin.App;

public partial class App : Application
{
    private SingleInstance? _single;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UseEmbeddedFont();

        _single = new SingleInstance();
        if (!_single.IsFirst)
        {
            // Already running: wake the first copy so a double-click on the exe still "does something".
            _single.SignalFirst();
            Shutdown();
            return;
        }

        try
        {
            _host = AppHost.Create();
        }
        catch (DatabaseTooNewException)
        {
            MessageBox.Show(Strings.DbTooNew, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Strings.StartupFailed}\n\n{ex.Message}", Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            // The note is the one thing that must survive: save, log, keep running.
            _host?.FlushAll();
            _host?.LogError("ui", args.Exception);
            args.Handled = true;
        };

        SessionEnding += (_, _) => _host?.PrepareForExit();
        _single.ShowRequested += () => Dispatcher.BeginInvoke(new Action(() => _host?.ShowCapture()));

        _host.Start();
        // "--background" (used by Start with Windows) only means: do not pop the capture window. We never
        // do that at start-up anyway, so there is nothing to branch on; the flag is accepted and ignored.
        _ = e.Args.Contains(StartupRegistry.BackgroundArg);
    }

    /// <summary>
    /// Overrides Font.Sans/Font.Display with the bundled Inter. If loading fails for any reason the
    /// Segoe UI fallbacks from Typography.xaml stay in effect.
    /// </summary>
    private void UseEmbeddedFont()
    {
        try
        {
            var inter = new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/Assets/Fonts/"),
                "./#Inter, Segoe UI Variable Text, Segoe UI");
            Resources["Font.Sans"] = inter;
            Resources["Font.Display"] = inter;
        }
        catch (Exception) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
