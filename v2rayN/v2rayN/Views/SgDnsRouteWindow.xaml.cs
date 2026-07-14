using System.Windows.Controls;
using System.Windows.Input;

namespace v2rayN.Views;

public partial class SgDnsRouteWindow : Window
{
    private readonly Config _config;

    public SgDnsRouteWindow()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();

        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        btnClose.Click += (_, _) => Close();
        btnCancel.Click += (_, _) => Close();
        btnApply.Click += Apply_Click;
        rbDnsViaVpn.Checked += (_, _) => RefreshWarning();
        rbDnsDirect.Checked += (_, _) => RefreshWarning();

        var throughVpn = _config.SgQuickSettingsItem.DnsThroughTun;
        rbDnsViaVpn.IsChecked = throughVpn;
        rbDnsDirect.IsChecked = !throughVpn;
        txtCurrent.Text = throughVpn
            ? "Текущее значение: DNS через VPN"
            : "Текущее значение: DNS напрямую";
        RefreshWarning();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        btnApply.IsEnabled = false;
        btnCancel.IsEnabled = false;
        try
        {
            var throughVpn = rbDnsViaVpn.IsChecked == true;
            var changed = _config.SgQuickSettingsItem.DnsThroughTun != throughVpn;
            _config.SgQuickSettingsItem.DnsThroughTun = throughVpn;

            if (await ConfigHandler.SaveConfig(_config) != 0)
            {
                throw new InvalidOperationException("Не удалось сохранить маршрут DNS.");
            }

            if (changed && _config.TunModeItem?.EnableTun == true)
            {
                AppEvents.ReloadRequested.Publish();
            }

            NoticeManager.Instance.Enqueue(throughVpn
                ? "DNS направлен через VPN."
                : "DNS направлен напрямую. Возможна утечка DNS-запросов.");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgDnsRouteWindow.Apply", ex);
            txtCurrent.Text = ex.Message;
            txtCurrent.SetResourceReference(TextBlock.ForegroundProperty, "SgErrorBrush");
        }
        finally
        {
            btnApply.IsEnabled = true;
            btnCancel.IsEnabled = true;
        }
    }

    private void RefreshWarning()
    {
        directWarning.Visibility = rbDnsDirect.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            // Mouse release can race with DragMove.
        }
    }
}
