namespace ServiceLib.Manager;

public sealed class SgRussiaRulesManager
{
    private static readonly Lazy<SgRussiaRulesManager> _instance = new(() => new SgRussiaRulesManager());
    public static SgRussiaRulesManager Instance => _instance.Value;

    private const string GeoBase = "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release";
    private const string ManifestFileName = "sg-routing-rules-manifest.json";
    private static readonly TimeSpan RefreshAge = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record RuleFileSpec(string Url, string RelativePath, long MinimumBytes);

    private sealed class RuleManifest
    {
        public string Source { get; set; } = GeoBase;
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

    private static readonly RuleFileSpec[] Files =
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
            return Files.All(spec =>
            {
                var entry = manifest.Files.FirstOrDefault(item =>
                    string.Equals(NormalizePath(item.RelativePath), NormalizePath(spec.RelativePath), StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return false;
                }

                var path = TargetPath(spec.RelativePath);
                if (!File.Exists(path) || new FileInfo(path).Length < spec.MinimumBytes)
                {
                    return false;
                }

                return string.Equals(ComputeFileSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Accept files from older SG Client releases, but the next update will create a verified manifest.
        return Files.All(item =>
        {
            var path = TargetPath(item.RelativePath);
            return File.Exists(path) && new FileInfo(path).Length >= item.MinimumBytes;
        });
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

        domains = domains.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ips = ips.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (domains.Count == 0 && ips.Count == 0)
        {
            return (true, "Дополнительные категории GeoFiles не требуются.");
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
            var staged = new List<(RuleFileSpec Spec, string Temp, string Target, string? Backup, RuleManifestEntry Entry)>();
            var manifestPath = TargetPath(ManifestFileName);
            var manifestBackup = File.Exists(manifestPath) ? manifestPath + ".sg-backup" : null;
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(3)
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SG-Client/069");

                for (var index = 0; index < Files.Length; index++)
                {
                    var spec = Files[index];
                    progress?.Report($"Загрузка {index + 1} из {Files.Length}: {Path.GetFileName(spec.RelativePath)}");
                    var target = TargetPath(spec.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temp = target + ".sg-new";
                    var backup = File.Exists(target) ? target + ".sg-backup" : null;

                    var bytes = await client.GetByteArrayAsync(spec.Url);
                    if (bytes.LongLength < spec.MinimumBytes)
                    {
                        throw new InvalidDataException($"Файл {Path.GetFileName(spec.RelativePath)} слишком мал и не прошёл проверку.");
                    }

                    var expectedHash = ComputeSha256(bytes);
                    await File.WriteAllBytesAsync(temp, bytes);
                    var writtenHash = ComputeFileSha256(temp);
                    if (!string.Equals(expectedHash, writtenHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SHA-256 файла {Path.GetFileName(spec.RelativePath)} изменился после записи.");
                    }

                    staged.Add((spec, temp, target, backup, new RuleManifestEntry
                    {
                        RelativePath = NormalizePath(spec.RelativePath),
                        Url = spec.Url,
                        Size = bytes.LongLength,
                        Sha256 = expectedHash,
                    }));
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
                return (true, $"Наборы маршрутизации обновлены. Локальная целостность SHA-256 проверена для {staged.Count} файлов.");
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
