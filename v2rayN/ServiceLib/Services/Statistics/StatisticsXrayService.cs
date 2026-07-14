namespace ServiceLib.Services.Statistics;

public class StatisticsXrayService
{
    private const long LinkBase = 1024;
    private readonly Config _config;
    private readonly Func<ServerSpeedItem, Task>? _updateFunc;
    private CounterSnapshot _lastCounters = new();
    private CounterSource _lastSource = CounterSource.OutboundProxy;
    private bool _hasBaseline;
    private bool _exitFlag;
    private string Url => $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";

    public StatisticsXrayService(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
        _exitFlag = false;

        _ = Task.Run(Run);
    }

    public void Close()
    {
        _exitFlag = true;
    }

    private async Task Run()
    {
        while (!_exitFlag)
        {
            await Task.Delay(1000);
            try
            {
                if (AppManager.Instance.RunningCoreType != ECoreType.Xray)
                {
                    continue;
                }

                var result = await HttpClientHelper.Instance.TryGetAsync(Url);
                if (result != null)
                {
                    var server = ParseOutput(result) ?? new ServerSpeedItem();
                    await _updateFunc?.Invoke(server);
                }
            }
            catch
            {
                // Traffic statistics must never interrupt VPN operation.
            }
        }
    }

    private ServerSpeedItem? ParseOutput(string result)
    {
        try
        {
            var source = JsonUtils.Deserialize<V2rayMetricsVars>(result);
            if (source?.stats == null)
            {
                return null;
            }

            var counters = ReadCounters(source.stats);
            var sourceKind = ShouldUseTunInbound(counters)
                ? CounterSource.TunInbound
                : CounterSource.OutboundProxy;

            // SG_XRAY_TUN_INBOUND_TRAFFIC_COUNTER
            // Xray's outbound proxy counter is not a dependable user-traffic
            // counter for every transport path. In global TUN mode we use the
            // TUN inbound bytes and subtract explicit Direct bytes. This counts
            // real TCP and UDP/QUIC payload, including YouTube/googlevideo,
            // while still excluding traffic intentionally routed Direct.
            var selectedUp = sourceKind == CounterSource.TunInbound
                ? Math.Max(0, counters.TunUp - counters.DirectUp)
                : counters.ProxyUp;
            var selectedDown = sourceKind == CounterSource.TunInbound
                ? Math.Max(0, counters.TunDown - counters.DirectDown)
                : counters.ProxyDown;

            var previousUp = _lastSource == CounterSource.TunInbound
                ? Math.Max(0, _lastCounters.TunUp - _lastCounters.DirectUp)
                : _lastCounters.ProxyUp;
            var previousDown = _lastSource == CounterSource.TunInbound
                ? Math.Max(0, _lastCounters.TunDown - _lastCounters.DirectDown)
                : _lastCounters.ProxyDown;

            if (_hasBaseline
                && (sourceKind != _lastSource
                    || selectedUp < previousUp
                    || selectedDown < previousDown
                    || counters.DirectUp < _lastCounters.DirectUp
                    || counters.DirectDown < _lastCounters.DirectDown))
            {
                _lastCounters = counters;
                _lastSource = sourceKind;
                return null;
            }

            var current = new ServerSpeedItem
            {
                ProxyUp = ToUnits(selectedUp - previousUp),
                ProxyDown = ToUnits(selectedDown - previousDown),
                DirectUp = ToUnits(counters.DirectUp - _lastCounters.DirectUp),
                DirectDown = ToUnits(counters.DirectDown - _lastCounters.DirectDown),
            };

            _lastCounters = counters;
            _lastSource = sourceKind;
            _hasBaseline = true;
            return current;
        }
        catch
        {
            // Traffic statistics must never interrupt VPN operation.
        }

        return null;
    }

    private bool ShouldUseTunInbound(CounterSnapshot counters)
    {
        if (!counters.HasTun || !_config.TunModeItem.EnableTun)
        {
            return false;
        }

        var routing = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        return routing.Preset == SgSmartRoutingHelper.PresetGlobal;
    }

    private static CounterSnapshot ReadCounters(V2rayMetricsVarsStats stats)
    {
        var result = new CounterSnapshot();

        if (stats.outbound != null)
        {
            foreach (var key in stats.outbound.Keys.Cast<string>())
            {
                var state = ReadLink(stats.outbound[key]);
                if (state == null)
                {
                    continue;
                }

                if (key.StartsWith(Global.ProxyTag, StringComparison.Ordinal))
                {
                    result.ProxyUp += state.uplink;
                    result.ProxyDown += state.downlink;
                }
                else if (string.Equals(key, Global.DirectTag, StringComparison.Ordinal))
                {
                    result.DirectUp += state.uplink;
                    result.DirectDown += state.downlink;
                }
            }
        }

        if (stats.inbound != null)
        {
            foreach (var key in stats.inbound.Keys.Cast<string>())
            {
                if (!string.Equals(key, "tun", StringComparison.Ordinal))
                {
                    continue;
                }

                var state = ReadLink(stats.inbound[key]);
                if (state == null)
                {
                    continue;
                }

                result.TunUp += state.uplink;
                result.TunDown += state.downlink;
                result.HasTun = true;
            }
        }

        return result;
    }

    private static V2rayMetricsVarsLink? ReadLink(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return JsonUtils.Deserialize<V2rayMetricsVarsLink>(value.ToString());
    }

    private static long ToUnits(long bytes)
    {
        return Math.Max(0, bytes) / LinkBase;
    }

    private enum CounterSource
    {
        OutboundProxy,
        TunInbound,
    }

    private sealed class CounterSnapshot
    {
        public long ProxyUp { get; set; }
        public long ProxyDown { get; set; }
        public long DirectUp { get; set; }
        public long DirectDown { get; set; }
        public long TunUp { get; set; }
        public long TunDown { get; set; }
        public bool HasTun { get; set; }
    }
}
