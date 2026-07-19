using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using v2rayN.Services;
using ServiceLib.Services;

namespace v2rayN.Views;

public partial class SgMaintenanceWindow : Window
{
    private const string CurrentVersion = "0.0.91";
    private readonly ObservableCollection<SgBackupEntry> _backups = [];
    private readonly BackupAndRestoreViewModel _backupViewModel;
    private Func<Task>? _confirmAction;
    private readonly SgXrayUpdateService _xrayUpdateService;
    private SgXrayCandidate? _pendingXrayCandidate;
    private string _xraySource = "stable";
    private SgXrayNetworkMode _xrayNetworkMode = SgXrayNetworkMode.CurrentRoute;
    private string? _xrayLocalPath;
    private readonly SgSingBoxUpdateService _singBoxUpdateService;
    private SgSingBoxCandidate? _pendingSingBoxCandidate;
    private string _singSource = "stable";
    private SgXrayNetworkMode _singNetworkMode = SgXrayNetworkMode.CurrentRoute;
    private string? _singLocalPath;
    private string _installedXrayVersion = string.Empty;
    private string _installedSingVersion = string.Empty;

    public SgMaintenanceWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);

        _backupViewModel = new BackupAndRestoreViewModel((_, _) => Task.FromResult(true));
        _xrayUpdateService = new SgXrayUpdateService(AppManager.Instance.Config);
        _singBoxUpdateService = new SgSingBoxUpdateService(AppManager.Instance.Config);
        lstBackups.ItemsSource = _backups;

        Loaded += async (_, _) =>
        {
            RefreshBackups();
            SelectXraySource("stable");
            SelectXrayNetwork(SgXrayNetworkMode.CurrentRoute);
            ResetXrayCandidateDisplay();
            SelectSingSource("stable");
            SelectSingNetwork(SgXrayNetworkMode.CurrentRoute);
            ResetSingCandidateDisplay();
            await RefreshInstalledVersionsAsync();
        };
    }

    private void BackupsTab_Click(object sender, RoutedEventArgs e)
    {
        tabBackups.IsChecked = true;
        tabUpdates.IsChecked = false;
        backupsPanel.Visibility = Visibility.Visible;
        updatesPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdatesTab_Click(object sender, RoutedEventArgs e)
    {
        tabBackups.IsChecked = false;
        tabUpdates.IsChecked = true;
        backupsPanel.Visibility = Visibility.Collapsed;
        updatesPanel.Visibility = Visibility.Visible;
    }


    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        btnCreateBackup.IsEnabled = false;
        SetBackupStatus("Создание локальной копии…", "SgMutedBrush");

        try
        {
            var fileName = $"SG-CLIENT-095-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var filePath = Utils.GetBackupPath(fileName);
            var created = await _backupViewModel.LocalBackup(filePath);

            if (!created || !File.Exists(filePath))
            {
                SetBackupStatus("Не удалось создать резервную копию.", "SgErrorBrush");
                return;
            }

            WriteBackupMetadata(filePath);
            RefreshBackups();
            if (TryInspectBackupArchive(filePath, out var inspection, out _))
            {
                var awgText = inspection.HasAwgProfiles
                    ? $"AWG-профилей: {inspection.AwgProfileCount}"
                    : "AWG-профилей: 0";
                SetBackupStatus($"Копия создана: {fileName} · {awgText} · настройки сохранены", "SgSuccessBrush");
            }
            else
            {
                SetBackupStatus($"Копия создана: {fileName}", "SgSuccessBrush");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.CreateBackup", ex);
            SetBackupStatus("Не удалось создать резервную копию. Подробности сохранены в журнале.", "SgErrorBrush");
        }
        finally
        {
            btnCreateBackup.IsEnabled = true;
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filePath } || !File.Exists(filePath))
        {
            SetBackupStatus("Выбранная копия больше не существует.", "SgErrorBrush");
            RefreshBackups();
            return;
        }

        BeginRestoreFromFile(filePath);
    }

    private void VerifyBackupFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Проверить резервную копию SG Client",
            Filter = "Резервная копия SG Client (*.zip)|*.zip|Все файлы (*.*)|*.*",
            DefaultExt = ".zip",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!TryInspectBackupArchive(dialog.FileName, out var inspection, out var error))
        {
            SetBackupStatus(error, "SgErrorBrush");
            return;
        }

        var entry = CreateBackupEntry(dialog.FileName);
        var contents = string.Join(", ", new[]
        {
            inspection.HasDatabase ? "профили и подписки" : null,
            inspection.HasAwgProfiles ? $"AmneziaWG-профили: {inspection.AwgProfileCount}" : null,
            inspection.HasSettings ? "настройки" : null,
            inspection.HasTraffic ? "статистика трафика" : null,
        }.Where(value => value != null));

        SetBackupStatus(
            $"ZIP исправен и совместим: {entry.VersionText}; {inspection.FormatText}; "
            + $"файлов: {inspection.FileCount}; найдено: {contents}.",
            "SgSuccessBrush");
    }

    private void RestoreBackupFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите резервную копию SG Client",
            Filter = "Резервная копия SG Client (*.zip)|*.zip|Все файлы (*.*)|*.*",
            DefaultExt = ".zip",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        BeginRestoreFromFile(dialog.FileName);
    }

    private async void CreateBackupFile_Click(object sender, RoutedEventArgs e)
    {
        var fileName = $"SG-CLIENT-095-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить резервную копию SG Client",
            Filter = "Резервная копия SG Client (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = fileName,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var sourceButton = sender as Button;
        if (sourceButton != null)
        {
            sourceButton.IsEnabled = false;
        }

        SetBackupStatus("Создание переносимой резервной копии…", "SgMutedBrush");

        try
        {
            var created = await _backupViewModel.LocalBackup(dialog.FileName);
            if (!created || !File.Exists(dialog.FileName))
            {
                SetBackupStatus("Не удалось сохранить резервную копию в выбранный файл.", "SgErrorBrush");
                return;
            }

            WriteBackupMetadata(dialog.FileName);
            if (TryInspectBackupArchive(dialog.FileName, out var inspection, out _))
            {
                var awgText = inspection.HasAwgProfiles
                    ? $"AWG-профилей: {inspection.AwgProfileCount}"
                    : "AWG-профилей: 0";
                SetBackupStatus($"Копия сохранена: {Path.GetFileName(dialog.FileName)} · {awgText} · настройки сохранены", "SgSuccessBrush");
            }
            else
            {
                SetBackupStatus($"Копия сохранена: {Path.GetFileName(dialog.FileName)}", "SgSuccessBrush");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.CreateBackupFile", ex);
            SetBackupStatus("Не удалось сохранить резервную копию. Подробности сохранены в журнале.", "SgErrorBrush");
        }
        finally
        {
            if (sourceButton != null)
            {
                sourceButton.IsEnabled = true;
            }
        }
    }

    private void BeginRestoreFromFile(string filePath)
    {
        if (!TryInspectBackupArchive(filePath, out var inspection, out var error))
        {
            SetBackupStatus(error, "SgErrorBrush");
            return;
        }

        var entry = CreateBackupEntry(filePath);
        var awgSummary = inspection.HasAwgProfiles
            ? $"Локальные AmneziaWG-профили: {inspection.AwgProfileCount}\n"
            : "Локальные AmneziaWG-профили: не найдены в архиве\n";
        var description =
            $"Файл: {Path.GetFileName(filePath)}\n" +
            $"{entry.VersionText} · {entry.SizeText}\n" +
            awgSummary +
            $"Настройки клиента: {(inspection.HasSettings ? "найдены" : "не найдены")}\n" +
            $"Статистика трафика: {(inspection.HasTraffic ? "найдена" : "не найдена")}\n\n" +
            "SG Client создаст страховочную копию текущих профилей и настроек, применит выбранную копию и перезапустится.\n\n" +
            "Безопасность: после восстановления маршрут DNS будет установлен «через VPN». " +
            "DNS напрямую можно включить позже только явным выбором пользователя.";

        ShowConfirmation(
            "Восстановить резервную копию из файла?",
            description,
            "Восстановить",
            async () =>
            {
                SetBackupStatus("Проверка и восстановление копии…", "SgWarningBrush");
                await _backupViewModel.LocalRestore(filePath);
            });
    }

    private static bool TryValidateBackupArchive(string filePath, out string error)
    {
        return TryInspectBackupArchive(filePath, out _, out error);
    }

    private static bool TryInspectBackupArchive(
        string filePath,
        out SgBackupArchiveInspection inspection,
        out string error)
    {
        inspection = new SgBackupArchiveInspection();
        error = string.Empty;

        if (!File.Exists(filePath))
        {
            error = "Выбранный файл больше не существует.";
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            if (!BackupAndRestoreViewModel.TryValidateBackupArchive(archive, out var prefix))
            {
                error = "ZIP повреждён, имеет неподдерживаемую структуру или недопустимый распакованный размер.";
                return false;
            }

            var configEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Where(entry =>
                {
                    var name = entry.FullName.Replace('\\', '/');
                    return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            var awgProfileCount = configEntries.Count(entry =>
            {
                var name = entry.FullName.Replace('\\', '/');
                return name.StartsWith(prefix + "sg-awg/profiles/", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase);
            });
            var hasAwgIndex = configEntries.Any(entry =>
            {
                var name = entry.FullName.Replace('\\', '/');
                return name.Equals(prefix + "sg-awg/profiles.json", StringComparison.OrdinalIgnoreCase);
            });

            inspection = new SgBackupArchiveInspection
            {
                FileCount = configEntries.Count,
                FormatText = prefix.Equals("guiConfigs/", StringComparison.OrdinalIgnoreCase)
                    ? "формат 072/073/074"
                    : "совместимый формат 071",
                HasDatabase = configEntries.Any(entry =>
                    entry.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)),
                HasSettings = configEntries.Any(entry =>
                    entry.Name.Equals("sgClientConfig.json", StringComparison.OrdinalIgnoreCase)),
                HasTraffic = configEntries.Any(entry =>
                    entry.Name.Equals("sg-traffic.json", StringComparison.OrdinalIgnoreCase)),
                HasAwgProfiles = hasAwgIndex || awgProfileCount > 0,
                AwgProfileCount = awgProfileCount,
            };

            if (!inspection.HasDatabase || !inspection.HasSettings)
            {
                error = "В ZIP найдена папка guiConfigs, но отсутствуют обязательные профили или настройки SG Client.";
                return false;
            }

            return true;
        }
        catch (InvalidDataException)
        {
            error = "Выбранный ZIP повреждён или имеет неподдерживаемый формат.";
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.ValidateBackupArchive", ex);
            error = "Не удалось проверить резервную копию. Подробности сохранены в журнале.";
            return false;
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filePath } || !File.Exists(filePath))
        {
            RefreshBackups();
            return;
        }

        ShowConfirmation(
            "Удалить резервную копию?",
            $"Файл «{Path.GetFileName(filePath)}» будет удалён без возможности восстановления.",
            "Удалить",
            () =>
            {
                File.Delete(filePath);
                RefreshBackups();
                SetBackupStatus("Резервная копия удалена.", "SgMutedBrush");
                return Task.CompletedTask;
            });
    }

    private void OpenBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath })
        {
            OpenInExplorer(filePath);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(Utils.GetBackupPath(string.Empty));
    }

    private void RefreshBackups()
    {
        _backups.Clear();

        var folder = Utils.GetBackupPath(string.Empty);
        var files = Directory
            .GetFiles(folder, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(path => CreateBackupEntry(path))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        foreach (var item in files)
        {
            _backups.Add(item);
        }

        txtBackupCount.Text = GetCopyCountText(_backups.Count);
        txtNoBackups.Visibility = _backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lstBackups.Visibility = _backups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        txtLastBackup.Text = _backups.Count == 0
            ? "Копий пока нет"
            : $"Последняя: {_backups[0].DisplayDate} · {_backups[0].SizeText}";
    }

    private static SgBackupEntry CreateBackupEntry(string filePath)
    {
        var info = new FileInfo(filePath);
        var version = "Версия не указана";
        var createdAt = info.LastWriteTime;

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var entry = archive.GetEntry("SG-BACKUP-INFO.json");
            if (entry != null)
            {
                using var stream = entry.Open();
                var metadata = JsonSerializer.Deserialize<SgBackupMetadata>(stream);
                if (!string.IsNullOrWhiteSpace(metadata?.Version))
                {
                    version = $"SG Client {metadata.Version}";
                }

                if (metadata?.CreatedAt != default)
                {
                    createdAt = metadata.CreatedAt.LocalDateTime;
                }
            }
        }
        catch
        {
            version = "Архив предыдущей версии";
        }

        return new SgBackupEntry
        {
            FilePath = filePath,
            CreatedAt = createdAt,
            DisplayDate = createdAt.ToString("dd.MM.yyyy  HH:mm"),
            VersionText = version,
            SizeText = FormatBytes(info.Length),
        };
    }

    private static void WriteBackupMetadata(string filePath)
    {
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
        archive.GetEntry("SG-BACKUP-INFO.json")?.Delete();

        var entry = archive.CreateEntry("SG-BACKUP-INFO.json", CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, new SgBackupMetadata
        {
            Version = CurrentVersion,
            CreatedAt = DateTimeOffset.Now,
        });
    }

    private async Task RefreshInstalledVersionsAsync()
    {
        txtSgInstalled.Text = $"Установлена: {CurrentVersion}";
        _installedXrayVersion = await GetCoreVersionAsync(ECoreType.Xray);
        _installedSingVersion = await GetCoreVersionAsync(ECoreType.sing_box);
        txtXrayInstalled.Text = $"Установлена: {_installedXrayVersion}";
        txtSingInstalled.Text = $"Установлена: {_installedSingVersion}";
        txtSingManualInstalled.Text = $"Установлена: {_installedSingVersion}";
        txtAwgInstalled.Text = $"Установлена: {GetAwgVersion()}";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        btnCheckUpdates.IsEnabled = false;
        var preRelease = chkSgPreRelease.IsChecked == true;

        SetUpdateStatus(txtSgStatus, "Проверка…", "SgMutedBrush");
        SetUpdateStatus(txtSingStatus, "Проверка…", "SgMutedBrush");
        SetUpdateStatus(txtAwgStatus, "Проверка локального пакета…", "SgMutedBrush");

        try
        {
            await RefreshInstalledVersionsAsync();
            var singService = new UpdateService(
                AppManager.Instance.Config,
                (_, _) => Task.CompletedTask);

            var sgTask = CheckSgClientReleaseAsync(preRelease);
            var singTask = singService.CheckHasUpdateOnly(ECoreType.sing_box, false);
            await Task.WhenAll(sgTask, singTask);

            ApplySgClientResult(await sgTask);
            ApplyCoreResult(txtSingStatus, await singTask);

            SetUpdateStatus(
                txtAwgStatus,
                "AmneziaWG обновляется только вместе с проверенным пакетом SG Client.",
                "SgAccentBrush");

            txtUpdateCheckedAt.Text =
                $"Последняя проверка: {DateTime.Now:dd.MM.yyyy HH:mm}";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.CheckUpdates", ex);
            SetUpdateStatus(
                txtSgStatus,
                GetFriendlyNetworkError(ex, "Не удалось проверить SG Client"),
                "SgErrorBrush");
            SetUpdateStatus(
                txtSingStatus,
                "Проверка sing-box не завершена. Проверьте интернет или активный VPN.",
                "SgErrorBrush");
            txtUpdateCheckedAt.Text =
                $"Ошибка проверки: {DateTime.Now:dd.MM.yyyy HH:mm}";
        }
        finally
        {
            btnCheckUpdates.IsEnabled = true;
        }
    }

    private void XraySource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string source })
        {
            SelectXraySource(source);
        }
    }

    private void XrayNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mode })
        {
            SelectXrayNetwork(
                mode == "direct"
                    ? SgXrayNetworkMode.DirectWithoutSystemProxy
                    : SgXrayNetworkMode.CurrentRoute);
        }
    }

    private void XrayExactVersion_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        InvalidateXrayCandidate("Номер версии изменён. Выполните проверку заново.");
    }

    private void SelectXraySource(string source)
    {
        _xraySource = source;
        rbXrayStable.IsChecked = source == "stable";
        rbXrayPreview.IsChecked = source == "preview";
        rbXrayExact.IsChecked = source == "exact";
        rbXrayLocal.IsChecked = source == "local";
        pnlXrayExact.Visibility = source == "exact"
            ? Visibility.Visible
            : Visibility.Collapsed;
        pnlXrayLocal.Visibility = source == "local"
            ? Visibility.Visible
            : Visibility.Collapsed;
        InvalidateXrayCandidate("Источник изменён. Выполните проверку выбранной версии.");
    }

    private void SelectXrayNetwork(SgXrayNetworkMode mode)
    {
        _xrayNetworkMode = mode;
        rbXrayCurrentRoute.IsChecked = mode == SgXrayNetworkMode.CurrentRoute;
        rbXrayDirect.IsChecked = mode == SgXrayNetworkMode.DirectWithoutSystemProxy;
        InvalidateXrayCandidate("Маршрут загрузки изменён. Выполните проверку заново.");
    }

    private void XrayChooseLocal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите xray.exe",
            Filter = "Xray (xray.exe)|xray.exe|Исполняемые файлы (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _xrayLocalPath = dialog.FileName;
        txtXrayLocalPath.Text = _xrayLocalPath;
        InvalidateXrayCandidate("Локальный файл выбран. Теперь выполните его проверку.");
    }

    private async void XrayPrepare_Click(object sender, RoutedEventArgs e)
    {
        Func<Task<SgXrayCandidate>> prepare;
        switch (_xraySource)
        {
            case "stable":
                prepare = () => _xrayUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Stable,
                    null,
                    _xrayNetworkMode,
                    SetXrayManualProgress);
                break;

            case "preview":
                prepare = () => _xrayUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Prerelease,
                    null,
                    _xrayNetworkMode,
                    SetXrayManualProgress);
                break;

            case "exact":
                var exactVersion = txtXrayExactVersion.Text.Trim();
                if (exactVersion.IsNullOrEmpty())
                {
                    SetUpdateStatus(
                        txtXrayManualStatus,
                        "Введите точную версию Xray, например 26.6.27.",
                        "SgErrorBrush");
                    return;
                }
                prepare = () => _xrayUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Exact,
                    exactVersion,
                    _xrayNetworkMode,
                    SetXrayManualProgress);
                break;

            case "local":
                if (_xrayLocalPath.IsNullOrEmpty() || !File.Exists(_xrayLocalPath))
                {
                    SetUpdateStatus(
                        txtXrayManualStatus,
                        "Сначала выберите локальный xray.exe.",
                        "SgErrorBrush");
                    return;
                }
                prepare = () => _xrayUpdateService.PrepareLocalAsync(
                    _xrayLocalPath!,
                    SetXrayManualProgress);
                break;

            default:
                return;
        }

        await PrepareXrayCandidateAsync(prepare);
    }

    private void ConfigureXrayCandidateAction()
    {
        if (_pendingXrayCandidate == null)
        {
            btnXrayInstall.IsEnabled = false;
            return;
        }

        var comparison = ParseVersion(_pendingXrayCandidate.Version)
            .CompareTo(ParseVersion(_installedXrayVersion));
        if (comparison > 0)
        {
            btnXrayInstall.Content = "Обновить Xray";
            btnXrayInstall.Style = (Style)FindResource("PrimaryActionButton");
            btnXrayInstall.IsEnabled = true;
            SetUpdateStatus(txtXrayManualStatus,
                $"Проверка пройдена. Доступно обновление {_installedXrayVersion} → {_pendingXrayCandidate.Version}.",
                "SgSuccessBrush");
            return;
        }

        if (comparison == 0)
        {
            btnXrayInstall.Content = "Версия актуальна";
            btnXrayInstall.Style = (Style)FindResource("SecondaryActionButton");
            btnXrayInstall.IsEnabled = false;
            SetUpdateStatus(txtXrayManualStatus,
                "Установлена актуальная выбранная версия Xray. Обновление не требуется.",
                "SgSuccessBrush");
            return;
        }

        var downgradeAllowed = _xraySource is "exact" or "local";
        btnXrayInstall.Content = downgradeAllowed ? "Понизить версию Xray" : "Версия старее";
        btnXrayInstall.Style = (Style)FindResource("DangerActionButton");
        btnXrayInstall.IsEnabled = downgradeAllowed;
        SetUpdateStatus(txtXrayManualStatus,
            downgradeAllowed
                ? $"Выбрана более старая версия {_pendingXrayCandidate.Version}. Понижение доступно как отдельная опасная операция."
                : $"Кандидат {_pendingXrayCandidate.Version} старее установленной {_installedXrayVersion}. Для намеренного понижения выберите «Точная версия» или «Локальный файл».",
            downgradeAllowed ? "SgErrorBrush" : "SgWarningBrush");
    }

    private void XrayInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingXrayCandidate == null)
        {
            SetUpdateStatus(
                txtXrayManualStatus,
                "Сначала проверьте выбранную версию Xray.",
                "SgErrorBrush");
            return;
        }

        var currentVersion = _installedXrayVersion;
        var comparison = ParseVersion(_pendingXrayCandidate.Version)
            .CompareTo(ParseVersion(currentVersion));
        if (comparison == 0)
        {
            SetUpdateStatus(
                txtXrayManualStatus,
                "Эта версия уже установлена. Обновление не требуется.",
                "SgSuccessBrush");
            return;
        }
        if (comparison < 0 && !(_xraySource is "exact" or "local"))
        {
            SetUpdateStatus(
                txtXrayManualStatus,
                "Понижение разрешено только через «Точная версия» или «Локальный файл».",
                "SgErrorBrush");
            return;
        }

        var isDowngrade = comparison < 0;
        ShowConfirmation(
            isDowngrade ? "Понизить версию Xray?" : "Обновить ядро Xray?",
            $"Операция: {(isDowngrade ? "понижение версии" : "обновление")}.\n\n"
                + $"Источник: {_pendingXrayCandidate.SourceDescription}\n"
                + $"Установлена: {currentVersion}\n"
                + $"Будет установлена: {_pendingXrayCandidate.Version}\n"
                + $"SHA-256 xray.exe: {_pendingXrayCandidate.ExecutableSha256}\n\n"
                + $"{_pendingXrayCandidate.IntegrityDescription}\n\n"
                + (isDowngrade
                    ? "ВНИМАНИЕ: более старая версия может не поддерживать текущую конфигурацию.\n\n"
                    : string.Empty)
                + "SG Client остановит подключение, создаст резервную копию, "
                + "заменит ядро, повторно проверит config.json и автоматически "
                + "вернёт прежний Xray при любой ошибке.",
            isDowngrade ? "Понизить версию Xray" : "Обновить Xray",
            InstallPendingXrayAsync);
    }

    private void OpenXrayBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(Utils.GetBackupPath("xray-core"));
    }

    private async Task PrepareXrayCandidateAsync(
        Func<Task<SgXrayCandidate>> prepare)
    {
        SetXrayControlsEnabled(false);
        CleanupPendingXrayCandidate();
        ResetXrayCandidateDisplay();
        try
        {
            SetUpdateStatus(
                txtXrayManualStatus,
                "Подготовка и проверка выбранной версии…",
                "SgMutedBrush");
            _pendingXrayCandidate = await prepare();

            txtXrayCandidateVersion.Text = _pendingXrayCandidate.Version;
            txtXrayCandidateSource.Text = _pendingXrayCandidate.SourceDescription;
            txtXrayCandidateSha.Text = _pendingXrayCandidate.ExecutableSha256;
            txtXrayCandidateConfig.Text = "Успешно";
            txtXrayCandidateConfig.Foreground =
                (Brush)FindResource("SgSuccessBrush");
            ConfigureXrayCandidateAction();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.PrepareXray", ex);
            SetUpdateStatus(
                txtXrayManualStatus,
                GetFriendlyNetworkError(ex, "Xray не подготовлен"),
                "SgErrorBrush");
            CleanupPendingXrayCandidate();
            ResetXrayCandidateDisplay();
        }
        finally
        {
            SetXrayControlsEnabled(true);
        }
    }

    private async Task InstallPendingXrayAsync()
    {
        var candidate = _pendingXrayCandidate;
        if (candidate == null)
        {
            SetUpdateStatus(
                txtXrayManualStatus,
                "Подготовленный Xray больше недоступен.",
                "SgErrorBrush");
            return;
        }

        SetXrayControlsEnabled(false);
        btnXrayInstall.IsEnabled = false;
        try
        {
            var result = await _xrayUpdateService.InstallAsync(
                candidate,
                SetXrayManualProgress);
            SetUpdateStatus(
                txtXrayManualStatus,
                result.Message,
                result.Success ? "SgSuccessBrush" : "SgErrorBrush");
            await RefreshInstalledVersionsAsync();
            if (result.Success)
            {
                NoticeManager.Instance.Enqueue("Ручное обновление Xray завершено.");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.InstallXray", ex);
            SetUpdateStatus(
                txtXrayManualStatus,
                $"Обновление Xray не выполнено: {ex.Message}",
                "SgErrorBrush");
        }
        finally
        {
            CleanupPendingXrayCandidate();
            ResetXrayCandidateDisplay();
            SetXrayControlsEnabled(true);
        }
    }

    private void SetXrayManualProgress(string text)
    {
        SetUpdateStatus(txtXrayManualStatus, text, "SgWarningBrush");
    }

    private void SetXrayControlsEnabled(bool enabled)
    {
        rbXrayStable.IsEnabled = enabled;
        rbXrayPreview.IsEnabled = enabled;
        rbXrayExact.IsEnabled = enabled;
        rbXrayLocal.IsEnabled = enabled;
        rbXrayCurrentRoute.IsEnabled = enabled;
        rbXrayDirect.IsEnabled = enabled;
        txtXrayExactVersion.IsEnabled = enabled;
        btnXrayChooseLocal.IsEnabled = enabled;
        btnXrayPrepare.IsEnabled = enabled;
        if (!enabled)
        {
            btnXrayInstall.IsEnabled = false;
        }
        else
        {
            ConfigureXrayCandidateAction();
        }
    }

    private void InvalidateXrayCandidate(string status)
    {
        CleanupPendingXrayCandidate();
        ResetXrayCandidateDisplay();
        if (IsLoaded)
        {
            SetUpdateStatus(txtXrayManualStatus, status, "SgMutedBrush");
        }
    }

    private void ResetXrayCandidateDisplay()
    {
        txtXrayCandidateVersion.Text = "—";
        txtXrayCandidateSource.Text = "—";
        txtXrayCandidateSha.Text = "—";
        txtXrayCandidateConfig.Text = "Не проверен";
        txtXrayCandidateConfig.Foreground =
            (Brush)FindResource("SgMutedBrush");
        btnXrayInstall.IsEnabled = false;
        btnXrayInstall.Content = "Обновить Xray";
        btnXrayInstall.Style = (Style)FindResource("PrimaryActionButton");
    }

    private void CleanupPendingXrayCandidate()
    {
        _pendingXrayCandidate?.Cleanup();
        _pendingXrayCandidate = null;
    }



    private void SingSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string source })
        {
            SelectSingSource(source);
        }
    }

    private void SingNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mode })
        {
            SelectSingNetwork(
                mode == "direct"
                    ? SgXrayNetworkMode.DirectWithoutSystemProxy
                    : SgXrayNetworkMode.CurrentRoute);
        }
    }

    private void SingExactVersion_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        InvalidateSingCandidate("Номер версии изменён. Выполните проверку заново.");
    }

    private void SelectSingSource(string source)
    {
        _singSource = source;
        rbSingStable.IsChecked = source == "stable";
        rbSingPreview.IsChecked = source == "preview";
        rbSingExact.IsChecked = source == "exact";
        rbSingLocal.IsChecked = source == "local";
        pnlSingExact.Visibility = source == "exact"
            ? Visibility.Visible
            : Visibility.Collapsed;
        pnlSingLocal.Visibility = source == "local"
            ? Visibility.Visible
            : Visibility.Collapsed;
        InvalidateSingCandidate("Источник изменён. Выполните проверку выбранной версии.");
    }

    private void SelectSingNetwork(SgXrayNetworkMode mode)
    {
        _singNetworkMode = mode;
        rbSingCurrentRoute.IsChecked = mode == SgXrayNetworkMode.CurrentRoute;
        rbSingDirect.IsChecked = mode == SgXrayNetworkMode.DirectWithoutSystemProxy;
        InvalidateSingCandidate("Маршрут загрузки изменён. Выполните проверку заново.");
    }

    private void SingChooseLocal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите sing-box.exe",
            Filter = "sing-box (sing-box.exe)|sing-box.exe|Исполняемые файлы (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _singLocalPath = dialog.FileName;
        txtSingLocalPath.Text = _singLocalPath;
        InvalidateSingCandidate("Локальный файл выбран. Теперь выполните его проверку.");
    }

    private async void SingPrepare_Click(object sender, RoutedEventArgs e)
    {
        Func<Task<SgSingBoxCandidate>> prepare;
        switch (_singSource)
        {
            case "stable":
                prepare = () => _singBoxUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Stable,
                    null,
                    _singNetworkMode,
                    SetSingManualProgress);
                break;
            case "preview":
                prepare = () => _singBoxUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Prerelease,
                    null,
                    _singNetworkMode,
                    SetSingManualProgress);
                break;
            case "exact":
                var exactVersion = txtSingExactVersion.Text.Trim();
                if (exactVersion.IsNullOrEmpty())
                {
                    SetUpdateStatus(
                        txtSingManualStatus,
                        "Введите точную версию sing-box, например 1.13.1.",
                        "SgErrorBrush");
                    return;
                }
                prepare = () => _singBoxUpdateService.PrepareOfficialAsync(
                    SgXrayReleaseChannel.Exact,
                    exactVersion,
                    _singNetworkMode,
                    SetSingManualProgress);
                break;
            case "local":
                if (_singLocalPath.IsNullOrEmpty() || !File.Exists(_singLocalPath))
                {
                    SetUpdateStatus(
                        txtSingManualStatus,
                        "Сначала выберите локальный sing-box.exe.",
                        "SgErrorBrush");
                    return;
                }
                prepare = () => _singBoxUpdateService.PrepareLocalAsync(
                    _singLocalPath!,
                    SetSingManualProgress);
                break;
            default:
                return;
        }

        await PrepareSingCandidateAsync(prepare);
    }

    private void ConfigureSingCandidateAction()
    {
        if (_pendingSingBoxCandidate == null)
        {
            btnSingInstall.IsEnabled = false;
            return;
        }

        var candidate = _pendingSingBoxCandidate;
        var comparison = ParseVersion(candidate.Version)
            .CompareTo(ParseVersion(_installedSingVersion));
        var validationNote = candidate.HasProfileValidation
            ? $"Рабочая конфигурация проверена: {candidate.ValidationDescription}."
            : "Рабочий профиль sing-box не найден. Проверены запуск ядра и минимальная конфигурация; установка потребует отдельного подтверждения.";
        var validationBrush = candidate.HasProfileValidation
            ? "SgSuccessBrush"
            : "SgWarningBrush";

        if (comparison > 0)
        {
            btnSingInstall.Content = candidate.HasProfileValidation
                ? "Обновить sing-box"
                : "Обновить без профиля";
            btnSingInstall.Style = (Style)FindResource(
                candidate.HasProfileValidation
                    ? "PrimaryActionButton"
                    : "WarningActionButton");
            btnSingInstall.IsEnabled = true;
            SetUpdateStatus(
                txtSingManualStatus,
                $"Доступно обновление {_installedSingVersion} → {candidate.Version}. {validationNote}",
                validationBrush);
            return;
        }

        if (comparison == 0)
        {
            btnSingInstall.Content = "Версия актуальна";
            btnSingInstall.Style = (Style)FindResource("SecondaryActionButton");
            btnSingInstall.IsEnabled = false;
            SetUpdateStatus(
                txtSingManualStatus,
                "Установлена актуальная выбранная версия sing-box. Обновление не требуется. "
                    + validationNote,
                validationBrush);
            return;
        }

        var downgradeAllowed = _singSource is "exact" or "local";
        btnSingInstall.Content = downgradeAllowed
            ? (candidate.HasProfileValidation
                ? "Понизить версию sing-box"
                : "Понизить без профиля")
            : "Версия старее";
        btnSingInstall.Style = (Style)FindResource("DangerActionButton");
        btnSingInstall.IsEnabled = downgradeAllowed;
        SetUpdateStatus(
            txtSingManualStatus,
            (downgradeAllowed
                ? $"Выбрана более старая версия {candidate.Version}. Понижение доступно как отдельная опасная операция. "
                : $"Кандидат {candidate.Version} старее установленной {_installedSingVersion}. Для намеренного понижения выберите «Точная версия» или «Локальный файл». ")
                + validationNote,
            downgradeAllowed ? "SgErrorBrush" : "SgWarningBrush");
    }

    private void SingInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSingBoxCandidate == null)
        {
            SetUpdateStatus(
                txtSingManualStatus,
                "Сначала проверьте выбранную версию sing-box.",
                "SgErrorBrush");
            return;
        }

        var currentVersion = _installedSingVersion;
        var comparison = ParseVersion(_pendingSingBoxCandidate.Version)
            .CompareTo(ParseVersion(currentVersion));
        if (comparison == 0)
        {
            SetUpdateStatus(
                txtSingManualStatus,
                "Эта версия уже установлена. Обновление не требуется.",
                "SgSuccessBrush");
            return;
        }
        if (comparison < 0 && !(_singSource is "exact" or "local"))
        {
            SetUpdateStatus(
                txtSingManualStatus,
                "Понижение разрешено только через «Точная версия» или «Локальный файл».",
                "SgErrorBrush");
            return;
        }

        var isDowngrade = comparison < 0;
        ShowConfirmation(
            isDowngrade ? "Понизить версию sing-box?" : "Обновить ядро sing-box?",
            $"Операция: {(isDowngrade ? "понижение версии" : "обновление")}.\n\n"
                + $"Источник: {_pendingSingBoxCandidate.SourceDescription}\n"
                + $"Установлена: {currentVersion}\n"
                + $"Будет установлена: {_pendingSingBoxCandidate.Version}\n"
                + $"SHA-256 sing-box.exe: {_pendingSingBoxCandidate.ExecutableSha256}\n\n"
                + $"{_pendingSingBoxCandidate.IntegrityDescription}\n"
                + $"Проверка конфигурации: {_pendingSingBoxCandidate.ValidationDescription}\n\n"
                + (!_pendingSingBoxCandidate.HasProfileValidation
                    ? "ВНИМАНИЕ: ни один текущий профиль не использует sing-box. Проверены только запуск ядра и минимальная конфигурация. Рабочий профиль после установки проверить сейчас невозможно.\n\n"
                    : string.Empty)
                + (isDowngrade
                    ? "ВНИМАНИЕ: более старая версия может не поддерживать текущую конфигурацию.\n\n"
                    : string.Empty)
                + "SG Client остановит подключение, сохранит всю папку sing_box, "
                + "заменит ядро, повторно проверит config.json и автоматически "
                + "вернёт прежнюю папку при любой ошибке.",
            isDowngrade ? "Понизить версию sing-box" : "Обновить sing-box",
            InstallPendingSingAsync);
    }

    private void OpenSingBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(Utils.GetBackupPath("sing-box-core"));
    }

    private async Task PrepareSingCandidateAsync(
        Func<Task<SgSingBoxCandidate>> prepare)
    {
        SetSingControlsEnabled(false);
        CleanupPendingSingCandidate();
        ResetSingCandidateDisplay();
        try
        {
            SetUpdateStatus(
                txtSingManualStatus,
                "Подготовка и проверка выбранной версии…",
                "SgMutedBrush");
            _pendingSingBoxCandidate = await prepare();

            txtSingCandidateVersion.Text = _pendingSingBoxCandidate.Version;
            txtSingCandidateSource.Text = _pendingSingBoxCandidate.SourceDescription;
            txtSingCandidateSha.Text = _pendingSingBoxCandidate.ExecutableSha256;
            txtSingCandidateConfig.Text = _pendingSingBoxCandidate.HasProfileValidation
                ? $"Успешно · {_pendingSingBoxCandidate.ValidationDescription}"
                : "Только ядро · рабочий профиль не найден";
            txtSingCandidateConfig.Foreground = (Brush)FindResource(
                _pendingSingBoxCandidate.HasProfileValidation
                    ? "SgSuccessBrush"
                    : "SgWarningBrush");
            ConfigureSingCandidateAction();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.PrepareSingBox", ex);
            SetUpdateStatus(
                txtSingManualStatus,
                GetFriendlyNetworkError(ex, "sing-box не подготовлен"),
                "SgErrorBrush");
            CleanupPendingSingCandidate();
            ResetSingCandidateDisplay();
        }
        finally
        {
            SetSingControlsEnabled(true);
        }
    }

    private async Task InstallPendingSingAsync()
    {
        var candidate = _pendingSingBoxCandidate;
        if (candidate == null)
        {
            SetUpdateStatus(
                txtSingManualStatus,
                "Подготовленный sing-box больше недоступен.",
                "SgErrorBrush");
            return;
        }

        SetSingControlsEnabled(false);
        btnSingInstall.IsEnabled = false;
        try
        {
            var result = await _singBoxUpdateService.InstallAsync(
                candidate,
                SetSingManualProgress);
            SetUpdateStatus(
                txtSingManualStatus,
                result.Message,
                result.Success ? "SgSuccessBrush" : "SgErrorBrush");
            await RefreshInstalledVersionsAsync();
            if (result.Success)
            {
                NoticeManager.Instance.Enqueue("Ручное обновление sing-box завершено.");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.InstallSingBox", ex);
            SetUpdateStatus(
                txtSingManualStatus,
                $"Обновление sing-box не выполнено: {ex.Message}",
                "SgErrorBrush");
        }
        finally
        {
            CleanupPendingSingCandidate();
            ResetSingCandidateDisplay();
            SetSingControlsEnabled(true);
        }
    }

    private void SetSingManualProgress(string text)
    {
        SetUpdateStatus(txtSingManualStatus, text, "SgWarningBrush");
    }

    private void SetSingControlsEnabled(bool enabled)
    {
        rbSingStable.IsEnabled = enabled;
        rbSingPreview.IsEnabled = enabled;
        rbSingExact.IsEnabled = enabled;
        rbSingLocal.IsEnabled = enabled;
        rbSingCurrentRoute.IsEnabled = enabled;
        rbSingDirect.IsEnabled = enabled;
        txtSingExactVersion.IsEnabled = enabled;
        btnSingChooseLocal.IsEnabled = enabled;
        btnSingPrepare.IsEnabled = enabled;
        if (!enabled)
        {
            btnSingInstall.IsEnabled = false;
        }
        else
        {
            ConfigureSingCandidateAction();
        }
    }

    private void InvalidateSingCandidate(string status)
    {
        CleanupPendingSingCandidate();
        ResetSingCandidateDisplay();
        if (IsLoaded)
        {
            SetUpdateStatus(txtSingManualStatus, status, "SgMutedBrush");
        }
    }

    private void ResetSingCandidateDisplay()
    {
        txtSingCandidateVersion.Text = "—";
        txtSingCandidateSource.Text = "—";
        txtSingCandidateSha.Text = "—";
        txtSingCandidateConfig.Text = "Не проверен";
        txtSingCandidateConfig.Foreground =
            (Brush)FindResource("SgMutedBrush");
        btnSingInstall.IsEnabled = false;
        btnSingInstall.Content = "Обновить sing-box";
        btnSingInstall.Style = (Style)FindResource("PrimaryActionButton");
    }

    private void CleanupPendingSingCandidate()
    {
        _pendingSingBoxCandidate?.Cleanup();
        _pendingSingBoxCandidate = null;
    }

    private static string GetFriendlyNetworkError(
        Exception exception,
        string prefix)
    {
        var message = exception.Message ?? string.Empty;
        var combined = string.Join(
            " ",
            new[] { message, exception.InnerException?.Message }
                .Where(value => value.IsNotEmpty()));

        if (combined.Contains("net_http_ssl_connection_failed", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("SSL connection", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("TLS", StringComparison.OrdinalIgnoreCase))
        {
            return prefix
                + ": не удалось установить защищённое соединение с сервером релизов. "
                + "Попробуйте переключить маршрут между «Текущий маршрут / VPN» "
                + "и «Без системного прокси», затем повторите проверку.";
        }

        if (exception is HttpRequestException
            || exception is TaskCanceledException)
        {
            return prefix
                + ": сервер релизов недоступен. Проверьте интернет, активный VPN "
                + "или выберите другой маршрут загрузки.";
        }

        return $"{prefix}: {message}";
    }

    private static async Task<string> CheckSgClientReleaseAsync(bool includePreRelease)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SG-Client/055");

        var url = includePreRelease
            ? "https://api.github.com/repos/s-gor/sg-client-win/releases?per_page=10"
            : "https://api.github.com/repos/s-gor/sg-client-win/releases/latest";

        var json = await client.GetStringAsync(url);
        using var document = JsonDocument.Parse(json);

        if (includePreRelease)
        {
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                {
                    continue;
                }

                if (release.TryGetProperty("tag_name", out var tag))
                {
                    return tag.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        return document.RootElement.TryGetProperty("tag_name", out var latestTag)
            ? latestTag.GetString() ?? string.Empty
            : string.Empty;
    }

    private void ApplySgClientResult(string remoteTag)
    {
        if (string.IsNullOrWhiteSpace(remoteTag))
        {
            SetUpdateStatus(txtSgStatus, "Не удалось получить опубликованную версию.", "SgErrorBrush");
            return;
        }

        var remote = ParseVersion(remoteTag);
        var current = ParseVersion(CurrentVersion);

        if (remote > current)
        {
            SetUpdateStatus(txtSgStatus, $"Доступна версия {remoteTag}. Установка пока выполняется вручную.", "SgWarningBrush");
        }
        else if (remote < current)
        {
            SetUpdateStatus(txtSgStatus, $"Сборка 073 новее опубликованной версии {remoteTag}.", "SgAccentBrush");
        }
        else
        {
            SetUpdateStatus(txtSgStatus, "Установлена актуальная опубликованная версия.", "SgSuccessBrush");
        }
    }

    private void ApplyCoreResult(TextBlock target, UpdateResult result)
    {
        if (result.Success && result.Version != null)
        {
            SetUpdateStatus(target, $"Доступна версия {result.Version}. Установка пока выполняется вручную.", "SgWarningBrush");
            return;
        }

        var message = result.Msg ?? string.Empty;
        if (message.Contains("latest", StringComparison.OrdinalIgnoreCase)
            || message.Contains("последн", StringComparison.OrdinalIgnoreCase)
            || message.Contains("актуаль", StringComparison.OrdinalIgnoreCase))
        {
            SetUpdateStatus(target, "Установлена актуальная стабильная версия.", "SgSuccessBrush");
            return;
        }

        SetUpdateStatus(
            target,
            "Не удалось проверить версию. Проверьте интернет или активный VPN.",
            "SgErrorBrush");
    }

    private static async Task<string> GetCoreVersionAsync(ECoreType type)
    {
        try
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
            var filePath = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out _);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return "не найдена";
            }

            var output = await Utils.GetCliWrapOutput(filePath, coreInfo?.VersionArg);
            if (string.IsNullOrWhiteSpace(output))
            {
                return FileVersionInfo.GetVersionInfo(filePath).FileVersion ?? "не определена";
            }

            var pattern = type == ECoreType.Xray
                ? @"Xray\s+([0-9.]+)"
                : @"sing-box\s+version\s+([0-9.]+)";

            var match = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : output.Split('\n')[0].Trim();
        }
        catch
        {
            return "не определена";
        }
    }

    private static string GetAwgVersion()
    {
        var filePath = Utils.GetBinPath("amneziawg.exe", "awg");
        if (!File.Exists(filePath))
        {
            return "не найдена";
        }

        var version = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
        return string.IsNullOrWhiteSpace(version)
            ? $"пакет от {File.GetLastWriteTime(filePath):dd.MM.yyyy}"
            : version;
    }

    private static Version ParseVersion(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+(?:\.\d+){1,3}");
        return Version.TryParse(match.Value, out var version)
            ? version
            : new Version(0, 0);
    }

    private void ShowConfirmation(string title, string body, string actionText, Func<Task> action)
    {
        txtConfirmTitle.Text = title;
        txtConfirmBody.Text = body;
        btnConfirmAction.Content = actionText;
        btnConfirmAction.Style = (Style)FindResource(
            actionText.StartsWith("Понизить", StringComparison.Ordinal)
                ? "DangerActionButton"
                : "PrimaryActionButton");
        _confirmAction = action;
        confirmOverlay.Visibility = Visibility.Visible;
    }

    private void CancelConfirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmAction = null;
        confirmOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _confirmAction;
        if (action == null)
        {
            confirmOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        btnConfirmAction.IsEnabled = false;
        try
        {
            confirmOverlay.Visibility = Visibility.Collapsed;
            _confirmAction = null;
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.ConfirmAction", ex);
            SetBackupStatus("Операция не выполнена. Подробности сохранены в журнале.", "SgErrorBrush");
        }
        finally
        {
            btnConfirmAction.IsEnabled = true;
        }
    }

    private void SetBackupStatus(string text, string brushKey)
    {
        txtBackupStatus.Text = text;
        txtBackupStatus.Foreground = (Brush)FindResource(brushKey);
    }

    private void SetUpdateStatus(TextBlock target, string text, string brushKey)
    {
        target.Text = text;
        target.Foreground = (Brush)FindResource(brushKey);
    }

    private static string GetCopyCountText(int count)
    {
        var lastTwo = count % 100;
        var last = count % 10;

        if (lastTwo is >= 11 and <= 14)
        {
            return $"{count} копий";
        }

        return last switch
        {
            1 => $"{count} копия",
            2 or 3 or 4 => $"{count} копии",
            _ => $"{count} копий",
        };
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        var size = (double)Math.Max(0, value);
        var index = 0;

        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.##} {units[index]}";
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
                return;
            }

            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgMaintenanceWindow.OpenInExplorer", ex);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var section = tabUpdates.IsChecked == true ? "updates" : "backups";
        new SgHelpWindow(section) { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CleanupPendingXrayCandidate();
        CleanupPendingSingCandidate();
        Close();
    }
}

public sealed class SgBackupArchiveInspection
{
    public int FileCount { get; init; }
    public string FormatText { get; init; } = string.Empty;
    public bool HasDatabase { get; init; }
    public bool HasSettings { get; init; }
    public bool HasTraffic { get; init; }
    public bool HasAwgProfiles { get; init; }
    public int AwgProfileCount { get; init; }
}

public sealed class SgBackupEntry
{
    public string FilePath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string DisplayDate { get; init; } = string.Empty;
    public string VersionText { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
}

public sealed class SgBackupMetadata
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
