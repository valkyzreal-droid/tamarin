using System.Windows;
using System.Windows.Controls;

namespace Tamarin.App.Controls;

/// <summary>Draws a shortcut such as "Ctrl+Shift+Space" as a row of keyboard keys.</summary>
internal sealed class KeyCaps : StackPanel
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(string), typeof(KeyCaps),
        new PropertyMetadata(string.Empty, (d, _) => ((KeyCaps)d).Rebuild()));

    public string Keys
    {
        get => (string)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    public KeyCaps() => Orientation = Orientation.Horizontal;

    private void Rebuild()
    {
        Children.Clear();
        if (string.IsNullOrWhiteSpace(Keys)) return;
        foreach (var key in Keys.Split('+', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            var text = new TextBlock { Text = key };
            text.SetResourceReference(StyleProperty, "KeyCapText");
            var cap = new Border { Child = text };
            cap.SetResourceReference(StyleProperty, "KeyCap");
            Children.Add(cap);
        }
    }
}
