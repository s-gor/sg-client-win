using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using v2rayN.Services;

namespace v2rayN.Views;

public partial class SgGeoFilesPanel : UserControl
{
    private readonly SgGeoFilesService _service = new();
    private SgGeoCandidate? _candidate;

    public SgGeoFilesPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            UpdateSourcePanels();
            UpdateRoscomOptionState();
            await LoadCurrentAsync();
        };
        Unloaded += (_, _) => CleanupCandidate();
    }

    public Task RefreshAsync() => LoadCurrentAsync();

    private async Task LoadCurrentAsync()
    {
        try
        {
            SetStatus("Чтение установленных GeoFiles…", "SgMutedBrush");
            var info = await _service.GetInstalledInfoAsync();

            txtCurrentSource.Text = $"Источник: {info.Source}";
            txtGeoIpInfo.Text = FormatInfo(
                info.GeoIpSize,
                info.GeoIpDate,
                info.GeoIpSha256);
            txtGeoSiteInfo.Text = FormatInfo(
                info.GeoSiteSize,
                info.GeoSiteDate,
                info.GeoSiteSha256);
            txtCurrentValidation.Text =
                $"Последняя проверка: {info.LastValidation}";
            txtCurrentFamily.Text =
                $"Семейство категорий: {info.Family}";
            txtCurrentCategories.Text = FormatCategorySummary(
                info.GeoIpCategories,
                info.GeoSiteCategories);
            txtCurrentCategoriesFull.Text = FormatFullCategories(
                info.GeoIpCategories,
                info.GeoSiteCategories);

            SetStatus(
                $"GeoFiles установлены и активны. Источник: {info.Source}.",
                "SgSuccessBrush");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgGeoFilesPanel.LoadCurrent", ex);
            SetStatus(ex.Message, "SgErrorBrush");
        }
    }

    private static string FormatInfo(
        long size,
        DateTime date,
        string sha)
    {
        return $"Размер: {FormatBytes(size)} · дата: {date:dd.MM.yyyy HH:mm}\n"
            + $"SHA-256: {sha}";
    }

    private static string FormatBytes(long value)
    {
        var units = new[] { "Б", "КБ", "МБ", "ГБ" };
        double size = value;
        var index = 0;

        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.##} {units[index]}";
    }

    private static string FormatCategorySummary(
        IReadOnlyList<string> geoIp,
        IReadOnlyList<string> geoSite)
    {
        return $"GeoIP ({geoIp.Count}): {FormatCategoryList(geoIp)}\n"
            + $"GeoSite ({geoSite.Count}): {FormatCategoryList(geoSite)}";
    }

    private static string FormatFullCategories(
        IReadOnlyList<string> geoIp,
        IReadOnlyList<string> geoSite)
    {
        return $"GeoIP ({geoIp.Count})\n{string.Join(", ", geoIp)}\n\n"
            + $"GeoSite ({geoSite.Count})\n{string.Join(", ", geoSite)}";
    }

    private static string FormatCategoryList(
        IReadOnlyList<string> categories,
        int limit = 14)
    {
        if (categories.Count == 0)
        {
            return "категории не найдены";
        }

        var visible = categories.Take(limit).ToList();
        var result = string.Join(", ", visible);
        if (categories.Count > visible.Count)
        {
            result += $" · ещё {categories.Count - visible.Count}";
        }

        return result;
    }

    private void SourceChanged(object sender, RoutedEventArgs e)
    {
        UpdateSourcePanels();

        if (IsLoaded)
        {
            InvalidateCandidate(
                "Источник изменён. Выполните проверку заново.");
        }
    }

    private void UpdateSourcePanels()
    {
        if (customUrlPanel == null
            || localFilePanel == null
            || roscomInfoPanel == null
            || rbCustom == null
            || rbLocal == null
            || rbRoscom == null)
        {
            return;
        }

        customUrlPanel.Visibility = rbCustom.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        localFilePanel.Visibility = rbLocal.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        roscomInfoPanel.Visibility = rbRoscom.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ChooseGeoIp_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = ChooseDatFile(
            "Выберите geoip.dat",
            "geoip.dat");
        if (path.IsNotEmpty())
        {
            txtGeoIpFile.Text = path;
        }
    }

    private void ChooseGeoSite_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = ChooseDatFile(
            "Выберите geosite.dat",
            "geosite.dat");
        if (path.IsNotEmpty())
        {
            txtGeoSiteFile.Text = path;
        }
    }

    private static string? ChooseDatFile(
        string title,
        string fileName)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "GeoFiles (*.dat)|*.dat|Все файлы (*.*)|*.*",
            FileName = fileName,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private async void Prepare_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetControls(false);
        CleanupCandidate();

        try
        {
            var request = BuildRequest();
            _candidate = await _service.PrepareAsync(
                request,
                text => SetStatus(
                    text,
                    "SgWarningBrush"));

            var compatibility = _candidate.MissingActiveCategories.Count == 0
                ? "Текущая маршрутизация совместима."
                : "Не найдены категории текущей маршрутизации: "
                  + string.Join(
                      ", ",
                      _candidate.MissingActiveCategories);

            txtCandidateInfo.Text =
                $"Источник: {_candidate.SourceDescription}\n"
                + $"Семейство: {_candidate.Family}\n"
                + $"geoip.dat: {FormatBytes(_candidate.GeoIpSize)} · {_candidate.GeoIpDate:dd.MM.yyyy HH:mm}\n"
                + $"SHA-256: {_candidate.GeoIpSha256}\n"
                + $"geosite.dat: {FormatBytes(_candidate.GeoSiteSize)} · {_candidate.GeoSiteDate:dd.MM.yyyy HH:mm}\n"
                + $"SHA-256: {_candidate.GeoSiteSha256}\n"
                + FormatCategorySummary(
                    _candidate.GeoIpCategories,
                    _candidate.GeoSiteCategories)
                + $"\n{compatibility}\n"
                + "Проверка установленным Xray: успешно";
            txtCandidateCategoriesFull.Text = FormatFullCategories(
                _candidate.GeoIpCategories,
                _candidate.GeoSiteCategories);
            expCandidateCategories.Visibility = Visibility.Visible;

            btnApply.IsEnabled = true;

            if (_candidate.MissingActiveCategories.Count > 0)
            {
                SetStatus(
                    _candidate.IsRoscomVpn
                        ? "RoscomVPN проверен. Для безопасной установки будет предложен совместимый пресет маршрутизации."
                        : "Источник проверен, но часть активных Geo-категорий отсутствует. При установке будет предложено сохранить только совместимые правила.",
                    "SgWarningBrush");
            }
            else
            {
                SetStatus(
                    "Источник полностью проверен. Теперь можно обновить GeoFiles.",
                    "SgSuccessBrush");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(
                "SgGeoFilesPanel.Prepare",
                ex);
            SetStatus(
                $"Источник не прошёл проверку: {ex.Message}",
                "SgErrorBrush");
            txtCandidateInfo.Text =
                "Источник ещё не подготовлен.";
            expCandidateCategories.Visibility = Visibility.Collapsed;
        }
        finally
        {
            SetControls(true);
        }
    }

    private SgGeoSourceRequest BuildRequest()
    {
        if (rbLoyal.IsChecked == true)
        {
            return new SgGeoSourceRequest(
                SgGeoSourceKind.Loyalsoldier,
                null,
                null);
        }

        if (rbRunet.IsChecked == true)
        {
            return new SgGeoSourceRequest(
                SgGeoSourceKind.RunetFreedom,
                null,
                null);
        }

        if (rbRoscom.IsChecked == true)
        {
            return new SgGeoSourceRequest(
                SgGeoSourceKind.RoscomVpn,
                null,
                null);
        }

        if (rbCustom.IsChecked == true)
        {
            return new SgGeoSourceRequest(
                SgGeoSourceKind.CustomUrls,
                txtGeoIpUrl.Text.Trim(),
                txtGeoSiteUrl.Text.Trim());
        }

        if (rbLocal.IsChecked == true)
        {
            return new SgGeoSourceRequest(
                SgGeoSourceKind.LocalFiles,
                txtGeoIpFile.Text.Trim(),
                txtGeoSiteFile.Text.Trim());
        }

        return new SgGeoSourceRequest(
            SgGeoSourceKind.Bundled,
            null,
            null);
    }

    private void Apply_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_candidate == null)
        {
            SetStatus(
                "Сначала проверьте выбранный источник.",
                "SgErrorBrush");
            return;
        }

        roscomOptionsPanel.Visibility = _candidate.IsRoscomVpn
            ? Visibility.Visible
            : Visibility.Collapsed;
        genericCompatibilityOptionsPanel.Visibility =
            !_candidate.IsRoscomVpn
            && _candidate.MissingActiveCategories.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        chkApplyRoscomPreset.IsChecked =
            _candidate.IsRoscomVpn
            && _candidate.MissingActiveCategories.Count > 0;
        chkApplySafeGlobalPreset.IsChecked =
            !_candidate.IsRoscomVpn
            && _candidate.MissingActiveCategories.Count > 0;
        chkRoscomBlockAds.IsChecked = false;
        chkRoscomBlockWinSpy.IsChecked = false;
        chkRoscomBlockTorrent.IsChecked = false;
        UpdateRoscomOptionState();

        var compatibilityText =
            _candidate.MissingActiveCategories.Count > 0
                ? "\n\nТекущая маршрутизация ссылается на отсутствующие категории. Для продолжения выберите предложенный безопасный пресет."
                : string.Empty;

        txtConfirmBody.Text =
            $"Будут установлены GeoFiles из источника «{_candidate.SourceDescription}».\n\n"
            + "SG Client остановит активное подключение, создаст резервную копию, "
            + "заменит оба файла одновременно и проверит их установленным Xray. "
            + "При ошибке старые файлы и маршрутизация будут возвращены."
            + compatibilityText;

        confirmOverlay.Visibility = Visibility.Visible;
    }

    private async void RestoreBundled_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetControls(false);
        CleanupCandidate();

        try
        {
            _candidate = await _service.PrepareBundledAsync(
                text => SetStatus(
                    text,
                    "SgWarningBrush"));

            txtCandidateInfo.Text =
                $"Подготовлен исходный комплект SG Client.\n"
                + $"Семейство: {_candidate.Family}\n"
                + FormatCategorySummary(
                    _candidate.GeoIpCategories,
                    _candidate.GeoSiteCategories)
                + "\nПроверка Xray: успешно.";
            txtCandidateCategoriesFull.Text = FormatFullCategories(
                _candidate.GeoIpCategories,
                _candidate.GeoSiteCategories);
            expCandidateCategories.Visibility = Visibility.Visible;

            btnApply.IsEnabled = true;

            SetStatus(
                _candidate.MissingActiveCategories.Count == 0
                    ? "Комплектные GeoFiles подготовлены. Нажмите «Обновить GeoFiles»."
                    : "Комплектные GeoFiles подготовлены. При применении будет предложено удалить несовместимые Geo-категории.",
                _candidate.MissingActiveCategories.Count == 0
                    ? "SgSuccessBrush"
                    : "SgWarningBrush");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(
                "SgGeoFilesPanel.RestoreBundled",
                ex);
            SetStatus(
                ex.Message,
                "SgErrorBrush");
        }
        finally
        {
            SetControls(true);
        }
    }

    private void RoscomPreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateRoscomOptionState();
    }

    private void UpdateRoscomOptionState()
    {
        if (chkApplyRoscomPreset == null
            || chkRoscomBlockAds == null
            || chkRoscomBlockWinSpy == null
            || chkRoscomBlockTorrent == null)
        {
            return;
        }

        var enabled = chkApplyRoscomPreset.IsChecked == true;
        var categories = _candidate?.GeoSiteCategories
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        chkRoscomBlockAds.IsEnabled =
            enabled && categories.Contains("category-ads");
        chkRoscomBlockWinSpy.IsEnabled =
            enabled && categories.Contains("win-spy");
        chkRoscomBlockTorrent.IsEnabled =
            enabled && categories.Contains("torrent");
    }

    private void CancelConfirm_Click(
        object sender,
        RoutedEventArgs e)
    {
        confirmOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmApply_Click(
        object sender,
        RoutedEventArgs e)
    {
        confirmOverlay.Visibility = Visibility.Collapsed;
        var candidate = _candidate;
        if (candidate == null)
        {
            return;
        }

        var options = new SgGeoApplyOptions(
            candidate.IsRoscomVpn
                && chkApplyRoscomPreset.IsChecked == true,
            candidate.IsRoscomVpn
                && chkRoscomBlockAds.IsChecked == true,
            candidate.IsRoscomVpn
                && chkRoscomBlockWinSpy.IsChecked == true,
            candidate.IsRoscomVpn
                && chkRoscomBlockTorrent.IsChecked == true,
            !candidate.IsRoscomVpn
                && candidate.MissingActiveCategories.Count > 0
                && chkApplySafeGlobalPreset.IsChecked == true);

        SetControls(false);

        try
        {
            var result = await _service.ApplyAsync(
                candidate,
                options,
                text => SetStatus(
                    text,
                    "SgWarningBrush"));

            SetStatus(
                result.Message,
                result.Success
                    ? "SgSuccessBrush"
                    : "SgErrorBrush");

            if (result.Success)
            {
                NoticeManager.Instance.Enqueue(
                    "GeoFiles успешно обновлены.");
                await LoadCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(
                "SgGeoFilesPanel.Apply",
                ex);
            SetStatus(
                ex.Message,
                "SgErrorBrush");
        }
        finally
        {
            CleanupCandidate();
            SetControls(true);
        }
    }

    private void OpenBackups_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = Utils.GetBackupPath("geofiles");
        Directory.CreateDirectory(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void SetControls(bool enabled)
    {
        btnPrepare.IsEnabled = enabled;
        rbBundled.IsEnabled = enabled;
        rbLoyal.IsEnabled = enabled;
        rbRunet.IsEnabled = enabled;
        rbRoscom.IsEnabled = enabled;
        rbCustom.IsEnabled = enabled;
        rbLocal.IsEnabled = enabled;

        btnApply.IsEnabled =
            enabled
            && _candidate != null;
    }

    private void InvalidateCandidate(string status)
    {
        CleanupCandidate();
        txtCandidateInfo.Text =
            "Источник ещё не проверен.";
        expCandidateCategories.Visibility = Visibility.Collapsed;
        SetStatus(
            status,
            "SgMutedBrush");
    }

    private void CleanupCandidate()
    {
        _candidate?.Dispose();
        _candidate = null;

        if (btnApply != null)
        {
            btnApply.IsEnabled = false;
        }
    }

    private void SetStatus(
        string text,
        string brushKey)
    {
        txtGeoStatus.Text = text;
        txtGeoStatus.Foreground =
            (Brush)FindResource(brushKey);
    }
}
