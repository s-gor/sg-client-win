namespace ServiceLib.Models.Entities;

public record ProtocolExtraItem
{
    public bool? Uot { get; init; }
    public string? CongestionControl { get; init; }
    public string? TuicUdpRelayMode { get; init; }

    // vmess
    public string? AlterId { get; init; }
    public string? VmessSecurity { get; init; }

    // vless
    public string? Flow { get; init; }
    public string? VlessEncryption { get; init; }
    //public string? VisionSeed { get; init; }

    // shadowsocks
    //public string? PluginArgs { get; init; }
    public string? SsMethod { get; init; }

    // wireguard
    public string? WgPublicKey { get; init; }
    public string? WgPresharedKey { get; init; }
    public string? WgInterfaceAddress { get; init; }
    public string? WgReserved { get; init; }
    public int? WgMtu { get; init; }

    // hysteria2
    // Null keeps the legacy meaning: Salamander when SalamanderPass is present.
    public string? HyObfsType { get; init; }
    public string? SalamanderPass { get; init; }
    // Upstream v2rayN Gecko representation. Keeping these explicit packet-size
    // fields makes imported profiles compatible with the official Gecko path.
    public string? GeckoMinPacketSize { get; init; }
    public string? GeckoMaxPacketSize { get; init; }
    public int? UpMbps { get; init; }
    public int? DownMbps { get; init; }
    public string? Ports { get; init; }
    public string? HopInterval { get; init; }

    // naiveproxy
    public int? InsecureConcurrency { get; init; }
    public bool? NaiveQuic { get; init; }

    // mieru (mihomo)
    public List<MieruBindingItem>? MieruBindings { get; init; }
    public string? MieruProfile { get; init; }
    public int? MieruMtu { get; init; }
    public string? MieruMultiplexing { get; init; }
    public string? MieruHandshakeMode { get; init; }
    public string? MieruTrafficPattern { get; init; }

    // group profile
    public string? GroupType { get; init; }
    public string? ChildItems { get; init; }
    public string? SubChildItems { get; init; }
    public string? Filter { get; init; }
    public EMultipleLoad? MultipleLoad { get; init; }
}

public record MieruBindingItem
{
    public string Port { get; init; } = string.Empty;
    public string Protocol { get; init; } = "TCP";
}
