namespace ServiceLib.Handler.Fmt;

public class VLESSFmt : BaseFmt
{
    private sealed record ParsedVlessUri(
        string Address,
        int Port,
        string UserInfo,
        string Query,
        string Fragment);

    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        if (!TryParseShareUri(str, out var parsed))
        {
            return null;
        }

        ProfileItem item = new()
        {
            ConfigType = EConfigType.VLESS,
            Address = parsed.Address,
            Port = parsed.Port,
            Remarks = SafeUrlDecode(parsed.Fragment),
            Password = SafeUrlDecode(parsed.UserInfo),
        };

        var query = Utils.ParseQueryString(parsed.Query);
        var encryption = GetQueryValue(query, "encryption", Global.None);
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            // Keep the complete VLESS Encryption string. New Xray formats are
            // intentionally opaque to the UI and must reach the core unchanged.
            VlessEncryption = encryption.IsNullOrEmpty() ? Global.None : encryption,
            Flow = GetQueryValue(query, "flow")
        });
        item.StreamSecurity = GetQueryValue(query, "security");
        ResolveUriQuery(query, ref item);
        MergeXhttpCompatibilityAliases(query, ref item);

        // XHTTP and VLESS Encryption are Xray features. Pin the imported node
        // to Xray instead of allowing a global sing-box preference to reject it.
        if (item.Network == nameof(ETransport.xhttp)
            || !string.Equals(
                item.GetProtocolExtra().VlessEncryption,
                Global.None,
                StringComparison.OrdinalIgnoreCase))
        {
            item.CoreType = ECoreType.Xray;
        }

        msg = string.Empty;
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
        dicQuery.Add("encryption",
            !item.GetProtocolExtra().VlessEncryption.IsNullOrEmpty() ? item.GetProtocolExtra().VlessEncryption : Global.None);
        if (!item.GetProtocolExtra().Flow.IsNullOrEmpty())
        {
            dicQuery.Add("flow", item.GetProtocolExtra().Flow);
        }
        ToUriQuery(item, Global.None, ref dicQuery);

        return ToUri(EConfigType.VLESS, item.Address, item.Port, item.Password, dicQuery, remark);
    }

    private static bool TryParseShareUri(string value, out ParsedVlessUri parsed)
    {
        parsed = null!;
        var source = value.TrimEx();
        var scheme = Global.ProtocolShares[EConfigType.VLESS];
        if (!source.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Parse the share URI without System.Uri. The built-in parser is strict
        // about some long/extended query strings, while Xray share links are an
        // opaque transport for JSON and new parameters.
        var remainder = source[scheme.Length..];
        var fragment = string.Empty;
        var fragmentIndex = remainder.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            fragment = remainder[(fragmentIndex + 1)..];
            remainder = remainder[..fragmentIndex];
        }

        var query = string.Empty;
        var queryIndex = remainder.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = remainder[queryIndex..];
            remainder = remainder[..queryIndex];
        }

        remainder = remainder.TrimEnd('/');
        var atIndex = remainder.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == remainder.Length - 1)
        {
            return false;
        }

        var userInfo = remainder[..atIndex];
        var hostPort = remainder[(atIndex + 1)..];
        string address;
        string portText;

        if (hostPort.StartsWith('['))
        {
            var closing = hostPort.IndexOf(']');
            if (closing <= 1 || closing + 2 > hostPort.Length || hostPort[closing + 1] != ':')
            {
                return false;
            }

            address = hostPort[1..closing];
            portText = hostPort[(closing + 2)..];
        }
        else
        {
            var separator = hostPort.LastIndexOf(':');
            if (separator <= 0 || separator == hostPort.Length - 1)
            {
                return false;
            }

            address = hostPort[..separator];
            portText = hostPort[(separator + 1)..];
        }

        if (address.IsNullOrEmpty()
            || userInfo.IsNullOrEmpty()
            || !int.TryParse(portText, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        parsed = new ParsedVlessUri(address, port, userInfo, query, fragment);
        return true;
    }

    private static string SafeUrlDecode(string value)
    {
        if (value.IsNullOrEmpty())
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            // A malformed optional display name must not invalidate the node.
            return value;
        }
    }

    private static void MergeXhttpCompatibilityAliases(
        System.Collections.Specialized.NameValueCollection query,
        ref ProfileItem item)
    {
        if (item.Network != nameof(ETransport.xhttp))
        {
            return;
        }

        var aliases = new (string QueryName, string JsonName, bool Boolean)[]
        {
            ("x_padding_bytes", "xPaddingBytes", false),
            ("x_padding_header", "xPaddingHeader", false),
            ("x_padding_key", "xPaddingKey", false),
            ("x_padding_method", "xPaddingMethod", false),
            ("x_padding_placement", "xPaddingPlacement", false),
            ("x_padding_obfs_mode", "xPaddingObfsMode", true),
        };

        var transport = item.GetTransportExtra();
        JsonObject? extra = null;
        if (transport.XhttpExtra.IsNotEmpty())
        {
            extra = JsonUtils.ParseJson(transport.XhttpExtra) as JsonObject;
        }

        foreach (var alias in aliases)
        {
            var value = GetQueryDecoded(query, alias.QueryName);
            if (value.IsNullOrEmpty())
            {
                continue;
            }

            extra ??= new JsonObject();
            if (extra.ContainsKey(alias.JsonName))
            {
                continue;
            }

            if (alias.Boolean && bool.TryParse(value, out var booleanValue))
            {
                extra[alias.JsonName] = booleanValue;
            }
            else
            {
                extra[alias.JsonName] = value;
            }
        }

        if (extra == null)
        {
            return;
        }

        item.SetTransportExtra(transport with
        {
            XhttpExtra = JsonUtils.Serialize(extra, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            })
        });
    }
}
