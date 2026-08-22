using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using Xunit;

namespace ServiceLib.Tests.Fmt;

public class FmtHandlerTests
{
    [Fact]
    public void GetShareUriAndResolveConfig_Vmess_ShouldRoundTripBasicFields()
    {
        var source = CreateVmessProfile();

        var resolved = ExportThenImport(source);

        resolved.ConfigType.Should().Be(EConfigType.VMess);
        resolved.Remarks.Should().Be(source.Remarks);
        resolved.Address.Should().Be(source.Address);
        resolved.Port.Should().Be(source.Port);
        resolved.Password.Should().Be(source.Password);
        resolved.GetProtocolExtra().AlterId.Should().Be(source.GetProtocolExtra().AlterId);
    }

    [Fact]
    public void GetShareUriAndResolveConfig_Vless_ShouldRoundTripBasicFields()
    {
        var source = CreateVlessProfile();

        var resolved = ExportThenImport(source);

        resolved.ConfigType.Should().Be(EConfigType.VLESS);
        resolved.Remarks.Should().Be(source.Remarks);
        resolved.Address.Should().Be(source.Address);
        resolved.Port.Should().Be(source.Port);
        resolved.Password.Should().Be(source.Password);
        resolved.GetProtocolExtra().VlessEncryption.Should().Be(Global.None);
    }


    [Fact]
    public void ResolveConfig_ModernVlessEncryptionXhttp_ShouldPreserveExtendedFields()
    {
        const string encryption =
            "mlkem768x25519plus.native.0rtt.ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd";
        const string extra =
            "{\"mode\":\"stream-one\",\"xPaddingMethod\":\"tokenish\",\"xPaddingObfsMode\":true}";
        const string finalMask =
            "{\"tcp\":[{\"type\":\"fragment\",\"settings\":{\"packets\":\"tlshello\"}}]}";
        var uri =
            $"vless://11111111-2222-4333-8444-555555555555@example.com:443"
            + $"?encryption={encryption}"
            + $"&extra={Uri.EscapeDataString(extra)}"
            + "&flow=xtls-rprx-vision"
            + $"&fm={Uri.EscapeDataString(finalMask)}"
            + "&fp=firefox&host=runtime.example.com&mode=stream-one"
            + "&path=%2Fplayer%2Fvideo%2F&pbk=public-key&security=reality"
            + "&sid=2b6a&sni=stream.example.com&spx=%2Ftest"
            + "&type=xhttp&x_padding_bytes=64-1420&future_parameter=alpha=beta"
            + "#Modern%20VLESS";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.ConfigType.Should().Be(EConfigType.VLESS);
        resolved.CoreType.Should().Be(ECoreType.Xray);
        resolved.Network.Should().Be(nameof(ETransport.xhttp));
        resolved.GetProtocolExtra().VlessEncryption.Should().Be(encryption);
        resolved.GetProtocolExtra().Flow.Should().Be("xtls-rprx-vision");
        resolved.GetTransportExtra().XhttpMode.Should().Be("stream-one");
        resolved.GetTransportExtra().Host.Should().Be("runtime.example.com");
        resolved.GetTransportExtra().Path.Should().Be("/player/video/");
        resolved.GetTransportExtra().XhttpExtra.Should().Contain("xPaddingBytes");
        resolved.GetTransportExtra().XhttpExtra.Should().Contain("64-1420");
        resolved.Finalmask.Should().Contain("tlshello");
    }


    [Fact]
    public void ResolveConfig_LongPanelVlessXhttpLink_ShouldRemainValid()
    {
        const string extra =
            "{\"mode\":\"stream-one\",\"seqKey\":\"X-Connection-ID\","
            + "\"seqPlacement\":\"header\",\"sessionIDKey\":\"X-Strm-Session\","
            + "\"sessionIDLength\":\"16-32\",\"sessionIDPlacement\":\"header\","
            + "\"sessionIDTable\":\"Base62\",\"uplinkHTTPMethod\":\"POST\","
            + "\"xPaddingBytes\":\"64-1420\",\"xPaddingHeader\":\"X-Strm-Log-Split\","
            + "\"xPaddingKey\":\"x-padding\",\"xPaddingMethod\":\"tokenish\","
            + "\"xPaddingObfsMode\":true,\"xPaddingPlacement\":\"header\","
            + "\"xmux\":{\"cMaxReuseTimes\":\"300-600\",\"hKeepAlivePeriod\":600,"
            + "\"hMaxRequestTimes\":\"1000-2000\",\"hMaxReusableSecs\":\"1200-2400\","
            + "\"maxConcurrency\":\"0\",\"maxConnections\":\"2-4\"}}";
        const string finalMask =
            "{\"tcp\":[{\"settings\":{\"delays\":[\"10-20\",\"5-20\"],"
            + "\"lengths\":[\"5-10\",\"10-15\"],\"maxSplit\":\"10-15\","
            + "\"packets\":\"tlshello\"},\"type\":\"fragment\"}]}";
        var uri =
            "vless://11111111-2222-4333-8444-555555555555@node.example:443"
            + "?encryption=mlkem768x25519plus.native.0rtt.ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd"
            + $"&extra={Uri.EscapeDataString(extra)}"
            + "&flow=xtls-rprx-vision"
            + $"&fm={Uri.EscapeDataString(finalMask)}"
            + "&fp=firefox&host=runtime.example.net&mode=stream-one"
            + "&path=%2Fplayer%2Fvideo%2F&pbk=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdEFGH"
            + "&security=reality&sid=2b6a&sni=stream.example.net&spx=%2Ftest"
            + "&type=xhttp&x_padding_bytes=64-1420"
            + "#%F0%9F%87%B7%F0%9F%87%BA%D0%A2%D0%B5%D1%81%D1%82";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.IsValid().Should().BeTrue();
        resolved.CoreType.Should().Be(ECoreType.Xray);
        resolved.Network.Should().Be(nameof(ETransport.xhttp));
        resolved.GetProtocolExtra().VlessEncryption.Should().StartWith("mlkem768x25519plus.native.0rtt.");
        resolved.GetProtocolExtra().Flow.Should().Be("xtls-rprx-vision");
        resolved.GetTransportExtra().XhttpExtra.Should().Contain("X-Strm-Session");
        resolved.GetTransportExtra().XhttpExtra.Should().Contain("maxConnections");
        resolved.Finalmask.Should().Contain("tlshello");
    }

    [Fact]
    public void GetShareUriAndResolveConfig_Shadowsocks_ShouldRoundTripBasicFields()
    {
        var source = CreateShadowsocksProfile();

        var resolved = ExportThenImport(source);

        resolved.ConfigType.Should().Be(EConfigType.Shadowsocks);
        resolved.Remarks.Should().Be(source.Remarks);
        resolved.Address.Should().Be(source.Address);
        resolved.Port.Should().Be(source.Port);
        resolved.Password.Should().Be(source.Password);
        resolved.GetProtocolExtra().SsMethod.Should().Be(source.GetProtocolExtra().SsMethod);
    }

    [Fact]
    public void GetShareUriAndResolveConfig_Socks_ShouldRoundTripBasicFields()
    {
        var source = CreateSocksProfile();

        var resolved = ExportThenImport(source);

        resolved.ConfigType.Should().Be(EConfigType.SOCKS);
        resolved.Remarks.Should().Be(source.Remarks);
        resolved.Address.Should().Be(source.Address);
        resolved.Port.Should().Be(source.Port);
        resolved.Username.Should().Be(source.Username);
        resolved.Password.Should().Be(source.Password);
    }

    [Fact]
    public void ResolveConfig_Hysteria2Gecko_ShouldImportAndPinXray()
    {
        const string uri =
            "hysteria2://auth-password@hy2.example.com:443/?obfs=gecko&obfs-password=mask-password#Gecko";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.ConfigType.Should().Be(EConfigType.Hysteria2);
        resolved.CoreType.Should().Be(ECoreType.Xray);
        resolved.GetProtocolExtra().HyObfsType.Should().Be(Hysteria2ObfsHelper.Gecko);
        resolved.GetProtocolExtra().SalamanderPass.Should().Be("mask-password");
    }

    [Fact]
    public void ResolveConfig_ExactSgGatewayGeckoLink_ShouldPreserveTlsObfsAndBbrSemantics()
    {
        const string uri =
            "hysteria2://wy1CDHjWv_jIgY0sucpu_WgIWR0YSaRh@infosec.opik.net:8446/?sni=infosec.opik.net&insecure=0&obfs=gecko&obfs-password=3k4HsopGTbu1jYidTOWGkRlkZgSRnrv8#Shany%20%C2%B7%20Hysteria%202";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.ConfigType.Should().Be(EConfigType.Hysteria2);
        resolved.CoreType.Should().Be(ECoreType.Xray);
        resolved.Address.Should().Be("infosec.opik.net");
        resolved.Port.Should().Be(8446);
        resolved.Password.Should().Be("wy1CDHjWv_jIgY0sucpu_WgIWR0YSaRh");
        resolved.StreamSecurity.Should().Be(Global.StreamSecurity);
        resolved.Sni.Should().Be("infosec.opik.net");
        resolved.GetAllowInsecure().Should().BeFalse();
        resolved.Remarks.Should().Be("Shany · Hysteria 2");
        var extra = resolved.GetProtocolExtra();
        Hysteria2ObfsHelper.GetEffectiveType(extra).Should().Be(Hysteria2ObfsHelper.Gecko);
        extra.SalamanderPass.Should().Be("3k4HsopGTbu1jYidTOWGkRlkZgSRnrv8");
        extra.GeckoMinPacketSize.Should().Be("512");
        extra.GeckoMaxPacketSize.Should().Be("1200");
        extra.UpMbps.Should().BeNull();
        extra.DownMbps.Should().BeNull();
    }

    [Fact]
    public void GetShareUriAndResolveConfig_Hysteria2Gecko_ShouldRoundTripObfsType()
    {
        var source = new ProfileItem
        {
            ConfigType = EConfigType.Hysteria2,
            CoreType = ECoreType.Xray,
            Remarks = "gecko demo",
            Address = "hy2.example.com",
            Port = 443,
            Password = "auth-password",
            StreamSecurity = Global.StreamSecurity,
        };
        source.SetProtocolExtra(new ProtocolExtraItem
        {
            HyObfsType = Hysteria2ObfsHelper.Gecko,
            SalamanderPass = "mask-password",
        });

        var resolved = ExportThenImport(source);

        resolved.CoreType.Should().Be(ECoreType.Xray);
        resolved.GetProtocolExtra().HyObfsType.Should().Be(Hysteria2ObfsHelper.Gecko);
        resolved.GetProtocolExtra().SalamanderPass.Should().Be("mask-password");
    }

    [Fact]
    public void GetShareUriAndResolveConfig_LegacyHysteria2ObfsPassword_ShouldStaySalamander()
    {
        var source = new ProfileItem
        {
            ConfigType = EConfigType.Hysteria2,
            CoreType = ECoreType.sing_box,
            Remarks = "legacy salamander",
            Address = "hy2.example.com",
            Port = 443,
            Password = "auth-password",
            StreamSecurity = Global.StreamSecurity,
        };
        source.SetProtocolExtra(new ProtocolExtraItem { SalamanderPass = "legacy-mask", });

        var uri = FmtHandler.GetShareUri(source);
        uri.Should().Contain("obfs=salamander");

        var resolved = FmtHandler.ResolveConfig(uri!, out var msg);
        resolved.Should().NotBeNull(msg);
        Hysteria2ObfsHelper.GetEffectiveType(resolved!.GetProtocolExtra()).Should()
            .Be(Hysteria2ObfsHelper.Salamander);
    }

    [Fact]
    public void ResolveConfig_Hysteria2UnknownObfs_ShouldRejectInsteadOfFallingBackToSalamander()
    {
        const string uri =
            "hysteria2://auth-password@hy2.example.com:443/?obfs=future-obfs&obfs-password=mask-password";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().BeNull();
        msg.Should().Contain("Unsupported Hysteria2 obfs type");
    }

    [Fact]
    public void ResolveConfig_UnsupportedProtocol_ShouldReturnNull()
    {
        var resolved = FmtHandler.ResolveConfig("not-a-share-uri", out var msg);

        resolved.Should().BeNull();
        msg.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetShareUri_UnsupportedConfigType_ShouldReturnNull()
    {
        var item = new ProfileItem { ConfigType = EConfigType.PolicyGroup, Remarks = "group", };

        var uri = FmtHandler.GetShareUri(item);

        uri.Should().BeNull();
    }

    [Fact]
    public void ResolveConfig_ExactAnyTlsLink_ShouldPreserveMihomoTlsEndpoint()
    {
        const string uri =
            "anytls://Et1aL5Cms0WroQxD_5O7C3PRRvNib_FATk614w@infosec.opik.net:9443?security=tls&sni=infosec.opik.net&fp=firefox&type=tcp#Shany%20%C2%B7%20AnyTLS";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.ConfigType.Should().Be(EConfigType.Anytls);
        resolved.CoreType.Should().Be(ECoreType.mihomo);
        resolved.Address.Should().Be("infosec.opik.net");
        resolved.Port.Should().Be(9443);
        resolved.Password.Should().Be("Et1aL5Cms0WroQxD_5O7C3PRRvNib_FATk614w");
        resolved.StreamSecurity.Should().Be(Global.StreamSecurity);
        resolved.Sni.Should().Be("infosec.opik.net");
        resolved.Fingerprint.Should().Be("firefox");
        resolved.Network.Should().Be(nameof(ETransport.raw));
        resolved.Remarks.Should().Be("Shany · AnyTLS");
    }

    [Fact]
    public void ResolveConfig_ExactTuicV5Link_ShouldPreserveMihomoTlsEndpoint()
    {
        const string uri =
            "tuic://ac5d2a30-c473-4ddd-8b0a-410a3d138526:UqrhT6qg3ogiFxxPAEJzUYMwZ6NsWkib@infosec.opik.net:10443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=infosec.opik.net#Shany%20%C2%B7%20TUIC%20v5";

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull(msg);
        resolved!.ConfigType.Should().Be(EConfigType.TUIC);
        resolved.CoreType.Should().Be(ECoreType.mihomo);
        resolved.Address.Should().Be("infosec.opik.net");
        resolved.Port.Should().Be(10443);
        resolved.Username.Should().Be("ac5d2a30-c473-4ddd-8b0a-410a3d138526");
        resolved.Password.Should().Be("UqrhT6qg3ogiFxxPAEJzUYMwZ6NsWkib");
        resolved.StreamSecurity.Should().Be(Global.StreamSecurity);
        resolved.Sni.Should().Be("infosec.opik.net");
        resolved.Alpn.Should().Be("h3");
        resolved.GetProtocolExtra().CongestionControl.Should().Be("bbr");
        resolved.GetProtocolExtra().TuicUdpRelayMode.Should().Be("native");
        resolved.Remarks.Should().Be("Shany · TUIC v5");
    }

    private static ProfileItem ExportThenImport(ProfileItem source)
    {
        var uri = FmtHandler.GetShareUri(source);

        uri.Should().NotBeNullOrWhiteSpace();
        uri!.StartsWith(Global.ProtocolShares[source.ConfigType], StringComparison.OrdinalIgnoreCase).Should()
            .BeTrue();

        var resolved = FmtHandler.ResolveConfig(uri, out var msg);

        resolved.Should().NotBeNull($"uri: {uri}, msg: {msg}");
        return resolved!;
    }

    private static ProfileItem CreateVmessProfile()
    {
        var item = new ProfileItem
        {
            ConfigType = EConfigType.VMess,
            Remarks = "vmess demo",
            Address = "example.com",
            Port = 443,
            Password = Guid.NewGuid().ToString(),
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
        };

        item.SetProtocolExtra(new ProtocolExtraItem { AlterId = "0", VmessSecurity = Global.DefaultSecurity, });
        item.SetTransportExtra(new TransportExtraItem { RawHeaderType = Global.None, });

        return item;
    }

    private static ProfileItem CreateVlessProfile()
    {
        var item = new ProfileItem
        {
            ConfigType = EConfigType.VLESS,
            Remarks = "vless demo",
            Address = "vless.example",
            Port = 8443,
            Password = Guid.NewGuid().ToString(),
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
        };

        item.SetProtocolExtra(new ProtocolExtraItem { VlessEncryption = Global.None, });
        item.SetTransportExtra(new TransportExtraItem { RawHeaderType = Global.None, });

        return item;
    }

    private static ProfileItem CreateShadowsocksProfile()
    {
        var item = new ProfileItem
        {
            ConfigType = EConfigType.Shadowsocks,
            Remarks = "ss demo",
            Address = "1.2.3.4",
            Port = 8388,
            Password = "pass123",
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
        };

        item.SetProtocolExtra(new ProtocolExtraItem { SsMethod = "aes-128-gcm", });
        item.SetTransportExtra(new TransportExtraItem { RawHeaderType = Global.None, });

        return item;
    }

    private static ProfileItem CreateSocksProfile()
    {
        return new ProfileItem
        {
            ConfigType = EConfigType.SOCKS,
            Remarks = "socks demo",
            Address = "127.0.0.1",
            Port = 1080,
            Username = "user",
            Password = "pass",
        };
    }
}
