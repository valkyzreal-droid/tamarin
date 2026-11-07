using System.Text.RegularExpressions;

namespace Tamarin.Core.Text;

/// <summary>
/// Pure text helpers behind the editor shortcuts: checklists, list continuation, Markdown wrapping,
/// links and word counts. Kept free of any UI type so they can be tested without Windows.
/// Lists use plain Markdown ("- [ ] item", "- item") so notes stay readable anywhere.
/// </summary>
public static class LineFormatter
{
    private static readonly Regex ListLine = new(@"^(?<indent>[ \t]*)(?<marker>- \[(?<state>[ xX])\] |- |\* |\d+\. )(?<rest>.*)$", RegexOptions.Compiled);

    public enum LineKind { Plain, Checkbox, Bullet, Numbered }

    public static LineKind Classify(string line)
    {
        var m = ListLine.Match(line);
        if (!m.Success) return LineKind.Plain;
        var marker = m.Groups["marker"].Value;
        if (marker.StartsWith("- [", StringComparison.Ordinal)) return LineKind.Checkbox;
        if (marker.Length > 0 && char.IsDigit(marker[0])) return LineKind.Numbered;
        return LineKind.Bullet;
    }

    /// <summary>Cycles one line: plain text, then checkbox, then bullet, then back to plain text.</summary>
    public static string CycleLine(string line)
    {
        var m = ListLine.Match(line);
        if (!m.Success)
        {
            var indent = LeadingWhitespace(line);
            return indent + "- [ ] " + line[indent.Length..];
        }
        var ind = m.Groups["indent"].Value;
        var rest = m.Groups["rest"].Value;
        return Classify(line) switch
        {
            LineKind.Checkbox => ind + "- " + rest,
            LineKind.Bullet or LineKind.Numbered => ind + rest,
            _ => line,
        };
    }

    /// <summary>Flips "- [ ]" and "- [x]". Returns false (and the same line) when the line is not a checkbox.</summary>
    public static bool TryToggleCheckbox(string line, out string result)
    {
        result = line;
        var m = ListLine.Match(line);
        if (!m.Success || Classify(line) != LineKind.Checkbox) return false;
        var done = m.Groups["state"].Value != " ";
        result = m.Groups["indent"].Value + (done ? "- [ ] " : "- [x] ") + m.Groups["rest"].Value;
        return true;
    }

    public static string Indent(string line) => IsList(line) ? "  " + line : line;

    public static string Outdent(string line)
    {
        if (!IsList(line)) return line;
        if (line.StartsWith("  ", StringComparison.Ordinal)) return line[2..];
        if (line.StartsWith('\t') || line.StartsWith(' ')) return line[1..];
        return line;
    }

    public static bool IsList(string line) => Classify(line) != LineKind.Plain;

    /// <summary>
    /// What the new line should start with after Enter on <paramref name="line"/>, or null when the line
    /// is not a list item. A numbered item continues with the next number; a checkbox continues unchecked.
    /// </summary>
    public static string? NextLinePrefix(string line)
    {
        var m = ListLine.Match(line);
        if (!m.Success) return null;
        var ind = m.Groups["indent"].Value;
        var marker = m.Groups["marker"].Value;
        if (marker.StartsWith("- [", StringComparison.Ordinal)) return ind + "- [ ] ";
        if (char.IsDigit(marker[0]))
        {
            var number = int.Parse(marker[..marker.IndexOf('.')]);
            return ind + (number + 1) + ". ";
        }
        return ind + marker;
    }

    /// <summary>True for a list line with nothing after the marker. Enter on it should end the list.</summary>
    public static bool IsEmptyListItem(string line)
    {
        var m = ListLine.Match(line);
        return m.Success && string.IsNullOrWhiteSpace(m.Groups["rest"].Value);
    }

    /// <summary>
    /// How many characters the indentation and list marker take at the start of the line (0 for plain text).
    /// Enter only continues the list when the caret is after the marker.
    /// </summary>
    public static int MarkerLength(string line)
    {
        var m = ListLine.Match(line);
        return m.Success ? m.Groups["indent"].Length + m.Groups["marker"].Length : 0;
    }

    private static string LeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }

    // ---- Markdown wrapping ---------------------------------------------------------------------

    /// <summary>
    /// Wraps the selection in a marker (for example ** for bold). If the selection is already wrapped,
    /// the marker is removed instead. With no selection it inserts the pair and puts the caret between.
    /// </summary>
    public static (string Text, int SelStart, int SelLength) ToggleWrap(string text, int selStart, int selLength, string marker)
    {
        selStart = Math.Clamp(selStart, 0, text.Length);
        selLength = Math.Clamp(selLength, 0, text.Length - selStart);
        var m = marker.Length;
        var selected = text.Substring(selStart, selLength);

        // Markers just outside the selection: "**|word|**" selected as "word".
        var outsideBefore = selStart >= m && text.Substring(selStart - m, m) == marker;
        var outsideAfter = selStart + selLength + m <= text.Length && text.Substring(selStart + selLength, m) == marker;
        if (selLength > 0 && outsideBefore && outsideAfter)
        {
            var result = text.Remove(selStart + selLength, m).Remove(selStart - m, m);
            return (result, selStart - m, selLength);
        }

        // Markers inside the selection: selected as "**word**".
        if (selLength >= 2 * m && selected.StartsWith(marker, StringComparison.Ordinal) && selected.EndsWith(marker, StringComparison.Ordinal))
        {
            var inner = selected[m..^m];
            return (text[..selStart] + inner + text[(selStart + selLength)..], selStart, inner.Length);
        }

        var wrapped = marker + selected + marker;
        var newText = text[..selStart] + wrapped + text[(selStart + selLength)..];
        return selLength == 0
            ? (newText, selStart + m, 0)
            : (newText, selStart + m, selLength);
    }

    // ---- links and counts ----------------------------------------------------------------------

    private static readonly Regex Url = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The http(s) link under the caret (or touching it), or null.</summary>
    public static string? FindUrlAt(string text, int index)
    {
        foreach (Match m in Url.Matches(text))
        {
            if (index >= m.Index && index <= m.Index + m.Length)
                return m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '!', '?');
        }
        return null;
    }

    public static int WordCount(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) inWord = false;
            else if (!inWord) { inWord = true; count++; }
        }
        return count;
    }
}
