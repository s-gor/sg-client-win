namespace v2rayN.Views;

public partial class OptionSettingWindow
{
    private sealed record SgOption<T>(T Id, string Name);

    private static Config _config;
    private bool _themeReady;
    private bool _hasApplied;

    public OptionSettingWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);

        Owner = Application.Current.MainWindow;
        _config = AppManager.Instance.Config;
        var stack = _config.TunModeItem.Stack?.Trim().ToLowerInvariant();
        if (stack is not "mixed" and not "system" and not "gvisor" and not "auto")
        {
            stack = "auto";
        }
        var mtu = _config.TunModeItem.Mtu;
        if (mtu != 0 && (mtu < 1280 || mtu > 9000))
        {
            mtu = 0;
        }
        _config.TunModeItem.Stack = stack;
        _config.TunModeItem.Mtu = mtu;
        ViewModel = new OptionSettingViewModel(UpdateViewHandler);

        cmbLogLevel.ItemsSource = Global.LogLevels;
        cmbStack.ItemsSource = new SgOption<string>[]
        {
            new("auto", "Автоматически — mixed"),
            new("mixed", "mixed — рекомендуется"),
            new("system", "system — системный"),
            new("gvisor", "gVisor — совместимость")
        };
        cmbMtu.ItemsSource = new SgOption<int>[]
        {
            new(0, "Автоматически — рекомендуется"),
            new(1280, "1280 — максимальная совместимость"),
            new(1500, "1500 — стандартный"),
            new(9000, "9000 — виртуальный TUN")
        };
        ViewModel.TunStack = stack;
        ViewModel.TunMtu = mtu;
        cmbStack.SelectedValue = stack;
        if (mtu is 0 or 1280 or 1500 or 9000)
        {
            cmbMtu.SelectedValue = mtu;
        }
        else
        {
            cmbMtu.Text = $"{mtu} — вручную";
        }
        cmbStack.SelectionChanged += (_, _) =>
        {
            if (cmbStack.SelectedValue is string value)
            {
                ViewModel.TunStack = value;
            }
        };
        cmbMtu.SelectionChanged += (_, _) =>
        {
            if (cmbMtu.SelectedValue is int value)
            {
                ViewModel.TunMtu = value;
            }
        };
        cmbTheme.ItemsSource = SgThemeManager.Options;
        cmbTheme.SelectedValue = SgThemeManager.Current;
        cmbTheme.SelectionChanged += CmbTheme_SelectionChanged;
        _themeReady = true;

        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        togAutoRecoverTun.IsChecked = _config.SgQuickSettingsItem.AutoRecoverTun;
        togAutoRecoverTun.Checked += AutoRecoverTun_Changed;
        togAutoRecoverTun.Unchecked += AutoRecoverTun_Changed;
        btnReserveProfileSetting.Click += ReserveProfileSetting_Click;
        RefreshReserveProfileSetting();

        btnHelp.Click += (_, _) => new SgHelpWindow("start") { Owner = this }.ShowDialog();
        btnClose.Click += (_, _) => CloseWindow();
        btnCancel.Click += (_, _) => CloseWindow();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.AutoRun, v => v.togAutoRun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoHideStartup, v => v.togAutoHideStartup.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableStatistics, v => v.togEnableStatistics.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DisplayRealTimeSpeed, v => v.togDisplayRealTimeSpeed.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableHWA, v => v.togEnableHWA.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.TunAutoRoute, v => v.togAutoRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunStrictRoute, v => v.togStrictRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunEnableIPv6Address, v => v.togEnableIPv6Address.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunRouteExcludeAddress, v => v.txtRouteExcludeAddress.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.UdpEnabled, v => v.togUdpEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SniffingEnabled, v => v.togSniffingEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.LocalPort, v => v.txtLocalPort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.LogEnabled, v => v.togLogEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Loglevel, v => v.cmbLogLevel.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });

        WindowsUtils.SetDarkBorder(this, SgThemeManager.Current == SgThemeManager.Light ? nameof(ETheme.Light) : nameof(ETheme.Dark));
    }

    private async void AutoRecoverTun_Changed(object sender, RoutedEventArgs e)
    {
        _config.SgQuickSettingsItem.AutoRecoverTun = togAutoRecoverTun.IsChecked == true;
        await ConfigHandler.SaveConfig(_config);
    }

    private void ReserveProfileSetting_Click(object sender, RoutedEventArgs e)
    {
        var changed = new SgReserveProfileWindow { Owner = this }.ShowDialog() ?? false;
        if (changed)
        {
            RefreshReserveProfileSetting();
        }
    }

    private void RefreshReserveProfileSetting()
    {
        txtReserveProfileSetting.Text = _config.SgQuickSettingsItem.AutoFailoverEnabled
            && _config.SgQuickSettingsItem.ReserveProfileName.IsNotEmpty()
                ? $"Автоматически: {_config.SgQuickSettingsItem.ReserveProfileName}"
                : "Не настроен";
    }

    private void CloseWindow()
    {
        DialogResult = _hasApplied;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SetFooterStatus(string message, string resourceKey = "SgMutedBrush")
    {
        txtFooterStatus.Text = message;
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource(resourceKey);
    }

    private void StackHelp_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("stack") { Owner = this }.ShowDialog();
    }

    private void Mtu_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (cmbMtu.SelectedValue is int selectedPreset)
        {
            ViewModel.TunMtu = selectedPreset;
            return;
        }

        var raw = cmbMtu.Text?.Trim() ?? string.Empty;
        var firstToken = raw.Split(new[] { ' ', '—', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        if (!int.TryParse(firstToken, out var value) || (value != 0 && (value < 1280 || value > 9000)))
        {
            NoticeManager.Instance.Enqueue("MTU должен быть 0 (автоматически) или числом от 1280 до 9000.");
            var current = ViewModel.TunMtu;
            cmbMtu.Text = current == 0 ? "Автоматически — рекомендуется" : $"{current} — вручную";
            return;
        }

        ViewModel.TunMtu = value;
        if (value is 0 or 1280 or 1500 or 9000)
        {
            cmbMtu.SelectedValue = value;
        }
        else
        {
            cmbMtu.SelectedIndex = -1;
            cmbMtu.Text = $"{value} — вручную";
        }
    }

    private void MtuHelp_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("mtu") { Owner = this }.ShowDialog();
    }

    private async void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_themeReady || cmbTheme.SelectedValue is not string theme)
        {
            return;
        }

        await SgThemeManager.ApplyAndSaveAsync(theme);
    }

    private Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.CloseWindow:
                _hasApplied = true;
                SetFooterStatus("Применено. Окно можно закрыть или продолжить настройку.", "SgAccentBrush");
                break;

            case EViewAction.InitSettingFont:
                break;
        }

        return Task.FromResult(true);
    }
}
