using Tamarin.Core.Db;
using Tamarin.Core.Hotkeys;
using Tamarin.Core.Export;
using Tamarin.Core.Notes;
using Xunit;

namespace Tamarin.Core.Tests;

/// <summary>Edge cases from replica/test-plan.md that can be run without Windows. Ids match the plan.</summary>
public class EdgeCaseTests
{
    [Fact] // F01-E2
    public void Emoji_and_accents_round_trip_and_are_searchable()
    {
        using var t = new TestDb();
        var n = t.Notes.Create("😀 Reunião de ação — 日本語\nsegunda linha");
        t.Reopen();
        Assert.Equal("😀 Reunião de ação — 日本語\nsegunda linha", t.Notes.Get(n.Id)!.Body);
        Assert.Single(t.Notes.Search("reuniao"));
        Assert.Single(t.Notes.Search("acao"));
    }

    [Fact] // F01-E3
    public void Body_over_the_cap_throws_and_the_old_text_stays()
    {
        using var t = new TestDb();
        var n = t.Notes.Create("keep me");
        Assert.Throws<ArgumentException>(() => t.Notes.Save(n.Id, new string('a', NoteRepository.MaxBodyLength + 1)));
        Assert.Equal("keep me", t.Notes.Get(n.Id)!.Body);
        Assert.True(t.Notes.Save(n.Id, new string('a', 1_900_000)));
    }

    [Fact] // F01-E5 (two copies of the app, or a backup running, on the same file)
    public void Two_connections_to_one_file_can_both_write()
    {
        using var t = new TestDb();
        using var second = Database.Open(t.DbPath);
        var other = new NoteRepository(second);
        for (var i = 0; i < 50; i++)
        {
            t.Notes.Create($"from first {i}");
            other.Create($"from second {i}");
        }
        Assert.Equal(100, t.Notes.Count());
        Assert.Equal(100, other.Count());
    }

    [Theory] // F02-E4
    [InlineData("\"")] [InlineData("*")] [InlineData("AND")] [InlineData("OR")] [InlineData("NOT")]
    [InlineData("(")] [InlineData("-")] [InlineData("NEAR(")] [InlineData("a:b")] [InlineData("'")]
    [InlineData("😀")] [InlineData("%")] [InlineData("\\")]
    public void Special_characters_never_throw_in_search(string query)
    {
        using var t = new TestDb();
        t.Notes.Create("hello world AND more");
        var ex = Record.Exception(() => t.Notes.Search(query));
        Assert.Null(ex);
    }

    [Fact] // F02-E3
    public void Search_over_three_thousand_notes_is_fast()
    {
        using var t = new TestDb();
        t.Db.InTransaction(c =>
        {
            for (var i = 0; i < 3000; i++)
                Database.Execute(c, $"INSERT INTO notes(id, body) VALUES ('n{i}', 'nota numero {i} sobre reunião e compras');");
            return 0;
        });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = t.Notes.Search("reun compr", 50);
        sw.Stop();
        Assert.Equal(50, hits.Count);
        Assert.True(sw.ElapsedMilliseconds < 500, $"search took {sw.ElapsedMilliseconds} ms");
    }

    [Fact] // F04-H1 + E
    public void Trashed_notes_leave_search_and_come_back_on_restore()
    {
        using var t = new TestDb();
        var n = t.Notes.Create("segredo importante");
        t.Notes.Trash(n.Id);
        Assert.Empty(t.Notes.Search("segredo"));
        t.Notes.Restore(n.Id);
        Assert.Single(t.Notes.Search("segredo"));
    }

    [Fact] // F04-E1
    public void Restoring_a_note_with_the_same_text_as_another_keeps_both()
    {
        using var t = new TestDb();
        var a = t.Notes.Create("igual");
        t.Notes.Trash(a.Id);
        var b = t.Notes.Create("igual");
        t.Notes.Restore(a.Id);
        Assert.Equal(2, t.Notes.Count());
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact] // F06-E3
    public void Export_of_notes_with_the_same_title_does_not_overwrite()
    {
        using var t = new TestDb();
        var a = t.Notes.Create("Mesmo título\ncorpo A");
        var b = t.Notes.Create("Mesmo título\ncorpo B");
        var folder = Path.Combine(t.Dir, "out");
        var files = NoteExporter.ExportAll(new[] { t.Notes.Get(a.Id)!, t.Notes.Get(b.Id)! }, folder, ExportFormat.Markdown);
        Assert.Equal(2, files.Distinct().Count());
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact] // F06-E (title that Windows forbids)
    public void Export_file_names_are_valid_on_windows()
    {
        using var t = new TestDb();
        var n = t.Notes.Create("a/b:c*d?e\"f<g>h|i\nbody");
        var name = NoteExporter.FileName(t.Notes.Get(n.Id)!);
        Assert.DoesNotContain(name, c => "\\/:*?\"<>|".Contains(c));
        Assert.False(name.EndsWith('.') || name.EndsWith(' '));
    }
}

public class LegacyFolderTests
{
    [Fact]
    public void Old_quicknote_folder_is_moved_once_and_keeps_its_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "tamarin-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Quicknote"));
        File.WriteAllText(Path.Combine(root, "Quicknote", "notes.db"), "x");
        try
        {
            Assert.True(AppPaths.MigrateLegacyFolder(root));
            Assert.True(File.Exists(Path.Combine(root, "Tamarin", "notes.db")));
            Assert.False(Directory.Exists(Path.Combine(root, "Quicknote")));
            Assert.False(AppPaths.MigrateLegacyFolder(root)); // second run does nothing
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Existing_new_folder_is_never_overwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "tamarin-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Quicknote"));
        Directory.CreateDirectory(Path.Combine(root, "Tamarin"));
        try
        {
            Assert.False(AppPaths.MigrateLegacyFolder(root));
            Assert.True(Directory.Exists(Path.Combine(root, "Quicknote")));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class CommonHotkeyTests
{
    [Theory]
    [InlineData("Ctrl+X")] [InlineData("Ctrl+C")] [InlineData("Ctrl+V")] [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+A")] [InlineData("Ctrl+S")] [InlineData("Ctrl+1")] [InlineData("Alt+F")]
    [InlineData("Ctrl+Enter")] [InlineData("Alt+Tab")]
    public void Ctrl_or_Alt_alone_with_a_typing_key_is_refused(string text) =>
        Assert.Equal(HotkeyProblem.TooCommon, HotkeyParser.Validate(text).Problem);

    [Theory]
    [InlineData("Ctrl+Shift+Space")] [InlineData("Ctrl+Space")] [InlineData("Ctrl+F9")]
    [InlineData("Win+Shift+N")] [InlineData("Ctrl+Shift+X")] [InlineData("Alt+Shift+N")] [InlineData("Win+N")]
    public void Reasonable_global_hotkeys_are_accepted(string text) =>
        Assert.True(HotkeyParser.Validate(text).IsValid);
}
