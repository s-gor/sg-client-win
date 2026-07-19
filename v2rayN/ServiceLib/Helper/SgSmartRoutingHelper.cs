namespace ServiceLib.Helper;

public static class SgSmartRoutingHelper
{
    public const string PresetGlobal = "global";
    public const string PresetRussiaDirect = "ru_except";
    public const string PresetBlockedOnly = "ru_blocked";
    public const string PresetCustom = "custom";

    public const string RussiaScopeNone = "none";
    public const string RussiaScopeTld = "tld";
    public const string RussiaScopeSitesAndIp = "sites_ip";

    public const string ActionNone = "none";
    public const string ActionDirect = "direct";
    public const string ActionProxy = "proxy";
    public const string ActionBlock = "block";

    private static readonly HashSet<string> Presets =
    [
        PresetGlobal,
        PresetRussiaDirect,
        PresetBlockedOnly,
        PresetCustom,
    ];

    private static readonly HashSet<string> RussiaScopes =
    [
        RussiaScopeNone,
        RussiaScopeTld,
        RussiaScopeSitesAndIp,
    ];

    private static readonly HashSet<string> Actions =
    [
        ActionNone,
        ActionDirect,
        ActionProxy,
        ActionBlock,
    ];

    public static SgSmartRoutingItem Normalize(SgQuickSettingsItem settings)
    {
        settings.SmartRouting ??= new SgSmartRoutingItem();
        var item = settings.SmartRouting;

        if (!item.MigratedFromLegacyPreset)
        {
            // A fully populated SmartRouting block is newer and more precise than
            // the old single RoutingMode value. Do not overwrite an explicit
            // custom preset or its lists during one-time legacy migration.
            var hasExplicitSmartRouting =
                NormalizePreset(item.Preset) != PresetGlobal
                || NormalizeAction(item.DefaultAction, ActionProxy) != ActionProxy
                || NormalizeAction(item.LocalNetworkAction, ActionDirect) != ActionDirect
                || NormalizeRussiaScope(item.RussiaScope) != RussiaScopeNone
                || NormalizeAction(item.RussiaAction, ActionProxy) != ActionProxy
                || NormalizeAction(item.BlockedAction, ActionProxy) != ActionProxy
                || NormalizeAction(item.AdsAction, ActionProxy) != ActionProxy
                || item.CustomDirectDomains?.Count > 0
                || item.CustomProxyDomains?.Count > 0
                || item.CustomBlockDomains?.Count > 0
                || item.CustomDirectIps?.Count > 0
                || item.CustomProxyIps?.Count > 0
                || item.CustomBlockIps?.Count > 0;

            if (!hasExplicitSmartRouting)
            {
                ApplyPreset(item, NormalizePreset(settings.RoutingMode), preserveCustomLists: true);
            }

            item.MigratedFromLegacyPreset = true;
        }

        item.Preset = NormalizePreset(item.Preset);
        item.DefaultAction = NormalizeDefaultAction(item.DefaultAction);
        item.LocalNetworkAction = NormalizeExplicitAction(item.LocalNetworkAction, ActionDirect);

        if (!item.RussiaScopeMigrated)
        {
            item.RussiaScope = item.RussiaAction != item.DefaultAction
                || item.Preset == PresetRussiaDirect
                ? RussiaScopeSitesAndIp
                : RussiaScopeNone;
            item.RussiaScopeMigrated = true;
        }

        item.RussiaScope = NormalizeRussiaScope(item.RussiaScope);
        item.RussiaAction = NormalizeExplicitAction(item.RussiaAction, item.DefaultAction);
        item.BlockedAction = NormalizeExplicitAction(item.BlockedAction, item.DefaultAction);
        item.AdsAction = NormalizeExplicitAction(item.AdsAction, item.DefaultAction);

        item.CustomDirectDomains = NormalizeEntries(item.CustomDirectDomains, isDomain: true);
        item.CustomProxyDomains = NormalizeEntries(item.CustomProxyDomains, isDomain: true);
        item.CustomBlockDomains = NormalizeEntries(item.CustomBlockDomains, isDomain: true);
        item.CustomDirectIps = NormalizeEntries(item.CustomDirectIps, isDomain: false);
        item.CustomProxyIps = NormalizeEntries(item.CustomProxyIps, isDomain: false);
        item.CustomBlockIps = NormalizeEntries(item.CustomBlockIps, isDomain: false);

        settings.RoutingMode = item.Preset == PresetCustom ? PresetCustom : item.Preset;
        return item;
    }

    public static void ApplyPreset(SgSmartRoutingItem item, string preset, bool preserveCustomLists = true)
    {
        var normalized = NormalizePreset(preset);
        item.Preset = normalized;
        item.LocalNetworkAction = ActionDirect;
        item.RussiaScopeMigrated = true;

        switch (normalized)
        {
            case PresetRussiaDirect:
                item.DefaultAction = ActionProxy;
                item.RussiaScope = RussiaScopeSitesAndIp;
                item.RussiaAction = ActionDirect;
                item.BlockedAction = ActionProxy;
                item.AdsAction = ActionProxy;
                break;
            case PresetBlockedOnly:
                item.DefaultAction = ActionDirect;
                item.RussiaScope = RussiaScopeNone;
                item.RussiaAction = ActionDirect;
                item.BlockedAction = ActionProxy;
                item.AdsAction = ActionDirect;
                break;
            case PresetCustom:
                item.DefaultAction = NormalizeDefaultAction(item.DefaultAction);
                item.RussiaScope = NormalizeRussiaScope(item.RussiaScope);
                item.RussiaAction = NormalizeExplicitAction(item.RussiaAction, item.DefaultAction);
                item.BlockedAction = NormalizeExplicitAction(item.BlockedAction, item.DefaultAction);
                item.AdsAction = NormalizeExplicitAction(item.AdsAction, item.DefaultAction);
                break;
            default:
                item.DefaultAction = ActionProxy;
                item.RussiaScope = RussiaScopeNone;
                item.RussiaAction = ActionProxy;
                item.BlockedAction = ActionProxy;
                item.AdsAction = ActionProxy;
                break;
        }

        if (!preserveCustomLists)
        {
            item.CustomDirectDomains = [];
            item.CustomProxyDomains = [];
            item.CustomBlockDomains = [];
            item.CustomDirectIps = [];
            item.CustomProxyIps = [];
            item.CustomBlockIps = [];
        }
    }

    public static string NormalizePreset(string? value)
    {
        var result = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Presets.Contains(result) ? result : PresetGlobal;
    }

    public static string NormalizeRussiaScope(string? value)
    {
        var result = (value ?? string.Empty).Trim().ToLowerInvariant();
        return RussiaScopes.Contains(result) ? result : RussiaScopeNone;
    }

    public static string NormalizeAction(string? value, string fallback = ActionNone)
    {
        var result = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Actions.Contains(result) ? result : fallback;
    }

    private static string NormalizeDefaultAction(string? value)
    {
        return NormalizeAction(value, ActionProxy) == ActionDirect ? ActionDirect : ActionProxy;
    }

    private static string NormalizeExplicitAction(string? value, string fallback)
    {
        var normalizedFallback = NormalizeAction(fallback, ActionProxy);
        if (normalizedFallback == ActionNone)
        {
            normalizedFallback = ActionProxy;
        }

        var action = NormalizeAction(value, normalizedFallback);
        return action == ActionNone ? normalizedFallback : action;
    }

    public static string ToOutboundTag(string action)
    {
        return NormalizeAction(action) switch
        {
            ActionDirect => Global.DirectTag,
            ActionBlock => Global.BlockTag,
            _ => Global.ProxyTag,
        };
    }

    public static bool RequiresCommunityRules(SgSmartRoutingItem item)
    {
        return NormalizeRussiaScope(item.RussiaScope) != RussiaScopeNone
            || item.BlockedAction != item.DefaultAction
            || item.AdsAction != item.DefaultAction;
    }

    public static IReadOnlyList<string> GetRussiaDomainRules(SgSmartRoutingItem item)
    {
        return NormalizeRussiaScope(item.RussiaScope) switch
        {
            RussiaScopeTld => ["geosite:tld-ru"],
            RussiaScopeSitesAndIp => ["geosite:category-ru"],
            _ => [],
        };
    }

    public static IReadOnlyList<string> GetRussiaIpRules(SgSmartRoutingItem item)
    {
        return NormalizeRussiaScope(item.RussiaScope) == RussiaScopeSitesAndIp
            ? ["geoip:ru"]
            : [];
    }

    public static string GetRussiaScopeTitle(string? scope)
    {
        return NormalizeRussiaScope(scope) switch
        {
            RussiaScopeTld => "Только российские доменные зоны",
            RussiaScopeSitesAndIp => "Российские сайты и IP",
            _ => "Выключено",
        };
    }

    public static List<string> ParseMultiline(string? value, bool isDomain)
    {
        return NormalizeEntries((value ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), isDomain);
    }

    public static string ToMultiline(IEnumerable<string>? values)
    {
        return string.Join(Environment.NewLine, values ?? []);
    }

    private static List<string> NormalizeEntries(IEnumerable<string>? values, bool isDomain)
    {
        var result = new List<string>();
        foreach (var raw in values ?? [])
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.IsNullOrEmpty() || value.StartsWith('#'))
            {
                continue;
            }

            if (isDomain)
            {
                value = NormalizeDomain(value);
            }
            else
            {
                value = NormalizeIp(value);
            }

            if (value.IsNotEmpty())
            {
                result.Add(value);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeDomain(string value)
    {
        var knownPrefixes = new[] { "domain:", "full:", "regexp:", "keyword:", "geosite:" };
        if (knownPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        value = value.Trim().TrimStart('.');
        if (Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}", UriKind.Absolute, out var uri))
        {
            value = uri.Host;
        }
        value = value.Trim().Trim('.').ToLowerInvariant();
        return value.IsNullOrEmpty() ? string.Empty : $"domain:{value}";
    }

    private static string NormalizeIp(string value)
    {
        try
        {
            if (value.StartsWith(Global.GeoIPPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return value.ToLowerInvariant();
            }

            if (!value.Contains('/'))
            {
                if (!IPAddress.TryParse(value, out var address))
                {
                    return string.Empty;
                }
                return address.AddressFamily == AddressFamily.InterNetwork ? $"{address}/32" : $"{address}/128";
            }

            return IPNetwork2.Parse(value).ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
