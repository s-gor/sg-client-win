using System.Buffers.Binary;

namespace ServiceLib.Services;

/// <summary>
/// Offline country resolver for SG Client profiles and Connections.
/// It reads SG Client's dedicated country database, which is intentionally
/// independent from the routing geoip.dat selected in GeoFiles.
/// Host names are resolved through the system DNS and cached for the current run.
/// </summary>
public sealed class SgGeoIpCountryService
{
    private static readonly Lazy<SgGeoIpCountryService> LazyInstance = new(() => new());
    public static SgGeoIpCountryService Instance => LazyInstance.Value;

    private readonly ConcurrentDictionary<string, string> _hostCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Task<GeoIpIndex?>> _indexTask;

    private SgGeoIpCountryService()
    {
        _indexTask = new Lazy<Task<GeoIpIndex?>>(
            () => Task.Run(LoadIndex),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string DatabasePath => FindDatabasePath() ?? string.Empty;

    public async Task<string> ResolveAddressAsync(string? rawAddress, CancellationToken cancellationToken = default)
    {
        var host = NormalizeHost(rawAddress);
        if (host.IsNullOrEmpty())
        {
            return string.Empty;
        }

        if (_hostCache.TryGetValue(host, out var cached))
        {
            return cached;
        }

        IPAddress? address = null;
        if (IPAddress.TryParse(host, out var parsed))
        {
            address = parsed;
        }
        else
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var resolved = await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
                address = resolved.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && IsPublicAddress(ip))
                    ?? resolved.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6 && IsPublicAddress(ip));
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"GeoIP DNS lookup failed for {host}", ex);
                return string.Empty;
            }
        }

        if (address is null || !IsPublicAddress(address))
        {
            _hostCache.TryAdd(host, string.Empty);
            return string.Empty;
        }

        GeoIpIndex? index;
        try
        {
            index = await _indexTask.Value.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Load dedicated SG Client country GeoIP database", ex);
            return string.Empty;
        }

        var code = index?.Lookup(address) ?? string.Empty;
        code = SgCountryHelper.NormalizeCode(code);
        _hostCache[host] = code;
        _hostCache[address.ToString()] = code;
        return code;
    }

    private static string NormalizeHost(string? value)
    {
        var host = value?.Trim() ?? string.Empty;
        if (host.StartsWith('[') && host.Contains(']'))
        {
            host = host[1..host.IndexOf(']')];
        }
        return host.TrimEnd('.');
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || bytes[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || (bytes[0] & 0xFE) == 0xFC);
        }

        return false;
    }

    private static GeoIpIndex? LoadIndex()
    {
        var path = FindDatabasePath();
        if (path.IsNullOrEmpty() || !File.Exists(path))
        {
            Logging.SaveLog("Dedicated SG Client country GeoIP database was not found; country flags are unavailable.");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var index = GeoIpIndex.Load(path);
        Logging.SaveLog($"GeoIP country index loaded: {Path.GetFileName(path)}; IPv4={index.Ipv4Count}; IPv6={index.Ipv6Count}; {stopwatch.ElapsedMilliseconds} ms");
        return index;
    }

    private static string? FindDatabasePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "bin", "sg-country-geoip.dat"),
            Path.Combine(AppContext.BaseDirectory, "sg-country-geoip.dat"),
            Path.Combine(Environment.CurrentDirectory, "bin", "sg-country-geoip.dat"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class GeoIpIndex
    {
        private readonly V4Entry[][] _ipv4;
        private readonly V6Entry[][] _ipv6;

        private GeoIpIndex(V4Entry[][] ipv4, V6Entry[][] ipv6)
        {
            _ipv4 = ipv4;
            _ipv6 = ipv6;
            Ipv4Count = ipv4.Sum(items => items.Length);
            Ipv6Count = ipv6.Sum(items => items.Length);
        }

        public int Ipv4Count { get; }
        public int Ipv6Count { get; }

        public static GeoIpIndex Load(string path)
        {
            var data = File.ReadAllBytes(path);
            var ipv4 = Enumerable.Range(0, 33).Select(_ => new List<V4Entry>()).ToArray();
            var ipv6 = Enumerable.Range(0, 129).Select(_ => new List<V6Entry>()).ToArray();

            var offset = 0;
            while (offset < data.Length && TryReadVarint(data, ref offset, data.Length, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, data.Length, out var messageStart, out var messageLength))
                {
                    ParseGeoIpMessage(data, messageStart, messageLength, ipv4, ipv6);
                    offset = messageStart + messageLength;
                }
                else if (!SkipField(data, ref offset, data.Length, wire))
                {
                    break;
                }
            }

            var ipv4Arrays = new V4Entry[33][];
            for (var prefix = 0; prefix <= 32; prefix++)
            {
                var list = ipv4[prefix];
                list.Sort(static (left, right) => left.Network.CompareTo(right.Network));
                ipv4Arrays[prefix] = list.ToArray();
            }

            var ipv6Arrays = new V6Entry[129][];
            for (var prefix = 0; prefix <= 128; prefix++)
            {
                var list = ipv6[prefix];
                list.Sort(static (left, right) => left.Network.CompareTo(right.Network));
                ipv6Arrays[prefix] = list.ToArray();
            }

            return new GeoIpIndex(ipv4Arrays, ipv6Arrays);
        }

        public string Lookup(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
            {
                var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                for (var prefix = 32; prefix >= 0; prefix--)
                {
                    var mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
                    var code = FindV4(_ipv4[prefix], value & mask);
                    if (code != 0)
                    {
                        return UnpackCode(code);
                    }
                }
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
            {
                var high = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8));
                var low = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8));
                var value = ((UInt128)high << 64) | low;
                for (var prefix = 128; prefix >= 0; prefix--)
                {
                    var mask = prefix == 0 ? UInt128.Zero : UInt128.MaxValue << (128 - prefix);
                    var code = FindV6(_ipv6[prefix], value & mask);
                    if (code != 0)
                    {
                        return UnpackCode(code);
                    }
                }
            }

            return string.Empty;
        }

        private static void ParseGeoIpMessage(
            byte[] data,
            int start,
            int length,
            List<V4Entry>[] ipv4,
            List<V6Entry>[] ipv6)
        {
            var end = start + length;
            var offset = start;
            string countryCode = string.Empty;

            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, end, out var valueStart, out var valueLength))
                {
                    countryCode = Encoding.UTF8.GetString(data, valueStart, valueLength);
                    break;
                }
                if (!SkipField(data, ref offset, end, wire))
                {
                    return;
                }
            }

            countryCode = SgCountryHelper.NormalizeCode(countryCode);
            if (countryCode.Length != 2)
            {
                return;
            }
            var packedCode = PackCode(countryCode);

            offset = start;
            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 2 && wire == 2 && TryReadLength(data, ref offset, end, out var cidrStart, out var cidrLength))
                {
                    ParseCidr(data, cidrStart, cidrLength, packedCode, ipv4, ipv6);
                    offset = cidrStart + cidrLength;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    return;
                }
            }
        }

        private static void ParseCidr(
            byte[] data,
            int start,
            int length,
            ushort packedCode,
            List<V4Entry>[] ipv4,
            List<V6Entry>[] ipv6)
        {
            var end = start + length;
            var offset = start;
            var ipStart = -1;
            var ipLength = 0;
            var prefix = -1;

            while (offset < end && TryReadVarint(data, ref offset, end, out var key))
            {
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (field == 1 && wire == 2 && TryReadLength(data, ref offset, end, out var valueStart, out var valueLength))
                {
                    ipStart = valueStart;
                    ipLength = valueLength;
                    offset = valueStart + valueLength;
                }
                else if (field == 2 && wire == 0 && TryReadVarint(data, ref offset, end, out var prefixValue))
                {
                    prefix = (int)prefixValue;
                }
                else if (!SkipField(data, ref offset, end, wire))
                {
                    return;
                }
            }

            if (ipStart < 0 || prefix < 0)
            {
                return;
            }

            if (ipLength == 4 && prefix <= 32)
            {
                var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(ipStart, 4));
                var mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
                ipv4[prefix].Add(new V4Entry(value & mask, packedCode));
            }
            else if (ipLength == 16 && prefix <= 128)
            {
                var high = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(ipStart, 8));
                var low = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(ipStart + 8, 8));
                var value = ((UInt128)high << 64) | low;
                var mask = prefix == 0 ? UInt128.Zero : UInt128.MaxValue << (128 - prefix);
                ipv6[prefix].Add(new V6Entry(value & mask, packedCode));
            }
        }

        private static bool TryReadLength(byte[] data, ref int offset, int limit, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (!TryReadVarint(data, ref offset, limit, out var rawLength) || rawLength > int.MaxValue)
            {
                return false;
            }
            length = (int)rawLength;
            start = offset;
            return length >= 0 && start >= 0 && start + length <= limit;
        }

        private static bool TryReadVarint(byte[] data, ref int offset, int limit, out ulong value)
        {
            value = 0;
            var shift = 0;
            while (offset < limit && shift <= 63)
            {
                var current = data[offset++];
                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return true;
                }
                shift += 7;
            }
            return false;
        }

        private static bool SkipField(byte[] data, ref int offset, int limit, int wire)
        {
            switch (wire)
            {
                case 0:
                    return TryReadVarint(data, ref offset, limit, out _);
                case 1:
                    offset += 8;
                    return offset <= limit;
                case 2:
                    if (!TryReadVarint(data, ref offset, limit, out var rawLength) || rawLength > int.MaxValue)
                    {
                        return false;
                    }
                    offset += (int)rawLength;
                    return offset <= limit;
                case 5:
                    offset += 4;
                    return offset <= limit;
                default:
                    return false;
            }
        }

        private static ushort FindV4(V4Entry[] entries, uint network)
        {
            var low = 0;
            var high = entries.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var current = entries[middle];
                var comparison = current.Network.CompareTo(network);
                if (comparison == 0)
                {
                    return current.Code;
                }
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return 0;
        }

        private static ushort FindV6(V6Entry[] entries, UInt128 network)
        {
            var low = 0;
            var high = entries.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var current = entries[middle];
                var comparison = current.Network.CompareTo(network);
                if (comparison == 0)
                {
                    return current.Code;
                }
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return 0;
        }

        private static ushort PackCode(string code) => (ushort)((code[0] << 8) | code[1]);
        private static string UnpackCode(ushort code) => new string(new[] { (char)(code >> 8), (char)(code & 0xFF) });

        private readonly record struct V4Entry(uint Network, ushort Code);
        private readonly record struct V6Entry(UInt128 Network, ushort Code);
    }
}
