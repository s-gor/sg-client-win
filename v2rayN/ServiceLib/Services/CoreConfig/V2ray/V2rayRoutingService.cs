namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private void GenRouting()
    {
        try
        {
            if (context.IsTunEnabled)
            {
                var tunRules = JsonUtils.Deserialize<List<RulesItem4Ray>>(EmbedUtils.GetEmbedText(Global.V2raySampleTunRules));
                if (tunRules != null)
                {
                    _coreConfig.routing.rules.AddRange(tunRules);
                }
                var (lstDnsExe, lstDirectExe) = BuildRoutingDirectExe();
                _coreConfig.routing.rules.Add(new()
                {
                    port = "53",
                    process = lstDnsExe,
                    outboundTag = Global.DnsOutboundTag,
                });
                _coreConfig.routing.rules.Add(new()
                {
                    process = lstDirectExe,
                    outboundTag = Global.DirectTag,
                });
                _coreConfig.routing.rules.Add(new()
                {
                    inboundTag = ["tun"],
                    port = "53",
                    outboundTag = Global.DnsOutboundTag,
                });
            }
            if (_coreConfig.routing?.rules != null)
            {
                _coreConfig.routing.domainStrategy = _config.RoutingBasicItem.DomainStrategy;

                var routing = context.RoutingItem;
                var sgRoutingAuthoritative = _config.SgQuickSettingsItem?.SmartRouting is not null;
                if (routing != null && !context.IsTunEnabled && !sgRoutingAuthoritative)
                {
                    if (routing.DomainStrategy.IsNotEmpty())
                    {
                        _coreConfig.routing.domainStrategy = routing.DomainStrategy;
                    }
                    var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet);
                    foreach (var item in rules ?? [])
                    {
                        if (!item.Enabled)
                        {
                            continue;
                        }

                        if (item.RuleType == ERuleType.DNS)
                        {
                            continue;
                        }

                        var item2 = JsonUtils.Deserialize<RulesItem4Ray>(JsonUtils.Serialize(item));
                        GenRoutingUserRule(item2);
                    }
                }

                // SG_ROUTING_AUTHORITATIVE_ALL_MODES_XRAY
                // SG_TUN_ROUTING_AUTHORITATIVE_XRAY
                // In TUN, the SG routing screen is the sole source of user
                // routing. This prevents an old v2rayN routing set (for example
                // UDP/443 block or geoip direct rules) from silently overriding
                // "Весь интернет через VPN" before the SG final proxy rule.
                if (sgRoutingAuthoritative)
                {
                    ApplySgSmartRouting4Ray();
                }

                var balancerTagList = _coreConfig.routing.balancers
                    ?.Select(p => p.tag)
                    .ToList() ?? [];
                if (balancerTagList.Count > 0)
                {
                    foreach (var rulesItem in _coreConfig.routing.rules.Where(r => balancerTagList.Contains(r.outboundTag + Global.BalancerTagSuffix)))
                    {
                        rulesItem.balancerTag = rulesItem.outboundTag + Global.BalancerTagSuffix;
                        rulesItem.outboundTag = null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private void GenRoutingUserRule(RulesItem4Ray? userRule)
    {
        try
        {
            if (userRule == null)
            {
                return;
            }
            userRule.outboundTag = GenRoutingUserRuleOutbound(userRule.outboundTag ?? Global.ProxyTag);

            if (userRule.port.IsNullOrEmpty())
            {
                userRule.port = null;
            }
            if (userRule.network.IsNullOrEmpty())
            {
                userRule.network = null;
            }
            if (userRule.domain?.Count == 0)
            {
                userRule.domain = null;
            }
            if (userRule.ip?.Count == 0)
            {
                userRule.ip = null;
            }
            if (userRule.protocol?.Count == 0)
            {
                userRule.protocol = null;
            }
            if (userRule.inboundTag?.Count == 0)
            {
                userRule.inboundTag = null;
            }
            if (userRule.process?.Count == 0)
            {
                userRule.process = null;
            }

            var hasDomainIp = false;
            if (userRule.domain?.Count > 0)
            {
                var it = JsonUtils.DeepCopy(userRule);
                it.ip = null;
                it.process = null;
                it.type = "field";
                for (var k = it.domain.Count - 1; k >= 0; k--)
                {
                    if (it.domain[k].StartsWith('#'))
                    {
                        it.domain.RemoveAt(k);
                    }
                    it.domain[k] = it.domain[k].Replace(Global.RoutingRuleComma, ",");
                }
                _coreConfig.routing.rules.Add(it);
                hasDomainIp = true;
            }
            if (userRule.ip?.Count > 0)
            {
                var it = JsonUtils.DeepCopy(userRule);
                it.domain = null;
                it.process = null;
                it.type = "field";
                _coreConfig.routing.rules.Add(it);
                hasDomainIp = true;
            }
            if (userRule.process?.Count > 0)
            {
                var it = JsonUtils.DeepCopy(userRule);
                it.domain = null;
                it.ip = null;
                it.type = "field";
                _coreConfig.routing.rules.Add(it);
                hasDomainIp = true;
            }
            if (!hasDomainIp)
            {
                if (userRule.port.IsNotEmpty()
                    || userRule.protocol?.Count > 0
                    || userRule.inboundTag?.Count > 0
                    || userRule.network != null
                    )
                {
                    var it = JsonUtils.DeepCopy(userRule);
                    it.type = "field";
                    _coreConfig.routing.rules.Add(it);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private string GenRoutingUserRuleOutbound(string outboundTag)
    {
        if (Global.OutboundTags.Contains(outboundTag))
        {
            return outboundTag;
        }

        var node = context.AllProxiesMap.GetValueOrDefault($"remark:{outboundTag}");

        if (node == null
            || (!Global.XraySupportConfigType.Contains(node.ConfigType)
            && !node.ConfigType.IsGroupType()))
        {
            return Global.ProxyTag;
        }

        var tag = $"{node.IndexId}-{Global.ProxyTag}-{node.Remarks}";
        if (_coreConfig.outbounds.Any(p => p.tag.StartsWith(tag)))
        {
            return tag;
        }

        var proxyOutbounds = new CoreConfigV2rayService(context with { Node = node, }).BuildAllProxyOutbounds(tag);
        _coreConfig.outbounds.AddRange(proxyOutbounds);
        if (proxyOutbounds.Count(n => n.tag.StartsWith(tag)) > 1)
        {
            var multipleLoad = node.GetProtocolExtra().MultipleLoad ?? EMultipleLoad.LeastPing;
            GenObservatory(multipleLoad, tag);
            GenBalancer(multipleLoad, tag);
        }

        return tag;
    }

    private RulesItem4Ray BuildFinalRule()
    {
        var finalRule = new RulesItem4Ray()
        {
            type = "field",
            network = "tcp,udp",
            outboundTag = Global.ProxyTag,
        };
        var balancer =
            _coreConfig?.routing?.balancers?.FirstOrDefault(b => b.tag == Global.ProxyTag + Global.BalancerTagSuffix, null);
        var domainStrategy = _coreConfig.routing?.domainStrategy ?? Global.AsIs;
        if (balancer is not null)
        {
            finalRule.outboundTag = null;
            finalRule.balancerTag = balancer.tag;
        }
        if (domainStrategy == Global.IPIfNonMatch)
        {
            finalRule.network = null;
            finalRule.ip = ["0.0.0.0/0", "::/0"];
        }
        return finalRule;
    }

    private void ApplySgSmartRouting4Ray()
    {
        if (_coreConfig.routing?.rules == null
            || _config.SgQuickSettingsItem?.SmartRouting is null)
        {
            return;
        }

        var item = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        var customPresetActive = item.Preset == SgSmartRoutingHelper.PresetCustom;
        if (SgSmartRoutingHelper.RequiresCommunityRules(item)
            || (customPresetActive
                && (item.CustomDirectIps.Count > 0
                    || item.CustomProxyIps.Count > 0
                    || item.CustomBlockIps.Count > 0)))
        {
            // Match domain rules first; only resolve to IP when no domain rule matched.
            _coreConfig.routing.domainStrategy = Global.IPIfNonMatch;
        }

        AddSgRayIpRule(["geoip:private"], item.LocalNetworkAction);

        // Stored custom lists are preserved when switching presets, but they
        // are active only in "Пользовательская". Otherwise an old Direct entry
        // could silently violate "Весь интернет через VPN".
        if (customPresetActive)
        {
            AddSgRayDomainRule(item.CustomBlockDomains, SgSmartRoutingHelper.ActionBlock);
            AddSgRayIpRule(item.CustomBlockIps, SgSmartRoutingHelper.ActionBlock);
            AddSgRayDomainRule(item.CustomDirectDomains, SgSmartRoutingHelper.ActionDirect);
            AddSgRayIpRule(item.CustomDirectIps, SgSmartRoutingHelper.ActionDirect);
            AddSgRayDomainRule(item.CustomProxyDomains, SgSmartRoutingHelper.ActionProxy);
            AddSgRayIpRule(item.CustomProxyIps, SgSmartRoutingHelper.ActionProxy);
        }

        if (SgSmartRoutingHelper.RequiresCommunityRules(item))
        {
            if (item.AdsAction != item.DefaultAction)
            {
                AddSgRayDomainRule(["geosite:category-ads-all"], item.AdsAction);
            }

            // Blocked rules must precede Russian IP rules: a blocked Russian address still goes through VPN.
            if (item.BlockedAction != item.DefaultAction)
            {
                AddSgRayDomainRule(["geosite:ru-blocked"], item.BlockedAction);
                AddSgRayIpRule(["geoip:ru-blocked"], item.BlockedAction);
            }

            // The two Russian presets are mutually exclusive:
            // tld-ru matches only Russian domain zones; category-ru already includes tld-ru
            // and also covers Russian services in other TLDs. Do not generate regex duplicates.
            AddSgRayDomainRule(SgSmartRoutingHelper.GetRussiaDomainRules(item), item.RussiaAction);
            AddSgRayIpRule(SgSmartRoutingHelper.GetRussiaIpRules(item), item.RussiaAction);
        }

        // Make the default action explicit in every SG Client mode. In TUN the
        // rule is scoped to the TUN inbound; in Local/System Proxy it applies
        // to the mixed inbound. This keeps the Routing screen authoritative
        // and prevents a legacy routing set from silently changing Direct to VPN.
        var finalRule = new RulesItem4Ray
        {
            type = "field",
            network = "tcp,udp",
            outboundTag = SgSmartRoutingHelper.ToOutboundTag(item.DefaultAction),
        };
        if (context.IsTunEnabled)
        {
            finalRule.inboundTag = ["tun"];
        }
        else
        {
            finalRule.inboundTag = _coreConfig.inbounds?
                .Where(inbound => inbound.tag.IsNotEmpty()
                    && !string.Equals(inbound.tag, "tun", StringComparison.OrdinalIgnoreCase))
                .Select(inbound => inbound.tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        _coreConfig.routing.rules.Add(finalRule);
    }

    private void AddSgRayDomainRule(IEnumerable<string>? domains, string action)
    {
        var normalizedAction = SgSmartRoutingHelper.NormalizeAction(action);
        var values = domains?.Where(value => value.IsNotEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (normalizedAction == SgSmartRoutingHelper.ActionNone || values.Count == 0)
        {
            return;
        }

        _coreConfig.routing.rules.Add(new RulesItem4Ray
        {
            type = "field",
            domain = values,
            outboundTag = SgSmartRoutingHelper.ToOutboundTag(normalizedAction),
        });
    }

    private void AddSgRayIpRule(IEnumerable<string>? addresses, string action)
    {
        var normalizedAction = SgSmartRoutingHelper.NormalizeAction(action);
        var values = addresses?.Where(value => value.IsNotEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (normalizedAction == SgSmartRoutingHelper.ActionNone || values.Count == 0)
        {
            return;
        }

        _coreConfig.routing.rules.Add(new RulesItem4Ray
        {
            type = "field",
            ip = values,
            outboundTag = SgSmartRoutingHelper.ToOutboundTag(normalizedAction),
        });
    }

    private static (List<string> lstDnsExe, List<string> lstDirectExe) BuildRoutingDirectExe()
    {
        var dnsExeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directExeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var coreInfoResult = CoreInfoManager.Instance.GetCoreInfo();

        foreach (var coreConfig in coreInfoResult)
        {
            if (coreConfig.CoreType == ECoreType.v2rayN)
            {
                continue;
            }

            foreach (var baseExeName in coreConfig.CoreExes)
            {
                if (coreConfig.CoreType != ECoreType.Xray)
                {
                    dnsExeSet.Add(Utils.GetExeName(baseExeName));
                }
                directExeSet.Add(Utils.GetExeName(baseExeName));
            }
        }

        directExeSet.Add("xray/");
        directExeSet.Add("self/");

        var splitApps = AppManager.Instance.Config.SgQuickSettingsItem?.SplitTunnelApplications ?? [];
        foreach (var application in splitApps.Where(item => item.IsNotEmpty()))
        {
            directExeSet.Add(System.IO.Path.GetFileName(application));
        }

        var lstDnsExe = new List<string>(dnsExeSet);
        var lstDirectExe = new List<string>(directExeSet);

        return (lstDnsExe, lstDirectExe);
    }
}
