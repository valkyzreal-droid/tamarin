using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Tamarin.App.Native;
using Tamarin.App.Services;
using Tamarin.Core.Notes;

namespace Tamarin.App.Views;

internal partial class TrashWindow : Window
{
    private sealed record Item(Note Note, string Title, string When);

    private readonly AppHost _host;
    private readonly ObservableCollection<Item> _items = new();

    public TrashWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = Strings.TrashTitle;
        RestoreButton.Content = Strings.Restore;
        DeleteButton.Content = Strings.DeleteForever;
        EmptyButton.Content = Strings.EmptyTrash;
        EmptyText.Text = Strings.TrashEmpty;
        List.ItemsSource = _items;
        SourceInitialized += (_, _) => WindowFocus.ApplyChrome(this, host.Theme.IsDark);
        Reload();
    }

    private void Reload()
    {
        _items.Clear();
        foreach (var n in _host.Notes.ListTrash())
        {
            var title = string.IsNullOrWhiteSpace(n.Body) ? Strings.EmptyNote : n.Title;
            _items.Add(new Item(n, title, RelativeTime.Format(n.DeletedAt ?? n.UpdatedAt)));
        }
        var empty = _items.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyButton.IsEnabled = !empty;
        if (!empty) List.SelectedIndex = 0;
        OnSelectionChanged(this, null!);
    }

    private Note? Selected => (List.SelectedItem as Item)?.Note;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        RestoreButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } n) return;
        _host.Notes.Restore(n.Id);
        Reload();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } n) return;
        if (!ConfirmDialog.Ask(this, Strings.ConfirmDeleteForever, Strings.Delete)) return;
        _host.Notes.DeleteForever(n.Id);
        Reload();
    }

    private void OnEmpty(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Ask(this, Strings.ConfirmEmptyTrash, Strings.EmptyTrash)) return;
        _host.Notes.EmptyTrash();
        Reload();
    }
}
