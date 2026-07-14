using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceLib.Handler.Builder;

namespace v2rayN.Views;

public partial class SgExpertWindow
{
    private readonly Config _config;
    private ProfileItem? _profile;
    private DNSItem? _xrayDns;
    private DNSItem? _singDns;
    private bool _profileValidated;

    public SgExpertWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);

        _config = AppManager.Instance.Config;
        cmbCore.ItemsSource = new[] { "Xray", "sing-box" };
        cmbTransport.ItemsSource = Global.Networks;
        cmbXhttpMode.ItemsSource = Global.XhttpMode;
        chkXrayCustomDns.Checked += CustomDnsToggle_Changed;
        chkXrayCustomDns.Unchecked += CustomDnsToggle_Changed;
        chkSingCustomDns.Checked += CustomDnsToggle_Changed;
        chkSingCustomDns.Unchecked += CustomDnsToggle_Changed;

        Loaded += async (_, _) =>
        {
            await LoadProfileAsync();
            await LoadDnsAsync();
            LoadConnectionMode();
        };

        WindowsUtils.SetDarkBorder(this, _config.UiItem.CurrentTheme);
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
        new SgHelpWindow("expert") { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenFullProfileEditor_Click(object sender, RoutedEventArgs e)
    {
        new SgProfileEditorWindow { Owner = this }.ShowDialog();
        await LoadProfileAsync();
        LoadConnectionMode();
    }

    private void ProfileTab_Click(object sender, RoutedEventArgs e)
    {
        tabProfile.IsChecked = true;
        tabDns.IsChecked = false;
        tabConnection.IsChecked = false;
        profilePanel.Visibility = Visibility.Visible;
        dnsPanel.Visibility = Visibility.Collapsed;
        connectionPanel.Visibility = Visibility.Collapsed;
    }

    private void DnsTab_Click(object sender, RoutedEventArgs e)
    {
        tabProfile.IsChecked = false;
        tabDns.IsChecked = true;
        tabConnection.IsChecked = false;
        profilePanel.Visibility = Visibility.Collapsed;
        dnsPanel.Visibility = Visibility.Visible;
        connectionPanel.Visibility = Visibility.Collapsed;
    }

    private void ConnectionTab_Click(object sender, RoutedEventArgs e)
    {
        tabProfile.IsChecked = false;
        tabDns.IsChecked = false;
        tabConnection.IsChecked = true;
        profilePanel.Visibility = Visibility.Collapsed;
        dnsPanel.Visibility = Visibility.Collapsed;
        connectionPanel.Visibility = Visibility.Visible;
        LoadConnectionMode();
    }

    private async void ReloadProfile_Click(object sender, RoutedEventArgs e)
    {
        await LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        _profileValidated = false;

        if (AmneziaWgManager.Instance.GetSelectedProfile() != null)
        {
            _profile = null;
            txtProfileName.Text = "AmneziaWG";
            txtProfileMeta.Text = "Поля XHTTP и FinalMask к AmneziaWG не применяются.";
            txtProfileSource.Text = "AWG";
            SetProfileControls(false);
            SetStatus(txtProfileStatus, "Выберите профиль Xray/VLESS, VMess или Trojan.", "SgWarningBrush");
            return;
        }

        _profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (_profile == null)
        {
            txtProfileName.Text = "Профиль не выбран";
            txtProfileMeta.Text = "Импортируйте или выберите профиль.";
            txtProfileSource.Text = "НЕТ";
            SetProfileControls(false);
            SetStatus(txtProfileStatus, "Нет профиля для редактирования.", "SgWarningBrush");
            return;
        }

        txtProfileName.Text = _profile.Remarks;
        txtProfileMeta.Text =
            $"{_profile.ConfigType} · {_profile.Address}:{_profile.Port} · {_profile.GetNetwork()}";
        txtProfileSource.Text = _profile.Subid.IsNotEmpty() ? "ПОДПИСКА" : "ЛОКАЛЬНЫЙ";

        cmbCore.SelectedIndex = _profile.CoreType == ECoreType.sing_box ? 1 : 0;
        cmbTransport.Text = _profile.GetNetwork();

        var transport = _profile.GetTransportExtra();
        cmbXhttpMode.Text = transport.XhttpMode ?? Global.DefaultXhttpMode;
        txtXhttpExtra.Text = PrettyJson(transport.XhttpExtra);
        txtFinalmask.Text = PrettyJson(_profile.Finalmask);

        SetProfileControls(true);
        UpdateXhttpControls();
        SetStatus(txtProfileStatus, "Изменения ещё не проверялись.", "SgMutedBrush");
    }

    private void SetProfileControls(bool enabled)
    {
        cmbCore.IsEnabled = enabled;
        cmbTransport.IsEnabled = enabled;
        cmbXhttpMode.IsEnabled = enabled;
        txtXhttpExtra.IsEnabled = enabled;
        txtFinalmask.IsEnabled = enabled;
        btnSaveProfile.IsEnabled = enabled;
        btnCopyProfile.IsEnabled = enabled;
    }

    private void Transport_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _profileValidated = false;
        UpdateXhttpControls();
    }

    private void UpdateXhttpControls()
    {
        var isXhttp = string.Equals(
            cmbTransport.Text?.Trim(),
            nameof(ETransport.xhttp),
            StringComparison.OrdinalIgnoreCase);

        cmbXhttpMode.IsEnabled = _profile != null && isXhttp;
        txtXhttpExtra.IsEnabled = _profile != null && isXhttp;
    }

    private async void ValidateProfile_Click(object sender, RoutedEventArgs e)
    {
        await ValidateCurrentProfileAsync(showSuccess: true);
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        await SaveProfileAsync(asIndependentCopy: false);
    }

    private async void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        await SaveProfileAsync(asIndependentCopy: true);
    }

    private ProfileItem? BuildCandidateProfile()
    {
        if (_profile == null)
        {
            return null;
        }

        var candidate = JsonUtils.DeepCopy(_profile);
        if (candidate == null)
        {
            return null;
        }

        candidate.CoreType = cmbCore.SelectedIndex == 1
            ? ECoreType.sing_box
            : ECoreType.Xray;

        candidate.Network = cmbTransport.Text?.Trim() ?? Global.DefaultNetwork;

        var transport = candidate.GetTransportExtra();
        candidate.SetTransportExtra(transport with
        {
            XhttpMode = cmbXhttpMode.Text?.Trim().NullIfEmpty(),
            XhttpExtra = txtXhttpExtra.Text.Trim().NullIfEmpty(),
        });

        candidate.Finalmask = txtFinalmask.Text.Trim();
        return candidate;
    }

    private async Task<bool> ValidateCurrentProfileAsync(bool showSuccess)
    {
        _profileValidated = false;
        var candidate = BuildCandidateProfile();
        if (candidate == null)
        {
            SetStatus(txtProfileStatus, "Не удалось подготовить профиль.", "SgErrorBrush");
            return false;
        }

        if (!ValidateJsonObject(txtXhttpExtra.Text, "XHTTP Extra", out var xhttpError))
        {
            SetStatus(txtProfileStatus, xhttpError, "SgErrorBrush");
            return false;
        }

        if (!ValidateJsonObject(txtFinalmask.Text, "FinalMask", out var finalmaskError))
        {
            SetStatus(txtProfileStatus, finalmaskError, "SgErrorBrush");
            return false;
        }

        if (candidate.GetNetwork() == nameof(ETransport.xhttp)
            && candidate.CoreType == ECoreType.sing_box)
        {
            SetStatus(
                txtProfileStatus,
                "Транспорт XHTTP поддерживается Xray. Выберите ядро Xray.",
                "SgErrorBrush");
            return false;
        }

        SetStatus(txtProfileStatus, "Создание временного config.json…", "SgMutedBrush");

        var validationConfig = JsonUtils.DeepCopy(_config);
        if (validationConfig == null)
        {
            SetStatus(txtProfileStatus, "Не удалось создать копию настроек.", "SgErrorBrush");
            return false;
        }

        validationConfig.TunModeItem.EnableTun = false;
        var builder = await CoreConfigContextBuilder.Build(validationConfig, candidate);
        if (!builder.Success)
        {
            var errors = builder.ValidatorResult.Errors.Count > 0
                ? string.Join(Environment.NewLine, builder.ValidatorResult.Errors)
                : "Профиль не прошёл внутреннюю проверку.";
            SetStatus(txtProfileStatus, errors, "SgErrorBrush");
            return false;
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"sg-client-profile-check-{Guid.NewGuid():N}.json");

        try
        {
            var result = await CoreConfigHandler.GenerateClientConfig(
                builder.Context,
                tempPath);

            if (result.Success != true || !File.Exists(tempPath))
            {
                SetStatus(
                    txtProfileStatus,
                    result.Msg.IsNotEmpty()
                        ? result.Msg
                        : "Не удалось создать тестовый config.json.",
                    "SgErrorBrush");
                return false;
            }

            if (builder.Context.RunCoreType == ECoreType.Xray)
            {
                var (success, output) = await RunXrayConfigTestAsync(tempPath);
                if (!success)
                {
                    SetStatus(
                        txtProfileStatus,
                        $"Xray отклонил конфигурацию:{Environment.NewLine}{output}",
                        "SgErrorBrush");
                    return false;
                }
            }

            _profileValidated = true;
            if (showSuccess)
            {
                SetStatus(
                    txtProfileStatus,
                    "Проверка пройдена. Временный config.json принят ядром.",
                    "SgSuccessBrush");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgExpertWindow.ValidateProfile", ex);
            SetStatus(txtProfileStatus, ex.Message, "SgErrorBrush");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<(bool Success, string Output)> RunXrayConfigTestAsync(
        string configPath)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray);
        var executable = CoreInfoManager.Instance.GetCoreExecFile(
            coreInfo,
            out var message);

        if (coreInfo == null || executable.IsNullOrEmpty())
        {
            return (false, message.IsNotEmpty() ? message : "Xray не найден.");
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

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-test");
        startInfo.ArgumentList.Add("-config");
        startInfo.ArgumentList.Add(configPath);

        foreach (var pair in coreInfo.Environment)
        {
            if (pair.Value != null)
            {
                startInfo.Environment[pair.Key] = string.Format(
                    pair.Value,
                    configPath);
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
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            return (false, "Проверка Xray превысила 20 секунд.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Join(
            Environment.NewLine,
            new[] { stdout.Trim(), stderr.Trim() }.Where(value => value.IsNotEmpty()));

        return (
            process.ExitCode == 0,
            output.IsNotEmpty() ? output : $"Код завершения: {process.ExitCode}");
    }

    private async Task SaveProfileAsync(bool asIndependentCopy)
    {
        if (!await ValidateCurrentProfileAsync(showSuccess: false))
        {
            return;
        }

        var candidate = BuildCandidateProfile();
        if (candidate == null)
        {
            return;
        }

        if (!asIndependentCopy && _profile?.Subid.IsNotEmpty() == true)
        {
            var answer = MessageBox.Show(
                this,
                "Профиль принадлежит подписке и может быть заменён при обновлении.\n\n"
                    + "Сохранить локальное изменение в этом профиле?",
                "Профиль подписки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            if (asIndependentCopy)
            {
                candidate.IndexId = string.Empty;
                candidate.Subid = string.Empty;
                candidate.IsSub = false;
                candidate.Remarks = $"{candidate.Remarks} · локальная копия";
            }
            else
            {
                SaveProfileBackup(_profile!);
            }

            var result = await ConfigHandler.AddServer(_config, candidate);
            if (result != 0)
            {
                SetStatus(txtProfileStatus, "Не удалось сохранить профиль.", "SgErrorBrush");
                return;
            }

            AppEvents.ProfilesRefreshRequested.Publish();

            if (!asIndependentCopy && candidate.IndexId == _config.IndexId)
            {
                AppEvents.ReloadRequested.Publish();
            }

            SetStatus(
                txtProfileStatus,
                asIndependentCopy
                    ? "Независимая локальная копия создана."
                    : "Профиль сохранён. Активная конфигурация перезапускается.",
                "SgSuccessBrush");

            if (!asIndependentCopy)
            {
                _profile = candidate;
            }

            NoticeManager.Instance.Enqueue(
                asIndependentCopy
                    ? "Создана независимая копия профиля."
                    : "Экспертные параметры профиля сохранены.");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgExpertWindow.SaveProfile", ex);
            SetStatus(txtProfileStatus, ex.Message, "SgErrorBrush");
        }
    }

    private static void SaveProfileBackup(ProfileItem profile)
    {
        var directory = Path.Combine(
            Utils.GetConfigPath(),
            "profile-backups");
        Directory.CreateDirectory(directory);

        var safeName = Regex.Replace(
            profile.Remarks.IsNotEmpty() ? profile.Remarks : "profile",
            @"[^a-zA-Z0-9а-яА-ЯёЁ._-]+",
            "_");

        var fileName =
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeName}-{profile.IndexId}.json";
        File.WriteAllText(
            Path.Combine(directory, fileName),
            JsonUtils.Serialize(profile, true));
    }

    private static bool ValidateJsonObject(
        string text,
        string fieldName,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            var node = JsonNode.Parse(text);
            if (node is not JsonObject)
            {
                error = $"{fieldName}: ожидается JSON-объект {{ ... }}.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"{fieldName}: {ex.Message}";
            return false;
        }
    }

    private static string PrettyJson(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(text);
            return node?.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }) ?? text;
        }
        catch
        {
            return text;
        }
    }

    private async void ReloadDns_Click(object sender, RoutedEventArgs e)
    {
        await LoadDnsAsync();
    }

    private async Task LoadDnsAsync()
    {
        var simple = _config.SimpleDNSItem;
        rbDnsViaVpn.IsChecked = _config.SgQuickSettingsItem.DnsThroughTun;
        rbDnsDirect.IsChecked = !_config.SgQuickSettingsItem.DnsThroughTun;

        txtDirectDns.Text = simple.DirectDNS ?? string.Empty;
        txtRemoteDns.Text = simple.RemoteDNS ?? string.Empty;
        txtBootstrapDns.Text = simple.BootstrapDNS ?? string.Empty;
        chkUseSystemHosts.IsChecked = simple.UseSystemHosts == true;
        chkFakeIp.IsChecked = simple.FakeIP == true;
        chkParallelQuery.IsChecked = simple.ParallelQuery == true;

        _xrayDns = await AppManager.Instance.GetDNSItem(ECoreType.Xray);
        _singDns = await AppManager.Instance.GetDNSItem(ECoreType.sing_box);

        var xrayNormal = EffectiveDnsText(
            _xrayDns.NormalDNS,
            Global.DNSV2rayNormalFileName);
        var xrayTun = EffectiveDnsText(
            _xrayDns.TunDNS,
            Global.DNSV2rayNormalFileName);
        var singNormal = EffectiveDnsText(
            _singDns.NormalDNS,
            Global.DNSSingboxNormalFileName);
        var singTun = EffectiveDnsText(
            _singDns.TunDNS,
            Global.TunSingboxDNSFileName);

        chkXrayCustomDns.IsChecked = _xrayDns.Enabled;
        txtXrayNormalDns.Text = PrettyJson(xrayNormal);
        txtXrayTunDns.Text = PrettyJson(xrayTun);

        chkSingCustomDns.IsChecked = _singDns.Enabled;
        txtSingNormalDns.Text = PrettyJson(singNormal);
        txtSingTunDns.Text = PrettyJson(singTun);

        UpdateDnsEditorState();
        txtDnsEffectiveSummary.Text =
            $"Маршрут DNS: {(_config.SgQuickSettingsItem.DnsThroughTun ? "через VPN" : "напрямую")} · "
            + $"прямой: {DisplayDnsValue(simple.DirectDNS)} · "
            + $"удалённый: {DisplayDnsValue(simple.RemoteDNS)} · "
            + $"bootstrap: {DisplayDnsValue(simple.BootstrapDNS)}";
        SetStatus(
            txtDnsStatus,
            "Показана фактически действующая DNS-конфигурация. "
                + "Пока полный JSON выключен, поля доступны только для чтения.",
            "SgAccentBrush");
    }

    private static string EffectiveDnsText(string? configured, string embeddedName)
    {
        return configured.IsNotEmpty()
            ? configured!
            : EmbedUtils.GetEmbedText(embeddedName);
    }

    private static string DisplayDnsValue(string? value)
    {
        return value.IsNotEmpty() ? value! : "не задан";
    }

    private void CustomDnsToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDnsEditorState();
    }

    private void UpdateDnsEditorState()
    {
        var xrayEditable = chkXrayCustomDns.IsChecked == true;
        txtXrayNormalDns.IsReadOnly = !xrayEditable;
        txtXrayTunDns.IsReadOnly = !xrayEditable;
        txtXrayNormalDns.Opacity = xrayEditable ? 1.0 : 0.82;
        txtXrayTunDns.Opacity = xrayEditable ? 1.0 : 0.82;
        txtXrayDnsSource.Text = xrayEditable
            ? "Источник: пользовательский полный JSON · имеет приоритет"
            : "Источник: встроенный шаблон или сохранённая эффективная конфигурация · только чтение";

        var singEditable = chkSingCustomDns.IsChecked == true;
        txtSingNormalDns.IsReadOnly = !singEditable;
        txtSingTunDns.IsReadOnly = !singEditable;
        txtSingNormalDns.Opacity = singEditable ? 1.0 : 0.82;
        txtSingTunDns.Opacity = singEditable ? 1.0 : 0.82;
        txtSingDnsSource.Text = singEditable
            ? "Источник: пользовательский полный JSON · имеет приоритет"
            : "Источник: встроенный шаблон или сохранённая эффективная конфигурация · только чтение";
    }

    private void ValidateDns_Click(object sender, RoutedEventArgs e)
    {
        ValidateDns(showSuccess: true);
    }

    private bool ValidateDns(bool showSuccess)
    {
        if (txtDirectDns.Text.Trim().IsNullOrEmpty()
            || txtRemoteDns.Text.Trim().IsNullOrEmpty()
            || txtBootstrapDns.Text.Trim().IsNullOrEmpty())
        {
            SetStatus(
                txtDnsStatus,
                "Заполните прямой, удалённый и bootstrap DNS.",
                "SgErrorBrush");
            return false;
        }

        if (chkXrayCustomDns.IsChecked == true)
        {
            if (!ValidateXrayDnsJson(txtXrayNormalDns.Text, "Xray · обычный режим", out var error)
                || !ValidateXrayDnsJson(txtXrayTunDns.Text, "Xray · TUN", out error))
            {
                SetStatus(txtDnsStatus, error, "SgErrorBrush");
                return false;
            }
        }

        if (chkSingCustomDns.IsChecked == true)
        {
            if (!ValidateSingDnsJson(txtSingNormalDns.Text, "sing-box · обычный режим", out var error)
                || !ValidateSingDnsJson(txtSingTunDns.Text, "sing-box · TUN", out error))
            {
                SetStatus(txtDnsStatus, error, "SgErrorBrush");
                return false;
            }
        }

        if (showSuccess)
        {
            SetStatus(
                txtDnsStatus,
                $"DNS прошёл синтаксическую проверку · {DateTime.Now:dd.MM.yyyy HH:mm}.",
                "SgSuccessBrush");
        }
        return true;
    }

    private static bool ValidateXrayDnsJson(
        string text,
        string title,
        out string error)
    {
        error = string.Empty;
        if (text.IsNullOrEmpty())
        {
            error = $"{title}: JSON не заполнен.";
            return false;
        }

        try
        {
            var node = JsonNode.Parse(text) as JsonObject;
            if (node == null || node["servers"] == null)
            {
                error = $"{title}: требуется объект с полем servers.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"{title}: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateSingDnsJson(
        string text,
        string title,
        out string error)
    {
        error = string.Empty;
        var dns = JsonUtils.Deserialize<Dns4Sbox>(text);
        if (dns == null || dns.servers.Count == 0)
        {
            error = $"{title}: требуется корректный объект DNS со списком servers.";
            return false;
        }

        if (dns.servers.Any(server => server.type.IsNullOrEmpty()))
        {
            error = $"{title}: у каждого DNS-сервера должно быть поле type.";
            return false;
        }

        return true;
    }

    private async void SaveDns_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateDns(showSuccess: false))
        {
            return;
        }

        try
        {
            SaveDnsBackup();

            _config.SgQuickSettingsItem.DnsThroughTun =
                rbDnsViaVpn.IsChecked == true;

            _config.SimpleDNSItem.DirectDNS = txtDirectDns.Text.Trim();
            _config.SimpleDNSItem.RemoteDNS = txtRemoteDns.Text.Trim();
            _config.SimpleDNSItem.BootstrapDNS = txtBootstrapDns.Text.Trim();
            _config.SimpleDNSItem.UseSystemHosts =
                chkUseSystemHosts.IsChecked == true;
            _config.SimpleDNSItem.FakeIP = chkFakeIp.IsChecked == true;
            _config.SimpleDNSItem.ParallelQuery =
                chkParallelQuery.IsChecked == true;

            _xrayDns ??= await AppManager.Instance.GetDNSItem(ECoreType.Xray);
            _xrayDns.Enabled = chkXrayCustomDns.IsChecked == true;
            _xrayDns.NormalDNS = txtXrayNormalDns.Text.Trim();
            _xrayDns.TunDNS = txtXrayTunDns.Text.Trim();
            await ConfigHandler.SaveDNSItems(_config, _xrayDns);

            _singDns ??= await AppManager.Instance.GetDNSItem(ECoreType.sing_box);
            _singDns.Enabled = chkSingCustomDns.IsChecked == true;
            _singDns.NormalDNS = NormalizeSingDns(txtSingNormalDns.Text);
            _singDns.TunDNS = NormalizeSingDns(txtSingTunDns.Text);
            await ConfigHandler.SaveDNSItems(_config, _singDns);

            await ConfigHandler.SaveConfig(_config);
            AppEvents.ReloadRequested.Publish();

            SetStatus(
                txtDnsStatus,
                "DNS сохранён. Активное ядро перезапускается.",
                "SgSuccessBrush");
            NoticeManager.Instance.Enqueue("Ручные настройки DNS сохранены.");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgExpertWindow.SaveDns", ex);
            SetStatus(txtDnsStatus, ex.Message, "SgErrorBrush");
        }
    }

    private static string NormalizeSingDns(string text)
    {
        var dns = JsonUtils.Deserialize<Dns4Sbox>(text);
        return dns == null ? text.Trim() : JsonUtils.Serialize(dns);
    }

    private void SaveDnsBackup()
    {
        var directory = Path.Combine(Utils.GetConfigPath(), "dns-backups");
        Directory.CreateDirectory(directory);

        var snapshot = new
        {
            CreatedAt = DateTimeOffset.Now,
            Simple = _config.SimpleDNSItem,
            DnsThroughTun = _config.SgQuickSettingsItem.DnsThroughTun,
            Xray = _xrayDns,
            SingBox = _singDns,
        };

        File.WriteAllText(
            Path.Combine(directory, $"dns-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
            JsonUtils.Serialize(snapshot, true));
    }

    private void LoadConnectionMode()
    {
        var mode = StatusBarViewModel.Instance.GetConnectionModeKey();
        var localActive = mode == "local-proxy"
            && CoreManager.Instance.IsCoreRunning;

        btnLocalProxyAction.Content = localActive
            ? "Отключить локальный прокси"
            : "Запустить локальный прокси";
        btnLocalProxyAction.Background = (Brush)FindResource(
            localActive ? "SgLocalProxySoftBrush" : "SgAccentBrush");
        btnLocalProxyAction.Foreground = (Brush)FindResource(
            localActive ? "SgLocalProxyBrush" : "SgOnButtonTextBrush");
        btnLocalProxyAction.BorderBrush = (Brush)FindResource(
            localActive ? "SgLocalProxyBorderBrush" : "SgAccentBrush");

        SetStatus(
            txtConnectionStatus,
            localActive
                ? "Локальный прокси работает. Системные настройки Windows не изменены."
                : mode switch
                {
                    "tun" => "Сейчас работает TUN. Переключить TUN или системный прокси можно на главном экране.",
                    "system-proxy" => "Сейчас работает системный прокси Windows. Переключить режим можно на главном экране.",
                    _ => "Локальный прокси выключен.",
                },
            localActive ? "SgLocalProxyBrush" : "SgMutedBrush");
    }

    private async void ToggleLocalProxy_Click(object sender, RoutedEventArgs e)
    {
        var localActive = StatusBarViewModel.Instance.GetConnectionModeKey()
            == "local-proxy"
            && CoreManager.Instance.IsCoreRunning;

        btnLocalProxyAction.IsEnabled = false;
        try
        {
            SetStatus(
                txtConnectionStatus,
                localActive
                    ? "Остановка локального прокси…"
                    : "Запуск локального HTTP/SOCKS-порта…",
                "SgMutedBrush");
            await StatusBarViewModel.Instance.ApplyConnectionModeAsync(
                localActive ? "off" : "local-proxy");
            LoadConnectionMode();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgExpertWindow.ToggleLocalProxy", ex);
            SetStatus(txtConnectionStatus, ex.Message, "SgErrorBrush");
        }
        finally
        {
            btnLocalProxyAction.IsEnabled = true;
        }
    }

    private void SetStatus(TextBlock target, string text, string brushKey)
    {
        target.Text = text;
        target.Foreground = (Brush)FindResource(brushKey);
    }
}
