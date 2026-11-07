using System.Text;

namespace Tamarin.Core.Notes;

/// <summary>
/// Turns what the user typed into a safe FTS5 MATCH expression. FTS5 has its own query language
/// (AND, OR, NEAR, quotes, column filters, parentheses), so raw input like <c>c++ "note</c> or <c>a:b</c>
/// would throw a syntax error. Every word becomes a quoted prefix term, so typing "reun" finds "reunião".
/// </summary>
public static class FtsQuery
{
    /// <summary>Returns null when there is nothing searchable in the input.</summary>
    public static string? BuildMatch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var terms = new List<string>();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length == 0) return;
            // Inside a quoted FTS5 string only a double quote needs escaping (by doubling it).
            terms.Add("\"" + word.ToString().Replace("\"", "\"\"") + "\"*");
            word.Clear();
        }

        foreach (var ch in input)
        {
            // Keep letters and digits (any script). Everything else separates words, which also
            // matches how the unicode61 tokenizer splits the stored text.
            if (char.IsLetterOrDigit(ch)) word.Append(ch);
            else Flush();
        }
        Flush();

        return terms.Count == 0 ? null : string.Join(" ", terms);
    }
}
