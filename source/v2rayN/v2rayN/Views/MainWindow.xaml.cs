using System.Windows.Controls;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class MainWindow
{
    private static Config _config;
    private CancellationTokenSource? _recoveryCts;
    private bool _networkWasUnavailable;
    private DateTimeOffset? _networkUnavailableSince;
    private bool _environmentEventsRegistered;
    private SgConnectionsWindow? _connectionsWindow;
    private SgLogWindow? _logWindow;

    public MainWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachMain(this);

        _config = AppManager.Instance.Config;
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        menuClose.Click += MenuClose_Click;
        btnWindowMinimize.Click += BtnWindowMinimize_Click;
        btnImport.Click += BtnImport_Click;
        btnSubscriptions.Click += BtnSubscriptions_Click;
        btnExpert.Click += BtnExpert_Click;
        btnConnections.Click += BtnConnections_Click;
        btnTheme.Click += BtnTheme_Click;
        btnHelp.Click += BtnHelp_Click;
        btnMaintenance.Click += BtnMaintenance_Click;
        btnThemeGraphite.Click += ThemeOption_Click;
        btnThemeLight.Click += ThemeOption_Click;
        btnThemeNorthern.Click += ThemeOption_Click;
        btnLogs.Click += BtnLogs_Click;
        btnLogsCompact.Click += BtnLogsCompact_Click;
        txtThemeName.Text = SgThemeManager.GetDisplayName(SgThemeManager.Current);
        SgThemeManager.ThemeChanged += SgThemeManager_ThemeChanged;
        RegisterEnvironmentEvents();

        ViewModel = new MainWindowViewModel(UpdateViewHandler);

        profilesHost.Content ??= new ProfilesView();
        logHost.Content ??= new MsgView();

        this.WhenActivated(disposables =>
        {
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(async content => await DelegateSnackMsg(content))
                .DisposeWith(disposables);

            AppEvents.AppExitRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => StorageUI())
                .DisposeWith(disposables);

            AppEvents.ShutdownRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(Shutdown)
                .DisposeWith(disposables);

            AppEvents.ShowHideWindowRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(ShowHideWindow)
                .DisposeWith(disposables);

            AppEvents.ShowLogsRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ShowLogs())
                .DisposeWith(disposables);
        });

        Title = "SG Client — 073";

        if (_config.UiItem.AutoHideStartup)
        {
            WindowState = WindowState.Minimized;
        }

        if (!_config.GuiItem.EnableHWA)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        WindowsManager.Instance.RegisterGlobalHotkey(_config, OnHotkeyHandler, null);
    }

    private void BtnSubscriptions_Click(object sender, RoutedEventArgs e)
    {
        new SgSubscriptionsWindow { Owner = this }.ShowDialog();
    }

    private void BtnExpert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new SgExpertWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MainWindow.BtnExpert_Click", ex);
            MessageBox.Show(
                this,
                "Не удалось открыть экспертные настройки.\n\n"
                    + ex.Message
                    + "\n\nПодробности записаны в журнал SG Client.",
                "Экспертные настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private void BtnConnections_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_connectionsWindow is { IsLoaded: true })
            {
                if (_connectionsWindow.WindowState == WindowState.Minimized)
                {
                    _connectionsWindow.WindowState = WindowState.Normal;
                }

                _connectionsWindow.Activate();
                _connectionsWindow.Focus();
                return;
            }

            var window = new SgConnectionsWindow { Owner = this };
            _connectionsWindow = window;
            window.Closed += (_, _) => _connectionsWindow = null;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            _connectionsWindow = null;
            Logging.SaveLog("MainWindow.BtnConnections_Click", ex);
            MessageBox.Show(
                this,
                "Не удалось открыть список соединений.\n\n"
                    + ex.Message
                    + "\n\nПодробности записаны в журнал SG Client.",
                "Соединения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BtnMaintenance_Click(object sender, RoutedEventArgs e)
    {
        new SgMaintenanceWindow { Owner = this }.ShowDialog();
    }

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow { Owner = this }.ShowDialog();
    }

    private void OnProgramStarted(object? state, bool timeout)
    {
        Application.Current?.Dispatcher.Invoke(() => ShowHideWindow(true));
    }

    private async Task DelegateSnackMsg(string content)
    {
        if (content.IsNotEmpty())
        {
            MainSnackbar.MessageQueue?.Enqueue(BuildSgSnackContent(content));
        }
        await Task.CompletedTask;
    }

    private FrameworkElement BuildSgSnackContent(string content)
    {
        var lowered = content.ToLowerInvariant();
        var brushKey = lowered.Contains("ошиб", StringComparison.Ordinal)
            || lowered.Contains("не удалось", StringComparison.Ordinal)
            || lowered.Contains("failed", StringComparison.Ordinal)
            || lowered.Contains("error", StringComparison.Ordinal)
                ? "SgErrorBrush"
                : lowered.Contains("предупреж", StringComparison.Ordinal)
                    || lowered.Contains("внимание", StringComparison.Ordinal)
                    || lowered.Contains("небезопас", StringComparison.Ordinal)
                    || lowered.Contains("warning", StringComparison.Ordinal)
                        ? "SgWarningBrush"
                        : "SgAccentBrush";

        var accent = (Brush)FindResource(brushKey);
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = 560,
        };
        panel.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(0, 2, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = accent,
            CornerRadius = new CornerRadius(4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = content,
            MaxWidth = 530,
            Foreground = (Brush)FindResource("SgTextBrush"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
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

            case EViewAction.DNSSettingWindow:
                return new DNSSettingWindow().ShowDialog() ?? false;

            case EViewAction.RoutingSettingWindow:
                return new RoutingSettingWindow().ShowDialog() ?? false;

            case EViewAction.OptionSettingWindow:
                return new OptionSettingWindow { Owner = this }.ShowDialog() ?? false;

            case EViewAction.SgSplitTunnelWindow:
                return new SgSplitTunnelWindow { Owner = this }.ShowDialog() ?? false;

            case EViewAction.SgReserveProfileWindow:
                return new SgReserveProfileWindow { Owner = this }.ShowDialog() ?? false;

            case EViewAction.SgRoutingWindow:
                return new SgRoutingWindow { Owner = this }.ShowDialog() ?? false;

            case EViewAction.SgDpiWindow:
                return new SgDpiWindow { Owner = this }.ShowDialog() ?? false;

            case EViewAction.SgHelpWindow:
                new SgHelpWindow { Owner = this }.ShowDialog();
                return true;

            case EViewAction.FullConfigTemplateWindow:
                return new FullConfigTemplateWindow().ShowDialog() ?? false;

            case EViewAction.GlobalHotkeySettingWindow:
                return new GlobalHotkeySettingWindow().ShowDialog() ?? false;

            case EViewAction.SubSettingWindow:
                return new SubSettingWindow().ShowDialog() ?? false;

            case EViewAction.ScanScreenTask:
                await ScanScreenTaskAsync();
                break;

            case EViewAction.ScanImageTask:
                await ScanImageTaskAsync();
                break;

            case EViewAction.AddServerViaClipboard:
                await AddServerViaClipboardAsync();
                break;
        }

        return true;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                break;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ShowHideWindow(false);
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        StorageUI();
        await StatusBarViewModel.Instance.DisableTunAsync();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
        UnregisterEnvironmentEvents();
        Application.Current.Shutdown();
    }

    private void SgThemeManager_ThemeChanged(string theme)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SgThemeManager_ThemeChanged(theme));
            return;
        }
        txtThemeName.Text = SgThemeManager.GetDisplayName(theme);
    }

    private void RegisterEnvironmentEvents()
    {
        if (_environmentEventsRegistered)
        {
            return;
        }

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        _environmentEventsRegistered = true;
    }

    private void UnregisterEnvironmentEvents()
    {
        if (!_environmentEventsRegistered)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        _environmentEventsRegistered = false;
        SgThemeManager.ThemeChanged -= SgThemeManager_ThemeChanged;
        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        _recoveryCts = null;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && _config.SgQuickSettingsItem.AutoRecoverTun)
        {
            ScheduleTunRecovery("выход компьютера из сна", TimeSpan.FromSeconds(4));
        }
    }

    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
        {
            // Creating or removing a TUN adapter can briefly change Windows network
            // availability. Do not interpret our own transition as a real outage.
            if (!_config.TunModeItem.EnableTun
                || StatusBarViewModel.Instance.TunBusy
                || TunOperationCoordinator.IsBusy)
            {
                _networkWasUnavailable = false;
                _networkUnavailableSince = null;
                return;
            }

            _networkWasUnavailable = true;
            _networkUnavailableSince = DateTimeOffset.UtcNow;
            return;
        }

        if (!_networkWasUnavailable)
        {
            return;
        }

        var outageDuration = DateTimeOffset.UtcNow - (_networkUnavailableSince ?? DateTimeOffset.UtcNow);
        _networkWasUnavailable = false;
        _networkUnavailableSince = null;

        // Ignore very short adapter flaps. They are usually caused by TUN switching.
        if (outageDuration >= TimeSpan.FromSeconds(2)
            && _config.SgQuickSettingsItem.AutoRecoverTun
            && _config.TunModeItem.EnableTun
            && !StatusBarViewModel.Instance.TunBusy
            && !TunOperationCoordinator.IsBusy)
        {
            ScheduleTunRecovery("восстановление сети", TimeSpan.FromSeconds(4));
        }
    }

    private void ScheduleTunRecovery(string reason, TimeSpan delay)
    {
        if (!_config.SgQuickSettingsItem.AutoRecoverTun || !_config.TunModeItem.EnableTun)
        {
            return;
        }

        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        var cts = new CancellationTokenSource();
        _recoveryCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);

                // After resume Windows may report the network before an uplink is
                // actually available. Wait for the real availability event instead
                // of restarting the tunnel into a guaranteed failure.
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    _networkWasUnavailable = true;
                    _networkUnavailableSince = DateTimeOffset.UtcNow;
                    return;
                }

                await (await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (cts.IsCancellationRequested
                        || !_config.SgQuickSettingsItem.AutoRecoverTun
                        || !_config.TunModeItem.EnableTun
                        || StatusBarViewModel.Instance.TunBusy
                        || TunOperationCoordinator.IsBusy
                        || ViewModel == null)
                    {
                        return;
                    }

                    Logging.SaveLog($"TUN recovery requested after {reason}");
                    await ViewModel.Reload();
                }));
            }
            catch (OperationCanceledException)
            {
                // A newer environment event replaced this recovery request.
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"TUN recovery failed after {reason}", ex);
                StatusBarViewModel.Instance.ReportTunError("Не удалось восстановить TUN после изменения сети. Откройте журнал.");
            }
        });
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                    {
                        return;
                    }
                    _ = AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    _ = ScanScreenTaskAsync();
                    break;
            }
        }
        else if (e.Key == Key.F5)
        {
            ViewModel?.Reload();
        }
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void BtnWindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        themePopup.IsOpen = false;
        importPopup.IsOpen = !importPopup.IsOpen;
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        importPopup.IsOpen = false;
        themePopup.IsOpen = !themePopup.IsOpen;
    }

    private async void ThemeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string theme })
        {
            return;
        }

        themePopup.IsOpen = false;
        await SgThemeManager.ApplyAndSaveAsync(theme);
        txtThemeName.Text = SgThemeManager.GetDisplayName(theme);
    }

    private void ImportAction_Click(object sender, RoutedEventArgs e)
    {
        importPopup.IsOpen = false;
    }

    private async void ImportAwgText_Click(object sender, RoutedEventArgs e)
    {
        importPopup.IsOpen = false;
        await OpenAwgTextImportAsync();
    }

    private async Task OpenAwgTextImportAsync(string? content = null, string? sourceFileName = null)
    {
        var previousAwgProfileId = AmneziaWgManager.Instance.SelectedProfileId;
        var dialog = new SgAwgTextImportWindow(content, sourceFileName ?? "AmneziaWG.conf") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ImportedProfile == null)
        {
            return;
        }

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

        AppEvents.ProfilesRefreshRequested.Publish();
        await DelegateSnackMsg($"Добавлен профиль AmneziaWG: {dialog.ImportedProfile.Name}");
    }

    private async void ImportFromFile_Click(object sender, RoutedEventArgs e)
    {
        importPopup.IsOpen = false;

        if (UI.OpenFileDialog(out var fileName, "Подключение|*.txt;*.url;*.conf|Все файлы|*.*") != true
            || fileName.IsNullOrEmpty())
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(fileName);
            if (content.IsNullOrEmpty())
            {
                await DelegateSnackMsg("Выбранный файл пуст.");
                return;
            }

            if (AmneziaWgManager.LooksLikeWireGuardConfig(content)
                && AmneziaWgManager.IsAmneziaConfig(content))
            {
                await OpenAwgTextImportAsync(content, fileName);
                return;
            }

            if (ViewModel != null)
            {
                // Ordinary WireGuard configs deliberately continue through the
                // standard WireGuard importer. Only configs with Amnezia J/S/H
                // parameters are routed to the AmneziaWG profile store.
                await ViewModel.AddServerViaClipboardAsync(content);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Import connection file", ex);
            await DelegateSnackMsg(ex.Message.IsNullOrEmpty()
                ? "Не удалось импортировать файл подключения."
                : ex.Message);
        }
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e)
    {
        ShowLogs();
    }

    private void BtnLogsCompact_Click(object sender, RoutedEventArgs e)
    {
        if (expLogs.IsExpanded)
        {
            HideCompactLogs();
        }
        else
        {
            ShowCompactLogs();
        }
    }

    private void ShowLogs()
    {
        ShowHideWindow(true);

        if (_logWindow is { IsLoaded: true })
        {
            _logWindow.ActivateAndBringToFront();
            return;
        }

        var initialText = (logHost.Content as MsgView)?.GetText();
        _logWindow = new SgLogWindow(initialText)
        {
            Owner = this,
        };
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show();
    }

    private void ShowCompactLogs()
    {
        expLogs.IsExpanded = true;
        icoLogsCompact.Kind = PackIconKind.ChevronUp;
        btnLogsCompact.ToolTip = "Скрыть краткий журнал";
    }

    private void HideCompactLogs()
    {
        expLogs.IsExpanded = false;
        icoLogsCompact.Kind = PackIconKind.ChevronDown;
        btnLogsCompact.ToolTip = "Показать краткий журнал в главном окне";
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = WindowsUtils.GetClipboardData();
        if (clipboardData.IsNullOrEmpty())
        {
            return;
        }
        if (AmneziaWgManager.LooksLikeWireGuardConfig(clipboardData)
            && AmneziaWgManager.IsAmneziaConfig(clipboardData))
        {
            await OpenAwgTextImportAsync(clipboardData, "AmneziaWG.conf");
            return;
        }
        if (ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);
        string? result = null;

        if (Application.Current?.MainWindow is Window window)
        {
            var bytes = QRCodeWindowsUtils.CaptureScreen(window);
            result = QRCodeUtils.ParseBarcode(bytes);
        }

        ShowHideWindow(true);
        await ImportScannedContentAsync(result, "AmneziaWG.conf");
    }

    private async Task ScanImageTaskAsync()
    {
        if (UI.OpenFileDialog(out var fileName, "PNG|*.png|All|*.*") != true || fileName.IsNullOrEmpty())
        {
            return;
        }

        var result = QRCodeUtils.ParseBarcode(fileName);
        await ImportScannedContentAsync(result, "AmneziaWG.conf");
    }

    private async Task ImportScannedContentAsync(string? result, string sourceFileName)
    {
        if (result.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.NoValidQRcodeFound);
            return;
        }

        if (AmneziaWgManager.LooksLikeWireGuardConfig(result)
            && AmneziaWgManager.IsAmneziaConfig(result))
        {
            await OpenAwgTextImportAsync(result, sourceFileName);
            return;
        }

        if (ViewModel != null)
        {
            await ViewModel.AddScanResultAsync(result);
        }
    }

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }
}
