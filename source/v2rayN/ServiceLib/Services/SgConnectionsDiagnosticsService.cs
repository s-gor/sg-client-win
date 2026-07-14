using System.Text.Json;

namespace ServiceLib.Services;

/// <summary>
/// Owns the minimal local Xray diagnostics required by the SG Connections window.
/// It enables info-level file logging, DNS answers and HTTP/TLS/QUIC sniffing,
/// but never enables verbose debug logging unless the user already selected it.
/// </summary>
public static class SgConnectionsDiagnosticsService
{
    private static readonly string[] RequiredOverrides = ["http", "tls", "quic"];

    public sealed record DiagnosticsState(
        bool SettingsEnabled,
        bool ActiveConfigEnabled,
        string AccessLogPath,
        string ErrorLogPath);

    public static bool IsEnabled(Config config)
    {
        if (config.CoreBasicItem?.LogEnabled != true || config.Inbound is not { Count: > 0 })
        {
            return false;
        }

        return config.Inbound.Any(inbound =>
        {
            var overrides = inbound.DestOverride ?? [];
            return inbound.SniffingEnabled
                && RequiredOverrides.All(required =>
                    overrides.Contains(required, StringComparer.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Reads the exact config.json used by the running Xray process. This avoids
    /// guessing today's filename and works identically in TUN, System Proxy and
    /// Local Proxy, including a core that stayed running across midnight.
    /// </summary>
    public static DiagnosticsState GetState(Config config)
    {
        var fallbackAccess = Utils.GetLogPath($"Vaccess_{DateTime.Now:yyyy-MM-dd}.txt");
        var fallbackError = Utils.GetLogPath($"Verror_{DateTime.Now:yyyy-MM-dd}.txt");
        var settingsEnabled = IsEnabled(config);

        var configPath = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        if (!File.Exists(configPath))
        {
            return new(settingsEnabled, false, fallbackAccess, fallbackError);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;

            var accessPath = fallbackAccess;
            var errorPath = fallbackError;
            var hasAccess = false;
            var hasError = false;
            var dnsLog = false;
            var logLevelEnabled = false;

            if (root.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.Object)
            {
                if (TryGetNonEmptyString(log, "access", out var access))
                {
                    accessPath = ResolveLogPath(access);
                    hasAccess = true;
                }

                if (TryGetNonEmptyString(log, "error", out var error))
                {
                    errorPath = ResolveLogPath(error);
                    hasError = true;
                }

                if (log.TryGetProperty("dnsLog", out var dnsElement)
                    && dnsElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    dnsLog = dnsElement.GetBoolean();
                }

                if (TryGetNonEmptyString(log, "loglevel", out var logLevel))
                {
                    logLevelEnabled = !string.Equals(logLevel, "none", StringComparison.OrdinalIgnoreCase);
                }
            }

            var sniffingEnabled = HasRequiredActiveSniffing(root);
            var activeEnabled = hasAccess
                && hasError
                && dnsLog
                && logLevelEnabled
                && sniffingEnabled;

            return new(settingsEnabled, activeEnabled, accessPath, errorPath);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read active Xray diagnostics config", ex);
            return new(settingsEnabled, false, fallbackAccess, fallbackError);
        }
    }

    public static async Task<bool> EnableAsync(Config config)
    {
        config.CoreBasicItem ??= new CoreBasicItem();
        config.CoreBasicItem.LogEnabled = true;
        if (!string.Equals(config.CoreBasicItem.Loglevel, "debug", StringComparison.OrdinalIgnoreCase))
        {
            config.CoreBasicItem.Loglevel = "info";
        }

        config.Inbound ??= [];
        if (config.Inbound.Count == 0)
        {
            config.Inbound.Add(new InItem
            {
                Protocol = nameof(EInboundProtocol.socks),
                LocalPort = 10808,
                UdpEnabled = true,
                SniffingEnabled = true,
                DestOverride = RequiredOverrides.ToList(),
                RouteOnly = false,
            });
        }
        else
        {
            foreach (var inbound in config.Inbound)
            {
                inbound.SniffingEnabled = true;
                inbound.DestOverride = (inbound.DestOverride ?? [])
                    .Concat(RequiredOverrides)
                    .Where(value => value.IsNotEmpty())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return await ConfigHandler.SaveConfig(config) == 0;
    }

    public static void CleanupExpiredXrayLogs(TimeSpan? maximumAge = null)
    {
        var cutoffUtc = DateTime.UtcNow - (maximumAge ?? TimeSpan.FromDays(2));
        var directory = Utils.GetLogPath();
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var pattern in new[] { "Vaccess_*.txt", "Verror_*.txt" })
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"Cleanup old Xray diagnostics: {path}", ex);
                }
            }
        }
    }

    private static bool HasRequiredActiveSniffing(JsonElement root)
    {
        if (!root.TryGetProperty("inbounds", out var inbounds)
            || inbounds.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var inbound in inbounds.EnumerateArray())
        {
            if (!inbound.TryGetProperty("sniffing", out var sniffing)
                || sniffing.ValueKind != JsonValueKind.Object
                || !sniffing.TryGetProperty("enabled", out var enabled)
                || enabled.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            if (!sniffing.TryGetProperty("destOverride", out var overrides)
                || overrides.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = overrides.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (RequiredOverrides.All(values.Contains))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetNonEmptyString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString()?.Trim() ?? string.Empty;
        return value.IsNotEmpty();
    }

    private static string ResolveLogPath(string value)
    {
        if (Path.IsPathRooted(value))
        {
            return value;
        }

        return Path.GetFullPath(Path.Combine(Utils.GetBinConfigPath(), value));
    }
}
