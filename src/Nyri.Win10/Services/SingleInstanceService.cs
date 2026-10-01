using System.Threading;

namespace Nyri.Win10.Services;

/// <summary>
/// Keeps one island per Windows session and asks it to appear on subsequent launches.
/// The primary instance must be disposed on the thread that created it.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string DefaultIdentity = @"Local\JEFFRIPPER.NYRI-WIN10";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly int _ownerThreadId;
    private RegisteredWaitHandle? _listener;
    private int _disposed;

    public SingleInstanceService(string? identity = null)
    {
        identity ??= DefaultIdentity;
        _ownerThreadId = Environment.CurrentManagedThreadId;

        // Open the event before competing for the mutex. A secondary launch can
        // signal during startup; AutoReset retains it until the primary listens.
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Activate");
        try
        {
            _mutex = new Mutex(false, identity);
            try
            {
                IsPrimary = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous primary terminated; WaitOne has granted ownership.
                IsPrimary = true;
            }
        }
        catch
        {
            _mutex?.Dispose();
            _activation.Dispose();
            throw;
        }
    }

    public bool IsPrimary { get; }

    /// <summary>Signals the existing island, including when it is still starting.</summary>
    public bool SignalPrimary()
    {
        if (IsPrimary || Volatile.Read(ref _disposed) != 0)
            return false;

        try
        {
            return _activation.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Calls activate on a worker thread; the caller marshals to the UI.</summary>
    public void StartListening(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can receive activation requests.");
        if (_listener is not null)
            throw new InvalidOperationException("Activation listening has already started.");

        _listener = ThreadPool.RegisterWaitForSingleObject(
            _activation,
            (_, _) =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                    activate();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (IsPrimary && Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Dispose the primary instance on its creating thread.");
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _listener?.Unregister(null);
        _activation.Dispose();
        try
        {
            if (IsPrimary)
                _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
