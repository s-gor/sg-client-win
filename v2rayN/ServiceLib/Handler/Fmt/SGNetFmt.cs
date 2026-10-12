namespace ServiceLib.Handler.Fmt;

// SG-Net/1 link: sgnet://host:port?device_id=<id>&secret=<url-encoded-base64>&sni=<hostname>&v=1#name
// The device secret is sensitive. Do not log or render full links.
public static class SGNetFmt
{
    public static ProfileItem? Resolve(string source, out string msg)
    {
        msg = "Invalid SG-Net configuration";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "sgnet", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is <= 0 or > 65535)
            return null;

        var fields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            if (parts.Length != 2) return null;
            var key = Uri.UnescapeDataString(parts[0]);
            if (!fields.TryAdd(key, Uri.UnescapeDataString(parts[1]))) return null;
        }
        if (!fields.TryGetValue("device_id", out var device) || string.IsNullOrWhiteSpace(device)
            || !fields.TryGetValue("secret", out var secret)
            || !fields.TryGetValue("sni", out var sni) || string.IsNullOrWhiteSpace(sni)
            || (fields.TryGetValue("v", out var version) && version != "1"))
            return null;
        byte[] decoded;
        try { decoded = Convert.FromBase64String(secret); }
        catch (FormatException) { return null; }
        if (decoded.Length < 32 || decoded.Length > 1024) return null;
        var profile = new ProfileItem
        {
            ConfigType = EConfigType.SGNet,
            CoreType = ECoreType.sgnet,
            Address = uri.Host,
            Port = uri.Port,
            Username = device,
            Password = secret,
            Sni = sni,
            Remarks = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
        };
        msg = string.Empty;
        return profile;
    }

    public static string? ToUri(ProfileItem? p)
    {
        if (p is null || p.ConfigType != EConfigType.SGNet || !p.IsValid()) return null;
        var host = p.Address.Contains(':') && !p.Address.StartsWith('[') ? "[" + p.Address + "]" : p.Address;
        return $"sgnet://{host}:{p.Port}?device_id={Uri.EscapeDataString(p.Username)}&secret={Uri.EscapeDataString(p.Password)}&sni={Uri.EscapeDataString(p.Sni)}&v=1#{Uri.EscapeDataString(p.Remarks)}";
    }
}
