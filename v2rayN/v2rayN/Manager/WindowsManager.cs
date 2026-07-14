using System.Drawing;
using System.Windows.Media.Imaging;

namespace v2rayN.Manager;

public sealed class WindowsManager
{
    private static readonly Lazy<WindowsManager> instance = new(() => new());
    public static WindowsManager Instance => instance.Value;
    private static readonly string _tag = "WindowsHandler";

    public Task<Icon> GetNotifyIcon(Config config)
    {
        var state = config.TunModeItem.EnableTun ? ETunUiState.On : ETunUiState.Off;
        return GetNotifyIcon(config, state);
    }

    public async Task<Icon> GetNotifyIcon(
        Config config,
        ETunUiState state,
        string? connectionMode = null)
    {
        await Task.CompletedTask;

        try
        {
            var systemProxyRunning =
                string.Equals(
                    connectionMode,
                    "system-proxy",
                    StringComparison.OrdinalIgnoreCase)
                && state == ETunUiState.On;
            var localProxyRunning =
                string.Equals(
                    connectionMode,
                    "local-proxy",
                    StringComparison.OrdinalIgnoreCase)
                && state == ETunUiState.On;

            var index = systemProxyRunning
                ? 2
                : localProxyRunning
                    ? 4
                    : state switch
                    {
                        ETunUiState.On => 1,
                        ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching => 2,
                        ETunUiState.Error => 3,
                        _ => 0
                    };
            var fileName = Utils.GetPath($"NotifyIcon{index + 1}.ico");
            if (File.Exists(fileName))
            {
                return new Icon(fileName);
            }

            return index switch
            {
                1 => Properties.Resources.NotifyIcon2,
                2 => Properties.Resources.NotifyIcon3,
                3 => Properties.Resources.NotifyIcon4,
                4 => Properties.Resources.NotifyIcon5,
                _ => Properties.Resources.NotifyIcon1
            };
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return Properties.Resources.NotifyIcon1;
        }
    }

    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        return GetAppIcon(config.TunModeItem.EnableTun ? ETunUiState.On : ETunUiState.Off);
    }

    public System.Windows.Media.ImageSource GetAppIcon(
        ETunUiState state,
        string? connectionMode = null)
    {
        var systemProxyRunning =
            string.Equals(
                connectionMode,
                "system-proxy",
                StringComparison.OrdinalIgnoreCase)
            && state == ETunUiState.On;
        var localProxyRunning =
            string.Equals(
                connectionMode,
                "local-proxy",
                StringComparison.OrdinalIgnoreCase)
            && state == ETunUiState.On;

        var index = systemProxyRunning
            ? 3
            : localProxyRunning
                ? 5
                : state switch
                {
                    ETunUiState.On => 2,
                    ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching => 3,
                    ETunUiState.Error => 4,
                    _ => 1
                };

        return BitmapFrame.Create(
            new Uri(
                $"pack://application:,,,/Resources/NotifyIcon{index}.ico",
                UriKind.RelativeOrAbsolute));
    }

    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
