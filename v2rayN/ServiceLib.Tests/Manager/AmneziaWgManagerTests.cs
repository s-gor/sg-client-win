using AwesomeAssertions;
using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests.Manager;

public class AmneziaWgManagerTests
{
    private const string Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void IsAmneziaConfig_ClusterProfileWithLargeRanges_ShouldBeAccepted()
    {
        var config =
            $"""
            # Name = CC1/Test-Client
            # Client = Test-Client
            # Server = CC2-Node
            # Source = SG-AWG-Panel Cluster
            [Interface]
            Address = 10.77.0.2/32
            DNS = 1.1.1.1, 1.0.0.1
            PrivateKey = {Key}
            MTU = 1280
            Jc = 6
            Jmin = 64
            Jmax = 128
            S1 = 48
            S2 = 48
            S3 = 32
            S4 = 16
            H1 = 550756890-554923388
            H2 = 2341829737-2349452404
            H3 = 3141955440-3146250630
            H4 = 1325007989-1328427967
            [Peer]
            PublicKey = {Key}
            PresharedKey = {Key}
            AllowedIPs = 0.0.0.0/0
            Endpoint = 192.0.2.10:585
            PersistentKeepalive = 25
            """;

        AmneziaWgManager.TryValidateAmneziaConfig(
                config,
                out var hasAmneziaParameters,
                out var error)
            .Should().BeTrue(error);
        hasAmneziaParameters.Should().BeTrue();
        AmneziaWgManager.GetSuggestedProfileName("AmneziaWG.conf", config)
            .Should().Be("CC1 Test-Client");
    }

    [Fact]
    public void IsAmneziaConfig_CollapsedClipboardText_ShouldBeAccepted()
    {
        var config =
            $"# Name = CC1/Test-Client # Client = Test-Client # Server = CC2-Node "
            + "# Source = SG-AWG-Panel Cluster [Interface] "
            + $"Address = 10.77.0.2/32 DNS = 1.1.1.1, 1.0.0.1 PrivateKey = {Key} "
            + "MTU = 1280 Jc = 6 Jmin = 64 Jmax = 128 S1 = 48 S2 = 48 S3 = 32 S4 = 16 "
            + "H1 = 550756890-554923388 H2 = 2341829737-2349452404 "
            + "H3 = 3141955440-3146250630 H4 = 1325007989-1328427967 [Peer] "
            + $"PublicKey = {Key} PresharedKey = {Key} AllowedIPs = 0.0.0.0/0 "
            + "Endpoint = 192.0.2.10:585 PersistentKeepalive = 25";

        AmneziaWgManager.IsAmneziaConfig(config).Should().BeTrue();
    }
    [Fact]
    public void GetSuggestedProfileName_SgGatewayAccessComment_ShouldUseAccessName()
    {
        var config =
            $"""
            # SG-Gateway AmneziaWG
            # Access: Sergey

            [Interface]
            PrivateKey = {Key}
            Address = 10.66.0.4/32
            DNS = 1.1.1.1
            Jc = 4
            Jmin = 40
            Jmax = 70
            S1 = 9
            S2 = 9
            S3 = 9
            S4 = 9
            H1 = 2211719691
            H2 = 2488385896
            H3 = 947123974
            H4 = 4290447877

            [Peer]
            PublicKey = {Key}
            Endpoint = 192.0.2.10:585
            AllowedIPs = 0.0.0.0/0, ::/0
            PersistentKeepalive = 25
            """;

        AmneziaWgManager.GetSuggestedProfileName("AmneziaWG.conf", config)
            .Should().Be("Sergey");
    }

    [Fact]
    public void InspectConfig_Awg3Parameters_ShouldReportVersion3()
    {
        var config =
            $"""
            # Name = AWG3 Test
            [Interface]
            Address = 10.88.0.2/32
            DNS = 1.1.1.1
            PrivateKey = {Key}
            Jc = 4
            Jmin = 40
            Jmax = 70
            S1 = 9
            S2 = 9
            S3 = 9
            S4 = 9
            H1 = 2211719691
            H2 = 2488385896
            H3 = 947123974
            H4 = 4290447877
            HeaderProtectionKey = {Key}
            ContentPaddingAddition = 16-96
            RekeyAfterTime = 120-180
            RekeyTimeout = 5-8
            RejectAfterTime = 240-300
            KeepaliveTimeout = 10-15
            MaxHandshakeAttempts = 10
            [Peer]
            PublicKey = {Key}
            AllowedIPs = 0.0.0.0/0, ::/0
            Endpoint = 192.0.2.20:585
            PersistentKeepalive = 22-30
            """;

        AmneziaWgManager.HasAmneziaParameterMarkers(config).Should().BeTrue();
        AmneziaWgManager.IsAmneziaConfig(config).Should().BeTrue();
        AmneziaWgManager.Instance.InspectConfig(config).Protocol.Should().Be("AmneziaWG 3.0");
    }

    [Fact]
    public void IsAmneziaConfig_CollapsedAwg3Text_ShouldBeAccepted()
    {
        var config =
            $"[Interface] Address = 10.88.0.2/32 PrivateKey = {Key} "
            + $"S1 = 9 S2 = 9 S3 = 9 S4 = 9 HeaderProtectionKey = {Key} ContentPaddingAddition = 16-96 "
            + "RekeyAfterTime = 120-180 RekeyTimeout = 5-8 RejectAfterTime = 240-300 "
            + "KeepaliveTimeout = 10-15 MaxHandshakeAttempts = 10 [Peer] "
            + $"PublicKey = {Key} AllowedIPs = 0.0.0.0/0 Endpoint = 192.0.2.20:585 "
            + "PersistentKeepalive = 22-30";

        AmneziaWgManager.IsAmneziaConfig(config).Should().BeTrue();
        AmneziaWgManager.Instance.InspectConfig(config).Protocol.Should().Be("AmneziaWG 3.0");
    }

    [Fact]
    public void IsAmneziaConfig_Awg3HeaderProtectionWithSmallPadding_ShouldBeRejected()
    {
        var config =
            $"""
            [Interface]
            Address = 10.88.0.2/32
            PrivateKey = {Key}
            S1 = 8
            S2 = 9
            S3 = 9
            S4 = 9
            HeaderProtectionKey = {Key}
            [Peer]
            PublicKey = {Key}
            AllowedIPs = 0.0.0.0/0
            Endpoint = 192.0.2.20:585
            """;

        AmneziaWgManager.TryValidateAmneziaConfig(config, out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("S1");
        error.Should().Contain("8");
    }

    [Fact]
    public void InspectConfig_Awg2Parameters_ShouldRemainVersion2()
    {
        var config =
            $"""
            [Interface]
            Address = 10.77.0.2/32
            PrivateKey = {Key}
            Jc = 4
            Jmin = 40
            Jmax = 70
            S1 = 0
            S2 = 0
            H1 = 2211719691
            H2 = 2488385896
            H3 = 947123974
            H4 = 4290447877
            [Peer]
            PublicKey = {Key}
            AllowedIPs = 0.0.0.0/0
            Endpoint = 192.0.2.10:585
            PersistentKeepalive = 25
            """;

        AmneziaWgManager.Instance.InspectConfig(config).Protocol.Should().Be("AmneziaWG 2.0");
    }

    [Fact]
    public void IsAmneziaConfig_PlainWireGuardWithKeepalive_ShouldRemainWireGuard()
    {
        var config =
            $"""
            [Interface]
            Address = 10.99.0.2/32
            PrivateKey = {Key}
            [Peer]
            PublicKey = {Key}
            AllowedIPs = 0.0.0.0/0
            Endpoint = 192.0.2.99:51820
            PersistentKeepalive = 25
            """;

        AmneziaWgManager.HasAmneziaParameterMarkers(config).Should().BeFalse();
        AmneziaWgManager.IsAmneziaConfig(config).Should().BeFalse();
    }


    [Theory]
    [InlineData("AmneziaWG 3.0", "AWG3")]
    [InlineData("AmneziaWG 2.0", "AWG2")]
    [InlineData("WireGuard", "")]
    [InlineData("", "")]
    public void GetProtocolBadge_ShouldShowOnlyKnownAwgGeneration(string protocol, string expected)
    {
        AmneziaWgManager.GetProtocolBadge(protocol).Should().Be(expected);
    }


    [Fact]
    public void BuildStaticCidrExclusions_WhiteListDirect_ShouldAddOnlyGranularCidrs()
    {
        var routing = new SgSmartRoutingItem
        {
            DefaultAction = SgSmartRoutingHelper.ActionProxy,
            RussiaScope = SgSmartRoutingHelper.RussiaScopeWhiteIp,
            RussiaAction = SgSmartRoutingHelper.ActionDirect,
        };

        var result = SgAwgWhiteListRouteManager.BuildStaticCidrExclusions(
            ["192.168.0.0/16"],
            routing,
            ["5.45.192.0/18", "185.89.12.0/24"]);

        result.Should().Contain("192.168.0.0/16");
        result.Should().Contain("5.45.192.0/18");
        result.Should().Contain("185.89.12.0/24");
        result.Should().NotContain("0.0.0.0/0");
    }

    [Fact]
    public void FilterDynamicAddressesOutsideStaticCidrs_ShouldKeepOnlyUncoveredPublicHosts()
    {
        var result = SgAwgWhiteListRouteManager.FilterDynamicAddressesOutsideStaticCidrs(
            [IPAddress.Parse("5.45.192.10"), IPAddress.Parse("203.0.113.44"), IPAddress.Parse("192.168.1.2")],
            ["5.45.192.0/18"]);

        result.Should().ContainSingle();
        result[0].Should().Be(IPAddress.Parse("203.0.113.44"));
    }
}
