using Tamarin.Core.Backup;
using Tamarin.Core.Db;
using Tamarin.Core.Export;
using Tamarin.Core.Import;
using Tamarin.Core.Notes;
using Xunit;

namespace Tamarin.Core.Tests;

public class NoteExporterTests : IDisposable
{
    private readonly TestDb _t = new();
    private string Out => Path.Combine(_t.Dir, "out");
    public void Dispose() => _t.Dispose();

    [Fact]
    public void File_names_come_from_the_first_line_and_are_safe_on_Windows()
    {
        Note N(string body) => _t.Notes.Create(body);
        Assert.Equal("Lista de compras", NoteExporter.FileName(N("Lista de compras\n- pão")));
        Assert.Equal("a b c d e f", NoteExporter.FileName(N("a<b>c:d\"e/f")));
        Assert.Equal("relatorio final", NoteExporter.FileName(N("relatorio  \t final...")));
        Assert.Equal("_CON", NoteExporter.FileName(N("CON")));
        Assert.Equal("_nul.txt", NoteExporter.FileName(N("nul.txt")));
        Assert.Equal("_com1", NoteExporter.FileName(N("com1")));               // reserved in any case; the original case is kept
        Assert.Equal("CONSOLE", NoteExporter.FileName(N("CONSOLE")));          // only the exact device names are reserved
        Assert.Equal(60, NoteExporter.FileName(N(new string('x', 300))).Length);
    }

    [Fact]
    public void A_blank_title_falls_back_to_a_dated_name()
    {
        var name = NoteExporter.FileName(_t.Notes.Create("   \n  "));
        Assert.StartsWith("note-", name);
        Assert.Matches(@"^note-\d{8}-\d{4}$", name);
    }

    [Fact]
    public void Markdown_and_text_export_the_body_unchanged_with_utf8_and_no_bom()
    {
        var note = _t.Notes.Create("Título ação\n\n- [ ] item ☕");

        var md = NoteExporter.ExportOne(note, Out, ExportFormat.Markdown);
        var txt = NoteExporter.ExportOne(note, Out, ExportFormat.Text);

        Assert.EndsWith(".md", md);
        Assert.EndsWith(".txt", txt);
        Assert.Equal("Título ação\n\n- [ ] item ☕", File.ReadAllText(md));
        var bytes = File.ReadAllBytes(md);
        Assert.False(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);   // no BOM
    }

    [Fact]
    public void Markdown_can_carry_dates_as_front_matter_for_Obsidian()
    {
        var note = _t.Notes.Create("Corpo");
        var text = NoteExporter.Render(note, ExportFormat.Markdown, frontMatter: true);
        Assert.StartsWith("---\ncreated: ", text);
        Assert.Contains("\nupdated: ", text);
        Assert.EndsWith("---\n\nCorpo", text);
        Assert.Equal("Corpo", NoteExporter.Render(note, ExportFormat.Text, frontMatter: true));   // text never gets it
    }

    [Fact]
    public void Two_notes_with_the_same_title_get_numbered_names_and_nothing_is_overwritten()
    {
        var a = _t.Notes.Create("Reunião\nprimeira");
        var b = _t.Notes.Create("Reunião\nsegunda");

        var pa = NoteExporter.ExportOne(a, Out, ExportFormat.Markdown);
        var pb = NoteExporter.ExportOne(b, Out, ExportFormat.Markdown);

        Assert.NotEqual(pa, pb);
        Assert.EndsWith("Reunião (2).md", pb);
        Assert.Contains("primeira", File.ReadAllText(pa));
        Assert.Contains("segunda", File.ReadAllText(pb));
    }

    [Fact]
    public void Export_all_writes_one_file_per_note_and_skips_blank_ones()
    {
        _t.Notes.Create("um");
        _t.Notes.Create("dois");
        _t.Notes.Create("   ");

        var files = NoteExporter.ExportAll(_t.Notes.ListAll(), Out, ExportFormat.Markdown);

        Assert.Equal(2, files.Count);
        Assert.Equal(2, Directory.GetFiles(Out).Length);
    }

    [Fact]
    public void Windows_line_endings_are_normalised()
    {
        var note = _t.Notes.Create("a\r\nb");
        Assert.Equal("a\nb", NoteExporter.Render(note, ExportFormat.Text));
    }
}

public class BackupServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private BackupService Backups => new(_t.Db, Path.Combine(_t.Dir, "backups"));
    public void Dispose() => _t.Dispose();

    [Fact]
    public void A_backup_is_a_complete_readable_copy_of_the_database()
    {
        _t.Notes.Create("dentro do backup");
        _t.Settings.TextSize = 22;

        var path = Backups.Run(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc));

        Assert.True(File.Exists(path));
        Assert.EndsWith("notes-20261103-100000.db", path);
        using var copy = Database.Open(path);
        var notes = new NoteRepository(copy);
        Assert.Equal("dentro do backup", notes.List().Single().Body);
        Assert.Equal("ok", copy.QuickCheck());
        Assert.Single(notes.Search("backup"));                               // the search index came along
        Assert.Equal(22, new Settings.SettingsStore(copy).TextSize);        // and the settings
    }

    [Fact]
    public void RunIfDue_skips_when_the_newest_backup_is_recent_and_runs_when_it_is_old()
    {
        _t.Notes.Create("x");
        var day1 = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

        Assert.NotNull(Backups.RunIfDue(day1, TimeSpan.FromHours(24), keep: 7));
        Assert.Null(Backups.RunIfDue(day1.AddHours(5), TimeSpan.FromHours(24), keep: 7));      // too soon
        Assert.NotNull(Backups.RunIfDue(day1.AddHours(25), TimeSpan.FromHours(24), keep: 7));  // a day later
        Assert.Equal(2, Backups.List().Count);
    }

    [Fact]
    public void Old_backups_are_pruned_keeping_the_newest()
    {
        _t.Notes.Create("x");
        var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++) Backups.Run(start.AddDays(i));

        var removed = Backups.Prune(keep: 3);

        Assert.Equal(2, removed);
        var left = Backups.List().Select(Path.GetFileName).ToList();
        Assert.Equal(3, left.Count);
        Assert.Equal("notes-20261105-080000.db", left[0]);     // newest first
        Assert.Equal("notes-20261103-080000.db", left[2]);
        Assert.Equal(new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc), BackupService.TimeOf(Backups.Latest()!));
    }

    [Fact]
    public void Two_backups_in_the_same_second_do_not_fail()
    {
        var when = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var a = Backups.Run(when);
        var b = Backups.Run(when);
        Assert.Equal(a, b);
    }

    [Fact]
    public void With_no_backups_yet_there_is_nothing_to_list()
    {
        Assert.Empty(Backups.List());
        Assert.Null(Backups.Latest());
        Assert.Empty(BackupService.ListIn(Path.Combine(_t.Dir, "does-not-exist")));
    }

    [Fact]
    public void Backups_can_be_listed_without_opening_the_database_for_recovery()
    {
        _t.Notes.Create("x");
        Backups.Run(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));
        Backups.Run(new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc));

        var list = BackupService.ListIn(Backups.Folder);

        Assert.Equal(2, list.Count);
        Assert.EndsWith("notes-20261102-080000.db", list[0]);   // newest first
    }
}

public class NoteImporterTests : IDisposable
{
    private readonly TestDb _t = new();
    private string In => Path.Combine(_t.Dir, "in");
    public void Dispose() => _t.Dispose();

    private string File_(string name, string content)
    {
        Directory.CreateDirectory(In);
        var p = Path.Combine(In, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Text_and_markdown_files_become_notes()
    {
        var a = File_("a.txt", "primeira nota\r\ncom duas linhas");
        var b = File_("b.md", "# Título\n\ncorpo");
        var importer = new NoteImporter(_t.Db, _t.Notes);

        var result = importer.ImportFiles(new[] { a, b });

        Assert.Equal(2, result.Imported);
        var bodies = _t.Notes.ListAll().Select(n => n.Body).ToList();
        Assert.Contains("primeira nota\ncom duas linhas", bodies);   // CRLF normalised
        Assert.Contains("# Título\n\ncorpo", bodies);
    }

    [Fact]
    public void Importing_the_same_content_twice_does_not_duplicate()
    {
        var a = File_("a.txt", "mesma coisa");
        var copy = File_("copia.txt", "mesma coisa\n\n");              // same text, different file and trailing blank lines
        var importer = new NoteImporter(_t.Db, _t.Notes);

        var first = importer.ImportFiles(new[] { a });
        var second = importer.ImportFiles(new[] { a, copy });

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(2, second.Duplicates);
        Assert.Equal(1, _t.Notes.Count());
    }

    [Fact]
    public void A_note_that_already_exists_is_not_imported_again()
    {
        _t.Notes.Create("já existe");
        var file = File_("x.txt", "já existe");
        var result = new NoteImporter(_t.Db, _t.Notes).ImportFiles(new[] { file });
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(1, _t.Notes.Count());
    }

    [Fact]
    public void Blank_missing_and_oversized_files_are_counted_not_fatal()
    {
        var blank = File_("blank.txt", "  \n \n");
        var good = File_("good.txt", "ok");
        var huge = File_("huge.txt", new string('x', NoteRepository.MaxBodyLength + 10));
        var missing = Path.Combine(In, "nope.txt");

        var result = new NoteImporter(_t.Db, _t.Notes).ImportFiles(new[] { blank, good, huge, missing });

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Empty);
        Assert.Equal(1, result.TooLarge);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void A_utf8_bom_is_not_kept()
    {
        var path = Path.Combine(In, "bom.txt");
        Directory.CreateDirectory(In);
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("com bom")).ToArray());

        new NoteImporter(_t.Db, _t.Notes).ImportFiles(new[] { path });

        Assert.Equal("com bom", _t.Notes.ListAll().Single().Body);
    }
}

public class AppPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tamarin-paths-" + Guid.NewGuid().ToString("N"));
    public AppPathsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Normal_mode_uses_the_appdata_folder()
    {
        var p = AppPaths.Resolve(_dir, Path.Combine(_dir, "appdata"));
        Assert.False(p.Portable);
        Assert.Equal(Path.Combine(_dir, "appdata", "Tamarin"), p.DataDirectory);
        Assert.EndsWith("notes.db", p.DatabasePath);
        Assert.EndsWith("backups", p.BackupDirectory);
    }

    [Fact]
    public void A_portable_flag_next_to_the_exe_keeps_everything_beside_it()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableFlag), "");
        var p = AppPaths.Resolve(_dir, Path.Combine(_dir, "appdata"));
        Assert.True(p.Portable);
        Assert.Equal(Path.Combine(_dir, "data"), p.DataDirectory);
    }
}
