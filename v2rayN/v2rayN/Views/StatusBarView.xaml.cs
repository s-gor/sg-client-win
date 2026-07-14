using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class StatusBarView
{
    private static Config _config;
    private readonly DispatcherTimer _sessionTimer;
    private DateTime? _tunConnectedAt;
    private ETunUiState _lastTunState = ETunUiState.Off;

    public StatusBarView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;
        ViewModel = StatusBarViewModel.Instance;
        DataContext = ViewModel;
        ViewModel?.InitUpdateView(UpdateViewHandler);

        _sessionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _sessionTimer.Tick += async (_, _) =>
        {
            RefreshTunSessionDisplay();
            if (ViewModel != null)
            {
                await ViewModel.ReconcileConnectionStateAsync();
                await ViewModel.RefreshAwgTrafficAsync();
            }
        };

        Loaded += StatusBarView_Loaded;
        Unloaded += StatusBarView_Unloaded;
        menuExit.Click += MenuExit_Click;
        menuProfiles.Click += MenuProfiles_Click;
        menuLogs.Click += MenuLogs_Click;
        btnTrafficResetSession.Click += TrafficResetSession_Click;
        btnTrafficResetAll.Click += TrafficResetAll_Click;
        btnTrafficProfiles.Click += TrafficProfiles_Click;
        btnQuickDns.Click += QuickDns_Click;
        txtRunningServerDisplay.PreviewMouseDown += RunningInfo_PreviewMouseDown;
        txtRunningInfoDisplay.PreviewMouseDown += RunningInfo_PreviewMouseDown;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileNameDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ProfileProtocolDisplay, v => v.txtProtocolDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TunDetailText, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TunStatusText, v => v.txtTunStatusDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TunStatusText, v => v.menuTrayStatus.Header, text => $"SG Client — {text}").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ProfileNameDisplay, v => v.menuTrayProfile.Header, text => $"Профиль: {text}").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TunTrayActionText, v => v.menuTun.Header).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.EnableTun, v => v.menuTun.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ToggleTunModeCmd, v => v.btnToggleTunMode).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ToggleSystemProxyModeCmd, v => v.btnToggleSystemProxyMode).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ToggleTunCmd, v => v.menuTun).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.QuickKillSwitch, v => v.btnQuickKillSwitch.IsChecked).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.QuickAllowLocalNetwork, v => v.btnQuickLocalNetwork.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ToggleQuickKillSwitchCmd, v => v.btnQuickKillSwitch).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ToggleQuickLocalNetworkCmd, v => v.btnQuickLocalNetwork).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenSplitTunnelCmd, v => v.btnQuickSplitTunnel).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenRoutingModeCmd, v => v.btnQuickRouting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenDpiModeCmd, v => v.btnQuickDpi).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ShowWindowCmd, v => v.menuShow).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard2).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.RunningServerToolTipText, v => v.tbNotify.ToolTipText).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NotifyLeftClickCmd, v => v.tbNotify.LeftClickCommand).DisposeWith(disposables);

            ViewModel.WhenAnyValue(vm => vm.TunUiState)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(async state =>
                {
                    UpdateTunSessionState(state);
                    await RefreshTrayIconAsync(state);
                })
                .DisposeWith(disposables);
        });
    }

    private async void StatusBarView_Loaded(object sender, RoutedEventArgs e)
    {
        var state = ViewModel?.TunUiState ?? ETunUiState.Off;
        UpdateTunSessionState(state);
        _sessionTimer.Start();
        await RefreshTrayIconAsync(state);
    }

    private void StatusBarView_Unloaded(object sender, RoutedEventArgs e)
    {
        _sessionTimer.Stop();
    }

    private void TrafficResetSession_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetTrafficSession();
    }

    private void QuickDns_Click(object sender, RoutedEventArgs e)
    {
        var window = new SgDnsRouteWindow
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
        ViewModel?.RefreshQuickDnsState();
    }

    private void TrafficProfiles_Click(object sender, RoutedEventArgs e)
    {
        var window = new SgTrafficProfilesWindow
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
    }

    private void TrafficResetAll_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var result = MessageBox.Show(
            owner,
            $"Сбросить статистику профиля «{ViewModel?.TrafficProfileNameDisplay}»?\n\n"
                + "Будут обнулены текущий сеанс, сегодня, этот месяц и всего. "
                + "Статистика других профилей сохранится.",
            "Сброс статистики профиля",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            ViewModel?.ResetAllTrafficStatistics();
        }
    }

    private void UpdateTunSessionState(ETunUiState state)
    {
        if (state == ETunUiState.On)
        {
            if (_lastTunState != ETunUiState.On || _tunConnectedAt is null)
            {
                _tunConnectedAt = DateTime.Now;
                FlashConnectedState();
            }
        }
        else
        {
            _tunConnectedAt = null;
        }

        _lastTunState = state;
        UpdateStateIconAnimation(state);
        RefreshTunSessionDisplay();
    }

    private void UpdateStateIconAnimation(ETunUiState state)
    {
        if (stateIcon.RenderTransform is not RotateTransform rotate)
        {
            return;
        }

        if (state is ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching)
        {
            var spin = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            return;
        }

        rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        rotate.Angle = 0;
    }

    private void RefreshTunSessionDisplay()
    {
        if (ViewModel?.TunUiState != ETunUiState.On || _tunConnectedAt is null)
        {
            txtTunSessionDisplay.Visibility = Visibility.Collapsed;
            txtTunSessionDisplay.Text = string.Empty;
            return;
        }

        var elapsed = DateTime.Now - _tunConnectedAt.Value;
        var duration = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        var core = ViewModel.CoreDisplay.IsNullOrEmpty() || ViewModel.CoreDisplay == "—"
            ? ViewModel.ProfileProtocolDisplay
            : ViewModel.CoreDisplay;

        txtTunSessionDisplay.Text = core.IsNullOrEmpty()
            ? $"Подключено {duration}"
            : $"Подключено {duration}  •  {core}";
        txtTunSessionDisplay.Visibility = Visibility.Visible;
    }

    private void FlashConnectedState()
    {
        var animation = new DoubleAnimation
        {
            From = 0.85,
            To = 0.28,
            Duration = TimeSpan.FromMilliseconds(900),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        stateHalo.BeginAnimation(OpacityProperty, animation);
    }

    private async Task RefreshTrayIconAsync(ETunUiState state)
    {
        var connectionMode = ViewModel?.ConnectionModeKey;
        tbNotify.Icon = await WindowsManager.Instance.GetNotifyIcon(
            _config,
            state,
            connectionMode);

        if (Application.Current?.MainWindow is Window window)
        {
            window.Icon = WindowsManager.Instance.GetAppIcon(
                state,
                connectionMode);
        }
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherRefreshIcon:
                await RefreshTrayIconAsync(ViewModel?.TunUiState ?? ETunUiState.Off);
                break;

            case EViewAction.SetClipboardData:
                if (obj is null)
                {
                    return false;
                }
                WindowsUtils.SetClipboardData((string)obj);
                break;

            case EViewAction.SgSplitTunnelWindow:
                return new SgSplitTunnelWindow { Owner = Application.Current.MainWindow }.ShowDialog() ?? false;

            case EViewAction.SgReserveProfileWindow:
                return new SgReserveProfileWindow { Owner = Application.Current.MainWindow }.ShowDialog() ?? false;

            case EViewAction.SgRoutingWindow:
                return new SgRoutingWindow { Owner = Application.Current.MainWindow }.ShowDialog() ?? false;

            case EViewAction.SgDpiWindow:
                return new SgDpiWindow { Owner = Application.Current.MainWindow }.ShowDialog() ?? false;

            case EViewAction.SgHelpWindow:
                new SgHelpWindow { Owner = Application.Current.MainWindow }.ShowDialog();
                return true;
        }

        return true;
    }

    private async void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        menuExit.IsEnabled = false;
        menuTun.IsEnabled = false;
        if (ViewModel != null)
        {
            await ViewModel.DisableTunAsync();
            if (ViewModel.TunUiState == ETunUiState.Error)
            {
                menuExit.IsEnabled = true;
                menuTun.IsEnabled = true;
                AppEvents.ShowHideWindowRequested.Publish(true);
                AppEvents.ShowLogsRequested.Publish();
                return;
            }
        }
        tbNotify.Dispose();
        await AppManager.Instance.AppExitAsync(true);
    }

    private void MenuProfiles_Click(object sender, RoutedEventArgs e)
    {
        AppEvents.ShowHideWindowRequested.Publish(true);
    }

    private void MenuLogs_Click(object sender, RoutedEventArgs e)
    {
        AppEvents.ShowHideWindowRequested.Publish(true);
        AppEvents.ShowLogsRequested.Publish();
    }

    private void RunningInfo_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && ViewModel?.TunUiState == ETunUiState.On)
        {
            ViewModel.TestServerAvailability();
        }
    }
}
