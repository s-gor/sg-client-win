namespace ServiceLib.Models.Dto;

[Serializable]
public class ServerSpeedItem : ServerStatItem
{
    public long ProxyUp { get; set; }

    public long ProxyDown { get; set; }

    public long DirectUp { get; set; }

    public long DirectDown { get; set; }

    // True when ProxyUp/ProxyDown come from the Windows virtual TUN adapter
    // rather than from a core-specific Stats API.
    public bool IsTunInterfaceTraffic { get; set; }

    public string? TrafficInterfaceName { get; set; }
}

[Serializable]
public class TrafficItem
{
    public ulong Up { get; set; }

    public ulong Down { get; set; }
}
