using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using Xunit;

namespace ServiceLib.Tests.Fmt;

public class MieruFmtTests
{
    [Fact]
    public void Resolve_ShouldParseRepeatedBindingsAndAdvancedFields()
    {
        const string uri =
            "mierus://demo%20user:secret%3Apass@node.example"
            + "?profile=Paris%20Mieru"
            + "&mtu=1400"
            + "&multiplexing=MULTIPLEXING_HIGH"
            + "&handshake-mode=HANDSHAKE_STANDARD"
            + "&traffic-pattern=YWJjZA%3D%3D"
            + "&port=2090-2099&protocol=TCP"
            + "&port=2999&protocol=UDP";

        var item = MieruFmt.Resolve(uri, out var message);

        item.Should().NotBeNull(message);
        item!.ConfigType.Should().Be(EConfigType.Mieru);
        item.CoreType.Should().Be(ECoreType.mihomo);
        item.Address.Should().Be("node.example");
        item.Port.Should().Be(2090);
        item.Username.Should().Be("demo user");
        item.Password.Should().Be("secret:pass");
        item.Remarks.Should().Be("Paris Mieru");
        item.Network.Should().Be("tcp");
        item.IsValid().Should().BeTrue();

        var extra = item.GetProtocolExtra();
        extra.MieruBindings.Should().HaveCount(2);
        extra.MieruBindings![0].Port.Should().Be("2090-2099");
        extra.MieruBindings[0].Protocol.Should().Be("TCP");
        extra.MieruBindings[1].Port.Should().Be("2999");
        extra.MieruBindings[1].Protocol.Should().Be("UDP");
        extra.MieruMtu.Should().Be(1400);
        extra.MieruMultiplexing.Should().Be("MULTIPLEXING_HIGH");
        extra.MieruHandshakeMode.Should().Be("HANDSHAKE_STANDARD");
        extra.MieruTrafficPattern.Should().Be("YWJjZA==");
    }

    [Fact]
    public void GetShareUriAndResolveConfig_ShouldRoundTripMieru()
    {
        const string uri =
            "mierus://user:pass@example.com?profile=Test&port=443&protocol=TCP&multiplexing=MULTIPLEXING_LOW";
        var first = FmtHandler.ResolveConfig(uri, out var firstMessage);

        first.Should().NotBeNull(firstMessage);
        var exported = FmtHandler.GetShareUri(first!);
        exported.Should().StartWith("mierus://");

        var second = FmtHandler.ResolveConfig(exported!, out var secondMessage);
        second.Should().NotBeNull(secondMessage);
        second!.ConfigType.Should().Be(EConfigType.Mieru);
        second.CoreType.Should().Be(ECoreType.mihomo);
        second.Address.Should().Be("example.com");
        second.Port.Should().Be(443);
        second.Username.Should().Be("user");
        second.Password.Should().Be("pass");
        second.GetProtocolExtra().MieruBindings.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_MismatchedPortAndProtocolCounts_ShouldFail()
    {
        const string uri =
            "mierus://user:pass@example.com?profile=Bad&port=443&port=444&protocol=TCP";

        var item = MieruFmt.Resolve(uri, out var message);

        item.Should().BeNull();
        message.Should().Contain("port").And.Contain("protocol");
    }
}
