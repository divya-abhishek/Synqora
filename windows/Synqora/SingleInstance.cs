namespace Synqora;

// One owner per interactive Windows session/user, shared by portable and installed builds.
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }
    public SingleInstance(string name)
    {
        mutex = new Mutex(false, name + ".Owner");
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }
    public void Signal() => activation.Set();
    public void Listen(Action restore) => listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => restore(), null, Timeout.Infinite, false);
    public void Dispose()
    {
        listener?.Unregister(null);
        activation.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
