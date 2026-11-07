namespace Tamarin.Core.Notes;

public enum NoteKind { Text, Checklist, Code, Math }

public sealed record Note(
    string Id,
    string Body,
    NoteKind Kind,
    bool Pinned,
    long CreatedAt,
    long UpdatedAt,
    long? ExpiresAt,
    long? DeletedAt)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Body);

    /// <summary>First non-empty line, trimmed and shortened. Used for the window title and list rows.</summary>
    public string Title => MakeTitle(Body);

    public static string MakeTitle(string body, int max = 80)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            return line.Length <= max ? line : line[..(max - 1)] + "…";
        }
        return "";
    }

    internal static string KindToDb(NoteKind kind) => kind switch
    {
        NoteKind.Checklist => "checklist",
        NoteKind.Code => "code",
        NoteKind.Math => "math",
        _ => "text",
    };

    internal static NoteKind KindFromDb(string? value) => value switch
    {
        "checklist" => NoteKind.Checklist,
        "code" => NoteKind.Code,
        "math" => NoteKind.Math,
        _ => NoteKind.Text,
    };
}

/// <summary>One search result: the note plus a snippet with match markers (see <see cref="SnippetParser"/>).</summary>
public sealed record SearchHit(Note Note, string Snippet);

public readonly record struct SnippetPart(string Text, bool IsMatch);

public static class SnippetParser
{
    /// <summary>Marker characters wrapped around matches by the SQL snippet() call. Control characters never appear in normal notes.</summary>
    public const char Open = '\u0002';
    public const char Close = '\u0003';

    public static IReadOnlyList<SnippetPart> Parse(string snippet)
    {
        var parts = new List<SnippetPart>();
        var sb = new System.Text.StringBuilder();
        var inMatch = false;
        foreach (var ch in snippet)
        {
            if (ch == Open || ch == Close)
            {
                if (sb.Length > 0) parts.Add(new SnippetPart(sb.ToString(), inMatch));
                sb.Clear();
                inMatch = ch == Open;
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(new SnippetPart(sb.ToString(), inMatch));
        return parts;
    }

    /// <summary>The snippet as plain text, markers removed. Newlines become spaces so it fits one row.</summary>
    public static string ToPlain(string snippet) =>
        snippet.Replace(Open.ToString(), "").Replace(Close.ToString(), "").Replace('\n', ' ').Replace('\r', ' ');
}
