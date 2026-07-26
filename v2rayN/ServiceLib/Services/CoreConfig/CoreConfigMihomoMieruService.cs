using ServiceLib.Helper;

namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Generates a native Mihomo configuration for an imported Mieru profile.
/// The generated config keeps SG Client ports, TUN mode, DNS and smart routing.
/// </summary>
public class CoreConfigMihomoMieruService(CoreConfigContext context)
{
    private const string ProxyGroupName = "SG-PROXY";

    public RetResult GenerateClientConfigContent()
    {
        var node = context.Node;
        var extra = node.GetProtocolExtra();
        var bindings = extra.MieruBindings is { Count: > 0 }
            ? extra.MieruBindings
            : [new MieruBindingItem { Port = node.Port.ToString(), Protocol = NormalizeProtocol(node.Network) }];

        if (node.Address.IsNullOrEmpty() || node.Username.IsNullOrEmpty() || node.Password.IsNullOrEmpty()
            || bindings.Count == 0)
        {
            return new RetResult { Msg = ResUI.CheckServerSettings };
        }

        try
        {
            var config = context.AppConfig;
            var proxies = BuildProxies(node, extra, bindings);
            var proxyNames = proxies
                .Select(proxy => proxy["name"]?.ToString() ?? string.Empty)
                .Where(name => name.IsNotEmpty())
                .ToList();

            var root = new Dictionary<string, object>
            {
                ["mixed-port"] = AppManager.Instance.GetLocalPort(EInboundProtocol.socks),
                ["allow-lan"] = config.Inbound.FirstOrDefault()?.AllowLANConn == true,
                ["bind-address"] = config.Inbound.FirstOrDefault()?.AllowLANConn == true ? "*" : Global.Loopback,
                ["mode"] = "rule",
                ["log-level"] = GetLogLevel(config.CoreBasicItem.Loglevel),
                ["ipv6"] = config.ClashUIItem.EnableIPv6,
                ["external-controller"] = $"{Global.Loopback}:{AppManager.Instance.StatePort2}",
                ["unified-delay"] = true,
                ["tcp-concurrent"] = true,
                ["geodata-mode"] = true,
                ["proxies"] = proxies,
                ["proxy-groups"] = new List<Dictionary<string, object>>
                {
                    BuildProxyGroup(proxyNames)
                },
                ["dns"] = BuildDns(config.SimpleDNSItem, config.ClashUIItem.EnableIPv6),
                ["rules"] = BuildRules(config),
            };

            if (context.IsTunEnabled)
            {
                var tun = EmbedUtils.GetEmbedText(Global.ClashTunYaml);
                var tunContent = tun.IsNotEmpty()
                    ? YamlUtils.FromYaml<Dictionary<string, object>>(tun)
                    : null;
                if (tunContent?.TryGetValue("tun", out var tunBlock) == true)
                {
                    root["tun"] = tunBlock;
                }
            }

            var yaml = YamlUtils.ToYaml(root);
            ClashApiManager.Instance.ProfileContent = root;
            return new RetResult
            {
                Success = true,
                Msg = string.Format(ResUI.SuccessfulConfiguration, node.GetSummary()),
                Data = yaml,
            };
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(CoreConfigMihomoMieruService), ex);
            return new RetResult { Msg = ResUI.FailedGenDefaultConfiguration };
        }
    }

    private static List<Dictionary<string, object>> BuildProxies(
        ProfileItem node,
        ProtocolExtraItem extra,
        IReadOnlyList<MieruBindingItem> bindings)
    {
        var result = new List<Dictionary<string, object>>();
        var baseName = node.Remarks.IsNotEmpty() ? node.Remarks : "Mieru";
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var protocol = NormalizeProtocol(binding.Protocol);
            var name = bindings.Count == 1
                ? baseName
                : $"{baseName} · {protocol} {binding.Port}";
            var proxy = new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = "mieru",
                ["server"] = node.Address,
                ["transport"] = protocol,
                ["username"] = node.Username,
                ["password"] = node.Password,
                ["multiplexing"] = NormalizeMultiplexing(extra.MieruMultiplexing),
                ["udp"] = true,
            };

            if (binding.Port.IndexOf('-') >= 0)
            {
                proxy["port-range"] = binding.Port;
            }
            else if (int.TryParse(binding.Port, out var port))
            {
                proxy["port"] = port;
            }

            if (extra.MieruTrafficPattern.IsNotEmpty())
            {
                proxy["traffic-pattern"] = extra.MieruTrafficPattern;
            }
            result.Add(proxy);
        }
        return result;
    }

    private static Dictionary<string, object> BuildProxyGroup(List<string> proxyNames)
    {
        var group = new Dictionary<string, object>
        {
            ["name"] = ProxyGroupName,
            ["type"] = proxyNames.Count > 1 ? "fallback" : "select",
            ["proxies"] = proxyNames,
        };
        if (proxyNames.Count > 1)
        {
            group["url"] = "https://www.gstatic.com/generate_204";
            group["interval"] = 300;
            group["lazy"] = true;
        }
        return group;
    }

    private static Dictionary<string, object> BuildDns(SimpleDNSItem dns, bool ipv6)
    {
        var remote = ParseDnsList(dns?.RemoteDNS, Global.DomainRemoteDNSAddress.First());
        var bootstrap = ParseDnsList(dns?.BootstrapDNS, "1.1.1.1,8.8.8.8");
        var block = new Dictionary<string, object>
        {
            ["enable"] = true,
            ["ipv6"] = ipv6,
            ["enhanced-mode"] = dns?.FakeIP == false ? "redir-host" : "fake-ip",
            ["fake-ip-range"] = "198.18.0.1/16",
            ["nameserver"] = remote,
            ["proxy-server-nameserver"] = bootstrap,
            ["nameserver-policy"] = new Dictionary<string, object>
            {
                ["geosite:private"] = bootstrap,
            },
        };
        return block;
    }

    private static List<string> ParseDnsList(string? value, string fallback)
    {
        var result = (value.IsNotEmpty() ? value! : fallback)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result.Count > 0 ? result : [fallback];
    }

    private static List<string> BuildRules(Config config)
    {
        var smart = SgSmartRoutingHelper.Normalize(config.SgQuickSettingsItem ?? new SgQuickSettingsItem());
        var rules = new List<string>();

        AddLocalNetworkRules(rules, Target(smart.LocalNetworkAction));
        AddDomainRules(rules, smart.CustomBlockDomains, "REJECT");
        AddIpRules(rules, smart.CustomBlockIps, "REJECT");
        AddDomainRules(rules, smart.CustomProxyDomains, ProxyGroupName);
        AddIpRules(rules, smart.CustomProxyIps, ProxyGroupName);
        AddDomainRules(rules, smart.CustomDirectDomains, "DIRECT");
        AddIpRules(rules, smart.CustomDirectIps, "DIRECT");

        var russiaTarget = Target(smart.RussiaAction);
        switch (SgSmartRoutingHelper.NormalizeRussiaScope(smart.RussiaScope))
        {
            case SgSmartRoutingHelper.RussiaScopeTld:
                rules.Add($"GEOSITE,tld-ru,{russiaTarget}");
                break;
            case SgSmartRoutingHelper.RussiaScopeSitesAndIp:
                rules.Add($"GEOSITE,category-ru,{russiaTarget}");
                rules.Add($"GEOIP,RU,{russiaTarget},no-resolve");
                break;
        }

        var blockedTarget = Target(smart.BlockedAction);
        rules.Add($"GEOSITE,ru-blocked,{blockedTarget}");
        rules.Add($"GEOIP,ru-blocked,{blockedTarget},no-resolve");

        var adsTarget = Target(smart.AdsAction);
        rules.Add($"GEOSITE,category-ads-all,{adsTarget}");
        rules.Add($"MATCH,{Target(smart.DefaultAction)}");
        return rules;
    }

    private static void AddLocalNetworkRules(List<string> rules, string target)
    {
        rules.Add($"IP-CIDR,127.0.0.0/8,{target},no-resolve");
        rules.Add($"IP-CIDR,10.0.0.0/8,{target},no-resolve");
        rules.Add($"IP-CIDR,172.16.0.0/12,{target},no-resolve");
        rules.Add($"IP-CIDR,192.168.0.0/16,{target},no-resolve");
        rules.Add($"IP-CIDR6,::1/128,{target},no-resolve");
        rules.Add($"IP-CIDR6,fc00::/7,{target},no-resolve");
    }

    private static void AddDomainRules(List<string> rules, IEnumerable<string>? values, string target)
    {
        foreach (var raw in values ?? [])
        {
            var value = raw.Trim();
            if (value.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"DOMAIN-SUFFIX,{value[7..]},{target}");
            }
            else if (value.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"DOMAIN,{value[5..]},{target}");
            }
            else if (value.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"DOMAIN-REGEX,{value[7..]},{target}");
            }
            else if (value.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"DOMAIN-KEYWORD,{value[8..]},{target}");
            }
            else if (value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"GEOSITE,{value[8..]},{target}");
            }
        }
    }

    private static void AddIpRules(List<string> rules, IEnumerable<string>? values, string target)
    {
        foreach (var raw in values ?? [])
        {
            var value = raw.Trim();
            if (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add($"GEOIP,{value[6..].ToUpperInvariant()},{target},no-resolve");
            }
            else if (value.Contains(':'))
            {
                rules.Add($"IP-CIDR6,{value},{target},no-resolve");
            }
            else if (value.Contains('/'))
            {
                rules.Add($"IP-CIDR,{value},{target},no-resolve");
            }
        }
    }

    private static string Target(string action)
        => SgSmartRoutingHelper.NormalizeAction(action) switch
        {
            SgSmartRoutingHelper.ActionDirect => "DIRECT",
            SgSmartRoutingHelper.ActionBlock => "REJECT",
            _ => ProxyGroupName,
        };

    private static string NormalizeProtocol(string? value)
        => string.Equals(value, "UDP", StringComparison.OrdinalIgnoreCase) ? "UDP" : "TCP";

    private static string NormalizeMultiplexing(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "MULTIPLEXING_OFF" or "MULTIPLEXING_LOW" or "MULTIPLEXING_MIDDLE" or "MULTIPLEXING_HIGH"
            ? normalized
            : "MULTIPLEXING_LOW";
    }

    private static string GetLogLevel(string? level)
        => string.Equals(level, "none", StringComparison.OrdinalIgnoreCase) ? "silent" : level.NullIfEmpty() ?? "warning";
}
