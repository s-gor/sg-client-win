using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ServiceLib.Helper;

namespace v2rayN.Services;

public sealed record SgRouteTraceItem(int Priority, string Rule, string Action, string Status, string Details);

public sealed record SgRouteTestResult(
    bool IsExact,
    string Input,
    string InputType,
    string MatchedRule,
    string Action,
    int Priority,
    string Outbound,
    string Profile,
    string Details,
    IReadOnlyList<string> ResolvedAddresses,
    IReadOnlyList<SgRouteTraceItem> Trace);

/// <summary>
/// Deterministic address-rule check. It follows active Routing address rules
/// before SG Smart Routing, reads the currently installed geoip.dat / geosite.dat
/// and never guesses a rule from a live connection row.
/// </summary>
public sealed class SgRouteTestService
{
    private static readonly Lazy<SgRouteTestService> LazyInstance = new(() => new());
    public static SgRouteTestService Instance => LazyInstance.Value;

    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private readonly GeoDataMatcher _geoData = new();

    private SgRouteTestService()
    {
    }

    public async Task<SgRouteTestResult> TestAsync(string? rawInput, CancellationToken cancellationToken = default)
    {
        var input = NormalizeInput(rawInput);
        if (input.Length == 0)
        {
            return Error("Введите домен или IP-адрес.", rawInput ?? string.Empty);
        }

        if (AmneziaWgManager.Instance.ActiveProfileId.IsNotEmpty())
        {
            return Error(
                "Для активного AmneziaWG точная проверка отдельных сайтов и правил недоступна: ядро сообщает только состояние туннеля целиком.",
                input);
        }

        var config = AppManager.Instance.Config;
        if (!config.TunModeItem.EnableTun)
        {
            return Error(
                "SG Smart Routing применяется в режиме TUN. В System Proxy и Local Proxy эти SG-правила не являются активным маршрутом.",
                input);
        }

        if (!AppManager.Instance.IsRunningCore(ECoreType.Xray)
            && !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            return Error(
                "Нет активного TUN-ядра Xray или sing-box. Подключите TUN и повторите проверку.",
                input);
        }

        var settings = SgSmartRoutingHelper.Normalize(config.SgQuickSettingsItem);
        var build = await BuildRulesAsync(config, settings).ConfigureAwait(false);
        if (build.Error.IsNotEmpty())
        {
            return Error(build.Error, input);
        }
        var rules = build.Rules;
        var activeProfile = await ResolveActiveProfileNameAsync().ConfigureAwait(false);
        var finalPriority = rules.Select(item => item.Priority).DefaultIfEmpty(0).Max() + 1;

        if (IPAddress.TryParse(input, out var address))
        {
            var evaluation = await EvaluateIpAsync(address, rules, cancellationToken).ConfigureAwait(false);
            return ToResult(input, "IP-адрес", evaluation, activeProfile, [address.ToString()], finalPriority);
        }

        if (!IsValidHost(input))
        {
            return Error("Не удалось распознать домен или IP-адрес.", input);
        }

        var domainEvaluation = await EvaluateDomainAsync(input, rules, cancellationToken).ConfigureAwait(false);
        if (domainEvaluation.Match != null || !domainEvaluation.IsExact)
        {
            return ToResult(input, "Домен", domainEvaluation, activeProfile, [], finalPriority);
        }

        IPAddress[] addresses;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DnsTimeout);
            addresses = await Dns.GetHostAddressesAsync(input, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BuildDnsError(input, domainEvaluation.Trace, "DNS не ответил за 5 секунд. IP-правила не проверены.");
        }
        catch (Exception ex)
        {
            return BuildDnsError(input, domainEvaluation.Trace, $"Не удалось разрешить домен: {ex.Message}");
        }

        var uniqueAddresses = addresses
            .Where(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Select(item => item.IsIPv4MappedToIPv6 ? item.MapToIPv4() : item)
            .Distinct()
            .ToArray();
        if (uniqueAddresses.Length == 0)
        {
            return BuildDnsError(input, domainEvaluation.Trace, "DNS не вернул IPv4/IPv6-адреса. IP-правила не проверены.");
        }

        var evaluations = new List<(IPAddress Address, RuleEvaluation Evaluation)>();
        foreach (var resolved in uniqueAddresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations.Add((resolved, await EvaluateIpAsync(resolved, rules, cancellationToken).ConfigureAwait(false)));
        }

        var incomplete = evaluations.FirstOrDefault(item => !item.Evaluation.IsExact);
        if (incomplete.Evaluation != null && !incomplete.Evaluation.IsExact)
        {
            var trace = domainEvaluation.Trace
                .Concat(incomplete.Evaluation.Trace)
                .ToList();
            return new SgRouteTestResult(
                false,
                input,
                "Домен",
                "Проверка не завершена",
                "—",
                0,
                "—",
                "—",
                incomplete.Evaluation.Error,
                uniqueAddresses.Select(item => item.ToString()).ToArray(),
                trace);
        }

        var groups = evaluations
            .GroupBy(item => new
            {
                Rule = item.Evaluation.Match?.Rule ?? "Финальное правило",
                Action = item.Evaluation.Match?.Action ?? settings.DefaultAction,
                Priority = item.Evaluation.Match?.Priority ?? finalPriority,
                Outbound = item.Evaluation.Match?.Outbound ?? SgSmartRoutingHelper.ToOutboundTag(settings.DefaultAction),
                Profile = item.Evaluation.Match?.Profile ?? string.Empty
            })
            .ToList();

        if (groups.Count != 1)
        {
            var details = new StringBuilder();
            details.AppendLine("DNS вернул адреса с разными результатами маршрутизации. Один точный итог для домена невозможен:");
            foreach (var item in evaluations)
            {
                var match = item.Evaluation.Match;
                var matchedProfile = match?.Profile ?? string.Empty;
                var profile = matchedProfile.IsNotEmpty() ? $" · {matchedProfile}" : string.Empty;
                details.AppendLine($"• {item.Address}: {ActionTitle(match?.Action ?? settings.DefaultAction)} · {match?.Rule ?? "Финальное правило"}{profile}");
            }

            return new SgRouteTestResult(
                false,
                input,
                "Домен",
                "Неоднозначный DNS-результат",
                "—",
                0,
                "—",
                "—",
                details.ToString().TrimEnd(),
                uniqueAddresses.Select(item => item.ToString()).ToArray(),
                domainEvaluation.Trace.Concat(evaluations.SelectMany(item => item.Evaluation.Trace)).ToList());
        }

        var selected = evaluations[0].Evaluation;
        var combinedTrace = domainEvaluation.Trace.Concat(selected.Trace).ToList();
        selected = selected with { Trace = combinedTrace };
        return ToResult(input, "Домен", selected, activeProfile, uniqueAddresses.Select(item => item.ToString()).ToArray(), finalPriority);
    }

    private async Task<RuleEvaluation> EvaluateDomainAsync(
        string host,
        IReadOnlyList<RouteRule> rules,
        CancellationToken cancellationToken)
    {
        var trace = new List<SgRouteTraceItem>();
        foreach (var rule in rules.Where(item => item.Kind == RuleKind.Domain))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await MatchDomainRuleAsync(host, rule.Rule, cancellationToken).ConfigureAwait(false);
            if (!result.IsExact)
            {
                var error = WithRuleSource(result.Details, rule);
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Не проверено",
                    error));
                return new RuleEvaluation(false, rule, error, trace);
            }

            if (!result.Matched)
            {
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Не совпало",
                    WithRuleSource(result.Details, rule)));
                continue;
            }

            if (rule.AdditionalConditions.IsNotEmpty())
            {
                var error = $"Адрес совпал с правилом «{rule.Rule}», но для точного итога также нужны: {rule.AdditionalConditions}. SG Client не подменяет недостающий контекст догадкой.";
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Недостаточно данных",
                    WithRuleSource(error, rule)));
                return new RuleEvaluation(false, rule, WithRuleSource(error, rule), trace);
            }

            trace.Add(new SgRouteTraceItem(
                rule.Priority,
                rule.Rule,
                ActionTitle(rule.Action),
                "Совпало",
                WithRuleSource(result.Details, rule)));
            return new RuleEvaluation(true, rule, string.Empty, trace);
        }
        return new RuleEvaluation(true, null, string.Empty, trace);
    }

    private async Task<RuleEvaluation> EvaluateIpAsync(
        IPAddress address,
        IReadOnlyList<RouteRule> rules,
        CancellationToken cancellationToken)
    {
        var trace = new List<SgRouteTraceItem>();
        foreach (var rule in rules.Where(item => item.Kind == RuleKind.Ip))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await MatchIpRuleAsync(address, rule.Rule, cancellationToken).ConfigureAwait(false);
            if (!result.IsExact)
            {
                var error = WithRuleSource(result.Details, rule);
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Не проверено",
                    error));
                return new RuleEvaluation(false, rule, error, trace);
            }

            if (!result.Matched)
            {
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Не совпало",
                    WithRuleSource(result.Details, rule)));
                continue;
            }

            if (rule.AdditionalConditions.IsNotEmpty())
            {
                var error = $"Адрес совпал с правилом «{rule.Rule}», но для точного итога также нужны: {rule.AdditionalConditions}. SG Client не подменяет недостающий контекст догадкой.";
                trace.Add(new SgRouteTraceItem(
                    rule.Priority,
                    rule.Rule,
                    ActionTitle(rule.Action),
                    "Недостаточно данных",
                    WithRuleSource(error, rule)));
                return new RuleEvaluation(false, rule, WithRuleSource(error, rule), trace);
            }

            trace.Add(new SgRouteTraceItem(
                rule.Priority,
                rule.Rule,
                ActionTitle(rule.Action),
                "Совпало",
                WithRuleSource(result.Details, rule)));
            return new RuleEvaluation(true, rule, string.Empty, trace);
        }
        return new RuleEvaluation(true, null, string.Empty, trace);
    }

    private static string WithRuleSource(string details, RouteRule rule)
    {
        var source = rule.Source.IsNotEmpty() ? $"Источник: {rule.Source}." : string.Empty;
        if (details.IsNullOrEmpty())
        {
            return source;
        }
        var normalized = details.TrimEnd().TrimEnd('.');
        return source.IsNotEmpty() ? $"{normalized}. {source}" : details;
    }

    private async Task<MatchResult> MatchDomainRuleAsync(string host, string rawRule, CancellationToken cancellationToken)
    {
        var rule = rawRule.Trim();
        if (rule.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
        {
            var value = NormalizeDomain(rule[5..]);
            return Exact(string.Equals(host, value, StringComparison.OrdinalIgnoreCase), "Полное совпадение домена");
        }
        if (rule.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
        {
            var value = NormalizeDomain(rule[7..]);
            var matched = string.Equals(host, value, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{value}", StringComparison.OrdinalIgnoreCase);
            return Exact(matched, "Домен или его поддомен");
        }
        if (rule.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
        {
            var value = rule[8..].Trim();
            return Exact(value.Length > 0 && host.Contains(value, StringComparison.OrdinalIgnoreCase), "Ключевое слово в домене");
        }
        if (rule.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var matched = Regex.IsMatch(host, rule[7..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                return Exact(matched, "Регулярное выражение");
            }
            catch (Exception ex)
            {
                return Incomplete($"Некорректное правило {rule}: {ex.Message}");
            }
        }
        if (rule.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
        {
            var category = rule[8..].Trim();
            var result = await _geoData.MatchGeoSiteAsync(category, host, cancellationToken).ConfigureAwait(false);
            return result;
        }
        if (rule.StartsWith("dotless:", StringComparison.OrdinalIgnoreCase))
        {
            var value = rule[8..].Trim();
            return Exact(!host.Contains('.') && (value.Length == 0 || host.Contains(value, StringComparison.OrdinalIgnoreCase)), "Домен без точки");
        }
        if (rule.StartsWith("ext:", StringComparison.OrdinalIgnoreCase)
            || rule.StartsWith("ext-domain:", StringComparison.OrdinalIgnoreCase))
        {
            return Incomplete($"Внешний формат доменного правила пока не поддерживается: {rule}");
        }

        return Exact(host.Contains(rule, StringComparison.OrdinalIgnoreCase), "Ключевое слово в домене (формат Routing без префикса)");
    }

    private async Task<MatchResult> MatchIpRuleAsync(IPAddress address, string rawRule, CancellationToken cancellationToken)
    {
        var rule = rawRule.Trim();
        if (rule.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
        {
            var category = rule[6..].Trim();
            if (string.Equals(category, "private", StringComparison.OrdinalIgnoreCase))
            {
                return Exact(IsPrivateAddress(address), "Локальный или служебный IP-диапазон");
            }
            return await _geoData.MatchGeoIpAsync(category, address, cancellationToken).ConfigureAwait(false);
        }

        if (!TryMatchCidr(address, rule, out var matched, out var error))
        {
            return Incomplete($"Некорректное IP/CIDR-правило {rule}: {error}");
        }
        return Exact(matched, "IP/CIDR");
    }


    private static bool TryMatchCidr(IPAddress address, string rawRule, out bool matched, out string error)
    {
        matched = false;
        error = string.Empty;
        var parts = rawRule.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var networkAddress))
        {
            error = "неверный IP-адрес сети";
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (networkAddress.IsIPv4MappedToIPv6)
        {
            networkAddress = networkAddress.MapToIPv4();
        }
        if (address.AddressFamily != networkAddress.AddressFamily)
        {
            matched = false;
            return true;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = networkAddress.GetAddressBytes();
        var maxPrefix = addressBytes.Length * 8;
        var prefix = maxPrefix;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix)
            || prefix < 0
            || prefix > maxPrefix))
        {
            error = $"префикс должен быть от 0 до {maxPrefix}";
            return false;
        }

        var wholeBytes = prefix / 8;
        var remainingBits = prefix % 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                matched = false;
                return true;
            }
        }
        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((addressBytes[wholeBytes] & mask) != (networkBytes[wholeBytes] & mask))
            {
                matched = false;
                return true;
            }
        }

        matched = true;
        return true;
    }

    private static Task<RuleBuildResult> BuildRulesAsync(Config config, SgSmartRoutingItem item)
    {
        var result = new List<RouteRule>();
        var priority = 1;

        // TUN uses SG Smart Routing exclusively. Do not evaluate the legacy
        // v2rayN Routing set here: the generators intentionally ignore it in
        // TUN so "Весь интернет через VPN" cannot be overridden silently.


        Add(result, ref priority, RuleKind.Ip, ["geoip:private"], item.LocalNetworkAction);
        if (item.Preset == SgSmartRoutingHelper.PresetCustom)
        {
            Add(result, ref priority, RuleKind.Domain, item.CustomBlockDomains, SgSmartRoutingHelper.ActionBlock);
            Add(result, ref priority, RuleKind.Ip, item.CustomBlockIps, SgSmartRoutingHelper.ActionBlock);
            Add(result, ref priority, RuleKind.Domain, item.CustomDirectDomains, SgSmartRoutingHelper.ActionDirect);
            Add(result, ref priority, RuleKind.Ip, item.CustomDirectIps, SgSmartRoutingHelper.ActionDirect);
            Add(result, ref priority, RuleKind.Domain, item.CustomProxyDomains, SgSmartRoutingHelper.ActionProxy);
            Add(result, ref priority, RuleKind.Ip, item.CustomProxyIps, SgSmartRoutingHelper.ActionProxy);
        }

        if (SgSmartRoutingHelper.RequiresCommunityRules(item))
        {
            if (item.AdsAction != item.DefaultAction)
            {
                Add(result, ref priority, RuleKind.Domain, ["geosite:category-ads-all"], item.AdsAction);
            }
            if (item.BlockedAction != item.DefaultAction)
            {
                Add(result, ref priority, RuleKind.Domain, ["geosite:ru-blocked"], item.BlockedAction);
                Add(result, ref priority, RuleKind.Ip, ["geoip:ru-blocked"], item.BlockedAction);
            }
            Add(result, ref priority, RuleKind.Domain, SgSmartRoutingHelper.GetRussiaDomainRules(item), item.RussiaAction);
            Add(result, ref priority, RuleKind.Ip, SgSmartRoutingHelper.GetRussiaIpRules(item), item.RussiaAction);
        }

        return Task.FromResult(new RuleBuildResult(result, string.Empty));
    }

    private static async Task<RoutingTarget> ResolveRoutingTargetAsync(
        string? rawOutbound,
        IDictionary<string, RoutingTarget> cache)
    {
        var outbound = rawOutbound.IsNotEmpty()
            ? rawOutbound!.Trim()
            : Global.ProxyTag;
        if (cache.TryGetValue(outbound, out var cached))
        {
            return cached;
        }

        RoutingTarget target;
        if (string.Equals(outbound, Global.DirectTag, StringComparison.OrdinalIgnoreCase))
        {
            target = new RoutingTarget(SgSmartRoutingHelper.ActionDirect, Global.DirectTag, "Direct");
        }
        else if (string.Equals(outbound, Global.BlockTag, StringComparison.OrdinalIgnoreCase))
        {
            target = new RoutingTarget(SgSmartRoutingHelper.ActionBlock, Global.BlockTag, "Block");
        }
        else if (string.Equals(outbound, Global.ProxyTag, StringComparison.OrdinalIgnoreCase))
        {
            target = new RoutingTarget(SgSmartRoutingHelper.ActionProxy, Global.ProxyTag, string.Empty);
        }
        else
        {
            var profile = await AppManager.Instance.GetProfileItemViaRemarks(outbound).ConfigureAwait(false);
            var supported = profile != null && (AppManager.Instance.IsRunningCore(ECoreType.Xray)
                ? Global.XraySupportConfigType.Contains(profile.ConfigType) || profile.ConfigType.IsGroupType()
                : Global.SingboxSupportConfigType.Contains(profile.ConfigType) || profile.ConfigType.IsGroupType());
            target = supported
                ? new RoutingTarget(SgSmartRoutingHelper.ActionProxy, outbound, profile!.Remarks)
                : new RoutingTarget(SgSmartRoutingHelper.ActionProxy, Global.ProxyTag, string.Empty);
        }

        cache[outbound] = target;
        return target;
    }

    private static string BuildAdditionalConditions(RulesItem rule)
    {
        var values = new List<string>();
        if (rule.Port.IsNotEmpty())
        {
            values.Add($"порт {rule.Port}");
        }
        if (rule.Network.IsNotEmpty())
        {
            values.Add($"сеть {rule.Network}");
        }
        if (rule.Protocol?.Count > 0)
        {
            values.Add($"протокол {string.Join(", ", rule.Protocol)}");
        }
        if (rule.InboundTag?.Count > 0)
        {
            values.Add($"inbound {string.Join(", ", rule.InboundTag)}");
        }
        if (rule.Process?.Count > 0)
        {
            values.Add($"процесс {string.Join(", ", rule.Process)}");
        }
        return string.Join("; ", values);
    }

    private static void Add(
        ICollection<RouteRule> target,
        ref int priority,
        RuleKind kind,
        IEnumerable<string>? rules,
        string action,
        string? outbound = null,
        string? profile = null,
        string source = "SG Smart Routing",
        string additionalConditions = "")
    {
        var values = rules?
            .Where(item => item.IsNotEmpty())
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (values.Length == 0)
        {
            return;
        }

        var normalizedAction = SgSmartRoutingHelper.NormalizeAction(action);
        var effectiveOutbound = outbound.IsNotEmpty()
            ? outbound!
            : SgSmartRoutingHelper.ToOutboundTag(normalizedAction);
        foreach (var rule in values)
        {
            target.Add(new RouteRule(
                priority,
                kind,
                rule,
                normalizedAction,
                effectiveOutbound,
                profile ?? string.Empty,
                source,
                additionalConditions));
        }
        priority++;
    }

    private static SgRouteTestResult ToResult(
        string input,
        string inputType,
        RuleEvaluation evaluation,
        string activeProfile,
        IReadOnlyList<string> resolvedAddresses,
        int finalPriority)
    {
        if (!evaluation.IsExact)
        {
            return new SgRouteTestResult(
                false,
                input,
                inputType,
                "Проверка не завершена",
                "—",
                0,
                "—",
                "—",
                evaluation.Error,
                resolvedAddresses,
                evaluation.Trace);
        }

        var settings = SgSmartRoutingHelper.Normalize(AppManager.Instance.Config.SgQuickSettingsItem);
        var match = evaluation.Match;
        var action = match?.Action ?? settings.DefaultAction;
        var rule = match?.Rule ?? "Финальное правило";
        var priority = match?.Priority ?? finalPriority;
        var actionTitle = ActionTitle(action);
        var outbound = match?.Outbound ?? SgSmartRoutingHelper.ToOutboundTag(action);
        var matchedProfile = match?.Profile ?? string.Empty;
        var profile = matchedProfile.IsNotEmpty()
            ? matchedProfile
            : action == SgSmartRoutingHelper.ActionProxy
                ? (activeProfile.IsNotEmpty() ? activeProfile : "Текущий VPN-профиль")
                : actionTitle;
        var details = match == null
            ? "Ни одно более раннее адресное правило не совпало. Применяется действие SG Smart Routing по умолчанию."
            : $"Совпадение доказано по текущей конфигурации. Источник: {match.Source}.";
        var trace = evaluation.Trace.ToList();
        if (match == null)
        {
            trace.Add(new SgRouteTraceItem(finalPriority, "Финальное правило", actionTitle, "Совпало", details));
        }

        return new SgRouteTestResult(
            true,
            input,
            inputType,
            rule,
            actionTitle,
            priority,
            outbound,
            profile,
            details,
            resolvedAddresses,
            trace);
    }

    private static async Task<string> ResolveActiveProfileNameAsync()
    {
        try
        {
            var profile = await ConfigHandler.GetDefaultServer(AppManager.Instance.Config);
            return profile?.Remarks?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Resolve active profile for route test", ex);
            return string.Empty;
        }
    }

    private static SgRouteTestResult BuildDnsError(string input, IReadOnlyList<SgRouteTraceItem> trace, string details)
    {
        return new SgRouteTestResult(false, input, "Домен", "Проверка не завершена", "—", 0, "—", "—", details, [], trace);
    }

    private static SgRouteTestResult Error(string details, string input)
    {
        return new SgRouteTestResult(false, input, "—", "—", "—", 0, "—", "—", details, [], []);
    }

    private static MatchResult Exact(bool matched, string details) => new(true, matched, details);
    private static MatchResult Incomplete(string details) => new(false, false, details);

    private static string NormalizeInput(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}", UriKind.Absolute, out var uri)
            && uri.Host.IsNotEmpty())
        {
            value = uri.Host;
        }
        else if (value.StartsWith('[') && value.Contains(']'))
        {
            value = value[1..value.IndexOf(']')];
        }
        else
        {
            var lastColon = value.LastIndexOf(':');
            if (lastColon > 0 && value.Count(item => item == ':') == 1 && int.TryParse(value[(lastColon + 1)..], out _))
            {
                value = value[..lastColon];
            }
        }

        value = value.Trim().Trim('.');
        if (IPAddress.TryParse(value, out var address))
        {
            return address.ToString();
        }

        try
        {
            return new IdnMapping().GetAscii(value).ToLowerInvariant();
        }
        catch
        {
            return value.ToLowerInvariant();
        }
    }

    private static string NormalizeDomain(string value)
    {
        try
        {
            return new IdnMapping().GetAscii(value.Trim().Trim('.')).ToLowerInvariant();
        }
        catch
        {
            return value.Trim().Trim('.').ToLowerInvariant();
        }
    }

    private static bool IsValidHost(string host)
    {
        if (host.Length is < 1 or > 253 || host.Contains(' '))
        {
            return false;
        }
        return host.Split('.').All(label => label.Length is >= 1 and <= 63);
    }

    private static string ActionTitle(string? action)
    {
        return SgSmartRoutingHelper.NormalizeAction(action) switch
        {
            SgSmartRoutingHelper.ActionDirect => "Direct",
            SgSmartRoutingHelper.ActionBlock => "Block",
            _ => "VPN",
        };
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) == 0xFC;
        }
        return false;
    }

    private enum RuleKind
    {
        Domain,
        Ip,
    }

    private sealed record RouteRule(
        int Priority,
        RuleKind Kind,
        string Rule,
        string Action,
        string Outbound,
        string Profile,
        string Source,
        string AdditionalConditions);
    private sealed record RuleBuildResult(IReadOnlyList<RouteRule> Rules, string Error);
    private sealed record RoutingTarget(string Action, string Outbound, string Profile);
    private sealed record MatchResult(bool IsExact, bool Matched, string Details);
    private sealed record RuleEvaluation(bool IsExact, RouteRule? Match, string Error, IReadOnlyList<SgRouteTraceItem> Trace);

    private sealed class GeoDataMatcher
    {
        private readonly SemaphoreSlim _geoSiteGate = new(1, 1);
        private readonly SemaphoreSlim _geoIpGate = new(1, 1);
        private readonly Dictionary<string, DomainCategory?> _geoSites = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IpCategory?> _geoIps = new(StringComparer.OrdinalIgnoreCase);
        private string _geoSiteFingerprint = string.Empty;
        private string _geoIpFingerprint = string.Empty;

        public async Task<MatchResult> MatchGeoSiteAsync(string category, string host, CancellationToken cancellationToken)
        {
            var path = FindGeoFile("geosite.dat");
            if (path.Length == 0)
            {
                return Incomplete("geosite.dat не найден. Точная проверка geosite невозможна.");
            }

            await EnsureGeoSiteAsync(path, category, cancellationToken).ConfigureAwait(false);
            if (!_geoSites.TryGetValue(category, out var data) || data == null)
            {
                return Incomplete($"Категория geosite:{category} отсутствует в текущем geosite.dat.");
            }

            var result = await Task.Run(() => data.Match(host), cancellationToken).ConfigureAwait(false);
            return result.IsExact
                ? Exact(result.Matched, $"Проверено по geosite:{category}")
                : Incomplete(result.Error);
        }

        public async Task<MatchResult> MatchGeoIpAsync(string category, IPAddress address, CancellationToken cancellationToken)
        {
            var path = FindGeoFile("geoip.dat");
            if (path.Length == 0)
            {
                return Incomplete("geoip.dat не найден. Точная проверка geoip невозможна.");
            }

            await EnsureGeoIpAsync(path, category, cancellationToken).ConfigureAwait(false);
            if (!_geoIps.TryGetValue(category, out var data) || data == null)
            {
                return Incomplete($"Категория geoip:{category} отсутствует в текущем geoip.dat.");
            }

            var matched = await Task.Run(() => data.Contains(address), cancellationToken).ConfigureAwait(false);
            return Exact(matched, $"Проверено по geoip:{category}");
        }

        private async Task EnsureGeoSiteAsync(string path, string category, CancellationToken cancellationToken)
        {
            await _geoSiteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var fingerprint = FileFingerprint(path);
                if (!string.Equals(_geoSiteFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    _geoSites.Clear();
                    _geoSiteFingerprint = fingerprint;
                }
                if (_geoSites.ContainsKey(category))
                {
                    return;
                }

                var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                var parsed = await Task.Run(
                    () => ParseGeoSiteCategory(data, category, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                _geoSites[category] = parsed;
            }
            finally
            {
                _geoSiteGate.Release();
            }
        }

        private async Task EnsureGeoIpAsync(string path, string category, CancellationToken cancellationToken)
        {
            await _geoIpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var fingerprint = FileFingerprint(path);
                if (!string.Equals(_geoIpFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    _geoIps.Clear();
                    _geoIpFingerprint = fingerprint;
                }
                if (_geoIps.ContainsKey(category))
                {
                    return;
                }

                var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                var parsed = await Task.Run(
                    () => ParseGeoIpCategory(data, category, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                _geoIps[category] = parsed;
            }
            finally
            {
                _geoIpGate.Release();
            }
        }

        private static DomainCategory? ParseGeoSiteCategory(byte[] data, string requestedCategory, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < data.Length && TryReadVarint(data, ref offset, data.Length, out var key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, data.Length, out var start, out var length))
                {
                    var category = ReadStringField(data, start, length, 1);
                    if (string.Equals(category, requestedCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        return ParseDomainCategory(data, start, length, cancellationToken);
                    }
                    offset = start + length;
                }
                else if (!SkipField(data, ref offset, data.Length, wire))
                {
                    break;
                }
            }
            return null;
        }

        private static DomainCategory ParseDomainCategory(byte[] data, int start, int length, CancellationToken cancellationToken)
        {
            var entries = new List<DomainEntry>();
            var end = start + length;
            var offset = start;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 2 && wire == 2 && TryReadLength(data, ref offset, end, out var domainStart, out var domainLength))
                {
                    var entry = ParseDomainEntry(data, domainStart, domainLength);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                    offset = domainStart + domainLength;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    break;
                }
            }
            return new DomainCategory(entries);
        }

        private static DomainEntry? ParseDomainEntry(byte[] data, int start, int length)
        {
            var end = start + length;
            var offset = start;
            var type = 0;
            var value = string.Empty;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 0 && TryReadVarint(data, ref offset, end, out var rawType))
                {
                    type = (int)rawType;
                }
                else if (field == 2 && wire == 2 && TryReadLength(data, ref offset, end, out var valueStart, out var valueLength))
                {
                    value = Encoding.UTF8.GetString(data, valueStart, valueLength);
                    offset = valueStart + valueLength;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    return null;
                }
            }
            return value.Length == 0 ? null : new DomainEntry(type, value);
        }

        private static IpCategory? ParseGeoIpCategory(byte[] data, string requestedCategory, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < data.Length && TryReadVarint(data, ref offset, data.Length, out var key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, data.Length, out var start, out var length))
                {
                    var category = ReadStringField(data, start, length, 1);
                    if (string.Equals(category, requestedCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        return ParseIpCategory(data, start, length, cancellationToken);
                    }
                    offset = start + length;
                }
                else if (!SkipField(data, ref offset, data.Length, wire))
                {
                    break;
                }
            }
            return null;
        }

        private static IpCategory ParseIpCategory(byte[] data, int start, int length, CancellationToken cancellationToken)
        {
            var ranges = new List<IpRange>();
            var end = start + length;
            var offset = start;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 2 && wire == 2 && TryReadLength(data, ref offset, end, out var cidrStart, out var cidrLength))
                {
                    var range = ParseIpRange(data, cidrStart, cidrLength);
                    if (range != null)
                    {
                        ranges.Add(range);
                    }
                    offset = cidrStart + cidrLength;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    break;
                }
            }
            return new IpCategory(ranges);
        }

        private static IpRange? ParseIpRange(byte[] data, int start, int length)
        {
            var end = start + length;
            var offset = start;
            byte[]? network = null;
            var prefix = -1;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, end, out var valueStart, out var valueLength))
                {
                    network = data.AsSpan(valueStart, valueLength).ToArray();
                    offset = valueStart + valueLength;
                }
                else if (field == 2 && wire == 0 && TryReadVarint(data, ref offset, end, out var rawPrefix))
                {
                    prefix = (int)rawPrefix;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    return null;
                }
            }
            if (network is not { Length: 4 or 16 } || prefix < 0 || prefix > network.Length * 8)
            {
                return null;
            }
            return new IpRange(network, prefix);
        }

        private static string ReadStringField(byte[] data, int start, int length, int requestedField)
        {
            var end = start + length;
            var offset = start;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == requestedField && wire == 2 && TryReadLength(data, ref offset, end, out var valueStart, out var valueLength))
                {
                    return Encoding.UTF8.GetString(data, valueStart, valueLength);
                }
                if (!SkipField(data, ref offset, end, wire))
                {
                    break;
                }
            }
            return string.Empty;
        }

        private static string FindGeoFile(string fileName)
        {
            var candidates = new[]
            {
                Utils.GetBinPath(fileName),
                Path.Combine(AppContext.BaseDirectory, "bin", fileName),
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Environment.CurrentDirectory, "bin", fileName),
            };
            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static string FileFingerprint(string path)
        {
            var info = new FileInfo(path);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        private static bool TryReadLength(byte[] data, ref int offset, int limit, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (!TryReadVarint(data, ref offset, limit, out var rawLength) || rawLength > int.MaxValue)
            {
                return false;
            }
            length = (int)rawLength;
            start = offset;
            return length >= 0 && start >= 0 && start + length <= limit;
        }

        private static bool TryReadVarint(byte[] data, ref int offset, int limit, out ulong value)
        {
            value = 0;
            var shift = 0;
            while (offset < limit && shift <= 63)
            {
                var current = data[offset++];
                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return true;
                }
                shift += 7;
            }
            return false;
        }

        private static bool SkipField(byte[] data, ref int offset, int limit, int wire)
        {
            switch (wire)
            {
                case 0:
                    return TryReadVarint(data, ref offset, limit, out _);
                case 1:
                    offset += 8;
                    return offset <= limit;
                case 2:
                    if (!TryReadVarint(data, ref offset, limit, out var rawLength) || rawLength > int.MaxValue)
                    {
                        return false;
                    }
                    offset += (int)rawLength;
                    return offset <= limit;
                case 5:
                    offset += 4;
                    return offset <= limit;
                default:
                    return false;
            }
        }

        private sealed class DomainCategory(IReadOnlyList<DomainEntry> entries)
        {
            public DomainMatch Match(string host)
            {
                foreach (var entry in entries)
                {
                    switch (entry.Type)
                    {
                        case 0: // Plain / keyword
                            if (host.Contains(entry.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                return new DomainMatch(true, true, string.Empty);
                            }
                            break;
                        case 1: // Regex
                            try
                            {
                                if (Regex.IsMatch(host, entry.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
                                {
                                    return new DomainMatch(true, true, string.Empty);
                                }
                            }
                            catch (Exception ex)
                            {
                                return new DomainMatch(false, false, $"Некорректное regexp в geosite.dat: {ex.Message}");
                            }
                            break;
                        case 2: // Domain / suffix
                            if (string.Equals(host, entry.Value, StringComparison.OrdinalIgnoreCase)
                                || host.EndsWith($".{entry.Value}", StringComparison.OrdinalIgnoreCase))
                            {
                                return new DomainMatch(true, true, string.Empty);
                            }
                            break;
                        case 3: // Full
                            if (string.Equals(host, entry.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                return new DomainMatch(true, true, string.Empty);
                            }
                            break;
                        default:
                            return new DomainMatch(false, false, $"Неизвестный тип домена {entry.Type} в geosite.dat.");
                    }
                }
                return new DomainMatch(true, false, string.Empty);
            }
        }

        private sealed class IpCategory(IReadOnlyList<IpRange> ranges)
        {
            public bool Contains(IPAddress address)
            {
                if (address.IsIPv4MappedToIPv6)
                {
                    address = address.MapToIPv4();
                }
                var bytes = address.GetAddressBytes();
                return ranges.Any(range => range.Contains(bytes));
            }
        }

        private sealed record DomainEntry(int Type, string Value);
        private sealed record DomainMatch(bool IsExact, bool Matched, string Error);

        private sealed record IpRange(byte[] Network, int Prefix)
        {
            public bool Contains(byte[] address)
            {
                if (address.Length != Network.Length)
                {
                    return false;
                }
                var fullBytes = Prefix / 8;
                var remainingBits = Prefix % 8;
                for (var i = 0; i < fullBytes; i++)
                {
                    if (address[i] != Network[i])
                    {
                        return false;
                    }
                }
                if (remainingBits == 0)
                {
                    return true;
                }
                var mask = (byte)(0xFF << (8 - remainingBits));
                return (address[fullBytes] & mask) == (Network[fullBytes] & mask);
            }
        }
    }
}
