using System.Windows;

namespace Tamarin.App.Views;

internal partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string confirmText)
    {
        InitializeComponent();
        Title = Strings.AppName;
        Message.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = Strings.Cancel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    public static bool Ask(Window? owner, string message, string confirmText)
    {
        var d = new ConfirmDialog(message, confirmText);
        if (owner is { IsVisible: true }) d.Owner = owner;
        return d.ShowDialog() == true;
    }
}
