using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace v2rayN.Views;

public partial class ProfilesView
{
    private static Config _config;
    private bool _selectionCommandRunning;
    private ICollectionView? _profilesView;
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _filterChoicesPending;
    private bool _suppressFilterRefresh;
    private HashSet<string> _knownProfileIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _profileSnapshotInitialized;
    private bool _browserPreferencesReady;
    private bool _showProblemProfilesOnly;

    public ProfilesView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;

        txtServerFilter.PreviewKeyDown += TxtServerFilter_PreviewKeyDown;
        lstProfiles.PreviewKeyDown += LstProfiles_PreviewKeyDown;
        lstProfiles.SelectionChanged += LstProfiles_SelectionChanged;
        btnRemoveServer.Click += BtnRemoveServer_Click;

        ViewModel = new ProfilesViewModel(UpdateViewHandler);
        InitializeProfileBrowser();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.lstGroup.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);
            // SG_PROFILE_ACTIONS_INLINE_LATENCY_086: latency progress and Stop reuse the permanent middle action button.
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.btnAddSub).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditSubCmd, v => v.btnEditSub).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.EditServerCmd, v => v.menuEditServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveServerCmd, v => v.menuRemoveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CopyServerCmd, v => v.menuCopyServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDefaultServerCmd, v => v.menuSetDefaultServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ShareServerCmd, v => v.menuShareServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RealPingServerCmd, v => v.menuRealPingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MixedTestServerCmd, v => v.menuMixedTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlCmd, v => v.menuExport2ShareUrl).DisposeWith(disposables);

            StatusBarViewModel.Instance.WhenAnyValue(
                    vm => vm.TunUiState,
                    vm => vm.ConnectionModeKey)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateActivateProfileButton())
                .DisposeWith(disposables);
        });
    }

    private void InitializeProfileBrowser()
    {
        _profilesView = CollectionViewSource.GetDefaultView(ViewModel.ProfileItems);
        _profilesView.Filter = ProfileMatchesFilter;
        lstProfiles.ItemsSource = _profilesView;

        cmbProfileSort.ItemsSource = new[]
        {
            new BrowserOption("original", "Исходный порядок"),
            new BrowserOption("latency", "Задержка: быстрее"),
            new BrowserOption("name", "Название: А–Я"),
            new BrowserOption("subscription", "По источнику"),
            new BrowserOption("protocol", "По протоколу"),
            new BrowserOption("country", "По стране"),
        };
        var quick = _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        cmbProfileSort.SelectedValue = quick.ProfileBrowserSort.IsNotEmpty()
            ? quick.ProfileBrowserSort
            : "original";

        cmbSubscriptionFilter.ItemsSource = new[] { new BrowserOption("all", "Все источники") };
        cmbSubscriptionFilter.SelectedValue = "all";
        cmbProtocolFilter.ItemsSource = new[] { new BrowserOption("all", "Все протоколы") };
        cmbProtocolFilter.SelectedValue = "all";
        cmbCountryFilter.ItemsSource = new[] { new BrowserOption("all", "Все страны") };
        cmbCountryFilter.SelectedValue = "all";

        if (ViewModel.ProfileItems is INotifyCollectionChanged changed)
        {
            changed.CollectionChanged += ProfileItems_CollectionChanged;
        }

        ViewModel.SpeedTestCompleted += ViewModel_SpeedTestCompleted;
        ViewModel.CountryMetadataChanged += ViewModel_CountryMetadataChanged;
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            RefreshBrowserView();
        };

        RebuildFilterChoices();
        RestoreBrowserPreferences();
        _browserPreferencesReady = true;
        RefreshBrowserView();
        UpdateActivateProfileButton();
    }

    private void ProfileItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_filterChoicesPending)
        {
            return;
        }

        _filterChoicesPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            _filterChoicesPending = false;
            var detectedNewLocalProfile = DetectNewLocalProfile();
            var pendingRevealId = ViewModel.ConsumePendingRevealProfileId();
            var importedLocalProfile = pendingRevealId.IsNotEmpty()
                ? ViewModel.ProfileItems.FirstOrDefault(item => string.Equals(item.IndexId, pendingRevealId, StringComparison.OrdinalIgnoreCase))
                : detectedNewLocalProfile;
            RebuildFilterChoices();
            if (importedLocalProfile != null)
            {
                await RevealImportedLocalProfileAsync(importedLocalProfile);
            }
            else
            {
                RefreshBrowserView();
            }
        }));
    }

    private void RebuildFilterChoices()
    {
        var quick = _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        var selectedSubscription = _browserPreferencesReady
            ? quick.ProfileBrowserSubscriptionFilter
            : cmbSubscriptionFilter.SelectedValue?.ToString() ?? "all";
        var selectedProtocol = _browserPreferencesReady
            ? quick.ProfileBrowserProtocolFilter
            : cmbProtocolFilter.SelectedValue?.ToString() ?? "all";
        var selectedCountry = _browserPreferencesReady
            ? quick.ProfileBrowserCountryFilter
            : cmbCountryFilter.SelectedValue?.ToString() ?? "all";

        _suppressFilterRefresh = true;
        try
        {
        var subscriptions = new List<BrowserOption> { new("all", "Все источники"), new("local", "Локальные профили") };
        subscriptions.AddRange(ViewModel.ProfileItems
            .Where(item => item.IsSub && item.Subid.IsNotEmpty())
            .GroupBy(item => item.Subid, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BrowserOption(group.Key, group.Select(item => item.SubRemarks).FirstOrDefault(value => value.IsNotEmpty()) ?? "Подписка"))
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase));
        cmbSubscriptionFilter.ItemsSource = subscriptions;
        cmbSubscriptionFilter.SelectedValue = subscriptions.Any(item => item.Key == selectedSubscription) ? selectedSubscription : "all";

        var protocols = new List<BrowserOption> { new("all", "Все протоколы") };
        var supportedProtocols = new[]
        {
            new BrowserOption("VLESS", "VLESS"),
            new BrowserOption("VLESS · REALITY", "VLESS · REALITY"),
            new BrowserOption("VLESS XHTTP · REALITY", "VLESS XHTTP · REALITY"),
            new BrowserOption("VLESS XHTTP · TLS", "VLESS XHTTP · TLS"),
            new BrowserOption("Hysteria2", "Hysteria2"),
            new BrowserOption("AmneziaWG", "AmneziaWG"),
            new BrowserOption("Trojan", "Trojan"),
            new BrowserOption("VMess", "VMess"),
        };
        protocols.AddRange(supportedProtocols);

        var knownProtocolKeys = supportedProtocols
            .Select(item => item.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        protocols.AddRange(ViewModel.ProfileItems
            .Select(item => item.ProtocolDisplay)
            .Where(value => value.IsNotEmpty() && !knownProtocolKeys.Contains(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Select(value => new BrowserOption(value, value)));
        cmbProtocolFilter.ItemsSource = protocols;
        cmbProtocolFilter.SelectedValue = protocols.Any(item => item.Key == selectedProtocol) ? selectedProtocol : "all";

        var countries = new List<BrowserOption> { new("all", "Все страны") };
        countries.AddRange(ViewModel.ProfileItems
            .Select(item => item.ResolvedCountryCode)
            .Where(code => code.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new BrowserOption(code, SgCountryHelper.GetFilterLabel(code), code))
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase));
        if (ViewModel.ProfileItems.Any(item => item.ResolvedCountryCode.IsNullOrEmpty()))
        {
            countries.Add(new BrowserOption("unknown", "Страна не определена"));
        }
        cmbCountryFilter.ItemsSource = countries;
        cmbCountryFilter.SelectedValue = countries.Any(item => item.Key == selectedCountry) ? selectedCountry : "all";
        chkExcludeCountry.IsEnabled = !string.Equals(cmbCountryFilter.SelectedValue?.ToString(), "all", StringComparison.Ordinal);
        chkExcludeCountry.IsChecked = chkExcludeCountry.IsEnabled && quick.ProfileBrowserExcludeCountry;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private ProfileItemModel? DetectNewLocalProfile()
    {
        var currentIds = ViewModel.ProfileItems
            .Select(item => item.IndexId)
            .Where(id => id.IsNotEmpty())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!_profileSnapshotInitialized)
        {
            _knownProfileIds = currentIds;
            _profileSnapshotInitialized = true;
            return null;
        }

        var imported = ViewModel.ProfileItems
            .Where(item => !item.IsSub && item.IndexId.IsNotEmpty() && !_knownProfileIds.Contains(item.IndexId))
            .OrderBy(item => item.Sort)
            .LastOrDefault();

        _knownProfileIds = currentIds;
        return imported;
    }

    private Task RevealImportedLocalProfileAsync(ProfileItemModel profile)
    {
        _suppressFilterRefresh = true;
        try
        {
            txtProfileSearch.Clear();
            cmbSubscriptionFilter.SelectedValue = "local";
            cmbProtocolFilter.SelectedValue = "all";
            cmbCountryFilter.SelectedValue = "all";
            chkExcludeCountry.IsChecked = false;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
        RefreshBrowserView();

        _selectionCommandRunning = true;
        try
        {
            ViewModel.SelectedProfile = profile;
            lstProfiles.SelectedItem = profile;
        }
        finally
        {
            _selectionCommandRunning = false;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            lstProfiles.ScrollIntoView(profile);
            lstProfiles.Focus();
            UpdateActivateProfileButton();
        }));
        return Task.CompletedTask;
    }

    private bool ProfileMatchesFilter(object obj)
    {
        if (obj is not ProfileItemModel item)
        {
            return false;
        }

        if (_showProblemProfilesOnly && item.Delay >= 0)
        {
            return false;
        }

        var query = txtProfileSearch.Text?.Trim();
        if (query.IsNotEmpty()
            && !(item.Remarks?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true
                 || item.DisplayRemarks?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true
                 || item.Address?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                 || item.ProtocolDisplay?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true
                 || item.SourceDisplay?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true
                 || item.ResolvedCountryCode.Contains(query, StringComparison.OrdinalIgnoreCase)
                 || item.CountryName?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true))
        {
            return false;
        }

        var subscription = cmbSubscriptionFilter.SelectedValue?.ToString() ?? "all";
        if (subscription == "local")
        {
            if (item.IsSub)
            {
                return false;
            }
        }
        else if (subscription != "all" && (!item.IsSub || !string.Equals(item.Subid, subscription, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var protocol = cmbProtocolFilter.SelectedValue?.ToString() ?? "all";
        if (protocol != "all" && !string.Equals(item.ProtocolDisplay, protocol, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        var country = cmbCountryFilter.SelectedValue?.ToString() ?? "all";
        if (country == "all")
        {
            return true;
        }

        var countryMatches = country == "unknown"
            ? item.ResolvedCountryCode.IsNullOrEmpty()
            : string.Equals(item.ResolvedCountryCode, country, StringComparison.OrdinalIgnoreCase);
        return chkExcludeCountry.IsChecked == true ? !countryMatches : countryMatches;
    }


    private void ProfileCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsEnabled || combo.IsDropDownOpen)
        {
            return;
        }

        combo.Focus();
        combo.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void ProfileCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsEnabled || combo.IsDropDownOpen)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Space or Key.F4
            || (e.Key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            combo.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void ProfileSearch_TextChanged(object sender, TextChangedEventArgs e) => ScheduleFilterRefresh();

    private void ClearProfileSearch_Click(object sender, RoutedEventArgs e)
    {
        txtProfileSearch.Clear();
        txtProfileSearch.Focus();
    }

    private void ProfileFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterRefresh)
        {
            return;
        }

        var country = cmbCountryFilter.SelectedValue?.ToString() ?? "all";
        chkExcludeCountry.IsEnabled = country != "all";
        if (country == "all")
        {
            chkExcludeCountry.IsChecked = false;
        }
        RefreshBrowserView();
        SaveBrowserPreferences();
    }

    private void CountryExclude_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterRefresh)
        {
            return;
        }
        RefreshBrowserView();
        SaveBrowserPreferences();
    }

    private void RestoreBrowserPreferences()
    {
        var quick = _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        _suppressFilterRefresh = true;
        try
        {
            SetSelectedValueIfAvailable(cmbSubscriptionFilter, quick.ProfileBrowserSubscriptionFilter, "all");
            SetSelectedValueIfAvailable(cmbProtocolFilter, quick.ProfileBrowserProtocolFilter, "all");
            SetSelectedValueIfAvailable(cmbCountryFilter, quick.ProfileBrowserCountryFilter, "all");
            SetSelectedValueIfAvailable(cmbProfileSort, quick.ProfileBrowserSort, "original");
            chkExcludeCountry.IsEnabled = !string.Equals(cmbCountryFilter.SelectedValue?.ToString(), "all", StringComparison.Ordinal);
            chkExcludeCountry.IsChecked = chkExcludeCountry.IsEnabled && quick.ProfileBrowserExcludeCountry;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private static void SetSelectedValueIfAvailable(ComboBox combo, string? requested, string fallback)
    {
        var value = requested.IsNotEmpty() ? requested! : fallback;
        var exists = combo.Items.Cast<object>()
            .OfType<BrowserOption>()
            .Any(item => string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedValue = exists ? value : fallback;
    }

    private void SaveBrowserPreferences()
    {
        if (!_browserPreferencesReady)
        {
            return;
        }

        var quick = _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        quick.ProfileBrowserSubscriptionFilter = cmbSubscriptionFilter.SelectedValue?.ToString() ?? "all";
        quick.ProfileBrowserProtocolFilter = cmbProtocolFilter.SelectedValue?.ToString() ?? "all";
        quick.ProfileBrowserCountryFilter = cmbCountryFilter.SelectedValue?.ToString() ?? "all";
        quick.ProfileBrowserSort = cmbProfileSort.SelectedValue?.ToString() ?? "original";
        quick.ProfileBrowserExcludeCountry = chkExcludeCountry.IsChecked == true;
        _ = ConfigHandler.SaveConfig(_config);
    }

    private void ScheduleFilterRefresh()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ProfileSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplySort();
        UpdateProfileCount();
        SaveBrowserPreferences();
    }

    private void ApplySort()
    {
        if (_profilesView is not ListCollectionView listView)
        {
            _profilesView?.Refresh();
            return;
        }

        var mode = cmbProfileSort.SelectedValue?.ToString() ?? "original";
        listView.CustomSort = new ProfileBrowserComparer(mode);
    }

    private void RefreshBrowserView()
    {
        _profilesView?.Refresh();
        ApplySort();
        UpdateProfileCount();
    }

    private void UpdateProfileCount()
    {
        var visible = _profilesView?.Cast<object>().Count() ?? 0;
        txtProfileCount.Text = visible == 0 && ViewModel.ProfileItems.Count > 0
            ? "Нет совпадений — измените фильтры"
            : $"Показано: {visible} из {ViewModel.ProfileItems.Count}";
    }

    private void ViewModel_SpeedTestCompleted()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (string.Equals(cmbProfileSort.SelectedValue?.ToString(), "latency", StringComparison.Ordinal))
            {
                ApplySort();
            }
            UpdateProfileCount();
        }));
    }

    private void ViewModel_CountryMetadataChanged()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            RebuildFilterChoices();
            RefreshBrowserView();
        }));
    }

    private void LatencyMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LatencyTestRunning)
        {
            if (!ViewModel.LatencyTestStopping)
            {
                ViewModel.ServerSpeedtestStop();
            }
            return;
        }

        if (btnLatencyMenu.ContextMenu == null)
        {
            return;
        }

        btnLatencyMenu.ContextMenu.PlacementTarget = btnLatencyMenu;
        btnLatencyMenu.ContextMenu.IsOpen = true;
    }

    private async void PingVisible_Click(object sender, RoutedEventArgs e)
    {
        _showProblemProfilesOnly = false;
        ViewModel.LatencyTestProblemButtonText = "Показать проблемные";
        RefreshBrowserView();
        var visible = _profilesView?.Cast<ProfileItemModel>().ToList() ?? [];
        await ViewModel.TestLatencyAsync(visible);
    }

    private async void PingAll_Click(object sender, RoutedEventArgs e)
    {
        _showProblemProfilesOnly = false;
        ViewModel.LatencyTestProblemButtonText = "Показать проблемные";
        RefreshBrowserView();
        await ViewModel.TestLatencyAsync(ViewModel.ProfileItems.ToList());
    }

    private void ShowProblemProfiles_Click(object sender, RoutedEventArgs e)
    {
        _showProblemProfilesOnly = !_showProblemProfilesOnly;
        ViewModel.LatencyTestProblemButtonText = _showProblemProfilesOnly
            ? "Показать все"
            : $"Проблемные: {ViewModel.LatencyTestProblemCount}";
        RefreshBrowserView();
    }

    private void CancelLatencyTest_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ServerSpeedtestStop();
    }

    private sealed record BrowserOption(string Key, string Label, string CountryCode = "")
    {
        public string CountryFlagUri => $"pack://application:,,,/Assets/Flags/{(string.IsNullOrWhiteSpace(CountryCode) ? "ZZ" : CountryCode)}.png";
        public override string ToString() => Label;
    }

    private sealed class ProfileBrowserComparer(string mode) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is not ProfileItemModel left)
            {
                return -1;
            }
            if (y is not ProfileItemModel right)
            {
                return 1;
            }

            var result = mode switch
            {
                "latency" => CompareLatency(left, right),
                "name" => CompareText(left.DisplayRemarks, right.DisplayRemarks),
                "subscription" => CompareText(left.SourceDisplay, right.SourceDisplay),
                "protocol" => CompareText(left.ProtocolDisplay, right.ProtocolDisplay),
                "country" => CompareCountry(left, right),
                _ => left.Sort.CompareTo(right.Sort),
            };

            if (result != 0)
            {
                return result;
            }

            result = CompareText(left.DisplayRemarks, right.DisplayRemarks);
            return result != 0 ? result : string.Compare(left.IndexId, right.IndexId, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareCountry(ProfileItemModel left, ProfileItemModel right)
        {
            var leftUnknown = left.ResolvedCountryCode.IsNullOrEmpty();
            var rightUnknown = right.ResolvedCountryCode.IsNullOrEmpty();
            if (leftUnknown != rightUnknown)
            {
                return leftUnknown ? 1 : -1;
            }

            var result = CompareText(left.CountryName, right.CountryName);
            return result != 0 ? result : CompareText(left.ResolvedCountryCode, right.ResolvedCountryCode);
        }

        private static int CompareLatency(ProfileItemModel left, ProfileItemModel right)
        {
            static int Rank(ProfileItemModel item) => item.Delay > 0 ? 0 : item.Delay == 0 ? 1 : 2;
            var rank = Rank(left).CompareTo(Rank(right));
            if (rank != 0)
            {
                return rank;
            }
            return left.Delay > 0 ? left.Delay.CompareTo(right.Delay) : 0;
        }

        private static int CompareText(string? left, string? right) =>
            string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.SetClipboardData:
                if (obj is null)
                {
                    return false;
                }
                WindowsUtils.SetClipboardData((string)obj);
                break;

            case EViewAction.ProfilesFocus:
                lstProfiles.Focus();
                break;

            case EViewAction.ShowYesNo:
                if (UI.ShowYesNo(ResUI.RemoveServer) == MessageBoxResult.No)
                {
                    return false;
                }
                break;

            case EViewAction.SaveFileDialog:
                if (obj is null || UI.SaveFileDialog(out var fileName, "Config|*.json") != true)
                {
                    return false;
                }
                ViewModel?.Export2ClientConfigResult(fileName, (ProfileItem)obj);
                break;

            case EViewAction.AddServerWindow:
                if (obj is null)
                {
                    return false;
                }
                return new AddServerWindow((ProfileItem)obj).ShowDialog() ?? false;

            case EViewAction.AddServer2Window:
                if (obj is null)
                {
                    return false;
                }
                return new AddServer2Window((ProfileItem)obj).ShowDialog() ?? false;

            case EViewAction.AddGroupServerWindow:
                if (obj is null)
                {
                    return false;
                }
                return new AddGroupServerWindow((ProfileItem)obj).ShowDialog() ?? false;

            case EViewAction.ShareServer:
                if (obj is null)
                {
                    return false;
                }
                ShareServer((string)obj);
                break;

            case EViewAction.SubEditWindow:
                if (obj is null)
                {
                    return false;
                }
                return new SubEditWindow((SubItem)obj).ShowDialog() ?? false;

            case EViewAction.DispatcherRefreshServersBiz:
                Application.Current?.Dispatcher.Invoke(RefreshServersBiz, DispatcherPriority.Normal);
                break;
        }

        return true;
    }

    public async void ShareServer(string url)
    {
        var img = QRCodeWindowsUtils.GetQRCode(url);
        var dialog = new QrcodeView
        {
            imgQrcode = { Source = img },
            txtContent = { Text = url },
        };

        await DialogHost.Show(dialog, "RootDialog");
    }

    public void RefreshServersBiz()
    {
        if (lstProfiles.SelectedIndex >= 0)
        {
            lstProfiles.ScrollIntoView(lstProfiles.SelectedItem);
        }
    }

    private void LstProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelectedProfiles(updatePrimary: true);
        UpdateActivateProfileButton();
    }

    private async void ActivateSelectedProfile_Click(object sender, RoutedEventArgs e)
    {
        if (lstProfiles.SelectedItem is ProfileItemModel selected)
        {
            await ActivateProfileFromUserAsync(selected);
        }
    }

    private void UpdateActivateProfileButton()
    {
        if (btnActivateProfile == null || txtActivateProfileButton == null)
        {
            return;
        }

        if (lstProfiles.SelectedItem is not ProfileItemModel selected
            || selected.IndexId.IsNullOrEmpty()
            || StatusBarViewModel.Instance.TunBusy)
        {
            btnActivateProfile.IsEnabled = false;
            txtActivateProfileButton.Text = "Подключить";
            return;
        }

        var running = StatusBarViewModel.Instance.TunUiState == ETunUiState.On
            && StatusBarViewModel.Instance.GetConnectionModeKey() != "off";
        if (selected.IsActive && running)
        {
            btnActivateProfile.IsEnabled = false;
            txtActivateProfileButton.Text = "Работает";
            return;
        }

        btnActivateProfile.IsEnabled = true;
        txtActivateProfileButton.Text = running ? "Переключить" : "Подключить";
    }

    private async Task ActivateProfileFromUserAsync(ProfileItemModel selected)
    {
        if (ViewModel == null || StatusBarViewModel.Instance.TunBusy || _selectionCommandRunning)
        {
            return;
        }

        _selectionCommandRunning = true;
        try
        {
            Logging.SaveLog($"Profile selected by user: {selected.IndexId}; subscription={selected.IsSub}; AWG={selected.IsAmneziaWG}");
            await ViewModel.ActivateProfileAsync(selected.IndexId);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Profile click activation failed: {selected.IndexId}", ex);
            NoticeManager.Instance.Enqueue(ex.Message.IsNullOrEmpty()
                ? "Не удалось переключить выбранный профиль."
                : ex.Message);
        }
        finally
        {
            _selectionCommandRunning = false;
            UpdateActivateProfileButton();
        }
    }

    private void SyncSelectedProfiles(bool updatePrimary = false)
    {
        if (ViewModel == null)
        {
            return;
        }

        var selected = new List<ProfileItemModel>();
        if (lstProfiles.SelectedItem is ProfileItemModel selectedItem)
        {
            selected.Add(selectedItem);
            if (updatePrimary)
            {
                ViewModel.SelectedProfile = selectedItem;
            }
        }

        ViewModel.SelectedProfiles = selected;
    }

    private void ProfileItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        if (!item.IsSelected)
        {
            item.IsSelected = true;
        }

        item.Focus();
        if (ViewModel != null && item.DataContext is ProfileItemModel profile)
        {
            ViewModel.SelectedProfile = profile;
        }
        SyncSelectedProfiles();
    }

    private async void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
    {
        SyncSelectedProfiles(updatePrimary: true);
        if (ViewModel != null)
        {
            await ViewModel.RemoveServerAsync();
        }
    }

    private async void LstProfiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (lstProfiles.SelectedItem is ProfileItemModel selected)
        {
            await ActivateProfileFromUserAsync(selected);
            e.Handled = true;
        }
    }

    private void LstProfiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.A:
                    break;
                case Key.C:
                    ViewModel?.Export2ShareUrlAsync(false);
                    break;
                case Key.R:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Realping);
                    break;
                case Key.O:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Tcping);
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (ViewModel != null && lstProfiles.SelectedItem is ProfileItemModel selected)
                    {
                        ViewModel.SelectedProfile = selected;
                        _ = ActivateProfileFromUserAsync(selected);
                        e.Handled = true;
                    }
                    break;
                case Key.Delete:
                case Key.Back:
                    SyncSelectedProfiles(updatePrimary: true);
                    _ = ViewModel?.RemoveServerAsync();
                    break;
                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    private void TxtServerFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            ViewModel?.RefreshServers();
        }
    }
}
