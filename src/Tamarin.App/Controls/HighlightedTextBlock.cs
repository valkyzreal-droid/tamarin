using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Tamarin.Core.Notes;

namespace Tamarin.App.Controls;

/// <summary>Shows a search snippet with the matched words in bold + accent colour.</summary>
internal sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty SnippetProperty = DependencyProperty.Register(
        nameof(Snippet), typeof(string), typeof(HighlightedTextBlock),
        new PropertyMetadata(string.Empty, (d, _) => ((HighlightedTextBlock)d).Rebuild()));

    public string Snippet
    {
        get => (string)GetValue(SnippetProperty);
        set => SetValue(SnippetProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        foreach (var part in SnippetParser.Parse(Snippet ?? string.Empty))
        {
            var run = new Run(part.Text.Replace('\n', ' ').Replace('\r', ' '));
            if (part.IsMatch)
            {
                run.FontWeight = FontWeights.Bold;
                run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Accent");
            }
            Inlines.Add(run);
        }
    }
}
