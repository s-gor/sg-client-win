namespace ServiceLib.ViewModels;

public class ProfilesViewModel : MyReactiveObject
{
    public event Action? SpeedTestCompleted;
    public event Action? CountryMetadataChanged;

    public static ProfilesViewModel? Instance { get; private set; }

    #region private prop

    private List<ProfileItem> _lstProfile;
    private string _serverFilter = string.Empty;
    private readonly Dictionary<string, bool> _dicHeaderSort = new();
    private SpeedtestService? _speedtestService;
    private string? _pendingSelectIndexId;
    private string? _pendingRevealProfileId;
    private readonly SemaphoreSlim _profileActivationGate = new(1, 1);
    private CancellationTokenSource? _countryLookupCts;
    private readonly object _latencyProgressLock = new();
    private readonly HashSet<string> _latencyCompletedIds = new(StringComparer.OrdinalIgnoreCase);

    #endregion private prop

    #region ObservableCollection

    public IObservableCollection<ProfileItemModel> ProfileItems { get; } = new ObservableCollectionExtended<ProfileItemModel>();

    public IObservableCollection<SubItem> SubItems { get; } = new ObservableCollectionExtended<SubItem>();

    [Reactive]
    public ProfileItemModel SelectedProfile { get; set; }

    [Reactive]
    public bool TunEnabled { get; set; }

    [Reactive]
    public bool TunBusy { get; set; }

    [Reactive]
    public ETunUiState TunUiState { get; set; }

    public IList<ProfileItemModel> SelectedProfiles { get; set; }

    [Reactive]
    public SubItem SelectedSub { get; set; }

    [Reactive]
    public SubItem SelectedMoveToGroup { get; set; }

    [Reactive]
    public string ServerFilter { get; set; }

    [Reactive]
    public bool LatencyTestRunning { get; set; }

    [Reactive]
    public bool LatencyTestPanelVisible { get; set; }

    [Reactive]
    public int LatencyTestCompleted { get; set; }

    [Reactive]
    public int LatencyTestTotal { get; set; }

    [Reactive]
    public double LatencyTestProgress { get; set; }

    [Reactive]
    public string LatencyTestStatus { get; set; } = "URL-тест не запущен";

    #endregion ObservableCollection

    #region Menu

    //servers delete
    public ReactiveCommand<Unit, Unit> EditServerCmd { get; }

    public ReactiveCommand<Unit, Unit> RemoveServerCmd { get; }
    public ReactiveCommand<Unit, Unit> RemoveDuplicateServerCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyServerCmd { get; }
    public ReactiveCommand<Unit, Unit> SetDefaultServerCmd { get; }
    public ReactiveCommand<Unit, Unit> ShareServerCmd { get; }
    public ReactiveCommand<Unit, Unit> GenGroupAllServerCmd { get; }
    public ReactiveCommand<Unit, Unit> GenGroupRegionServerCmd { get; }

    //servers move
    public ReactiveCommand<Unit, Unit> MoveTopCmd { get; }

    public ReactiveCommand<Unit, Unit> MoveUpCmd { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCmd { get; }
    public ReactiveCommand<Unit, Unit> MoveBottomCmd { get; }
    public ReactiveCommand<SubItem, Unit> MoveToGroupCmd { get; }

    //servers ping
    public ReactiveCommand<Unit, Unit> MixedTestServerCmd { get; }

    public ReactiveCommand<Unit, Unit> TcpingServerCmd { get; }
    public ReactiveCommand<Unit, Unit> RealPingServerCmd { get; }
    public ReactiveCommand<Unit, Unit> UdpTestServerCmd { get; }
    public ReactiveCommand<Unit, Unit> SpeedServerCmd { get; }
    public ReactiveCommand<Unit, Unit> SortServerResultCmd { get; }
    public ReactiveCommand<Unit, Unit> RemoveInvalidServerResultCmd { get; }
    public ReactiveCommand<Unit, Unit> FastRealPingCmd { get; }

    //servers export
    public ReactiveCommand<Unit, Unit> Export2ClientConfigCmd { get; }

    public ReactiveCommand<Unit, Unit> Export2ClientConfigClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> Export2ShareUrlCmd { get; }
    public ReactiveCommand<Unit, Unit> Export2ShareUrlBase64Cmd { get; }
    public ReactiveCommand<Unit, Unit> Export2InnerUriCmd { get; }

    public ReactiveCommand<Unit, Unit> AddSubCmd { get; }
    public ReactiveCommand<Unit, Unit> EditSubCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteSubCmd { get; }

    #endregion Menu

    #region Init

    public ProfilesViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        Instance = this;
        _config = AppManager.Instance.Config;
        _updateView = updateView;
        TunEnabled = _config.TunModeItem.EnableTun;
        TunBusy = StatusBarViewModel.Instance.TunBusy;
        TunUiState = StatusBarViewModel.Instance.TunUiState;

        StatusBarViewModel.Instance.WhenAnyValue(x => x.TunUiState)
            .Subscribe(value =>
            {
                TunUiState = value;
                TunBusy = value is ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching;
                TunEnabled = StatusBarViewModel.Instance.EnableTun;
            });

        #region WhenAnyValue && ReactiveCommand

        var canEditRemove = this.WhenAnyValue(
           x => x.SelectedProfile,
           selectedSource => selectedSource != null && !selectedSource.IndexId.IsNullOrEmpty());

        this.WhenAnyValue(
            x => x.SelectedSub,
            y => y != null && !y.Remarks.IsNullOrEmpty() && _config.SubIndexId != y.Id)
                .Subscribe(async c => await SubSelectedChangedAsync(c));
        this.WhenAnyValue(
             x => x.SelectedMoveToGroup,
             y => y != null && !y.Remarks.IsNullOrEmpty())
                 .Subscribe(async c => await MoveToGroup(c));

        this.WhenAnyValue(
          x => x.ServerFilter,
          y => y != null && _serverFilter != y)
              .Subscribe(async c => await ServerFilterChanged(c));

        //servers delete
        EditServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditServerAsync();
        }, canEditRemove);
        RemoveServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveServerAsync();
        }, canEditRemove);
        RemoveDuplicateServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveDuplicateServer();
        });
        CopyServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyServer();
        }, canEditRemove);
        SetDefaultServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetDefaultServer();
        }, canEditRemove);
        ShareServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ShareServerAsync();
        }, canEditRemove);
        GenGroupAllServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await GenGroupAllServer();
        }, canEditRemove);
        GenGroupRegionServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await GenGroupRegionServer();
        }, canEditRemove);

        //servers move
        MoveTopCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Top);
        }, canEditRemove);
        MoveUpCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Up);
        }, canEditRemove);
        MoveDownCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Down);
        }, canEditRemove);
        MoveBottomCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Bottom);
        }, canEditRemove);
        MoveToGroupCmd = ReactiveCommand.CreateFromTask<SubItem>(async sub =>
        {
            SelectedMoveToGroup = sub;
        });

        //servers ping
        FastRealPingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.FastRealping);
        });
        MixedTestServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Mixedtest);
        });
        TcpingServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Tcping);
        }, canEditRemove);
        RealPingServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Realping);
        }, canEditRemove);
        UdpTestServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.UdpTest);
        }, canEditRemove);
        SpeedServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Speedtest);
        }, canEditRemove);
        SortServerResultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SortServer(nameof(EServerColName.DelayVal));
        });
        RemoveInvalidServerResultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveInvalidServerResult();
        });
        //servers export
        Export2ClientConfigCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ClientConfigAsync(false);
        }, canEditRemove);
        Export2ClientConfigClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ClientConfigAsync(true);
        }, canEditRemove);
        Export2ShareUrlCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ShareUrlAsync(false);
        }, canEditRemove);
        Export2ShareUrlBase64Cmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ShareUrlAsync(true);
        }, canEditRemove);
        Export2InnerUriCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2InnerUrlAsync();
        }, canEditRemove);

        //Subscription
        AddSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(true);
        });
        EditSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(false);
        });
        DeleteSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DeleteSubAsync();
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.ProfilesRefreshRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await RefreshServersBiz());

        AppEvents.SubscriptionsRefreshRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await RefreshSubscriptions());

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await UpdateStatistics(result));

        AppEvents.SetDefaultServerRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async indexId => { await ActivateProfileAsync(indexId); });

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        SelectedProfile = new();
        SelectedSub = new();
        SelectedMoveToGroup = new();

        await RefreshSubscriptions();
        //await RefreshServers();
    }

    #endregion Init

    public void RequestRevealProfile(string indexId)
    {
        if (indexId.IsNotEmpty())
        {
            _pendingRevealProfileId = indexId;
            _pendingSelectIndexId = indexId;
        }
    }

    public string? ConsumePendingRevealProfileId()
    {
        var value = _pendingRevealProfileId;
        _pendingRevealProfileId = null;
        return value;
    }

    #region Actions

    private void Reload()
    {
        AppEvents.ReloadRequested.Publish();
    }

    public async Task SetSpeedTestResult(SpeedTestResult result)
    {
        if (result.IndexId.IsNullOrEmpty())
        {
            FinishLatencyTest(result.Delay);
            NoticeManager.Instance.SendMessageEx(result.Delay);
            NoticeManager.Instance.Enqueue(result.Delay);
            SpeedTestCompleted?.Invoke();
            return;
        }
        var item = ProfileItems.FirstOrDefault(it => it.IndexId == result.IndexId);
        if (item == null)
        {
            return;
        }

        if (result.Delay.IsNotEmpty())
        {
            item.Delay = result.Delay.ToInt();
            item.DelayVal = result.Delay ?? string.Empty;
        }
        if (result.Speed.IsNotEmpty())
        {
            item.SpeedVal = result.Speed ?? string.Empty;
        }
        if (result.IpInfo.IsNotEmpty())
        {
            item.IpInfo = result.IpInfo ?? string.Empty;
        }
        TrackLatencyProgress(result);
        SpeedTestCompleted?.Invoke();
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.EnableStatistics
            || (update.ProxyUp + update.ProxyDown) <= 0
            || DateTime.Now.Second % 3 != 0)
        {
            return;
        }

        try
        {
            var item = ProfileItems.FirstOrDefault(it => it.IndexId == update.IndexId);
            if (item != null)
            {
                item.TodayDown = Utils.HumanFy(update.TodayDown);
                item.TodayUp = Utils.HumanFy(update.TodayUp);
                item.TotalDown = Utils.HumanFy(update.TotalDown);
                item.TotalUp = Utils.HumanFy(update.TotalUp);
            }
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private async Task SubSelectedChangedAsync(bool c)
    {
        if (!c)
        {
            return;
        }
        _config.SubIndexId = SelectedSub?.Id;

        await RefreshServers();

        await _updateView?.Invoke(EViewAction.ProfilesFocus, null);
    }

    private async Task ServerFilterChanged(bool c)
    {
        if (!c)
        {
            return;
        }
        _serverFilter = ServerFilter;
        if (_serverFilter.IsNullOrEmpty())
        {
            await RefreshServers();
        }
    }

    public async Task RefreshServers()
    {
        AppEvents.ProfilesRefreshRequested.Publish();

        await Task.Delay(200);
    }

    private async Task RefreshServersBiz()
    {
        var lstModel = await GetProfileItemsEx(string.Empty, _serverFilter) ?? [];
        _lstProfile = JsonUtils.Deserialize<List<ProfileItem>>(
            JsonUtils.Serialize(lstModel.Where(item => !item.IsAmneziaWG))) ?? [];

        TunEnabled = _config.TunModeItem.EnableTun;

        // Keep the user's highlighted row separate from the active profile.
        // During Clear/AddRange WPF may temporarily select the first item; using
        // the stable IndexId prevents that transient selection from switching
        // a subscription row back to a local profile.
        var highlightedIndexId = _pendingSelectIndexId.IsNotEmpty()
            ? _pendingSelectIndexId
            : SelectedProfile?.IndexId;

        ProfileItems.Clear();
        ProfileItems.AddRange(lstModel);
        if (lstModel.Count > 0)
        {
            var selected = highlightedIndexId.IsNotEmpty()
                ? lstModel.FirstOrDefault(t => string.Equals(t.IndexId, highlightedIndexId, StringComparison.OrdinalIgnoreCase))
                : null;

            // A pending reveal/select request belongs to this refresh only.
            // Keeping a missing stale id could later override a real click.
            if (_pendingSelectIndexId.IsNotEmpty())
            {
                _pendingSelectIndexId = null;
            }

            selected ??= lstModel.FirstOrDefault(t => t.IsActive);
            selected ??= lstModel.FirstOrDefault(t => string.Equals(t.IndexId, _config.IndexId, StringComparison.OrdinalIgnoreCase));
            SelectedProfile = selected ?? lstModel.First();
        }

        await _updateView?.Invoke(EViewAction.DispatcherRefreshServersBiz, null);
        StartCountryEnrichment(lstModel);
    }

    private async Task RefreshSubscriptions()
    {
        SubItems.Clear();

        SubItems.Add(new SubItem { Remarks = ResUI.AllGroupServers });

        foreach (var item in await AppManager.Instance.SubItems())
        {
            SubItems.Add(item);
        }
        // The compact SG profile browser always starts with all subscriptions.
        // Filtering by a particular source is handled instantly in ProfilesView,
        // without reloading thousands of profiles from storage.
        SelectedSub = SubItems.FirstOrDefault();
    }

    private async Task<List<ProfileItemModel>?> GetProfileItemsEx(string subid, string filter)
    {
        var lstModel = await AppManager.Instance.ProfileModels(subid, filter);

        await ConfigHandler.SetDefaultServer(_config, lstModel);

        var awgSelectedId = AmneziaWgManager.Instance.SelectedProfileId;
        var lstServerStat = (_config.GuiItem.EnableStatistics ? StatisticsManager.Instance.ServerStat : null) ?? [];
        var lstProfileExs = await ProfileExManager.Instance.GetProfileExs();
        var result = (from t in lstModel
                      join t2 in lstServerStat on t.IndexId equals t2.IndexId into t2b
                      from t22 in t2b.DefaultIfEmpty()
                      join t3 in lstProfileExs on t.IndexId equals t3.IndexId into t3b
                      from t33 in t3b.DefaultIfEmpty()
                      select new ProfileItemModel
                      {
                          IndexId = t.IndexId,
                          ConfigType = t.ConfigType,
                          Remarks = t.Remarks,
                          CountryCode = t.CountryCode,
                          CountrySource = SgCountryHelper.NormalizeCode(t.CountryCode).IsNotEmpty()
                              ? "Профиль"
                              : (SgCountryHelper.ResolveCode(string.Empty, t.Remarks).IsNotEmpty() ? "Имя профиля" : string.Empty),
                          Address = t.Address,
                          Port = t.Port,
                          Network = t.Network,
                          StreamSecurity = t.StreamSecurity,
                          Subid = t.Subid,
                          IsSub = t.IsSub,
                          SubRemarks = t.SubRemarks,
                          IsActive = awgSelectedId.IsNullOrEmpty() && t.IndexId == _config.IndexId,
                          IsAmneziaWG = false,
                          ProtocolDisplay = GetProtocolDisplay(t.ConfigType, t.Network, t.StreamSecurity),
                          SourceDisplay = t.IsSub
                              ? (t.SubRemarks.IsNotEmpty() ? $"Подписка: {t.SubRemarks}" : "Подписка")
                              : "Локальный профиль",
                          Sort = t33?.Sort ?? 0,
                          Delay = t33?.Delay ?? 0,
                          Speed = t33?.Speed ?? 0,
                          DelayVal = t33?.Delay != 0 ? $"{t33?.Delay} мс" : string.Empty,
                          SpeedVal = t33?.Speed > 0 ? $"{t33?.Speed}" : t33?.Message ?? string.Empty,
                          IpInfo = t33?.IpInfo ?? string.Empty,
                          TodayDown = t22 == null ? "" : Utils.HumanFy(t22.TodayDown),
                          TodayUp = t22 == null ? "" : Utils.HumanFy(t22.TodayUp),
                          TotalDown = t22 == null ? "" : Utils.HumanFy(t22.TotalDown),
                          TotalUp = t22 == null ? "" : Utils.HumanFy(t22.TotalUp)
                      }).OrderBy(t => t.Sort).ToList();

        foreach (var item in result)
        {
            ApplyCountryMetadata(item);
        }

        var awgProfiles = AmneziaWgManager.Instance.GetProfiles();
        for (var awgIndex = 0; awgIndex < awgProfiles.Count; awgIndex++)
        {
            var profile = awgProfiles[awgIndex];
            if (filter.IsNotEmpty()
                && !profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !profile.Endpoint.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var awgItem = new ProfileItemModel
            {
                IndexId = profile.Id,
                ConfigType = EConfigType.WireGuard,
                Remarks = profile.Name,
                CountryCode = profile.CountryCode,
                CountrySource = SgCountryHelper.NormalizeCode(profile.CountryCode).IsNotEmpty() ? "GeoIP" : string.Empty,
                Address = profile.EndpointHost,
                Port = profile.EndpointPort,
                Network = "udp",
                StreamSecurity = string.Empty,
                IsActive = string.Equals(profile.Id, awgSelectedId, StringComparison.OrdinalIgnoreCase),
                IsAmneziaWG = true,
                ProtocolDisplay = "AmneziaWG",
                SourceDisplay = "Локальный профиль · AmneziaWG",
                DelayVal = profile.EndpointPort > 0 ? $"UDP {profile.EndpointPort}" : "UDP",
                Sort = 1_000_000 + awgIndex
            };
            ApplyCountryMetadata(awgItem);
            result.Add(awgItem);
        }

        return result;
    }

    private static void ApplyCountryMetadata(ProfileItemModel item)
    {
        var structured = SgCountryHelper.NormalizeCode(item.CountryCode);
        var fromName = SgCountryHelper.ResolveCode(string.Empty, item.Remarks);
        var resolved = structured.IsNotEmpty() ? structured : fromName;
        var source = item.CountrySource;
        if (source.IsNullOrEmpty())
        {
            source = structured.IsNotEmpty() ? "Профиль" : (fromName.IsNotEmpty() ? "Имя профиля" : string.Empty);
        }
        item.ApplyCountry(resolved, source);
    }

    private void StartCountryEnrichment(IReadOnlyList<ProfileItemModel> items)
    {
        _countryLookupCts?.Cancel();
        _countryLookupCts?.Dispose();
        _countryLookupCts = new CancellationTokenSource();
        _ = EnrichMissingCountriesSafeAsync(items.ToList(), _countryLookupCts.Token);
    }

    private async Task EnrichMissingCountriesSafeAsync(List<ProfileItemModel> items, CancellationToken cancellationToken)
    {
        try
        {
            await EnrichMissingCountriesAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Profile GeoIP enrichment failed", ex);
        }
    }

    private async Task EnrichMissingCountriesAsync(List<ProfileItemModel> items, CancellationToken cancellationToken)
    {
        var candidates = items
            .Where(item => item.ResolvedCountryCode.IsNullOrEmpty() && item.Address.IsNotEmpty())
            .GroupBy(item => (item.Address ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var resolvedByAddress = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(16, 16);
        var tasks = candidates.Select(async group =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var code = await SgGeoIpCountryService.Instance
                    .ResolveAddressAsync(group.Key, cancellationToken)
                    .ConfigureAwait(false);
                if (code.IsNotEmpty())
                {
                    resolvedByAddress[group.Key] = code;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"Profile GeoIP lookup failed: {group.Key}", ex);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || resolvedByAddress.Count == 0)
        {
            return;
        }

        var resolvedItems = items
            .Where(item => item.ResolvedCountryCode.IsNullOrEmpty()
                && resolvedByAddress.ContainsKey((item.Address ?? string.Empty).Trim()))
            .ToList();

        foreach (var item in resolvedItems.Where(item => item.IsAmneziaWG))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var code = resolvedByAddress[(item.Address ?? string.Empty).Trim()];
            await AmneziaWgManager.Instance.SetCountryCodeAsync(item.IndexId, code).ConfigureAwait(false);
        }

        var regularIds = resolvedItems
            .Where(item => !item.IsAmneziaWG && item.IndexId.IsNotEmpty())
            .Select(item => item.IndexId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var regularCodeById = resolvedItems
            .Where(item => !item.IsAmneziaWG && item.IndexId.IsNotEmpty())
            .GroupBy(item => item.IndexId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => resolvedByAddress[(group.First().Address ?? string.Empty).Trim()],
                StringComparer.OrdinalIgnoreCase);

        const int databaseChunkSize = 400;
        for (var offset = 0; offset < regularIds.Count; offset += databaseChunkSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var ids = regularIds.Skip(offset).Take(databaseChunkSize).ToList();
            var entities = await AppManager.Instance.GetProfileItemsByIndexIds(ids).ConfigureAwait(false);
            var updates = new List<ProfileItem>();
            foreach (var entity in entities)
            {
                if (SgCountryHelper.NormalizeCode(entity.CountryCode).IsNullOrEmpty()
                    && regularCodeById.TryGetValue(entity.IndexId, out var code))
                {
                    entity.CountryCode = code;
                    updates.Add(entity);
                }
            }
            if (updates.Count > 0)
            {
                await SQLiteHelper.Instance.UpdateAllAsync(updates).ConfigureAwait(false);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RxSchedulers.MainThreadScheduler.Schedule(items, (scheduler, scheduledItems) =>
        {
            foreach (var item in scheduledItems)
            {
                if (item.ResolvedCountryCode.IsNullOrEmpty()
                    && resolvedByAddress.TryGetValue((item.Address ?? string.Empty).Trim(), out var code))
                {
                    item.ApplyCountry(code, "GeoIP · geoip.dat");
                }
            }
            CountryMetadataChanged?.Invoke();
            return Disposable.Empty;
        });
    }

    private static string GetProtocolDisplay(EConfigType configType, string network, string streamSecurity)
    {
        if (configType == EConfigType.Hysteria2)
        {
            return "Hysteria2";
        }
        if (configType == EConfigType.VLESS)
        {
            if (streamSecurity == Global.StreamSecurityReality)
            {
                return network == ETransport.xhttp.ToString() ? "VLESS XHTTP · REALITY" : "VLESS · REALITY";
            }
            if (network == ETransport.xhttp.ToString())
            {
                return "VLESS XHTTP · TLS";
            }
            return "VLESS";
        }
        return configType.ToString();
    }

    #endregion Servers && Groups

    #region Add Servers

    private async Task<List<ProfileItem>?> GetProfileItems(bool latest)
    {
        var lstSelected = new List<ProfileItem>();
        if (SelectedProfiles == null || SelectedProfiles.Count <= 0)
        {
            return null;
        }

        var orderProfiles = SelectedProfiles
            .Where(item => !item.IsAmneziaWG)
            .OrderBy(t => t.Sort)
            .ToList();
        if (orderProfiles.Count == 0)
        {
            return null;
        }
        if (latest)
        {
            lstSelected.AddRange(await AppManager.Instance.GetProfileItemsOrderedByIndexIds(orderProfiles.Select(sp => sp?.IndexId)));
        }
        else
        {
            lstSelected = JsonUtils.Deserialize<List<ProfileItem>>(JsonUtils.Serialize(orderProfiles));
        }

        return lstSelected;
    }

    public async Task EditServerAsync()
    {
        if (string.IsNullOrEmpty(SelectedProfile?.IndexId))
        {
            return;
        }
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }
        var eConfigType = item.ConfigType;

        bool? ret = false;
        if (eConfigType == EConfigType.Custom)
        {
            ret = await _updateView?.Invoke(EViewAction.AddServer2Window, item);
        }
        else if (eConfigType.IsGroupType())
        {
            ret = await _updateView?.Invoke(EViewAction.AddGroupServerWindow, item);
        }
        else
        {
            ret = await _updateView?.Invoke(EViewAction.AddServerWindow, item);
        }
        if (ret == true)
        {
            await RefreshServers();
            if (item.IndexId == _config.IndexId)
            {
                Reload();
            }
        }
    }

    public async Task RemoveServerAsync()
    {
        if (SelectedProfile?.IsAmneziaWG == true)
        {
            if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
            {
                return;
            }
            var selectedId = SelectedProfile.IndexId;
            if (StatusBarViewModel.Instance.EnableTun
                && string.Equals(AmneziaWgManager.Instance.SelectedProfileId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                await StatusBarViewModel.Instance.DisableTunAsync();
                if (StatusBarViewModel.Instance.TunUiState == ETunUiState.Error)
                {
                    return;
                }
            }
            await AmneziaWgManager.Instance.DeleteProfileAsync(selectedId);
            NoticeManager.Instance.Enqueue("Профиль AmneziaWG удалён.");
            await RefreshServers();
            AppEvents.ProfilesRefreshRequested.Publish();
            return;
        }

        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null)
        {
            return;
        }
        if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
        {
            return;
        }
        var exists = lstSelected.Exists(t => t.IndexId == _config.IndexId);

        if (exists && StatusBarViewModel.Instance.EnableTun)
        {
            await StatusBarViewModel.Instance.DisableTunAsync();
        }

        await ConfigHandler.RemoveServers(_config, lstSelected);
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        if (lstSelected.Count == ProfileItems.Count)
        {
            ProfileItems.Clear();
        }
        await RefreshServers();
        if (exists)
        {
            AppEvents.ProfilesRefreshRequested.Publish();
        }
    }

    private async Task RemoveDuplicateServer()
    {
        if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
        {
            return;
        }

        var tuple = await ConfigHandler.DedupServerList(_config, _config.SubIndexId);
        if (tuple.Item1 > 0 || tuple.Item2 > 0)
        {
            await RefreshServers();
            Reload();
        }
        NoticeManager.Instance.Enqueue(string.Format(ResUI.RemoveDuplicateServerResult, tuple.Item1, tuple.Item2));
    }

    private async Task CopyServer()
    {
        var lstSelected = await GetProfileItems(false);
        if (lstSelected == null)
        {
            return;
        }
        if (await ConfigHandler.CopyServer(_config, lstSelected) == 0)
        {
            await RefreshServers();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    public async Task SetDefaultServer()
    {
        await ActivateProfileAsync(SelectedProfile?.IndexId);
    }

    public async Task<bool> ActivateProfileAsync(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return false;
        }

        if (!await _profileActivationGate.WaitAsync(0))
        {
            Logging.SaveLog($"Profile activation ignored while another switch is active: {indexId}");
            return false;
        }

        try
        {
            return await ActivateProfileCoreAsync(indexId);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Activate profile {indexId}", ex);
            NoticeManager.Instance.Enqueue(ex.Message.IsNullOrEmpty()
                ? "Не удалось переключить профиль."
                : ex.Message);
            AppEvents.ProfilesRefreshRequested.Publish();
            return false;
        }
        finally
        {
            _profileActivationGate.Release();
        }
    }

    private async Task<bool> ActivateProfileCoreAsync(string indexId)
    {
        Logging.SaveLog($"Profile switch requested: {indexId}; TUN={_config.TunModeItem.EnableTun}");

        if (AmneziaWgManager.IsAwgProfileId(indexId))
        {
            var awgProfile = AmneziaWgManager.Instance.GetProfile(indexId);
            if (awgProfile == null)
            {
                NoticeManager.Instance.Enqueue("Профиль AmneziaWG не найден.");
                return false;
            }

            var alreadySelected = string.Equals(indexId, AmneziaWgManager.Instance.SelectedProfileId, StringComparison.OrdinalIgnoreCase);
            if (alreadySelected && (!_config.TunModeItem.EnableTun || AmneziaWgManager.Instance.ActiveProfileId == indexId))
            {
                return true;
            }

            _pendingSelectIndexId = indexId;
            await AmneziaWgManager.Instance.SelectProfileAsync(indexId);
            Logging.SaveLog($"AmneziaWG profile selected: {awgProfile.Name} ({awgProfile.Id})");

            if (_config.TunModeItem.EnableTun)
            {
                StatusBarViewModel.Instance.ReportProfileSwitching(awgProfile.Name, "AmneziaWG", "AmneziaWG");
            }
            else
            {
                StatusBarViewModel.Instance.ReportProfileSelected(awgProfile.Name, "AmneziaWG");
            }

            AppEvents.ProfilesRefreshRequested.Publish();
            await Task.Delay(60);

            if (_config.TunModeItem.EnableTun)
            {
                var mainWindowViewModel = MainWindowViewModel.Instance;
                if (mainWindowViewModel != null)
                {
                    Logging.SaveLog($"Starting direct TUN switch to AmneziaWG: {awgProfile.Name}");
                    await mainWindowViewModel.Reload();
                    Logging.SaveLog($"Direct TUN switch to AmneziaWG completed: {awgProfile.Name}; active={AmneziaWgManager.Instance.ActiveProfileId}");
                    return StatusBarViewModel.Instance.TunUiState == ETunUiState.On;
                }

                AppEvents.ReloadRequested.Publish();
            }
            return true;
        }

        var awgWasSelected = AmneziaWgManager.Instance.SelectedProfileId.IsNotEmpty();
        if (indexId == _config.IndexId && !awgWasSelected)
        {
            return true;
        }
        var item = await AppManager.Instance.GetProfileItem(indexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return false;
        }

        _pendingSelectIndexId = indexId;
        await AmneziaWgManager.Instance.ClearSelectionAsync();
        if (await ConfigHandler.SetDefaultServerIndex(_config, indexId) != 0)
        {
            return false;
        }

        var protocol = GetProtocolDisplay(item.ConfigType, item.Network, item.StreamSecurity);
        var connectionMode = _config.SgQuickSettingsItem?.ConnectionMode ?? "off";
        var proxyModeActive = !_config.TunModeItem.EnableTun
            && connectionMode is "system-proxy" or "local-proxy";

        if (_config.TunModeItem.EnableTun)
        {
            var effectiveCore = AppManager.Instance.GetCoreType(item, item.ConfigType);
            var core = effectiveCore switch
            {
                ECoreType.sing_box => "sing-box",
                ECoreType.Xray => "Xray",
                _ => effectiveCore.ToString(),
            };
            StatusBarViewModel.Instance.ReportProfileSwitching(item.Remarks, protocol, core);
        }
        else
        {
            StatusBarViewModel.Instance.ReportProfileSelected(item.Remarks, protocol);
            if (proxyModeActive)
            {
                StatusBarViewModel.Instance.ReportProxyStarting(connectionMode);
            }
        }

        AppEvents.ProfilesRefreshRequested.Publish();
        await Task.Delay(60);

        // A profile click must switch the running core in every active mode.
        // Previously only TUN reloaded; System Proxy and Local Proxy kept using
        // the old local profile, which made subscription rows appear to jump back.
        if (_config.TunModeItem.EnableTun || proxyModeActive)
        {
            var mainWindowViewModel = MainWindowViewModel.Instance;
            if (mainWindowViewModel != null)
            {
                await mainWindowViewModel.Reload();
                return _config.TunModeItem.EnableTun
                    ? StatusBarViewModel.Instance.TunUiState == ETunUiState.On
                    : CoreManager.Instance.IsCoreRunning;
            }
            Reload();
        }
        return true;
    }

    public async Task ShareServerAsync()
    {
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }
        var url = FmtHandler.GetShareUri(item);
        if (url.IsNullOrEmpty())
        {
            return;
        }

        await _updateView?.Invoke(EViewAction.ShareServer, url);
    }

    private async Task GenGroupAllServer()
    {
        var ret = await ConfigHandler.AddGroupAllServer(_config, SelectedSub);
        if (ret.Success != true)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return;
        }
        _pendingSelectIndexId = ret.Data?.ToString();
        await RefreshServers();
    }

    private async Task GenGroupRegionServer()
    {
        var ret = await ConfigHandler.AddGroupRegionServer(_config, SelectedSub);
        if (ret.Success != true)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return;
        }
        var indexIdList = ret.Data as List<string>;
        _pendingSelectIndexId = indexIdList?.FirstOrDefault();
        await RefreshServers();
    }

    public async Task SortServer(string colName)
    {
        if (colName.IsNullOrEmpty())
        {
            return;
        }

        _dicHeaderSort.TryAdd(colName, true);
        _dicHeaderSort.TryGetValue(colName, out var asc);
        if (await ConfigHandler.SortServers(_config, _config.SubIndexId, colName, asc) != 0)
        {
            return;
        }
        _dicHeaderSort[colName] = !asc;
        await RefreshServers();
    }

    public async Task RemoveInvalidServerResult()
    {
        var count = await ConfigHandler.RemoveInvalidServerResult(_config, _config.SubIndexId);
        await RefreshServers();
        NoticeManager.Instance.Enqueue(string.Format(ResUI.RemoveInvalidServerResultTip, count));
    }

    //move server
    private async Task MoveToGroup(bool c)
    {
        if (!c)
        {
            return;
        }

        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null)
        {
            return;
        }

        await ConfigHandler.MoveToGroup(_config, lstSelected, SelectedMoveToGroup.Id);
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);

        await RefreshServers();
        SelectedMoveToGroup = null;
        SelectedMoveToGroup = new();
    }

    public async Task MoveServer(EMove eMove)
    {
        var item = _lstProfile.FirstOrDefault(t => t.IndexId == SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }

        var index = _lstProfile.IndexOf(item);
        if (index < 0)
        {
            return;
        }
        if (await ConfigHandler.MoveServer(_config, _lstProfile, index, eMove) == 0)
        {
            await RefreshServers();
        }
    }

    public async Task MoveServerTo(int startIndex, ProfileItemModel targetItem)
    {
        var targetIndex = ProfileItems.IndexOf(targetItem);
        if (startIndex >= 0 && targetIndex >= 0 && startIndex != targetIndex)
        {
            if (await ConfigHandler.MoveServer(_config, _lstProfile, startIndex, EMove.Position, targetIndex) == 0)
            {
                await RefreshServers();
            }
        }
    }

    public async Task TestLatencyAsync(IEnumerable<ProfileItemModel> profiles)
    {
        var candidates = profiles?
            .Where(item => item != null && !item.IsAmneziaWG && item.IndexId.IsNotEmpty())
            .GroupBy(item => item.IndexId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList() ?? [];

        if (candidates.Count == 0)
        {
            NoticeManager.Instance.Enqueue("В текущем списке нет профилей Xray или sing-box для проверки задержки.");
            return;
        }

        if (LatencyTestRunning)
        {
            NoticeManager.Instance.Enqueue("URL-тест уже выполняется.");
            return;
        }

        BeginLatencyTest(candidates);
        var previousSelection = SelectedProfiles;
        try
        {
            SelectedProfiles = candidates;
            await ServerSpeedtest(ESpeedActionType.Realping);
        }
        finally
        {
            SelectedProfiles = previousSelection;
        }
    }

    public async Task ServerSpeedtest(ESpeedActionType actionType)
    {
        List<ProfileItem>? lstSelected;
        if (actionType is ESpeedActionType.Mixedtest or ESpeedActionType.FastRealping)
        {
            if (actionType == ESpeedActionType.FastRealping)
            {
                actionType = ESpeedActionType.Realping;
            }

            lstSelected = JsonUtils.Deserialize<List<ProfileItem>>(JsonUtils.Serialize(ProfileItems?.OrderBy(t => t.Sort)));
        }
        else
        {
            lstSelected = await GetProfileItems(false);
        }

        if (lstSelected is null || lstSelected.Count <= 0)
        {
            return;
        }

        _speedtestService ??= new SpeedtestService(_config, async (SpeedTestResult result) =>
        {
            RxSchedulers.MainThreadScheduler.Schedule(result, (scheduler, result) =>
            {
                _ = SetSpeedTestResult(result);
                return Disposable.Empty;
            });
            await Task.CompletedTask;
        });
        _speedtestService?.RunLoop(actionType, lstSelected);
    }

    public void ServerSpeedtestStop()
    {
        _speedtestService?.ExitLoop();
        if (LatencyTestRunning)
        {
            // Cancellation must release the UI immediately. The remaining
            // per-profile tasks observe the stop token in the background, but
            // they must never keep the profile browser or connection controls
            // trapped in a global busy-looking state.
            FinishLatencyTest(ResUI.SpeedtestingStop);
            SpeedTestCompleted?.Invoke();
        }
    }

    private void BeginLatencyTest(IReadOnlyCollection<ProfileItemModel> candidates)
    {
        lock (_latencyProgressLock)
        {
            _latencyCompletedIds.Clear();
        }
        LatencyTestPanelVisible = true;
        LatencyTestTotal = candidates.Count;
        LatencyTestCompleted = 0;
        LatencyTestProgress = 0;
        LatencyTestRunning = true;
        LatencyTestStatus = $"URL-тест: 0 из {LatencyTestTotal}";
    }

    private void TrackLatencyProgress(SpeedTestResult result)
    {
        if (!LatencyTestRunning || result.IndexId.IsNullOrEmpty() || result.Delay.IsNullOrEmpty())
        {
            return;
        }

        if (string.Equals(result.Delay, ResUI.Speedtesting, StringComparison.OrdinalIgnoreCase)
            || result.Delay.Contains("ожид", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_latencyProgressLock)
        {
            if (!_latencyCompletedIds.Add(result.IndexId))
            {
                return;
            }
            LatencyTestCompleted = Math.Min(_latencyCompletedIds.Count, LatencyTestTotal);
        }

        LatencyTestProgress = LatencyTestTotal <= 0
            ? 0
            : Math.Min(100d, LatencyTestCompleted * 100d / LatencyTestTotal);
        LatencyTestStatus = $"URL-тест: {LatencyTestCompleted} из {LatencyTestTotal}";
    }

    private void FinishLatencyTest(string? message)
    {
        if (!LatencyTestRunning)
        {
            return;
        }

        var stopped = message?.Contains("останов", StringComparison.OrdinalIgnoreCase) == true
            || message?.Contains("stop", StringComparison.OrdinalIgnoreCase) == true;
        if (!stopped)
        {
            LatencyTestCompleted = LatencyTestTotal;
            LatencyTestProgress = LatencyTestTotal > 0 ? 100 : 0;
        }
        LatencyTestStatus = stopped
            ? $"URL-тест остановлен: {LatencyTestCompleted} из {LatencyTestTotal}"
            : $"URL-тест завершён: {LatencyTestCompleted} из {LatencyTestTotal}";
        LatencyTestRunning = false;
    }

    private async Task Export2ClientConfigAsync(bool blClipboard)
    {
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }

        var (context, validatorResult) = await CoreConfigContextBuilder.Build(_config, item);
        if (NoticeManager.Instance.NotifyValidatorResult(validatorResult) && !validatorResult.Success)
        {
            return;
        }

        if (blClipboard)
        {
            var result = await CoreConfigHandler.GenerateClientConfig(context, null);
            if (result.Success != true)
            {
                NoticeManager.Instance.Enqueue(result.Msg);
            }
            else
            {
                await _updateView?.Invoke(EViewAction.SetClipboardData, result.Data);
                NoticeManager.Instance.SendMessage(ResUI.OperationSuccess);
            }
        }
        else
        {
            await _updateView?.Invoke(EViewAction.SaveFileDialog, item);
        }
    }

    public async Task Export2ClientConfigResult(string fileName, ProfileItem item)
    {
        if (fileName.IsNullOrEmpty())
        {
            return;
        }
        var (context, validatorResult) = await CoreConfigContextBuilder.Build(_config, item);
        if (NoticeManager.Instance.NotifyValidatorResult(validatorResult) && !validatorResult.Success)
        {
            return;
        }
        var result = await CoreConfigHandler.GenerateClientConfig(context, fileName);
        if (result.Success != true)
        {
            NoticeManager.Instance.Enqueue(result.Msg);
        }
        else
        {
            NoticeManager.Instance.SendMessageAndEnqueue(string.Format(ResUI.SaveClientConfigurationIn, fileName));
        }
    }

    public async Task Export2ShareUrlAsync(bool blEncode)
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null)
        {
            return;
        }

        StringBuilder sb = new();
        foreach (var it in lstSelected)
        {
            var url = FmtHandler.GetShareUri(it);
            if (url.IsNullOrEmpty())
            {
                continue;
            }
            sb.Append(url);
            sb.AppendLine();
        }
        if (sb.Length > 0)
        {
            if (blEncode)
            {
                await _updateView?.Invoke(EViewAction.SetClipboardData, Utils.Base64Encode(sb.ToString()));
            }
            else
            {
                await _updateView?.Invoke(EViewAction.SetClipboardData, sb.ToString());
            }
            NoticeManager.Instance.SendMessage(ResUI.BatchExportURLSuccessfully);
        }
    }

    public async Task Export2InnerUrlAsync()
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null)
        {
            return;
        }

        var result = string.Empty;

        await Task.Run(() =>
        {
            result = InnerFmt.ToUri(lstSelected);
        });

        if (!result.IsNullOrEmpty())
        {
            await _updateView?.Invoke(EViewAction.SetClipboardData, result);
            NoticeManager.Instance.SendMessage(ResUI.BatchExportURLSuccessfully);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    #endregion Add Servers

    #region Subscription

    private async Task EditSubAsync(bool blNew)
    {
        SubItem item;
        if (blNew)
        {
            item = new();
        }
        else
        {
            item = await AppManager.Instance.GetSubItem(_config.SubIndexId);
            if (item is null)
            {
                return;
            }
        }
        if (await _updateView?.Invoke(EViewAction.SubEditWindow, item) == true)
        {
            await RefreshSubscriptions();
            await SubSelectedChangedAsync(true);
        }
    }

    private async Task DeleteSubAsync()
    {
        var item = await AppManager.Instance.GetSubItem(_config.SubIndexId);
        if (item is null)
        {
            return;
        }

        if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
        {
            return;
        }
        await ConfigHandler.DeleteSubItem(_config, item.Id);

        await RefreshSubscriptions();
        await SubSelectedChangedAsync(true);
    }

    #endregion Subscription
}
