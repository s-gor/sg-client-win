namespace ServiceLib.Manager;

public sealed class SgRussiaRulesManager
{
    private static readonly Lazy<SgRussiaRulesManager> _instance = new(() => new SgRussiaRulesManager());
    public static SgRussiaRulesManager Instance => _instance.Value;

    private const string GeoBase = "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release";
    private const string WhiteBase = "https://raw.githubusercontent.com/GrimbirdUsers/ru-routing-dat/main";
    private const string WhiteRootRelativePath = "rules/ru-white";
    private static readonly string WhiteDomainsRelativePath = Path.Combine(WhiteRootRelativePath, "ru-white-domains.txt");
    private static readonly string WhiteCidrsRelativePath = Path.Combine(WhiteRootRelativePath, "ru-white-cidrs.txt");
    private const string WhiteDomainRootCategory = "category-ru-whitelist";
    private const string ManifestFileName = "sg-routing-rules-manifest.json";
    private static readonly TimeSpan RefreshAge = TimeSpan.FromHours(24);

    // Deliberately granular service/network categories from ru-routing-dat.
    // Never add data-geoip/ru.txt here: that file represents almost all RU address space,
    // not the regulator/service white list.
    private static readonly string[] WhiteIpCategories =
    [
        "ru-wildberries",
        "ru-yandex",
        "ru-vk",
        "ru-banks",
        "ru-ozon",
        "ru-analytics",
        "ru-payments",
        "ru-cdn",
        "ru-avito",
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record RuleFileSpec(string Url, string RelativePath, long MinimumBytes);
    private sealed record GeneratedRuleFile(string Url, string RelativePath, byte[] Bytes, long MinimumBytes);

    private sealed class RuleManifest
    {
        public string Source { get; set; } = $"{GeoBase}; {WhiteBase}";
        public string Version { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; }
        public List<RuleManifestEntry> Files { get; set; } = [];
    }

    private sealed class RuleManifestEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    private static readonly RuleFileSpec[] CoreFiles =
    [
        new($"{GeoBase}/geoip.dat", "geoip.dat", 1_000_000),
        new($"{GeoBase}/geosite.dat", "geosite.dat", 1_000_000),
        new($"{GeoBase}/sing-box/rule-set-geoip/geoip-ru.srs", Path.Combine("srss", "geoip-ru.srs"), 100),
        new($"{GeoBase}/sing-box/rule-set-geoip/geoip-ru-blocked.srs", Path.Combine("srss", "geoip-ru-blocked.srs"), 100),
        new($"{GeoBase}/sing-box/rule-set-geosite/geosite-ru-available-only-inside.srs", Path.Combine("srss", "geosite-ru-available-only-inside.srs"), 100),
        new($"{GeoBase}/sing-box/rule-set-geosite/geosite-ru-blocked.srs", Path.Combine("srss", "geosite-ru-blocked.srs"), 100),
        new($"{GeoBase}/sing-box/rule-set-geosite/geosite-category-ads-all.srs", Path.Combine("srss", "geosite-category-ads-all.srs"), 100),
    ];

    private SgRussiaRulesManager()
    {
    }

    public static void ApplySources(Config config)
    {
        // GeoFiles are selected and managed in the dedicated GeoFiles UI.
        // Do not silently replace the user's geoip.dat/geosite.dat source.
        if (config.ConstItem.SrsSourceUrl.IsNullOrEmpty())
        {
            config.ConstItem.SrsSourceUrl = Global.SingboxRulesetSources[1];
        }
    }

    public bool HasUsableRules()
    {
        var manifest = ReadManifest();
        if (manifest?.Files.Count > 0)
        {
            foreach (var spec in CoreFiles)
            {
                if (!ManifestEntryMatchesFile(manifest, spec.RelativePath, spec.MinimumBytes))
                {
                    return false;
                }
            }

            if (!ManifestEntryMatchesFile(manifest, WhiteDomainsRelativePath, 1_000)
                || !ManifestEntryMatchesFile(manifest, WhiteCidrsRelativePath, 500))
            {
                return false;
            }

            return HasUsableWhiteList();
        }

        // Accept files from a build-time/bootstrap snapshot without a manifest.
        return CoreFiles.All(item =>
        {
            var path = TargetPath(item.RelativePath);
            return File.Exists(path) && new FileInfo(path).Length >= item.MinimumBytes;
        }) && HasUsableWhiteList();
    }

    public bool HasUsableWhiteList()
    {
        return GetWhiteDomains().Count >= 400 && GetWhiteIpCidrs().Count >= 50;
    }

    public IReadOnlyList<string> GetWhiteDomains()
    {
        var path = TargetPath(WhiteDomainsRelativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return ParseWhiteDomains(File.ReadLines(path));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read Russia White List domains", ex);
            return [];
        }
    }

    public IReadOnlyList<string> GetWhiteDomainHosts()
    {
        return ExtractResolvableWhiteDomainHosts(GetWhiteDomains());
    }

    public static List<string> ExtractResolvableWhiteDomainHosts(IEnumerable<string> rules)
    {
        var result = new List<string>();
        foreach (var raw in rules ?? [])
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.IsNullOrEmpty())
            {
                continue;
            }

            if (value.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                value = value["domain:".Length..];
            }
            else if (value.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
            {
                value = value["full:".Length..];
            }
            else
            {
                // regexp:/keyword: cannot be represented by an AWG IP route
                // until an actual hostname is observed, so do not invent one.
                continue;
            }

            value = value.Trim().Trim('.').ToLowerInvariant();
            if (value.IsNullOrEmpty() || IPAddress.TryParse(value, out _))
            {
                continue;
            }

            if (Uri.CheckHostName(value) == UriHostNameType.Dns)
            {
                result.Add(value);
            }
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetWhiteIpCidrs()
    {
        var path = TargetPath(WhiteCidrsRelativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return ParseWhiteIpCidrs(File.ReadLines(path));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read Russia White List CIDRs", ex);
            return [];
        }
    }

    public static List<string> ParseWhiteDomains(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var value = StripRuleComment(raw).Trim();
            if (value.IsNullOrEmpty() || value.StartsWith("include:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // v2fly geosite source attributes follow the first whitespace-delimited token.
            value = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            if (value.IsNullOrEmpty())
            {
                continue;
            }

            if (!value.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("full:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
            {
                value = $"domain:{value.TrimStart('.')}";
            }

            result.Add(value);
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> ParseWhiteIpCidrs(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var cleaned = StripRuleComment(raw).Trim();
            if (cleaned.IsNullOrEmpty())
            {
                continue;
            }

            // Some upstream category files publish several CIDRs on one whitespace-delimited line.
            foreach (var value in cleaned.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    result.Add(IPNetwork2.Parse(value).ToString());
                }
                catch
                {
                    // Ignore malformed upstream entries; valid CIDRs remain usable.
                }
            }
        }
        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool NeedsRefresh(Config config)
    {
        if (!HasUsableRules())
        {
            return true;
        }

        return config.SgQuickSettingsItem.RussiaRulesUpdatedUtc == default
            || DateTime.UtcNow - config.SgQuickSettingsItem.RussiaRulesUpdatedUtc > RefreshAge;
    }

    public string GetIntegritySummary()
    {
        var manifest = ReadManifest();
        if (manifest == null)
        {
            return HasUsableRules() ? "Файлы найдены; локальный SHA-256 будет создан при следующем обновлении" : "Наборы не установлены";
        }

        return HasUsableRules()
            ? $"Локальная целостность SHA-256 проверена · {manifest.Files.Count} файлов"
            : "Ошибка локальной целостности или отсутствует файл";
    }

    public async Task<(bool Success, string Message)> ValidateRequiredCategoriesAsync(
        SgSmartRoutingItem item,
        IProgress<string>? progress = null)
    {
        var domains = new List<string>();
        var ips = new List<string>();
        var whiteDomainCount = 0;
        var whiteIpCount = 0;

        if (SgSmartRoutingHelper.NormalizeRussiaScope(item.RussiaScope) == SgSmartRoutingHelper.RussiaScopeWhiteIp)
        {
            whiteDomainCount = GetWhiteDomains().Count;
            whiteIpCount = GetWhiteIpCidrs().Count;
            if (whiteDomainCount < 400 || whiteIpCount < 50)
            {
                return (false, "RU White List не установлен или повреждён. Нажмите «Обновить наборы» и повторите сохранение.");
            }
            progress?.Report($"RU White List: {whiteDomainCount} доменов · {whiteIpCount} CIDR-подсетей…");
        }

        if (item.AdsAction != item.DefaultAction)
        {
            domains.Add("geosite:category-ads-all");
        }
        if (item.BlockedAction != item.DefaultAction)
        {
            domains.Add("geosite:ru-blocked");
            ips.Add("geoip:ru-blocked");
        }

        domains.AddRange(SgSmartRoutingHelper.GetRussiaDomainRules(item));
        ips.AddRange(SgSmartRoutingHelper.GetRussiaIpRules(item));

        if (item.Preset == SgSmartRoutingHelper.PresetCustom)
        {
            domains.AddRange(item.CustomDirectDomains.Where(value =>
                value.StartsWith(Global.GeoSitePrefix, StringComparison.OrdinalIgnoreCase)));
            domains.AddRange(item.CustomProxyDomains.Where(value =>
                value.StartsWith(Global.GeoSitePrefix, StringComparison.OrdinalIgnoreCase)));
            domains.AddRange(item.CustomBlockDomains.Where(value =>
                value.StartsWith(Global.GeoSitePrefix, StringComparison.OrdinalIgnoreCase)));

            ips.AddRange(item.CustomDirectIps.Where(value =>
                value.StartsWith(Global.GeoIPPrefix, StringComparison.OrdinalIgnoreCase)));
            ips.AddRange(item.CustomProxyIps.Where(value =>
                value.StartsWith(Global.GeoIPPrefix, StringComparison.OrdinalIgnoreCase)));
            ips.AddRange(item.CustomBlockIps.Where(value =>
                value.StartsWith(Global.GeoIPPrefix, StringComparison.OrdinalIgnoreCase)));
        }

        domains = domains.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ips = ips.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (domains.Count == 0 && ips.Count == 0)
        {
            return whiteDomainCount > 0
                ? (true, $"RU White List проверен: {whiteDomainCount} доменов · {whiteIpCount} CIDR-подсетей.")
                : (true, "Дополнительные категории GeoFiles не требуются.");
        }

        var xray = Utils.GetBinPath("xray.exe", "xray");
        var assetDirectory = Utils.GetBinPath(string.Empty);
        var geoIp = Path.Combine(assetDirectory, "geoip.dat");
        var geoSite = Path.Combine(assetDirectory, "geosite.dat");
        if (!File.Exists(xray))
        {
            return (false, $"Не найден Xray для проверки GeoFiles: {xray}");
        }
        if (!File.Exists(geoIp) || !File.Exists(geoSite))
        {
            return (false, "Не найдены выбранные geoip.dat или geosite.dat. Откройте «Обслуживание» → «GeoFiles» и восстановите рабочий комплект.");
        }

        progress?.Report("Проверяю необходимые категории в выбранных GeoFiles…");
        var configPath = Path.Combine(assetDirectory, $"sg-routing-category-test-{Guid.NewGuid():N}.json");
        try
        {
            var rules = new List<object>();
            if (domains.Count > 0)
            {
                rules.Add(new
                {
                    type = "field",
                    domain = domains,
                    outboundTag = "direct",
                });
            }
            if (ips.Count > 0)
            {
                rules.Add(new
                {
                    type = "field",
                    ip = ips,
                    outboundTag = "direct",
                });
            }

            var config = new
            {
                log = new { loglevel = "warning" },
                inbounds = new[]
                {
                    new
                    {
                        listen = "127.0.0.1",
                        port = 10889,
                        protocol = "socks",
                        settings = new { udp = true },
                    },
                },
                outbounds = new[]
                {
                    new { protocol = "freedom", tag = "direct" },
                },
                routing = new
                {
                    domainStrategy = "AsIs",
                    rules,
                },
            };
            await File.WriteAllTextAsync(configPath, JsonUtils.Serialize(config, true), new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = xray,
                WorkingDirectory = assetDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("-test");
            startInfo.ArgumentList.Add("-config");
            startInfo.ArgumentList.Add(configPath);
            startInfo.Environment["XRAY_LOCATION_ASSET"] = assetDirectory;

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return (false, "Проверка категорий GeoFiles превысила 30 секунд. Настройки не применены.");
            }

            var output = string.Join(
                Environment.NewLine,
                new[] { await stdout, await stderr }
                    .Where(value => value.IsNotEmpty())
                    .Select(value => value.Trim()));
            if (process.ExitCode != 0)
            {
                var required = string.Join(", ", domains.Concat(ips));
                var detail = output.IsNotEmpty() ? $" Ядро сообщает: {output}" : string.Empty;
                return (false,
                    $"В выбранных GeoFiles отсутствует или повреждена одна из необходимых категорий: {required}.{detail} "
                    + "Настройки не применены; действующая конфигурация сохранена.");
            }

            return (true, $"Категории GeoFiles проверены: {string.Join(", ", domains.Concat(ips))}.");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Validate smart routing GeoFiles", ex);
            return (false, $"Не удалось проверить категории GeoFiles: {ex.Message}. Настройки не применены.");
        }
        finally
        {
            try
            {
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }
            catch
            {
            }
        }
    }

    public async Task<(bool Success, string Message)> EnsureRulesAsync(Config config, bool force, IProgress<string>? progress = null)
    {
        await _gate.WaitAsync();
        try
        {
            ApplySources(config);
            if (!force && !NeedsRefresh(config))
            {
                return (true, "Наборы маршрутизации уже загружены; локальная целостность проверена.");
            }

            var hadUsableRules = HasUsableRules();
            var staged = new List<(string Temp, string Target, string? Backup, RuleManifestEntry Entry)>();
            var manifestPath = TargetPath(ManifestFileName);
            var manifestBackup = File.Exists(manifestPath) ? manifestPath + ".sg-backup" : null;
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(3)
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SG-Client/099F");

                for (var index = 0; index < CoreFiles.Length; index++)
                {
                    var spec = CoreFiles[index];
                    progress?.Report($"Базовые наборы {index + 1} из {CoreFiles.Length}: {Path.GetFileName(spec.RelativePath)}");
                    var bytes = await client.GetByteArrayAsync(spec.Url);
                    StageBytes(staged, spec.Url, spec.RelativePath, bytes, spec.MinimumBytes);
                }

                progress?.Report("RU White List: загружаю доменные категории ru-routing-dat…");
                var whiteFiles = await BuildWhiteListSnapshotAsync(client, progress);
                foreach (var generated in whiteFiles)
                {
                    StageBytes(staged, generated.Url, generated.RelativePath, generated.Bytes, generated.MinimumBytes);
                }

                progress?.Report("Создание резервной копии и применение наборов…");
                if (manifestBackup != null)
                {
                    File.Copy(manifestPath, manifestBackup, true);
                }

                foreach (var item in staged)
                {
                    if (item.Backup != null)
                    {
                        File.Copy(item.Target, item.Backup, true);
                    }
                    File.Move(item.Temp, item.Target, true);
                }

                var updatedUtc = DateTime.UtcNow;
                var version = updatedUtc.ToString("yyyy.MM.dd HH:mm 'UTC'");
                var manifest = new RuleManifest
                {
                    Version = version,
                    UpdatedUtc = updatedUtc,
                    Files = staged.Select(item => item.Entry).ToList(),
                };
                var manifestTemp = manifestPath + ".sg-new";
                await File.WriteAllTextAsync(manifestTemp, JsonUtils.Serialize(manifest, true), new UTF8Encoding(false));
                File.Move(manifestTemp, manifestPath, true);

                if (!HasUsableRules())
                {
                    throw new InvalidDataException("Контрольная проверка локальной целостности после установки не пройдена.");
                }

                config.SgQuickSettingsItem.RussiaRulesUpdatedUtc = updatedUtc;
                config.SgQuickSettingsItem.RussiaRulesVersion = version;
                await ConfigHandler.SaveConfig(config);

                foreach (var item in staged.Where(item => item.Backup != null))
                {
                    File.Delete(item.Backup!);
                }
                if (manifestBackup != null && File.Exists(manifestBackup))
                {
                    File.Delete(manifestBackup);
                }

                var domainCount = GetWhiteDomains().Count;
                var cidrCount = GetWhiteIpCidrs().Count;
                return (true, $"Наборы обновлены. RU White List: {domainCount} доменов · {cidrCount} CIDR. SHA-256 проверен для {staged.Count} файлов.");
            }
            catch (Exception ex)
            {
                foreach (var item in staged)
                {
                    try
                    {
                        if (File.Exists(item.Temp))
                        {
                            File.Delete(item.Temp);
                        }
                        if (item.Backup != null && File.Exists(item.Backup))
                        {
                            File.Copy(item.Backup, item.Target, true);
                            File.Delete(item.Backup);
                        }
                        else if (item.Backup == null && File.Exists(item.Target))
                        {
                            File.Delete(item.Target);
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    var manifestTemp = manifestPath + ".sg-new";
                    if (File.Exists(manifestTemp))
                    {
                        File.Delete(manifestTemp);
                    }
                    if (manifestBackup != null && File.Exists(manifestBackup))
                    {
                        File.Copy(manifestBackup, manifestPath, true);
                        File.Delete(manifestBackup);
                    }
                    else if (manifestBackup == null && File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }
                }
                catch
                {
                }

                Logging.SaveLog("Update smart routing lists", ex);
                if (hadUsableRules && HasUsableRules())
                {
                    return (true, $"Обновление не выполнено: {ex.Message}. Используется предыдущий набор с проверенной локальной целостностью.");
                }
                return (false, $"Не удалось установить наборы маршрутизации: {ex.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<GeneratedRuleFile>> BuildWhiteListSnapshotAsync(HttpClient client, IProgress<string>? progress)
    {
        var generated = new List<GeneratedRuleFile>();
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task DownloadDomainCategoryAsync(string category)
        {
            category = category.Trim();
            if (!IsSafeCategoryName(category) || !visited.Add(category))
            {
                return;
            }

            var url = $"{WhiteBase}/data-geosite/{category}";
            var text = await client.GetStringAsync(url);
            var rawBytes = new UTF8Encoding(false).GetBytes(text);
            generated.Add(new GeneratedRuleFile(
                url,
                Path.Combine(WhiteRootRelativePath, "source-geosite", category),
                rawBytes,
                1));

            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var raw in lines)
            {
                var line = StripRuleComment(raw).Trim();
                if (line.StartsWith("include:", StringComparison.OrdinalIgnoreCase))
                {
                    var included = line["include:".Length..].Trim();
                    await DownloadDomainCategoryAsync(included);
                    continue;
                }

                foreach (var value in ParseWhiteDomains([line]))
                {
                    domains.Add(value);
                }
            }
        }

        await DownloadDomainCategoryAsync(WhiteDomainRootCategory);
        if (domains.Count < 400)
        {
            throw new InvalidDataException($"RU White List domain snapshot слишком мал: {domains.Count} записей.");
        }

        var cidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < WhiteIpCategories.Length; index++)
        {
            var category = WhiteIpCategories[index];
            progress?.Report($"RU White List IP {index + 1} из {WhiteIpCategories.Length}: {category}");
            var url = $"{WhiteBase}/data-geoip/{category}.txt";
            var text = await client.GetStringAsync(url);
            var rawBytes = new UTF8Encoding(false).GetBytes(text);
            generated.Add(new GeneratedRuleFile(
                url,
                Path.Combine(WhiteRootRelativePath, "source-geoip", $"{category}.txt"),
                rawBytes,
                1));

            foreach (var cidr in ParseWhiteIpCidrs(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                cidrs.Add(cidr);
            }
        }

        if (cidrs.Count < 50)
        {
            throw new InvalidDataException($"RU White List CIDR snapshot слишком мал: {cidrs.Count} записей.");
        }

        var domainText = BuildCombinedListText(
            "RU White List domains",
            $"{WhiteBase}/data-geosite/{WhiteDomainRootCategory}",
            domains.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        generated.Add(new GeneratedRuleFile(
            $"{WhiteBase}/data-geosite/{WhiteDomainRootCategory} (expanded)",
            WhiteDomainsRelativePath,
            new UTF8Encoding(false).GetBytes(domainText),
            1_000));

        var cidrText = BuildCombinedListText(
            "RU White List service CIDRs (geoip:ru intentionally excluded)",
            $"{WhiteBase}/data-geoip/ru-*.txt",
            cidrs.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        generated.Add(new GeneratedRuleFile(
            $"{WhiteBase}/data-geoip/ru-*.txt (granular categories only)",
            WhiteCidrsRelativePath,
            new UTF8Encoding(false).GetBytes(cidrText),
            500));

        return generated;
    }

    private static string BuildCombinedListText(string title, string source, IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine($"# Source: {source}");
        builder.AppendLine("# Generated by SG Client from ru-routing-dat source categories.");
        builder.AppendLine("# Do not add geoip:ru / data-geoip/ru.txt to this list.");
        foreach (var value in values)
        {
            builder.AppendLine(value);
        }
        return builder.ToString();
    }

    private static void StageBytes(
        List<(string Temp, string Target, string? Backup, RuleManifestEntry Entry)> staged,
        string url,
        string relativePath,
        byte[] bytes,
        long minimumBytes)
    {
        if (bytes.LongLength < minimumBytes)
        {
            throw new InvalidDataException($"Файл {Path.GetFileName(relativePath)} слишком мал и не прошёл проверку.");
        }

        var target = TargetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".sg-new";
        var backup = File.Exists(target) ? target + ".sg-backup" : null;
        var expectedHash = ComputeSha256(bytes);
        File.WriteAllBytes(temp, bytes);
        var writtenHash = ComputeFileSha256(temp);
        if (!string.Equals(expectedHash, writtenHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 файла {Path.GetFileName(relativePath)} изменился после записи.");
        }

        staged.Add((temp, target, backup, new RuleManifestEntry
        {
            RelativePath = NormalizePath(relativePath),
            Url = url,
            Size = bytes.LongLength,
            Sha256 = expectedHash,
        }));
    }

    private static bool ManifestEntryMatchesFile(RuleManifest manifest, string relativePath, long minimumBytes)
    {
        var entry = manifest.Files.FirstOrDefault(item =>
            string.Equals(NormalizePath(item.RelativePath), NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return false;
        }

        var path = TargetPath(relativePath);
        if (!File.Exists(path) || new FileInfo(path).Length < minimumBytes)
        {
            return false;
        }

        return string.Equals(ComputeFileSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripRuleComment(string? value)
    {
        var text = value ?? string.Empty;
        var comment = text.IndexOf('#');
        return comment >= 0 ? text[..comment] : text;
    }

    private static bool IsSafeCategoryName(string value)
    {
        return value.IsNotEmpty()
            && value.All(ch => (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch is '-' or '_');
    }

    private static RuleManifest? ReadManifest()
    {
        try
        {
            var path = TargetPath(ManifestFileName);
            return File.Exists(path)
                ? JsonUtils.Deserialize<RuleManifest>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string TargetPath(string relativePath)
    {
        return Path.Combine(Utils.GetBinPath(string.Empty), relativePath);
    }
}
