namespace ServiceLib.Services.Statistics;

/// <summary>
/// Reads the Windows virtual TUN adapter counters directly. Unlike Xray's
/// Stats API, the adapter counters include TCP, UDP and QUIC traffic, so large
/// HTTP/3 flows such as YouTube/googlevideo are not lost.
///
/// The counter is authoritative only for the global TUN preset. In split
/// routing modes the adapter also contains traffic intentionally sent Direct,
/// so profile attribution continues to use the core's proxy counters there.
/// </summary>
public sealed class StatisticsTunInterfaceService
{
    private const int PollIntervalMilliseconds = 1000;

    private readonly Config _config;
    private readonly Func<ServerSpeedItem, Task>? _updateFunc;
    private readonly object _statusSync = new();

    private bool _exitFlag;
    private string _baselineKey = string.Empty;
    private long _lastBytesSent = -1;
    private long _lastBytesReceived = -1;
    private long _sentRemainder;
    private long _receivedRemainder;
    private string _lastLogState = string.Empty;
    private SgTunInterfaceCounterStatus _status = new();

    public StatisticsTunInterfaceService(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
        _ = Task.Run(RunAsync);
    }

    public bool IsActive
    {
        get
        {
            lock (_statusSync)
            {
                return _status.IsActive;
            }
        }
    }

    public SgTunInterfaceCounterStatus GetStatus()
    {
        lock (_statusSync)
        {
            return new SgTunInterfaceCounterStatus
            {
                IsActive = _status.IsActive,
                InterfaceName = _status.InterfaceName,
                InterfaceDescription = _status.InterfaceDescription,
                BytesSent = _status.BytesSent,
                BytesReceived = _status.BytesReceived,
                LastUpdatedUtc = _status.LastUpdatedUtc,
                StatusMessage = _status.StatusMessage,
            };
        }
    }

    public void Close()
    {
        _exitFlag = true;
        SetInactive("Счётчик TUN остановлен");
    }

    public static bool ShouldUseAsProfileCounter(Config config)
    {
        if (!OperatingSystem.IsWindows()
            || !config.TunModeItem.EnableTun
            || !string.Equals(
                config.SgQuickSettingsItem.ConnectionMode,
                "tun",
                StringComparison.OrdinalIgnoreCase)
            || AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty())
        {
            return false;
        }

        if (!AppManager.Instance.IsRunningCore(ECoreType.Xray)
            && !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            return false;
        }

        var routing = SgSmartRoutingHelper.Normalize(config.SgQuickSettingsItem);
        return routing.Preset == SgSmartRoutingHelper.PresetGlobal;
    }

    private async Task RunAsync()
    {
        while (!_exitFlag)
        {
            await Task.Delay(PollIntervalMilliseconds);

            try
            {
                if (!ShouldUseAsProfileCounter(_config))
                {
                    ResetBaseline();
                    SetInactive("Доступен в TUN · Весь интернет");
                    continue;
                }

                var expectedName = ResolveExpectedInterfaceName();
                if (expectedName.IsNullOrEmpty())
                {
                    ResetBaseline();
                    SetInactive("Ядро TUN ещё не запущено");
                    continue;
                }

                var networkInterface = FindTunInterface(expectedName);
                if (networkInterface == null)
                {
                    ResetBaseline();
                    SetInactive($"Ожидание интерфейса {expectedName}");
                    LogStateOnce($"missing:{expectedName}",
                        $"SG TUN traffic counter: interface '{expectedName}' is not available yet");
                    continue;
                }

                var statistics = networkInterface.GetIPv4Statistics();
                var bytesSent = Math.Max(0, statistics.BytesSent);
                var bytesReceived = Math.Max(0, statistics.BytesReceived);
                var baselineKey = string.Join("|",
                    networkInterface.Id,
                    _config.IndexId ?? string.Empty,
                    AppManager.Instance.RunningCoreType.ToString());

                if (!string.Equals(_baselineKey, baselineKey, StringComparison.Ordinal)
                    || _lastBytesSent < 0
                    || _lastBytesReceived < 0
                    || bytesSent < _lastBytesSent
                    || bytesReceived < _lastBytesReceived)
                {
                    _baselineKey = baselineKey;
                    _lastBytesSent = bytesSent;
                    _lastBytesReceived = bytesReceived;
                    _sentRemainder = 0;
                    _receivedRemainder = 0;
                    SetActive(networkInterface, bytesSent, bytesReceived,
                        "WinTUN счётчик активен");
                    LogStateOnce($"active:{networkInterface.Id}",
                        $"SG TUN traffic counter active: name={networkInterface.Name}; description={networkInterface.Description}; id={networkInterface.Id}");
                    continue;
                }

                var sentDelta = Math.Max(0, bytesSent - _lastBytesSent);
                var receivedDelta = Math.Max(0, bytesReceived - _lastBytesReceived);
                _lastBytesSent = bytesSent;
                _lastBytesReceived = bytesReceived;

                SetActive(networkInterface, bytesSent, bytesReceived,
                    "WinTUN счётчик активен");

                var divisor = AppManager.Instance.IsRunningCore(ECoreType.sing_box)
                    ? 1000L
                    : 1024L;

                _sentRemainder = SaturatingAdd(_sentRemainder, sentDelta);
                _receivedRemainder = SaturatingAdd(_receivedRemainder, receivedDelta);

                var uploadUnits = _sentRemainder / divisor;
                var downloadUnits = _receivedRemainder / divisor;
                _sentRemainder %= divisor;
                _receivedRemainder %= divisor;

                if (uploadUnits == 0 && downloadUnits == 0)
                {
                    continue;
                }

                await _updateFunc?.Invoke(new ServerSpeedItem
                {
                    ProxyUp = uploadUnits,
                    ProxyDown = downloadUnits,
                    IsTunInterfaceTraffic = true,
                    TrafficInterfaceName = networkInterface.Name,
                });
            }
            catch (Exception ex)
            {
                ResetBaseline();
                SetInactive("Не удалось прочитать счётчик TUN");
                LogStateOnce($"error:{ex.GetType().Name}:{ex.Message}",
                    $"SG TUN traffic counter error: {ex.Message}");
            }
        }
    }

    private static NetworkInterface? FindTunInterface(string expectedName)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        var named = interfaces
            .Where(item =>
                string.Equals(item.Name, expectedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Description, expectedName, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(expectedName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .FirstOrDefault();
        if (named != null)
        {
            return named;
        }

        // The explicit adapter alias is normally preserved, but Windows may
        // append a suffix after a driver reinstall. The SG TUN address is a
        // safe fallback because both generated configs use 172.18.0.1/30.
        return interfaces
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .FirstOrDefault(item =>
            {
                try
                {
                    return item.GetIPProperties().UnicastAddresses.Any(address =>
                        string.Equals(
                            address.Address.ToString(),
                            "172.18.0.1",
                            StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            });
    }

    private static string ResolveExpectedInterfaceName()
    {
        if (AppManager.Instance.IsRunningCore(ECoreType.Xray))
        {
            return "xray_tun";
        }

        if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            return "singbox_tun";
        }

        return string.Empty;
    }

    private void ResetBaseline()
    {
        _baselineKey = string.Empty;
        _lastBytesSent = -1;
        _lastBytesReceived = -1;
        _sentRemainder = 0;
        _receivedRemainder = 0;
    }

    private void SetActive(
        NetworkInterface networkInterface,
        long bytesSent,
        long bytesReceived,
        string message)
    {
        lock (_statusSync)
        {
            _status = new SgTunInterfaceCounterStatus
            {
                IsActive = true,
                InterfaceName = networkInterface.Name,
                InterfaceDescription = networkInterface.Description,
                BytesSent = bytesSent,
                BytesReceived = bytesReceived,
                LastUpdatedUtc = DateTime.UtcNow,
                StatusMessage = message,
            };
        }
    }

    private void SetInactive(string message)
    {
        lock (_statusSync)
        {
            _status = new SgTunInterfaceCounterStatus
            {
                IsActive = false,
                InterfaceName = _status.InterfaceName,
                InterfaceDescription = _status.InterfaceDescription,
                BytesSent = _status.BytesSent,
                BytesReceived = _status.BytesReceived,
                LastUpdatedUtc = _status.LastUpdatedUtc,
                StatusMessage = message,
            };
        }
    }

    private void LogStateOnce(string state, string message)
    {
        if (string.Equals(_lastLogState, state, StringComparison.Ordinal))
        {
            return;
        }

        _lastLogState = state;
        Logging.SaveLog(message);
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0)
        {
            return Math.Max(0, left);
        }

        return long.MaxValue - left < right ? long.MaxValue : left + right;
    }
}

public sealed class SgTunInterfaceCounterStatus
{
    public bool IsActive { get; init; }
    public string InterfaceName { get; init; } = string.Empty;
    public string InterfaceDescription { get; init; } = string.Empty;
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }
    public DateTime LastUpdatedUtc { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}
