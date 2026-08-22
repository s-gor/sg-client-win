namespace ServiceLib.Handler.Fmt;

public class Hysteria2Fmt : BaseFmt
{
    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;
        ProfileItem item = new()
        {
            ConfigType = EConfigType.Hysteria2
        };

        var url = Utils.TryUri(str);
        if (url == null)
        {
            return null;
        }

        item.Address = url.IdnHost;
        item.Port = url.Port;
        item.Remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        item.Password = Utils.UrlDecode(url.UserInfo);

        var query = Utils.ParseQueryString(url.Query);
        ResolveUriQuery(query, ref item);

        // Hysteria2 is QUIC over TLS. The standard hysteria2:// URI carries
        // TLS options (sni/insecure/pinSHA256) but has no security=tls query
        // parameter. Do not let the generic URI parser turn that omission into
        // StreamSecurity=none.
        item.StreamSecurity = Global.StreamSecurity;

        if (item.CertSha.IsNullOrEmpty())
        {
            item.CertSha = GetQueryDecoded(query, "pinSHA256");
        }
        var rawObfsType = GetQueryDecoded(query, "obfs");
        var obfsType = Hysteria2ObfsHelper.NormalizeType(rawObfsType);
        if (rawObfsType.IsNotEmpty() && obfsType.IsNullOrEmpty())
        {
            msg = $"Unsupported Hysteria2 obfs type: {rawObfsType}";
            return null;
        }

        var obfsPassword = GetQueryDecoded(query, "obfs-password");
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            Ports = GetQueryDecoded(query, "mport"),
            HyObfsType = obfsType,
            SalamanderPass = obfsPassword,
            GeckoMinPacketSize = obfsType == Hysteria2ObfsHelper.Gecko
                ? Hysteria2ObfsHelper.GeckoMinPacketSize.ToString()
                : null,
            GeckoMaxPacketSize = obfsType == Hysteria2ObfsHelper.Gecko
                ? Hysteria2ObfsHelper.GeckoMaxPacketSize.ToString()
                : null,
            // The share URI does not carry bandwidth. Keep it null; both Gecko
            // generators preserve that omission as BBR instead of inheriting the
            // legacy global Hysteria bandwidth values.
            UpMbps = null,
            DownMbps = null,
        });

        // SG-Gateway renders client-facing `obfs=gecko` with Xray FinalMask:
        // salamander + packetSize=512-1200. Pin Gecko to the bundled Xray so the
        // client and server use the same Gecko wire implementation.
        if (obfsType == Hysteria2ObfsHelper.Gecko)
        {
            item.CoreType = ECoreType.Xray;
        }

        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var url = string.Empty;

        var remark = string.Empty;
        if (item.Remarks.IsNotEmpty())
        {
            remark = "#" + Utils.UrlEncode(item.Remarks);
        }
        var dicQuery = new Dictionary<string, string>();
        ToUriQueryLite(item, ref dicQuery);
        var protocolExtraItem = item.GetProtocolExtra();

        var obfsType = Hysteria2ObfsHelper.GetEffectiveType(protocolExtraItem);
        if (obfsType.IsNotEmpty())
        {
            dicQuery.Add("obfs", obfsType!);
            dicQuery.Add("obfs-password", Utils.UrlEncode(protocolExtraItem.SalamanderPass!));
        }
        if (!protocolExtraItem.Ports.IsNullOrEmpty())
        {
            dicQuery.Add("mport", Utils.UrlEncode(protocolExtraItem.Ports.Replace(':', '-')));
        }
        if (!item.CertSha.IsNullOrEmpty())
        {
            var sha = item.CertSha;
            var idx = sha.IndexOf(',');
            if (idx > 0)
            {
                sha = sha[..idx];
            }
            dicQuery.Add("pinSHA256", Utils.UrlEncode(sha));
        }

        return ToUri(EConfigType.Hysteria2, item.Address, item.Port, item.Password, dicQuery, remark);
    }

    public static ProfileItem? ResolveFull2(string strData, string? subRemarks)
    {
        if (Contains(strData, "server", "auth", "up", "down", "listen"))
        {
            var fileName = WriteAllText(strData);

            var profileItem = new ProfileItem
            {
                CoreType = ECoreType.hysteria2,
                Address = fileName,
                Remarks = subRemarks ?? "hysteria2_custom"
            };
            return profileItem;
        }

        return null;
    }
}
