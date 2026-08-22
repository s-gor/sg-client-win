namespace ServiceLib.Handler.Fmt;

public class TuicFmt : BaseFmt
{
    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        ProfileItem item = new()
        {
            ConfigType = EConfigType.TUIC
        };

        var url = Utils.TryUri(str);
        if (url == null)
        {
            return null;
        }

        item.Address = url.IdnHost;
        item.Port = url.Port;
        item.Remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        var rawUserInfo = Utils.UrlDecode(url.UserInfo);
        var userInfoParts = rawUserInfo.Split(new[] { ':' }, 2);
        if (userInfoParts.Length == 2)
        {
            item.Username = userInfoParts.First();
            item.Password = userInfoParts.Last();
        }

        var query = Utils.ParseQueryString(url.Query);
        ResolveUriQuery(query, ref item);
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            CongestionControl = GetQueryValue(query, "congestion_control"),
            TuicUdpRelayMode = GetQueryValue(query, "udp_relay_mode", "native")
        });

        // SG099: TUIC v5 is handled by bundled Mihomo. TLS is intrinsic to the
        // protocol, so keep TLS/h3 defaults even when the URI omits security=.
        item.CoreType = ECoreType.mihomo;
        if (item.StreamSecurity.IsNullOrEmpty())
        {
            item.StreamSecurity = Global.StreamSecurity;
        }
        if (item.Alpn.IsNullOrEmpty())
        {
            item.Alpn = "h3";
        }

        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var remark = string.Empty;
        if (item.Remarks.IsNotEmpty())
        {
            remark = "#" + Utils.UrlEncode(item.Remarks);
        }

        var dicQuery = new Dictionary<string, string>();
        ToUriQueryLite(item, ref dicQuery);

        if (!item.GetProtocolExtra().CongestionControl.IsNullOrEmpty())
        {
            dicQuery.Add("congestion_control", item.GetProtocolExtra().CongestionControl);
        }
        if (!item.GetProtocolExtra().TuicUdpRelayMode.IsNullOrEmpty())
        {
            dicQuery.Add("udp_relay_mode", item.GetProtocolExtra().TuicUdpRelayMode);
        }

        return ToUri(EConfigType.TUIC, item.Address, item.Port, $"{item.Username ?? ""}:{item.Password}", dicQuery, remark);
    }
}
