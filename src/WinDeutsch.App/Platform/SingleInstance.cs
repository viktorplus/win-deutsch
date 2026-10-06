namespace WinDeutsch.App.Platform;

// One process per user session: the second one signals the first and exits.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\WinDeutsch.Single";
    private const string EventName = @"Local\WinDeutsch.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    // Null when another instance owns the mutex; that instance has been signalled.
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (createdNew)
            return new SingleInstance(mutex, activate);

        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    // `onActivate` runs on a thread-pool thread each time a second instance starts.
    public void Listen(Action onActivate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
