using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ServiceLib.Helper;
using ServiceLib.Services;
using v2rayN.Services;

namespace v2rayN.Views;

public partial class SgConnectionsWindow
{
    private const int MaxDisplayedRows = 1500;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FileReadTimeout = TimeSpan.FromSeconds(3);

    private static readonly Regex XrayAccessRegex = new(
        @"^(?<time>\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s+(?:(?:from\s+)?(?<source>\S+)\s+)?accepted\s+(?<target>.+?)\s+\[(?:(?<inbound>.+?)\s+(?:->|>>)\s+)?(?<outbound>[^\]]+?)\](?:\s+.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex XrayTraceLineRegex = new(
        @"^(?<time>\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s+\[(?:Info|Debug)\]\s+\[(?<id>\d+)\]\s+(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex XrayTraceDestinationRegex = new(
        @"\b(?:processing\s+from\s+.+?\s+to|Connect request to|dispatch request to:)\s+(?<network>tcp|udp):(?<destination>\[[^\]]+\](?::\d+)?|\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex XraySniffedDomainRegex = new(
        @"\bapp/dispatcher:\s+sniffed domain:\s+(?<domain>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex XrayDetourRegex = new(
        @"\bapp/dispatcher:\s+taking detour \[(?<outbound>[^\]]+)\]\s+for\s+\[(?<network>tcp|udp):(?<destination>[^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex XrayDnsAnswerRegex = new(
        @"^(?<time>\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?).*?\b(?:got answer|cache HIT):?\s+(?<domain>[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)+)\.?\s+(?:TypeA(?:AAA)?\s+)?->\s+\[(?<addresses>[^\]]*)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly List<SgConnectionRow> _allRows = [];
    private readonly List<SgConnectionRow> _visibleRows = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Dictionary<string, (DateTimeOffset CachedAt, SgRouteTestResult Result)> _routeDecisionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SingBoxHistoryEntry> _singBoxHistory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RouteDecisionCacheLifetime = TimeSpan.FromMinutes(5);

    private DateTimeOffset _xrayIgnoreBefore = DateTimeOffset.MinValue;
    private DateTimeOffset _xraySessionStartedAt = DateTimeOffset.Now;
    private bool _isSingBoxMode;
    private bool _isXrayMode;
    private bool _supportsConnectionList;
    private bool _disposed;
    private string _emptyMessage = "Активные соединения пока не обнаружены.";
    private string _routeFilter = "Все выходы";
    private DiagnosticsPromptAction _diagnosticsPromptAction;
    private bool _diagnosticsEnableRunning;
    private bool _vpnCheckHasRun;

    private enum DiagnosticsPromptAction
    {
        None,
        EnableAndReconnect,
        ReconnectOnly,
    }

    public SgConnectionsWindow()
    {
        InitializeComponent();
        mainTabs.SelectedIndex = 0;
        SgWindowSizing.AttachConnections(this);
        SgConnectionsDiagnosticsService.CleanupExpiredXrayLogs();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += (_, _) => QueueRefresh(force: false);

        btnTrafficStatistics.Click += (_, _) =>
        {
            var window = new SgTrafficProfilesWindow { Owner = this };
            window.ShowDialog();
        };
        btnWindowMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        btnWindowClose.Click += (_, _) => Close();
        btnRefresh.Click += (_, _) => QueueRefresh(force: true);
        btnRouteFilter.Click += BtnRouteFilter_Click;
        btnUnsupportedRefresh.Click += async (_, _) =>
        {
            if (_diagnosticsPromptAction != DiagnosticsPromptAction.None)
            {
                await ApplyConnectionsDiagnosticsAsync();
            }
            else
            {
                QueueRefresh(force: true);
            }
        };
        btnUnsupportedSecondary.Click += (_, _) => Close();
        btnClear.Click += (_, _) => ClearRows();
        btnExportCsv.Click += (_, _) => ExportCsv();
        btnExportJson.Click += (_, _) => ExportJson();
        btnCloseSelected.Click += (_, _) => QueueCloseSelected();
        btnCloseAll.Click += (_, _) => QueueCloseAll();
        btnCloseWindow.Click += (_, _) => Close();
        btnRouteTest.Click += (_, _) => QueueRouteTest();
        btnQuickVpnCheck.Click += (_, _) =>
        {
            mainTabs.SelectedIndex = 2;
            QueueVpnCheck();
        };
        btnVpnCheck.Click += (_, _) => QueueVpnCheck();
        btnOpenDnsRoute.Click += (_, _) => OpenDnsRouteWindow();
        txtRouteInput.KeyDown += RouteInput_KeyDown;
        txtSearch.TextChanged += Search_TextChanged;
        btnClearSearch.Click += (_, _) => ClearSearch();
        chkAutoRefresh.Checked += (_, _) => UpdateTimerState();
        chkAutoRefresh.Unchecked += (_, _) => UpdateTimerState();
        chkIncludeHistory.Checked += (_, _) => QueueRefresh(force: true);
        chkIncludeHistory.Unchecked += (_, _) => QueueRefresh(force: true);
        PreviewKeyDown += Window_PreviewKeyDown;

        Loaded += (_, _) =>
        {
            mainTabs.SelectedIndex = 0;
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    QueueRuntimeSummaryRefresh();
                    QueueRefresh(force: true);
                }));
        };
        Closing += (_, _) =>
        {
            _refreshTimer.Stop();
            _lifetimeCts.Cancel();
        };
        Closed += (_, _) =>
        {
            _disposed = true;
            _refreshTimer.Stop();
        };
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(txtSearch.Text);
        txtSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        btnClearSearch.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilters();
    }

    private void ClearSearch()
    {
        txtSearch.Clear();
        txtSearch.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.F)
        {
            mainTabs.SelectedIndex = 0;
            txtSearch.Focus();
            txtSearch.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (routeFilterPopup.IsOpen)
        {
            routeFilterPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(txtSearch.Text))
        {
            ClearSearch();
            e.Handled = true;
            return;
        }

        Close();
        e.Handled = true;
    }

    private void BtnRouteFilter_Click(object sender, RoutedEventArgs e)
    {
        routeFilterPopup.IsOpen = !routeFilterPopup.IsOpen;
    }

    private void RouteFilterOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton selectedOption)
        {
            return;
        }

        _routeFilter = selectedOption.Tag?.ToString() ?? "Все выходы";
        txtRouteFilter.Text = _routeFilter;
        selectedOption.IsChecked = true;
        routeFilterPopup.IsOpen = false;
        ApplyFilters();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // A close or mouse release can race with DragMove. The window must remain responsive.
        }
    }

    private void QueueRefresh(bool force)
    {
        if (_disposed || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _ = RefreshAsync(force, _lifetimeCts.Token);
    }

    private async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            entered = await _refreshGate.WaitAsync(0, cancellationToken);
            if (!entered || _disposed)
            {
                return;
            }

            SetBusy(true);
            if (force)
            {
                _routeDecisionCache.Clear();
            }
            await RefreshRuntimeSummaryAsync(cancellationToken);

            if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
            {
                await RefreshSingBoxAsync(cancellationToken);
                return;
            }

            if (AppManager.Instance.IsRunningCore(ECoreType.Xray))
            {
                await RefreshXrayAsync(cancellationToken);
                return;
            }

            if (AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty())
            {
                ReplaceRows([]);
                await RefreshAmneziaWgAsync(cancellationToken);
                return;
            }

            SetUnsupportedMode(
                "Нет активного ядра",
                "Подключите TUN, системный или локальный прокси.",
                "После подключения нажмите «Проверить снова». Для sing-box появятся активные соединения, для Xray — недавние записи access log.");
            ReplaceRows([]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the window cancels all pending work immediately.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgConnectionsWindow.Refresh", ex);
            if (!_disposed)
            {
                txtHint.Text = $"Не удалось обновить список: {ex.Message}";
            }
        }
        finally
        {
            if (entered)
            {
                _refreshGate.Release();
            }

            if (!_disposed && !_lifetimeCts.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private async Task RefreshSingBoxAsync(CancellationToken cancellationToken)
    {
        _isSingBoxMode = true;
        _isXrayMode = false;
        chkIncludeHistory.Visibility = Visibility.Visible;
        btnClear.Content = "Очистить список";
        SetSupportedMode(
            "sing-box · активные соединения",
            "Точное правило, payload, процесс, трафик и цепочка берутся напрямую из Clash API.",
            canClose: true,
            emptyMessage: "Активных соединений sing-box пока нет.");

        ClashConnections? response;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(ApiTimeout);
            try
            {
                response = await ClashApiManager.Instance.GetClashConnectionsAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                txtHint.Text = "Clash API не ответил за 3 секунды. Интерфейс не заблокирован; повторите обновление позже.";
                ReplaceRows([]);
                return;
            }
        }

        if (response?.connections == null)
        {
            txtHint.Text = "Clash API пока не ответил. Подождите запуска ядра или нажмите «Обновить».";
            ReplaceRows([]);
            return;
        }

        var sourceItems = response.connections;
        var now = DateTimeOffset.Now;
        var rows = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sourceItems.Select(item =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = item.metadata ?? new MetadataItem();
                var host = BuildHost(metadata.host, metadata.destinationIP, metadata.destinationPort);
                var chain = item.chains is { Count: > 0 }
                    ? string.Join(" → ", item.chains)
                    : "—";
                var outbound = item.chains?.FirstOrDefault() ?? "—";
                var started = new DateTimeOffset(item.start);
                var elapsed = now - started;
                if (elapsed < TimeSpan.Zero)
                {
                    elapsed = TimeSpan.Zero;
                }

                return new SgConnectionRow
                {
                    Id = item.id ?? string.Empty,
                    Host = host,
                    DestinationIp = metadata.destinationIP ?? string.Empty,
                    DestinationDisplay = !string.IsNullOrWhiteSpace(metadata.host)
                        ? metadata.host.Trim()
                        : (!string.IsNullOrWhiteSpace(metadata.destinationIP) ? metadata.destinationIP.Trim() : "—"),
                    DomainName = IsDomainName(metadata.host ?? string.Empty)
                        ? NormalizeDomain(metadata.host ?? string.Empty)
                        : string.Empty,
                    DomainSource = IsDomainName(metadata.host ?? string.Empty)
                        ? "sing-box Clash API"
                        : string.Empty,
                    PortDisplay = FormatPort(metadata.destinationPort),
                    Source = "sing-box Clash API",
                    RuleDisplay = BuildRule(item.rule, item.rulePayload),
                    RuleName = item.rule ?? string.Empty,
                    RulePayload = item.rulePayload ?? string.Empty,
                    OutboundDisplay = NormalizeOutbound(outbound),
                    OutboundTag = outbound,
                    TunnelDisplay = chain,
                    ProtocolDisplay = BuildProtocol(metadata.network, metadata.type),
                    ProcessDisplay = BuildProcess(metadata.process, metadata.processPath),
                    DownloadBytes = item.download,
                    UploadBytes = item.upload,
                    DownloadDisplay = FormatBytes(item.download),
                    UploadDisplay = FormatBytes(item.upload),
                    ElapsedDisplay = FormatElapsed(elapsed),
                    StartedAt = started,
                    RouteKind = DetectRouteKind(outbound, chain, item.rule),
                    CanClose = !string.IsNullOrWhiteSpace(item.id),
                    IsLive = true
                };
            })
            .OrderByDescending(x => x.DownloadBytes + x.UploadBytes)
            .Take(MaxDisplayedRows)
            .ToList();
        }, cancellationToken);

        var displayRows = MergeSingBoxHistory(rows, now, chkIncludeHistory.IsChecked != true);
        ReplaceRows(displayRows);
        await EnrichCountriesAsync(displayRows, cancellationToken);
    }

    private List<SgConnectionRow> MergeSingBoxHistory(
        IReadOnlyCollection<SgConnectionRow> liveRows,
        DateTimeOffset now,
        bool includeHistory)
    {
        var cutoff = now - TimeSpan.FromMinutes(15);
        foreach (var expired in _singBoxHistory
                     .Where(pair => pair.Value.LastSeen < cutoff)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _singBoxHistory.Remove(expired);
        }

        var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in liveRows)
        {
            var key = GetSingBoxHistoryKey(row);
            liveKeys.Add(key);
            _singBoxHistory[key] = new SingBoxHistoryEntry(row, now);
        }

        if (!includeHistory)
        {
            return liveRows.ToList();
        }

        var result = liveRows.ToList();
        result.AddRange(_singBoxHistory
            .Where(pair => !liveKeys.Contains(pair.Key) && pair.Value.LastSeen >= cutoff)
            .OrderByDescending(pair => pair.Value.LastSeen)
            .Select(pair => CloneAsHistorical(pair.Value.Row, pair.Value.LastSeen)));
        return result
            .OrderByDescending(row => row.IsLive)
            .ThenByDescending(row => row.StartedAt)
            .Take(MaxDisplayedRows)
            .ToList();
    }

    private static string GetSingBoxHistoryKey(SgConnectionRow row)
    {
        return row.Id.IsNotEmpty()
            ? row.Id
            : string.Join('|', row.DestinationAddressDisplay, row.ProtocolDisplay, row.ProcessDisplay, row.OutboundTag, row.StartedAt.ToUnixTimeMilliseconds());
    }

    private static SgConnectionRow CloneAsHistorical(SgConnectionRow row, DateTimeOffset lastSeen)
    {
        var clone = new SgConnectionRow
        {
            Id = row.Id,
            Host = row.Host,
            DestinationDisplay = row.DestinationDisplay,
            DestinationIp = row.DestinationIp,
            DomainName = row.DomainName,
            DomainSource = row.DomainSource,
            PortDisplay = row.PortDisplay,
            Source = row.Source,
            RuleDisplay = row.RuleDisplay,
            RuleName = row.RuleName,
            RulePayload = row.RulePayload,
            OutboundDisplay = row.OutboundDisplay,
            OutboundTag = row.OutboundTag,
            TunnelDisplay = row.TunnelDisplay,
            ProtocolDisplay = row.ProtocolDisplay,
            ProcessDisplay = row.ProcessDisplay,
            DownloadBytes = row.DownloadBytes,
            UploadBytes = row.UploadBytes,
            DownloadDisplay = row.DownloadDisplay,
            UploadDisplay = row.UploadDisplay,
            ElapsedDisplay = $"завершено · {FormatLastSeen(lastSeen)}",
            StartedAt = row.StartedAt,
            FirstSeenAt = row.FirstSeenAt,
            HitCount = row.HitCount,
            RouteKind = row.RouteKind,
            CanClose = false,
            IsLive = false,
        };
        clone.CountryCode = row.CountryCode;
        return clone;
    }

    private async Task RefreshXrayAsync(CancellationToken cancellationToken)
    {
        _isSingBoxMode = false;
        _isXrayMode = true;

        var diagnostics = SgConnectionsDiagnosticsService.GetState(AppManager.Instance.Config);
        if (!diagnostics.SettingsEnabled)
        {
            SetDiagnosticsPromptMode(DiagnosticsPromptAction.EnableAndReconnect);
            ReplaceRows([]);
            return;
        }

        if (!diagnostics.ActiveConfigEnabled)
        {
            SetDiagnosticsPromptMode(DiagnosticsPromptAction.ReconnectOnly);
            ReplaceRows([]);
            return;
        }

        var historyEnabled = chkIncludeHistory.IsChecked != true;
        SetSupportedMode(
            "Xray · диагностика активна",
            historyEnabled
                ? "Локальный журнал · история 15 минут · содержимое трафика не записывается."
                : "Локальный журнал · текущий тест · содержимое трафика не записывается.",
            canClose: false,
            emptyMessage: historyEnabled
                ? "В локальном журнале пока нет назначений за последние 15 минут."
                : "Откройте нужный сайт: здесь появятся назначения текущего теста.");

        chkIncludeHistory.Visibility = Visibility.Visible;
        btnClear.Content = "Новый тест";
        btnClear.ToolTip = "Очистить отображение и учитывать только новые обращения после нажатия";

        // Read the exact files from the config used by the running Xray core.
        // This is the same for TUN, System Proxy and Local Proxy and also works
        // when the core remained active across midnight.
        var accessLogPath = diagnostics.AccessLogPath;
        var errorLogPath = diagnostics.ErrorLogPath;
        if (!File.Exists(accessLogPath))
        {
            txtHint.Text = "Диагностика активна. Откройте сайт — Xray создаст локальный журнал автоматически.";
            _emptyMessage = "Журнал готов. Откройте сайт, чтобы появились первые назначения.";
            ReplaceRows([]);
            return;
        }

        var includeHistory = chkIncludeHistory.IsChecked != true;
        List<SgConnectionRow> rows;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(FileReadTimeout);
            try
            {
                rows = await Task.Run(
                    () => ReadAndParseXrayRows(accessLogPath, errorLogPath, includeHistory, timeoutCts.Token),
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                txtHint.Text = "Access log не удалось прочитать за 3 секунды. Интерфейс не заблокирован.";
                ReplaceRows([]);
                return;
            }
        }

        await EnrichCountriesAsync(rows, cancellationToken);
        await EnrichXrayRouteRulesAsync(rows, cancellationToken);
        ReplaceRows(rows);
    }

    private void SetDiagnosticsPromptMode(DiagnosticsPromptAction action)
    {
        _diagnosticsPromptAction = action;
        _supportsConnectionList = false;
        _isSingBoxMode = false;
        _isXrayMode = true;
        _refreshTimer.Stop();

        var enableSettings = action == DiagnosticsPromptAction.EnableAndReconnect;
        txtEngine.Text = enableSettings
            ? "Xray · диагностика выключена"
            : "Xray · требуется переподключение";
        txtHint.Text = enableSettings
            ? "Для доменов и Output нужен минимальный локальный журнал."
            : "Настройка включена, но запущенный Xray использует старую конфигурацию.";
        txtUnsupportedTitle.Text = enableSettings
            ? "Диагностика соединений выключена"
            : "Переподключите Xray для применения журнала";
        txtUnsupportedDetails.Text = enableSettings
            ? "Чтобы показывать домены, Output VPN / Direct / Block и количество обращений, " +
              "SG Client должен включить минимальный локальный журнал Xray и распознавание доменов.\n\n" +
              "Записываются только технические сведения: домен или IP, порт, TCP/UDP и выбранный Output. " +
              "Содержимое страниц, пароли, ключи и передаваемые данные не записываются. " +
              "Старые диагностические файлы удаляются автоматически."
            : "Журнал уже разрешён в настройках, но текущий процесс Xray был запущен без него " +
              "или до применения HTTP/TLS/QUIC sniffing. SG Client переподключит текущий профиль " +
              "и начнёт читать журнал именно запущенного Xray. Это работает одинаково в TUN, " +
              "System Proxy и Local Proxy.";

        filterPanel.Visibility = Visibility.Collapsed;
        tablePanel.Visibility = Visibility.Collapsed;
        emptyState.Visibility = Visibility.Collapsed;
        unsupportedPanel.Visibility = Visibility.Visible;
        awgDiagnosticsPanel.Visibility = Visibility.Collapsed;
        chkAutoRefresh.Visibility = Visibility.Collapsed;
        chkIncludeHistory.Visibility = Visibility.Collapsed;
        closeButtonsPanel.Visibility = Visibility.Collapsed;
        btnUnsupportedSecondary.Visibility = Visibility.Visible;
        btnUnsupportedRefresh.Width = enableSettings ? 220 : 190;
        btnUnsupportedRefresh.Content = enableSettings
            ? "Включить и переподключить"
            : "Переподключить Xray";
        btnUnsupportedRefresh.IsEnabled = true;
        btnUnsupportedSecondary.IsEnabled = true;
        btnCloseSelected.IsEnabled = false;
        btnCloseAll.IsEnabled = false;
    }

    private async Task ApplyConnectionsDiagnosticsAsync()
    {
        if (_diagnosticsEnableRunning
            || _disposed
            || _diagnosticsPromptAction == DiagnosticsPromptAction.None)
        {
            return;
        }

        var requestedAction = _diagnosticsPromptAction;
        _diagnosticsEnableRunning = true;
        btnUnsupportedRefresh.IsEnabled = false;
        btnUnsupportedSecondary.IsEnabled = false;
        btnUnsupportedRefresh.Content = "Применение…";
        txtHint.Text = requestedAction == DiagnosticsPromptAction.EnableAndReconnect
            ? "Сохраняем настройки и переподключаем текущее ядро…"
            : "Переподключаем текущее ядро с активным журналом…";

        try
        {
            var config = AppManager.Instance.Config;
            if (requestedAction == DiagnosticsPromptAction.EnableAndReconnect
                && !await SgConnectionsDiagnosticsService.EnableAsync(config))
            {
                throw new InvalidOperationException("Не удалось сохранить настройки диагностики.");
            }

            _xraySessionStartedAt = DateTimeOffset.Now;
            _xrayIgnoreBefore = _xraySessionStartedAt;

            if (MainWindowViewModel.Instance != null)
            {
                await MainWindowViewModel.Instance.Reload();
            }
            else
            {
                AppEvents.ReloadRequested.Publish();
                await Task.Delay(1200);
            }

            SgConnectionsDiagnosticsService.DiagnosticsState state =
                SgConnectionsDiagnosticsService.GetState(config);
            for (var attempt = 0; attempt < 10 && !state.ActiveConfigEnabled; attempt++)
            {
                await Task.Delay(200);
                state = SgConnectionsDiagnosticsService.GetState(config);
            }

            if (!state.ActiveConfigEnabled)
            {
                throw new InvalidOperationException(
                    "Xray переподключён, но активный config.json всё ещё не содержит журнал соединений.");
            }

            _diagnosticsPromptAction = DiagnosticsPromptAction.None;
            QueueRefresh(force: true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Apply SG Connections diagnostics", ex);
            txtHint.Text = ex.Message.IsNotEmpty()
                ? ex.Message
                : "Не удалось применить диагностику соединений.";
            btnUnsupportedRefresh.Content = requestedAction == DiagnosticsPromptAction.EnableAndReconnect
                ? "Повторить включение"
                : "Повторить переподключение";
        }
        finally
        {
            _diagnosticsEnableRunning = false;
            if (!_disposed)
            {
                btnUnsupportedRefresh.IsEnabled = true;
                btnUnsupportedSecondary.IsEnabled = true;
            }
        }
    }

    private async Task RefreshAmneziaWgAsync(CancellationToken cancellationToken)
    {
        var profileId = AmneziaWgManager.Instance.ActiveProfileId
            ?? AmneziaWgManager.Instance.SelectedProfileId;
        var profile = AmneziaWgManager.Instance.GetProfile(profileId);
        if (profile == null)
        {
            SetUnsupportedMode(
                "AmneziaWG",
                "Профиль активного туннеля не найден.",
                "Обновите список или переподключите профиль AmneziaWG.");
            return;
        }

        var status = await AmneziaWgManager.Instance.QueryStatusAsync(profile);
        cancellationToken.ThrowIfCancellationRequested();
        var traffic = await AmneziaWgManager.Instance.GetActiveTrafficTotalsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        SetUnsupportedMode(
            "AmneziaWG · состояние туннеля",
            "Ядро показывает состояние туннеля целиком, а не отдельные сайты и соединения.",
            "AmneziaWG не предоставляет список сайтов, процессы или сработавшие правила. Ниже показаны только фактические данные туннеля.");

        awgDiagnosticsPanel.Visibility = Visibility.Visible;
        txtAwgState.Text = status.State switch
        {
            "connected" => "Подключён",
            "connecting" => "Подключается",
            "disconnected" => "Отключён",
            "error" => "Ошибка",
            _ => status.Message.IsNotEmpty() ? status.Message : status.State,
        };
        txtAwgEndpoint.Text = profile.Endpoint.IsNotEmpty() ? profile.Endpoint : "—";
        txtAwgEndpoint.ToolTip = profile.Endpoint;
        txtAwgPort.Text = profile.EndpointPort > 0 ? $"UDP {profile.EndpointPort}" : "—";
        txtAwgHandshake.Text = status.LastHandshake is DateTime handshake
            ? handshake.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
            : "Ещё не получен";
        txtAwgRx.Text = traffic is { } totals ? FormatBytes((ulong)Math.Max(0, totals.ReceivedBytes)) : "—";
        txtAwgTx.Text = traffic is { } totals2 ? FormatBytes((ulong)Math.Max(0, totals2.SentBytes)) : "—";
        txtAwgUptime.Text = "Ядро не сообщает";
        var masking = ReadAwgMasking(profile.ConfigPath);
        txtAwgMasking.Text = masking.Short;
        txtAwgMasking.ToolTip = masking.Full;
        txtCount.Text = status.State == "connected" ? "Туннель подключён" : "Туннель";

        // Keep diagnostics fresh even though there is no per-connection table.
        if (!_disposed && chkAutoRefresh.IsChecked == true)
        {
            _refreshTimer.Start();
        }
    }

    private static (string Short, string Full) ReadAwgMasking(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return ("Не удалось прочитать", "Файл конфигурации не найден.");
            }

            var names = new HashSet<string>(
                ["Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4"],
                StringComparer.OrdinalIgnoreCase);
            var values = new List<string>();
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
                {
                    continue;
                }
                var separator = line.IndexOf('=');
                var key = line[..separator].Trim();
                if (!names.Contains(key))
                {
                    continue;
                }
                values.Add($"{key}={line[(separator + 1)..].Trim()}");
            }

            if (values.Count == 0)
            {
                return ("Не указана", "Параметры Jc/Jmin/Jmax/S1–S4/H1–H4 отсутствуют.");
            }

            var full = string.Join(" · ", values);
            var shortValue = values.Count <= 3
                ? full
                : $"Встроена · {values.Count} параметров";
            return (shortValue, full);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read AmneziaWG masking parameters", ex);
            return ("Ошибка чтения", ex.Message);
        }
    }

    private async Task EnrichCountriesAsync(IReadOnlyList<SgConnectionRow> rows, CancellationToken cancellationToken)
    {
        var addresses = rows
            .Select(row => row.DestinationIp?.Trim() ?? string.Empty)
            .Where(address => IPAddress.TryParse(address, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (addresses.Length == 0 || _disposed)
        {
            return;
        }

        using var limiter = new SemaphoreSlim(12, 12);
        var tasks = addresses.Select(async address =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                var code = await SgGeoIpCountryService.Instance.ResolveAddressAsync(address, cancellationToken);
                return (Address: address, Code: SgCountryHelper.NormalizeCode(code));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"Connections GeoIP lookup failed: {address}", ex);
                return (Address: address, Code: string.Empty);
            }
            finally
            {
                limiter.Release();
            }
        }).ToArray();

        var resolved = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        var countryByAddress = resolved
            .Where(item => item.Code.Length == 2)
            .ToDictionary(item => item.Address, item => item.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (countryByAddress.TryGetValue(row.DestinationIp, out var code))
            {
                row.CountryCode = code;
            }
        }

    }

    private List<SgConnectionRow> ReadAndParseXrayRows(
        string accessLogPath,
        string errorLogPath,
        bool includeHistory,
        CancellationToken cancellationToken)
    {
        var lines = ReadTailLines(accessLogPath, 5000, 4 * 1024 * 1024, cancellationToken);
        var historyCutoff = DateTimeOffset.Now.AddMinutes(-15);
        var sessionCutoff = _xraySessionStartedAt > _xrayIgnoreBefore ? _xraySessionStartedAt : _xrayIgnoreBefore;
        var cutoff = includeHistory ? historyCutoff : sessionCutoff;
        var domainIndex = BuildXrayDomainIndex(errorLogPath, cutoff.AddMinutes(-10), cancellationToken);
        var groups = new Dictionary<string, XrayGroupAccumulator>(StringComparer.OrdinalIgnoreCase);
        var seenEvents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = XrayAccessRegex.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            if (!TryParseXrayTime(match.Groups["time"].Value, out var started))
            {
                started = DateTimeOffset.Now;
            }

            if (started < cutoff)
            {
                continue;
            }

            if (!TryNormalizeXrayAccessTarget(
                    match.Groups["target"].Value,
                    out var destination,
                    out var network))
            {
                continue;
            }

            var outbound = match.Groups["outbound"].Value.Trim();
            var eventKey = $"{match.Groups["time"].Value}|{destination}|{outbound}|{network}|{match.Groups["source"].Value}";
            if (!seenEvents.Add(eventKey))
            {
                continue;
            }

            var host = ExtractHost(destination);
            var port = ExtractPort(destination);
            var domainName = string.Empty;
            var domainSource = string.Empty;

            if (!IPAddress.TryParse(host, out _) && IsDomainName(host))
            {
                domainName = NormalizeDomain(host);
                domainSource = "Xray access log";
            }
            else if (IPAddress.TryParse(host, out _))
            {
                var domainMatch = domainIndex.Resolve(host, port, network, outbound, started);
                domainName = domainMatch.Domain;
                domainSource = domainMatch.Source;
            }

            var groupKey = $"{host.ToLowerInvariant()}|{port}|{domainName.ToLowerInvariant()}|{outbound.ToLowerInvariant()}|{network}";
            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = new XrayGroupAccumulator(
                    destination,
                    host,
                    port,
                    outbound,
                    network,
                    domainName,
                    domainSource,
                    started);
                groups[groupKey] = group;
            }
            else
            {
                group.Add(started);
            }
        }

        var now = DateTimeOffset.Now;
        return groups.Values
            .Select(group =>
            {
                var elapsed = now - group.LastSeen;
                if (elapsed < TimeSpan.Zero)
                {
                    elapsed = TimeSpan.Zero;
                }

                var destinationIp = IPAddress.TryParse(group.Host, out _) ? group.Host : string.Empty;
                return new SgConnectionRow
                {
                    Id = $"{group.Host}|{group.Port}|{group.DomainName}|{group.Outbound}|{group.Network}",
                    Host = group.Destination,
                    DestinationIp = destinationIp,
                    DestinationDisplay = group.DomainName.IsNotEmpty() ? group.DomainName : group.Host,
                    DomainName = group.DomainName,
                    DomainSource = group.DomainSource,
                    PortDisplay = group.Port,
                    Source = "Xray access log",
                    RuleDisplay = "—",
                    RuleName = string.Empty,
                    RulePayload = string.Empty,
                    OutboundDisplay = NormalizeOutbound(group.Outbound),
                    OutboundTag = group.Outbound,
                    TunnelDisplay = group.Outbound,
                    ProtocolDisplay = group.Network,
                    ProcessDisplay = "—",
                    DownloadDisplay = "—",
                    UploadDisplay = "—",
                    ElapsedDisplay = FormatElapsed(elapsed),
                    StartedAt = group.LastSeen,
                    FirstSeenAt = group.FirstSeen,
                    HitCount = group.Count,
                    RouteKind = DetectRouteKind(group.Outbound, group.Outbound, string.Empty),
                    CanClose = false,
                    IsLive = false
                };
            })
            .OrderByDescending(row => row.StartedAt)
            .Take(1000)
            .ToList();
    }

    private XrayDomainCorrelationIndex BuildXrayDomainIndex(
        string errorLogPath,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(errorLogPath))
        {
            return XrayDomainCorrelationIndex.Empty;
        }

        var lines = ReadTailLines(errorLogPath, 12000, 6 * 1024 * 1024, cancellationToken);
        var traces = new Dictionary<string, XrayTraceAccumulator>(StringComparer.Ordinal);
        var dnsEvidence = new List<XrayDomainEvidence>();

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = rawLine.Trim();

            var dnsMatch = XrayDnsAnswerRegex.Match(line);
            if (dnsMatch.Success
                && TryParseXrayTime(dnsMatch.Groups["time"].Value, out var dnsTime)
                && dnsTime >= cutoff)
            {
                var domain = NormalizeDomain(dnsMatch.Groups["domain"].Value);
                if (IsDomainName(domain))
                {
                    foreach (var address in ParseXrayAddressList(dnsMatch.Groups["addresses"].Value))
                    {
                        dnsEvidence.Add(new XrayDomainEvidence(
                            address,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            domain,
                            "DNS-сопоставление Xray",
                            dnsTime));
                    }
                }
            }

            var traceMatch = XrayTraceLineRegex.Match(line);
            if (!traceMatch.Success
                || !TryParseXrayTime(traceMatch.Groups["time"].Value, out var traceTime)
                || traceTime < cutoff)
            {
                continue;
            }

            var id = traceMatch.Groups["id"].Value;
            var message = traceMatch.Groups["message"].Value;
            if (!traces.TryGetValue(id, out var trace))
            {
                trace = new XrayTraceAccumulator(id, traceTime);
                traces[id] = trace;
            }
            trace.Touch(traceTime);

            var destinationMatch = XrayTraceDestinationRegex.Match(message);
            if (destinationMatch.Success)
            {
                trace.Network = destinationMatch.Groups["network"].Value.ToUpperInvariant();
                trace.SetDestination(destinationMatch.Groups["destination"].Value, traceTime);
            }

            var sniffedMatch = XraySniffedDomainRegex.Match(message);
            if (sniffedMatch.Success)
            {
                trace.SetDomain(sniffedMatch.Groups["domain"].Value, traceTime);
            }

            var detourMatch = XrayDetourRegex.Match(message);
            if (detourMatch.Success)
            {
                trace.Outbound = detourMatch.Groups["outbound"].Value.Trim();
                trace.Network = detourMatch.Groups["network"].Value.ToUpperInvariant();
                var detourDestination = detourMatch.Groups["destination"].Value.Trim();
                var detourHost = ExtractHost(detourDestination);
                if (IPAddress.TryParse(detourHost, out _))
                {
                    trace.SetDestination(detourDestination, traceTime);
                }
                else if (IsDomainName(detourHost))
                {
                    trace.SetDomain(detourHost, traceTime);
                }
            }
        }

        var sniffingEvidence = traces.Values
            .Where(trace => trace.Address.IsNotEmpty() && IsDomainName(trace.Domain))
            .Select(trace => new XrayDomainEvidence(
                trace.Address,
                trace.Port,
                trace.Network,
                trace.Outbound,
                trace.Domain,
                "Xray sniffing",
                trace.DomainSeenAt != default ? trace.DomainSeenAt : trace.FirstSeen))
            .ToList();

        return new XrayDomainCorrelationIndex(sniffingEvidence, dnsEvidence);
    }

    private static IReadOnlyList<string> ParseXrayAddressList(string value)
    {
        return value
            .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim('[', ']', ','))
            .Where(item => IPAddress.TryParse(item, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeDomain(string value)
    {
        return (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static bool IsDomainName(string value)
    {
        var normalized = NormalizeDomain(value);
        return normalized.IsNotEmpty()
            && !IPAddress.TryParse(normalized, out _)
            && Uri.CheckHostName(normalized) == UriHostNameType.Dns;
    }

    private void ReplaceRows(IEnumerable<SgConnectionRow> rows)
    {
        var normalizedRows = rows.ToList();
        foreach (var row in normalizedRows)
        {
            if (row.RouteKind == "Direct" && IsLoopbackDestination(row))
            {
                row.RuleDisplay = "Локальный адрес · Direct (нормально)";
            }
        }

        _allRows.Clear();
        _allRows.AddRange(normalizedRows);

        var hasOther = _allRows.Any(row => row.RouteKind == "Other");
        routeOther.Visibility = hasOther ? Visibility.Visible : Visibility.Collapsed;
        if (!hasOther && string.Equals(_routeFilter, "Other", StringComparison.Ordinal))
        {
            _routeFilter = "Все выходы";
            txtRouteFilter.Text = _routeFilter;
            routeAll.IsChecked = true;
        }

        ApplyFilters();
        UpdateSelectionState();
        UpdateRouteSummary();
    }

    private void ApplyFilters()
    {
        if (_disposed)
        {
            return;
        }

        var selectedId = (gridConnections.SelectedItem as SgConnectionRow)?.Id;
        var query = txtSearch.Text?.Trim() ?? string.Empty;
        var routeFilter = _routeFilter;

        IEnumerable<SgConnectionRow> filtered = _allRows;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(row => searchTerms.All(term =>
                row.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = routeFilter switch
        {
            "Direct" => filtered.Where(row => row.RouteKind == "Direct"),
            "VPN" => filtered.Where(row => row.RouteKind == "VPN"),
            "Block" => filtered.Where(row => row.RouteKind == "Block"),
            "Other" => filtered.Where(row => row.RouteKind == "Other"),
            _ => filtered
        };

        _visibleRows.Clear();
        _visibleRows.AddRange(filtered.Take(MaxDisplayedRows));

        // Replace the source once instead of firing hundreds or thousands of
        // ObservableCollection notifications on the UI thread.
        gridConnections.ItemsSource = null;
        gridConnections.ItemsSource = _visibleRows;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            gridConnections.SelectedItem = _visibleRows.FirstOrDefault(row => row.Id == selectedId);
        }

        if (_isXrayMode)
        {
            var visibleEvents = _visibleRows.Sum(row => row.HitCount);
            txtCount.Text = $"{FormatDestinationCount(_visibleRows.Count)} · {FormatRequestCount(visibleEvents)}";
        }
        else
        {
            txtCount.Text = _allRows.Count > MaxDisplayedRows
                ? $"{FormatCount(_visibleRows.Count)} из {FormatCount(_allRows.Count)}"
                : FormatCount(_visibleRows.Count);
        }
        UpdateEmptyState();
        UpdateActionState();
        UpdateRouteSummary();
    }

    private void ClearRows()
    {
        var now = DateTimeOffset.Now;
        _xrayIgnoreBefore = now;
        _xraySessionStartedAt = now;
        chkIncludeHistory.IsChecked = false;
        _singBoxHistory.Clear();
        _allRows.Clear();
        _visibleRows.Clear();
        gridConnections.ItemsSource = null;
        gridConnections.ItemsSource = _visibleRows;
        txtCount.Text = _isXrayMode ? "0 назначений · 0 обращений" : "0 соединений";
        txtHint.Text = _isXrayMode
            ? "Новый тест начат. Откройте нужный сайт и нажмите кнопку обновления."
            : "Список очищен. Новые соединения появятся при следующем обновлении.";
        UpdateEmptyState();
        UpdateSelectionState();
        UpdateRouteSummary();
        if (_isXrayMode)
        {
            QueueRefresh(force: true);
        }
    }

    private void QueueCloseSelected()
    {
        if (!_disposed)
        {
            _ = CloseSelectedAsync(_lifetimeCts.Token);
        }
    }

    private async Task CloseSelectedAsync(CancellationToken cancellationToken)
    {
        if (!_isSingBoxMode || gridConnections.SelectedItem is not SgConnectionRow row || !row.CanClose)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ApiTimeout);
        try
        {
            await ClashApiManager.Instance.ClashConnectionClose(row.Id, timeoutCts.Token);
            QueueRefresh(force: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            txtHint.Text = "Закрытие соединения не ответило за 3 секунды. Окно продолжает работать.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The window is closing.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgConnectionsWindow.CloseSelected", ex);
            txtHint.Text = "Не удалось закрыть соединение. Подробности записаны в журнал.";
        }
    }

    private void QueueCloseAll()
    {
        if (!_disposed)
        {
            _ = CloseAllAsync(_lifetimeCts.Token);
        }
    }

    private async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        if (!_isSingBoxMode)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ApiTimeout);
        try
        {
            await ClashApiManager.Instance.ClashConnectionClose(string.Empty, timeoutCts.Token);
            QueueRefresh(force: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            txtHint.Text = "Закрытие соединений не ответило за 3 секунды. Окно продолжает работать.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The window is closing.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgConnectionsWindow.CloseAll", ex);
            txtHint.Text = "Не удалось закрыть соединения. Подробности записаны в журнал.";
        }
    }

    private void GridConnections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void GridConnections_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        btnCloseSelected.IsEnabled = _isSingBoxMode
            && gridConnections.SelectedItem is SgConnectionRow row
            && row.CanClose;
        UpdateActionState();
    }

    private void SetSupportedMode(string engine, string hint, bool canClose, string emptyMessage)
    {
        _supportsConnectionList = true;
        _isSingBoxMode = canClose;
        _isXrayMode = !canClose;
        _emptyMessage = emptyMessage;

        _diagnosticsPromptAction = DiagnosticsPromptAction.None;
        btnUnsupportedSecondary.Visibility = Visibility.Collapsed;
        btnUnsupportedRefresh.Width = 170;
        btnUnsupportedRefresh.Content = "Проверить снова";
        txtEngine.Text = engine;
        txtHint.Text = hint;
        filterPanel.Visibility = Visibility.Visible;
        tablePanel.Visibility = Visibility.Visible;
        unsupportedPanel.Visibility = Visibility.Collapsed;
        awgDiagnosticsPanel.Visibility = Visibility.Collapsed;
        chkAutoRefresh.Visibility = Visibility.Visible;
        chkIncludeHistory.Visibility = Visibility.Visible;
        closeButtonsPanel.Visibility = canClose ? Visibility.Visible : Visibility.Collapsed;
        btnClear.Content = canClose ? "Очистить список" : "Новый тест";
        SetColumnsForMode(canClose);
        UpdateTimerState();
        UpdateEmptyState();
        UpdateActionState();
        UpdateRouteSummary();
    }

    private void SetColumnsForMode(bool singBox)
    {
        colRule.Visibility = Visibility.Visible;
        colOutbound.Visibility = Visibility.Visible;
        colTunnel.Visibility = singBox ? Visibility.Visible : Visibility.Collapsed;
        colProtocol.Visibility = Visibility.Visible;
        colProcess.Visibility = singBox ? Visibility.Visible : Visibility.Collapsed;
        colHitCount.Visibility = singBox ? Visibility.Collapsed : Visibility.Visible;
        colTime.Header = singBox ? "Время" : "Последнее";
    }

    private void SetUnsupportedMode(string engine, string hint, string details)
    {
        _supportsConnectionList = false;
        _isSingBoxMode = false;
        _isXrayMode = false;
        _refreshTimer.Stop();

        _diagnosticsPromptAction = DiagnosticsPromptAction.None;
        btnUnsupportedSecondary.Visibility = Visibility.Collapsed;
        btnUnsupportedRefresh.Width = 170;
        btnUnsupportedRefresh.Content = "Проверить снова";
        btnUnsupportedRefresh.IsEnabled = true;
        txtEngine.Text = engine;
        txtHint.Text = hint;
        txtUnsupportedTitle.Text = engine;
        txtUnsupportedDetails.Text = details;
        filterPanel.Visibility = Visibility.Collapsed;
        tablePanel.Visibility = Visibility.Collapsed;
        emptyState.Visibility = Visibility.Collapsed;
        unsupportedPanel.Visibility = Visibility.Visible;
        awgDiagnosticsPanel.Visibility = Visibility.Collapsed;
        chkAutoRefresh.Visibility = Visibility.Collapsed;
        chkIncludeHistory.Visibility = Visibility.Collapsed;
        closeButtonsPanel.Visibility = Visibility.Collapsed;
        btnCloseSelected.IsEnabled = false;
        btnCloseAll.IsEnabled = false;
        colProcess.Visibility = Visibility.Collapsed;
        colHitCount.Visibility = Visibility.Collapsed;
        UpdateRouteSummary();
    }

    private void QueueRuntimeSummaryRefresh()
    {
        if (!_disposed && !_lifetimeCts.IsCancellationRequested)
        {
            _ = RefreshRuntimeSummaryAsync(_lifetimeCts.Token);
        }
    }

    private async Task<RuntimeSummary> RefreshRuntimeSummaryAsync(CancellationToken cancellationToken)
    {
        var summary = await BuildRuntimeSummaryAsync(cancellationToken);
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return summary;
        }

        txtSummaryMode.Text = summary.ModeTitle;
        txtSummaryRouting.Text = summary.RoutingTitle;
        txtSummaryProfile.Text = summary.ProfileTitle;
        txtSummaryCore.Text = summary.CoreTitle;
        txtSummaryDns.Text = summary.DnsTitle;
        txtSummaryDns.SetResourceReference(
            TextBlock.ForegroundProperty,
            summary.DnsThroughTun ? "SgSuccessBrush" : "SgWarningBrush");
        return summary;
    }

    private async Task<RuntimeSummary> BuildRuntimeSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = AppManager.Instance.Config;
        var settings = SgSmartRoutingHelper.Normalize(config.SgQuickSettingsItem);
        var modeKey = ResolveConnectionModeKey(config);
        var modeTitle = modeKey switch
        {
            "tun" => "TUN",
            "system-proxy" => "System Proxy",
            "local-proxy" => "Local Proxy",
            _ => "Отключено",
        };
        var routingTitle = settings.Preset switch
        {
            SgSmartRoutingHelper.PresetRussiaDirect => "Россия напрямую",
            SgSmartRoutingHelper.PresetBlockedOnly => "Только блокировки",
            SgSmartRoutingHelper.PresetCustom when settings.RussiaScope == SgSmartRoutingHelper.RussiaScopeTld
                => "Пользовательская · доменные зоны РФ",
            SgSmartRoutingHelper.PresetCustom when settings.RussiaScope == SgSmartRoutingHelper.RussiaScopeSitesAndIp
                => "Пользовательская · сайты и IP РФ",
            SgSmartRoutingHelper.PresetCustom => "Пользовательская",
            _ => "Весь интернет",
        };

        var profileTitle = "Профиль не выбран";
        var coreTitle = "—";
        var awgProfileId = AmneziaWgManager.Instance.ActiveProfileId;
        if (awgProfileId.IsNullOrEmpty()
            && !AppManager.Instance.IsRunningCore(ECoreType.Xray)
            && !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            awgProfileId = AmneziaWgManager.Instance.SelectedProfileId;
        }
        var awgProfile = AmneziaWgManager.Instance.GetProfile(awgProfileId);
        if (awgProfile != null)
        {
            profileTitle = awgProfile.Name.IsNotEmpty() ? awgProfile.Name : "AmneziaWG";
            coreTitle = AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty()
                ? "AmneziaWG · активно"
                : "AmneziaWG · выбрано";
        }
        else
        {
            var profile = await ConfigHandler.GetDefaultServer(config);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile != null)
            {
                profileTitle = profile.Remarks.IsNotEmpty() ? profile.Remarks : profile.ConfigType.ToString();
                coreTitle = ResolveCoreTitle(profile);
            }
        }

        if (AppManager.Instance.IsRunningCore(ECoreType.Xray))
        {
            coreTitle = "Xray · активно";
        }
        else if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            coreTitle = "sing-box · активно";
        }

        var dnsThroughTun = config.SgQuickSettingsItem.DnsThroughTun;
        var dnsTitle = modeKey == "tun"
            ? (dnsThroughTun ? "Через VPN" : "Напрямую · риск")
            : (dnsThroughTun ? "Для TUN: через VPN" : "Для TUN: напрямую");
        var targetHost = ResolveIpCheckHost(config.SpeedTestItem.IPAPIUrl);
        return new RuntimeSummary(
            modeKey,
            modeTitle,
            routingTitle,
            profileTitle,
            coreTitle,
            dnsTitle,
            dnsThroughTun,
            settings.Preset == SgSmartRoutingHelper.PresetGlobal,
            targetHost);
    }

    private static string ResolveConnectionModeKey(Config config)
    {
        if (config.TunModeItem.EnableTun || AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty())
        {
            return "tun";
        }

        var mode = config.SgQuickSettingsItem.ConnectionMode;
        if (mode is "system-proxy" or "local-proxy")
        {
            return mode;
        }

        return config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange
            ? "system-proxy"
            : "off";
    }

    private static string ResolveCoreTitle(ProfileItem profile)
    {
        var core = profile.CoreType?.ToString();
        if (core.IsNotEmpty())
        {
            return $"{core} · {profile.ConfigType}";
        }

        return profile.ConfigType switch
        {
            EConfigType.Hysteria2 => "sing-box · Hysteria2",
            EConfigType.VLESS => "Xray · VLESS",
            _ => profile.ConfigType.ToString(),
        };
    }

    private static string ResolveIpCheckHost(string? rawUrl)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) && uri.Host.IsNotEmpty())
        {
            return uri.Host;
        }

        return "api.ip.sb";
    }

    private void UpdateRouteSummary()
    {
        if (_disposed)
        {
            return;
        }

        var vpn = _allRows.Where(row => row.RouteKind == "VPN").ToList();
        var direct = _allRows.Where(row => row.RouteKind == "Direct").ToList();
        var block = _allRows.Where(row => row.RouteKind == "Block").ToList();

        var config = AppManager.Instance.Config;
        var routing = SgSmartRoutingHelper.Normalize(config.SgQuickSettingsItem);
        var publicDirect = direct.Where(IsPublicDestination).ToList();
        var unexpectedDirect = config.TunModeItem.EnableTun
            && routing.Preset == SgSmartRoutingHelper.PresetGlobal
            && publicDirect.Count > 0;
        txtSummaryRoutes.SetResourceReference(
            TextBlock.ForegroundProperty,
            unexpectedDirect ? "SgErrorBrush" : "SgTextBrush");

        if (_isSingBoxMode)
        {
            txtSummaryRoutes.Text = $"VPN {FormatBytes(SumTraffic(vpn))} · Direct {FormatBytes(SumTraffic(direct))} · Block {block.Count}";
            txtSummaryRoutesHint.Text = unexpectedDirect
                ? "Весь интернет: обнаружен Direct — откройте строку и проверьте правило"
                : "sing-box: фактические соединения и объём из Clash API";
            return;
        }

        if (_isXrayMode)
        {
            txtSummaryRoutes.Text = $"VPN {FormatRequestCount(vpn.Sum(row => row.HitCount))} · Direct {FormatRequestCount(direct.Sum(row => row.HitCount))} · Block {FormatRequestCount(block.Sum(row => row.HitCount))}";
            txtSummaryRoutesHint.Text = unexpectedDirect
                ? "Весь интернет: обнаружен Direct — это требует проверки"
                : "Xray: фактические назначения; объём — в статистике профиля";
            return;
        }

        txtSummaryRoutes.SetResourceReference(TextBlock.ForegroundProperty, "SgTextBrush");
        txtSummaryRoutes.Text = "VPN 0 · Direct 0 · Block 0";
        txtSummaryRoutesHint.Text = AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty()
            ? "AmneziaWG показывает общий трафик туннеля без сайтов"
            : "Подключите режим и откройте сайт";
    }

    private static bool IsLoopbackDestination(SgConnectionRow row)
    {
        if (string.Equals(row.DomainName, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.DestinationDisplay, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidates = new[] { row.DestinationIp, ExtractHost(row.Host), ExtractHost(row.DestinationDisplay) };
        foreach (var candidate in candidates)
        {
            if (candidate.IsNullOrEmpty())
            {
                continue;
            }
            var normalized = candidate.Trim().Trim('[', ']');
            if (IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPublicDestination(SgConnectionRow row)
    {
        if (row.DestinationIp.IsNotEmpty() && IPAddress.TryParse(row.DestinationIp, out var address))
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            {
                return false;
            }
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] == 10
                    || bytes[0] == 127
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127))
                {
                    return false;
                }
            }
            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                     && (bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }
            return true;
        }

        var domain = row.DomainName.Trim();
        return domain.IsNotEmpty()
            && domain.Contains('.', StringComparison.Ordinal)
            && !domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !domain.EndsWith(".lan", StringComparison.OrdinalIgnoreCase);
    }

    private static ulong SumTraffic(IEnumerable<SgConnectionRow> rows)
    {
        ulong total = 0;
        foreach (var row in rows)
        {
            var value = ulong.MaxValue - row.DownloadBytes < row.UploadBytes
                ? ulong.MaxValue
                : row.DownloadBytes + row.UploadBytes;
            total = ulong.MaxValue - total < value ? ulong.MaxValue : total + value;
        }
        return total;
    }

    private async Task EnrichXrayRouteRulesAsync(IReadOnlyList<SgConnectionRow> rows, CancellationToken cancellationToken)
    {
        var config = AppManager.Instance.Config;
        if (!config.TunModeItem.EnableTun
            || (!AppManager.Instance.IsRunningCore(ECoreType.Xray) && !AppManager.Instance.IsRunningCore(ECoreType.sing_box)))
        {
            foreach (var row in rows)
            {
                row.RuleDisplay = "Фактический access log";
            }
            return;
        }

        foreach (var row in rows)
        {
            row.RuleDisplay = "Фактический access log";
        }

        var candidates = rows
            .Where(row => row.DomainName.IsNotEmpty())
            .Select(row => new
            {
                Row = row,
                Target = row.DomainName,
            })
            .GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .ToArray();

        using var routeBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        routeBudget.CancelAfter(TimeSpan.FromSeconds(2));
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SgRouteTestResult result;
            if (_routeDecisionCache.TryGetValue(candidate.Target, out var cached)
                && DateTimeOffset.Now - cached.CachedAt <= RouteDecisionCacheLifetime)
            {
                result = cached.Result;
            }
            else
            {
                try
                {
                    result = await SgRouteTestService.Instance.TestAsync(candidate.Target, routeBudget.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                _routeDecisionCache[candidate.Target] = (DateTimeOffset.Now, result);
            }

            foreach (var row in rows.Where(row => string.Equals(
                         row.DomainName.IsNotEmpty() ? row.DomainName : row.DestinationIp,
                         candidate.Target,
                         StringComparison.OrdinalIgnoreCase)))
            {
                if (!result.IsExact)
                {
                    row.RuleDisplay = "Не определено";
                    continue;
                }

                var expectedKind = NormalizeRouteKind(result.Action);
                row.RuleDisplay = expectedKind.IsNotEmpty()
                    && !string.Equals(expectedKind, row.RouteKind, StringComparison.OrdinalIgnoreCase)
                        ? $"⚠ {result.MatchedRule} · ожидался {result.Action}"
                        : result.MatchedRule;
            }
        }
    }

    private static string NormalizeRouteKind(string? value)
    {
        if (value.IsNullOrEmpty())
        {
            return string.Empty;
        }
        if (value.Contains("direct", StringComparison.OrdinalIgnoreCase)
            || value.Contains("напрям", StringComparison.OrdinalIgnoreCase))
        {
            return "Direct";
        }
        if (value.Contains("block", StringComparison.OrdinalIgnoreCase)
            || value.Contains("блок", StringComparison.OrdinalIgnoreCase))
        {
            return "Block";
        }
        if (value.Contains("vpn", StringComparison.OrdinalIgnoreCase)
            || value.Contains("proxy", StringComparison.OrdinalIgnoreCase))
        {
            return "VPN";
        }
        return string.Empty;
    }

    private void QueueVpnCheck()
    {
        if (!_disposed && !_lifetimeCts.IsCancellationRequested)
        {
            _ = RunVpnCheckAsync(_lifetimeCts.Token);
        }
    }

    private async Task RunVpnCheckAsync(CancellationToken cancellationToken)
    {
        _vpnCheckHasRun = true;
        SetVpnCheckBusy(true);
        try
        {
            var summary = await RefreshRuntimeSummaryAsync(cancellationToken);
            txtVpnCheckMode.Text = $"{summary.ModeTitle} · {summary.RoutingTitle}";
            txtVpnCheckProfile.Text = $"{summary.ProfileTitle} · {summary.CoreTitle}";
            txtVpnCheckDns.Text = summary.DnsTitle;
            txtVpnCheckDns.SetResourceReference(
                TextBlock.ForegroundProperty,
                summary.DnsThroughTun ? "SgSuccessBrush" : "SgWarningBrush");
            txtVpnCheckTarget.Text = summary.RouteTestHost;

            IpInfoResult? ipInfo = null;
            if (summary.ModeKey != "off")
            {
                if (AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty())
                {
                    ipInfo = await GetIpInfoWithTimeoutAsync(null, cancellationToken);
                }
                else if (AppManager.Instance.IsRunningCore(ECoreType.Xray)
                         || AppManager.Instance.IsRunningCore(ECoreType.sing_box))
                {
                    var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
                    var proxy = new WebProxy($"socks5://{Global.Loopback}:{port}");
                    ipInfo = await GetIpInfoWithTimeoutAsync(proxy, cancellationToken);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            txtVpnCheckExternalIp.Text = ipInfo is { } value && value.Ip.IsNotEmpty()
                ? $"{value.Ip} · {value.Country}"
                : "Не удалось определить";

            SgRouteTestResult? routeResult = null;
            if (summary.ModeKey == "tun"
                && AmneziaWgManager.Instance.ActiveProfileId.IsNullOrEmpty()
                && (AppManager.Instance.IsRunningCore(ECoreType.Xray)
                    || AppManager.Instance.IsRunningCore(ECoreType.sing_box)))
            {
                routeResult = await SgRouteTestService.Instance.TestAsync(summary.RouteTestHost, cancellationToken);
            }

            if (routeResult != null)
            {
                txtVpnCheckRoute.Text = routeResult.Action;
                txtVpnCheckRule.Text = routeResult.MatchedRule;
            }
            else
            {
                txtVpnCheckRoute.Text = summary.ModeKey == "tun" && AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty()
                    ? "Туннель целиком · отдельные правила недоступны"
                    : "Недоступно для этого режима";
                txtVpnCheckRule.Text = summary.ModeKey is "system-proxy" or "local-proxy"
                    ? "SG Routing применяется только в TUN"
                    : "—";
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            if (summary.ModeKey == "off")
            {
                errors.Add("Режим подключения выключен.");
            }
            if (summary.ProfileTitle == "Профиль не выбран")
            {
                errors.Add("Активный профиль не выбран.");
            }
            if (!summary.DnsThroughTun && summary.ModeKey == "tun")
            {
                warnings.Add("DNS настроен напрямую. Запросы имён могут обходить VPN.");
            }
            if (summary.ModeKey is "system-proxy" or "local-proxy")
            {
                warnings.Add("Точная проверка SG Routing и DNS-маршрута доступна только в TUN.");
            }
            if (!ipInfo.HasValue || ipInfo.Value.Ip.IsNullOrEmpty())
            {
                warnings.Add("Внешний IP через SG Client определить не удалось; фактический выход в интернет не подтверждён.");
            }

            var tunCounter = StatisticsManager.Instance.GetTunInterfaceCounterStatus();
            if (summary.ModeKey == "tun"
                && summary.IsGlobalRouting
                && AmneziaWgManager.Instance.ActiveProfileId.IsNullOrEmpty())
            {
                if (!tunCounter.IsActive)
                {
                    warnings.Add($"Системный счётчик WinTUN пока не активен: {tunCounter.StatusMessage}.");
                }
            }
            if (summary.IsGlobalRouting && summary.ModeKey == "tun" && routeResult != null)
            {
                if (!routeResult.IsExact || NormalizeRouteKind(routeResult.Action) != "VPN")
                {
                    errors.Add("В режиме «Весь интернет» тестовый публичный адрес не получил маршрут VPN.");
                }
            }

            string status;
            string summaryText;
            string brush;
            if (errors.Count > 0)
            {
                status = "Проверка обнаружила ошибку";
                summaryText = string.Join(" ", errors);
                brush = "SgErrorBrush";
            }
            else if (warnings.Count > 0)
            {
                status = "Проверка частичная · VPN не подтверждён полностью";
                summaryText = string.Join(" ", warnings);
                brush = "SgWarningBrush";
            }
            else
            {
                status = "VPN и маршрутизация выглядят исправно";
                summaryText = summary.IsGlobalRouting
                    ? "Тестовый публичный адрес направлен через VPN, DNS — через VPN."
                    : "Подключение активно. В раздельном режиме отдельные сайты могут идти Direct по выбранным правилам.";
                brush = "SgSuccessBrush";
            }

            txtVpnCheckStatus.Text = status;
            txtVpnCheckSummary.Text = summaryText;
            vpnCheckDot.SetResourceReference(Border.BackgroundProperty, brush);
            var details = new List<string>();
            details.Add(summary.IsGlobalRouting
                ? "Режим «Весь интернет»: Direct для публичного тестового адреса считается ошибкой."
                : "Раздельная маршрутизация: Direct допустим только когда его выбрало активное правило.");
            if (routeResult != null && routeResult.Details.IsNotEmpty())
            {
                details.Add(routeResult.Details);
            }
            if (summary.ModeKey == "tun" && summary.IsGlobalRouting)
            {
                details.Add(tunCounter.IsActive
                    ? $"Учёт трафика: системный интерфейс {tunCounter.InterfaceName}; RX {FormatBytes((ulong)Math.Max(0, tunCounter.BytesReceived))}; TX {FormatBytes((ulong)Math.Max(0, tunCounter.BytesSent))}. TCP, UDP и QUIC учитываются на уровне WinTUN."
                    : $"Учёт трафика WinTUN: {tunCounter.StatusMessage}.");
            }
            if (warnings.Count > 0)
            {
                details.Add(string.Join(" ", warnings));
            }
            txtVpnCheckDetails.Text = string.Join(Environment.NewLine, details);
            txtVpnCheckLastRun.Text = $"Последняя проверка: {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Window is closing.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgConnectionsWindow.VpnCheck", ex);
            txtVpnCheckStatus.Text = "Не удалось выполнить проверку";
            txtVpnCheckSummary.Text = ex.Message;
            vpnCheckDot.SetResourceReference(Border.BackgroundProperty, "SgErrorBrush");
        }
        finally
        {
            if (!_disposed)
            {
                txtVpnCheckLastRun.Text = $"Последняя проверка: {DateTime.Now:HH:mm:ss}";
                SetVpnCheckBusy(false);
            }
        }
    }

    private static async Task<IpInfoResult?> GetIpInfoWithTimeoutAsync(IWebProxy? proxy, CancellationToken cancellationToken)
    {
        var request = ConnectionHandler.GetIPInfo(proxy);
        var timeout = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        var completed = await Task.WhenAny(request, timeout);
        if (completed != request)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        return await request;
    }

    private void SetVpnCheckBusy(bool busy)
    {
        btnVpnCheck.IsEnabled = !busy;
        btnQuickVpnCheck.IsEnabled = !busy;
        btnVpnCheck.Content = busy
            ? "Проверяю…"
            : (_vpnCheckHasRun ? "Проверить снова" : "Запустить проверку");
        vpnCheckProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            txtVpnCheckStatus.Text = "Проверка выполняется";
            txtVpnCheckSummary.Text = "Проверяю внешний IP, фактический маршрут, DNS и системный счётчик TUN.";
            vpnCheckDot.SetResourceReference(Border.BackgroundProperty, "SgWarningBrush");
        }
    }

    private void OpenDnsRouteWindow()
    {
        var window = new SgDnsRouteWindow { Owner = this };
        window.ShowDialog();
        QueueRuntimeSummaryRefresh();
    }

    private sealed record RuntimeSummary(
        string ModeKey,
        string ModeTitle,
        string RoutingTitle,
        string ProfileTitle,
        string CoreTitle,
        string DnsTitle,
        bool DnsThroughTun,
        bool IsGlobalRouting,
        string RouteTestHost);

    private void RouteInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            QueueRouteTest();
            e.Handled = true;
        }
    }

    private void QueueRouteTest()
    {
        if (!_disposed && !_lifetimeCts.IsCancellationRequested)
        {
            _ = RunRouteTestAsync(_lifetimeCts.Token);
        }
    }

    private async Task RunRouteTestAsync(CancellationToken cancellationToken)
    {
        SetRouteTestBusy(true);
        try
        {
            var result = await SgRouteTestService.Instance.TestAsync(txtRouteInput.Text, cancellationToken);
            if (_disposed)
            {
                return;
            }

            txtRouteStatus.Text = result.IsExact ? "Адресное правило определено точно" : "Точный итог не получен";
            txtMatchedRule.Text = result.MatchedRule;
            runRouteAction.Text = result.Action;
            runRoutePriority.Text = result.Priority > 0 ? $"приоритет {result.Priority}" : "—";
            txtRouteProfile.Text = FormatRouteTarget(result.Profile, result.Outbound);
            txtRouteResolved.Text = result.ResolvedAddresses.Count > 0
                ? $"DNS/IP: {string.Join(", ", result.ResolvedAddresses)}"
                : string.Empty;
            txtRouteDetails.Text = result.Details;
            gridRouteTrace.ItemsSource = result.Trace;
            routeResultDot.SetResourceReference(
                Border.BackgroundProperty,
                result.IsExact ? "SgSuccessBrush" : "SgWarningBrush");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Window is closing.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgConnectionsWindow.RouteTest", ex);
            txtRouteStatus.Text = "Ошибка проверки";
            txtMatchedRule.Text = "—";
            runRouteAction.Text = "—";
            runRoutePriority.Text = "—";
            txtRouteProfile.Text = "—";
            txtRouteResolved.Text = string.Empty;
            txtRouteDetails.Text = ex.Message;
            gridRouteTrace.ItemsSource = null;
            routeResultDot.SetResourceReference(Border.BackgroundProperty, "SgErrorBrush");
        }
        finally
        {
            if (!_disposed)
            {
                SetRouteTestBusy(false);
            }
        }
    }

    private static string FormatRouteTarget(string profile, string outbound)
    {
        if (profile.IsNullOrEmpty())
        {
            return outbound;
        }
        if (outbound.IsNullOrEmpty()
            || string.Equals(profile, outbound, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(profile, "Direct", StringComparison.OrdinalIgnoreCase) && string.Equals(outbound, Global.DirectTag, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(profile, "Block", StringComparison.OrdinalIgnoreCase) && string.Equals(outbound, Global.BlockTag, StringComparison.OrdinalIgnoreCase)))
        {
            return profile;
        }
        return $"{profile} · {outbound}";
    }

    private void SetRouteTestBusy(bool busy)
    {
        btnRouteTest.IsEnabled = !busy;
        txtRouteInput.IsEnabled = !busy;
        routeTestProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        btnRefresh.IsEnabled = !busy;
        btnUnsupportedRefresh.IsEnabled = !busy;
        refreshProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTimerState()
    {
        if (_disposed || !_supportsConnectionList || chkAutoRefresh.IsChecked != true)
        {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Start();
    }

    private void UpdateEmptyState()
    {
        if (!_supportsConnectionList)
        {
            emptyState.Visibility = Visibility.Collapsed;
            return;
        }

        var hasSearch = !string.IsNullOrWhiteSpace(txtSearch.Text);
        var hasRouteFilter = !string.Equals(_routeFilter, "Все выходы", StringComparison.Ordinal);
        txtEmptyState.Text = _allRows.Count > 0 && (hasSearch || hasRouteFilter)
            ? "Ничего не найдено. Очистите поиск или выберите «Все выходы»."
            : _emptyMessage;
        emptyState.Visibility = _visibleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActionState()
    {
        var hasRows = _visibleRows.Count > 0;
        btnExportCsv.IsEnabled = _supportsConnectionList && hasRows;
        btnExportJson.IsEnabled = _supportsConnectionList && hasRows;
        btnClear.IsEnabled = _supportsConnectionList && _allRows.Count > 0;
        btnCloseAll.IsEnabled = _isSingBoxMode && _allRows.Any(row => row.IsLive && row.CanClose);
    }

    private void ExportCsv()
    {
        if (_visibleRows.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить список соединений",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"SG-Client-connections-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Domain;DomainSource;Destination;Country;Rule;ActualOutbound;TechnicalOutbound;Tunnel;Protocol;Process;Requests;Elapsed;Source");
        foreach (var row in _visibleRows)
        {
            builder.AppendLine(string.Join(';', new[]
            {
                Csv(row.DomainName),
                Csv(row.DomainSource),
                Csv(row.DestinationAddressDisplay),
                Csv(row.CountryCode),
                Csv(row.RuleDisplay),
                Csv(row.OutboundDisplay),
                Csv(row.OutboundTag),
                Csv(row.TunnelDisplay),
                Csv(row.ProtocolDisplay),
                Csv(row.ProcessDisplay),
                Csv(row.HitCountDisplay),
                Csv(row.ElapsedDisplay),
                Csv(row.Source)
            }));
        }

        File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
    }

    private void ExportJson()
    {
        if (_visibleRows.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить список соединений",
            Filter = "JSON (*.json)|*.json",
            FileName = $"SG-Client-connections-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_visibleRows, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
    }

    private static string Csv(string value)
    {
        var safe = value ?? string.Empty;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string BuildHost(string? host, string? ip, string? port)
    {
        var target = string.IsNullOrWhiteSpace(host) ? ip : host;
        target = string.IsNullOrWhiteSpace(target) ? "—" : target;
        return string.IsNullOrWhiteSpace(port) ? target : $"{target}:{port}";
    }

    private static string FormatPort(string? port)
    {
        var value = port?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $":{value}";
    }

    private static bool TryNormalizeXrayAccessTarget(
        string rawTarget,
        out string destination,
        out string network)
    {
        destination = string.Empty;
        network = string.Empty;

        var value = rawTarget?.Trim() ?? string.Empty;
        if (value.IsNullOrEmpty())
        {
            return false;
        }

        if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("udp:", StringComparison.OrdinalIgnoreCase))
        {
            network = value[..3].ToUpperInvariant();
            destination = value[4..].Trim();
            return destination.IsNotEmpty();
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            network = "TCP";
            destination = value[2..].Trim();
            return destination.IsNotEmpty();
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            network = "TCP";
            var host = uri.Host;
            if (host.Contains(':', StringComparison.Ordinal)
                && !host.StartsWith("[", StringComparison.Ordinal))
            {
                host = $"[{host}]";
            }

            destination = $"{host}:{uri.Port}";
            return host.IsNotEmpty();
        }

        return false;
    }

    private static string ExtractPort(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        var value = destination.Trim();
        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            if (closing >= 0 && closing + 1 < value.Length && value[closing + 1] == ':')
            {
                return FormatPort(value[(closing + 2)..]);
            }
            return string.Empty;
        }

        var lastColon = value.LastIndexOf(':');
        return lastColon > 0 && int.TryParse(value[(lastColon + 1)..], out _)
            ? FormatPort(value[(lastColon + 1)..])
            : string.Empty;
    }

    private static string BuildRule(string? rule, string? payload)
    {
        var left = string.IsNullOrWhiteSpace(rule) ? "Final" : rule.Trim();
        return string.IsNullOrWhiteSpace(payload) ? left : $"{left}: {payload.Trim()}";
    }

    private static string BuildProtocol(string? network, string? type)
    {
        var parts = new[] { network, type }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var result = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(result) ? "—" : result;
    }

    private static string BuildProcess(string? process, string? processPath)
    {
        if (!string.IsNullOrWhiteSpace(process))
        {
            return process.Trim();
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return Path.GetFileName(processPath.Trim());
        }

        return "—";
    }

    private sealed record XrayDomainMatch(string Domain, string Source)
    {
        public static XrayDomainMatch Empty { get; } = new(string.Empty, string.Empty);
    }

    private sealed record XrayDomainEvidence(
        string Address,
        string Port,
        string Network,
        string Outbound,
        string Domain,
        string Source,
        DateTimeOffset Timestamp);

    private sealed class XrayDomainCorrelationIndex
    {
        private static readonly TimeSpan SniffingWindow = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan DnsWindow = TimeSpan.FromMinutes(10);

        private readonly Dictionary<string, List<XrayDomainEvidence>> _sniffingByAddress;
        private readonly Dictionary<string, List<XrayDomainEvidence>> _dnsByAddress;

        public XrayDomainCorrelationIndex(
            IEnumerable<XrayDomainEvidence> sniffingEvidence,
            IEnumerable<XrayDomainEvidence> dnsEvidence)
        {
            _sniffingByAddress = BuildIndex(sniffingEvidence);
            _dnsByAddress = BuildIndex(dnsEvidence);
        }

        public static XrayDomainCorrelationIndex Empty { get; } = new([], []);

        public XrayDomainMatch Resolve(
            string address,
            string port,
            string network,
            string outbound,
            DateTimeOffset timestamp)
        {
            if (_sniffingByAddress.TryGetValue(address, out var sniffingCandidates))
            {
                var sniffing = sniffingCandidates
                    .Where(item => PortMatches(item.Port, port)
                        && TextMatches(item.Network, network)
                        && (timestamp - item.Timestamp).Duration() <= SniffingWindow)
                    .OrderBy(item => TextMatches(item.Outbound, outbound) ? 0 : 1)
                    .ThenBy(item => (timestamp - item.Timestamp).Duration())
                    .FirstOrDefault();

                if (sniffing != null)
                {
                    return new XrayDomainMatch(sniffing.Domain, sniffing.Source);
                }
            }

            if (_dnsByAddress.TryGetValue(address, out var dnsCandidates))
            {
                var dns = dnsCandidates
                    .Where(item => item.Timestamp <= timestamp.AddSeconds(2)
                        && timestamp - item.Timestamp <= DnsWindow)
                    .OrderBy(item => (timestamp - item.Timestamp).Duration())
                    .FirstOrDefault();

                if (dns != null)
                {
                    return new XrayDomainMatch(dns.Domain, dns.Source);
                }
            }

            return XrayDomainMatch.Empty;
        }

        private static Dictionary<string, List<XrayDomainEvidence>> BuildIndex(
            IEnumerable<XrayDomainEvidence> evidence)
        {
            return evidence
                .Where(item => IPAddress.TryParse(item.Address, out _) && IsDomainName(item.Domain))
                .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(item => item.Timestamp).ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static bool PortMatches(string evidencePort, string connectionPort)
        {
            return evidencePort.IsNullOrEmpty()
                || connectionPort.IsNullOrEmpty()
                || string.Equals(evidencePort, connectionPort, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TextMatches(string evidenceValue, string connectionValue)
        {
            return evidenceValue.IsNullOrEmpty()
                || connectionValue.IsNullOrEmpty()
                || string.Equals(evidenceValue, connectionValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class XrayTraceAccumulator
    {
        public XrayTraceAccumulator(string id, DateTimeOffset timestamp)
        {
            Id = id;
            FirstSeen = timestamp;
            LastSeen = timestamp;
        }

        public string Id { get; }
        public string Address { get; private set; } = string.Empty;
        public string Port { get; private set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string Outbound { get; set; } = string.Empty;
        public string Domain { get; private set; } = string.Empty;
        public DateTimeOffset FirstSeen { get; private set; }
        public DateTimeOffset LastSeen { get; private set; }
        public DateTimeOffset DestinationSeenAt { get; private set; }
        public DateTimeOffset DomainSeenAt { get; private set; }

        public void Touch(DateTimeOffset timestamp)
        {
            if (timestamp < FirstSeen)
            {
                FirstSeen = timestamp;
            }
            if (timestamp > LastSeen)
            {
                LastSeen = timestamp;
            }
        }

        public void SetDestination(string destination, DateTimeOffset timestamp)
        {
            var host = ExtractHost(destination);
            if (!IPAddress.TryParse(host, out _))
            {
                return;
            }

            Address = host;
            Port = ExtractPort(destination);
            DestinationSeenAt = timestamp;
        }

        public void SetDomain(string domain, DateTimeOffset timestamp)
        {
            var normalized = NormalizeDomain(domain);
            if (!IsDomainName(normalized))
            {
                return;
            }

            Domain = normalized;
            DomainSeenAt = timestamp;
        }
    }

    private sealed class XrayGroupAccumulator
    {
        public XrayGroupAccumulator(
            string destination,
            string host,
            string port,
            string outbound,
            string network,
            string domainName,
            string domainSource,
            DateTimeOffset started)
        {
            Destination = destination;
            Host = host;
            Port = port;
            Outbound = outbound;
            Network = network;
            DomainName = domainName;
            DomainSource = domainSource;
            FirstSeen = started;
            LastSeen = started;
            Count = 1;
        }

        public string Destination { get; }
        public string Host { get; }
        public string Port { get; }
        public string Outbound { get; }
        public string Network { get; }
        public string DomainName { get; }
        public string DomainSource { get; }
        public DateTimeOffset FirstSeen { get; private set; }
        public DateTimeOffset LastSeen { get; private set; }
        public int Count { get; private set; }

        public void Add(DateTimeOffset started)
        {
            if (started < FirstSeen)
            {
                FirstSeen = started;
            }
            if (started > LastSeen)
            {
                LastSeen = started;
            }
            Count++;
        }
    }

    private static string FormatDestinationCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var noun = mod100 is >= 11 and <= 14
            ? "назначений"
            : mod10 == 1
                ? "назначение"
                : mod10 is >= 2 and <= 4
                    ? "назначения"
                    : "назначений";
        return $"{count} {noun}";
    }

    private static string FormatRequestCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var noun = mod100 is >= 11 and <= 14 ? "обращений" : mod10 == 1 ? "обращение" : mod10 is >= 2 and <= 4 ? "обращения" : "обращений";
        return $"{count} {noun}";
    }

    private static string NormalizeOutbound(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return "—";
        }

        var value = tag.Trim();
        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("direct") || lowered.Contains("bypass") || lowered.Contains("freedom"))
        {
            return "Direct";
        }
        if (lowered.Contains("block") || lowered.Contains("reject") || lowered.Contains("blackhole"))
        {
            return "Block";
        }
        if (lowered.Contains("dns") || lowered.Contains("api"))
        {
            return "Other";
        }
        return "VPN";
    }

    private static string DetectRouteKind(string? outbound, string? chain, string? rule)
    {
        var value = $"{outbound} {chain} {rule}".ToLowerInvariant();
        if (value.Contains("block") || value.Contains("reject") || value.Contains("blackhole"))
        {
            return "Block";
        }
        if (value.Contains("direct") || value.Contains("bypass") || value.Contains("freedom"))
        {
            return "Direct";
        }
        if (value.Contains("dns") || value.Contains("api"))
        {
            return "Other";
        }
        if (value.Contains("proxy") || value.Contains("vpn") || value.Contains("selector") || value.Contains("outbound"))
        {
            return "VPN";
        }
        return string.IsNullOrWhiteSpace(outbound) || outbound == "—" ? "Other" : "VPN";
    }

    private static string FormatBytes(ulong value)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double number = value;
        var index = 0;
        while (number >= 1024 && index < units.Length - 1)
        {
            number /= 1024;
            index++;
        }
        return index == 0 ? $"{number:0} {units[index]}" : $"{number:0.0} {units[index]}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays}д {elapsed:hh\\:mm\\:ss}";
        }
        return elapsed.ToString(@"hh\:mm\:ss");
    }

    private static string FormatLastSeen(DateTimeOffset timestamp)
    {
        if (timestamp == default)
        {
            return "—";
        }

        var elapsed = DateTimeOffset.Now - timestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 5)
        {
            return "только что";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)} сек. назад";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} мин. назад";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} ч. назад";
        }

        return $"{Math.Max(1, (int)elapsed.TotalDays)} дн. назад";
    }

    private static string FormatCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var noun = mod100 is >= 11 and <= 14
            ? "соединений"
            : mod10 == 1
                ? "соединение"
                : mod10 is >= 2 and <= 4
                    ? "соединения"
                    : "соединений";
        return $"{count} {noun}";
    }

    private static IReadOnlyList<string> ReadTailLines(
        string path,
        int maxLines,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var readLength = (int)Math.Min(length, maxBytes);
        stream.Seek(-readLength, SeekOrigin.End);
        var buffer = new byte[readLength];
        var totalRead = 0;
        while (totalRead < readLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, totalRead, readLength - totalRead);
            if (read <= 0)
            {
                break;
            }
            totalRead += read;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var text = Encoding.UTF8.GetString(buffer, 0, totalRead);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length <= maxLines ? lines : lines[^maxLines..];
    }

    private static bool TryParseXrayTime(string value, out DateTimeOffset result)
    {
        var formats = new[]
        {
            "yyyy/MM/dd HH:mm:ss.ffffff",
            "yyyy/MM/dd HH:mm:ss.fffff",
            "yyyy/MM/dd HH:mm:ss.ffff",
            "yyyy/MM/dd HH:mm:ss.fff",
            "yyyy/MM/dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            result = new DateTimeOffset(date);
            return true;
        }
        result = default;
        return false;
    }

    private static string ExtractHost(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }
        var value = destination.Trim();
        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            return closing > 0 ? value[1..closing] : value;
        }
        var lastColon = value.LastIndexOf(':');
        return lastColon > 0 && int.TryParse(value[(lastColon + 1)..], out _) ? value[..lastColon] : value;
    }

    private sealed record SingBoxHistoryEntry(SgConnectionRow Row, DateTimeOffset LastSeen);

    public sealed class SgConnectionRow : INotifyPropertyChanged
    {
        public string Id { get; init; } = string.Empty;
        public string Host { get; init; } = "—";
        private string _countryCode = string.Empty;

        public string DestinationDisplay { get; init; } = "—";
        public string DestinationIp { get; init; } = string.Empty;
        public string DomainName { get; init; } = string.Empty;
        public string DomainSource { get; init; } = string.Empty;
        public string PortDisplay { get; init; } = string.Empty;
        public bool HasDomain => DomainName.IsNotEmpty();
        public string SiteDisplay => HasDomain ? DomainName : "Домен не определён";
        public string DomainSourceDisplay => HasDomain && DomainSource.IsNotEmpty() ? DomainSource : "—";
        public string PrimaryDestinationDisplay => HasDomain ? DomainName : DestinationDisplay;
        public string DestinationAddressDisplay
        {
            get
            {
                var address = DestinationIp.IsNotEmpty()
                    ? DestinationIp
                    : DestinationDisplay;
                if (address.IsNullOrEmpty() || address == "—")
                {
                    return "—";
                }

                var displayAddress = address.Contains(':', StringComparison.Ordinal)
                    && !address.StartsWith("[", StringComparison.Ordinal)
                    ? $"[{address}]"
                    : address;
                return $"{displayAddress}{PortDisplay}";
            }
        }
        public string SecondaryDestinationDisplay => HasDomain
            ? (DestinationIp.IsNotEmpty() ? DestinationAddressDisplay : "IP не передан Xray")
            : "Домен не определён";
        public string CountryCode
        {
            get => _countryCode;
            set
            {
                var normalized = SgCountryHelper.NormalizeCode(value);
                if (string.Equals(_countryCode, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _countryCode = normalized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountryCode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountryFlagUri)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountryToolTip)));
            }
        }
        public string CountryFlagUri => $"pack://application:,,,/Assets/Flags/{(CountryCode.Length == 2 ? CountryCode : "ZZ")}.png";
        public string CountryToolTip => CountryCode.Length == 2
            ? CountryCode
            : HasDomain && DestinationIp.IsNullOrEmpty()
                ? "Страна недоступна: Xray передал домен без IP."
                : "Страна не определена";
        public string Source { get; init; } = string.Empty;
        private string _ruleDisplay = "—";
        public string RuleDisplay
        {
            get => _ruleDisplay;
            set
            {
                if (string.Equals(_ruleDisplay, value, StringComparison.Ordinal))
                {
                    return;
                }
                _ruleDisplay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RuleDisplay)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            }
        }
        public string RuleName { get; init; } = string.Empty;
        public string RulePayload { get; init; } = string.Empty;
        public string OutboundDisplay { get; init; } = "—";
        public string OutboundTag { get; init; } = string.Empty;
        public string TunnelDisplay { get; init; } = "—";
        public string ProtocolDisplay { get; init; } = "—";
        public string ProcessDisplay { get; init; } = "—";
        public ulong DownloadBytes { get; init; }
        public ulong UploadBytes { get; init; }
        public string DownloadDisplay { get; init; } = "—";
        public string UploadDisplay { get; init; } = "—";
        public string ElapsedDisplay { get; init; } = "—";
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset FirstSeenAt { get; init; }
        public int HitCount { get; init; } = 1;
        public string HitCountDisplay => HitCount.ToString(CultureInfo.InvariantCulture);
        public bool IsXrayAccessLog => string.Equals(Source, "Xray access log", StringComparison.Ordinal);
        public string DetailTimeLabel => IsXrayAccessLog ? "Последнее:" : "Время соединения:";
        public string DetailTimeDisplay => IsXrayAccessLog ? FormatLastSeen(StartedAt) : ElapsedDisplay;
        public string RouteKind { get; init; } = "Other";
        public bool CanClose { get; init; }
        public bool IsLive { get; init; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public string SearchText => string.Join(' ', new[]
        {
            Host,
            DestinationDisplay,
            DestinationIp,
            DomainName,
            DomainSource,
            DestinationAddressDisplay,
            PortDisplay,
            CountryCode,
            CountryToolTip,
            RuleDisplay,
            OutboundDisplay,
            TunnelDisplay,
            ProtocolDisplay,
            ProcessDisplay,
            Source,
            RouteKind
        });
    }
}
