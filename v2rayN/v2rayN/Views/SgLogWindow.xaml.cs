using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;

namespace v2rayN.Views;

public partial class SgLogWindow : Window
{
    private readonly MsgView _logView;

    public SgLogWindow(string? initialText = null)
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);

        _logView = new MsgView();
        _logView.SetInitialText(initialText);
        logHost.Content = _logView;

        btnSave.Click += Save_Click;
        btnOpenFolder.Click += OpenFolder_Click;
        btnWindowMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        btnWindowMaximize.Click += (_, _) => ToggleMaximize();
        btnWindowClose.Click += (_, _) => Close();
        StateChanged += (_, _) => UpdateMaximizeIcon();
        Loaded += (_, _) => RestoreWindowSize();
        Closed += (_, _) => SaveWindowSize();
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    public void ActivateAndBringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeIcon()
    {
        icoWindowMaximize.Kind = WindowState == WindowState.Maximized
            ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore
            : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
        btnWindowMaximize.ToolTip = WindowState == WindowState.Maximized
            ? "Восстановить размер"
            : "Развернуть";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить журнал SG Client",
            Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"SG-Client-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _logView.GetText());
            txtWindowHint.Text = $"Журнал сохранён: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Save SG Client log", ex);
            txtWindowHint.Text = $"Не удалось сохранить журнал: {ex.Message}";
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Utils.GetLogPath()}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Open SG Client log folder", ex);
            txtWindowHint.Text = $"Не удалось открыть папку журналов: {ex.Message}";
        }
    }


    private void RestoreWindowSize()
    {
        try
        {
            var size = ConfigHandler.GetWindowSizeItem(
                AppManager.Instance.Config,
                GetType().Name);
            if (size == null)
            {
                return;
            }

            var maxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width);
            var maxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height);
            Width = Math.Clamp(size.Width, MinWidth, maxWidth);
            Height = Math.Clamp(size.Height, MinHeight, maxHeight);
            Left = SystemParameters.WorkArea.Left
                + ((SystemParameters.WorkArea.Width - Width) / 2);
            Top = SystemParameters.WorkArea.Top
                + ((SystemParameters.WorkArea.Height - Height) / 2);
        }
        catch
        {
            // The XAML size remains the safe fallback.
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            ConfigHandler.SaveWindowSizeItem(
                AppManager.Instance.Config,
                GetType().Name,
                bounds.Width,
                bounds.Height);
        }
        catch
        {
            // Window sizing must never affect the client shutdown path.
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
