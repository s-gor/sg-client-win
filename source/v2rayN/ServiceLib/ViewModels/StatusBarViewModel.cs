namespace ServiceLib.ViewModels;

public class StatusBarViewModel : MyReactiveObject
{
    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new(null));
    public static StatusBarViewModel Instance => _instance.Value;

    private readonly SgTrafficStatisticsManager _trafficStatistics =
        SgTrafficStatisticsManager.Instance;

    private int _awgTrafficPolling;

    // Refreshing the tray profile list changes SelectedServer programmatically.
    // That must never be interpreted as a user request to switch back from AWG
    // to the previous Xray/sing-box profile.
    private bool _suppressServerSelectionChanged;

    #region ObservableCollection

    public IObservableCollection<RoutingItem> RoutingItems { get; } = new ObservableCollectionExtended<RoutingItem>();

    public IObservableCollection<ComboItem> Servers { get; } = new ObservableCollectionExtended<ComboItem>();

    [Reactive]
    public RoutingItem SelectedRouting { get; set; }

    [Reactive]
    public ComboItem SelectedServer { get; set; }

    [Reactive]
    public bool BlServers { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<Unit, Unit> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaScanCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> NotifyLeftClickCmd { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCmd { get; }
    public ReactiveCommand<Unit, Unit> HideWindowCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleTunCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleTunModeCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleSystemProxyModeCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleLocalProxyModeCmd { get; }
    public ReactiveCommand<Unit, Unit> EnableTunCmd { get; }
    public ReactiveCommand<Unit, Unit> DisableTunCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleQuickKillSwitchCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleQuickAutoRecoverCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleQuickLocalNetworkCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleQuickDnsCmd { get; }
    public ReactiveCommand<Unit, Unit> ToggleQuickAutoRunCmd { get; }
    public ReactiveCommand<Unit, Unit> OpenSplitTunnelCmd { get; }
    public ReactiveCommand<Unit, Unit> OpenReserveProfileCmd { get; }
    public ReactiveCommand<Unit, Unit> OpenRoutingModeCmd { get; }
    public ReactiveCommand<Unit, Unit> OpenDpiModeCmd { get; }

    #region System Proxy

    [Reactive]
    public bool BlSystemProxyClear { get; set; }

    [Reactive]
    public bool BlSystemProxySet { get; set; }

    [Reactive]
    public bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<Unit, Unit> SystemProxyClearCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxySetCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyNothingCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyPacCmd { get; }

    [Reactive]
    public bool BlRouting { get; set; }

    [Reactive]
    public int SystemProxySelected { get; set; }

    [Reactive]
    public bool BlSystemProxyPacVisible { get; set; }

    #endregion System Proxy

    #region UI

    [Reactive]
    public string InboundDisplay { get; set; }

    [Reactive]
    public string InboundLanDisplay { get; set; }

    [Reactive]
    public string RunningServerDisplay { get; set; }

    [Reactive]
    public string RunningServerToolTipText { get; set; }

    [Reactive]
    public string RunningInfoDisplay { get; set; }

    [Reactive]
    public string SpeedProxyDisplay { get; set; }

    [Reactive]
    public string SpeedDirectDisplay { get; set; }

    [Reactive]
    public string TrafficProfileNameDisplay { get; set; }

    [Reactive]
    public string TrafficCurrentDownloadDisplay { get; set; }

    [Reactive]
    public string TrafficCurrentUploadDisplay { get; set; }

    [Reactive]
    public double TrafficDownloadLevel { get; set; }

    [Reactive]
    public double TrafficUploadLevel { get; set; }

    [Reactive]
    public string TrafficSessionDownloadDisplay { get; set; }

    [Reactive]
    public string TrafficSessionUploadDisplay { get; set; }

    [Reactive]
    public string TrafficTodayDownloadDisplay { get; set; }

    [Reactive]
    public string TrafficTodayUploadDisplay { get; set; }

    [Reactive]
    public string TrafficMonthDownloadDisplay { get; set; }

    [Reactive]
    public string TrafficMonthUploadDisplay { get; set; }

    [Reactive]
    public string TrafficTotalDownloadDisplay { get; set; }

    [Reactive]
    public string TrafficTotalUploadDisplay { get; set; }

    [Reactive]
    public bool EnableTun { get; set; }

    [Reactive]
    public bool TunBusy { get; set; }

    [Reactive]
    public ETunUiState TunUiState { get; set; }

    [Reactive]
    public string TunStatusText { get; set; }

    [Reactive]
    public string TunDetailText { get; set; }

    [Reactive]
    public string TunButtonText { get; set; }

    [Reactive]
    public string TunTrayActionText { get; set; }

    [Reactive]
    public string ConnectionModeKey { get; set; }

    [Reactive]
    public string TunModeButtonText { get; set; }

    [Reactive]
    public string SystemProxyModeButtonText { get; set; }

    [Reactive]
    public bool CanUseSystemProxyMode { get; set; }

    [Reactive]
    public string SystemProxyModeToolTip { get; set; }

    [Reactive]
    public string LocalProxyModeButtonText { get; set; }

    [Reactive]
    public bool IsTunModeActive { get; set; }

    [Reactive]
    public bool IsSystemProxyModeActive { get; set; }

    [Reactive]
    public bool IsLocalProxyModeActive { get; set; }

    [Reactive]
    public string ProfileNameDisplay { get; set; }

    [Reactive]
    public string ProfileProtocolDisplay { get; set; }

    [Reactive]
    public string CoreDisplay { get; set; }

    [Reactive]
    public bool QuickKillSwitch { get; set; }

    [Reactive]
    public string QuickKillSwitchStatus { get; set; }

    [Reactive]
    public bool QuickAutoRecover { get; set; }

    [Reactive]
    public bool QuickAllowLocalNetwork { get; set; }

    [Reactive]
    public bool QuickDnsThroughTun { get; set; }

    [Reactive]
    public string QuickDnsStatusDisplay { get; set; }

    [Reactive]
    public string QuickLocalNetworkStatusDisplay { get; set; }

    [Reactive]
    public bool QuickAutoRun { get; set; }

    [Reactive]
    public string QuickSplitTunnelSummary { get; set; }

    [Reactive]
    public string QuickReserveProfileSummary { get; set; }

    [Reactive]
    public string QuickRoutingSummary { get; set; }

    [Reactive]
    public string QuickDpiSummary { get; set; }

    [Reactive]
    public bool BlIsNonWindows { get; set; }

    #endregion UI

    public StatusBarViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        SelectedRouting = new();
        SelectedServer = new();
        RunningServerToolTipText = "-";
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();
        UpdateTrafficDisplays(_trafficStatistics.Current);

        EnableTun = _config.TunModeItem.EnableTun;
        ProfileNameDisplay = "Профиль не выбран";
        ProfileProtocolDisplay = "—";
        CoreDisplay = "—";
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        if (_config.SgQuickSettingsItem.ConnectionMode.IsNullOrEmpty()
            || _config.SgQuickSettingsItem.ConnectionMode
                is not ("off" or "tun" or "system-proxy" or "local-proxy"))
        {
            _config.SgQuickSettingsItem.ConnectionMode = _config.TunModeItem.EnableTun
                ? "tun"
                : _config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange
                    ? "system-proxy"
                    : "off";
        }
        ConnectionModeKey = _config.SgQuickSettingsItem.ConnectionMode;
        QuickKillSwitch = _config.SgQuickSettingsItem.KillSwitchEnabled;
        QuickKillSwitchStatus = SgKillSwitchManager.Instance.IsEmergencyBlockActive ? "Блокирует интернет" : (QuickKillSwitch ? "Готов" : "Выключен");
        QuickAutoRecover = _config.SgQuickSettingsItem.AutoRecoverTun;
        QuickAllowLocalNetwork = _config.SgQuickSettingsItem.AllowLocalNetwork;
        QuickDnsThroughTun = _config.SgQuickSettingsItem.DnsThroughTun;
        QuickAutoRun = _config.GuiItem.AutoRun;
        RefreshQuickSummaries();
        ConfigHandler.ApplySgLocalNetworkPreference(_config);
        ApplyTunState(EnableTun ? ETunUiState.Starting : ETunUiState.Off);
        if (!EnableTun && ConnectionModeKey is "system-proxy" or "local-proxy")
        {
            ReportProxyStarting(ConnectionModeKey);
        }
        RefreshModeButtons();

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(
                x => x.SelectedRouting,
                y => y != null && !y.Remarks.IsNullOrEmpty())
            .Subscribe(async c => await RoutingSelectedChangedAsync(c));

        this.WhenAnyValue(
                x => x.SelectedServer,
                y => y != null && !y.Text.IsNullOrEmpty())
            .Subscribe(ServerSelectedChanged);

        SystemProxySelected = (int)(
            ConnectionModeKey == "system-proxy"
                ? ESysProxyType.ForcedChange
                : ESysProxyType.ForcedClear);
        this.WhenAnyValue(
                x => x.SystemProxySelected,
                y => y >= 0)
            .Subscribe(async c => await DoSystemProxySelected(c));

        ToggleTunCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (ConnectionModeKey is "system-proxy" or "local-proxy")
            {
                await ApplyConnectionModeAsync("off");
            }
            else
            {
                await SetTunEnabledAsync(!EnableTun);
            }
        });
        ToggleTunModeCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                // A transition can occasionally remain marked as busy while
                // the real TUN/AWG tunnel is already alive. Treat the persisted
                // TUN state as active as well, so this button always remains an
                // escape hatch and can turn the connection off.
                var isActive = GetConnectionModeKey() == "tun"
                    && (EnableTun
                        || _config.TunModeItem.EnableTun
                        || TunUiState is ETunUiState.On or ETunUiState.Starting or ETunUiState.Switching);
                await ApplyConnectionModeAsync(isActive ? "off" : "tun");
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Main TUN mode action", ex);
                ReportTunError(ex.Message.IsNotEmpty()
                    ? ex.Message
                    : "Не удалось переключить режим TUN.");
            }
        });
        ToggleSystemProxyModeCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var isActive = GetConnectionModeKey() == "system-proxy"
                    && TunUiState == ETunUiState.On;
                await ApplyConnectionModeAsync(
                    isActive ? "off" : "system-proxy");
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Main system proxy mode action", ex);
                ReportTunError(ex.Message.IsNotEmpty()
                    ? ex.Message
                    : "Не удалось переключить системный прокси.");
            }
        });
        ToggleLocalProxyModeCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var isActive = GetConnectionModeKey() == "local-proxy"
                    && TunUiState == ETunUiState.On;
                await ApplyConnectionModeAsync(
                    isActive ? "off" : "local-proxy");
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Main local proxy mode action", ex);
                ReportTunError(ex.Message.IsNotEmpty()
                    ? ex.Message
                    : "Не удалось переключить локальный прокси.");
            }
        });
        EnableTunCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetTunEnabledAsync(true);
        });
        DisableTunCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetTunEnabledAsync(false);
        });
        ToggleQuickKillSwitchCmd = ReactiveCommand.CreateFromTask(ToggleQuickKillSwitchAsync);
        ToggleQuickAutoRecoverCmd = ReactiveCommand.CreateFromTask(ToggleQuickAutoRecoverAsync);
        ToggleQuickLocalNetworkCmd = ReactiveCommand.CreateFromTask(ToggleQuickLocalNetworkAsync);
        ToggleQuickDnsCmd = ReactiveCommand.CreateFromTask(ToggleQuickDnsAsync);
        ToggleQuickAutoRunCmd = ReactiveCommand.CreateFromTask(ToggleQuickAutoRunAsync);
        OpenSplitTunnelCmd = ReactiveCommand.CreateFromTask(OpenSplitTunnelAsync);
        OpenReserveProfileCmd = ReactiveCommand.CreateFromTask(OpenReserveProfileAsync);
        OpenRoutingModeCmd = ReactiveCommand.CreateFromTask(OpenRoutingModeAsync);
        OpenDpiModeCmd = ReactiveCommand.CreateFromTask(OpenDpiModeAsync);

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyConnectionModeAsync("off");
        });
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyConnectionModeAsync("system-proxy");
        });
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        });
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        if (updateView != null)
        {
            InitUpdateView(updateView);
        }

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await UpdateStatistics(result));

        AppEvents.RoutingsMenuRefreshRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await RefreshRoutingsMenu());

        AppEvents.TestServerRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await TestServerAvailability());

        AppEvents.InboundDisplayRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await InboundDisplayStatus());

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await SetListenerType(ESysProxyType.ForcedClear));

        #endregion AppEvents

        _ = Init();
        _ = RunTunWatchdogAsync();
    }

    private async Task Init()
    {
        var mode = GetConnectionModeKey();
        var startupProxyType = mode == "system-proxy"
            ? ESysProxyType.ForcedChange
            : ESysProxyType.ForcedClear;

        // Never publish a Windows proxy before the local mixed port is ready.
        // MainWindowViewModel.Reload applies ForcedChange after the core starts.
        _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        SystemProxySelected = (int)startupProxyType;

        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        await ChangeSystemProxyAsync(ESysProxyType.ForcedClear, true);
        await ConfigHandler.SaveConfig(_config);
    }

    public void InitUpdateView(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _updateView = updateView;
        if (_updateView != null)
        {
            AppEvents.ProfilesRefreshRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async _ => await RefreshServersBiz()); //.DisposeWith(_disposables);
        }
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await _updateView?.Invoke(EViewAction.SetClipboardData, sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AppEvents.AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AppEvents.AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        AppEvents.SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }

    private async Task RefreshServersBiz()
    {
        await RefreshServersMenu();

        var activeAwgProfile = AmneziaWgManager.Instance.GetProfile(
            AmneziaWgManager.Instance.ActiveProfileId);
        if (activeAwgProfile != null)
        {
            ProfileNameDisplay = CleanProfileName(activeAwgProfile.Name, string.Empty, "AmneziaWG");
            ProfileProtocolDisplay = "AmneziaWG";
            RunningServerDisplay = ProfileNameDisplay;
            UpdateTrafficDisplays(_trafficStatistics.SetActiveProfile(activeAwgProfile.Id, ProfileNameDisplay));
            if (EnableTun && TunUiState == ETunUiState.On)
            {
                CoreDisplay = "AmneziaWG";
            }
        }
        else
        {
            var running = await ConfigHandler.GetDefaultServer(_config);
            if (running != null)
            {
                ProfileNameDisplay = CleanProfileName(running.Remarks, running.CountryCode, "Подключение");
                ProfileProtocolDisplay = GetProtocolDisplay(running);
                RunningServerDisplay = ProfileNameDisplay;

                // A highlighted/default profile is not necessarily the profile carrying traffic.
                // Attribute bytes only while the Xray/sing-box core is actually running.
                UpdateTrafficDisplays(
                    CoreManager.Instance.IsCoreRunning
                        ? _trafficStatistics.SetActiveProfile(running.IndexId, ProfileNameDisplay)
                        : _trafficStatistics.SetIdle());
            }
            else
            {
                var selectedAwgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
                if (selectedAwgProfile != null)
                {
                    ProfileNameDisplay = CleanProfileName(selectedAwgProfile.Name, string.Empty, "AmneziaWG");
                    ProfileProtocolDisplay = "AmneziaWG";
                    RunningServerDisplay = ProfileNameDisplay;
                    UpdateTrafficDisplays(_trafficStatistics.SetIdle());
                }
                else
                {
                    ProfileNameDisplay = "Выберите профиль";
                    ProfileProtocolDisplay = "—";
                    RunningServerDisplay = ProfileNameDisplay;
                    UpdateTrafficDisplays(_trafficStatistics.SetIdle());
                }
            }
        }
        RefreshQuickSummaries();
        RefreshModeButtons();
        UpdateTrayText();
    }

    private async Task RefreshServersMenu()
    {
        var coreProfiles = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");
        var awgProfiles = AmneziaWgManager.Instance.GetProfiles();
        var totalCount = coreProfiles.Count + awgProfiles.Count;

        _suppressServerSelectionChanged = true;
        try
        {
            Servers.Clear();
            SelectedServer = new ComboItem();

            if (totalCount > _config.GuiItem.TrayMenuServersLimit)
            {
                BlServers = false;
                return;
            }

            BlServers = true;
            foreach (var it in coreProfiles)
            {
                Servers.Add(new ComboItem
                {
                    ID = it.IndexId,
                    Text = it.GetSummary()
                });
            }

            foreach (var awg in awgProfiles)
            {
                Servers.Add(new ComboItem
                {
                    ID = awg.Id,
                    Text = $"AmneziaWG · {awg.Name}"
                });
            }

            var selectedId = AmneziaWgManager.Instance.SelectedProfileId;
            if (selectedId.IsNullOrEmpty())
            {
                selectedId = _config.IndexId;
            }

            SelectedServer = Servers.FirstOrDefault(item =>
                string.Equals(item.ID, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? new ComboItem();
        }
        finally
        {
            _suppressServerSelectionChanged = false;
        }
    }

    private void ServerSelectedChanged(bool c)
    {
        if (!c || _suppressServerSelectionChanged)
        {
            return;
        }
        if (SelectedServer == null)
        {
            return;
        }
        if (SelectedServer.ID.IsNullOrEmpty())
        {
            return;
        }

        Logging.SaveLog($"Tray profile selected by user: {SelectedServer.ID}");
        AppEvents.SetDefaultServerRequested.Publish(SelectedServer.ID);
    }

    public async Task TestServerAvailability()
    {
        var awgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
        if (awgProfile != null)
        {
            var status = await AmneziaWgManager.Instance.QueryStatusAsync(awgProfile);
            var message = status.LastHandshake == null
                ? status.Message
                : $"Handshake AmneziaWG: {status.LastHandshake:dd.MM.yyyy HH:mm:ss}";
            NoticeManager.Instance.SendMessageEx(message);
            await TestServerAvailabilitySub(message);
            return;
        }

        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            return;
        }

        await TestServerAvailabilitySub(ResUI.Speedtesting);
        var msg = await Task.Run(ConnectionHandler.RunAvailabilityCheck);
        NoticeManager.Instance.SendMessageEx(msg);
        await TestServerAvailabilitySub(msg);
    }

    private async Task TestServerAvailabilitySub(string msg)
    {
        RxSchedulers.MainThreadScheduler.Schedule(msg, (scheduler, msg) =>
        {
            _ = TestServerAvailabilityResult(msg);
            return Disposable.Empty;
        });
        await Task.CompletedTask;
    }

    public async Task TestServerAvailabilityResult(string msg)
    {
        RunningInfoDisplay = msg;
        await Task.CompletedTask;
    }

    #region System proxy and Routings

    public string GetConnectionModeKey()
    {
        if (_config.TunModeItem.EnableTun)
        {
            return "tun";
        }

        var mode = _config.SgQuickSettingsItem?.ConnectionMode;
        if (mode is "system-proxy" or "local-proxy" or "off")
        {
            return mode;
        }

        return _config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange
            ? "system-proxy"
            : "off";
    }

    public async Task ApplyConnectionModeAsync(string mode)
    {
        mode = mode?.Trim().ToLowerInvariant() ?? "tun";
        if (mode is not ("tun" or "system-proxy" or "local-proxy" or "off"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Неизвестный режим подключения.");
        }

        if (mode != "tun"
            && mode != "off"
            && AmneziaWgManager.Instance.GetSelectedProfile() != null)
        {
            NoticeManager.Instance.Enqueue(
                "AmneziaWG работает только через TUN. "
                + "Для System Proxy выберите профиль Xray или sing-box.");
            RefreshModeButtons();
            return;
        }


        try
        {
            _config.SgQuickSettingsItem.ConnectionMode = mode;
            ConnectionModeKey = mode;

            if (mode == "tun")
            {
                _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
                SetSystemProxySelectedSilently(ESysProxyType.ForcedClear);
                await ChangeSystemProxyAsync(ESysProxyType.ForcedClear, true);
                await ConfigHandler.SaveConfig(_config);
                await SetTunEnabledAsync(true);
                return;
            }

            _config.TunModeItem.EnableTun = false;
            EnableTun = false;
            _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
            SetSystemProxySelectedSilently(ESysProxyType.ForcedClear);
            await ChangeSystemProxyAsync(ESysProxyType.ForcedClear, true);
            await ConfigHandler.SaveConfig(_config);

            await StopAllTunEnginesAsync();

            if (mode == "off")
            {
                ReportConnectionOff();
                AppEvents.ProfilesRefreshRequested.Publish();
                return;
            }

            ReportProxyStarting(mode);
            if (MainWindowViewModel.Instance == null)
            {
                throw new InvalidOperationException(
                    "Главное окно ещё не готово к запуску прокси.");
            }

            await MainWindowViewModel.Instance.Reload();
            if (!CoreManager.Instance.IsCoreRunning)
            {
                throw new InvalidOperationException(
                    MainWindowViewModel.Instance.LastProxyStartError
                        .NullIfEmpty()
                        ?? "Локальное ядро не запустилось.");
            }
        }
        catch (Exception ex)
        {
            // A failed proxy start must never leave Windows pointing at a dead
            // local port. Roll back to the safe disconnected state.
            _config.SgQuickSettingsItem.ConnectionMode = "off";
            ConnectionModeKey = "off";
            _config.TunModeItem.EnableTun = false;
            EnableTun = false;
            _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
            SetSystemProxySelectedSilently(ESysProxyType.ForcedClear);

            try
            {
                await ChangeSystemProxyAsync(
                    ESysProxyType.ForcedClear,
                    true);
            }
            catch (Exception proxyRollbackError)
            {
                Logging.SaveLog(
                    "Rollback failed connection mode",
                    proxyRollbackError);
            }

            await ConfigHandler.SaveConfig(_config);
            ReportTunError(
                ex.Message.IsNotEmpty()
                    ? ex.Message
                    : "Не удалось переключить режим подключения.");
            throw;
        }
    }

    public async Task StopConnectionForMaintenanceAsync()
    {
        if (_config.TunModeItem.EnableTun)
        {
            await SetTunEnabledAsync(false);
        }
        else
        {
            await SetListenerType(ESysProxyType.ForcedClear);
            await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(20));
            await AmneziaWgManager.Instance.DisconnectAllAsync();
        }
    }

    public async Task RestoreConnectionAfterMaintenanceAsync(string mode)
    {
        await ApplyConnectionModeAsync(mode);
    }

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SetSystemProxySelectedSilently(_config.SystemProxyItem.SysProxyType);
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        await SysProxyHandler.UpdateSysProxy(_config, false);

        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            _updateView?.Invoke(EViewAction.DispatcherRefreshIcon, null);
        }
    }

    private async Task RefreshRoutingsMenu()
    {
        RoutingItems.Clear();

        BlRouting = true;
        var routings = await AppManager.Instance.RoutingItems();
        foreach (var item in routings)
        {
            RoutingItems.Add(item);
            if (item.IsActive)
            {
                SelectedRouting = item;
            }
        }
    }

    private async Task RoutingSelectedChangedAsync(bool c)
    {
        if (!c)
        {
            return;
        }

        if (SelectedRouting == null)
        {
            return;
        }

        var item = await AppManager.Instance.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            AppEvents.ReloadRequested.Publish();
            _updateView?.Invoke(EViewAction.DispatcherRefreshIcon, null);
        }
    }

    private void SetSystemProxySelectedSilently(ESysProxyType type)
    {
        _suppressSystemProxySelectionChanged = true;
        try
        {
            SystemProxySelected = (int)type;
        }
        finally
        {
            _suppressSystemProxySelectionChanged = false;
        }
    }

    private async Task DoSystemProxySelected(bool c)
    {
        if (!c || _suppressSystemProxySelectionChanged)
        {
            return;
        }

        var selected = (ESysProxyType)SystemProxySelected;
        if (selected == ESysProxyType.ForcedChange)
        {
            if (GetConnectionModeKey() != "system-proxy")
            {
                await ApplyConnectionModeAsync("system-proxy");
            }
            return;
        }

        if (selected == ESysProxyType.ForcedClear
            && GetConnectionModeKey() is "system-proxy" or "local-proxy")
        {
            await ApplyConnectionModeAsync("off");
            return;
        }

        if (_config.SystemProxyItem.SysProxyType != selected)
        {
            await SetListenerType(selected);
        }
    }

    private bool _tunSwitching;
    private bool _suppressSystemProxySelectionChanged;
    private int _healthFailureCount;

    private async Task SetTunEnabledAsync(bool enabled)
    {
        if (_tunSwitching || TunBusy)
        {
            return;
        }

        _tunSwitching = true;
        try
        {
            await using var operation = await TunOperationCoordinator.EnterAsync(enabled ? "enable TUN" : "disable TUN");
            ApplyTunState(enabled ? ETunUiState.Starting : ETunUiState.Stopping);
            if (!enabled && SgKillSwitchManager.Instance.IsEmergencyBlockActive)
            {
                await SgKillSwitchManager.Instance.DeactivateEmergencyBlockAsync();
                QuickKillSwitchStatus = QuickKillSwitch ? "Готов" : "Выключен";
            }
            if (enabled && AllowEnableTun() == false)
            {
                if (Utils.IsWindows())
                {
                    // Preserve the requested ON state before restarting elevated.
                    _config.TunModeItem.EnableTun = true;
                    EnableTun = true;
                    await ConfigHandler.SaveConfig(_config);

                    if (ProcUtils.RebootAsAdmin())
                    {
                        await AppManager.Instance.AppExitAsync(true);
                    }
                    else
                    {
                        _config.TunModeItem.EnableTun = false;
                        EnableTun = false;
                        await ConfigHandler.SaveConfig(_config);
                        ReportTunError("Не удалось перезапустить SG Client с правами администратора.");
                    }
                    return;
                }

                bool? passwordResult = await _updateView?.Invoke(EViewAction.PasswordInput, null);
                if (passwordResult == false)
                {
                    _config.TunModeItem.EnableTun = false;
                    EnableTun = false;
                    ReportTunError("Не получены права для создания TUN-интерфейса.");
                    return;
                }
            }

            if (_config.TunModeItem.EnableTun == enabled && EnableTun == enabled)
            {
                if (enabled)
                {
                    var awgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
                    if (awgProfile != null)
                    {
                        var awgStatus = await AmneziaWgManager.Instance.QueryStatusAsync(awgProfile);
                        if (awgStatus.State == "connected" && awgStatus.LastHandshake != null)
                        {
                            ReportAwgRunning(awgProfile, awgStatus.LastHandshake.Value);
                        }
                        else
                        {
                            ReportTunStarting();
                            AppEvents.ReloadRequested.Publish();
                        }
                    }
                    else if (CoreManager.Instance.IsCoreRunning)
                    {
                        ReportTunRunning();
                    }
                    else
                    {
                        ReportTunStarting();
                        AppEvents.ReloadRequested.Publish();
                    }
                }
                else
                {
                    _config.SgQuickSettingsItem.ConnectionMode = "off";
                    ConnectionModeKey = "off";
                    await ConfigHandler.SaveConfig(_config);
                    await StopAllTunEnginesAsync();
                    ReportConnectionOff();
                }
                return;
            }

            _config.TunModeItem.EnableTun = enabled;
            EnableTun = enabled;
            _config.SgQuickSettingsItem.ConnectionMode = enabled ? "tun" : "off";
            ConnectionModeKey = _config.SgQuickSettingsItem.ConnectionMode;

            _config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
            SystemProxySelected = (int)ESysProxyType.ForcedClear;
            await ChangeSystemProxyAsync(ESysProxyType.ForcedClear, false);
            await ConfigHandler.SaveConfig(_config);

            if (!enabled)
            {
                await StopAllTunEnginesAsync();
                ReportTunStopped();
                AppEvents.ProfilesRefreshRequested.Publish();
            }
            else
            {
                AppEvents.ProfilesRefreshRequested.Publish();
                AppEvents.ReloadRequested.Publish();
            }

            if (_updateView != null)
            {
                await _updateView.Invoke(EViewAction.DispatcherRefreshIcon, null);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Set TUN state", ex);
            _config.TunModeItem.EnableTun = false;
            EnableTun = false;
            _config.SgQuickSettingsItem.ConnectionMode = "off";
            ConnectionModeKey = "off";
            await ConfigHandler.SaveConfig(_config);
            try
            {
                await StopAllTunEnginesAsync();
            }
            catch (Exception cleanupEx)
            {
                Logging.SaveLog("Cleanup TUN engines after failure", cleanupEx);
            }
            ReportTunError(ex.Message.IsNullOrEmpty()
                ? "Не удалось изменить состояние TUN. Откройте журнал для подробностей."
                : ex.Message);
        }
        finally
        {
            _tunSwitching = false;
        }
    }

    private async Task StopAllTunEnginesAsync()
    {
        var errors = new List<Exception>();

        try
        {
            await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            errors.Add(ex);
            Logging.SaveLog("Stop Xray/sing-box TUN engine", ex);
        }

        if (Utils.IsWindows())
        {
            try
            {
                await WindowsUtils.RemoveTunDevice().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                Logging.SaveLog("Remove SG Client TUN adapter", ex);
            }
        }

        try
        {
            await AmneziaWgManager.Instance.DisconnectAllAsync().WaitAsync(TimeSpan.FromSeconds(55));
        }
        catch (Exception ex)
        {
            errors.Add(ex);
            Logging.SaveLog("Stop AmneziaWG TUN engine", ex);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Не удалось полностью остановить TUN. Подробности сохранены в журнале.",
                new AggregateException(errors));
        }
    }

    private async Task RunTunWatchdogAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(12));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                if (!_config.TunModeItem.EnableTun || TunBusy || TunOperationCoordinator.IsBusy)
                {
                    _healthFailureCount = 0;
                    continue;
                }

                var healthy = await IsCurrentTunHealthyAsync();
                if (healthy)
                {
                    _healthFailureCount = 0;
                    continue;
                }

                _healthFailureCount++;
                Logging.SaveLog($"TUN watchdog failure {_healthFailureCount}/2");
                if (_healthFailureCount < 2)
                {
                    continue;
                }

                _healthFailureCount = 0;
                string? reserveProfileId = null;
                var confirmedFailure = false;

                await using (var operation = await TunOperationCoordinator.EnterAsync("watchdog emergency cleanup"))
                {
                    if (!_config.TunModeItem.EnableTun || TunBusy)
                    {
                        continue;
                    }

                    if (await IsCurrentTunHealthyAsync())
                    {
                        continue;
                    }

                    confirmedFailure = true;
                    try
                    {
                        await StopAllTunEnginesAsync();
                    }
                    catch (Exception cleanupEx)
                    {
                        Logging.SaveLog("TUN watchdog cleanup", cleanupEx);
                    }

                    var currentId = AmneziaWgManager.Instance.SelectedProfileId.IsNotEmpty()
                        ? AmneziaWgManager.Instance.SelectedProfileId
                        : _config.IndexId;
                    var settings = _config.SgQuickSettingsItem;
                    if (settings.AutoFailoverEnabled
                        && settings.ReserveProfileId.IsNotEmpty()
                        && !string.Equals(settings.ReserveProfileId, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        reserveProfileId = settings.ReserveProfileId;
                        ReportProfileSwitching(settings.ReserveProfileName, "Резервный профиль", string.Empty);
                    }
                }

                if (!confirmedFailure)
                {
                    continue;
                }

                if (reserveProfileId.IsNotEmpty() && ProfilesViewModel.Instance != null)
                {
                    Logging.SaveLog($"TUN watchdog starts reserve profile: {reserveProfileId}");
                    var switched = await ProfilesViewModel.Instance.ActivateProfileAsync(reserveProfileId);
                    if (switched && TunUiState == ETunUiState.On)
                    {
                        NoticeManager.Instance.Enqueue($"SG Client переключился на резервный профиль «{_config.SgQuickSettingsItem.ReserveProfileName}».");
                        continue;
                    }
                    Logging.SaveLog($"Reserve profile failed: {reserveProfileId}");
                }

                _config.TunModeItem.EnableTun = false;
                EnableTun = false;
                await ConfigHandler.SaveConfig(_config);

                if (_config.SgQuickSettingsItem.KillSwitchEnabled)
                {
                    try
                    {
                        var endpointHosts = await GetKillSwitchEndpointHostsAsync();
                        await SgKillSwitchManager.Instance.ActivateEmergencyBlockAsync(endpointHosts, _config.SgQuickSettingsItem.AllowLocalNetwork);
                        QuickKillSwitchStatus = "Блокирует интернет";
                    }
                    catch (Exception killSwitchEx)
                    {
                        Logging.SaveLog("Activate SG Client Kill Switch", killSwitchEx);
                        QuickKillSwitchStatus = "Ошибка включения";
                    }
                }

                ReportTunError(SgKillSwitchManager.Instance.IsEmergencyBlockActive
                    ? "TUN неожиданно остановился. Kill Switch заблокировал интернет."
                    : "TUN неожиданно остановился. Нажмите «Повторить TUN» или откройте журнал.");
                AppEvents.ProfilesRefreshRequested.Publish();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("TUN watchdog check", ex);
            }
        }
    }

    private async Task<bool> IsCurrentTunHealthyAsync()
    {
        var awgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
        if (awgProfile != null)
        {
            var status = await AmneziaWgManager.Instance.QueryStatusAsync(awgProfile).WaitAsync(TimeSpan.FromSeconds(8));
            return status.State == "connected" && status.ServicePresent;
        }
        return CoreManager.Instance.IsCoreRunning;
    }

    private async Task<List<string>> GetKillSwitchEndpointHostsAsync()
    {
        var result = new List<string>();
        var awg = AmneziaWgManager.Instance.GetSelectedProfile();
        if (awg != null && awg.EndpointHost.IsNotEmpty())
        {
            result.Add(awg.EndpointHost);
        }
        else
        {
            var profile = await ConfigHandler.GetDefaultServer(_config);
            if (profile?.Address.IsNotEmpty() == true)
            {
                result.Add(profile.Address);
            }
        }

        var reserveId = _config.SgQuickSettingsItem.ReserveProfileId;
        if (reserveId.IsNotEmpty())
        {
            if (AmneziaWgManager.IsAwgProfileId(reserveId))
            {
                var reserveAwg = AmneziaWgManager.Instance.GetProfile(reserveId);
                if (reserveAwg?.EndpointHost.IsNotEmpty() == true)
                {
                    result.Add(reserveAwg.EndpointHost);
                }
            }
            else
            {
                var reserve = await AppManager.Instance.GetProfileItem(reserveId);
                if (reserve?.Address.IsNotEmpty() == true)
                {
                    result.Add(reserve.Address);
                }
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task ToggleQuickKillSwitchAsync()
    {
        QuickKillSwitch = !QuickKillSwitch;
        _config.SgQuickSettingsItem.KillSwitchEnabled = QuickKillSwitch;
        if (!QuickKillSwitch && SgKillSwitchManager.Instance.IsEmergencyBlockActive)
        {
            try
            {
                await SgKillSwitchManager.Instance.DeactivateEmergencyBlockAsync();
            }
            catch (Exception ex)
            {
                QuickKillSwitch = true;
                _config.SgQuickSettingsItem.KillSwitchEnabled = true;
                Logging.SaveLog("Disable SG Client Kill Switch", ex);
                NoticeManager.Instance.Enqueue("Не удалось снять аварийную блокировку интернета.");
            }
        }
        await ConfigHandler.SaveConfig(_config);
        QuickKillSwitchStatus = SgKillSwitchManager.Instance.IsEmergencyBlockActive
            ? "Блокирует интернет"
            : (QuickKillSwitch ? "Готов" : "Выключен");
    }

    private async Task OpenSplitTunnelAsync()
    {
        if (_updateView == null)
        {
            return;
        }
        var changed = await _updateView.Invoke(EViewAction.SgSplitTunnelWindow, null);
        if (!changed)
        {
            return;
        }
        RefreshQuickSummaries();
        if (EnableTun)
        {
            AppEvents.ReloadRequested.Publish();
        }
    }

    private async Task OpenReserveProfileAsync()
    {
        if (_updateView == null)
        {
            return;
        }
        var changed = await _updateView.Invoke(EViewAction.SgReserveProfileWindow, null);
        if (!changed)
        {
            return;
        }
        RefreshQuickSummaries();
        NoticeManager.Instance.Enqueue(_config.SgQuickSettingsItem.AutoFailoverEnabled
            ? $"Резервный профиль: {_config.SgQuickSettingsItem.ReserveProfileName}."
            : "Автоматический резерв выключен.");
    }

    private async Task OpenRoutingModeAsync()
    {
        if (_updateView == null)
        {
            return;
        }
        var changed = await _updateView.Invoke(EViewAction.SgRoutingWindow, null);
        if (!changed)
        {
            return;
        }
        RefreshQuickSummaries();
        if (EnableTun)
        {
            AppEvents.ReloadRequested.Publish();
        }
    }

    private async Task OpenDpiModeAsync()
    {
        if (_updateView == null)
        {
            return;
        }
        await _updateView.Invoke(EViewAction.SgDpiWindow, null);
        RefreshQuickSummaries();
    }

    public void RefreshSgQuickSummaries() => RefreshQuickSummaries();

    private void RefreshQuickSummaries()
    {
        _config.SgQuickSettingsItem.SplitTunnelApplications ??= [];
        _config.SgQuickSettingsItem.SplitTunnelAddresses ??= [];
        var appCount = _config.SgQuickSettingsItem.SplitTunnelApplications.Count;
        var addressCount = _config.SgQuickSettingsItem.SplitTunnelAddresses.Count;
        QuickSplitTunnelSummary = appCount == 0 && addressCount == 0
            ? "Не настроен"
            : $"{appCount} прил. · {addressCount} адрес.";

        QuickReserveProfileSummary = _config.SgQuickSettingsItem.AutoFailoverEnabled
            && _config.SgQuickSettingsItem.ReserveProfileName.IsNotEmpty()
                ? _config.SgQuickSettingsItem.ReserveProfileName
                : "Не настроен";

        var smartRouting = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        QuickRoutingSummary = smartRouting.Preset switch
        {
            SgSmartRoutingHelper.PresetRussiaDirect => "Россия напрямую",
            SgSmartRoutingHelper.PresetBlockedOnly => "Только блокировки",
            SgSmartRoutingHelper.PresetCustom when smartRouting.RussiaScope == SgSmartRoutingHelper.RussiaScopeTld
                => "Доменные зоны РФ",
            SgSmartRoutingHelper.PresetCustom when smartRouting.RussiaScope == SgSmartRoutingHelper.RussiaScopeSitesAndIp
                => "Сайты и IP РФ",
            SgSmartRoutingHelper.PresetCustom => "Пользовательская",
            _ => "Весь интернет",
        };

        if (AmneziaWgManager.Instance.SelectedProfileId.IsNotEmpty())
        {
            QuickDpiSummary = "Встроена в профиль";
        }
        else if (ProfileProtocolDisplay?.Contains("Hysteria2", StringComparison.OrdinalIgnoreCase) == true)
        {
            QuickDpiSummary = "Штатная QUIC";
        }
        else
        {
            QuickDpiSummary = (_config.SgQuickSettingsItem.DpiMode ?? "auto") switch
            {
                "off" => "Выключена",
                "tls" => "Дробление TLS",
                "tls_noise" => "TLS + шум",
                "custom" => "Пользовательская",
                _ => "Автоматически",
            };
        }

        RefreshModeAwareQuickStatus();
    }

    private void RefreshModeAwareQuickStatus()
    {
        var mode = ConnectionModeKey;
        if (mode == "tun")
        {
            QuickDnsStatusDisplay = QuickDnsThroughTun
                ? "Через VPN"
                : "Напрямую · риск";
            QuickLocalNetworkStatusDisplay = QuickAllowLocalNetwork
                ? "Разрешена"
                : "Через VPN";
            return;
        }

        QuickDnsStatusDisplay = QuickDnsThroughTun
            ? "Для TUN: через VPN"
            : "Для TUN: напрямую";
        QuickLocalNetworkStatusDisplay = QuickAllowLocalNetwork
            ? "Для TUN: разрешена"
            : "Для TUN: через VPN";
    }

    public void RefreshQuickDnsState()
    {
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        QuickDnsThroughTun = _config.SgQuickSettingsItem.DnsThroughTun;
        RefreshModeAwareQuickStatus();
    }

    private async Task ToggleQuickAutoRecoverAsync()
    {
        QuickAutoRecover = !QuickAutoRecover;
        _config.SgQuickSettingsItem.AutoRecoverTun = QuickAutoRecover;
        await ConfigHandler.SaveConfig(_config);
        NoticeManager.Instance.Enqueue(QuickAutoRecover
            ? "Автовосстановление TUN включено."
            : "Автовосстановление TUN выключено.");
    }

    private async Task ToggleQuickLocalNetworkAsync()
    {
        QuickAllowLocalNetwork = !QuickAllowLocalNetwork;
        _config.SgQuickSettingsItem.AllowLocalNetwork = QuickAllowLocalNetwork;
        ConfigHandler.ApplySgLocalNetworkPreference(_config);
        await ConfigHandler.SaveConfig(_config);
        RefreshModeAwareQuickStatus();
        if (EnableTun)
        {
            AppEvents.ReloadRequested.Publish();
        }
    }

    private async Task ToggleQuickDnsAsync()
    {
        QuickDnsThroughTun = !QuickDnsThroughTun;
        _config.SgQuickSettingsItem.DnsThroughTun = QuickDnsThroughTun;
        await ConfigHandler.SaveConfig(_config);
        RefreshModeAwareQuickStatus();
        if (EnableTun)
        {
            AppEvents.ReloadRequested.Publish();
        }
    }

    private async Task ToggleQuickAutoRunAsync()
    {
        var previous = QuickAutoRun;
        QuickAutoRun = !QuickAutoRun;
        _config.GuiItem.AutoRun = QuickAutoRun;
        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            QuickAutoRun = previous;
            _config.GuiItem.AutoRun = previous;
            NoticeManager.Instance.Enqueue("Не удалось сохранить настройку автозапуска.");
            return;
        }

        try
        {
            await AutoStartupHandler.UpdateTask(_config);
            NoticeManager.Instance.Enqueue(QuickAutoRun
                ? "Автозапуск SG Client включён."
                : "Автозапуск SG Client выключен.");
        }
        catch (Exception ex)
        {
            QuickAutoRun = previous;
            _config.GuiItem.AutoRun = previous;
            await ConfigHandler.SaveConfig(_config);
            Logging.SaveLog("Update SG Client autostart", ex);
            NoticeManager.Instance.Enqueue("Не удалось изменить автозапуск SG Client.");
        }
    }

    public async Task DisableTunAsync()
    {
        if (!EnableTun && TunUiState == ETunUiState.Off)
        {
            if (SgKillSwitchManager.Instance.IsEmergencyBlockActive)
            {
                await SgKillSwitchManager.Instance.DeactivateEmergencyBlockAsync();
                QuickKillSwitchStatus = QuickKillSwitch ? "Готов" : "Выключен";
            }
            try
            {
                await using var operation = await TunOperationCoordinator.EnterAsync("final TUN cleanup");
                await StopAllTunEnginesAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Final TUN cleanup", ex);
                ReportTunError(ex.Message.IsNullOrEmpty()
                    ? "Не удалось полностью очистить TUN перед выходом."
                    : ex.Message);
            }
            return;
        }
        await SetTunEnabledAsync(false);
    }

    private static string CleanProfileName(string? remarks, string? structuredCountryCode, string fallback)
    {
        if (remarks.IsNullOrEmpty())
        {
            return fallback;
        }

        var countryCode = SgCountryHelper.ResolveCode(structuredCountryCode, remarks);
        return SgCountryHelper.CleanRemarks(remarks, countryCode);
    }

    public void ReportTunStarting() => ApplyTunState(ETunUiState.Starting);

    public void ReportProfileSwitching(string profileName, string protocol, string core)
    {
        ProfileNameDisplay = CleanProfileName(profileName, string.Empty, "Подключение");
        ProfileProtocolDisplay = protocol.IsNullOrEmpty() ? "—" : protocol;
        RunningServerDisplay = ProfileNameDisplay;
        CoreDisplay = core.IsNullOrEmpty() ? "—" : core;
        ApplyTunState(ETunUiState.Switching);
        TunDetailText = $"Останавливается текущий туннель и запускается профиль «{ProfileNameDisplay}».";
        RefreshQuickSummaries();
        UpdateTrayText();
    }

    public void ReportProfileSelected(string profileName, string protocol)
    {
        ProfileNameDisplay = CleanProfileName(profileName, string.Empty, "Подключение");
        ProfileProtocolDisplay = protocol.IsNullOrEmpty() ? "—" : protocol;
        RunningServerDisplay = ProfileNameDisplay;
        if (!EnableTun)
        {
            var mode = GetConnectionModeKey();
            if (mode is "system-proxy" or "local-proxy"
                && CoreManager.Instance.IsCoreRunning)
            {
                ReportProxyRunning(mode);
            }
            else
            {
                ApplyTunState(ETunUiState.Off);
            }
        }
        RefreshQuickSummaries();
        RefreshModeButtons();
        UpdateTrayText();
    }

    public void ReportTunRunning()
    {
        EnableTun = true;
        ConnectionModeKey = "tun";
        _config.SgQuickSettingsItem.ConnectionMode = "tun";
        CoreDisplay = AppManager.Instance.RunningCoreType switch
        {
            ECoreType.sing_box => "sing-box",
            ECoreType.Xray => "Xray",
            _ => AppManager.Instance.RunningCoreType.ToString()
        };
        ApplyTunState(ETunUiState.On);
        RefreshQuickSummaries();
        _ = DeactivateKillSwitchAfterTunStartAsync();
    }

    public void ReportAwgRunning(AwgProfile profile, DateTime handshake)
    {
        EnableTun = true;
        ProfileNameDisplay = CleanProfileName(profile.Name, string.Empty, "AmneziaWG");
        ProfileProtocolDisplay = "AmneziaWG";
        RunningServerDisplay = ProfileNameDisplay;
        CoreDisplay = "AmneziaWG";
        ConnectionModeKey = "tun";
        _config.SgQuickSettingsItem.ConnectionMode = "tun";
        ApplyTunState(ETunUiState.On);
        TunDetailText = $"Получен handshake AmneziaWG: {handshake:dd.MM.yyyy HH:mm:ss}.";
        RefreshQuickSummaries();
        UpdateTrayText();
        _ = DeactivateKillSwitchAfterTunStartAsync();
    }

    public void ReportTunStopped()
    {
        EnableTun = false;
        CoreDisplay = "—";
        if (GetConnectionModeKey() is "system-proxy" or "local-proxy")
        {
            ReportProxyStarting(GetConnectionModeKey());
            return;
        }

        ReportConnectionOff();
    }

    public void ReportProxyStarting(string mode)
    {
        EnableTun = false;
        ConnectionModeKey = mode;
        TunUiState = ETunUiState.Starting;
        TunBusy = true;
        TunStatusText = mode == "system-proxy"
            ? "Системный прокси включается…"
            : "Локальный прокси включается…";
        TunButtonText = "ПРОКСИ ВКЛЮЧАЕТСЯ…";
        TunTrayActionText = TunStatusText;
        TunDetailText = "Запускается ядро и проверяется локальный HTTP/SOCKS-порт.";
        UpdateTrafficDisplays(_trafficStatistics.SetIdle());
        RefreshModeButtons();
        UpdateTrayText();
    }

    public void ReportProxyRunning(string mode)
    {
        EnableTun = false;
        ConnectionModeKey = mode;
        SetSystemProxySelectedSilently(
            mode == "system-proxy"
                ? ESysProxyType.ForcedChange
                : ESysProxyType.ForcedClear);
        TunUiState = ETunUiState.On;
        TunBusy = false;
        CoreDisplay = AppManager.Instance.RunningCoreType switch
        {
            ECoreType.sing_box => "sing-box",
            ECoreType.Xray => "Xray",
            _ => AppManager.Instance.RunningCoreType.ToString(),
        };
        TunStatusText = mode == "system-proxy"
            ? "Системный прокси работает"
            : "Локальный прокси работает";
        TunButtonText = "ОТКЛЮЧИТЬ ПРОКСИ";
        TunTrayActionText = "Отключить прокси";
        TunDetailText = mode == "system-proxy"
            ? "Браузеры и приложения Windows используют локальный смешанный HTTP/SOCKS-порт."
            : "Ядро работает на локальном HTTP/SOCKS-порту без изменения настроек Windows.";
        RefreshModeButtons();
        RefreshQuickSummaries();
        UpdateTrayText();
    }

    public void ReportConnectionOff()
    {
        EnableTun = false;
        ConnectionModeKey = "off";
        SetSystemProxySelectedSilently(ESysProxyType.ForcedClear);
        CoreDisplay = "—";
        ApplyTunState(ETunUiState.Off);
        RefreshQuickSummaries();
    }

    public void ReportTunError(string message)
    {
        EnableTun = false;
        CoreDisplay = "—";
        ApplyTunState(ETunUiState.Error, message);
        RefreshQuickSummaries();
    }

    private async Task DeactivateKillSwitchAfterTunStartAsync()
    {
        if (!SgKillSwitchManager.Instance.IsEmergencyBlockActive)
        {
            QuickKillSwitchStatus = QuickKillSwitch ? "Готов" : "Выключен";
            return;
        }
        try
        {
            await SgKillSwitchManager.Instance.DeactivateEmergencyBlockAsync();
            QuickKillSwitchStatus = QuickKillSwitch ? "Готов" : "Выключен";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Release Kill Switch after successful TUN start", ex);
            QuickKillSwitchStatus = "Ошибка снятия";
        }
    }

    private void ApplyTunState(ETunUiState state, string? detail = null)
    {
        var previousState = TunUiState;
        if (state == ETunUiState.Starting
            && previousState is ETunUiState.Off or ETunUiState.Error)
        {
            UpdateTrafficDisplays(_trafficStatistics.ResetSession());
        }
        else if (state != ETunUiState.On)
        {
            UpdateTrafficDisplays(_trafficStatistics.SetIdle());
        }

        TunUiState = state;
        TunBusy = state is ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching;

        (TunStatusText, TunButtonText, TunTrayActionText, TunDetailText) = state switch
        {
            ETunUiState.Starting => ("TUN включается…", "TUN ВКЛЮЧАЕТСЯ…", "TUN включается…", "Запускается ядро и создаётся системный интерфейс."),
            ETunUiState.On => ("TUN работает", "ОТКЛЮЧИТЬ TUN", "Отключить TUN", "Системный трафик направлен через выбранный профиль."),
            ETunUiState.Stopping => ("TUN отключается…", "TUN ОТКЛЮЧАЕТСЯ…", "TUN отключается…", "Останавливается ядро и очищаются системные маршруты."),
            ETunUiState.Switching => ("Переключение профиля…", "ПЕРЕКЛЮЧЕНИЕ ПРОФИЛЯ…", "Переключение профиля…", "TUN остаётся включённым, ядро запускается с новым профилем."),
            ETunUiState.Error => ("Не удалось подключиться", "TUN On", "Повторить подключение", detail ?? "Не удалось запустить выбранный профиль."),
            _ => ("TUN выключен", "ВКЛЮЧИТЬ TUN", "Включить TUN", "Выберите профиль и включите системный туннель.")
        };
        RefreshModeButtons();
        UpdateTrayText();
    }

    private void RefreshModeButtons()
    {
        var mode = ConnectionModeKey;
        var running = TunUiState == ETunUiState.On;
        IsTunModeActive = running && mode == "tun";
        IsSystemProxyModeActive = running && mode == "system-proxy";
        IsLocalProxyModeActive = running && mode == "local-proxy";

        if (TunBusy && mode == "tun")
        {
            TunModeButtonText = TunUiState == ETunUiState.Stopping
                ? "TUN Off…"
                : "TUN On…";
        }
        else
        {
            TunModeButtonText = IsTunModeActive ? "TUN Off" : "TUN On";
        }

        if (TunBusy && mode == "system-proxy")
        {
            SystemProxyModeButtonText = "System Proxy On…";
        }
        else
        {
            SystemProxyModeButtonText = IsSystemProxyModeActive
                ? "System Proxy Off"
                : "System Proxy On";
        }

        var awgSelected = AmneziaWgManager.Instance.GetSelectedProfile() != null;
        CanUseSystemProxyMode = !TunBusy
            && (IsSystemProxyModeActive || !awgSelected);
        SystemProxyModeToolTip = awgSelected && !IsSystemProxyModeActive
            ? "Недоступно для AmneziaWG: этот протокол работает только через TUN."
            : IsSystemProxyModeActive
                ? "Выключить системный прокси Windows."
                : "Включить системный прокси Windows через выбранный профиль.";

        if (TunBusy && mode == "local-proxy")
        {
            LocalProxyModeButtonText = "LOCAL ВКЛЮЧАЕТСЯ…";
        }
        else if (IsLocalProxyModeActive)
        {
            LocalProxyModeButtonText = "ОТКЛЮЧИТЬ LOCAL";
        }
        else if (running)
        {
            LocalProxyModeButtonText = "НА LOCAL PROXY";
        }
        else
        {
            LocalProxyModeButtonText = "LOCAL PROXY";
        }

        RefreshModeAwareQuickStatus();
    }

    private void UpdateTrayText()
    {
        var profile = ProfileNameDisplay.IsNullOrEmpty() ? "не выбран" : ProfileNameDisplay;
        RunningServerToolTipText = $"SG Client — {TunStatusText}{Environment.NewLine}Профиль: {profile}";
    }

    private static string GetProtocolDisplay(ProfileItem running)
    {
        if (running.ConfigType == EConfigType.Hysteria2)
        {
            return "Hysteria2";
        }
        if (running.ConfigType == EConfigType.VLESS)
        {
            if (running.StreamSecurity == Global.StreamSecurityReality)
            {
                return running.Network == ETransport.xhttp.ToString() ? "VLESS XHTTP · REALITY" : "VLESS · REALITY";
            }
            if (running.Network == ETransport.xhttp.ToString())
            {
                return "VLESS XHTTP · TLS";
            }
            return "VLESS";
        }
        return running.ConfigType.ToString();
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    #endregion System proxy and Routings

    #region UI

    private async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        try
        {
            var activeProfileId = AmneziaWgManager.Instance.ActiveProfileId;

            if (!AmneziaWgManager.IsAwgProfileId(activeProfileId))
            {
                var bytesPerUnit = AppManager.Instance.IsRunningCore(
                    ECoreType.sing_box)
                        ? 1000L
                        : 1024L;

                UpdateTrafficDisplays(
                    _trafficStatistics.AddDelta(
                        _config.IndexId,
                        ProfileNameDisplay,
                        update.ProxyUp,
                        update.ProxyDown,
                        bytesPerUnit));
            }

            if (_config.GuiItem.DisplayRealTimeSpeed)
            {
                if (update.IsTunInterfaceTraffic)
                {
                    SpeedProxyDisplay = string.Format(
                        ResUI.SpeedDisplayText,
                        update.TrafficInterfaceName.IsNotEmpty()
                            ? update.TrafficInterfaceName
                            : "TUN",
                        Utils.HumanFy(update.ProxyUp),
                        Utils.HumanFy(update.ProxyDown));
                    SpeedDirectDisplay = string.Empty;
                }
                else if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
                {
                    SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, EInboundProtocol.mixed, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                    SpeedDirectDisplay = string.Empty;
                }
                else
                {
                    SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, Global.ProxyTag, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                    SpeedDirectDisplay = string.Format(ResUI.SpeedDisplayText, Global.DirectTag, Utils.HumanFy(update.DirectUp), Utils.HumanFy(update.DirectDown));
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Update SG Client traffic statistics", ex);
        }

        await Task.CompletedTask;
    }

    public async Task ReconcileConnectionStateAsync()
    {
        if (!TunBusy
            || TunUiState is ETunUiState.Stopping
            || GetConnectionModeKey() != "tun"
            || !_config.TunModeItem.EnableTun)
        {
            return;
        }

        try
        {
            var activeAwgId = AmneziaWgManager.Instance.ActiveProfileId;
            var awgProfile = AmneziaWgManager.IsAwgProfileId(activeAwgId)
                ? AmneziaWgManager.Instance.GetProfile(activeAwgId)
                : AmneziaWgManager.Instance.GetSelectedProfile();

            if (awgProfile != null)
            {
                var status = await AmneziaWgManager.Instance
                    .QueryStatusAsync(awgProfile)
                    .WaitAsync(TimeSpan.FromSeconds(3));
                if (status.State == "connected" && status.LastHandshake != null)
                {
                    ReportAwgRunning(awgProfile, status.LastHandshake.Value);
                }
                return;
            }

            if (CoreManager.Instance.IsCoreRunning)
            {
                ReportTunRunning();
            }
        }
        catch (TimeoutException)
        {
            // Keep the current transition state; the next lightweight poll can
            // reconcile it without blocking the UI thread.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Reconcile busy TUN state", ex);
        }
    }

    public async Task RefreshAwgTrafficAsync()
    {
        var profileId = AmneziaWgManager.Instance.ActiveProfileId;

        if (TunUiState != ETunUiState.On
            || !AmneziaWgManager.IsAwgProfileId(profileId)
            || Interlocked.Exchange(ref _awgTrafficPolling, 1) != 0)
        {
            return;
        }

        try
        {
            var totals = await AmneziaWgManager.Instance
                .GetActiveTrafficTotalsAsync();

            if (totals == null)
            {
                return;
            }

            var awgProfile = AmneziaWgManager.Instance.GetProfile(profileId);
            UpdateTrafficDisplays(
                _trafficStatistics.UpdateCumulative(
                    profileId ?? "awg",
                    awgProfile?.Name ?? ProfileNameDisplay,
                    totals.Value.SentBytes,
                    totals.Value.ReceivedBytes));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read AmneziaWG traffic statistics", ex);
        }
        finally
        {
            Volatile.Write(ref _awgTrafficPolling, 0);
        }
    }

    public void ResetTrafficSession()
    {
        UpdateTrafficDisplays(_trafficStatistics.ResetSession());
    }

    public void ResetAllTrafficStatistics()
    {
        UpdateTrafficDisplays(_trafficStatistics.ResetAll());
    }

    private void UpdateTrafficDisplays(SgTrafficSnapshot snapshot)
    {
        TrafficProfileNameDisplay = snapshot.ProfileName.IsNotEmpty()
            ? snapshot.ProfileName
            : "Профиль не выбран";

        TrafficCurrentDownloadDisplay =
            $"↓ {FormatTraffic(snapshot.CurrentDownloadBytesPerSecond)}/с";

        TrafficCurrentUploadDisplay =
            $"↑ {FormatTraffic(snapshot.CurrentUploadBytesPerSecond)}/с";

        TrafficDownloadLevel = CalculateTrafficLevel(
            snapshot.CurrentDownloadBytesPerSecond);

        TrafficUploadLevel = CalculateTrafficLevel(
            snapshot.CurrentUploadBytesPerSecond);

        TrafficSessionDownloadDisplay =
            $"↓ {FormatTraffic(snapshot.SessionDownloadBytes)}";

        TrafficSessionUploadDisplay =
            $"↑ {FormatTraffic(snapshot.SessionUploadBytes)}";

        TrafficTodayDownloadDisplay =
            $"↓ {FormatTraffic(snapshot.TodayDownloadBytes)}";

        TrafficTodayUploadDisplay =
            $"↑ {FormatTraffic(snapshot.TodayUploadBytes)}";

        TrafficMonthDownloadDisplay =
            $"↓ {FormatTraffic(snapshot.MonthDownloadBytes)}";

        TrafficMonthUploadDisplay =
            $"↑ {FormatTraffic(snapshot.MonthUploadBytes)}";

        TrafficTotalDownloadDisplay =
            $"↓ {FormatTraffic(snapshot.TotalDownloadBytes)}";

        TrafficTotalUploadDisplay =
            $"↑ {FormatTraffic(snapshot.TotalUploadBytes)}";
    }

    private static double CalculateTrafficLevel(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return 4;
        }

        // Logarithmic scale: visible at low speed and calm at high speed.
        var kilobytes = bytesPerSecond / 1024d;
        var normalized = Math.Log10(1 + kilobytes) / Math.Log10(1 + 102400d);
        return Math.Clamp(6 + normalized * 42, 6, 48);
    }

    private static string FormatTraffic(long bytes)
    {
        var safeBytes = Math.Max(0, bytes);
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

        double value = safeBytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{value:0} {units[unitIndex]}";
        }

        return value >= 100
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    #endregion UI
}
