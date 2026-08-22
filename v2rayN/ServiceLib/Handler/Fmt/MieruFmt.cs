namespace ServiceLib.Handler.Fmt;

public class MieruFmt : BaseFmt
{
    private const string DefaultMultiplexing = "MULTIPLEXING_LOW";

    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;
        var uri = Utils.TryUri(str);
        if (uri == null || !string.Equals(uri.Scheme, "mierus", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rawUserInfo = uri.UserInfo ?? string.Empty;
        var separator = rawUserInfo.IndexOf(':');
        if (separator <= 0 || separator == rawUserInfo.Length - 1)
        {
            msg = "В ссылке Mieru отсутствуют имя пользователя или пароль.";
            return null;
        }

        var username = SafeDecode(rawUserInfo[..separator]);
        var password = SafeDecode(rawUserInfo[(separator + 1)..]);
        var query = ParseRepeatedQuery(uri.Query);
        var ports = Values(query, "port");
        var protocols = Values(query, "protocol");
        if (ports.Count == 0 || protocols.Count == 0 || ports.Count != protocols.Count)
        {
            msg = "В ссылке Mieru должны быть указаны соответствующие пары port и protocol.";
            return null;
        }

        var bindings = new List<MieruBindingItem>();
        for (var i = 0; i < ports.Count; i++)
        {
            var port = ports[i].Trim();
            var protocol = NormalizeProtocol(protocols[i]);
            if (!IsValidPortOrRange(port) || protocol == null)
            {
                msg = "В ссылке Mieru указан недопустимый порт, диапазон портов или протокол.";
                return null;
            }
            bindings.Add(new MieruBindingItem { Port = port, Protocol = protocol });
        }

        var firstPort = GetFirstPort(bindings[0].Port);
        var profileName = First(query, "profile");
        var fragmentName = SafeDecode(uri.Fragment.TrimStart('#'));
        var item = new ProfileItem
        {
            ConfigType = EConfigType.Mieru,
            CoreType = ECoreType.mihomo,
            Address = uri.IdnHost,
            Port = firstPort,
            Username = username,
            Password = password,
            Network = bindings[0].Protocol.ToLowerInvariant(),
            Remarks = fragmentName.IsNotEmpty()
                ? fragmentName
                : profileName.IsNotEmpty() ? profileName : $"Mieru · {uri.IdnHost}",
        };

        var mtu = int.TryParse(First(query, "mtu"), out var parsedMtu) ? parsedMtu : 0;
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            MieruBindings = bindings,
            MieruProfile = profileName.NullIfEmpty(),
            MieruMtu = mtu is >= 1280 and <= 1400 ? mtu : null,
            MieruMultiplexing = NormalizeMultiplexing(First(query, "multiplexing")),
            MieruHandshakeMode = First(query, "handshake-mode").NullIfEmpty(),
            MieruTrafficPattern = First(query, "traffic-pattern").NullIfEmpty(),
        });

        msg = string.Empty;
        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null || item.ConfigType != EConfigType.Mieru)
        {
            return null;
        }

        var extra = item.GetProtocolExtra();
        var bindings = extra.MieruBindings is { Count: > 0 }
            ? extra.MieruBindings
            : [new MieruBindingItem { Port = item.Port.ToString(), Protocol = NormalizeProtocol(item.Network) ?? "TCP" }];

        var profileName = extra.MieruProfile.IsNotEmpty()
            ? extra.MieruProfile
            : item.Remarks.IsNotEmpty() ? item.Remarks : "default";
        var query = new List<string>
        {
            $"profile={Utils.UrlEncode(profileName)}",
        };
        if (extra.MieruMtu is > 0)
        {
            query.Add($"mtu={extra.MieruMtu}");
        }
        if (extra.MieruMultiplexing.IsNotEmpty())
        {
            query.Add($"multiplexing={Utils.UrlEncode(extra.MieruMultiplexing)}");
        }
        if (extra.MieruHandshakeMode.IsNotEmpty())
        {
            query.Add($"handshake-mode={Utils.UrlEncode(extra.MieruHandshakeMode)}");
        }
        if (extra.MieruTrafficPattern.IsNotEmpty())
        {
            query.Add($"traffic-pattern={Utils.UrlEncode(extra.MieruTrafficPattern)}");
        }
        foreach (var binding in bindings)
        {
            query.Add($"port={Utils.UrlEncode(binding.Port)}");
            query.Add($"protocol={Utils.UrlEncode(NormalizeProtocol(binding.Protocol) ?? "TCP")}");
        }

        var userInfo = $"{Utils.UrlEncode(item.Username)}:{Utils.UrlEncode(item.Password)}";
        var fragment = item.Remarks.IsNotEmpty()
                       && !string.Equals(item.Remarks, profileName, StringComparison.Ordinal)
            ? $"#{Utils.UrlEncode(item.Remarks)}"
            : string.Empty;
        return $"mierus://{userInfo}@{GetIpv6(item.Address)}?{string.Join("&", query)}{fragment}";
    }

    private static Dictionary<string, List<string>> ParseRepeatedQuery(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var raw = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            var key = SafeDecode(part[..separator]);
            var value = SafeDecode(part[(separator + 1)..]);
            if (!result.TryGetValue(key, out var values))
            {
                values = [];
                result[key] = values;
            }
            values.Add(value);
        }
        return result;
    }

    private static string SafeDecode(string value)
    {
        try
        {
            return Utils.UrlDecode(value);
        }
        catch
        {
            return value;
        }
    }

    private static List<string> Values(Dictionary<string, List<string>> query, string key)
        => query.TryGetValue(key, out var values) ? values : [];

    private static string First(Dictionary<string, List<string>> query, string key)
        => Values(query, key).FirstOrDefault() ?? string.Empty;

    private static string? NormalizeProtocol(string? value)
    {
        if (string.Equals(value, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            return "TCP";
        }
        if (string.Equals(value, "udp", StringComparison.OrdinalIgnoreCase))
        {
            return "UDP";
        }
        return null;
    }

    private static string NormalizeMultiplexing(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "MULTIPLEXING_OFF" or "MULTIPLEXING_LOW" or "MULTIPLEXING_MIDDLE" or "MULTIPLEXING_HIGH"
            ? normalized
            : DefaultMultiplexing;
    }

    private static bool IsValidPortOrRange(string value)
    {
        if (int.TryParse(value, out var port))
        {
            return port is > 0 and <= 65535;
        }
        var parts = value.Split('-', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out var start)
            && int.TryParse(parts[1], out var end)
            && start is > 0 and <= 65535
            && end is > 0 and <= 65535
            && start <= end;
    }

    private static int GetFirstPort(string value)
    {
        var first = value.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return int.TryParse(first, out var port) ? port : 0;
    }
}
