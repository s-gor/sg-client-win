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
}
