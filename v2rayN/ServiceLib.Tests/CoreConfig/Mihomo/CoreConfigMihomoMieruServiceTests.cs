using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig.Mihomo;

public class CoreConfigMihomoMieruServiceTests
{
    [Fact]
    public void GenerateClientConfigContent_ShouldGenerateMieruProxiesAndSmartRouting()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        config.SgQuickSettingsItem = new SgQuickSettingsItem
        {
            RoutingMode = "custom",
            SmartRouting = new SgSmartRoutingItem
            {
                Preset = "custom",
                LocalNetworkAction = "direct",
                RussiaScope = "none",
                BlockedAction = "proxy",
                AdsAction = "block",
                DefaultAction = "direct",
                CustomProxyDomains = ["domain:only-vpn.example"],
                CustomBlockDomains = ["full:blocked.example"],
            },
        };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateMieruNode();
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().NotBeNullOrWhiteSpace();
        yaml.Should().Contain("type: mieru");
        yaml.Should().Contain("server: mieru.example.com");
        yaml.Should().Contain("port-range: 40000-40010");
        yaml.Should().Contain("port: 41000");
        yaml.Should().Contain("transport: TCP");
        yaml.Should().Contain("transport: UDP");
        yaml.Should().Contain("multiplexing: MULTIPLEXING_HIGH");
        yaml.Should().Contain("traffic-pattern: pattern-a");
        yaml.Should().Contain("DOMAIN-SUFFIX,only-vpn.example,SG-PROXY");
        yaml.Should().Contain("DOMAIN,blocked.example,REJECT");
        yaml.Should().Contain("MATCH,DIRECT");
    }

    [Fact]
    public void GenerateClientConfigContent_TunEnabled_ShouldIncludeTunBlock()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        config.TunModeItem.EnableTun = true;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateMieruNode();
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo) with
        {
            IsTunEnabled = true,
        };

        var result = new CoreConfigMihomoMieruService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        result.Data?.ToString().Should().Contain("tun:");
    }
    [Fact]
    public void GenerateClientConfigContent_ExactAnyTlsLink_ShouldEmitMihomoAnyTls9443()
    {
        const string uri =
            "anytls://Et1aL5Cms0WroQxD_5O7C3PRRvNib_FATk614w@infosec.opik.net:9443?security=tls&sni=infosec.opik.net&fp=firefox&type=tcp#Shany%20%C2%B7%20AnyTLS";
        var node = FmtHandler.ResolveConfig(uri, out var msg);
        node.Should().NotBeNull(msg);
        node!.CoreType.Should().Be(ECoreType.mihomo);

        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().NotBeNullOrWhiteSpace();
        yaml.Should().Contain("type: anytls");
        yaml.Should().Contain("server: infosec.opik.net");
        yaml.Should().Contain("port: 9443");
        yaml.Should().Contain("password: Et1aL5Cms0WroQxD_5O7C3PRRvNib_FATk614w");
        yaml.Should().Contain("sni: infosec.opik.net");
        yaml.Should().Contain("client-fingerprint: firefox");
        yaml.Should().Contain("skip-cert-verify: false");
    }

    [Fact]
    public void GenerateClientConfigContent_ExactTuicV5Link_ShouldEmitMihomoTuic10443()
    {
        const string uri =
            "tuic://ac5d2a30-c473-4ddd-8b0a-410a3d138526:UqrhT6qg3ogiFxxPAEJzUYMwZ6NsWkib@infosec.opik.net:10443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=infosec.opik.net#Shany%20%C2%B7%20TUIC%20v5";
        var node = FmtHandler.ResolveConfig(uri, out var msg);
        node.Should().NotBeNull(msg);
        node!.CoreType.Should().Be(ECoreType.mihomo);

        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().NotBeNullOrWhiteSpace();
        yaml.Should().Contain("type: tuic");
        yaml.Should().Contain("server: infosec.opik.net");
        yaml.Should().Contain("port: 10443");
        yaml.Should().Contain("uuid: ac5d2a30-c473-4ddd-8b0a-410a3d138526");
        yaml.Should().Contain("password: UqrhT6qg3ogiFxxPAEJzUYMwZ6NsWkib");
        yaml.Should().Contain("udp-relay-mode: native");
        yaml.Should().Contain("congestion-controller: bbr");
        yaml.Should().Contain("sni: infosec.opik.net");
        yaml.Should().Contain("h3");
        yaml.Should().Contain("skip-cert-verify: false");
    }

    [Fact]
    public void GenerateClientConfigContent_NonCustomPreset_ShouldIgnoreStoredCustomRules()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        config.SgQuickSettingsItem = new SgQuickSettingsItem
        {
            RoutingMode = SgSmartRoutingHelper.PresetGlobal,
            SmartRouting = new SgSmartRoutingItem
            {
                Preset = SgSmartRoutingHelper.PresetGlobal,
                RussiaScope = SgSmartRoutingHelper.RussiaScopeNone,
                DefaultAction = SgSmartRoutingHelper.ActionProxy,
                CustomDirectDomains = ["domain:stale-direct.example"],
                CustomBlockIps = ["203.0.113.0/24"],
            },
        };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateMieruNode();
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().NotBeNullOrWhiteSpace();
        yaml.Should().NotContain("stale-direct.example");
        yaml.Should().NotContain("203.0.113.0/24");
        yaml.Should().Contain("MATCH,SG-PROXY");
    }

    [Fact]
    public void GenerateClientSpeedtestConfig_UdpOnlyMieru_ShouldUseActualUdpBindingAndDedicatedLocalPort()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateMieruNode();
        node.Network = "udp";
        node.SetProtocolExtra(node.GetProtocolExtra() with
        {
            MieruBindings = [new MieruBindingItem { Port = "41000", Protocol = "UDP" }],
        });
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientSpeedtestConfig(24001);

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().NotBeNullOrWhiteSpace();
        yaml.Should().Contain("mixed-port: 24001");
        yaml.Should().Contain("type: mieru");
        yaml.Should().Contain("transport: UDP");
        yaml.Should().Contain("port: 41000");
        yaml.Should().Contain("MATCH,SG-PROXY");
        yaml.Should().NotContain("external-controller");
        yaml.Should().NotContain("tun:");
        yaml.Should().NotContain("geosite:");
    }

    [Fact]
    public void GenerateClientSpeedtestConfig_TuicV5_ShouldKeepRemoteUdpPort()
    {
        const string uri =
            "tuic://ac5d2a30-c473-4ddd-8b0a-410a3d138526:UqrhT6qg3ogiFxxPAEJzUYMwZ6NsWkib@infosec.opik.net:10443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=infosec.opik.net#Shany%20%C2%B7%20TUIC%20v5";
        var node = FmtHandler.ResolveConfig(uri, out var msg);
        node.Should().NotBeNull(msg);
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node!, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientSpeedtestConfig(24002);

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().Contain("mixed-port: 24002");
        yaml.Should().Contain("type: tuic");
        yaml.Should().Contain("server: infosec.opik.net");
        yaml.Should().Contain("port: 10443");
        yaml.Should().Contain("MATCH,SG-PROXY");
    }

    [Fact]
    public void GenerateClientSpeedtestConfig_AnyTls_ShouldKeepRemoteTcpPort()
    {
        const string uri =
            "anytls://Et1aL5Cms0WroQxD_5O7C3PRRvNib_FATk614w@infosec.opik.net:9443?security=tls&sni=infosec.opik.net&fp=firefox&type=tcp#Shany%20%C2%B7%20AnyTLS";
        var node = FmtHandler.ResolveConfig(uri, out var msg);
        node.Should().NotBeNull(msg);
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node!, ECoreType.mihomo);

        var result = new CoreConfigMihomoMieruService(context).GenerateClientSpeedtestConfig(24003);

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var yaml = result.Data?.ToString();
        yaml.Should().Contain("mixed-port: 24003");
        yaml.Should().Contain("type: anytls");
        yaml.Should().Contain("server: infosec.opik.net");
        yaml.Should().Contain("port: 9443");
        yaml.Should().Contain("MATCH,SG-PROXY");
    }

}
