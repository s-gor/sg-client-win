namespace ServiceLib.Common;

public sealed record SgXrayDpiProfile(
    bool Enabled,
    string Packets,
    List<string> Lengths,
    List<string> Delays,
    string MaxSplit,
    bool EnableUdpNoise,
    string NoiseReset,
    string NoiseLength,
    string NoiseDelay);

public sealed record SgSingboxDpiProfile(
    bool RecordFragment,
    bool Fragment,
    string? FragmentFallbackDelay);

public static class SgDpiModeHelper
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
    };

    public static string Normalize(string? mode)
    {
        return mode is "auto" or "off" or "tls" or "tls_noise" or "custom" ? mode : "auto";
    }

    public static string GetTitle(string? mode)
    {
        return Normalize(mode) switch
        {
            "off" => "Выключена",
            "tls" => "Дробление TLS / ClientHello",
            "tls_noise" => "TLS + шум — экспериментально",
            "custom" => "Пользовательские параметры",
            _ => "Автоматически — рекомендуется",
        };
    }

    public static SgXrayDpiProfile GetXrayProfile(SgQuickSettingsItem? settings)
    {
        return GetXrayProfile(settings?.DpiMode, settings?.DpiCustomJson);
    }

    public static SgXrayDpiProfile GetXrayProfile(string? mode, string? customJson = null)
    {
        var normalized = Normalize(mode);
        if (normalized == "custom"
            && TryParseCustomProfiles(customJson, out var customXray, out _, out _))
        {
            return customXray;
        }

        return normalized switch
        {
            "off" => new(false, "tlshello", [], [], "0", false, "0", "0", "0"),
            "tls" => new(true, "tlshello", ["30-60"], ["10-20"], "8-12", false, "0", "0", "0"),
            "tls_noise" => new(true, "tlshello", ["20-40"], ["8-16"], "10-14", true, "30-60", "8-32", "5-10"),
            _ => new(true, "tlshello", ["50-90"], ["5-10"], "6-8", false, "0", "0", "0"),
        };
    }

    public static SgSingboxDpiProfile GetSingboxProfile(SgQuickSettingsItem? settings)
    {
        return GetSingboxProfile(settings?.DpiMode, settings?.DpiCustomJson);
    }

    public static SgSingboxDpiProfile GetSingboxProfile(string? mode, string? customJson = null)
    {
        var normalized = Normalize(mode);
        if (normalized == "custom"
            && TryParseCustomProfiles(customJson, out _, out var customSingbox, out _))
        {
            return customSingbox;
        }

        return normalized switch
        {
            "off" => new(false, false, null),
            "tls" => new(false, true, "50ms"),
            "tls_noise" => new(true, true, "50ms"),
            _ => new(true, false, null),
        };
    }

    public static string GetDefaultCustomJson()
    {
        return BuildCustomJson(GetXrayProfile("auto"), GetSingboxProfile("auto"));
    }

    public static string GetCustomJsonForMode(string? mode, string? currentCustomJson = null)
    {
        var normalized = Normalize(mode);
        if (normalized == "custom"
            && TryParseCustomProfiles(currentCustomJson, out var customXray, out var customSingbox, out _))
        {
            return BuildCustomJson(customXray, customSingbox);
        }

        return BuildCustomJson(GetXrayProfile(normalized), GetSingboxProfile(normalized));
    }

    public static bool TryParseCustomProfiles(
        string? json,
        out SgXrayDpiProfile xray,
        out SgSingboxDpiProfile singbox,
        out string error)
    {
        xray = GetXrayProfile("auto");
        singbox = GetSingboxProfile("auto");
        error = string.Empty;

        if (json.IsNullOrEmpty())
        {
            error = "JSON не заполнен.";
            return false;
        }

        try
        {
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("Корневой элемент должен быть объектом JSON.");

            var xrayNode = root["xray"] as JsonObject
                ?? throw new InvalidDataException("Не найден объект xray.");
            var singNode = root["sing-box"] as JsonObject
                ?? throw new InvalidDataException("Не найден объект sing-box.");

            var tcp = xrayNode["tcp"] as JsonArray
                ?? throw new InvalidDataException("xray.tcp должен быть массивом.");
            var fragment = tcp
                .OfType<JsonObject>()
                .FirstOrDefault(item => string.Equals(item["type"]?.GetValue<string>(), "fragment", StringComparison.OrdinalIgnoreCase));

            var xrayEnabled = fragment != null;
            var packets = "tlshello";
            var lengths = new List<string>();
            var delays = new List<string>();
            var maxSplit = "0";

            if (fragment != null)
            {
                var settings = fragment["settings"] as JsonObject
                    ?? throw new InvalidDataException("Не найден xray.tcp[].settings.");

                packets = ReadRequiredString(settings, "packets");
                lengths = ReadStringList(settings, "lengths", "length");
                delays = ReadStringList(settings, "delays", "delay");
                maxSplit = ReadRequiredString(settings, "maxSplit");

                ValidateRangeList(lengths, "lengths", allowMilliseconds: false);
                ValidateRangeList(delays, "delays", allowMilliseconds: true);
                ValidateRange(maxSplit, "maxSplit", allowMilliseconds: false);
            }
            else if (tcp.Count > 0)
            {
                throw new InvalidDataException("xray.tcp содержит неподдерживаемые элементы. Разрешён type=fragment или пустой массив для выключенного дробления.");
            }

            var enableNoise = false;
            var noiseReset = "0";
            var noiseLength = "0";
            var noiseDelay = "0";
            if (xrayNode["udp"] is JsonArray udp)
            {
                var noise = udp
                    .OfType<JsonObject>()
                    .FirstOrDefault(item => string.Equals(item["type"]?.GetValue<string>(), "noise", StringComparison.OrdinalIgnoreCase));
                if (noise?["settings"] is JsonObject noiseSettings)
                {
                    if (!xrayEnabled)
                    {
                        throw new InvalidDataException("UDP-noise нельзя включить без Xray TCP fragment.");
                    }

                    enableNoise = true;
                    noiseReset = ReadRequiredString(noiseSettings, "reset");
                    if (noiseSettings["noise"] is not JsonArray noiseItems
                        || noiseItems.Count == 0
                        || noiseItems[0] is not JsonObject firstNoise)
                    {
                        throw new InvalidDataException("Для UDP-noise требуется непустой settings.noise.");
                    }
                    noiseLength = ReadRequiredString(firstNoise, "rand", "length");
                    noiseDelay = ReadRequiredString(firstNoise, "delay");
                    ValidateRange(noiseReset, "udp.reset", allowMilliseconds: false);
                    ValidateRange(noiseLength, "udp.noise.rand", allowMilliseconds: false);
                    ValidateRange(noiseDelay, "udp.noise.delay", allowMilliseconds: true);
                }
                else if (udp.Count > 0)
                {
                    throw new InvalidDataException("xray.udp содержит неподдерживаемые элементы. Разрешён type=noise или пустой массив.");
                }
            }
            else
            {
                throw new InvalidDataException("xray.udp должен быть массивом.");
            }

            var recordFragment = ReadBool(singNode, "record_fragment");
            var singFragment = ReadBool(singNode, "fragment");
            string? fallback = null;
            if (singNode["fragment_fallback_delay"] is JsonValue fallbackValue
                && fallbackValue.TryGetValue<string>(out var fallbackText))
            {
                fallback = fallbackText?.Trim();
            }
            if (singFragment && fallback.IsNullOrEmpty())
            {
                throw new InvalidDataException("Для sing-box fragment требуется fragment_fallback_delay, например 50ms.");
            }

            xray = new(
                xrayEnabled,
                packets,
                lengths,
                delays,
                maxSplit,
                enableNoise,
                noiseReset,
                noiseLength,
                noiseDelay);
            singbox = new(recordFragment, singFragment, singFragment ? fallback : null);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void ApplyLegacyFragmentSettings(Config config)
    {
        var profile = GetXrayProfile(config.SgQuickSettingsItem);
        config.CoreBasicItem.EnableFragment = profile.Enabled;
        config.CoreBasicItem.EnableFinalFragment = false;
        config.Fragment4RayItem ??= new Fragment4RayItem();
        config.Fragment4RayItem.Packets = profile.Packets;
        config.Fragment4RayItem.Length = profile.Lengths.FirstOrDefault() ?? "50-90";
        config.Fragment4RayItem.Interval = profile.Delays.FirstOrDefault() ?? "5-10";
    }

    private static string BuildCustomJson(SgXrayDpiProfile xray, SgSingboxDpiProfile singbox)
    {
        var tcp = new JsonArray();
        if (xray.Enabled)
        {
            tcp.Add(new JsonObject
            {
                ["type"] = "fragment",
                ["settings"] = new JsonObject
                {
                    ["packets"] = xray.Packets,
                    ["length"] = xray.Lengths.FirstOrDefault() ?? "50-90",
                    ["delay"] = xray.Delays.FirstOrDefault() ?? "5-10",
                    ["maxSplit"] = xray.MaxSplit,
                },
            });
        }

        var udp = new JsonArray();
        if (xray.EnableUdpNoise)
        {
            udp.Add(new JsonObject
            {
                ["type"] = "noise",
                ["settings"] = new JsonObject
                {
                    ["reset"] = xray.NoiseReset,
                    ["noise"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "rand",
                            ["rand"] = xray.NoiseLength,
                            ["delay"] = xray.NoiseDelay,
                        },
                    },
                },
            });
        }

        var root = new JsonObject
        {
            ["xray"] = new JsonObject
            {
                ["tcp"] = tcp,
                ["udp"] = udp,
            },
            ["sing-box"] = new JsonObject
            {
                ["record_fragment"] = singbox.RecordFragment,
                ["fragment"] = singbox.Fragment,
                ["fragment_fallback_delay"] = singbox.FragmentFallbackDelay,
            },
        };
        return root.ToJsonString(PrettyJson);
    }

    private static string ReadRequiredString(JsonObject node, params string[] names)
    {
        foreach (var name in names)
        {
            if (node[name] is JsonValue value
                && value.TryGetValue<string>(out var text)
                && text.IsNotEmpty())
            {
                return text.Trim();
            }
        }
        throw new InvalidDataException($"Не заполнено поле {string.Join("/", names)}.");
    }

    private static List<string> ReadStringList(JsonObject node, params string[] names)
    {
        foreach (var name in names)
        {
            if (node[name] is JsonArray array)
            {
                var result = array
                    .Select(item => item?.GetValue<string>()?.Trim())
                    .Where(item => item.IsNotEmpty())
                    .Cast<string>()
                    .ToList();
                if (result.Count == 0)
                {
                    throw new InvalidDataException($"Массив {name} не должен быть пустым.");
                }
                return result;
            }
            if (node[name] is JsonValue value
                && value.TryGetValue<string>(out var text)
                && text.IsNotEmpty())
            {
                return [text.Trim()];
            }
        }
        throw new InvalidDataException($"Не найдено поле {string.Join("/", names)}.");
    }

    private static bool ReadBool(JsonObject node, string name)
    {
        if (node[name] is JsonValue value && value.TryGetValue<bool>(out var result))
        {
            return result;
        }
        throw new InvalidDataException($"Поле sing-box.{name} должно быть true или false.");
    }

    private static void ValidateRangeList(IEnumerable<string> values, string name, bool allowMilliseconds)
    {
        foreach (var value in values)
        {
            ValidateRange(value, name, allowMilliseconds);
        }
    }

    private static void ValidateRange(string value, string name, bool allowMilliseconds)
    {
        var normalized = allowMilliseconds && value.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            ? value[..^2]
            : value;
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2
            || parts.Any(part => !int.TryParse(part, out var number) || number < 0))
        {
            throw new InvalidDataException($"Поле {name} содержит неверный диапазон «{value}».");
        }
        if (parts.Length == 2
            && int.Parse(parts[0]) > int.Parse(parts[1]))
        {
            throw new InvalidDataException($"В поле {name} начало диапазона больше конца: «{value}».");
        }
    }
}
