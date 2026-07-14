using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Controls;

namespace v2rayN.Views;

public partial class SgSplitTunnelWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private readonly ObservableCollection<string> _applications = [];
    private readonly ObservableCollection<string> _addresses = [];
    private bool _hasApplied;

    public SgSplitTunnelWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        _config.SgQuickSettingsItem.SplitTunnelApplications ??= [];
        _config.SgQuickSettingsItem.SplitTunnelAddresses ??= [];

        foreach (var item in _config.SgQuickSettingsItem.SplitTunnelApplications.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _applications.Add(item);
        }
        foreach (var item in _config.SgQuickSettingsItem.SplitTunnelAddresses.Where(value => value.IsNotEmpty()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _addresses.Add(item);
        }
        lstApps.ItemsSource = _applications;
        lstAddresses.ItemsSource = _addresses;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите приложение",
            Filter = "Приложения Windows (*.exe)|*.exe",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        foreach (var file in dialog.FileNames)
        {
            if (!_applications.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                _applications.Add(file);
            }
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (lstApps.SelectedItem is string item)
        {
            _applications.Remove(item);
        }
    }

    private void AddAddress_Click(object sender, RoutedEventArgs e) => AddAddress();

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddAddress();
            e.Handled = true;
        }
    }

    private void AddAddress()
    {
        var value = txtAddress.Text.Trim();
        if (!TryNormalizeNetwork(value, out var normalized, out var error))
        {
            MessageBox.Show(this, error, "Раздельный TUN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_addresses.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _addresses.Add(normalized);
        }
        txtAddress.Clear();
        txtAddress.Focus();
    }

    private void RemoveAddress_Click(object sender, RoutedEventArgs e)
    {
        if (lstAddresses.SelectedItem is string item)
        {
            _addresses.Remove(item);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        foreach (var address in _addresses)
        {
            if (!TryNormalizeNetwork(address, out _, out var error))
            {
                MessageBox.Show(this, error, "Раздельный TUN", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _config.SgQuickSettingsItem.SplitTunnelApplications = _applications.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _config.SgQuickSettingsItem.SplitTunnelAddresses = _addresses.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            MessageBox.Show(this, "Не удалось сохранить настройки.", "Раздельный TUN", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _hasApplied = true;
        txtFooterStatus.Text = "Применено. Окно можно закрыть или продолжить редактирование.";
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource("SgAccentBrush");
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("split") { Owner = this }.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _hasApplied;
        Close();
    }

    private static bool TryNormalizeNetwork(string value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (value.IsNullOrEmpty())
        {
            error = "Введите IP-адрес или подсеть.";
            return false;
        }

        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            error = $"Некорректный адрес: {value}";
            return false;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxPrefix;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maxPrefix))
        {
            error = $"Некорректная длина префикса: {value}";
            return false;
        }
        if (prefix == 0)
        {
            error = "Нельзя исключить из TUN весь интернет. Укажите более узкий адрес или подсеть.";
            return false;
        }

        normalized = $"{address}/{prefix}";
        return true;
    }
}
