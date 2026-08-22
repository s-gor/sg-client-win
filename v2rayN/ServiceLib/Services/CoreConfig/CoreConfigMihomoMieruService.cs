using ServiceLib.Helper;

namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Generates a native Mihomo configuration for SG Client profiles handled by Mihomo.
/// Supports Mieru, AnyTLS and TUIC v5 while keeping SG Client ports, TUN mode, DNS and smart routing.
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

        var valid = node.ConfigType switch
        {
            EConfigType.Mieru => node.Address.IsNotEmpty() && node.Username.IsNotEmpty() && node.Password.IsNotEmpty() && bindings.Count > 0,
            EConfigType.Anytls => node.Address.IsNotEmpty() && node.Port is > 0 and <= 65535 && node.Password.IsNotEmpty(),
            EConfigType.TUIC => node.Address.IsNotEmpty() && node.Port is > 0 and <= 65535 && node.Username.IsNotEmpty() && node.Password.IsNotEmpty(),
            _ => false,
        };
        if (!valid)
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

    public RetResult GenerateClientSpeedtestConfig(int port)
    {
        if (port is <= 0 or > 65535)
        {
            return new RetResult { Msg = ResUI.CheckServerSettings };
        }

        var node = context.Node;
        var extra = node.GetProtocolExtra();
        var bindings = extra.MieruBindings is { Count: > 0 }
            ? extra.MieruBindings
            : [new MieruBindingItem { Port = node.Port.ToString(), Protocol = NormalizeProtocol(node.Network) }];

        var valid = node.ConfigType switch
        {
            EConfigType.Mieru => node.Address.IsNotEmpty() && node.Username.IsNotEmpty() && node.Password.IsNotEmpty() && bindings.Count > 0,
            EConfigType.Anytls => node.Address.IsNotEmpty() && node.Port is > 0 and <= 65535 && node.Password.IsNotEmpty(),
            EConfigType.TUIC => node.Address.IsNotEmpty() && node.Port is > 0 and <= 65535 && node.Username.IsNotEmpty() && node.Password.IsNotEmpty(),
            _ => false,
        };
        if (!valid)
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
            if (proxyNames.Count == 0)
            {
                return new RetResult { Msg = ResUI.CheckServerSettings };
            }

            // Latency testing must measure this profile itself, not the user's
            // current routing policy. Keep the temporary Mihomo instance local,
            // route every probe through SG-PROXY and do not expose a controller
            // or TUN interface that could conflict with the running client.
            var root = new Dictionary<string, object>
            {
                ["mixed-port"] = port,
                ["allow-lan"] = false,
                ["bind-address"] = Global.Loopback,
                ["mode"] = "rule",
                ["log-level"] = "warning",
                ["ipv6"] = config.ClashUIItem.EnableIPv6,
                ["unified-delay"] = true,
                ["tcp-concurrent"] = true,
                ["proxies"] = proxies,
                ["proxy-groups"] = new List<Dictionary<string, object>>
                {
                    new()
                    {
                        ["name"] = ProxyGroupName,
                        ["type"] = "select",
                        ["proxies"] = proxyNames,
                    }
                },
                ["dns"] = BuildSpeedtestDns(config.SimpleDNSItem, config.ClashUIItem.EnableIPv6),
                ["rules"] = new List<string> { $"MATCH,{ProxyGroupName}" },
            };

            return new RetResult
            {
                Success = true,
                Msg = string.Format(ResUI.SuccessfulConfiguration, node.GetSummary()),
                Data = YamlUtils.ToYaml(root),
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
        return node.ConfigType switch
        {
            EConfigType.Anytls => [BuildAnyTlsProxy(node)],
            EConfigType.TUIC => [BuildTuicProxy(node, extra)],
            _ => BuildMieruProxies(node, extra, bindings),
        };
    }

    private static List<Dictionary<string, object>> BuildMieruProxies(
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

    private static Dictionary<string, object> BuildAnyTlsProxy(ProfileItem node)
    {
        var proxy = BuildTlsProxyBase(node, "AnyTLS", "anytls");
        proxy["password"] = node.Password;
        proxy["udp"] = true;
        if (node.Fingerprint.IsNotEmpty())
        {
            proxy["client-fingerprint"] = node.Fingerprint;
        }
        return proxy;
    }

    private static Dictionary<string, object> BuildTuicProxy(ProfileItem node, ProtocolExtraItem extra)
    {
        var proxy = BuildTlsProxyBase(node, "TUIC v5", "tuic");
        proxy["uuid"] = node.Username;
        proxy["password"] = node.Password;
        proxy["udp-relay-mode"] = extra.TuicUdpRelayMode is "quic" ? "quic" : "native";
        proxy["congestion-controller"] = extra.CongestionControl is "cubic" or "new_reno" or "bbr"
            ? extra.CongestionControl
            : "bbr";
        return proxy;
    }

    private static Dictionary<string, object> BuildTlsProxyBase(ProfileItem node, string fallbackName, string type)
    {
        var proxy = new Dictionary<string, object>
        {
            ["name"] = node.Remarks.IsNotEmpty() ? node.Remarks : fallbackName,
            ["type"] = type,
            ["server"] = node.Address,
            ["port"] = node.Port,
            ["skip-cert-verify"] = node.GetAllowInsecure(),
        };
        if (node.Sni.IsNotEmpty())
        {
            proxy["sni"] = node.Sni;
        }
        var alpn = node.GetAlpn();
        if (alpn is { Count: > 0 })
        {
            proxy["alpn"] = alpn;
        }
        return proxy;
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

    private static Dictionary<string, object> BuildSpeedtestDns(SimpleDNSItem dns, bool ipv6)
    {
        var remote = ParseDnsList(dns?.RemoteDNS, Global.DomainRemoteDNSAddress.First());
        var bootstrap = ParseDnsList(dns?.BootstrapDNS, "1.1.1.1,8.8.8.8");
        return new Dictionary<string, object>
        {
            ["enable"] = true,
            ["ipv6"] = ipv6,
            ["enhanced-mode"] = "redir-host",
            ["nameserver"] = remote,
            ["proxy-server-nameserver"] = bootstrap,
        };
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
        if (smart.Preset == SgSmartRoutingHelper.PresetCustom)
        {
            AddDomainRules(rules, smart.CustomBlockDomains, "REJECT");
            AddIpRules(rules, smart.CustomBlockIps, "REJECT");
            AddDomainRules(rules, smart.CustomProxyDomains, ProxyGroupName);
            AddIpRules(rules, smart.CustomProxyIps, ProxyGroupName);
            AddDomainRules(rules, smart.CustomDirectDomains, "DIRECT");
            AddIpRules(rules, smart.CustomDirectIps, "DIRECT");
        }

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
            case SgSmartRoutingHelper.RussiaScopeWhiteIp:
                AddDomainRules(rules, SgRussiaRulesManager.Instance.GetWhiteDomains(), russiaTarget);
                AddIpRules(rules, SgRussiaRulesManager.Instance.GetWhiteIpCidrs(), russiaTarget);
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
