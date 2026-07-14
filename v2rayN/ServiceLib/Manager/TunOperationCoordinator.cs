namespace ServiceLib.Manager;

public static class TunOperationCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _busy;

    public static bool IsBusy => Volatile.Read(ref _busy) != 0;

    public static async Task<IAsyncDisposable> EnterAsync(string operation, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _busy, 1);
        Logging.SaveLog($"TUN operation begin: {operation}");
        return new Lease(operation);
    }

    private sealed class Lease(string operation) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            Logging.SaveLog($"TUN operation end: {operation}");
            Interlocked.Exchange(ref _busy, 0);
            Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
