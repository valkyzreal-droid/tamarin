using Tamarin.Core.Autosave;
using Tamarin.Core.Capture;
using Xunit;

namespace Tamarin.Core.Tests;

public class AutosaveSchedulerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);

    private sealed class Recorder
    {
        private readonly object _gate = new();
        public List<(string Id, string Body)> Saves { get; } = new();
        public Func<bool>? Fail { get; set; }
        public void Save(string id, string body)
        {
            if (Fail?.Invoke() == true) throw new IOException("disk full");
            lock (_gate) Saves.Add((id, body));
        }
        public int Count { get { lock (_gate) return Saves.Count; } }
    }

    [Fact]
    public void Rapid_typing_is_coalesced_into_one_save_with_the_latest_text()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, Delay);

        s.Schedule("a", "1");
        s.Schedule("a", "12");
        s.Schedule("a", "123");

        Assert.True(Wait.Until(() => r.Count == 1));
        Thread.Sleep(Delay.Milliseconds * 3);       // no second save sneaks in
        Assert.Equal(1, r.Count);
        Assert.Equal(("a", "123"), r.Saves[0]);
        Assert.False(s.HasPending);
    }

    [Fact]
    public void Nothing_is_saved_before_the_delay_passes()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, TimeSpan.FromSeconds(30));
        s.Schedule("a", "x");
        Thread.Sleep(100);
        Assert.Equal(0, r.Count);
        Assert.True(s.HasPending);
    }

    [Fact]
    public void Flush_saves_immediately_and_cancels_the_timer()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, Delay);
        s.Schedule("a", "x");

        Assert.True(s.Flush());

        Assert.Equal(1, r.Count);                    // already saved, no waiting
        Thread.Sleep(Delay.Milliseconds * 4);
        Assert.Equal(1, r.Count);                    // and the timer does not save it again
    }

    [Fact]
    public void Flush_with_nothing_pending_is_a_no_op()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, Delay);
        Assert.True(s.Flush());
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Switching_to_another_note_saves_the_previous_text_first()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, TimeSpan.FromSeconds(30));

        s.Schedule("a", "texto da nota a");
        s.Schedule("b", "texto da nota b");

        Assert.Equal(("a", "texto da nota a"), Assert.Single(r.Saves));
        s.Flush();
        Assert.Equal(2, r.Count);
        Assert.Equal(("b", "texto da nota b"), r.Saves[1]);
    }

    [Fact]
    public void A_failed_save_keeps_the_text_pending_and_the_next_flush_retries_it()
    {
        var r = new Recorder();
        var failing = true;
        r.Fail = () => failing;
        Exception? reported = null;
        using var s = new AutosaveScheduler(r.Save, TimeSpan.FromSeconds(30));
        s.Failed += ex => reported = ex;
        s.Schedule("a", "não perder");

        Assert.False(s.Flush());
        Assert.True(s.HasPending);
        Assert.IsType<IOException>(reported);

        failing = false;
        Assert.True(s.Flush());
        Assert.Equal(("a", "não perder"), Assert.Single(r.Saves));
        Assert.False(s.HasPending);
    }

    [Fact]
    public void Dispose_flushes_what_is_pending()
    {
        var r = new Recorder();
        var s = new AutosaveScheduler(r.Save, TimeSpan.FromSeconds(30));
        s.Schedule("a", "no encerramento");
        s.Dispose();
        Assert.Equal(("a", "no encerramento"), Assert.Single(r.Saves));
    }

    [Fact]
    public void Discard_drops_pending_text_for_that_note_only()
    {
        var r = new Recorder();
        using var s = new AutosaveScheduler(r.Save, TimeSpan.FromSeconds(30));
        s.Schedule("a", "x");
        s.Discard("other");
        Assert.True(s.HasPending);
        s.Discard("a");
        Assert.False(s.HasPending);
        s.Flush();
        Assert.Equal(0, r.Count);
    }
}

public class CaptureSessionTests : IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);
    private readonly TestDb _t = new();
    private CaptureSession? _s;

    private CaptureSession Session() => _s ??= new CaptureSession(_t.Notes, Delay);

    public void Dispose()
    {
        _s?.Dispose();
        _t.Dispose();
    }

    [Fact]
    public void Opening_on_a_fresh_database_creates_one_empty_note()
    {
        var note = Session().Open();
        Assert.True(note.IsEmpty);
        Assert.Equal(1, _t.Notes.Count());
        Assert.Same(note, Session().Current);
    }

    [Fact]
    public void Opening_again_continues_with_the_same_note_instead_of_making_a_new_one()
    {
        var first = Session().Open();
        var second = Session().Open();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _t.Notes.Count());
    }

    [Fact]
    public void A_new_session_after_a_restart_reopens_the_note_you_were_editing()
    {
        var older = _t.Notes.Create("antiga");
        var newer = _t.Notes.Create("recente");
        _t.SetUpdated(older, 1000);
        _t.SetUpdated(newer, 2000);

        Assert.Equal(newer.Id, Session().Open().Id);
    }

    [Fact]
    public void Typing_is_saved_automatically_after_the_delay()
    {
        var note = Session().Open();
        Session().OnTextChanged("anotação rápida");

        Assert.True(Wait.Until(() => _t.Notes.Get(note.Id)!.Body == "anotação rápida"));
        Assert.True(Wait.Until(() => Session().State == SaveState.Saved));
    }

    [Fact]
    public void The_state_goes_to_Saving_while_text_is_pending()
    {
        Session().Open();
        var states = new List<SaveState>();
        Session().StateChanged += states.Add;

        Session().OnTextChanged("a");

        Assert.Equal(SaveState.Saving, Session().State);
        Assert.True(Wait.Until(() => Session().State == SaveState.Saved));
        Assert.Contains(SaveState.Saving, states);
    }

    [Fact]
    public void Flush_saves_at_once_so_hiding_the_window_never_loses_text()
    {
        var note = Session().Open();
        Session().OnTextChanged("escrevi e fechei na hora");

        Assert.True(Session().Flush());

        Assert.Equal("escrevi e fechei na hora", _t.Notes.Get(note.Id)!.Body);   // no waiting
    }

    [Fact]
    public void Text_survives_the_process_dying_right_after_a_flush()
    {
        var note = Session().Open();
        Session().OnTextChanged("sobrevive ao crash");
        Session().Flush();

        // Simulate the process dying: the connection goes away without any orderly shutdown of the session.
        _t.Db.Dispose();
        var reopened = Db.Database.Open(_t.DbPath);
        try
        {
            var repo = new Notes.NoteRepository(reopened);
            Assert.Equal("sobrevive ao crash", repo.Get(note.Id)!.Body);
        }
        finally { reopened.Dispose(); }
    }

    [Fact]
    public void Only_the_last_moments_of_typing_are_at_risk_without_a_flush()
    {
        var note = Session().Open();
        Session().OnTextChanged("primeira parte");
        Assert.True(Wait.Until(() => _t.Notes.Get(note.Id)!.Body == "primeira parte"));   // saved by the timer

        Session().OnTextChanged("primeira parte e mais um pouco");
        // No flush and no wait: the database still has the earlier text. That window is exactly one delay long.
        Assert.Equal("primeira parte", _t.Notes.Get(note.Id)!.Body);
        Assert.True(Session().HasUnsavedText);
    }

    [Fact]
    public void Disposing_the_session_flushes_pending_text()
    {
        var note = Session().Open();
        Session().OnTextChanged("no desligamento");
        Session().Dispose();
        Assert.Equal("no desligamento", _t.Notes.Get(note.Id)!.Body);
    }

    [Fact]
    public void NewNote_reuses_a_blank_note_but_starts_a_new_one_after_real_text()
    {
        var first = Session().Open();
        var again = Session().NewNote();
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(1, _t.Notes.Count());

        Session().OnTextChanged("com texto");
        var second = Session().NewNote();
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("com texto", _t.Notes.Get(first.Id)!.Body);   // saved before moving on
        Assert.Equal(2, _t.Notes.Count());
    }

    [Fact]
    public void Walking_between_notes_with_the_keyboard_saves_each_one_first()
    {
        var a = _t.Notes.Create("a"); var b = _t.Notes.Create("b"); var c = _t.Notes.Create("c");
        _t.SetUpdated(a, 1000); _t.SetUpdated(b, 2000); _t.SetUpdated(c, 3000);

        Assert.Equal(c.Id, Session().Open().Id);               // newest first
        Session().OnTextChanged("c editada");                  // updates c, which stays the newest
        Assert.True(Session().GoOlder());
        Assert.Equal(b.Id, Session().Current!.Id);
        Assert.True(Session().GoOlder());
        Assert.Equal(a.Id, Session().Current!.Id);
        Assert.False(Session().GoOlder());                     // end of the list: stays put
        Assert.Equal(a.Id, Session().Current!.Id);
        Assert.True(Session().GoNewer());
        Assert.Equal(b.Id, Session().Current!.Id);
        Assert.Equal("c editada", _t.Notes.Get(c.Id)!.Body);
    }

    [Fact]
    public void Position_reports_where_you_are_in_the_list()
    {
        var a = _t.Notes.Create("a"); var b = _t.Notes.Create("b");
        _t.SetUpdated(a, 1000); _t.SetUpdated(b, 2000);
        Session().Open();
        Assert.Equal((1, 2), Session().Position());
        Session().GoOlder();
        Assert.Equal((2, 2), Session().Position());
    }

    [Fact]
    public void Walking_away_from_a_blank_note_deletes_it_so_blanks_do_not_pile_up()
    {
        var keep = _t.Notes.Create("nota real");
        _t.SetUpdated(keep, 1000);                 // clearly older: same-millisecond creation would tie and order by random id
        Session().Open();
        Session().NewNote();                       // a blank note appears
        Assert.Equal(2, _t.Notes.Count());

        Assert.True(Session().GoOlder());          // back to the real one

        Assert.Equal(keep.Id, Session().Current!.Id);
        Assert.Equal(1, _t.Notes.Count());
        Assert.Empty(_t.Notes.ListTrash());        // deleted, not trashed
    }

    [Fact]
    public void Trashing_saves_first_so_the_trashed_text_is_what_you_last_saw()
    {
        var other = _t.Notes.Create("outra");
        var note = _t.Notes.Create("original");
        _t.SetUpdated(other, 1000); _t.SetUpdated(note, 2000);
        Session().Open();

        Session().OnTextChanged("texto novo que ainda não foi salvo");
        Session().TrashCurrent();                  // immediately, before the timer fires

        var trashed = Assert.Single(_t.Notes.ListTrash());
        Assert.Equal("texto novo que ainda não foi salvo", trashed.Body);
        Assert.Equal(other.Id, Session().Current!.Id);   // moved to the neighbour
    }

    [Fact]
    public void Trashing_the_only_note_leaves_a_fresh_blank_one_open()
    {
        var only = _t.Notes.Create("única");
        Session().Open();

        Session().TrashCurrent();

        Assert.NotEqual(only.Id, Session().Current!.Id);
        Assert.True(Session().Current!.IsEmpty);
        Assert.Single(_t.Notes.ListTrash());
    }

    [Fact]
    public void Trashing_a_blank_note_skips_the_trash()
    {
        Session().Open();                          // a blank note
        var blankId = Session().Current!.Id;

        Session().TrashCurrent();

        Assert.Empty(_t.Notes.ListTrash());
        Assert.Null(_t.Notes.Get(blankId));
    }

    [Fact]
    public void A_note_trashed_elsewhere_is_not_shown_again_when_the_window_reopens()
    {
        var a = _t.Notes.Create("a"); var b = _t.Notes.Create("b");
        _t.SetUpdated(a, 1000); _t.SetUpdated(b, 2000);
        Session().Open();                          // b
        _t.Notes.Trash(b.Id);                      // trash screen removed it

        var reopened = Session().Open();

        Assert.Equal(a.Id, reopened.Id);
    }

    [Fact]
    public void OpenNote_switches_to_a_search_result_and_refuses_a_trashed_one()
    {
        var a = _t.Notes.Create("a"); var b = _t.Notes.Create("b");
        _t.SetUpdated(a, 1000); _t.SetUpdated(b, 2000);
        Session().Open();

        Assert.True(Session().OpenNote(a.Id));
        Assert.Equal(a.Id, Session().Current!.Id);

        _t.Notes.Trash(b.Id);
        Assert.False(Session().OpenNote(b.Id));
        Assert.Equal(a.Id, Session().Current!.Id);
        Assert.False(Session().OpenNote("does-not-exist"));
    }

    [Fact]
    public void A_failing_database_reports_Failed_and_keeps_the_text_to_retry()
    {
        Session().Open();
        _t.Db.Dispose();                           // the disk "goes away"
        Session().OnTextChanged("não pode sumir");

        Assert.False(Session().Flush());

        Assert.Equal(SaveState.Failed, Session().State);
        Assert.NotNull(Session().LastError);
        Assert.True(Session().HasUnsavedText);
        Assert.Equal("não pode sumir", Session().Current!.Body);   // the editor still holds it
    }

    [Fact]
    public void Pinning_the_open_note_is_saved_with_its_text()
    {
        var note = Session().Open();
        Session().OnTextChanged("fixar esta");
        Session().SetPinned(true);

        var stored = _t.Notes.Get(note.Id)!;
        Assert.True(stored.Pinned);
        Assert.Equal("fixar esta", stored.Body);
        Assert.True(Session().Current!.Pinned);
    }
}
