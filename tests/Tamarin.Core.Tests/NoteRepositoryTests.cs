using Tamarin.Core.Notes;
using Xunit;

namespace Tamarin.Core.Tests;

public class NoteRepositoryTests : IDisposable
{
    private readonly TestDb _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Create_and_Get_round_trip_with_accents_and_emoji()
    {
        var body = "Reunião às 15h: café ☕ e pão de queijo";
        var note = _t.Notes.Create(body);
        var loaded = _t.Notes.Get(note.Id)!;
        Assert.Equal(body, loaded.Body);
        Assert.Equal(NoteKind.Text, loaded.Kind);
        Assert.False(loaded.Pinned);
        Assert.Null(loaded.DeletedAt);
    }

    [Fact]
    public void Save_changes_the_body_and_moves_the_note_to_the_front()
    {
        var a = _t.Notes.Create("a");
        var b = _t.Notes.Create("b");
        _t.SetUpdated(a, 1000);
        _t.SetUpdated(b, 2000);
        Assert.Equal(b.Id, _t.Notes.Latest()!.Id);

        Assert.True(_t.Notes.Save(a.Id, "a edited"));

        var after = _t.Notes.Get(a.Id)!;
        Assert.Equal("a edited", after.Body);
        Assert.True(after.UpdatedAt > 2000);
        Assert.Equal(a.Id, _t.Notes.Latest()!.Id);
    }

    [Fact]
    public void Save_returns_false_when_the_note_is_gone()
    {
        var a = _t.Notes.Create("a");
        _t.Notes.DeleteForever(a.Id);
        Assert.False(_t.Notes.Save(a.Id, "late text"));
    }

    [Fact]
    public void Trash_hides_a_note_from_the_list_and_search_and_Restore_brings_it_back()
    {
        var a = _t.Notes.Create("lista de compras de sábado");
        _t.Notes.Create("outra nota");
        Assert.Equal(2, _t.Notes.Count());
        Assert.Single(_t.Notes.Search("compras"));

        _t.Notes.Trash(a.Id);

        Assert.Equal(1, _t.Notes.Count());
        Assert.Empty(_t.Notes.Search("compras"));
        Assert.Single(_t.Notes.ListTrash());
        Assert.NotNull(_t.Notes.Get(a.Id)!.DeletedAt);

        _t.Notes.Restore(a.Id);

        Assert.Equal(2, _t.Notes.Count());
        Assert.Single(_t.Notes.Search("compras"));
        Assert.Empty(_t.Notes.ListTrash());
    }

    [Fact]
    public void DeleteForever_removes_the_text_from_the_search_index_too()
    {
        var a = _t.Notes.Create("segredo bancário");
        Assert.Single(_t.Notes.Search("segredo"));

        _t.Notes.DeleteForever(a.Id);

        Assert.Null(_t.Notes.Get(a.Id));
        Assert.Empty(_t.Notes.Search("segredo"));
        Assert.Equal("ok", _t.Db.QuickCheck());
        // The index itself is consistent with the table (FTS5 integrity check raises if not).
        _t.Db.Exec("INSERT INTO notes_fts(notes_fts) VALUES('integrity-check');");
    }

    [Fact]
    public void EmptyTrash_deletes_only_trashed_notes()
    {
        var keep = _t.Notes.Create("keep");
        var gone1 = _t.Notes.Create("gone 1");
        var gone2 = _t.Notes.Create("gone 2");
        _t.Notes.Trash(gone1.Id);
        _t.Notes.Trash(gone2.Id);

        Assert.Equal(2, _t.Notes.EmptyTrash());

        Assert.Empty(_t.Notes.ListTrash());
        Assert.NotNull(_t.Notes.Get(keep.Id));
    }

    [Fact]
    public void PurgeTrash_removes_notes_trashed_longer_than_the_retention_period()
    {
        var old = _t.Notes.Create("old");
        var recent = _t.Notes.Create("recent");
        _t.Notes.Trash(old.Id);
        _t.Now += TimeSpan.FromDays(20).Ticks / TimeSpan.TicksPerMillisecond;
        _t.Notes.Trash(recent.Id);
        _t.Now += TimeSpan.FromDays(15).Ticks / TimeSpan.TicksPerMillisecond; // old: 35 days, recent: 15 days

        Assert.Equal(1, _t.Notes.PurgeTrash(30));

        Assert.Null(_t.Notes.Get(old.Id));
        Assert.NotNull(_t.Notes.Get(recent.Id));
    }

    [Fact]
    public void SweepExpired_sends_expired_notes_to_the_trash_not_to_oblivion()
    {
        var note = _t.Notes.Create("lembrete");
        var stays = _t.Notes.Create("fica");
        _t.Notes.SetExpiry(note.Id, _t.Now + 1000);

        Assert.Equal(0, _t.Notes.SweepExpired());          // not expired yet
        _t.Now += 2000;
        Assert.Equal(1, _t.Notes.SweepExpired());

        Assert.NotNull(_t.Notes.Get(note.Id)!.DeletedAt);   // recoverable
        Assert.Null(_t.Notes.Get(stays.Id)!.DeletedAt);
    }

    [Fact]
    public void Pinned_notes_come_first()
    {
        var a = _t.Notes.Create("a");
        var b = _t.Notes.Create("b");
        var c = _t.Notes.Create("c");
        _t.SetUpdated(a, 1000);
        _t.SetUpdated(b, 2000);
        _t.SetUpdated(c, 3000);

        _t.Notes.SetPinned(a.Id, true);

        var order = _t.Notes.List().Select(n => n.Id).ToList();
        Assert.Equal(a.Id, order[0]);
        Assert.True(_t.Notes.Get(a.Id)!.Pinned);
    }

    [Fact]
    public void Older_Newer_and_Position_walk_the_list_in_order()
    {
        var a = _t.Notes.Create("a");
        var b = _t.Notes.Create("b");
        var c = _t.Notes.Create("c");
        _t.SetUpdated(a, 3000);   // newest
        _t.SetUpdated(b, 2000);
        _t.SetUpdated(c, 1000);   // oldest
        a = _t.Notes.Get(a.Id)!; b = _t.Notes.Get(b.Id)!; c = _t.Notes.Get(c.Id)!;

        Assert.Equal(b.Id, _t.Notes.Older(a)!.Id);
        Assert.Equal(c.Id, _t.Notes.Older(b)!.Id);
        Assert.Null(_t.Notes.Older(c));
        Assert.Equal(b.Id, _t.Notes.Newer(c)!.Id);
        Assert.Equal(a.Id, _t.Notes.Newer(b)!.Id);
        Assert.Null(_t.Notes.Newer(a));

        Assert.Equal((1, 3), _t.Notes.Position(a));
        Assert.Equal((2, 3), _t.Notes.Position(b));
        Assert.Equal((3, 3), _t.Notes.Position(c));
    }

    [Fact]
    public void Notes_with_the_same_timestamp_still_have_a_stable_total_order()
    {
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var n = _t.Notes.Create("n" + i);
            _t.SetUpdated(n, 5000); // identical on purpose
            ids.Add(n.Id);
        }

        // Walking Older from the first note visits every note exactly once.
        var current = _t.Notes.List()[0];
        var seen = new List<string> { current.Id };
        while (_t.Notes.Older(current) is { } next) { seen.Add(next.Id); current = next; }

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(seen, _t.Notes.List().Select(n => n.Id).ToList());
    }

    [Fact]
    public void Note_title_is_the_first_non_blank_line_and_is_shortened()
    {
        Assert.Equal("Primeira linha", Note.MakeTitle("\n\n  Primeira linha  \nsegunda"));
        Assert.Equal("", Note.MakeTitle("  \n \n"));
        var longTitle = Note.MakeTitle(new string('a', 200), 80);
        Assert.Equal(80, longTitle.Length);
        Assert.EndsWith("…", longTitle);
        Assert.Equal("xadrez", Note.MakeTitle("xadrez")); // a leading x is not a checkbox
    }

    [Fact]
    public void A_note_edited_through_pinning_keeps_its_text()
    {
        var a = _t.Notes.Create("texto importante");
        _t.Notes.SetPinned(a.Id, true);
        _t.Notes.SetPinned(a.Id, false);
        Assert.Equal("texto importante", _t.Notes.Get(a.Id)!.Body);
        Assert.Single(_t.Notes.Search("importante"));
    }
}
