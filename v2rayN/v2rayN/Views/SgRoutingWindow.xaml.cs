using System.Net;
using System.Text.Json;
using System.Windows.Controls;
using ServiceLib.Helper;

namespace v2rayN.Views;

public partial class SgRoutingWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private readonly SgSmartRoutingItem _working;
    private bool _loading;

    public SgRoutingWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        ApplyAdaptiveWindowSize();
        _config.SgQuickSettingsItem ??= new SgQuickSettingsItem();
        var source = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        _working = JsonUtils.DeepCopy(source) ?? new SgSmartRoutingItem();

        _loading = true;
        LoadWorkingToControls();
        _loading = false;
        RefreshSelectedPreset();

        RefreshVersion();
        RefreshWarning();
        RefreshEngineSupport();
        Loaded += SgRoutingWindow_Loaded;
    }

    private void SgRoutingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSelectedPreset();
        RefreshDiagnostics();
    }

    private void ApplyAdaptiveWindowSize()
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(560d, workArea.Width - 64d);
        var availableHeight = Math.Max(440d, workArea.Height - 64d);

        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private void RefreshSelectedPreset()
    {
        txtSelectedPreset.Text = PresetCaption(GetCheckedTag(presetOptions, SgSmartRoutingHelper.PresetCustom));
    }

    private static string PresetCaption(string preset)
    {
        return preset switch
        {
            SgSmartRoutingHelper.PresetGlobal => "Весь интернет через VPN",
            SgSmartRoutingHelper.PresetRussiaDirect => "Россия напрямую, остальное через VPN",
            SgSmartRoutingHelper.PresetBlockedOnly => "Только заблокированное через VPN",
            _ => "Пользовательская схема",
        };
    }

    private void LoadWorkingToControls()
    {
        SelectRadio(presetOptions, _working.Preset);
        SelectRadio(localActions, _working.LocalNetworkAction);
        SelectRadio(russiaScopeOptions, _working.RussiaScope);
        SelectRadio(russiaActions, _working.RussiaAction);
        SelectRadio(blockedActions, _working.BlockedAction);
        SelectRadio(adsActions, _working.AdsAction);
        SelectRadio(defaultActions, _working.DefaultAction);

        txtDirectDomains.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomDirectDomains);
        txtProxyDomains.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomProxyDomains);
        txtBlockDomains.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomBlockDomains);
        txtDirectIps.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomDirectIps);
        txtProxyIps.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomProxyIps);
        txtBlockIps.Text = SgSmartRoutingHelper.ToMultiline(_working.CustomBlockIps);
        RefreshRussiaScopeState();
    }

    private void ReadControlsToWorking()
    {
        _working.Preset = GetCheckedTag(presetOptions, SgSmartRoutingHelper.PresetCustom);
        _working.LocalNetworkAction = GetCheckedTag(localActions, SgSmartRoutingHelper.ActionDirect);
        _working.DefaultAction = GetCheckedTag(defaultActions, SgSmartRoutingHelper.ActionProxy);
        _working.RussiaScope = GetCheckedTag(russiaScopeOptions, SgSmartRoutingHelper.RussiaScopeNone);
        _working.RussiaAction = GetCheckedTag(russiaActions, _working.DefaultAction);
        _working.BlockedAction = GetCheckedTag(blockedActions, _working.DefaultAction);
        _working.AdsAction = GetCheckedTag(adsActions, _working.DefaultAction);
        _working.CustomDirectDomains = SgSmartRoutingHelper.ParseMultiline(txtDirectDomains.Text, true);
        _working.CustomProxyDomains = SgSmartRoutingHelper.ParseMultiline(txtProxyDomains.Text, true);
        _working.CustomBlockDomains = SgSmartRoutingHelper.ParseMultiline(txtBlockDomains.Text, true);
        _working.CustomDirectIps = SgSmartRoutingHelper.ParseMultiline(txtDirectIps.Text, false);
        _working.CustomProxyIps = SgSmartRoutingHelper.ParseMultiline(txtProxyIps.Text, false);
        _working.CustomBlockIps = SgSmartRoutingHelper.ParseMultiline(txtBlockIps.Text, false);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PresetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ReadControlsToWorking();
        var preset = GetCheckedTag(presetOptions, SgSmartRoutingHelper.PresetGlobal);
        if (preset != SgSmartRoutingHelper.PresetCustom)
        {
            _loading = true;
            SgSmartRoutingHelper.ApplyPreset(_working, preset, preserveCustomLists: true);
            LoadWorkingToControls();
            _loading = false;
        }

        RefreshSelectedPreset();
        RefreshWarning();
        RefreshSelectedConfiguration();
    }

    private void RussiaScopeRadio_Checked(object sender, RoutedEventArgs e)
    {
        RefreshRussiaScopeState();
        if (_loading)
        {
            return;
        }

        MarkCustom();
        RefreshWarning();
        RefreshSelectedConfiguration();
    }

    private void RefreshRussiaScopeState()
    {
        if (russiaScopeOptions == null || russiaActions == null || txtRussiaSyntax == null)
        {
            return;
        }

        var scope = GetCheckedTag(russiaScopeOptions, SgSmartRoutingHelper.RussiaScopeNone);
        russiaActions.IsEnabled = scope != SgSmartRoutingHelper.RussiaScopeNone;
        txtRussiaSyntax.Text = scope switch
        {
            SgSmartRoutingHelper.RussiaScopeTld => "Создаётся: geosite:tld-ru",
            SgSmartRoutingHelper.RussiaScopeSitesAndIp => "Создаются: geosite:category-ru и geoip:ru",
            _ => "Российское правило выключено",
        };
    }

    private void RuleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        MarkCustom();
        RefreshWarning();
        RefreshSelectedConfiguration();
    }

    private void CustomRules_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            MarkCustom();
            RefreshSelectedConfiguration();
        }
    }

    private void RefreshSelectedConfiguration()
    {
        ReadControlsToWorking();
        RefreshDiagnostics();
    }

    private void MarkCustom()
    {
        _loading = true;
        SelectRadio(presetOptions, SgSmartRoutingHelper.PresetCustom);
        _loading = false;
        _working.Preset = SgSmartRoutingHelper.PresetCustom;
        RefreshSelectedPreset();
    }

    private void RoutingTab_Click(object sender, RoutedEventArgs e)
    {
        tabRouting.IsChecked = true;
        tabGeoFiles.IsChecked = false;
        routingPanel.Visibility = Visibility.Visible;
        geoFilesPanel.Visibility = Visibility.Collapsed;
        routingFooter.Visibility = Visibility.Visible;
    }

    private async void GeoFilesTab_Click(object sender, RoutedEventArgs e)
    {
        tabRouting.IsChecked = false;
        tabGeoFiles.IsChecked = true;
        routingPanel.Visibility = Visibility.Collapsed;
        geoFilesPanel.Visibility = Visibility.Visible;
        routingFooter.Visibility = Visibility.Collapsed;
        await geoFilesPanel.RefreshAsync();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        await UpdateRulesAsync(true);
    }

    private async Task<bool> UpdateRulesAsync(bool force)
    {
        btnUpdate.IsEnabled = false;
        btnSave.IsEnabled = false;
        txtProgress.Text = "Подготовка обновления…";
        var progress = new Progress<string>(text => txtProgress.Text = text);
        var result = await SgRussiaRulesManager.Instance.EnsureRulesAsync(_config, force, progress);
        btnUpdate.IsEnabled = true;
        btnSave.IsEnabled = true;
        txtProgress.Text = result.Message;
        RefreshVersion();
        if (!result.Success)
        {
            SetInlineStatus(result.Message, "SgErrorBrush");
        }
        return result.Success;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ReadControlsToWorking();
        _working.MigratedFromLegacyPreset = true;
        _working.RussiaScopeMigrated = true;
        btnSave.IsEnabled = false;
        SetInlineStatus("Сохраняю правила маршрутизации…", "SgWarningBrush");

        try
        {
            if (SgSmartRoutingHelper.RequiresCommunityRules(_working))
            {
                var progress = new Progress<string>(text => SetInlineStatus(text, "SgWarningBrush"));
                var validation = await SgRussiaRulesManager.Instance.ValidateRequiredCategoriesAsync(_working, progress);
                if (!validation.Success)
                {
                    SetInlineStatus(validation.Message, "SgErrorBrush");
                    return;
                }

                SetInlineStatus(validation.Message, "SgAccentBrush");
                SgRussiaRulesManager.ApplySources(_config);
            }

            _config.SgQuickSettingsItem.SmartRouting = JsonUtils.DeepCopy(_working) ?? _working;
            _config.SgQuickSettingsItem.RoutingMode = _working.Preset;
            SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
            if (await ConfigHandler.SaveConfig(_config) != 0)
            {
                SetInlineStatus("Не удалось сохранить правила маршрутизации.", "SgErrorBrush");
                return;
            }

            StatusBarViewModel.Instance.RefreshSgQuickSummaries();
            RefreshSelectedPreset();
            RefreshWarning();

            // SG_ROUTING_RELOAD_ACTIVE_MODE_078
            // Routing is authoritative in TUN, System Proxy and Local Proxy.
            // Saving while a proxy mode is active must rebuild the running
            // config.json immediately; otherwise the UI shows the new scheme
            // while Xray/sing-box continues using the previous one.
            var activeMode = StatusBarViewModel.Instance.GetConnectionModeKey();
            if (activeMode is "tun" or "system-proxy" or "local-proxy")
            {
                var modeTitle = ConnectionModeTitle(activeMode);
                SetInlineStatus($"Настройки сохранены. Перезапускаю {modeTitle} и проверяю рабочий config.json…", "SgWarningBrush");

                if (MainWindowViewModel.Instance != null)
                {
                    await MainWindowViewModel.Instance.Reload();
                }
                else
                {
                    AppEvents.ReloadRequested.Publish();
                    await Task.Delay(1800);
                }

                RefreshDiagnostics();
                var verification = VerifyGeneratedDefaultRoute(_working, activeMode);
                if (!verification.Success)
                {
                    SetInlineStatus($"Настройки сохранены, но рабочая маршрутизация не подтверждена: {verification.Message}", "SgErrorBrush");
                    return;
                }

                SetInlineStatus($"Применено к активному режиму {modeTitle}. {verification.Message}", "SgAccentBrush");
            }
            else
            {
                RefreshDiagnostics();
                SetInlineStatus("Сохранено. Правила будут применены при следующем включении TUN, System Proxy или Local Proxy.", "SgAccentBrush");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Apply SG smart routing", ex);
            SetInlineStatus($"Не удалось применить маршрутизацию: {ex.Message}", "SgErrorBrush");
        }
        finally
        {
            btnSave.IsEnabled = true;
        }
    }

    private void SetInlineStatus(string text, string brushKey)
    {
        txtFooterStatus.Text = text;
        txtFooterStatus.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
        txtProgress.Text = text;
        txtProgress.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
    }


    private static string ConnectionModeTitle(string mode)
    {
        return mode switch
        {
            "tun" => "TUN",
            "system-proxy" => "System Proxy",
            "local-proxy" => "Local Proxy",
            _ => "подключение",
        };
    }

    private static (bool Success, string Message) VerifyGeneratedDefaultRoute(SgSmartRoutingItem item, string activeMode)
    {
        if (AmneziaWgManager.Instance.GetSelectedProfile() != null)
        {
            return (true, "AmneziaWG переподключён; для него действует поддерживаемый набор IP/подсетей.");
        }

        var path = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        if (!File.Exists(path))
        {
            return (false, "рабочий config.json не найден");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var expected = SgSmartRoutingHelper.ToOutboundTag(item.DefaultAction);

            if (root.TryGetProperty("route", out var singboxRoute))
            {
                var actual = ReadString(singboxRoute, "final") ?? "не указан";
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    ? (true, $"Проверено: финальный маршрут → {actual}.")
                    : (false, $"в config.json final={actual}, ожидалось {expected}");
            }

            if (root.TryGetProperty("routing", out var xrayRouting)
                && xrayRouting.TryGetProperty("rules", out var rules)
                && rules.ValueKind == JsonValueKind.Array)
            {
                string? actual = null;
                var expectedInbound = activeMode == "tun" ? "tun" : nameof(EInboundProtocol.socks);
                foreach (var rule in rules.EnumerateArray())
                {
                    if (!IsXrayCatchAllRule(rule))
                    {
                        continue;
                    }

                    var inboundTags = ReadStringArray(rule, "inboundTag");
                    var appliesToActiveInbound = inboundTags.Count == 0
                        || inboundTags.Contains(expectedInbound, StringComparer.OrdinalIgnoreCase);
                    if (!appliesToActiveInbound)
                    {
                        continue;
                    }

                    // Xray uses the first matching rule. Select the first
                    // catch-all that can actually receive the active inbound.
                    actual = ReadString(rule, "outboundTag") ?? ReadString(rule, "balancerTag");
                    break;
                }

                if (actual.IsNullOrEmpty())
                {
                    return (false, "в Xray config.json не найдено явное финальное правило");
                }

                var matches = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    || (expected == Global.ProxyTag
                        && actual!.StartsWith(Global.ProxyTag, StringComparison.OrdinalIgnoreCase));
                return matches
                    ? (true, $"Проверено: финальный маршрут → {actual}.")
                    : (false, $"в config.json финальный маршрут → {actual}, ожидалось {expected}");
            }

            return (false, "в config.json не найден раздел routing/route");
        }
        catch (Exception ex)
        {
            return (false, $"не удалось прочитать config.json: {ex.Message}");
        }
    }

    private static bool IsXrayCatchAllRule(JsonElement rule)
    {
        static bool HasValues(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value))
            {
                return false;
            }
            return value.ValueKind switch
            {
                JsonValueKind.Array => value.GetArrayLength() > 0,
                JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
                _ => false,
            };
        }

        return !HasValues(rule, "domain")
            && !HasValues(rule, "ip")
            && !HasValues(rule, "port")
            && !HasValues(rule, "process")
            && !HasValues(rule, "protocol");
    }

    private void RefreshVersion()
    {
        txtVersion.Text = _config.SgQuickSettingsItem.RussiaRulesVersion.IsNotEmpty()
            ? $"Версия: {_config.SgQuickSettingsItem.RussiaRulesVersion}"
            : SgRussiaRulesManager.Instance.HasUsableRules() ? "Локальные наборы найдены" : "Не загружены";
        txtIntegrity.Text = SgRussiaRulesManager.Instance.GetIntegritySummary();
    }

    private void RefreshWarning()
    {
        var direct = (GetCheckedTag(russiaScopeOptions, SgSmartRoutingHelper.RussiaScopeNone) != SgSmartRoutingHelper.RussiaScopeNone
                && GetCheckedTag(russiaActions, SgSmartRoutingHelper.ActionProxy) == SgSmartRoutingHelper.ActionDirect)
            || GetCheckedTag(blockedActions, SgSmartRoutingHelper.ActionProxy) == SgSmartRoutingHelper.ActionDirect
            || GetCheckedTag(adsActions, SgSmartRoutingHelper.ActionProxy) == SgSmartRoutingHelper.ActionDirect
            || GetCheckedTag(defaultActions, SgSmartRoutingHelper.ActionProxy) == SgSmartRoutingHelper.ActionDirect
            || txtDirectDomains.Text.IsNotEmpty()
            || txtDirectIps.Text.IsNotEmpty();
        warningCard.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
        txtWarning.Text = direct
            ? "Прямой трафик видит ваш реальный IP. Такой режим предназначен прежде всего для пользователей, находящихся в России."
            : string.Empty;
    }

    private void RefreshEngineSupport()
    {
        txtEngineSupport.Text = AmneziaWgManager.Instance.SelectedProfileId.IsNotEmpty()
            ? "AmneziaWG: в 043 работают общий маршрут и пользовательские IP/подсети «напрямую»/«через VPN». Домены, категории и блокировка появятся после общего DNS/TUN-модуля."
            : "Xray и sing-box: категории и пользовательские правила компилируются полностью. AmneziaWG: текущий этап поддерживает только направление пользовательских IP/подсетей.";
    }

    private void RefreshDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        ReadControlsToWorking();
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        try
        {
            var planned = DescribePlannedRules(_working);
            var actual = InspectGeneratedRouting(_working);
            tbDiagnostic.Text = $"Выбрано сейчас:\n{planned}\n\nФактически в последней конфигурации:\n{actual}";
        }
        catch (Exception ex)
        {
            tbDiagnostic.Text = $"Диагностика не выполнена: {ex.Message}";
        }
    }

    private static string DescribePlannedRules(SgSmartRoutingItem item)
    {
        static string Title(string action) => SgSmartRoutingHelper.NormalizeAction(action) switch
        {
            SgSmartRoutingHelper.ActionDirect => "Direct",
            SgSmartRoutingHelper.ActionBlock => "Block",
            _ => "VPN",
        };

        var customCount = item.CustomDirectDomains.Count + item.CustomDirectIps.Count
            + item.CustomProxyDomains.Count + item.CustomProxyIps.Count
            + item.CustomBlockDomains.Count + item.CustomBlockIps.Count;
        var russia = SgSmartRoutingHelper.NormalizeRussiaScope(item.RussiaScope) == SgSmartRoutingHelper.RussiaScopeNone
            ? "выключено"
            : $"{SgSmartRoutingHelper.GetRussiaScopeTitle(item.RussiaScope)} → {Title(item.RussiaAction)}";
        return $"Локальная сеть: {Title(item.LocalNetworkAction)}; Россия: {russia}; "
            + $"блокировки: {Title(item.BlockedAction)}; реклама: {Title(item.AdsAction)}; "
            + $"остальное: {Title(item.DefaultAction)}; пользовательских записей: {customCount}.";
    }

    private static string InspectGeneratedRouting(SgSmartRoutingItem item)
    {
        var awg = AmneziaWgManager.Instance.GetSelectedProfile();
        if (awg != null)
        {
            var runtimePath = Path.Combine(AmneziaWgManager.Instance.RuntimeDirectory, awg.ConfigFileName);
            if (!File.Exists(runtimePath))
            {
                return "AmneziaWG runtime-конфигурация ещё не создана. Подключите профиль и обновите диагностику.";
            }

            var allowed = File.ReadLines(runtimePath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase) && line.Contains('='));
            var value = allowed?.Split('=', 2)[1].Trim() ?? "не найдено";
            return $"AmneziaWG · файл изменён {File.GetLastWriteTime(runtimePath):dd.MM.yyyy HH:mm:ss}\nAllowedIPs: {value}";
        }

        var path = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        if (!File.Exists(path))
        {
            return "config.json ещё не создан. Включите TUN, System Proxy или Local Proxy, затем обновите диагностику.";
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var stamp = File.GetLastWriteTime(path).ToString("dd.MM.yyyy HH:mm:ss");
        if (root.TryGetProperty("routing", out var xrayRouting))
        {
            return InspectXrayRouting(xrayRouting, stamp, item);
        }
        if (root.TryGetProperty("route", out var singboxRoute))
        {
            return InspectSingboxRouting(singboxRoute, stamp, item);
        }
        return $"Файл изменён {stamp}, но раздел routing/route не найден.";
    }

    private static string InspectXrayRouting(JsonElement routing, string stamp, SgSmartRoutingItem item)
    {
        var findings = new List<string>();
        var observed = new List<(int Index, List<string> Values, string Target)>();
        var strategy = routing.TryGetProperty("domainStrategy", out var strategyValue)
            ? strategyValue.GetString() ?? "не указана"
            : "не указана";
        int? categoryIndex = null;
        if (routing.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var rule in rules.EnumerateArray())
            {
                var domains = ReadStringArray(rule, "domain");
                var ips = ReadStringArray(rule, "ip");
                var inbound = ReadStringArray(rule, "inboundTag");
                var target = ReadString(rule, "outboundTag") ?? ReadString(rule, "balancerTag") ?? "не указан";
                var values = domains.Concat(ips).ToList();
                observed.Add((index, values, target));
                if (categoryIndex is null && values.Any(IsDownloadedCategoryValue))
                {
                    categoryIndex = index;
                }

                // SG_XRAY_PROXY_CATCH_ALL_DIAGNOSTICS_078
                var catchAll = values.Count == 0 && IsXrayCatchAllRule(rule);
                var relevant = values.Any(IsSgRoutingValue) || catchAll;
                if (relevant)
                {
                    var finalTitle = inbound.Contains("tun", StringComparer.OrdinalIgnoreCase)
                        ? "остальной TUN-трафик"
                        : inbound.Any(tag => tag.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
                            ? "остальной трафик Local/System Proxy"
                            : "остальной трафик";
                    findings.Add(values.Count > 0
                        ? $"{string.Join(", ", values)} => {target}"
                        : $"{finalTitle} => {target}");
                }
                index++;
            }
        }

        var customCheck = VerifyCustomRules(item, observed, categoryIndex, singbox: false);
        return $"Xray · файл изменён {stamp} · domainStrategy={strategy}\n"
            + (findings.Count == 0 ? "Правила SG Client не найдены." : string.Join("\n", findings.Take(24)))
            + $"\n\nПользовательские правила:\n{customCheck}";
    }

    private static string InspectSingboxRouting(JsonElement route, string stamp, SgSmartRoutingItem item)
    {
        var findings = new List<string>();
        var observed = new List<(int Index, List<string> Values, string Target)>();
        var final = ReadString(route, "final") ?? "не указан";
        int? categoryIndex = null;
        if (route.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var rule in rules.EnumerateArray())
            {
                var values = new List<string>();
                foreach (var property in new[] { "rule_set", "domain", "domain_suffix", "domain_keyword", "domain_regex", "ip_cidr", "geosite", "geoip" })
                {
                    values.AddRange(ReadStringArray(rule, property));
                }
                if (rule.TryGetProperty("ip_is_private", out var privateValue) && privateValue.ValueKind == JsonValueKind.True)
                {
                    values.Add("geoip:private");
                }

                var target = ReadString(rule, "outbound") ?? ReadString(rule, "action") ?? "не указан";
                observed.Add((index, values, target));
                if (categoryIndex is null && values.Any(IsDownloadedCategoryValue))
                {
                    categoryIndex = index;
                }
                if (values.Any(IsSgRoutingValue))
                {
                    findings.Add($"{string.Join(", ", values)} => {target}");
                }
                index++;
            }
        }

        var localSets = new List<string>();
        if (route.TryGetProperty("rule_set", out var ruleSets) && ruleSets.ValueKind == JsonValueKind.Array)
        {
            foreach (var set in ruleSets.EnumerateArray())
            {
                var tag = ReadString(set, "tag");
                if (tag.IsNotEmpty() && IsSgRoutingValue(tag!))
                {
                    localSets.Add(tag!);
                }
            }
        }

        var customCheck = VerifyCustomRules(item, observed, categoryIndex, singbox: true);
        var setText = localSets.Count > 0 ? $"\nrule-set: {string.Join(", ", localSets.Distinct(StringComparer.OrdinalIgnoreCase))}" : string.Empty;
        return $"sing-box · файл изменён {stamp} · final={final}{setText}\n"
            + (findings.Count == 0 ? "Правила SG Client не найдены." : string.Join("\n", findings.Take(24)))
            + $"\n\nПользовательские правила:\n{customCheck}";
    }

    private static string VerifyCustomRules(
        SgSmartRoutingItem item,
        List<(int Index, List<string> Values, string Target)> observed,
        int? categoryIndex,
        bool singbox)
    {
        var expected = new List<(string Value, string Action)>();
        expected.AddRange(item.CustomBlockDomains.Select(value => (value, SgSmartRoutingHelper.ActionBlock)));
        expected.AddRange(item.CustomBlockIps.Select(value => (value, SgSmartRoutingHelper.ActionBlock)));
        expected.AddRange(item.CustomDirectDomains.Select(value => (value, SgSmartRoutingHelper.ActionDirect)));
        expected.AddRange(item.CustomDirectIps.Select(value => (value, SgSmartRoutingHelper.ActionDirect)));
        expected.AddRange(item.CustomProxyDomains.Select(value => (value, SgSmartRoutingHelper.ActionProxy)));
        expected.AddRange(item.CustomProxyIps.Select(value => (value, SgSmartRoutingHelper.ActionProxy)));
        if (expected.Count == 0)
        {
            return "не заданы.";
        }

        var results = new List<string>();
        foreach (var rule in expected.Take(18))
        {
            var searchValue = singbox ? NormalizeForSingboxInspection(rule.Value) : rule.Value;
            var expectedTarget = singbox && rule.Action == SgSmartRoutingHelper.ActionBlock
                ? "reject"
                : SgSmartRoutingHelper.ToOutboundTag(rule.Action);
            var match = observed.FirstOrDefault(candidate => candidate.Values.Any(value =>
                string.Equals(value, searchValue, StringComparison.OrdinalIgnoreCase)));
            if (match.Values == null)
            {
                results.Add($"НЕ НАЙДЕНО · {ActionTitle(rule.Action)} · {rule.Value}");
                continue;
            }

            var targetOk = string.Equals(match.Target, expectedTarget, StringComparison.OrdinalIgnoreCase);
            var priorityOk = categoryIndex is null || match.Index < categoryIndex.Value;
            var state = targetOk && priorityOk ? "OK" : "ПРОВЕРИТЬ";
            var priority = priorityOk ? "до базовых категорий" : "после базовых категорий";
            results.Add($"{state} · {ActionTitle(rule.Action)} · {rule.Value} => {match.Target}; {priority}");
        }

        if (expected.Count > 18)
        {
            results.Add($"…ещё {expected.Count - 18} записей. Полный список находится в config.json.");
        }
        return string.Join("\n", results);
    }

    private static string NormalizeForSingboxInspection(string value)
    {
        foreach (var prefix in new[] { "domain:", "full:", "regexp:", "keyword:", "geosite:", "geoip:" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..];
            }
        }
        return value;
    }

    private static string ActionTitle(string action)
    {
        return SgSmartRoutingHelper.NormalizeAction(action) switch
        {
            SgSmartRoutingHelper.ActionDirect => "Direct",
            SgSmartRoutingHelper.ActionBlock => "Block",
            _ => "VPN",
        };
    }

    private static bool IsDownloadedCategoryValue(string value)
    {
        return value.Contains("category-ads-all", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ru-blocked", StringComparison.OrdinalIgnoreCase)
            || value.Contains("category-ru", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tld-ru", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ru-available-only-inside", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "geoip:ru", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "ru", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSgRoutingValue(string value)
    {
        return value.Contains("ru", StringComparison.OrdinalIgnoreCase)
            || value.Contains("category-ads-all", StringComparison.OrdinalIgnoreCase)
            || value.Contains("private", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
            || value.Contains('/')
            || IPAddress.TryParse(value, out _);
    }

    private static List<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.IsNotEmpty())
            .ToList();
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var section = tabGeoFiles.IsChecked == true ? "geofiles" : "routing";
        new SgHelpWindow(section) { Owner = this }.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void SelectRadio(Panel panel, string tag)
    {
        var radio = panel.Children.OfType<RadioButton>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? panel.Children.OfType<RadioButton>().FirstOrDefault();

        if (radio != null)
        {
            radio.IsChecked = true;
        }
    }

    private static string GetCheckedTag(Panel panel, string fallback)
    {
        return panel.Children.OfType<RadioButton>()
            .FirstOrDefault(item => item.IsChecked == true)?.Tag?.ToString()
            ?? fallback;
    }
}
