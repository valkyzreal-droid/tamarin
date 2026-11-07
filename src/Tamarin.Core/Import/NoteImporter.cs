using System.Security.Cryptography;
using System.Text;
using Tamarin.Core.Db;
using Tamarin.Core.Notes;

namespace Tamarin.Core.Import;

public sealed record ImportResult(int Imported, int Duplicates, int Empty, int TooLarge, int Failed);

/// <summary>Reads .txt and .md files into notes. Importing the same file twice does not create a second note.</summary>
public sealed class NoteImporter
{
    private readonly Database _db;
    private readonly NoteRepository _notes;

    public NoteImporter(Database db, NoteRepository notes)
    {
        _db = db;
        _notes = notes;
    }

    public ImportResult ImportFiles(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(_notes.ListAll().Select(n => Hash(n.Body)));
        int imported = 0, dupes = 0, empty = 0, large = 0, failed = 0;

        foreach (var path in paths)
        {
            string body;
            try
            {
                var info = new FileInfo(path);
                // Four bytes per character is the worst case in UTF-8; skip obviously huge files unread.
                if (info.Length > NoteRepository.MaxBodyLength * 4L) { large++; continue; }
                body = Normalize(File.ReadAllText(path)); // ReadAllText also strips a UTF-8 BOM
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(body)) { empty++; continue; }
            if (body.Length > NoteRepository.MaxBodyLength) { large++; continue; }
            if (!known.Add(Hash(body))) { dupes++; continue; }

            _notes.Create(body);
            imported++;
        }
        return new ImportResult(imported, dupes, empty, large, failed);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string Hash(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(body))));
}
