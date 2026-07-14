using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ServiceLib.Manager;

namespace v2rayN.Views;

public partial class SgTrafficProfilesWindow : Window
{
    public sealed class TrafficProfileRow
    {
        public string ProfileName { get; init; } = string.Empty;
        public string MonthDownloadDisplay { get; init; } = string.Empty;
        public string MonthUploadDisplay { get; init; } = string.Empty;
        public string MonthTotalDisplay { get; init; } = string.Empty;
        public string MonthShareDisplay { get; init; } = string.Empty;
        public string TodayDisplay { get; init; } = string.Empty;
        public string LastUsedDisplay { get; init; } = string.Empty;
    }

    public ObservableCollection<TrafficProfileRow> Rows { get; } = new();

    public SgTrafficProfilesWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        DataContext = this;
        Loaded += (_, _) => RefreshRows();
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
    }

    private void RefreshRows()
    {
        Rows.Clear();

        var profiles = SgTrafficStatisticsManager.Instance.GetProfiles()
            .Where(item => item.MonthDownloadBytes > 0 || item.MonthUploadBytes > 0)
            .ToList();
        var monthDownload = profiles.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.MonthDownloadBytes));
        var monthUpload = profiles.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.MonthUploadBytes));
        var monthTotal = SaturatingAdd(monthDownload, monthUpload);

        foreach (var profile in profiles)
        {
            var profileTotal = SaturatingAdd(
                profile.MonthDownloadBytes,
                profile.MonthUploadBytes);

            Rows.Add(new TrafficProfileRow
            {
                ProfileName = profile.ProfileName,
                MonthDownloadDisplay = FormatBytes(profile.MonthDownloadBytes),
                MonthUploadDisplay = FormatBytes(profile.MonthUploadBytes),
                MonthTotalDisplay = FormatBytes(profileTotal),
                MonthShareDisplay = monthTotal <= 0
                    ? "—"
                    : $"{profileTotal * 100d / monthTotal:0.#}%",
                TodayDisplay = FormatPair(
                    profile.TodayDownloadBytes,
                    profile.TodayUploadBytes),
                LastUsedDisplay = profile.LastUsedUtc == default
                    ? "—"
                    : profile.LastUsedUtc.ToLocalTime().ToString(
                        "dd.MM.yyyy HH:mm",
                        CultureInfo.CurrentCulture),
            });
        }

        emptyState.Visibility = Rows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        dgProfiles.Visibility = Rows.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        var ruCulture = CultureInfo.GetCultureInfo("ru-RU");
        var monthName = DateTime.Now.ToString("MMMM yyyy", ruCulture);
        txtMonthPeriod.Text = char.ToUpper(monthName[0], ruCulture) + monthName[1..];
        txtMonthDownload.Text = FormatBytes(monthDownload);
        txtMonthUpload.Text = FormatBytes(monthUpload);
        txtMonthTotal.Text = FormatBytes(monthTotal);
        txtSummary.Text = Rows.Count == 0
            ? "Трафик за текущий месяц пока не зафиксирован"
            : $"Профилей за месяц: {Rows.Count} · распределение отсортировано по объёму";
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (left < 0)
        {
            left = 0;
        }
        if (right < 0)
        {
            right = 0;
        }

        return left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
    }

    private static string FormatPair(long downloadBytes, long uploadBytes)
    {
        return $"↓ {FormatBytes(downloadBytes)}   ↑ {FormatBytes(uploadBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{display:0} {units[unit]}"
            : $"{display:0.#} {units[unit]}";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshRows();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
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
