using AwesomeAssertions;
using ServiceLib.Enums;
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
}
