namespace ServiceLib.Services;

/// <summary>
/// Reliable SG Client latency testing for large profile lists.
/// The upstream speed-test loop was designed for interactive single-profile tests;
/// this service adds deterministic progress, cancellation and a final state for
/// every profile so the UI never remains stuck on "Testing...".
/// </summary>
public sealed class SgLatencyTestService
{
    public const string TestingText = "Проверяется…";
    public const string UnavailableText = "Недоступна";
    public const string ErrorText = "Ошибка";
    public const string CancelledText = "Отменено";
    public const string NotTestedText = "Не проверено";

    public const int UnavailableDelay = -1;
    public const int ErrorDelay = -2;
    public const int CancelledDelay = -3;
    public const int NotTestedDelay = -4;

    private static readonly TimeSpan CoreStartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProfileTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IpInfoTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CoreStopTimeout = TimeSpan.FromSeconds(6);
    private const int MaxReliableBatchSize = 64;

    private readonly Config _config;
    private readonly Func<SgLatencyTestUpdate, Task> _update;
    private readonly object _runSync = new();
    private readonly ConcurrentDictionary<string, SgLatencyOutcome> _outcomes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _runCts;

    public SgLatencyTestService(Config config, Func<SgLatencyTestUpdate, Task> update)
    {
        _config = config;
        _update = update;
    }

    public bool IsRunning
    {
        get
        {
            lock (_runSync)
            {
                return _runCts is { IsCancellationRequested: false };
            }
        }
    }

    public bool Cancel()
    {
        lock (_runSync)
        {
            if (_runCts is null || _runCts.IsCancellationRequested)
            {
                return false;
            }

            _runCts.Cancel();
            return true;
        }
    }

    public async Task<SgLatencyTestSummary> RunAsync(IReadOnlyCollection<ProfileItemModel> selectedProfiles)
    {
        CancellationTokenSource cts;
        lock (_runSync)
        {
            if (_runCts is { IsCancellationRequested: false })
            {
                throw new InvalidOperationException("Проверка задержки уже выполняется.");
            }

            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            cts = _runCts;
        }

        _outcomes.Clear();
        var profiles = selectedProfiles
            .Where(item => item is not null && item.IndexId.IsNotEmpty())
            .GroupBy(item => item.IndexId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        try
        {
            var validItems = await PrepareItemsAsync(profiles, cts.Token);
            var configuredPageSize = _config.SpeedTestItem.SpeedTestPageSize ?? MaxReliableBatchSize;
            var pageSize = Math.Max(1, Math.Min(validItems.Count, Math.Min(configuredPageSize, MaxReliableBatchSize)));

            foreach (var coreGroup in validItems.GroupBy(item => item.CoreType))
            {
                foreach (var batch in coreGroup.Chunk(pageSize))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await RunBatchWithFallbackAsync(batch.ToList(), cts.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // The finalizer below gives every unfinished profile an explicit
            // "Отменено" state. Cancellation is a normal user action.
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(SgLatencyTestService), ex);
        }
        finally
        {
            try
            {
                var fallback = cts.IsCancellationRequested ? SgLatencyOutcome.Cancelled : SgLatencyOutcome.Error;
                await FinalizeRemainingAsync(profiles, fallback);
                await ProfileExManager.Instance.SaveTo();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SG latency finalization failed", ex);
            }
            finally
            {
                lock (_runSync)
                {
                    if (ReferenceEquals(_runCts, cts))
                    {
                        _runCts = null;
                    }
                }
                cts.Dispose();
            }
        }

        return BuildSummary(profiles.Count);
    }

    private async Task<List<ServerTestItem>> PrepareItemsAsync(
        IReadOnlyCollection<ProfileItemModel> selectedProfiles,
        CancellationToken cancellationToken)
    {
        var ids = selectedProfiles.Select(item => item.IndexId).ToList();
        var profileMap = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(ids);
        var result = new List<ServerTestItem>(selectedProfiles.Count);
        var queue = 0;

        foreach (var model in selectedProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = profileMap.GetValueOrDefault(model.IndexId);
            if (profile is null
                || model.ConfigType == EConfigType.Custom
                || (!model.ConfigType.IsComplexType() && model.Port <= 0))
            {
                await FinalizeAsync(model.IndexId, SgLatencyOutcome.NotTested);
                continue;
            }

            var item = new ServerTestItem
            {
                IndexId = model.IndexId,
                Address = model.Address,
                Port = model.Port,
                ConfigType = model.ConfigType,
                QueueNum = queue++,
                Profile = profile,
                CoreType = AppManager.Instance.GetCoreType(profile, model.ConfigType),
            };
            result.Add(item);
            await _update(new SgLatencyTestUpdate(item.IndexId, TestingText, null, null, null, false));
            ProfileExManager.Instance.SetTestDelay(item.IndexId, 0);
        }

        return result;
    }

    private async Task RunBatchWithFallbackAsync(List<ServerTestItem> batch, CancellationToken cancellationToken)
    {
        var pending = batch.Where(item => !_outcomes.ContainsKey(item.IndexId)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var started = await RunBatchAsync(pending, cancellationToken);
        if (started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (pending.Count == 1)
        {
            await FinalizeAsync(pending[0].IndexId, SgLatencyOutcome.Error);
            return;
        }

        // A large generated core may fail because of one malformed profile or
        // resource pressure. Split the failed batch until the exact bad profile
        // is isolated instead of leaving the whole group in "Testing...".
        var half = Math.Max(1, pending.Count / 2);
        await RunBatchWithFallbackAsync(pending.Take(half).ToList(), cancellationToken);
        await RunBatchWithFallbackAsync(pending.Skip(half).ToList(), cancellationToken);
    }

    private async Task<bool> RunBatchAsync(List<ServerTestItem> selected, CancellationToken cancellationToken)
    {
        ProcessService? processService = null;
        try
        {
            processService = await CoreManager.Instance
                .LoadCoreConfigSpeedtest(selected)
                .WaitAsync(CoreStartTimeout, cancellationToken);
            if (processService is null)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(800), cancellationToken);
            var testable = selected.Where(item => !_outcomes.ContainsKey(item.IndexId)).ToList();
            foreach (var item in testable.Where(item => !item.AllowTest))
            {
                await FinalizeAsync(item.IndexId, SgLatencyOutcome.NotTested);
            }

            var tasks = testable
                .Where(item => item.AllowTest)
                .Select(item => TestProfileAsync(item, cancellationToken))
                .ToArray();
            await Task.WhenAll(tasks);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            Logging.SaveLog("SG latency test core start timeout", ex);
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SG latency test batch failed", ex);
            return false;
        }
        finally
        {
            if (processService is not null)
            {
                try
                {
                    await processService.StopAsync().WaitAsync(CoreStopTimeout);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("SG latency test core stop failed", ex);
                }
            }
        }
    }

    private async Task TestProfileAsync(ServerTestItem item, CancellationToken cancellationToken)
    {
        try
        {
            var webProxy = new WebProxy($"socks5://{Global.Loopback}:{item.Port}");
            var responseTime = await ConnectionHandler
                .GetRealPingTime(webProxy, 10)
                .WaitAsync(ProfileTimeout, cancellationToken);

            if (responseTime <= 0)
            {
                await FinalizeAsync(item.IndexId, SgLatencyOutcome.Unavailable);
                return;
            }

            await FinalizeAsync(item.IndexId, SgLatencyOutcome.Available, responseTime);

            try
            {
                var ipInfo = await ConnectionHandler.GetIPInfo(webProxy).WaitAsync(IpInfoTimeout, cancellationToken);
                var ipText = ipInfo?.ToString() ?? Global.None;
                ProfileExManager.Instance.SetTestIpInfo(item.IndexId, ipText);
                await _update(new SgLatencyTestUpdate(item.IndexId, string.Empty, null, ipText, null, false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Delay result is already final; IP metadata is optional.
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"SG latency IP info failed: {item.IndexId}", ex);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FinalizeAsync(item.IndexId, SgLatencyOutcome.Cancelled);
        }
        catch (TimeoutException)
        {
            await FinalizeAsync(item.IndexId, SgLatencyOutcome.Unavailable);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"SG latency profile failed: {item.IndexId}", ex);
            await FinalizeAsync(item.IndexId, SgLatencyOutcome.Error);
        }
    }

    private async Task FinalizeRemainingAsync(
        IReadOnlyCollection<ProfileItemModel> selectedProfiles,
        SgLatencyOutcome fallback)
    {
        foreach (var profile in selectedProfiles)
        {
            if (!_outcomes.ContainsKey(profile.IndexId))
            {
                await FinalizeAsync(profile.IndexId, fallback);
            }
        }
    }

    private async Task FinalizeAsync(
        string indexId,
        SgLatencyOutcome outcome,
        int? delayMilliseconds = null)
    {
        if (indexId.IsNullOrEmpty() || !_outcomes.TryAdd(indexId, outcome))
        {
            return;
        }

        var persistedDelay = outcome switch
        {
            SgLatencyOutcome.Available => Math.Max(1, delayMilliseconds.GetValueOrDefault()),
            SgLatencyOutcome.Unavailable => UnavailableDelay,
            SgLatencyOutcome.Error => ErrorDelay,
            SgLatencyOutcome.Cancelled => CancelledDelay,
            _ => NotTestedDelay,
        };
        ProfileExManager.Instance.SetTestDelay(indexId, persistedDelay);

        var display = GetDelayDisplay(persistedDelay);
        await _update(new SgLatencyTestUpdate(
            indexId,
            display,
            outcome == SgLatencyOutcome.Available ? persistedDelay : null,
            null,
            outcome,
            true));
    }

    public static string GetDelayDisplay(int delay) => delay switch
    {
        > 0 => $"{delay} ms",
        UnavailableDelay => UnavailableText,
        ErrorDelay => ErrorText,
        CancelledDelay => CancelledText,
        NotTestedDelay => NotTestedText,
        _ => string.Empty,
    };

    private SgLatencyTestSummary BuildSummary(int total)
    {
        var values = _outcomes.Values.ToArray();
        return new SgLatencyTestSummary(
            total,
            values.Count(value => value == SgLatencyOutcome.Available),
            values.Count(value => value == SgLatencyOutcome.Unavailable),
            values.Count(value => value == SgLatencyOutcome.Error),
            values.Count(value => value == SgLatencyOutcome.Cancelled),
            values.Count(value => value == SgLatencyOutcome.NotTested));
    }
}

public enum SgLatencyOutcome
{
    Available,
    Unavailable,
    Error,
    Cancelled,
    NotTested,
}

public sealed record SgLatencyTestUpdate(
    string IndexId,
    string DelayDisplay,
    int? DelayMilliseconds,
    string? IpInfo,
    SgLatencyOutcome? Outcome,
    bool IsTerminal);

public sealed record SgLatencyTestSummary(
    int Total,
    int Available,
    int Unavailable,
    int Errors,
    int Cancelled,
    int NotTested)
{
    public bool WasCancelled => Cancelled > 0;
    public int Finalized => Available + Unavailable + Errors + Cancelled + NotTested;
    public int Tested => Available + Unavailable + Errors;
}
