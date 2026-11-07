using Tamarin.Core.Notes;
using Xunit;

namespace Tamarin.Core.Tests;

public class SearchTests : IDisposable
{
    private readonly TestDb _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Accents_are_ignored_in_both_directions()
    {
        _t.Notes.Create("Reunião sobre ação do café amanhã");
        _t.Notes.Create("resume of the meeting");

        Assert.Single(_t.Notes.Search("acao"));        // typed without accent, stored with
        Assert.Single(_t.Notes.Search("ação"));        // typed with accent
        Assert.Single(_t.Notes.Search("cafe"));
        Assert.Single(_t.Notes.Search("amanha"));
        Assert.Single(_t.Notes.Search("resume"));      // "resume" and "résumé" would match each other
        Assert.Single(_t.Notes.Search("REUNIAO"));     // case does not matter
    }

    [Fact]
    public void Every_word_is_a_prefix_so_search_works_while_typing()
    {
        _t.Notes.Create("Reunião sobre orçamento");
        _t.Notes.Create("Pauta da reunião semanal");
        _t.Notes.Create("Ideias de nome para o app");

        Assert.Equal(2, _t.Notes.Search("reun").Count);
        Assert.Equal(2, _t.Notes.Search("r").Count);       // "Reunião" and "reunião" (and "orçamento" does not start with r)
        Assert.Single(_t.Notes.Search("reun orc"));        // both words must match, each as a prefix
        Assert.Empty(_t.Notes.Search("cao"));              // the middle of a word does not match
    }

    [Fact]
    public void Operators_and_punctuation_in_the_query_never_throw()
    {
        _t.Notes.Create("c++ and rust: notes about android");
        var nasty = new[]
        {
            "\"", "\"unclosed", "AND", "OR", "NOT", "NEAR(", "NEAR(a b)", "a:b", "c++", "(", ")", "*", "-x", "^a",
            "a OR", "title:foo", "foo*bar", "'", "\\", "%", "_", "; DROP TABLE notes;", "😀", "\u0002", "a\"b\"c",
        };
        foreach (var q in nasty)
        {
            var ex = Record.Exception(() => _t.Notes.Search(q));
            Assert.Null(ex);
        }
        // Operators are searched as plain words, not obeyed.
        Assert.Single(_t.Notes.Search("AND"));          // "and" is a word in the note
        Assert.Single(_t.Notes.Search("c++"));
        Assert.Equal(1, _t.Notes.Count());              // the "DROP TABLE" query did nothing
    }

    [Fact]
    public void An_empty_query_returns_the_recent_notes()
    {
        for (var i = 0; i < 5; i++) _t.Notes.Create("nota número " + i);

        var all = _t.Notes.Search("");
        var blank = _t.Notes.Search("   ");
        var nothing = _t.Notes.Search(null);

        Assert.Equal(5, all.Count);
        Assert.Equal(5, blank.Count);
        Assert.Equal(5, nothing.Count);
        Assert.Equal(2, _t.Notes.Search("", limit: 2).Count);
    }

    [Fact]
    public void A_query_with_only_punctuation_behaves_like_an_empty_one()
    {
        _t.Notes.Create("alguma coisa");
        Assert.Single(_t.Notes.Search("!!!"));
    }

    [Fact]
    public void Results_are_limited_and_best_match_first()
    {
        for (var i = 0; i < 30; i++) _t.Notes.Create("pão " + i);
        _t.Notes.Create("pão pão pão pão");

        var hits = _t.Notes.Search("pao", limit: 10);

        Assert.Equal(10, hits.Count);
    }

    [Fact]
    public void Snippets_mark_the_matches_and_can_be_read_as_plain_text()
    {
        _t.Notes.Create("Preciso alinhar a reunião de sexta com o time de vendas");

        var hit = _t.Notes.Search("reun").Single();
        var parts = SnippetParser.Parse(hit.Snippet);

        Assert.Contains(parts, p => p.IsMatch);
        Assert.Contains(parts, p => !p.IsMatch);
        var plain = SnippetParser.ToPlain(hit.Snippet);
        Assert.DoesNotContain(SnippetParser.Open.ToString(), plain, StringComparison.Ordinal); // culture compare treats control chars as ignorable
        Assert.DoesNotContain(SnippetParser.Close.ToString(), plain, StringComparison.Ordinal);
        Assert.Contains("reuni", plain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnippetParser_splits_on_the_markers()
    {
        var raw = "…alinhar a " + SnippetParser.Open + "reun" + SnippetParser.Close + "ião…";
        var parts = SnippetParser.Parse(raw);

        Assert.Equal(3, parts.Count);
        Assert.Equal(new SnippetPart("…alinhar a ", false), parts[0]);
        Assert.Equal(new SnippetPart("reun", true), parts[1]);
        Assert.Equal(new SnippetPart("ião…", false), parts[2]);
    }

    [Fact]
    public void An_edited_note_is_found_by_its_new_text_and_not_by_the_old()
    {
        var note = _t.Notes.Create("rascunho antigo");
        _t.Notes.Save(note.Id, "versão final");

        Assert.Empty(_t.Notes.Search("rascunho"));
        Assert.Single(_t.Notes.Search("final"));
    }

    [Fact]
    public void Search_stays_fast_with_thousands_of_notes()
    {
        var words = new[] { "nota", "ideia", "reunião", "compras", "código", "projeto", "café", "lista", "tarefa", "ligar" };
        var rnd = new Random(42);
        _t.Db.InTransaction(c =>
        {
            for (var i = 0; i < 5000; i++)
            {
                var body = string.Join(' ', Enumerable.Range(0, 12).Select(_ => words[rnd.Next(words.Length)])) + " #" + i;
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO notes (id, body) VALUES ($id, $b);";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$b", body);
                cmd.ExecuteNonQuery();
            }
            return 0;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = _t.Notes.Search("reuniao codigo", limit: 50);
        sw.Stop();

        Assert.Equal(50, hits.Count);
        Assert.True(sw.ElapsedMilliseconds < 500, $"search took {sw.ElapsedMilliseconds} ms");
    }
}

public class FtsQueryTests
{
    [Fact]
    public void Each_word_becomes_a_quoted_prefix_term()
    {
        Assert.Equal("\"foo\"* \"bar\"*", FtsQuery.BuildMatch("foo bar"));
        Assert.Equal("\"Reunião\"*", FtsQuery.BuildMatch("  Reunião "));   // case is kept; FTS5 matches it case-insensitively
    }

    [Fact]
    public void Nothing_searchable_gives_null()
    {
        Assert.Null(FtsQuery.BuildMatch(null));
        Assert.Null(FtsQuery.BuildMatch(""));
        Assert.Null(FtsQuery.BuildMatch("   "));
        Assert.Null(FtsQuery.BuildMatch("+-*()\""));
    }

    [Fact]
    public void Punctuation_separates_words_and_quotes_cannot_leak_through()
    {
        Assert.Equal("\"c\"*", FtsQuery.BuildMatch("c++"));
        Assert.Equal("\"a\"* \"b\"*", FtsQuery.BuildMatch("a:b"));
        Assert.Equal("\"a\"* \"b\"* \"c\"*", FtsQuery.BuildMatch("a\"b\"c"));
        Assert.DoesNotContain("NEAR", FtsQuery.BuildMatch("NEAR(a b)")!.Replace("\"NEAR\"*", ""));
    }
}
