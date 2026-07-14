using System.Text.Json;
using System.Windows.Controls;
using ServiceLib.Handler.Builder;

namespace v2rayN.Views;

public partial class SgDpiWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private string _savedMode;
    private bool _isApplying;
    private bool _showAppliedComparison;
    private bool _hasApplied;
    private string? _beforeApplyTitle;
    private DpiConfigInspection? _beforeApplyInspection;
    private DpiConfigInspection? _afterApplyInspection;
    private bool _customJsonValidated;
    private string? _validatedCustomJson;
    private bool _suppressJsonChanged;

    private enum DpiTargetKind
    {
        None,
        Xray,
        Singbox,
        Hysteria2,
        AmneziaWg,
    }

    private sealed record DpiProfileContext(bool AppliesSelectedMode, string AppliedTitle, string Description, DpiTargetKind TargetKind);
    private sealed record DpiConfigInspection(bool Exists, string? DetectedMode, DateTime? ModifiedAt, string Summary, string Parameters);
    private sealed record DpiReloadResult(bool Success, string Message);

    public SgDpiWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        if (_config.SgQuickSettingsItem.DpiCustomJson.IsNullOrEmpty())
        {
            _config.SgQuickSettingsItem.DpiCustomJson = SgDpiModeHelper.GetDefaultCustomJson();
        }
        _savedMode = SgDpiModeHelper.Normalize(_config.SgQuickSettingsItem.DpiMode);
        rbAuto.IsChecked = _savedMode == "auto";
        rbOff.IsChecked = _savedMode == "off";
        rbTls.IsChecked = _savedMode == "tls";
        rbNoise.IsChecked = _savedMode == "tls_noise";
        rbCustom.IsChecked = _savedMode == "custom";
        SetJsonText(SgDpiModeHelper.GetCustomJsonForMode(_savedMode, _config.SgQuickSettingsItem.DpiCustomJson));
        Loaded += SgDpiWindow_Loaded;
    }

    private async void SgDpiWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshStateAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplying)
        {
            return;
        }

        var selectedMode = GetSelectedMode();
        if (selectedMode == "custom"
            && (!_customJsonValidated
                || !string.Equals(_validatedCustomJson, txtCustomJson.Text, StringComparison.Ordinal)))
        {
            SetJsonStatus("Сначала нажмите «Проверить» и дождитесь успешной проверки выбранным ядром.", "SgErrorBrush");
            return;
        }

        _isApplying = true;
        _showAppliedComparison = false;
        txtFooterStatus.Text = "Применение в процессе…";
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource("SgMutedBrush");
        SetControlsEnabled(false);
        SetVisualState("ПРИМЕНЕНИЕ…", "Сохраняю режим, пересоздаю рабочий конфиг и проверяю запущенное ядро.", "warning");

        var previousMode = _savedMode;
        var previousCustomJson = _config.SgQuickSettingsItem.DpiCustomJson;
        var settingsSaved = false;
        Logging.SaveLog($"[SG DPI] Selected: {SgDpiModeHelper.GetTitle(selectedMode)}");
        try
        {
            var context = await GetCurrentProfileContextAsync();
            var beforeInspection = InspectGeneratedConfig();
            var beforeTitle = GetAppliedModeTitle(beforeInspection, context);

            CreateDpiSettingsBackup(previousMode, previousCustomJson);
            _config.SgQuickSettingsItem.DpiMode = selectedMode;
            if (selectedMode == "custom")
            {
                _config.SgQuickSettingsItem.DpiCustomJson = txtCustomJson.Text;
            }
            SgDpiModeHelper.ApplyLegacyFragmentSettings(_config);
            if (await ConfigHandler.SaveConfig(_config) != 0)
            {
                _config.SgQuickSettingsItem.DpiMode = previousMode;
                _config.SgQuickSettingsItem.DpiCustomJson = previousCustomJson;
                SgDpiModeHelper.ApplyLegacyFragmentSettings(_config);
                SetVisualState("НЕ СОХРАНЕНО", "Не удалось сохранить режим маскировки в настройках SG Client.", "error");
                btnApply.IsEnabled = true;
                return;
            }

            settingsSaved = true;
            _savedMode = selectedMode;
            if (selectedMode == "custom")
            {
                _config.SgQuickSettingsItem.DpiCustomJson = txtCustomJson.Text;
            }
            _hasApplied = true;
            txtFooterStatus.Text = "Применено. Окно можно закрыть или продолжить настройку.";
            txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource("SgAccentBrush");
            Logging.SaveLog($"[SG DPI] Settings saved: mode={selectedMode}");
            StatusBarViewModel.Instance.RefreshSgQuickSummaries();

            if (!_config.TunModeItem.EnableTun)
            {
                await RefreshStateAsync("СОХРАНЕНО", "Режим сохранён. Он будет записан в рабочий конфиг при следующем включении TUN.", "warning");
                return;
            }

            if (!context.AppliesSelectedMode)
            {
                await RefreshStateAsync("СОХРАНЕНО", context.Description, "warning");
                return;
            }

            var configPath = Utils.GetBinConfigPath(Global.CoreConfigFileName);
            var previousStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : (DateTime?)null;

            AppEvents.ReloadRequested.Publish();
            var reloadResult = await WaitForReloadAsync(previousStamp);
            if (!reloadResult.Success)
            {
                Logging.SaveLog($"[SG DPI] Applied: NO; reason={reloadResult.Message}");
                await RollbackDpiSettingsAsync(previousMode, previousCustomJson);
                await RefreshStateAsync("ОТКАТ ВЫПОЛНЕН", reloadResult.Message + " Предыдущие настройки восстановлены.", "error");
                return;
            }

            var inspection = InspectGeneratedConfig();
            if (selectedMode != "custom"
                && !string.Equals(inspection.DetectedMode, selectedMode, StringComparison.Ordinal))
            {
                var detected = inspection.DetectedMode == null
                    ? "не удалось определить"
                    : SgDpiModeHelper.GetTitle(inspection.DetectedMode);
                Logging.SaveLog($"[SG DPI] Config match: NO; selected={selectedMode}; detected={inspection.DetectedMode ?? "unknown"}");
                await RollbackDpiSettingsAsync(previousMode, previousCustomJson);
                await RefreshStateAsync(
                    "НЕ ПРИМЕНЕНО",
                    $"Ядро запущено, но рабочий конфиг не совпал с выбором. Обнаружено: {detected}. Предыдущие настройки восстановлены.",
                    "error");
                return;
            }

            _beforeApplyTitle = beforeTitle;
            _beforeApplyInspection = beforeInspection;
            _afterApplyInspection = inspection;
            _showAppliedComparison = true;

            Logging.SaveLog($"[SG DPI] Config match: OK; mode={selectedMode}; Applied: YES");
            await RefreshStateAsync(
                "ПРИМЕНЕНО",
                $"Режим записан в рабочий config.json и подтверждён после запуска ядра · {DateTime.Now:dd.MM.yyyy HH:mm:ss}.",
                "success");
        }
        catch (Exception ex)
        {
            _showAppliedComparison = false;
            Logging.SaveLog("Apply SG DPI mode", ex);
            if (!settingsSaved)
            {
                await RefreshStateAsync("НЕ ПРИМЕНЕНО", $"Ошибка применения: {ex.Message}", "error");
            }
            else
            {
                try
                {
                    await RollbackDpiSettingsAsync(previousMode, previousCustomJson);
                    await RefreshStateAsync(
                        "ОТКАТ ВЫПОЛНЕН",
                        $"Ошибка применения: {ex.Message} Предыдущие настройки восстановлены.",
                        "error");
                }
                catch (Exception rollbackEx)
                {
                    Logging.SaveLog("Rollback SG DPI mode", rollbackEx);
                    await RefreshStateAsync(
                        "ОШИБКА ОТКАТА",
                        $"Ошибка применения: {ex.Message} Автоматический откат не завершён: {rollbackEx.Message}",
                        "error");
                }
            }
        }
        finally
        {
            _isApplying = false;
            SetControlsEnabled(true);
        }
    }

    private async void DpiMode_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_isApplying)
        {
            _showAppliedComparison = false;
            _beforeApplyTitle = null;
            _beforeApplyInspection = null;
            _afterApplyInspection = null;
            var selectedMode = GetSelectedMode();
            if (selectedMode != "custom")
            {
                _jsonEditing = false;
                txtCustomJson.IsReadOnly = true;
            }
            if (!_jsonEditing)
            {
                SetJsonText(SgDpiModeHelper.GetCustomJsonForMode(
                    selectedMode,
                    _config.SgQuickSettingsItem.DpiCustomJson));
                _customJsonValidated = false;
                _validatedCustomJson = null;
                btnValidateJson.IsEnabled = false;
                SetJsonStatus(
                    selectedMode == "custom"
                        ? "Сохранённый пользовательский JSON показан только для чтения. Нажмите «Редактировать», чтобы изменить его."
                        : "Показан фактически используемый пресет. Нажмите «Редактировать», чтобы создать пользовательский режим.",
                    "SgMutedBrush");
            }
            await RefreshStateAsync();
        }
    }

    private async Task RefreshStateAsync(string? forcedStatus = null, string? forcedDetail = null, string? forcedVisual = null)
    {
        try
        {
            var selectedMode = GetSelectedMode();
            var context = await GetCurrentProfileContextAsync();
            var inspection = InspectGeneratedConfig();
            var customJsonChanged = selectedMode == "custom"
                && !string.Equals(txtCustomJson.Text, _config.SgQuickSettingsItem.DpiCustomJson, StringComparison.Ordinal);
            var selectedChanged = !string.Equals(selectedMode, _savedMode, StringComparison.Ordinal)
                || customJsonChanged;

            RenderComparison(context, inspection, selectedMode, selectedChanged);
            tbDiagnostic.Text = inspection.Summary;

            if (forcedStatus != null)
            {
                SetVisualState(forcedStatus, forcedDetail ?? string.Empty, forcedVisual ?? "success");
                btnApply.IsEnabled = !_isApplying
                    && CanApplySelectedMode()
                    && (forcedStatus is "НЕ ПРИМЕНЕНО" or "НЕ СОХРАНЕНО" or "ОШИБКА ПРОВЕРКИ");
                return;
            }

            if (selectedChanged)
            {
                SetVisualState(
                    "БУДЕТ ИЗМЕНЕНО",
                    "Справа показаны параметры, которые создаст тот же генератор. Рабочий config.json пока не изменён.",
                    "warning");
                btnApply.IsEnabled = CanApplySelectedMode();
                return;
            }

            if (!_config.TunModeItem.EnableTun)
            {
                SetVisualState(
                    "СОХРАНЕНО",
                    "Настройка сохранена, но TUN выключен. Применение будет подтверждено после следующего запуска.",
                    "warning");
                btnApply.IsEnabled = false;
                return;
            }

            if (!context.AppliesSelectedMode)
            {
                SetVisualState("НЕ ПРИМЕНЯЕТСЯ", context.Description, "warning");
                btnApply.IsEnabled = false;
                return;
            }

            var appliedModeMatches = selectedMode == "custom"
                ? _savedMode == "custom" && inspection.Exists
                : string.Equals(inspection.DetectedMode, selectedMode, StringComparison.Ordinal);
            if (appliedModeMatches
                && StatusBarViewModel.Instance.TunUiState == ETunUiState.On
                && CoreManager.Instance.IsCoreRunning)
            {
                SetVisualState(
                    "ПРИМЕНЕНО",
                    inspection.ModifiedAt.HasValue
                        ? $"Режим подтверждён в рабочем config.json от {inspection.ModifiedAt.Value:dd.MM.yyyy HH:mm:ss}; ядро работает."
                        : "Режим подтверждён в рабочем config.json; ядро работает.",
                    "success");
                btnApply.IsEnabled = false;
                return;
            }

            SetVisualState(
                "НЕ ПОДТВЕРЖДЕНО",
                "Сохранённый режим не совпадает с последним рабочим config.json. Нажмите «Применить».",
                "error");
            btnApply.IsEnabled = CanApplySelectedMode();
        }
        catch (Exception ex)
        {
            tbBeforeMode.Text = "Не определено";
            tbBeforeParameters.Text = $"Диагностика не выполнена: {ex.Message}";
            SetAfterComparisonVisible(false);
            tbDiagnostic.Text = $"Диагностика не выполнена: {ex.Message}";
            SetVisualState("ОШИБКА ПРОВЕРКИ", ex.Message, "error");
            btnApply.IsEnabled = CanApplySelectedMode();
        }
    }

    private void RenderComparison(DpiProfileContext context, DpiConfigInspection currentInspection, string selectedMode, bool selectedChanged)
    {
        if (_showAppliedComparison && _beforeApplyInspection != null && _afterApplyInspection != null)
        {
            tbBeforeCaption.Text = "БЫЛО";
            tbBeforeState.Text = "АКТИВНАЯ КОНФИГУРАЦИЯ ДО ПРИМЕНЕНИЯ";
            tbBeforeMode.Text = _beforeApplyTitle ?? GetAppliedModeTitle(_beforeApplyInspection, context);
            tbBeforeParameters.Text = _beforeApplyInspection.Parameters;
            SetComparisonCardStyle(comparisonBeforeCard, "neutral");

            SetAfterComparisonVisible(true);
            tbAfterCaption.Text = "ПРИМЕНЕНО";
            tbAfterMode.Text = GetAppliedModeTitle(_afterApplyInspection, context);
            tbAfterParameters.Text = _afterApplyInspection.Parameters;
            SetComparisonCardStyle(comparisonAfterCard, "success");
            return;
        }

        tbBeforeCaption.Text = "АКТИВНАЯ КОНФИГУРАЦИЯ";
        tbBeforeState.Text = _config.TunModeItem.EnableTun ? "ПРИМЕНЕНО СЕЙЧАС" : "ПОСЛЕДНИЙ СОЗДАННЫЙ CONFIG.JSON · TUN ВЫКЛЮЧЕН";
        tbBeforeMode.Text = GetAppliedModeTitle(currentInspection, context);
        tbBeforeParameters.Text = context.AppliesSelectedMode ? currentInspection.Parameters : context.Description;
        SetComparisonCardStyle(comparisonBeforeCard, "neutral");

        if (!selectedChanged)
        {
            SetAfterComparisonVisible(false);
            return;
        }

        SetAfterComparisonVisible(true);
        tbAfterCaption.Text = "БУДЕТ ПОСЛЕ ПРИМЕНЕНИЯ";
        tbAfterMode.Text = SgDpiModeHelper.GetTitle(selectedMode);
        tbAfterParameters.Text = BuildExpectedParameters(selectedMode, context);
        SetComparisonCardStyle(comparisonAfterCard, "warning");
    }

    private void SetAfterComparisonVisible(bool visible)
    {
        comparisonAfterCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        comparisonGapColumn.Width = visible ? new GridLength(18) : new GridLength(0);
        comparisonAfterColumn.Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private string BuildExpectedParameters(string mode, DpiProfileContext context)
    {
        if (!context.AppliesSelectedMode)
        {
            return context.Description;
        }

        if (context.TargetKind == DpiTargetKind.Singbox)
        {
            var profile = SgDpiModeHelper.GetSingboxProfile(
                mode,
                mode == "custom" ? txtCustomJson.Text : _config.SgQuickSettingsItem.DpiCustomJson);
            return $"record_fragment={(profile.RecordFragment ? "true" : "нет")}\n"
                + $"fragment={(profile.Fragment ? "true" : "нет")}\n"
                + $"fragment_fallback_delay={profile.FragmentFallbackDelay ?? "нет"}";
        }

        var xray = SgDpiModeHelper.GetXrayProfile(
            mode,
            mode == "custom" ? txtCustomJson.Text : _config.SgQuickSettingsItem.DpiCustomJson);
        if (!xray.Enabled)
        {
            return "FinalMask fragment=нет\n"
                + "packets=нет\n"
                + "length=нет\n"
                + "delay=нет\n"
                + "maxSplit=нет\n"
                + "UDP-noise=выключен";
        }

        var lengths = xray.Lengths.Count == 0 ? "нет" : string.Join(", ", xray.Lengths);
        var delays = xray.Delays.Count == 0 ? "нет" : string.Join(", ", xray.Delays);
        var noise = xray.EnableUdpNoise
            ? $"reset={xray.NoiseReset}; noise={xray.NoiseLength}; delay={xray.NoiseDelay}"
            : "выключен";
        return $"packets={xray.Packets}\n"
            + $"length={lengths}\n"
            + $"delay={delays}\n"
            + $"maxSplit={xray.MaxSplit}\n"
            + $"UDP-noise={noise}";
    }

    private static void SetComparisonCardStyle(Border card, string visual)
    {
        var background = visual switch
        {
            "success" => "SgSuccessSoftBrush",
            "warning" => "SgWarningSoftBrush",
            _ => "SgInputBrush",
        };
        var border = visual switch
        {
            "success" => "SgSuccessBrush",
            "warning" => "SgWarningBrush",
            _ => "SgBorderBrush",
        };
        card.SetResourceReference(Border.BackgroundProperty, background);
        card.SetResourceReference(Border.BorderBrushProperty, border);
    }

    private void SetVisualState(string status, string detail, string visual)
    {
        tbApplyStatus.Text = status;
        tbApplyDetail.Text = detail;

        var background = visual switch
        {
            "error" => "SgErrorSoftBrush",
            "warning" => "SgWarningSoftBrush",
            _ => "SgSuccessSoftBrush",
        };
        var foreground = visual switch
        {
            "error" => "SgErrorBrush",
            "warning" => "SgWarningBrush",
            _ => "SgSuccessBrush",
        };

        stateCard.SetResourceReference(Border.BackgroundProperty, background);
        stateCard.SetResourceReference(Border.BorderBrushProperty, foreground);
        statusBadge.SetResourceReference(Border.BackgroundProperty, background);
        statusBadge.SetResourceReference(Border.BorderBrushProperty, foreground);
        tbApplyStatus.SetResourceReference(TextBlock.ForegroundProperty, foreground);
    }

    private void SetControlsEnabled(bool enabled)
    {
        rbAuto.IsEnabled = enabled;
        rbOff.IsEnabled = enabled;
        rbTls.IsEnabled = enabled;
        rbNoise.IsEnabled = enabled;
        rbCustom.IsEnabled = enabled;
        btnEditJson.IsEnabled = enabled;
        btnDefaultJson.IsEnabled = enabled;
        btnValidateJson.IsEnabled = enabled && _jsonEditing;
        txtCustomJson.IsEnabled = enabled;
        if (!enabled)
        {
            btnApply.IsEnabled = false;
        }
        else
        {
            btnApply.IsEnabled = CanApplySelectedMode()
                && tbApplyStatus.Text != "ПРИМЕНЕНО"
                && tbApplyStatus.Text != "СОХРАНЕНО"
                && tbApplyStatus.Text != "НЕ ПРИМЕНЯЕТСЯ";
        }
        btnApply.Content = enabled ? "Применить" : "Применение…";
    }

    private bool CanApplySelectedMode()
    {
        return GetSelectedMode() != "custom"
            || (_customJsonValidated
                && string.Equals(_validatedCustomJson, txtCustomJson.Text, StringComparison.Ordinal));
    }

    private string GetSelectedMode()
    {
        return rbOff.IsChecked == true
            ? "off"
            : rbTls.IsChecked == true
                ? "tls"
                : rbNoise.IsChecked == true
                    ? "tls_noise"
                    : rbCustom.IsChecked == true
                        ? "custom"
                        : "auto";
    }

    private async Task<DpiProfileContext> GetCurrentProfileContextAsync()
    {
        var awg = AmneziaWgManager.Instance.GetSelectedProfile();
        if (awg != null)
        {
            return new(false, "Встроенная маскировка AmneziaWG", $"Для текущего профиля AmneziaWG действует встроенная маскировка из .conf. {DescribeAwgParameters(awg)}", DpiTargetKind.AmneziaWg);
        }

        var profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (profile == null)
        {
            return new(false, "Нет активного профиля", "Текущий профиль не выбран. Режим сохранён для следующего поддерживаемого подключения.", DpiTargetKind.None);
        }

        var core = AppManager.Instance.GetCoreType(profile, profile.ConfigType);
        var header = $"Профиль: {profile.Remarks} · {profile.ConfigType} · ядро {core}.";
        if (profile.ConfigType == EConfigType.Hysteria2)
        {
            return new(false, "Штатный QUIC/UDP", $"{header} Обычное Xray-дробление к Hysteria2/QUIC не применяется.", DpiTargetKind.Hysteria2);
        }

        if (profile.StreamSecurity is not (Global.StreamSecurity or Global.StreamSecurityReality))
        {
            return new(false, "Не применяется", $"{header} У профиля нет TLS/REALITY, поэтому SG DPI к нему не применяется.", DpiTargetKind.None);
        }

        if (core == ECoreType.sing_box)
        {
            return new(true, string.Empty, header, DpiTargetKind.Singbox);
        }

        if (core is ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5)
        {
            return new(true, string.Empty, header, DpiTargetKind.Xray);
        }

        return new(false, "Не применяется", $"{header} Это ядро не поддерживается модулем SG DPI.", DpiTargetKind.None);
    }

    private async Task<DpiReloadResult> WaitForReloadAsync(DateTime? previousConfigStamp)
    {
        var deadline = DateTime.UtcNow.AddSeconds(70);
        var observedReload = false;
        var configPath = Utils.GetBinConfigPath(Global.CoreConfigFileName);

        while (DateTime.UtcNow < deadline)
        {
            var state = StatusBarViewModel.Instance.TunUiState;
            var currentStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : (DateTime?)null;
            var configChanged = currentStamp.HasValue
                && (!previousConfigStamp.HasValue || currentStamp.Value > previousConfigStamp.Value);

            if ((state is ETunUiState.Starting or ETunUiState.Stopping or ETunUiState.Switching) || configChanged)
            {
                observedReload = true;
            }

            if (state == ETunUiState.Error)
            {
                return new(false, StatusBarViewModel.Instance.TunDetailText.IsNullOrEmpty()
                    ? "TUN сообщил об ошибке при применении режима."
                    : StatusBarViewModel.Instance.TunDetailText);
            }

            if (observedReload
                && state == ETunUiState.On
                && CoreManager.Instance.IsCoreRunning
                && configChanged)
            {
                return new(true, "Рабочий конфиг обновлён, ядро запущено.");
            }

            await Task.Delay(250);
        }

        return new(false, "Применение не было подтверждено за 70 секунд. Проверьте журнал SG Client.");
    }

    private string GetAppliedModeTitle(DpiConfigInspection inspection, DpiProfileContext context)
    {
        if (!context.AppliesSelectedMode)
        {
            return context.AppliedTitle;
        }
        if (!inspection.Exists)
        {
            return "Рабочий конфиг ещё не создан";
        }
        if (_savedMode == "custom")
        {
            return SgDpiModeHelper.GetTitle("custom");
        }
        return inspection.DetectedMode == null
            ? "Не удалось определить"
            : SgDpiModeHelper.GetTitle(inspection.DetectedMode);
    }

    private static string DescribeAwgParameters(AwgProfile profile)
    {
        if (!File.Exists(profile.ConfigPath))
        {
            return "Файл профиля не найден.";
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4"
        };
        var values = new List<string>();
        foreach (var rawLine in File.ReadLines(profile.ConfigPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }
            var parts = line.Split('=', 2);
            var key = parts[0].Trim();
            if (allowed.Contains(key))
            {
                values.Add($"{key}={parts[1].Trim()}");
            }
        }
        return values.Count == 0
            ? "В профиле не найдены Jc/Jmin/Jmax, S1–S4 и H1–H4."
            : $"Параметры: {string.Join("; ", values)}.";
    }

    private static DpiConfigInspection InspectGeneratedConfig()
    {
        var path = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        if (!File.Exists(path))
        {
            const string missing = "Рабочий config.json ещё не создан.";
            return new(false, null, null, missing, missing);
        }

        var modifiedAt = File.GetLastWriteTime(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("outbounds", out var outbounds) || outbounds.ValueKind != JsonValueKind.Array)
        {
            var missing = $"config.json изменён {modifiedAt:dd.MM.yyyy HH:mm:ss}; раздел outbounds не найден.";
            return new(true, null, modifiedAt, missing, "Раздел outbounds не найден.");
        }

        var findings = new List<string>();
        string? detectedMode = null;
        foreach (var outbound in outbounds.EnumerateArray())
        {
            var tag = GetString(outbound, "tag") ?? "без тега";
            if (outbound.TryGetProperty("streamSettings", out var streamSettings)
                && streamSettings.TryGetProperty("finalmask", out var finalmask))
            {
                var fragment = FindMask(finalmask, "tcp", "fragment");
                if (fragment.HasValue)
                {
                    var settings = fragment.Value.GetProperty("settings");
                    var packets = GetRaw(settings, "packets");
                    var length = GetRaw(settings, "length", "lengths");
                    var delay = GetRaw(settings, "delay", "delays");
                    var maxSplit = GetRaw(settings, "maxSplit");
                    findings.Add($"Xray {tag}:\npackets={packets}\nlength={length}\ndelay={delay}\nmaxSplit={maxSplit}");
                    detectedMode = PickStrongerMode(detectedMode, DetectXrayMode(length, delay, maxSplit));
                }
                var noise = FindMask(finalmask, "udp", "noise");
                if (noise.HasValue)
                {
                    var settings = noise.Value.GetProperty("settings");
                    var noiseLength = "нет";
                    var noiseDelay = "нет";
                    if (settings.TryGetProperty("noise", out var noiseItems)
                        && noiseItems.ValueKind == JsonValueKind.Array
                        && noiseItems.GetArrayLength() > 0)
                    {
                        var firstNoise = noiseItems[0];
                        noiseLength = GetRaw(firstNoise, "rand", "length");
                        noiseDelay = GetRaw(firstNoise, "delay");
                    }
                    findings.Add($"Xray {tag}: UDP-noise reset={GetRaw(settings, "reset")}; noise={noiseLength}; delay={noiseDelay}");
                    detectedMode = "tls_noise";
                }
                else if (fragment.HasValue)
                {
                    findings.Add($"Xray {tag}: UDP-noise=выключен");
                }
            }

            if (outbound.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.Object)
            {
                var recordFragment = GetRaw(tls, "record_fragment");
                var fragment = GetRaw(tls, "fragment");
                if (recordFragment != "нет" || fragment != "нет")
                {
                    findings.Add($"sing-box {tag}:\nrecord_fragment={recordFragment}\nfragment={fragment}\nfragment_fallback_delay={GetRaw(tls, "fragment_fallback_delay")}");
                    detectedMode = PickStrongerMode(detectedMode, DetectSingboxMode(recordFragment, fragment));
                }
            }
        }

        if (findings.Count == 0)
        {
            detectedMode = "off";
            findings.Add("Настройки SG DPI в рабочем config.json отсутствуют.");
        }

        var parameters = string.Join("\n\n", findings);
        return new(
            true,
            detectedMode,
            modifiedAt,
            $"Рабочий config.json · {modifiedAt:dd.MM.yyyy HH:mm:ss}\n{parameters}",
            parameters);
    }

    private static string? DetectXrayMode(string length, string delay, string maxSplit)
    {
        if (length.Contains("20-40", StringComparison.Ordinal) || maxSplit.Contains("10-14", StringComparison.Ordinal))
        {
            return "tls_noise";
        }
        if (length.Contains("30-60", StringComparison.Ordinal) || maxSplit.Contains("8-12", StringComparison.Ordinal))
        {
            return "tls";
        }
        if (length.Contains("50-90", StringComparison.Ordinal) || maxSplit.Contains("6-8", StringComparison.Ordinal))
        {
            return "auto";
        }
        return "custom";
    }

    private static string? DetectSingboxMode(string recordFragment, string fragment)
    {
        var recordEnabled = string.Equals(recordFragment, "true", StringComparison.OrdinalIgnoreCase);
        var fragmentEnabled = string.Equals(fragment, "true", StringComparison.OrdinalIgnoreCase);
        if (recordEnabled && fragmentEnabled)
        {
            return "tls_noise";
        }
        if (fragmentEnabled)
        {
            return "tls";
        }
        if (recordEnabled)
        {
            return "auto";
        }
        return "off";
    }

    private static string? PickStrongerMode(string? current, string? candidate)
    {
        if (candidate == null)
        {
            return current;
        }
        static int Rank(string mode) => mode switch
        {
            "custom" => 5,
            "tls_noise" => 4,
            "tls" => 3,
            "auto" => 2,
            "off" => 1,
            _ => 0,
        };
        return current == null || Rank(candidate) > Rank(current) ? candidate : current;
    }

    private static JsonElement? FindMask(JsonElement finalmask, string network, string type)
    {
        if (!finalmask.TryGetProperty(network, out var masks) || masks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var mask in masks.EnumerateArray())
        {
            if (string.Equals(GetString(mask, "type"), type, StringComparison.OrdinalIgnoreCase)
                && mask.TryGetProperty("settings", out _))
            {
                return mask;
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string GetRaw(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.TryGetProperty(property, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
            }
        }
        return "нет";
    }


    private bool _jsonEditing;

    private void EditJson_Click(object sender, RoutedEventArgs e)
    {
        _jsonEditing = true;
        rbCustom.IsChecked = true;
        txtCustomJson.IsReadOnly = false;
        btnValidateJson.IsEnabled = true;
        _customJsonValidated = false;
        _validatedCustomJson = null;
        txtCustomJson.Focus();
        SetJsonStatus("Ручное редактирование включено. После каждого изменения требуется новая проверка.", "SgWarningBrush");
    }

    private async void ValidateJson_Click(object sender, RoutedEventArgs e)
    {
        btnValidateJson.IsEnabled = false;
        btnApply.IsEnabled = false;
        SetJsonStatus("Создаю полный временный config.json и запускаю проверку ядром…", "SgMutedBrush");
        try
        {
            if (!SgDpiModeHelper.TryParseCustomProfiles(
                    txtCustomJson.Text,
                    out _,
                    out _,
                    out var parseError))
            {
                throw new InvalidDataException(parseError);
            }

            var profile = await AppManager.Instance.GetProfileItem(_config.IndexId)
                ?? throw new InvalidOperationException("Сначала выберите профиль Xray или sing-box.");
            if (profile.ConfigType == EConfigType.Hysteria2)
            {
                throw new InvalidOperationException("Для Hysteria2 эти параметры не применяются. Выберите профиль Xray или обычный профиль sing-box с TLS/REALITY.");
            }

            var validationConfig = JsonUtils.DeepCopy(_config)
                ?? throw new InvalidOperationException("Не удалось создать копию настроек.");
            validationConfig.SgQuickSettingsItem ??= new SgQuickSettingsItem();
            validationConfig.SgQuickSettingsItem.DpiMode = "custom";
            validationConfig.SgQuickSettingsItem.DpiCustomJson = txtCustomJson.Text;
            validationConfig.TunModeItem.EnableTun = false;
            SgDpiModeHelper.ApplyLegacyFragmentSettings(validationConfig);

            var builder = await CoreConfigContextBuilder.Build(validationConfig, profile);
            if (!builder.Success)
            {
                var errors = builder.ValidatorResult.Errors.Count > 0
                    ? string.Join(Environment.NewLine, builder.ValidatorResult.Errors)
                    : "Профиль не прошёл внутреннюю проверку.";
                throw new InvalidDataException(errors);
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"sg-client-dpi-check-{Guid.NewGuid():N}.json");
            try
            {
                var generated = await CoreConfigHandler.GenerateClientConfig(builder.Context, tempPath);
                if (generated.Success != true || !File.Exists(tempPath))
                {
                    throw new InvalidDataException(generated.Msg.IsNotEmpty() ? generated.Msg : "Не удалось создать временный config.json.");
                }

                var (success, output) = builder.Context.RunCoreType == ECoreType.sing_box
                    ? await RunCoreCheckAsync(ECoreType.sing_box, new[] { "check", "-c", tempPath }, tempPath)
                    : await RunCoreCheckAsync(ECoreType.Xray, new[] { "run", "-test", "-config", tempPath }, tempPath);
                if (!success)
                {
                    throw new InvalidDataException($"{builder.Context.RunCoreType} отклонил конфигурацию:{Environment.NewLine}{output}");
                }
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }

            _customJsonValidated = true;
            _validatedCustomJson = txtCustomJson.Text;
            rbCustom.IsChecked = true;
            SetJsonStatus("Проверка пройдена. Полный временный config.json принят выбранным ядром.", "SgSuccessBrush");
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            _customJsonValidated = false;
            _validatedCustomJson = null;
            Logging.SaveLog("SgDpiWindow.ValidateCustomJson", ex);
            SetJsonStatus(ex.Message, "SgErrorBrush");
        }
        finally
        {
            btnValidateJson.IsEnabled = _jsonEditing;
            btnApply.IsEnabled = _customJsonValidated;
        }
    }

    private async void DefaultJson_Click(object sender, RoutedEventArgs e)
    {
        _jsonEditing = true;
        rbCustom.IsChecked = true;
        txtCustomJson.IsReadOnly = false;
        SetJsonText(SgDpiModeHelper.GetDefaultCustomJson());
        _customJsonValidated = false;
        _validatedCustomJson = null;
        btnValidateJson.IsEnabled = true;
        SetJsonStatus("Комплектные параметры SG Client восстановлены в редакторе. Нажмите «Проверить», затем «Применить».", "SgWarningBrush");
        await RefreshStateAsync();
    }

    private void CustomJson_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressJsonChanged || !IsLoaded)
        {
            return;
        }
        _customJsonValidated = false;
        _validatedCustomJson = null;
        btnValidateJson.IsEnabled = _jsonEditing;
        btnApply.IsEnabled = false;
        if (_jsonEditing)
        {
            rbCustom.IsChecked = true;
            SetJsonStatus("JSON изменён. Предыдущая проверка аннулирована.", "SgWarningBrush");
        }
    }

    private void SetJsonText(string text)
    {
        _suppressJsonChanged = true;
        try
        {
            txtCustomJson.Text = text;
        }
        finally
        {
            _suppressJsonChanged = false;
        }
    }

    private void SetJsonStatus(string text, string brushKey)
    {
        txtCustomJsonStatus.Text = text;
        txtCustomJsonStatus.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
    }

    private static async Task<(bool Success, string Output)> RunCoreCheckAsync(
        ECoreType coreType,
        IEnumerable<string> arguments,
        string configPath)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var executable = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var message);
        if (coreInfo == null || executable.IsNullOrEmpty())
        {
            return (false, message.IsNotEmpty() ? message : $"Ядро {coreType} не найдено.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Utils.StartupPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var pair in coreInfo.Environment)
        {
            if (pair.Value != null)
            {
                startInfo.Environment[pair.Key] = string.Format(pair.Value, configPath);
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, $"Проверка {coreType} превысила 20 секунд.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { (await stdoutTask).Trim(), (await stderrTask).Trim() }
                .Where(value => value.IsNotEmpty()));
        return (process.ExitCode == 0, output.IsNotEmpty() ? output : $"Код завершения: {process.ExitCode}");
    }

    private static void CreateDpiSettingsBackup(string mode, string customJson)
    {
        var directory = Utils.GetBackupPath("dpi-settings");
        Directory.CreateDirectory(directory);
        var snapshot = new
        {
            CreatedAt = DateTimeOffset.Now,
            Mode = mode,
            CustomJson = customJson,
        };
        File.WriteAllText(
            Path.Combine(directory, $"dpi-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"),
            JsonUtils.Serialize(snapshot, true));
    }

    private async Task RollbackDpiSettingsAsync(string mode, string customJson)
    {
        _config.SgQuickSettingsItem.DpiMode = mode;
        _config.SgQuickSettingsItem.DpiCustomJson = customJson;
        SgDpiModeHelper.ApplyLegacyFragmentSettings(_config);
        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            throw new InvalidOperationException("Не удалось сохранить предыдущие настройки DPI при автоматическом откате.");
        }
        _savedMode = mode;
        rbAuto.IsChecked = mode == "auto";
        rbOff.IsChecked = mode == "off";
        rbTls.IsChecked = mode == "tls";
        rbNoise.IsChecked = mode == "tls_noise";
        rbCustom.IsChecked = mode == "custom";
        SetJsonText(SgDpiModeHelper.GetCustomJsonForMode(mode, customJson));
        _customJsonValidated = false;
        _validatedCustomJson = null;
        AppEvents.ReloadRequested.Publish();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("dpi") { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_isApplying)
        {
            DialogResult = _hasApplied;
            Close();
        }
    }

    private void SgDpiWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isApplying)
        {
            e.Cancel = true;
        }
    }
}
