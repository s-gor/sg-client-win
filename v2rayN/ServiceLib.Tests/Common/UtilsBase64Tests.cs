using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class UtilsBase64Tests
{
    [Fact]
    public void TryBase64Decode_UnpaddedSubscriptionWithBomAndLineBreaks_ShouldDecode()
    {
        const string payload =
            "anytls://password@example.com:443?sni=example.com&insecure=0#AnyTLS\r\n"
            + "vless://11111111-2222-4333-8444-555555555555@example.net:443?security=reality&type=tcp#VLESS";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=');
        var wrapped = "\uFEFF  " + encoded[..40] + "\r\n" + encoded[40..] + "  ";

        var success = Utils.TryBase64Decode(wrapped, out var decoded);

        success.Should().BeTrue();
        decoded.Should().Be(payload);
        Utils.IsBase64String(wrapped).Should().BeTrue();
    }

    [Fact]
    public void FlexibleBase64Subscription_DecodedLines_ShouldResolveSupportedProtocolsIndependently()
    {
        const string payload =
            "anytls://password@example.com:443?sni=example.com&insecure=0#AnyTLS\n"
            + "unsupported://value\n"
            + "vless://11111111-2222-4333-8444-555555555555@example.net:443?security=reality&type=tcp#VLESS";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=');

        Utils.TryBase64Decode(encoded, out var decoded).Should().BeTrue();
        var resolved = decoded
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => FmtHandler.ResolveConfig(line, out _))
            .Where(item => item != null)
            .ToList();

        resolved.Should().HaveCount(2);
        resolved.Select(item => item!.ConfigType).Should().Contain(EConfigType.Anytls);
        resolved.Select(item => item!.ConfigType).Should().Contain(EConfigType.VLESS);
    }
}
