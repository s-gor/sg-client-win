namespace v2rayN.Views;

public partial class SgReserveProfileWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private readonly List<ProfileChoice> _choices = [];
    private bool _hasApplied;

    public SgReserveProfileWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        chkAutoFailover.IsChecked = _config.SgQuickSettingsItem.AutoFailoverEnabled;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var currentId = AmneziaWgManager.Instance.SelectedProfileId.IsNotEmpty()
            ? AmneziaWgManager.Instance.SelectedProfileId
            : _config.IndexId;

        var coreProfiles = await AppManager.Instance.ProfileModels(_config.SubIndexId, string.Empty) ?? [];
        foreach (var profile in coreProfiles.Where(item => !string.Equals(item.IndexId, currentId, StringComparison.OrdinalIgnoreCase)))
        {
            _choices.Add(new ProfileChoice(profile.IndexId, $"{profile.Remarks} · {GetProtocol(profile)}"));
        }
        foreach (var profile in AmneziaWgManager.Instance.GetProfiles().Where(item => !string.Equals(item.Id, currentId, StringComparison.OrdinalIgnoreCase)))
        {
            _choices.Add(new ProfileChoice(profile.Id, $"{profile.Name} · AmneziaWG"));
        }

        cmbProfiles.ItemsSource = _choices;
        cmbProfiles.SelectedItem = _choices.FirstOrDefault(item => string.Equals(item.Id, _config.SgQuickSettingsItem.ReserveProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (cmbProfiles.SelectedItem is not ProfileChoice selected)
        {
            if (chkAutoFailover.IsChecked == true)
            {
                MessageBox.Show(this, "Выберите резервный профиль.", "Резервный профиль", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _config.SgQuickSettingsItem.ReserveProfileId = string.Empty;
            _config.SgQuickSettingsItem.ReserveProfileName = string.Empty;
        }
        else
        {
            _config.SgQuickSettingsItem.ReserveProfileId = selected.Id;
            _config.SgQuickSettingsItem.ReserveProfileName = selected.DisplayName.Split(" · ")[0];
        }

        _config.SgQuickSettingsItem.AutoFailoverEnabled = chkAutoFailover.IsChecked == true
            && _config.SgQuickSettingsItem.ReserveProfileId.IsNotEmpty();
        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            MessageBox.Show(this, "Не удалось сохранить настройку.", "Резервный профиль", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _hasApplied = true;
        txtFooterStatus.Text = "Применено. Окно можно закрыть или продолжить настройку.";
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource("SgAccentBrush");
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        _config.SgQuickSettingsItem.ReserveProfileId = string.Empty;
        _config.SgQuickSettingsItem.ReserveProfileName = string.Empty;
        _config.SgQuickSettingsItem.AutoFailoverEnabled = false;
        await ConfigHandler.SaveConfig(_config);
        _hasApplied = true;
        chkAutoFailover.IsChecked = false;
        cmbProfiles.SelectedItem = null;
        txtFooterStatus.Text = "Сброшено. Окно можно закрыть или выбрать новый резервный профиль.";
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource("SgAccentBrush");
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("reserve") { Owner = this }.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _hasApplied;
        Close();
    }

    private static string GetProtocol(ProfileItemModel profile)
    {
        if (profile.ConfigType == EConfigType.Hysteria2)
        {
            return "Hysteria2";
        }
        if (profile.ConfigType == EConfigType.VLESS && profile.Network == ETransport.xhttp.ToString())
        {
            return profile.StreamSecurity == Global.StreamSecurityReality ? "VLESS XHTTP · REALITY" : "VLESS XHTTP · TLS";
        }
        if (profile.ConfigType == EConfigType.VLESS && profile.StreamSecurity == Global.StreamSecurityReality)
        {
            return "VLESS · REALITY";
        }
        return profile.ConfigType.ToString();
    }

    private sealed record ProfileChoice(string Id, string DisplayName);
}
