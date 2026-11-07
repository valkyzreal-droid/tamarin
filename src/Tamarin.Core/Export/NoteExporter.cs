using System.Text;
using Tamarin.Core.Notes;

namespace Tamarin.Core.Export;

public enum ExportFormat { Markdown, Text }

public static class NoteExporter
{
    // Windows rules, applied on every platform so a folder exported anywhere opens on Windows.
    private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Extension(ExportFormat format) => format == ExportFormat.Markdown ? ".md" : ".txt";

    /// <summary>The note as file content. Markdown can carry created/updated dates as front matter (Obsidian reads it).</summary>
    public static string Render(Note note, ExportFormat format, bool frontMatter = false)
    {
        var body = note.Body.Replace("\r\n", "\n");
        if (format == ExportFormat.Markdown && frontMatter)
        {
            var created = DateTimeOffset.FromUnixTimeMilliseconds(note.CreatedAt).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            var updated = DateTimeOffset.FromUnixTimeMilliseconds(note.UpdatedAt).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            return $"---\ncreated: {created}\nupdated: {updated}\n---\n\n{body}";
        }
        return body;
    }

    /// <summary>A safe Windows file name (without extension) from the note's first line.</summary>
    public static string FileName(Note note)
    {
        var name = MakeSafe(note.Title, 60);
        if (name.Length == 0)
        {
            var created = DateTimeOffset.FromUnixTimeMilliseconds(note.CreatedAt).ToLocalTime();
            name = "note-" + created.ToString("yyyyMMdd-HHmm");
        }
        return name;
    }

    internal static string MakeSafe(string text, int maxLength)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch < ' ' || Array.IndexOf(Invalid, ch) >= 0) sb.Append(' ');
            else sb.Append(ch);
        }
        var s = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.Length > maxLength) s = s[..maxLength].TrimEnd();
        s = s.TrimEnd('.', ' ');                       // Windows drops trailing dots and spaces
        var stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        if (Reserved.Contains(stem)) s = "_" + s;      // "CON.txt" is still the console device
        return s;
    }

    /// <summary>Writes one file. Returns the path actually used (a number is added if the name is taken).</summary>
    public static string ExportOne(Note note, string folder, ExportFormat format, bool frontMatter = false)
    {
        Directory.CreateDirectory(folder);
        var path = UniquePath(folder, FileName(note), Extension(format));
        File.WriteAllText(path, Render(note, format, frontMatter), new UTF8Encoding(false));
        return path;
    }

    public static IReadOnlyList<string> ExportAll(IEnumerable<Note> notes, string folder, ExportFormat format, bool frontMatter = false)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        foreach (var note in notes)
        {
            if (note.IsEmpty) continue;
            written.Add(ExportOne(note, folder, format, frontMatter));
        }
        return written;
    }

    /// <summary>Writes to an exact path chosen by the user (a Save As dialog).</summary>
    public static void ExportTo(Note note, string path, ExportFormat format, bool frontMatter = false) =>
        File.WriteAllText(path, Render(note, format, frontMatter), new UTF8Encoding(false));

    private static string UniquePath(string folder, string stem, string extension)
    {
        var path = Path.Combine(folder, stem + extension);
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }
}
