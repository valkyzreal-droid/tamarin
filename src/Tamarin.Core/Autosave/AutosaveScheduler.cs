namespace Tamarin.Core.Autosave;

/// <summary>
/// Debounced autosave. Each <see cref="Schedule"/> restarts a short timer; when it fires, the latest
/// text is saved. <see cref="Flush"/> saves immediately and is called when the window hides, when the
/// app quits and when Windows shuts down, so at most the last <see cref="Delay"/> of typing is ever at risk.
/// </summary>
public sealed class AutosaveScheduler : IDisposable
{
    private readonly Action<string, string> _save;   // (noteId, body)
    private readonly object _lock = new();
    private Timer? _timer;
    private string? _pendingId;
    private string? _pendingBody;
    private bool _disposed;

    public TimeSpan Delay { get; }

    /// <summary>Raised after a save succeeds. Runs on the timer thread for timed saves.</summary>
    public event Action? Saved;

    /// <summary>Raised when the save callback throws. The text stays pending, so the next flush retries it.</summary>
    public event Action<Exception>? Failed;

    public AutosaveScheduler(Action<string, string> save, TimeSpan? delay = null)
    {
        _save = save;
        Delay = delay ?? TimeSpan.FromMilliseconds(400);
    }

    public bool HasPending
    {
        get { lock (_lock) return _pendingId is not null; }
    }

    public void Schedule(string noteId, string body)
    {
        lock (_lock)
        {
            if (_disposed) return;
            // A different note than the pending one: save the old text first, never lose it.
            if (_pendingId is not null && _pendingId != noteId) SaveLocked();
            _pendingId = noteId;
            _pendingBody = body;
            _timer ??= new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Saves now if anything is pending. Returns true when there is nothing left unsaved.</summary>
    public bool Flush()
    {
        lock (_lock)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return SaveLocked();
        }
    }

    /// <summary>Forgets what is pending without saving (the note was deleted, for example).</summary>
    public void Discard(string noteId)
    {
        lock (_lock)
        {
            if (_pendingId != noteId) return;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _pendingId = null;
            _pendingBody = null;
        }
    }

    private void OnTimer()
    {
        lock (_lock)
        {
            if (_disposed) return;
            SaveLocked();
        }
    }

    private bool SaveLocked()
    {
        if (_pendingId is null || _pendingBody is null) return true;
        try
        {
            _save(_pendingId, _pendingBody);
            _pendingId = null;
            _pendingBody = null;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
            return false;
        }
        Saved?.Invoke();
        return true;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            SaveLocked();
            _disposed = true;
            _timer?.Dispose();
        }
    }
}
