using Microsoft.Win32;

namespace ServiceLib.Manager;

public sealed class AwgProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TunnelName { get; set; } = string.Empty;
    public string ConfigFileName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DNS { get; set; } = string.Empty;
    public string Protocol { get; set; } = "AmneziaWG 2.0";
    public string ContentHash { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string ConfigPath => Path.Combine(AmneziaWgManager.Instance.ConfigDirectory, ConfigFileName);

    [JsonIgnore]
    public int EndpointPort
    {
        get
        {
            var endpoint = Endpoint.Trim();
            var separator = endpoint.LastIndexOf(':');
            return separator >= 0 && int.TryParse(endpoint[(separator + 1)..], out var port) ? port : 0;
        }
    }

    [JsonIgnore]
    public string EndpointHost
    {
        get
        {
            var endpoint = Endpoint.Trim();
            if (endpoint.StartsWith('[') && endpoint.Contains(']'))
            {
                return endpoint[1..endpoint.IndexOf(']')];
            }
            var separator = endpoint.LastIndexOf(':');
            return separator > 0 ? endpoint[..separator] : endpoint;
        }
    }
}

public sealed class AwgOperationResult
{
    public bool Success { get; init; }
    public string State { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool ServicePresent { get; init; }
    public DateTime? LastHandshake { get; init; }
}

public sealed class AwgConfigPreview
{
    public string Address { get; init; } = string.Empty;
    public string DNS { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string AllowedIPs { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string SuggestedName { get; init; } = string.Empty;
    public string DuplicateProfileName { get; init; } = string.Empty;
}

internal sealed class AwgProfileStore
{
    public string? SelectedProfileId { get; set; }
    public List<AwgProfile> Profiles { get; set; } = [];
}

internal sealed class AwgParsedConfig
{
    public string Address { get; set; } = string.Empty;
    public string DNS { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string AllowedIps { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string SuggestedName { get; set; } = string.Empty;
    public bool HasAmneziaParameters { get; set; }
}

internal sealed record AwgProcessResult(int ExitCode, string Output, string Error);

public sealed class AmneziaWgManager
{
    private static readonly Lazy<AmneziaWgManager> LazyInstance = new(() => new AmneziaWgManager());
    public static AmneziaWgManager Instance => LazyInstance.Value;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private AwgProfileStore _store = new();

    public string StorageDirectory { get; }
    public string ConfigDirectory { get; }
    public string StorePath { get; }
    public string EngineDirectory { get; }
    public string RuntimeDirectory { get; }
    public string? ActiveProfileId { get; private set; }

    public string? SelectedProfileId
    {
        get
        {
            lock (_store)
            {
                return _store.SelectedProfileId;
            }
        }
    }

    private AmneziaWgManager()
    {
        StorageDirectory = Path.Combine(AppContext.BaseDirectory, "guiConfigs", "sg-awg");
        ConfigDirectory = Path.Combine(StorageDirectory, "profiles");
        StorePath = Path.Combine(StorageDirectory, "profiles.json");
        EngineDirectory = Path.Combine(AppContext.BaseDirectory, "bin", "awg");
        RuntimeDirectory = Path.Combine(StorageDirectory, "runtime");
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
        LoadStore();
    }

    public static bool IsAwgProfileId(string? id) => id?.StartsWith("awg:", StringComparison.OrdinalIgnoreCase) == true;

    public IReadOnlyList<AwgProfile> GetProfiles()
    {
        lock (_store)
        {
            return _store.Profiles
                .OrderBy(profile => profile.CreatedAt)
                .Select(CloneProfile)
                .ToList();
        }
    }

    public AwgProfile? GetProfile(string? id)
    {
        if (id.IsNullOrEmpty())
        {
            return null;
        }
        lock (_store)
        {
            var profile = _store.Profiles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            return profile == null ? null : CloneProfile(profile);
        }
    }

    public AwgProfile? GetSelectedProfile() => GetProfile(SelectedProfileId);

    public async Task SetCountryCodeAsync(string id, string countryCode)
    {
        var normalized = SgCountryHelper.NormalizeCode(countryCode);
        if (id.IsNullOrEmpty() || normalized.IsNullOrEmpty())
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var profile = _store.Profiles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile == null || string.Equals(profile.CountryCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            profile.CountryCode = normalized;
            SaveStore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasCompleteEngine()
    {
        return File.Exists(Path.Combine(EngineDirectory, "amneziawg.exe"))
            && File.Exists(Path.Combine(EngineDirectory, "awg.exe"))
            && File.Exists(Path.Combine(EngineDirectory, "wintun.dll"));
    }

    public static bool LooksLikeWireGuardConfig(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return false;
        }

        var normalized = NormalizeConfig(content!);
        return normalized.Contains("[Interface]", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("[Peer]", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasAmneziaParameterMarkers(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return false;
        }

        var normalized = NormalizeConfig(content!);
        return Regex.IsMatch(
            normalized,
            @"(?im)^\s*(?:Jc|Jmin|Jmax|S[1-4]|H[1-4]|I[1-5])\s*=",
            RegexOptions.CultureInvariant);
    }

    public static bool TryValidateAmneziaConfig(
        string? content,
        out bool hasAmneziaParameters,
        out string error)
    {
        hasAmneziaParameters = false;
        error = string.Empty;
        if (!LooksLikeWireGuardConfig(content))
        {
            error = "Не найдены разделы [Interface] и [Peer].";
            return false;
        }

        try
        {
            hasAmneziaParameters = ParseConfig(content!).HasAmneziaParameters;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message.IsNullOrEmpty()
                ? "Не удалось разобрать конфигурацию AmneziaWG."
                : ex.Message;
            return false;
        }
    }

    public static bool IsAmneziaConfig(string? content)
    {
        return TryValidateAmneziaConfig(content, out var hasAmneziaParameters, out _)
            && hasAmneziaParameters;
    }

    public static string GetSuggestedProfileName(string sourceFileName, string content)
    {
        try
        {
            var parsed = ParseConfig(content);
            return NormalizeProfileName(parsed.SuggestedName, sourceFileName);
        }
        catch
        {
            return NormalizeProfileName(null, sourceFileName);
        }
    }

    public AwgConfigPreview InspectConfig(string content)
    {
        var parsed = ParseConfig(content);
        if (!parsed.HasAmneziaParameters)
        {
            throw new InvalidDataException("Конфигурация похожа на WireGuard, но не содержит параметров AmneziaWG из SG-AWG-Panel.");
        }

        var normalized = NormalizeConfig(content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        string duplicateName;
        lock (_store)
        {
            duplicateName = _store.Profiles
                .FirstOrDefault(item => string.Equals(item.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                ?.Name ?? string.Empty;
        }

        return new AwgConfigPreview
        {
            Address = parsed.Address,
            DNS = parsed.DNS,
            Endpoint = parsed.Endpoint,
            AllowedIPs = parsed.AllowedIps,
            Protocol = parsed.Protocol,
            SuggestedName = parsed.SuggestedName,
            DuplicateProfileName = duplicateName
        };
    }

    public async Task<AwgProfile> ImportProfileAsync(string sourceFileName, string content, string? requestedName = null)
    {
        var parsed = ParseConfig(content);
        if (!parsed.HasAmneziaParameters)
        {
            throw new InvalidDataException("Файл похож на WireGuard, но не содержит параметров AmneziaWG из SG-AWG-Panel.");
        }

        var normalized = NormalizeConfig(content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

        await _gate.WaitAsync();
        try
        {
            var duplicate = _store.Profiles.FirstOrDefault(item => string.Equals(item.ContentHash, hash, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                _store.SelectedProfileId = duplicate.Id;
                SaveStore();
                return CloneProfile(duplicate);
            }

            var guid = Guid.NewGuid().ToString("N");
            var tunnelName = "sgawg-" + guid[..12];
            var profile = new AwgProfile
            {
                Id = "awg:" + guid,
                Name = NormalizeProfileName(
                    requestedName.IsNotEmpty() ? requestedName : parsed.SuggestedName,
                    sourceFileName),
                TunnelName = tunnelName,
                ConfigFileName = tunnelName + ".conf",
                Endpoint = parsed.Endpoint,
                Address = parsed.Address,
                DNS = parsed.DNS,
                Protocol = parsed.Protocol,
                ContentHash = hash,
                CreatedAt = DateTime.Now
            };

            Directory.CreateDirectory(ConfigDirectory);
            var finalPath = Path.Combine(ConfigDirectory, profile.ConfigFileName);
            var temporaryPath = finalPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, normalized, new UTF8Encoding(false));
            File.Move(temporaryPath, finalPath, true);

            _store.Profiles.Add(profile);
            _store.SelectedProfileId = profile.Id;
            SaveStore();
            Logging.SaveLog($"AmneziaWG profile imported: {profile.Name} ({profile.Endpoint})");
            return CloneProfile(profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AwgProfile> UpdateProfileAsync(
        string id,
        string requestedName,
        string content)
    {
        var parsed = ParseConfig(content);
        if (!parsed.HasAmneziaParameters)
        {
            throw new InvalidDataException(
                "Конфигурация не содержит обязательных параметров AmneziaWG.");
        }

        var normalized = NormalizeConfig(content);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

        await _gate.WaitAsync();
        try
        {
            var profile = _store.Profiles.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                throw new InvalidOperationException(
                    "Профиль AmneziaWG не найден.");
            }
            if (string.Equals(
                ActiveProfileId,
                id,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Сначала отключите активный туннель AmneziaWG.");
            }

            Directory.CreateDirectory(ConfigDirectory);
            var backupDirectory = Path.Combine(
                StorageDirectory,
                "backups");
            Directory.CreateDirectory(backupDirectory);

            var currentPath = profile.ConfigPath;
            if (File.Exists(currentPath))
            {
                var backupName =
                    $"{DateTime.Now:yyyyMMdd-HHmmss}-{profile.TunnelName}.conf";
                File.Copy(
                    currentPath,
                    Path.Combine(backupDirectory, backupName),
                    overwrite: true);
            }

            var temporaryPath = currentPath + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                normalized,
                new UTF8Encoding(false));
            File.Move(temporaryPath, currentPath, true);

            profile.Name = NormalizeProfileName(
                requestedName,
                profile.ConfigFileName);
            profile.Endpoint = parsed.Endpoint;
            profile.Address = parsed.Address;
            profile.DNS = parsed.DNS;
            profile.Protocol = parsed.Protocol;
            profile.ContentHash = hash;
            SaveStore();

            Logging.SaveLog(
                $"AmneziaWG profile updated: {profile.Name} ({profile.Endpoint})");
            return CloneProfile(profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SelectProfileAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_store.Profiles.All(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Профиль AmneziaWG не найден.");
            }
            _store.SelectedProfileId = id;
            SaveStore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearSelectionAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _store.SelectedProfileId = null;
            SaveStore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteProfileAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            var profile = _store.Profiles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                return;
            }
            if (string.Equals(ActiveProfileId, id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Сначала отключите TUN AmneziaWG.");
            }

            _store.Profiles.Remove(profile);
            if (string.Equals(_store.SelectedProfileId, id, StringComparison.OrdinalIgnoreCase))
            {
                _store.SelectedProfileId = null;
            }
            SaveStore();
            TryDeleteFile(profile.ConfigPath);
            TryDeleteFile(Path.Combine(RuntimeDirectory, profile.ConfigFileName));
            Logging.SaveLog($"AmneziaWG profile deleted: {profile.Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AwgOperationResult> ConnectSelectedAsync()
    {
        var profile = GetSelectedProfile() ?? throw new InvalidOperationException("Профиль AmneziaWG не выбран.");
        return await ConnectAsync(profile);
    }

    public async Task<AwgOperationResult> ConnectAsync(AwgProfile profile)
    {
        EnsureEngine();
        if (!File.Exists(profile.ConfigPath))
        {
            throw new FileNotFoundException("Не найден файл профиля AmneziaWG.", profile.ConfigPath);
        }

        await _gate.WaitAsync();
        try
        {
            Logging.SaveLog($"AmneziaWG connect begin: profile={profile.Name}; tunnel={profile.TunnelName}; endpoint={profile.Endpoint}; admin={Utils.IsAdministrator()}");
            await DisconnectAllCoreAsync();
            var enginePath = Path.Combine(EngineDirectory, "amneziawg.exe");
            var runtimeConfigPath = await BuildRuntimeConfigAsync(profile);
            Logging.SaveLog($"AmneziaWG install service command: engine={enginePath}; config={runtimeConfigPath}; localNetwork={AppManager.Instance.Config.SgQuickSettingsItem.AllowLocalNetwork}; dnsThroughTun={AppManager.Instance.Config.SgQuickSettingsItem.DnsThroughTun}");
            var install = await RunProcessAsync(enginePath, ["/installtunnelservice", runtimeConfigPath], TimeSpan.FromSeconds(20));
            Logging.SaveLog($"AmneziaWG install service result: exit={install.ExitCode}; output={install.Output.Trim()}; error={install.Error.Trim()}");
            if (install.ExitCode != 0)
            {
                throw new InvalidOperationException(GetProcessError(install, "не удалось установить туннельную службу AmneziaWG"));
            }

            var status = await WaitForHandshakeAsync(profile, TimeSpan.FromSeconds(45));
            ActiveProfileId = profile.Id;
            Logging.SaveLog($"AmneziaWG handshake confirmed: profile={profile.Name}; time={status.LastHandshake:O}");
            Logging.SaveLog($"AmneziaWG connected: {profile.Name}; handshake={status.LastHandshake:O}");
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly string[] PublicIpv4AllowedIps =
    [
        "0.0.0.0/5", "8.0.0.0/7", "11.0.0.0/8", "12.0.0.0/6",
        "16.0.0.0/4", "32.0.0.0/3", "64.0.0.0/2", "128.0.0.0/3",
        "160.0.0.0/5", "168.0.0.0/8", "169.0.0.0/9", "169.128.0.0/10",
        "169.192.0.0/11", "169.224.0.0/12", "169.240.0.0/13", "169.248.0.0/14",
        "169.252.0.0/15", "169.255.0.0/16", "170.0.0.0/7", "172.0.0.0/12",
        "172.32.0.0/11", "172.64.0.0/10", "172.128.0.0/9", "173.0.0.0/8",
        "174.0.0.0/7", "176.0.0.0/4", "192.0.0.0/9", "192.128.0.0/11",
        "192.160.0.0/13", "192.169.0.0/16", "192.170.0.0/15", "192.172.0.0/14",
        "192.176.0.0/12", "192.192.0.0/10", "193.0.0.0/8", "194.0.0.0/7",
        "196.0.0.0/6", "200.0.0.0/5", "208.0.0.0/4", "224.0.0.0/3"
    ];

    private static readonly string[] PublicIpv6AllowedIps =
    [
        "::/1", "8000::/2", "c000::/3", "e000::/4", "f000::/5",
        "f800::/6", "fe00::/9", "fec0::/10", "ff00::/8"
    ];

    private async Task<string> BuildRuntimeConfigAsync(AwgProfile profile)
    {
        var settings = AppManager.Instance.Config.SgQuickSettingsItem ?? new SgQuickSettingsItem();
        var source = await File.ReadAllTextAsync(profile.ConfigPath);
        var result = new List<string>();
        var dnsHostRoutes = settings.DnsThroughTun ? BuildDnsHostRoutes(profile.DNS) : [];
        var smartRouting = SgSmartRoutingHelper.Normalize(settings);
        var routeExclusions = ConfigHandler.GetSgRouteExclusions(AppManager.Instance.Config);
        if (smartRouting.DefaultAction == SgSmartRoutingHelper.ActionProxy)
        {
            routeExclusions.AddRange(smartRouting.CustomDirectIps);
            routeExclusions = routeExclusions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        foreach (var rawLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = rawLine.TrimStart();
            if (!settings.DnsThroughTun
                && trimmed.StartsWith("DNS", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains('='))
            {
                continue;
            }

            if (trimmed.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains('=')
                && (routeExclusions.Count > 0
                    || smartRouting.DefaultAction == SgSmartRoutingHelper.ActionDirect))
            {
                var separator = rawLine.IndexOf('=');
                var values = rawLine[(separator + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var rewritten = smartRouting.DefaultAction == SgSmartRoutingHelper.ActionDirect
                    ? BuildIncludedAllowedIps(values, smartRouting.CustomProxyIps, dnsHostRoutes)
                    : RewriteAllowedIps(values, routeExclusions, dnsHostRoutes);
                result.Add($"AllowedIPs = {string.Join(", ", rewritten)}");
                continue;
            }

            result.Add(rawLine);
        }

        Directory.CreateDirectory(RuntimeDirectory);
        var finalPath = Path.Combine(RuntimeDirectory, profile.ConfigFileName);
        var temporaryPath = finalPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporaryPath, finalPath, true);
        return finalPath;
    }

    private static List<string> RewriteAllowedIps(
        List<string> values,
        List<string> exclusions,
        List<string> dnsHostRoutes)
    {
        var result = values
            .Where(item => !string.Equals(item, "0.0.0.0/0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item, "::/0", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var parsedExclusions = exclusions
            .Select(item =>
            {
                try
                {
                    return IPNetwork2.Parse(item);
                }
                catch
                {
                    return null;
                }
            })
            .Where(item => item != null)
            .Cast<IPNetwork2>()
            .ToList();

        if (values.Any(item => string.Equals(item, "0.0.0.0/0", StringComparison.OrdinalIgnoreCase)))
        {
            var include = new List<IPNetwork2> { IPNetwork2.Parse("0.0.0.0/0") };
            foreach (var exclusion in parsedExclusions.Where(item => item.AddressFamily == AddressFamily.InterNetwork))
            {
                include = include.SelectMany(item => item.Subtract(exclusion)).ToList();
            }
            if (include.Count > 0)
            {
                result.AddRange(IPNetwork2.Supernet(include.ToArray()).Select(item => item.ToString()));
            }
        }

        if (values.Any(item => string.Equals(item, "::/0", StringComparison.OrdinalIgnoreCase)))
        {
            var include = new List<IPNetwork2> { IPNetwork2.Parse("::/0") };
            foreach (var exclusion in parsedExclusions.Where(item => item.AddressFamily == AddressFamily.InterNetworkV6))
            {
                include = include.SelectMany(item => item.Subtract(exclusion)).ToList();
            }
            if (include.Count > 0)
            {
                result.AddRange(IPNetwork2.Supernet(include.ToArray()).Select(item => item.ToString()));
            }
        }

        result.InsertRange(0, dnsHostRoutes);
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> BuildIncludedAllowedIps(
        List<string> originalValues,
        List<string> proxyIncludes,
        List<string> dnsHostRoutes)
    {
        var result = originalValues
            .Where(item => !string.Equals(item, "0.0.0.0/0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item, "::/0", StringComparison.OrdinalIgnoreCase))
            .ToList();
        result.AddRange(proxyIncludes);
        result.InsertRange(0, dnsHostRoutes);
        return result
            .Where(item => item.IsNotEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildDnsHostRoutes(string dnsValue)
    {
        var routes = new List<string>();
        foreach (var value in (dnsValue ?? string.Empty)
                     .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                continue;
            }
            routes.Add(address.AddressFamily == AddressFamily.InterNetwork
                ? $"{address}/32"
                : $"{address}/128");
        }
        return routes;
    }

    public async Task<AwgOperationResult> QuerySelectedStatusAsync()
    {
        var profile = GetSelectedProfile();
        return profile == null
            ? new AwgOperationResult { Success = true, State = "disconnected", Message = "Профиль AmneziaWG не выбран" }
            : await QueryStatusAsync(profile);
    }

    public async Task DisconnectAllAsync()
    {
        if (!HasCompleteEngine())
        {
            ActiveProfileId = null;
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await DisconnectAllCoreAsync();
            ActiveProfileId = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisconnectAllCoreAsync()
    {
        var tunnelNames = FindInstalledTunnelNames();
        if (tunnelNames.Count == 0)
        {
            ActiveProfileId = null;
            return;
        }

        var enginePath = Path.Combine(EngineDirectory, "amneziawg.exe");
        foreach (var tunnelName in tunnelNames)
        {
            Logging.SaveLog($"AmneziaWG uninstall service command: {tunnelName}");
            var result = await RunProcessAsync(enginePath, ["/uninstalltunnelservice", tunnelName], TimeSpan.FromSeconds(15));
            Logging.SaveLog($"AmneziaWG uninstall service result: tunnel={tunnelName}; exit={result.ExitCode}; output={result.Output.Trim()}; error={result.Error.Trim()}");
            if (result.ExitCode != 0 && ServiceExists(tunnelName))
            {
                throw new InvalidOperationException(GetProcessError(result, $"не удалось отключить туннель {tunnelName}"));
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (DateTime.UtcNow < deadline)
        {
            if (tunnelNames.All(name => !ServiceExists(name)))
            {
                ActiveProfileId = null;
                Logging.SaveLog("All SG AmneziaWG tunnel services removed");
                return;
            }
            await Task.Delay(400);
        }
        throw new TimeoutException("Не все туннельные службы AmneziaWG были удалены.");
    }

    public async Task<AwgOperationResult> QueryStatusAsync(AwgProfile profile)
    {
        if (!ServiceExists(profile.TunnelName))
        {
            if (string.Equals(ActiveProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                ActiveProfileId = null;
            }
            return new AwgOperationResult
            {
                Success = true,
                State = "disconnected",
                Message = "Туннельная служба не установлена",
                ServicePresent = false
            };
        }

        var sc = await RunProcessAsync("sc.exe", ["query", GetServiceName(profile.TunnelName)], TimeSpan.FromSeconds(5));
        if (sc.ExitCode != 0)
        {
            return new AwgOperationResult
            {
                Success = false,
                State = "error",
                Message = GetProcessError(sc, "не удалось прочитать состояние туннельной службы"),
                ServicePresent = true
            };
        }

        var stateCode = ParseServiceState(sc.Output + Environment.NewLine + sc.Error);
        switch (stateCode)
        {
            case 2:
                return new AwgOperationResult { Success = true, State = "connecting", Message = "Туннельная служба запускается", ServicePresent = true };
            case 3:
                return new AwgOperationResult { Success = true, State = "disconnecting", Message = "Туннельная служба останавливается", ServicePresent = true };
            case 1:
                return new AwgOperationResult { Success = false, State = "error", Message = "Туннельная служба остановлена, но ещё не удалена", ServicePresent = true };
            case 4:
                var handshake = await GetLatestHandshakeAsync(profile.TunnelName);
                if (handshake == null)
                {
                    return new AwgOperationResult { Success = true, State = "connecting", Message = "Служба запущена; handshake ещё не получен", ServicePresent = true };
                }
                ActiveProfileId = profile.Id;
                return new AwgOperationResult
                {
                    Success = true,
                    State = "connected",
                    Message = "Получен реальный handshake AmneziaWG",
                    ServicePresent = true,
                    LastHandshake = handshake
                };
            default:
                return new AwgOperationResult { Success = false, State = "error", Message = "Не удалось определить состояние туннельной службы", ServicePresent = true };
        }
    }

    private async Task<AwgOperationResult> WaitForHandshakeAsync(AwgProfile profile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var serviceSeen = false;
        AwgOperationResult? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await QueryStatusAsync(profile);
            serviceSeen |= last.ServicePresent;
            if (last.State == "connected" && last.LastHandshake != null)
            {
                return last;
            }
            if (last.State == "error" && last.ServicePresent)
            {
                throw new InvalidOperationException(last.Message);
            }
            if (!serviceSeen && DateTime.UtcNow > deadline - timeout + TimeSpan.FromSeconds(8))
            {
                throw new InvalidOperationException("Windows не создала туннельную службу AmneziaWG.");
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Handshake AmneziaWG не получен за {timeout.TotalSeconds:0} секунд: {last?.Message ?? "нет ответа"}");
    }

    public async Task<(long ReceivedBytes, long SentBytes)?> GetActiveTrafficTotalsAsync()
    {
        var profileId = ActiveProfileId.IsNotEmpty()
            ? ActiveProfileId
            : SelectedProfileId;

        var profile = GetProfile(profileId);
        if (profile == null || profile.TunnelName.IsNullOrEmpty())
        {
            return null;
        }

        var awgPath = Path.Combine(EngineDirectory, "awg.exe");
        var result = await RunProcessAsync(
            awgPath,
            ["show", profile.TunnelName, "transfer"],
            TimeSpan.FromSeconds(4));

        if (result.ExitCode != 0)
        {
            return null;
        }

        long receivedBytes = 0;
        long sentBytes = 0;
        var found = false;

        foreach (var line in result.Output
                     .Replace("\r\n", "\n")
                     .Split('\n'))
        {
            var fields = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 3
                || !long.TryParse(fields[^2], out var received)
                || !long.TryParse(fields[^1], out var sent))
            {
                continue;
            }

            received = Math.Max(0, received);
            sent = Math.Max(0, sent);

            receivedBytes = receivedBytes > long.MaxValue - received
                ? long.MaxValue
                : receivedBytes + received;

            sentBytes = sentBytes > long.MaxValue - sent
                ? long.MaxValue
                : sentBytes + sent;

            found = true;
        }

        return found
            ? (receivedBytes, sentBytes)
            : null;
    }

    private async Task<DateTime?> GetLatestHandshakeAsync(string tunnelName)
    {
        var awgPath = Path.Combine(EngineDirectory, "awg.exe");
        var result = await RunProcessAsync(awgPath, ["show", tunnelName, "latest-handshakes"], TimeSpan.FromSeconds(4));
        if (result.ExitCode != 0)
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var line in result.Output.Replace("\r\n", "\n").Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !long.TryParse(fields[^1], out var seconds) || seconds <= 0)
            {
                continue;
            }
            var candidate = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
            if (latest == null || candidate > latest.Value)
            {
                latest = candidate;
            }
        }
        return latest;
    }

    private List<string> FindInstalledTunnelNames()
    {
        var result = new List<string>();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
            if (services == null)
            {
                return result;
            }
            foreach (var serviceName in services.GetSubKeyNames())
            {
                const string prefix = "AmneziaWGTunnel$sgawg-";
                if (serviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(serviceName["AmneziaWGTunnel$".Length..]);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read AmneziaWG services", ex);
        }
        return result;
    }

    private static bool ServiceExists(string tunnelName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + GetServiceName(tunnelName), false);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetServiceName(string tunnelName) => "AmneziaWGTunnel$" + tunnelName;

    private static int ParseServiceState(string output)
    {
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }
            var right = line[(separator + 1)..].Trim();
            var first = right.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (int.TryParse(first, out var code) && code is >= 1 and <= 7)
            {
                return code;
            }
        }
        return 0;
    }

    private async Task<AwgProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(fileName).IsNullOrEmpty() ? AppContext.BaseDirectory : Path.GetDirectoryName(fileName)!
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new AwgProcessResult(-1, string.Empty, "процесс не запустился");
        }
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return new AwgProcessResult(-1, await outputTask, "превышено время ожидания процесса");
        }
        return new AwgProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string GetProcessError(AwgProcessResult result, string fallback)
    {
        var detail = result.Error.Trim();
        if (detail.IsNullOrEmpty())
        {
            detail = result.Output.Trim();
        }
        return detail.IsNullOrEmpty() ? fallback : $"{fallback}: {detail}";
    }

    private void EnsureEngine()
    {
        if (!HasCompleteEngine())
        {
            throw new FileNotFoundException("Не найдены amneziawg.exe, awg.exe и wintun.dll в bin\\awg.");
        }
    }

    private static AwgParsedConfig ParseConfig(string content)
    {
        var normalized = NormalizeConfig(content);
        if (normalized.IsNullOrEmpty())
        {
            throw new InvalidDataException("Конфигурация пуста.");
        }

        var result = new AwgParsedConfig();
        var section = string.Empty;
        var hasInterface = false;
        var hasPeer = false;
        var hasPrivateKey = false;
        var hasPublicKey = false;
        var hasAllowedIps = false;
        var privateKey = string.Empty;
        var publicKey = string.Empty;
        var presharedKey = string.Empty;
        var mtuValue = string.Empty;
        var metadataName = string.Empty;
        var metadataClient = string.Empty;
        var amneziaKeys = 0;

        foreach (var rawLine in normalized.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.IsNullOrEmpty())
            {
                continue;
            }
            if (line.StartsWith('#') || line.StartsWith(';'))
            {
                if (TryParseMetadataComment(line, out var metadataKey, out var metadataValue))
                {
                    if (metadataKey.Equals("name", StringComparison.OrdinalIgnoreCase)
                        && metadataName.IsNullOrEmpty())
                    {
                        metadataName = metadataValue;
                    }
                    else if (metadataKey.Equals("client", StringComparison.OrdinalIgnoreCase)
                             && metadataClient.IsNullOrEmpty())
                    {
                        metadataClient = metadataValue;
                    }
                }
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                hasInterface |= section == "interface";
                hasPeer |= section == "peer";
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }
            var key = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();
            if (section == "interface")
            {
                switch (key)
                {
                    case "privatekey":
                        privateKey = value;
                        hasPrivateKey = value.IsNotEmpty();
                        break;
                    case "address": result.Address = FirstCsv(value); break;
                    case "dns": result.DNS = CleanCsv(value); break;
                    case "mtu": mtuValue = value; break;
                    case "jc": case "jmin": case "jmax": case "s1": case "s2": case "s3": case "s4":
                    case "h1": case "h2": case "h3": case "h4": case "i1": case "i2": case "i3": case "i4": case "i5":
                        if (value.IsNotEmpty()) amneziaKeys++;
                        break;
                }
            }
            else if (section == "peer")
            {
                switch (key)
                {
                    case "publickey":
                        publicKey = value;
                        hasPublicKey = value.IsNotEmpty();
                        break;
                    case "presharedkey": presharedKey = value; break;
                    case "endpoint": result.Endpoint = value; break;
                    case "allowedips":
                        result.AllowedIps = CleanCsv(value);
                        hasAllowedIps = value.IsNotEmpty();
                        break;
                }
            }
        }

        if (!hasInterface) throw new InvalidDataException("Не найден раздел [Interface].");
        if (!hasPeer) throw new InvalidDataException("Не найден раздел [Peer].");
        if (!hasPrivateKey) throw new InvalidDataException("Не найден PrivateKey.");
        if (result.Address.IsNullOrEmpty()) throw new InvalidDataException("Не найден Address.");
        if (!hasPublicKey) throw new InvalidDataException("Не найден PublicKey.");
        if (result.Endpoint.IsNullOrEmpty()) throw new InvalidDataException("Не найден Endpoint.");
        if (!hasAllowedIps) throw new InvalidDataException("Не найден AllowedIPs.");

        ValidateAddressWithPrefix(result.Address, "Address");
        ValidateEndpoint(result.Endpoint);
        ValidateKey(privateKey, "PrivateKey", required: true);
        ValidateKey(publicKey, "PublicKey", required: true);
        ValidateKey(presharedKey, "PresharedKey", required: false);
        ValidateMtu(mtuValue);

        foreach (var allowed in result.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ValidateAddressWithPrefix(allowed, "AllowedIPs");
        }

        result.HasAmneziaParameters = amneziaKeys > 0;
        result.Protocol = result.HasAmneziaParameters ? "AmneziaWG 2.0" : "WireGuard";
        result.SuggestedName = metadataName.IsNotEmpty() ? metadataName : metadataClient;
        return result;
    }

    private static bool TryParseMetadataComment(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var match = Regex.Match(
            line,
            @"^[#;]\s*(?<key>Name|Client)\s*[:=]\s*(?<value>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        key = match.Groups["key"].Value.Trim();
        value = match.Groups["value"].Value.Trim().Trim('"', '\'');
        return value.IsNotEmpty();
    }

    private static string NormalizeConfig(string content)
    {
        var text = content
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ')
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Trim();

        // Accept configurations copied from Markdown/code blocks or from a UI
        // that placed literal \n sequences into the clipboard.
        text = Regex.Replace(
            text,
            @"^\s*```[^\n]*\n",
            string.Empty,
            RegexOptions.CultureInvariant);
        text = Regex.Replace(
            text,
            @"\n\s*```\s*$",
            string.Empty,
            RegexOptions.CultureInvariant);
        if (!text.Contains('\n') && text.Contains("\\n", StringComparison.Ordinal))
        {
            text = text.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal);
        }

        text = ExpandCollapsedConfig(text);
        return text.IsNullOrEmpty()
            ? string.Empty
            : text.Replace("\n", "\r\n") + "\r\n";
    }

    private static string ExpandCollapsedConfig(string text)
    {
        if (!text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("[Peer]", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        // Some panel/browser clipboard paths collapse a multiline .conf into a
        // single line. Recreate line boundaries only before known section and
        // key tokens; values themselves remain untouched.
        text = Regex.Replace(
            text,
            @"\s*(?=\[(?:Interface|Peer)\])",
            "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(
            text,
            @"\s+(?=[#;]\s*(?:Name|Client|Server|Source)\s*[:=])",
            "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(
            text,
            @"\s+(?=(?:Address|DNS|PrivateKey|MTU|Jc|Jmin|Jmax|S[1-4]|H[1-4]|I[1-5]|PublicKey|PresharedKey|AllowedIPs|Endpoint|PersistentKeepalive)\s*=)",
            "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text.Trim();
    }

    private static string FirstCsv(string value)
    {
        var separator = value.IndexOf(',');
        return (separator >= 0 ? value[..separator] : value).Trim();
    }

    private static string CleanCsv(string value)
    {
        return string.Join(", ", value.Split(',').Select(item => item.Trim()).Where(item => item.IsNotEmpty()));
    }

    private static void ValidateAddressWithPrefix(string value, string fieldName)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            throw new InvalidDataException($"Некорректное значение {fieldName}.");
        }
        if (parts.Length == 1)
        {
            return;
        }
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > maxPrefix)
        {
            throw new InvalidDataException($"Некорректная маска в {fieldName}.");
        }
    }

    private static void ValidateEndpoint(string endpoint)
    {
        var value = endpoint.Trim();
        string host;
        string portText;
        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            if (closing <= 1 || closing + 2 > value.Length || value[closing + 1] != ':')
            {
                throw new InvalidDataException("Некорректный Endpoint.");
            }
            host = value[1..closing];
            portText = value[(closing + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new InvalidDataException("Некорректный Endpoint.");
            }
            host = value[..separator];
            portText = value[(separator + 1)..];
        }
        if (host.IsNullOrEmpty() || !int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidDataException("Некорректный Endpoint или порт.");
        }
    }

    private static void ValidateKey(string value, string fieldName, bool required)
    {
        if (value.IsNullOrEmpty())
        {
            if (required)
            {
                throw new InvalidDataException($"Не найден {fieldName}.");
            }
            return;
        }
        try
        {
            if (Convert.FromBase64String(value).Length != 32)
            {
                throw new InvalidDataException($"Некорректный формат {fieldName}.");
            }
        }
        catch (FormatException)
        {
            throw new InvalidDataException($"Некорректный формат {fieldName}.");
        }
    }

    private static void ValidateMtu(string value)
    {
        if (value.IsNullOrEmpty())
        {
            return;
        }
        if (!int.TryParse(value, out var mtu) || mtu is < 576 or > 65535)
        {
            throw new InvalidDataException("MTU должен быть числом от 576 до 65535.");
        }
    }

    private static string NormalizeProfileName(string? requestedName, string sourceFileName)
    {
        var name = requestedName?.Trim();
        if (name.IsNullOrEmpty())
        {
            name = GetSuggestedName(sourceFileName);
        }
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }
        name = Regex.Replace(name, @"\s+", " ").Trim();
        if (name.Length > 80)
        {
            name = name[..80].Trim();
        }
        return name.IsNullOrEmpty() ? "AmneziaWG" : name;
    }

    private static string GetSuggestedName(string sourceFileName)
    {
        var name = Path.GetFileNameWithoutExtension(sourceFileName).Trim();
        foreach (var suffix in new[] { "-awg", "_awg", " awg" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length].Trim();
                break;
            }
        }
        return name.IsNullOrEmpty() ? "AmneziaWG" : name;
    }

    private void LoadStore()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                _store = new AwgProfileStore();
                return;
            }
            _store = JsonSerializer.Deserialize<AwgProfileStore>(File.ReadAllText(StorePath), _jsonOptions) ?? new AwgProfileStore();
            _store.Profiles ??= [];
            _store.Profiles.RemoveAll(profile => profile.Id.IsNullOrEmpty() || profile.ConfigFileName.IsNullOrEmpty());
            if (_store.SelectedProfileId.IsNotEmpty() && _store.Profiles.All(profile => profile.Id != _store.SelectedProfileId))
            {
                _store.SelectedProfileId = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Load AmneziaWG profile store", ex);
            _store = new AwgProfileStore();
        }
    }

    private void SaveStore()
    {
        Directory.CreateDirectory(StorageDirectory);
        var temporary = StorePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_store, _jsonOptions), new UTF8Encoding(false));
        File.Move(temporary, StorePath, true);
    }

    private static AwgProfile CloneProfile(AwgProfile source)
    {
        return new AwgProfile
        {
            Id = source.Id,
            Name = source.Name,
            TunnelName = source.TunnelName,
            ConfigFileName = source.ConfigFileName,
            Endpoint = source.Endpoint,
            Address = source.Address,
            DNS = source.DNS,
            Protocol = source.Protocol,
            ContentHash = source.ContentHash,
            CountryCode = source.CountryCode,
            CreatedAt = source.CreatedAt
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Delete AmneziaWG profile file", ex);
        }
    }
}
