using Tamarin.Core.Text;
using Xunit;

namespace Tamarin.Core.Tests;

public class LineFormatterTests
{
    [Fact]
    public void Cycling_a_line_goes_text_then_checkbox_then_bullet_then_text()
    {
        var line = "comprar pão";
        line = LineFormatter.CycleLine(line);
        Assert.Equal("- [ ] comprar pão", line);
        line = LineFormatter.CycleLine(line);
        Assert.Equal("- comprar pão", line);
        line = LineFormatter.CycleLine(line);
        Assert.Equal("comprar pão", line);
    }

    [Fact]
    public void Cycling_keeps_indentation_and_handles_a_checked_box()
    {
        Assert.Equal("  - [ ] filho", LineFormatter.CycleLine("  filho"));
        Assert.Equal("  - filho", LineFormatter.CycleLine("  - [x] filho"));
        Assert.Equal("texto", LineFormatter.CycleLine("1. texto"));
    }

    [Fact]
    public void Toggling_flips_a_checkbox_and_ignores_other_lines()
    {
        Assert.True(LineFormatter.TryToggleCheckbox("- [ ] tarefa", out var done));
        Assert.Equal("- [x] tarefa", done);
        Assert.True(LineFormatter.TryToggleCheckbox(done, out var undone));
        Assert.Equal("- [ ] tarefa", undone);
        Assert.True(LineFormatter.TryToggleCheckbox("  - [X] aninhada", out var nested));
        Assert.Equal("  - [ ] aninhada", nested);

        Assert.False(LineFormatter.TryToggleCheckbox("- só bullet", out var same));
        Assert.Equal("- só bullet", same);
        Assert.False(LineFormatter.TryToggleCheckbox("texto", out _));
    }

    [Fact]
    public void Tab_nests_list_items_and_leaves_plain_text_alone()
    {
        Assert.Equal("  - [ ] item", LineFormatter.Indent("- [ ] item"));
        Assert.Equal("- [ ] item", LineFormatter.Outdent("  - [ ] item"));
        Assert.Equal("- item", LineFormatter.Outdent("- item"));          // nothing to remove
        Assert.Equal("texto", LineFormatter.Indent("texto"));              // not a list
        Assert.Equal("- x", LineFormatter.Outdent("  - x"));
        Assert.Equal("- x", LineFormatter.Outdent(" - x"));                // a single space also comes off
    }

    [Fact]
    public void Enter_continues_a_list_with_the_right_marker()
    {
        Assert.Equal("- [ ] ", LineFormatter.NextLinePrefix("- [x] feito"));
        Assert.Equal("- [ ] ", LineFormatter.NextLinePrefix("- [ ] a fazer"));
        Assert.Equal("- ", LineFormatter.NextLinePrefix("- bullet"));
        Assert.Equal("  - ", LineFormatter.NextLinePrefix("  - aninhado"));
        Assert.Equal("* ", LineFormatter.NextLinePrefix("* estrela"));
        Assert.Equal("4. ", LineFormatter.NextLinePrefix("3. terceiro"));
        Assert.Equal("11. ", LineFormatter.NextLinePrefix("10. décimo"));
        Assert.Null(LineFormatter.NextLinePrefix("texto normal"));
        Assert.Null(LineFormatter.NextLinePrefix(""));
    }

    [Fact]
    public void Enter_on_an_empty_list_item_ends_the_list()
    {
        Assert.True(LineFormatter.IsEmptyListItem("- [ ] "));
        Assert.True(LineFormatter.IsEmptyListItem("  - "));
        Assert.True(LineFormatter.IsEmptyListItem("2. "));
        Assert.False(LineFormatter.IsEmptyListItem("- [ ] algo"));
        Assert.False(LineFormatter.IsEmptyListItem("texto"));
    }

    [Fact]
    public void MarkerLength_counts_indentation_and_marker_so_Enter_only_continues_after_it()
    {
        Assert.Equal(0, LineFormatter.MarkerLength("texto"));
        Assert.Equal(2, LineFormatter.MarkerLength("- item"));
        Assert.Equal(6, LineFormatter.MarkerLength("- [ ] item"));
        Assert.Equal(8, LineFormatter.MarkerLength("  - [x] item"));
        Assert.Equal(4, LineFormatter.MarkerLength("10. item"));
        Assert.Equal(2, LineFormatter.MarkerLength("- "));
    }

    [Fact]
    public void Classify_recognises_the_four_line_kinds()
    {
        Assert.Equal(LineFormatter.LineKind.Plain, LineFormatter.Classify("texto"));
        Assert.Equal(LineFormatter.LineKind.Plain, LineFormatter.Classify("-sem espaço"));
        Assert.Equal(LineFormatter.LineKind.Checkbox, LineFormatter.Classify("- [x] a"));
        Assert.Equal(LineFormatter.LineKind.Bullet, LineFormatter.Classify("- a"));
        Assert.Equal(LineFormatter.LineKind.Numbered, LineFormatter.Classify("12. a"));
    }

    [Fact]
    public void Wrapping_adds_the_marker_around_the_selection_and_removes_it_on_the_second_press()
    {
        var (text, start, len) = LineFormatter.ToggleWrap("uma palavra aqui", 4, 7, "**");
        Assert.Equal("uma **palavra** aqui", text);
        Assert.Equal((6, 7), (start, len));

        // Pressing again with the same selection removes the markers.
        (text, start, len) = LineFormatter.ToggleWrap(text, start, len, "**");
        Assert.Equal("uma palavra aqui", text);
        Assert.Equal((4, 7), (start, len));
    }

    [Fact]
    public void Wrapping_works_when_the_markers_are_inside_the_selection()
    {
        var (text, start, len) = LineFormatter.ToggleWrap("uma **palavra** aqui", 4, 11, "**");
        Assert.Equal("uma palavra aqui", text);
        Assert.Equal((4, 7), (start, len));
    }

    [Fact]
    public void Wrapping_with_no_selection_inserts_the_pair_with_the_caret_between()
    {
        var (text, start, len) = LineFormatter.ToggleWrap("abc", 3, 0, "~~");
        Assert.Equal("abc~~~~", text);
        Assert.Equal((5, 0), (start, len));
    }

    [Fact]
    public void Wrapping_clamps_a_bad_selection_instead_of_throwing()
    {
        var (text, _, _) = LineFormatter.ToggleWrap("abc", 99, 50, "*");
        Assert.Equal("abc**", text);
        (text, _, _) = LineFormatter.ToggleWrap("abc", -5, -1, "*");
        Assert.Equal("**abc", text);
    }

    [Fact]
    public void A_link_is_found_under_the_caret_without_trailing_punctuation()
    {
        var text = "veja https://exemplo.com/pagina?x=1, depois http://outro.org.";
        Assert.Equal("https://exemplo.com/pagina?x=1", LineFormatter.FindUrlAt(text, 10));
        Assert.Equal("https://exemplo.com/pagina?x=1", LineFormatter.FindUrlAt(text, 5));   // right at the start
        Assert.Equal("http://outro.org", LineFormatter.FindUrlAt(text, text.Length - 3));
        Assert.Null(LineFormatter.FindUrlAt(text, 1));
        Assert.Null(LineFormatter.FindUrlAt("sem link", 3));
        Assert.Null(LineFormatter.FindUrlAt("ftp://nao.suportado", 3));
    }

    [Fact]
    public void Word_count_handles_whitespace_newlines_and_accents()
    {
        Assert.Equal(0, LineFormatter.WordCount(""));
        Assert.Equal(0, LineFormatter.WordCount("  \n\t "));
        Assert.Equal(3, LineFormatter.WordCount("uma  nota\ncurta"));
        Assert.Equal(2, LineFormatter.WordCount("ação  rápida"));
    }
}
