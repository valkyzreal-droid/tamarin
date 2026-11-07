using System;
using System.Threading;

namespace Tamarin.App.Services;

/// <summary>One copy per Windows user. A second launch asks the first to show its window, then exits.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Tamarin.Instance";
    private const string EventName = @"Local\Tamarin.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly Thread? _listener;
    private volatile bool _stop;

    public bool IsFirst { get; }
    public event Action? ShowRequested;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsFirst = created;
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (IsFirst)
        {
            _listener = new Thread(Listen) { IsBackground = true, Name = "Tamarin.SingleInstance" };
            _listener.Start();
        }
    }

    /// <summary>Called by a second copy: wakes the first one.</summary>
    public void SignalFirst() => _show.Set();

    private void Listen()
    {
        while (!_stop)
        {
            if (_show.WaitOne(500) && !_stop) ShowRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _stop = true;
        _listener?.Join(TimeSpan.FromSeconds(2)); // else it could wait on a disposed handle and crash at exit
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        _mutex.Dispose();
        _show.Dispose();
    }
}
