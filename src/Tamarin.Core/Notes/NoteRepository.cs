using Microsoft.Data.Sqlite;
using Tamarin.Core.Db;

namespace Tamarin.Core.Notes;

/// <summary>
/// All reads and writes of notes. Order used everywhere a list is shown or walked:
/// pinned first, then most recently updated, then id (so the order is total and stable).
/// </summary>
public sealed class NoteRepository
{
    public const int MaxBodyLength = 2_000_000;

    private const string Columns = "id, body, kind, pinned, created_at, updated_at, expires_at, deleted_at";
    private const string ListOrder = "pinned DESC, updated_at DESC, id DESC";

    private readonly Database _db;
    private readonly Func<long> _nowMs;

    public NoteRepository(Database db, Func<long>? nowMs = null)
    {
        _db = db;
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    // ---- create / read -------------------------------------------------------------------------

    public Note Create(string body = "", NoteKind kind = NoteKind.Text)
    {
        var id = Guid.NewGuid().ToString("N");
        _db.Run(c => Database.Execute(c,
            "INSERT INTO notes (id, body, kind) VALUES ($id, $body, $kind);",
            ("$id", id), ("$body", body), ("$kind", Note.KindToDb(kind))));
        return Get(id) ?? throw new InvalidOperationException("The note was just inserted but cannot be read.");
    }

    public Note? Get(string id) => _db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM notes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadNote(r) : null;
    });

    /// <summary>The most recently edited note that is not in the trash.</summary>
    public Note? Latest() => QueryNotes($"SELECT {Columns} FROM notes WHERE deleted_at IS NULL ORDER BY updated_at DESC, id DESC LIMIT 1;").FirstOrDefault();

    public IReadOnlyList<Note> List(int limit = 100, int offset = 0) => QueryNotes(
        $"SELECT {Columns} FROM notes WHERE deleted_at IS NULL ORDER BY {ListOrder} LIMIT $limit OFFSET $offset;",
        ("$limit", (long)limit), ("$offset", (long)offset));

    public IReadOnlyList<Note> ListAll() => QueryNotes(
        $"SELECT {Columns} FROM notes WHERE deleted_at IS NULL ORDER BY created_at ASC, id ASC;");

    public int Count() => Convert.ToInt32(_db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM notes WHERE deleted_at IS NULL;";
        return cmd.ExecuteScalar();
    }));

    // ---- update --------------------------------------------------------------------------------

    /// <summary>Writes the body. The database trigger refreshes updated_at. Returns false if the note no longer exists.</summary>
    public bool Save(string id, string body)
    {
        if (body.Length > MaxBodyLength)
            throw new ArgumentException($"A note can hold at most {MaxBodyLength:N0} characters.", nameof(body));
        var changed = _db.Run(c => Database.Execute(c,
            "UPDATE notes SET body = $body WHERE id = $id;", ("$body", body), ("$id", id)));
        return changed > 0;
    }

    public void SetPinned(string id, bool pinned) => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET pinned = $p WHERE id = $id;", ("$p", pinned ? 1L : 0L), ("$id", id)));

    public void SetKind(string id, NoteKind kind) => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET kind = $k WHERE id = $id;", ("$k", Note.KindToDb(kind)), ("$id", id)));

    public void SetExpiry(string id, long? expiresAtMs) => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET expires_at = $e WHERE id = $id;", ("$e", expiresAtMs), ("$id", id)));

    // ---- trash ---------------------------------------------------------------------------------

    public void Trash(string id) => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET deleted_at = $now WHERE id = $id AND deleted_at IS NULL;", ("$now", _nowMs()), ("$id", id)));

    public void Restore(string id) => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET deleted_at = NULL WHERE id = $id;", ("$id", id)));

    public IReadOnlyList<Note> ListTrash() => QueryNotes(
        $"SELECT {Columns} FROM notes WHERE deleted_at IS NOT NULL ORDER BY deleted_at DESC, id DESC;");

    /// <summary>A real DELETE. The FTS triggers remove the text from the search index too.</summary>
    public void DeleteForever(string id) => _db.Run(c => Database.Execute(c,
        "DELETE FROM notes WHERE id = $id;", ("$id", id)));

    public int EmptyTrash() => _db.Run(c => Database.Execute(c,
        "DELETE FROM notes WHERE deleted_at IS NOT NULL;"));

    /// <summary>Notes whose expiry has passed go to the trash (not straight to deletion).</summary>
    public int SweepExpired() => _db.Run(c => Database.Execute(c,
        "UPDATE notes SET deleted_at = $now WHERE deleted_at IS NULL AND expires_at IS NOT NULL AND expires_at <= $now;",
        ("$now", _nowMs())));

    /// <summary>Deletes notes that have been in the trash longer than the retention period.</summary>
    public int PurgeTrash(int retentionDays)
    {
        var cutoff = _nowMs() - TimeSpan.FromDays(Math.Max(1, retentionDays)).Ticks / TimeSpan.TicksPerMillisecond;
        return _db.Run(c => Database.Execute(c,
            "DELETE FROM notes WHERE deleted_at IS NOT NULL AND deleted_at <= $cutoff;", ("$cutoff", cutoff)));
    }

    // ---- walking the list ----------------------------------------------------------------------

    /// <summary>The next note in list order (older), or null at the end.</summary>
    public Note? Older(Note current) => QueryNotes(
        $"SELECT {Columns} FROM notes WHERE deleted_at IS NULL AND (pinned, updated_at, id) < ($p, $u, $id) ORDER BY {ListOrder} LIMIT 1;",
        ("$p", current.Pinned ? 1L : 0L), ("$u", current.UpdatedAt), ("$id", current.Id)).FirstOrDefault();

    /// <summary>The previous note in list order (newer), or null at the start.</summary>
    public Note? Newer(Note current) => QueryNotes(
        $"SELECT {Columns} FROM notes WHERE deleted_at IS NULL AND (pinned, updated_at, id) > ($p, $u, $id) ORDER BY pinned ASC, updated_at ASC, id ASC LIMIT 1;",
        ("$p", current.Pinned ? 1L : 0L), ("$u", current.UpdatedAt), ("$id", current.Id)).FirstOrDefault();

    /// <summary>1-based position of the note in list order and the total, for "Note 3 of 12".</summary>
    public (int Index, int Total) Position(Note current) => _db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT (SELECT COUNT(*) FROM notes WHERE deleted_at IS NULL AND (pinned, updated_at, id) > ($p, $u, $id)) + 1, " +
            "       (SELECT COUNT(*) FROM notes WHERE deleted_at IS NULL);";
        cmd.Parameters.AddWithValue("$p", current.Pinned ? 1L : 0L);
        cmd.Parameters.AddWithValue("$u", current.UpdatedAt);
        cmd.Parameters.AddWithValue("$id", current.Id);
        using var r = cmd.ExecuteReader();
        r.Read();
        return ((int)r.GetInt64(0), (int)r.GetInt64(1));
    });

    // ---- search --------------------------------------------------------------------------------

    /// <summary>
    /// Full-text search over notes that are not in the trash. Accents are ignored ("acao" finds "ação")
    /// and every word is a prefix ("reun" finds "reunião"). An empty query returns the recent notes.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string? query, int limit = 50)
    {
        var match = FtsQuery.BuildMatch(query);
        if (match is null)
            return List(limit).Select(n => new SearchHit(n, Preview(n.Body))).ToList();

        return _db.Run(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                $"SELECT n.id, n.body, n.kind, n.pinned, n.created_at, n.updated_at, n.expires_at, n.deleted_at, " +
                "       snippet(notes_fts, 0, $open, $close, '…', 14) " +
                "FROM notes_fts JOIN notes n ON n.rowid = notes_fts.rowid " +
                "WHERE notes_fts MATCH $match AND n.deleted_at IS NULL " +
                "ORDER BY rank, n.updated_at DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$open", SnippetParser.Open.ToString());
            cmd.Parameters.AddWithValue("$close", SnippetParser.Close.ToString());
            cmd.Parameters.AddWithValue("$match", match);
            cmd.Parameters.AddWithValue("$limit", (long)limit);
            var hits = new List<SearchHit>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) hits.Add(new SearchHit(ReadNote(r), r.GetString(8)));
            return (IReadOnlyList<SearchHit>)hits;
        });
    }

    private static string Preview(string body)
    {
        var flat = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 100 ? flat : flat[..99] + "…";
    }

    // ---- plumbing ------------------------------------------------------------------------------

    private IReadOnlyList<Note> QueryNotes(string sql, params (string Name, object? Value)[] args) => _db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var notes = new List<Note>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) notes.Add(ReadNote(r));
        return (IReadOnlyList<Note>)notes;
    });

    private static Note ReadNote(SqliteDataReader r) => new(
        Id: r.GetString(0),
        Body: r.GetString(1),
        Kind: Note.KindFromDb(r.GetString(2)),
        Pinned: r.GetInt64(3) != 0,
        CreatedAt: r.GetInt64(4),
        UpdatedAt: r.GetInt64(5),
        ExpiresAt: r.IsDBNull(6) ? null : r.GetInt64(6),
        DeletedAt: r.IsDBNull(7) ? null : r.GetInt64(7));
}
