using System.Reactive.Concurrency;

namespace ServiceLib.ViewModels;

public class MainWindowViewModel : MyReactiveObject
{
    public string? LastProxyStartError { get; private set; }

    public static MainWindowViewModel? Instance { get; private set; }

    #region Menu

    //servers
    public ReactiveCommand<Unit, Unit> AddVmessServerCmd { get; }

    public ReactiveCommand<Unit, Unit> AddVlessServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddShadowsocksServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddSocksServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddHttpServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddTrojanServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddHysteria2ServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddTuicServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddWireguardServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddAnytlsServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddNaiveServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddCustomServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddPolicyGroupServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddProxyChainServerCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaScanCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaImageCmd { get; }

    //Subscription
    public ReactiveCommand<Unit, Unit> SubSettingCmd { get; }

    public ReactiveCommand<Unit, Unit> SubUpdateCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<Unit, Unit> SubGroupUpdateCmd { get; }
    public ReactiveCommand<Unit, Unit> SubGroupUpdateViaProxyCmd { get; }

    //Setting
    public ReactiveCommand<Unit, Unit> OptionSettingCmd { get; }

    public ReactiveCommand<Unit, Unit> RoutingSettingCmd { get; }
    public ReactiveCommand<Unit, Unit> DNSSettingCmd { get; }
    public ReactiveCommand<Unit, Unit> FullConfigTemplateCmd { get; }
    public ReactiveCommand<Unit, Unit> GlobalHotkeySettingCmd { get; }
    public ReactiveCommand<Unit, Unit> RebootAsAdminCmd { get; }
    public ReactiveCommand<Unit, Unit> ClearServerStatisticsCmd { get; }
    public ReactiveCommand<Unit, Unit> OpenTheFileLocationCmd { get; }

    //Presets
    public ReactiveCommand<Unit, Unit> RegionalPresetDefaultCmd { get; }

    public ReactiveCommand<Unit, Unit> RegionalPresetRussiaCmd { get; }

    public ReactiveCommand<Unit, Unit> RegionalPresetIranCmd { get; }

    public ReactiveCommand<Unit, Unit> ReloadCmd { get; }

    [Reactive]
    public bool BlReloadEnabled { get; set; }

    [Reactive]
    public bool ShowClashUI { get; set; }

    [Reactive]
    public int TabMainSelectedIndex { get; set; }

    [Reactive] public bool BlIsWindows { get; set; }

    [Reactive] public bool BlNewUpdate { get; set; }

    #endregion Menu

    #region Init

    public MainWindowViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        Instance = this;
        _config = AppManager.Instance.Config;
        _updateView = updateView;
        BlIsWindows = Utils.IsWindows();

        #region WhenAnyValue && ReactiveCommand

        //servers
        AddVmessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VMess);
        });
        AddVlessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VLESS);
        });
        AddShadowsocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Shadowsocks);
        });
        AddSocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.SOCKS);
        });
        AddHttpServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.HTTP);
        });
        AddTrojanServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Trojan);
        });
        AddHysteria2ServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Hysteria2);
        });
        AddTuicServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.TUIC);
        });
        AddWireguardServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.WireGuard);
        });
        AddAnytlsServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Anytls);
        });
        AddNaiveServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Naive);
        });
        AddCustomServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Custom);
        });
        AddPolicyGroupServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.PolicyGroup);
        });
        AddProxyChainServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.ProxyChain);
        });
        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaClipboardAsync(null);
        });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScanAsync();
        });
        AddServerViaImageCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaImageAsync();
        });

        //Subscription
        SubSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SubSettingAsync();
        });

        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", true);
        });
        SubGroupUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, false);
        });
        SubGroupUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, true);
        });

        //Setting
        OptionSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OptionSettingAsync();
        });
        RoutingSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingSettingAsync();
        });
        DNSSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DNSSettingAsync();
        });
        FullConfigTemplateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await FullConfigTemplateAsync();
        });
        GlobalHotkeySettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _updateView?.Invoke(EViewAction.GlobalHotkeySettingWindow, null) == true)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            }
        });
        RebootAsAdminCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AppManager.Instance.RebootAsAdmin();
        });
        ClearServerStatisticsCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ClearServerStatistics();
        });
        OpenTheFileLocationCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OpenTheFileLocation();
        });

        ReloadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Reload();
        });

        RegionalPresetDefaultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Default);
        });

        RegionalPresetRussiaCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Russia);
        });

        RegionalPresetIranCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Iran);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.ReloadRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await Reload());

        AppEvents.AddServerViaScanRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await AddServerViaScanAsync());

        AppEvents.AddServerViaClipboardRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await AddServerViaClipboardAsync(null));

        AppEvents.SubscriptionsUpdateRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async blProxy => await UpdateSubscriptionProcess("", blProxy));

        AppEvents.HasUpdateNotified
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async bl => BlNewUpdate = bl);

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        AppManager.Instance.ShowInTaskbar = true;

        //await ConfigHandler.InitBuiltinRouting(_config);
        await ConfigHandler.InitBuiltinDNS(_config);
        await ConfigHandler.InitBuiltinFullConfigTemplate(_config);
        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateHandler);
        TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

        // The SG Client traffic card is always active, independently of the
        // legacy detailed-statistics switch in Settings.
        await StatisticsManager.Instance.Init(_config, UpdateStatisticsHandler);
        await RefreshServers();

        await Reload();
    }

    #endregion Init

    #region Actions

    private async Task UpdateHandler(bool notify, string msg)
    {
        NoticeManager.Instance.SendMessage(msg);
        if (notify)
        {
            NoticeManager.Instance.Enqueue(msg);
        }
        await Task.CompletedTask;
    }

    private async Task UpdateTaskHandler(bool success, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        if (success)
        {
            var indexIdOld = _config.IndexId;
            await RefreshServers();

            // If indexId changed or subIndexId is empty, directly reload.
            if (indexIdOld != _config.IndexId || _config.SubIndexId.IsNullOrEmpty())
            {
                await Reload();
            }
            else
            {
                // The activity config belongs to the current group.
                var profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
                if (profile != null && profile.Subid == _config.SubIndexId)
                {
                    await Reload();
                }
            }

            if (_config.UiItem.EnableAutoAdjustMainLvColWidth)
            {
                AppEvents.AdjustMainLvColWidthRequested.Publish();
            }
        }
    }

    private async Task UpdateStatisticsHandler(ServerSpeedItem update)
    {
        // Keep SG Client traffic totals accurate while the main window
        // is hidden in the notification area.
        AppEvents.DispatcherStatisticsRequested.Publish(update);
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private async Task RefreshServers()
    {
        AppEvents.ProfilesRefreshRequested.Publish();

        await Task.Delay(200);
    }

    private void RefreshSubscriptions()
    {
        AppEvents.SubscriptionsRefreshRequested.Publish();
    }

    #endregion Servers && Groups

    #region Add Servers

    public async Task AddServerAsync(EConfigType eConfigType)
    {
        ProfileItem item = new()
        {
            Subid = _config.SubIndexId,
            ConfigType = eConfigType,
            IsSub = false,
        };

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
                await Reload();
            }
        }
    }

    public async Task AddServerViaClipboardAsync(string? clipboardData)
    {
        if (clipboardData == null)
        {
            await _updateView?.Invoke(EViewAction.AddServerViaClipboard, null);
            return;
        }

        if (AmneziaWgManager.LooksLikeWireGuardConfig(clipboardData))
        {
            var parsed = AmneziaWgManager.TryValidateAmneziaConfig(
                clipboardData,
                out var hasAmneziaParameters,
                out var validationError);
            if (parsed && hasAmneziaParameters)
            {
                try
                {
                    var previousAwgProfileId = AmneziaWgManager.Instance.SelectedProfileId;
                    var profile = await AmneziaWgManager.Instance.ImportProfileAsync(
                        "AmneziaWG.conf",
                        clipboardData,
                        AmneziaWgManager.GetSuggestedProfileName("AmneziaWG.conf", clipboardData));
                    if (_config.TunModeItem.EnableTun)
                    {
                        if (previousAwgProfileId.IsNotEmpty())
                        {
                            await AmneziaWgManager.Instance.SelectProfileAsync(previousAwgProfileId);
                        }
                        else
                        {
                            await AmneziaWgManager.Instance.ClearSelectionAsync();
                        }
                    }
                    await RefreshServers();
                    AppEvents.ProfilesRefreshRequested.Publish();
                    NoticeManager.Instance.Enqueue($"Добавлен профиль AmneziaWG: {profile.Name}");
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("Import AmneziaWG from clipboard", ex);
                    NoticeManager.Instance.Enqueue(ex.Message);
                }
                return;
            }

            if (AmneziaWgManager.HasAmneziaParameterMarkers(clipboardData))
            {
                var message = parsed
                    ? "Конфигурация не содержит распознанных параметров AmneziaWG."
                    : validationError;
                Logging.SaveLog($"AmneziaWG import validation failed: {message}");
                NoticeManager.Instance.Enqueue(message);
                return;
            }
        }

        // SG_IMPORT_SUBSCRIPTION_URL_086: a single HTTP(S) URL pasted into Import
        // is saved as a subscription and updated immediately. The dedicated
        // “Подписки” section remains available for editing and manual refresh.
        if (TryResolveSingleSubscriptionUrl(clipboardData, out var subscriptionUrl, out var subscriptionUri))
        {
            await ImportSubscriptionUrlAsync(subscriptionUrl, subscriptionUri);
            return;
        }

        var directProfile = TryResolveSingleDirectProfile(clipboardData, out var directImportError);
        if (directProfile != null)
        {
            directProfile.Subid = string.Empty;
            directProfile.IsSub = false;
            directProfile.CountryCode = SgCountryHelper.ResolveCode(directProfile.CountryCode, directProfile.Remarks);

            if (!directProfile.IsValid())
            {
                var validationMessage = DescribeDirectProfileValidationError(directProfile);
                Logging.SaveLog($"Direct profile import validation failed: {validationMessage}");
                NoticeManager.Instance.Enqueue(validationMessage);
                return;
            }

            var existing = await ConfigHandler.FindExactLocalProfile(directProfile);
            if (existing != null)
            {
                ProfilesViewModel.Instance?.RequestRevealProfile(existing.IndexId);
                await RefreshServers();
                NoticeManager.Instance.Enqueue($"Профиль уже существует: {existing.Remarks}");
                return;
            }

            // A single share link has already been parsed by the protocol-specific
            // importer. Save that exact ProfileItem directly. Sending the original
            // long XHTTP/VLESS URI through the generic batch importer a second time
            // could discard modern opaque fields or fail without a useful reason.
            var addResult = await ConfigHandler.AddServer(_config, directProfile);
            if (addResult == 0)
            {
                RefreshSubscriptions();
                await RefreshServers();
                AppEvents.ProfilesRefreshRequested.Publish();
                ProfilesViewModel.Instance?.RequestRevealProfile(directProfile.IndexId);
                NoticeManager.Instance.Enqueue(
                    $"Добавлен профиль: {directProfile.Remarks.NullIfEmpty() ?? directProfile.Address}");
                return;
            }

            var saveMessage = DescribeDirectProfileValidationError(directProfile);
            Logging.SaveLog($"Direct profile save failed: {saveMessage}");
            NoticeManager.Instance.Enqueue(saveMessage);
            return;
        }

        if (LooksLikeSingleShareLink(clipboardData))
        {
            var message = directImportError.NullIfEmpty()
                ?? "Ссылка распознана как профиль, но её параметры не удалось разобрать.";
            Logging.SaveLog($"Direct share-link import failed: {message}");
            NoticeManager.Instance.Enqueue(message);
            return;
        }

        var ret = await ConfigHandler.AddBatchServers(_config, clipboardData, string.Empty, false);
        if (ret > 0)
        {
            RefreshSubscriptions();
            await RefreshServers();
            NoticeManager.Instance.Enqueue(string.Format(ResUI.SuccessfullyImportedServerViaClipboard, ret));
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private static bool TryResolveSingleSubscriptionUrl(string clipboardData, out string url, out Uri uri)
    {
        url = string.Empty;
        uri = null!;

        var lines = clipboardData
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            return false;
        }

        var value = lines[0].Trim();
        if (!value.StartsWith(Global.HttpProtocol, StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith(Global.HttpsProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parsed = Utils.TryUri(value);
        if (parsed == null)
        {
            return false;
        }

        url = value;
        uri = parsed;
        return true;
    }

    private async Task ImportSubscriptionUrlAsync(string url, Uri uri)
    {
        var subscriptions = await AppManager.Instance.SubItems();
        var subscription = subscriptions?.FirstOrDefault(item =>
            string.Equals(item.Url.TrimEx(), url, StringComparison.OrdinalIgnoreCase));
        var isNew = subscription == null;

        if (subscription == null)
        {
            subscription = new SubItem
            {
                Id = string.Empty,
                Url = url,
                Remarks = BuildImportedSubscriptionName(uri),
                Enabled = true,
                UserAgent = "SG-Client/086"
            };

            if (await ConfigHandler.AddSubItem(_config, subscription) != 0)
            {
                NoticeManager.Instance.Enqueue("Не удалось добавить ссылку подписки.");
                return;
            }
        }

        RefreshSubscriptions();

        // Prefer the running local SOCKS proxy when available and fall back to a
        // direct request automatically. This matters for GitHub/raw links that
        // may be unreachable directly on the current network. The subscription
        // record remains saved even when the first download fails.
        await UpdateSubscriptionProcess(subscription.Id, true);
        await RefreshServers();
        AppEvents.ProfilesRefreshRequested.Publish();

        var profileCount = (await AppManager.Instance.ProfileItems(subscription.Id))?.Count ?? 0;
        NoticeManager.Instance.Enqueue(profileCount > 0
            ? (isNew
                ? $"Добавлена подписка: {subscription.Remarks}. Профилей: {profileCount}."
                : $"Обновлена подписка: {subscription.Remarks}. Профилей: {profileCount}.")
            : $"Подписка сохранена: {subscription.Remarks}. Не удалось загрузить её сейчас — откройте «Подписки» и выберите обновление через VPN.");
    }

    private static string BuildImportedSubscriptionName(Uri uri)
    {
        var query = Utils.ParseQueryString(uri.Query);
        var explicitName = query["remarks"]?.Trim();
        if (explicitName.IsNotEmpty())
        {
            return explicitName!;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var fileName = segments.LastOrDefault() ?? string.Empty;
        if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        var isGithub = uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (isGithub && segments.Length >= 2 && fileName.IsNotEmpty())
        {
            return $"{segments[1]} · {fileName}";
        }

        return fileName.IsNotEmpty() ? $"{uri.IdnHost} · {fileName}" : uri.IdnHost;
    }

    private static ProfileItem? TryResolveSingleDirectProfile(
        string clipboardData,
        out string error)
    {
        error = string.Empty;
        var lines = clipboardData
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            return null;
        }

        var value = lines[0];
        if (value.StartsWith(Global.HttpProtocol, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(Global.HttpsProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var profile = FmtHandler.ResolveConfig(value, out var parserMessage);
        if (profile == null)
        {
            error = parserMessage.NullIfEmpty()
                ?? "Не удалось разобрать ссылку профиля.";
        }
        return profile;
    }

    private static bool LooksLikeSingleShareLink(string clipboardData)
    {
        var lines = clipboardData
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            return false;
        }

        var value = lines[0];
        return Global.ProtocolShares.Values.Any(prefix =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeDirectProfileValidationError(ProfileItem profile)
    {
        if (profile.Address.IsNullOrEmpty())
        {
            return "В ссылке отсутствует адрес сервера.";
        }
        if (profile.Port is <= 0 or >= 65536)
        {
            return "В ссылке указан недопустимый порт сервера.";
        }
        if (profile.Password.IsNullOrEmpty())
        {
            return "В ссылке отсутствует UUID или пароль профиля.";
        }
        if (profile.ConfigType == EConfigType.VLESS
            && profile.StreamSecurity == Global.StreamSecurityReality
            && profile.PublicKey.IsNullOrEmpty())
        {
            return "Для VLESS REALITY в ссылке отсутствует публичный ключ pbk.";
        }

        return "Профиль разобран, но не прошёл внутреннюю проверку SG Client.";
    }

    public async Task AddServerViaScanAsync()
    {
        _updateView?.Invoke(EViewAction.ScanScreenTask, null);
        await Task.CompletedTask;
    }

    public async Task ScanScreenResult(byte[]? bytes)
    {
        var result = QRCodeUtils.ParseBarcode(bytes);
        await AddScanResultAsync(result);
    }

    public async Task AddServerViaImageAsync()
    {
        _updateView?.Invoke(EViewAction.ScanImageTask, null);
        await Task.CompletedTask;
    }

    public async Task ScanImageResult(string fileName)
    {
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        var result = QRCodeUtils.ParseBarcode(fileName);
        await AddScanResultAsync(result);
    }

    public async Task AddScanResultAsync(string? result)
    {
        if (result.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.NoValidQRcodeFound);
        }
        else
        {
            if (AmneziaWgManager.LooksLikeWireGuardConfig(result))
            {
                var parsed = AmneziaWgManager.TryValidateAmneziaConfig(
                    result,
                    out var hasAmneziaParameters,
                    out var validationError);
                if (parsed && hasAmneziaParameters)
                {
                    try
                    {
                        var profile = await AmneziaWgManager.Instance.ImportProfileAsync(
                            "AmneziaWG.conf",
                            result,
                            AmneziaWgManager.GetSuggestedProfileName("AmneziaWG.conf", result));
                        await RefreshServers();
                        AppEvents.ProfilesRefreshRequested.Publish();
                        NoticeManager.Instance.Enqueue($"Добавлен профиль AmneziaWG: {profile.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logging.SaveLog("Import AmneziaWG from QR", ex);
                        NoticeManager.Instance.Enqueue(ex.Message);
                    }
                    return;
                }

                if (AmneziaWgManager.HasAmneziaParameterMarkers(result))
                {
                    NoticeManager.Instance.Enqueue(parsed
                        ? "Конфигурация не содержит распознанных параметров AmneziaWG."
                        : validationError);
                    return;
                }
            }

            var ret = await ConfigHandler.AddBatchServers(_config, result, _config.SubIndexId, false);
            if (ret > 0)
            {
                RefreshSubscriptions();
                await RefreshServers();
                NoticeManager.Instance.Enqueue(ResUI.SuccessfullyImportedServerViaScan);
            }
            else
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            }
        }
    }

    #endregion Add Servers

    #region Subscription

    private async Task SubSettingAsync()
    {
        if (await _updateView?.Invoke(EViewAction.SubSettingWindow, null) == true)
        {
            RefreshSubscriptions();
        }
    }

    public async Task UpdateSubscriptionProcess(string subId, bool blProxy)
    {
        await Task.Run(async () => await SubscriptionHandler.UpdateProcess(_config, subId, blProxy, UpdateTaskHandler));
    }

    #endregion Subscription

    #region Setting

    private async Task OptionSettingAsync()
    {
        var ret = await _updateView?.Invoke(EViewAction.OptionSettingWindow, null);
        if (ret == true)
        {
            AppEvents.InboundDisplayRequested.Publish();
            await Reload();
        }
    }

    private async Task RoutingSettingAsync()
    {
        var ret = await _updateView?.Invoke(EViewAction.RoutingSettingWindow, null);
        if (ret == true)
        {
            await ConfigHandler.InitBuiltinRouting(_config);
            AppEvents.RoutingsMenuRefreshRequested.Publish();
            await Reload();
        }
    }

    private async Task DNSSettingAsync()
    {
        var ret = await _updateView?.Invoke(EViewAction.DNSSettingWindow, null);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task FullConfigTemplateAsync()
    {
        var ret = await _updateView?.Invoke(EViewAction.FullConfigTemplateWindow, null);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task ClearServerStatistics()
    {
        await StatisticsManager.Instance.ClearAllServerStatistics();
        await RefreshServers();
    }

    private async Task OpenTheFileLocation()
    {
        var path = Utils.StartupPath();
        if (Utils.IsWindows())
        {
            ProcUtils.ProcessStart(path);
        }
        else if (Utils.IsLinux())
        {
            ProcUtils.ProcessStart("xdg-open", path);
        }
        else if (Utils.IsMacOS())
        {
            ProcUtils.ProcessStart("open", path);
        }
        await Task.CompletedTask;
    }

    #endregion Setting

    #region core job

    private bool _hasNextReloadJob = false;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    public async Task Reload()
    {
        if (!await _reloadSemaphore.WaitAsync(0))
        {
            _hasNextReloadJob = true;
            return;
        }

        try
        {
            await using var operation = await TunOperationCoordinator.EnterAsync("reload selected TUN profile");
            SetReloadEnabled(false);

            if (!_config.TunModeItem.EnableTun)
            {
                var connectionMode =
                    _config.SgQuickSettingsItem?.ConnectionMode ?? "off";

                await CoreManager.Instance.CoreStop()
                    .WaitAsync(TimeSpan.FromSeconds(15));
                if (Utils.IsWindows())
                {
                    await WindowsUtils.RemoveTunDevice()
                        .WaitAsync(TimeSpan.FromSeconds(15));
                }

                try
                {
                    await AmneziaWgManager.Instance.DisconnectAllAsync()
                        .WaitAsync(TimeSpan.FromSeconds(55));
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(
                        "Stop AmneziaWG before proxy mode",
                        ex);
                    StatusBarViewModel.Instance.ReportTunError(
                        "Не удалось остановить предыдущий туннель AmneziaWG.");
                    return;
                }

                _config.SystemProxyItem.SysProxyType =
                    ESysProxyType.ForcedClear;
                await SysProxyHandler.UpdateSysProxy(_config, false)
                    .WaitAsync(TimeSpan.FromSeconds(10));

                if (connectionMode is not ("system-proxy" or "local-proxy"))
                {
                    StatusBarViewModel.Instance.ReportConnectionOff();
                    return;
                }

                var awgSelected =
                    AmneziaWgManager.Instance.GetSelectedProfile();
                if (awgSelected != null)
                {
                    _config.SgQuickSettingsItem.ConnectionMode = "off";
                    await ConfigHandler.SaveConfig(_config);
                    StatusBarViewModel.Instance.ReportTunError(
                        "AmneziaWG не поддерживает системный или локальный прокси.");
                    return;
                }

                var proxyProfile =
                    await ConfigHandler.GetDefaultServer(_config);
                if (proxyProfile == null)
                {
                    _config.SgQuickSettingsItem.ConnectionMode = "off";
                    await ConfigHandler.SaveConfig(_config);
                    StatusBarViewModel.Instance.ReportConnectionOff();
                    // SG_EMPTY_STATE_TOP_IMPORT: no hidden clipboard action.
                    // Profile links and subscription sources remain separate.
                    NoticeManager.Instance.Enqueue(
                        "Сначала импортируйте ссылку через «Импорт» или добавьте источник в «Подписки».");
                    return;
                }

                StatusBarViewModel.Instance.ReportProxyStarting(
                    connectionMode);
                LastProxyStartError = null;

                try
                {
                    var proxyBuild =
                        await CoreConfigContextBuilder.BuildAll(
                            _config,
                            proxyProfile);
                    if (NoticeManager.Instance.NotifyValidatorResult(
                            proxyBuild.CombinedValidatorResult)
                        && !proxyBuild.Success)
                    {
                        throw new InvalidOperationException(
                            "Профиль не прошёл проверку для прокси-режима.");
                    }

                    await Task.Run(async () =>
                    {
                        await LoadCore(
                                proxyBuild.MainResult.Context,
                                proxyBuild.PreSocksResult?.Context)
                            .WaitAsync(TimeSpan.FromSeconds(35));
                    }).WaitAsync(TimeSpan.FromSeconds(45));

                    if (!CoreManager.Instance.IsCoreRunning)
                    {
                        throw new InvalidOperationException(
                            "Ядро завершилось сразу после запуска.");
                    }

                    var mixedPort = AppManager.Instance.GetLocalPort(
                        EInboundProtocol.socks);
                    if (!await WaitForLocalHttpProxyAsync(
                            mixedPort,
                            TimeSpan.FromSeconds(15)))
                    {
                        throw new InvalidOperationException(
                            $"Локальный смешанный HTTP/SOCKS-порт 127.0.0.1:{mixedPort} "
                            + "не прошёл проверку HTTP-прокси.");
                    }

                    // SG Client always uses the verified local mixed HTTP/SOCKS
                    // inbound for Windows system proxy. Ignore stale v2rayN
                    // advanced proxy strings that can point Windows elsewhere.
                    if (connectionMode == "system-proxy")
                    {
                        _config.SystemProxyItem.SystemProxyAdvancedProtocol =
                            string.Empty;
                    }

                    _config.SystemProxyItem.SysProxyType =
                        connectionMode == "system-proxy"
                            ? ESysProxyType.ForcedChange
                            : ESysProxyType.ForcedClear;

                    var proxyApplied =
                        await SysProxyHandler.UpdateSysProxy(
                            _config,
                            false);
                    if (!proxyApplied)
                    {
                        throw new InvalidOperationException(
                            "Windows не приняла настройки системного прокси.");
                    }

                    await ConfigHandler.SaveConfig(_config);
                    LastProxyStartError = null;
                    StatusBarViewModel.Instance.ReportProxyRunning(
                        connectionMode);
                    AppEvents.ProfilesRefreshRequested.Publish();
                    AppEvents.TestServerRequested.Publish();
                    return;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(
                        "Start Xray/sing-box proxy mode",
                        ex);
                    try
                    {
                        await CoreManager.Instance.CoreStop()
                            .WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    catch (Exception cleanupEx)
                    {
                        Logging.SaveLog(
                            "Cleanup failed proxy core",
                            cleanupEx);
                    }

                    _config.SystemProxyItem.SysProxyType =
                        ESysProxyType.ForcedClear;
                    _config.SgQuickSettingsItem.ConnectionMode = "off";
                    await SysProxyHandler.UpdateSysProxy(_config, false);
                    await ConfigHandler.SaveConfig(_config);
                    LastProxyStartError = ex.Message.IsNullOrEmpty()
                        ? "Не удалось запустить прокси-режим."
                        : ex.Message;
                    StatusBarViewModel.Instance.ReportTunError(
                        LastProxyStartError);
                    return;
                }
            }

            var awgProfile = AmneziaWgManager.Instance.GetSelectedProfile();
            if (awgProfile != null)
            {
                StatusBarViewModel.Instance.ReportTunStarting();
                await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(15));
                if (Utils.IsWindows())
                {
                    await WindowsUtils.RemoveTunDevice().WaitAsync(TimeSpan.FromSeconds(15));
                }
                await SysProxyHandler.UpdateSysProxy(_config, false).WaitAsync(TimeSpan.FromSeconds(10));

                try
                {
                    var result = await AmneziaWgManager.Instance.ConnectAsync(awgProfile).WaitAsync(TimeSpan.FromSeconds(75));
                    if (!result.Success || result.LastHandshake == null)
                    {
                        await FailTunStartAsync(result.Message.IsNullOrEmpty()
                            ? "Handshake AmneziaWG не получен."
                            : result.Message);
                        return;
                    }
                    StatusBarViewModel.Instance.ReportAwgRunning(awgProfile, result.LastHandshake.Value);
                    AppEvents.ProfilesRefreshRequested.Publish();
                    return;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("Start AmneziaWG TUN", ex);
                    await FailTunStartAsync(ex.Message);
                    return;
                }
            }

            try
            {
                await AmneziaWgManager.Instance.DisconnectAllAsync().WaitAsync(TimeSpan.FromSeconds(55));
            }
            catch (Exception ex)
            {
                Logging.SaveLog("Stop AmneziaWG before Xray/sing-box", ex);
                await FailTunStartAsync("Не удалось остановить предыдущий туннель AmneziaWG.");
                return;
            }

            var profileItem = await ConfigHandler.GetDefaultServer(_config);
            if (profileItem == null)
            {
                _config.TunModeItem.EnableTun = false;
                _config.SgQuickSettingsItem.ConnectionMode = "off";
                await ConfigHandler.SaveConfig(_config);
                StatusBarViewModel.Instance.ReportConnectionOff();
                // SG_EMPTY_STATE_TOP_IMPORT: no hidden clipboard action.
                // Profile links and subscription sources remain separate.
                NoticeManager.Instance.Enqueue(
                    "Сначала импортируйте ссылку через «Импорт» или добавьте источник в «Подписки».");
                return;
            }
            var allResult = await CoreConfigContextBuilder.BuildAll(_config, profileItem);
            if (NoticeManager.Instance.NotifyValidatorResult(allResult.CombinedValidatorResult) && !allResult.Success)
            {
                await FailTunStartAsync("Конфигурация выбранного профиля не прошла проверку.");
                return;
            }

            await Task.Run(async () =>
            {
                await LoadCore(allResult.MainResult.Context, allResult.PreSocksResult?.Context).WaitAsync(TimeSpan.FromSeconds(35));
                await SysProxyHandler.UpdateSysProxy(_config, false).WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(1000);
            }).WaitAsync(TimeSpan.FromSeconds(50));
            if (!CoreManager.Instance.IsCoreRunning)
            {
                await FailTunStartAsync("Ядро завершилось сразу после запуска.");
                return;
            }

            StatusBarViewModel.Instance.ReportTunRunning();
            AppEvents.ProfilesRefreshRequested.Publish();
            AppEvents.TestServerRequested.Publish();

            var showClashUI = AppManager.Instance.IsRunningCore(ECoreType.sing_box);
            if (showClashUI)
            {
                AppEvents.ProxiesReloadRequested.Publish();
            }

            ReloadResult(showClashUI);
        }
        catch (TimeoutException ex)
        {
            Logging.SaveLog("TUN operation timeout", ex);
            await FailTunStartAsync("Операция TUN не завершилась вовремя. Выполнен безопасный откат.");
        }
        finally
        {
            SetReloadEnabled(true);
            _reloadSemaphore.Release();
            if (_hasNextReloadJob)
            {
                _hasNextReloadJob = false;
                await Reload();
            }
        }
    }

    private static async Task<bool> WaitForLocalHttpProxyAsync(
        int port,
        TimeSpan timeout)
    {
        if (port is <= 0 or > 65535)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        var probe = Encoding.ASCII.GetBytes(
            "CONNECT 127.0.0.1:1 HTTP/1.1\r\n"
                + "Host: 127.0.0.1:1\r\n"
                + "Proxy-Connection: close\r\n\r\n");

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                using var attempt = new CancellationTokenSource(
                    TimeSpan.FromSeconds(2));
                await client.ConnectAsync(
                    IPAddress.Loopback,
                    port,
                    attempt.Token);

                await using var stream = client.GetStream();
                await stream.WriteAsync(probe, attempt.Token);
                await stream.FlushAsync(attempt.Token);

                var buffer = new byte[64];
                var read = await stream.ReadAsync(buffer, attempt.Token);
                if (read > 0)
                {
                    var response = Encoding.ASCII.GetString(buffer, 0, read);
                    if (response.StartsWith(
                        "HTTP/",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(250);
        }

        return false;
    }

    private async Task FailTunStartAsync(string message)
    {
        _config.TunModeItem.EnableTun = false;
        await ConfigHandler.SaveConfig(_config);
        var cleanupFailed = false;

        try
        {
            await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            cleanupFailed = true;
            Logging.SaveLog("Cleanup Xray/sing-box after failed TUN start", ex);
        }

        if (Utils.IsWindows())
        {
            try
            {
                await WindowsUtils.RemoveTunDevice().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                cleanupFailed = true;
                Logging.SaveLog("Cleanup TUN adapter after failed start", ex);
            }
        }

        try
        {
            await AmneziaWgManager.Instance.DisconnectAllAsync().WaitAsync(TimeSpan.FromSeconds(55));
        }
        catch (Exception ex)
        {
            cleanupFailed = true;
            Logging.SaveLog("Cleanup AmneziaWG after failed start", ex);
        }

        if (cleanupFailed)
        {
            message += " Часть очистки TUN не завершилась; подробности сохранены в журнале.";
        }

        StatusBarViewModel.Instance.ReportTunError(message);
        AppEvents.ProfilesRefreshRequested.Publish();
    }

    private void ReloadResult(bool showClashUI)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ShowClashUI = showClashUI;
            TabMainSelectedIndex = showClashUI ? TabMainSelectedIndex : 0;
        });
    }

    private void SetReloadEnabled(bool enabled)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() => BlReloadEnabled = enabled);
    }

    private async Task LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext)
    {
        await CoreManager.Instance.LoadCore(mainContext, preContext);
    }

    #endregion core job

    #region Presets

    public async Task ApplyRegionalPreset(EPresetType type)
    {
        await ConfigHandler.ApplyRegionalPreset(_config, type);
        await ConfigHandler.InitRouting(_config);
        AppEvents.RoutingsMenuRefreshRequested.Publish();

        await ConfigHandler.SaveConfig(_config);
        await new UpdateService(_config, UpdateTaskHandler).UpdateGeoFileAll();
        await Reload();
    }

    #endregion Presets
}
