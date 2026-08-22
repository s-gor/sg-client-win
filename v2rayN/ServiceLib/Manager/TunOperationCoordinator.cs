namespace ServiceLib.Manager;

public static class TunOperationCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object Sync = new();
    private static CancellationTokenSource? _activeCancellation;
    private static long _generation;
    private static int _busy;

    public static bool IsBusy => Volatile.Read(ref _busy) != 0;
    public static long Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// Cancels the operation currently holding the TUN gate and invalidates
    /// requests that were already waiting before the emergency stop.
    /// New operations created after this call are allowed normally.
    /// </summary>
    public static void CancelCurrentAndPending(string reason)
    {
        CancellationTokenSource? active;
        lock (Sync)
        {
            Interlocked.Increment(ref _generation);
            active = _activeCancellation;
        }

        try
        {
            active?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Logging.SaveLog($"TUN operation cancellation requested: {reason}");
    }

    public static async Task<Lease> EnterAsync(string operation, CancellationToken cancellationToken = default)
    {
        var requestedGeneration = Volatile.Read(ref _generation);
        await Gate.WaitAsync(cancellationToken);

        CancellationTokenSource? linked = null;
        try
        {
            // If an emergency stop happened while this request was queued,
            // do not let the stale request restart the connection afterwards.
            if (requestedGeneration != Volatile.Read(ref _generation))
            {
                throw new OperationCanceledException(
                    $"TUN operation invalidated before start: {operation}");
            }

            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (Sync)
            {
                _activeCancellation = linked;
            }

            Interlocked.Exchange(ref _busy, 1);
            Logging.SaveLog($"TUN operation begin: {operation}; generation={requestedGeneration}");
            return new Lease(operation, linked, requestedGeneration);
        }
        catch
        {
            linked?.Dispose();
            Gate.Release();
            throw;
        }
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly string _operation;
        private readonly CancellationTokenSource _cancellation;
        private readonly long _leaseGeneration;
        private int _disposed;

        internal Lease(string operation, CancellationTokenSource cancellation, long generation)
        {
            _operation = operation;
            _cancellation = cancellation;
            _leaseGeneration = generation;
        }

        public CancellationToken Token => _cancellation.Token;

        public bool IsCurrent => _leaseGeneration == Volatile.Read(ref _generation);

        public void ThrowIfCancellationRequested()
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent)
            {
                throw new OperationCanceledException(
                    $"TUN operation invalidated: {_operation}");
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            lock (Sync)
            {
                if (ReferenceEquals(_activeCancellation, _cancellation))
                {
                    _activeCancellation = null;
                }
            }

            _cancellation.Dispose();
            Logging.SaveLog($"TUN operation end: {_operation}; generation={_leaseGeneration}");
            Interlocked.Exchange(ref _busy, 0);
            Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
