using Tamarin.Core.Autosave;
using Tamarin.Core.Notes;

namespace Tamarin.Core.Capture;

public enum SaveState { Saved, Saving, Failed }

/// <summary>
/// The state behind the capture window: which note is open, saving what you type, moving between
/// notes. No UI types here, so the "never lose text" rules can be tested without Windows.
/// </summary>
public sealed class CaptureSession : IDisposable
{
    private readonly NoteRepository _notes;
    private readonly AutosaveScheduler _autosave;

    public Note? Current { get; private set; }
    public SaveState State { get; private set; } = SaveState.Saved;
    public Exception? LastError { get; private set; }

    /// <summary>The open note changed (opened, switched, created, trashed). The UI reloads the editor.</summary>
    public event Action? CurrentChanged;

    /// <summary>The save state changed. May be raised from the autosave timer thread: marshal to the UI thread.</summary>
    public event Action<SaveState>? StateChanged;

    public CaptureSession(NoteRepository notes, TimeSpan? autosaveDelay = null)
    {
        _notes = notes;
        _autosave = new AutosaveScheduler(SaveNote, autosaveDelay);
        _autosave.Saved += () => SetState(SaveState.Saved, null);
        _autosave.Failed += ex => SetState(SaveState.Failed, ex);
    }

    private void SaveNote(string id, string body)
    {
        // Save() returns false if the note was deleted for good in the meantime; nothing to keep then.
        _notes.Save(id, body);
    }

    private void SetState(SaveState state, Exception? error)
    {
        State = state;
        LastError = error;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Called each time the window is shown. Continues with the note that was open; otherwise the most
    /// recently edited one; otherwise starts a fresh note.
    /// </summary>
    public Note Open()
    {
        if (Current is not null)
        {
            // It may have been trashed or deleted from another window (the trash screen).
            // Keep Current as is: it may hold text newer than the database until the next flush.
            var fresh = _notes.Get(Current.Id);
            if (fresh is { DeletedAt: null }) return Current;
            Current = null;
        }
        Current = _notes.Latest() ?? _notes.Create();
        CurrentChanged?.Invoke();
        return Current;
    }

    /// <summary>Called on every edit in the editor.</summary>
    public void OnTextChanged(string body)
    {
        if (Current is null) return;
        Current = Current with { Body = body };
        SetState(SaveState.Saving, null);
        _autosave.Schedule(Current.Id, body);
    }

    /// <summary>Saves immediately. Call when the window hides, on quit and on Windows shutdown.</summary>
    public bool Flush() => _autosave.Flush();

    public bool HasUnsavedText => _autosave.HasPending;

    public Note NewNote()
    {
        Flush();
        // An empty note is reused rather than piling up blank ones.
        if (Current is { IsEmpty: true }) { CurrentChanged?.Invoke(); return Current; }
        Current = _notes.Create();
        CurrentChanged?.Invoke();
        return Current;
    }

    /// <summary>Switch to the next (older) note. Returns false at the end of the list.</summary>
    public bool GoOlder() => Go(c => _notes.Older(c));

    /// <summary>Switch to the previous (newer) note. Returns false at the start of the list.</summary>
    public bool GoNewer() => Go(c => _notes.Newer(c));

    private bool Go(Func<Note, Note?> step)
    {
        if (Current is null) return false;
        Flush();
        // Re-read so the saved timestamp is what the ordering uses.
        var cur = _notes.Get(Current.Id) ?? Current;
        var next = step(cur);
        if (next is null) return false;
        Current = next;
        DropIfEmpty(cur);
        CurrentChanged?.Invoke();
        return true;
    }

    /// <summary>Opens a specific note, for example one picked from the search results.</summary>
    public bool OpenNote(string id)
    {
        Flush();
        var note = _notes.Get(id);
        if (note is null || note.DeletedAt is not null) return false;
        var old = Current is null ? null : _notes.Get(Current.Id);
        Current = note;
        if (old is not null && old.Id != note.Id) DropIfEmpty(old);
        CurrentChanged?.Invoke();
        return true;
    }

    /// <summary>A blank note you walked away from is deleted for good instead of piling up.</summary>
    private void DropIfEmpty(Note old)
    {
        if (old.IsEmpty) _notes.DeleteForever(old.Id);
    }

    /// <summary>Moves the open note to the trash and opens a neighbour (or a fresh note).</summary>
    public void TrashCurrent()
    {
        if (Current is null) return;
        var id = Current.Id;
        // Save first, so what lands in the trash is the text as you last saw it and can be recovered.
        Flush();
        var cur = _notes.Get(id) ?? Current;
        var neighbour = _notes.Older(cur) ?? _notes.Newer(cur);
        // A blank note has nothing worth recovering, so it skips the trash.
        if (cur.IsEmpty) _notes.DeleteForever(id);
        else _notes.Trash(id);
        Current = neighbour ?? _notes.Create();
        CurrentChanged?.Invoke();
    }

    public (int Index, int Total) Position() =>
        Current is null ? (0, 0) : _notes.Position(_notes.Get(Current.Id) ?? Current);

    public void SetPinned(bool pinned)
    {
        if (Current is null) return;
        Flush();
        _notes.SetPinned(Current.Id, pinned);
        Current = _notes.Get(Current.Id) ?? Current;
    }

    public void Dispose() => _autosave.Dispose();
}
