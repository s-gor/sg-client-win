using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceLib.Helper;

namespace v2rayN.Services;

public enum SgGeoSourceKind
{
    Bundled,
    Loyalsoldier,
    RunetFreedom,
    RoscomVpn,
    CustomUrls,
    LocalFiles,
}

public sealed record SgGeoSourceRequest(
    SgGeoSourceKind Kind,
    string? GeoIpValue,
    string? GeoSiteValue);

public sealed record SgGeoInstalledInfo(
    string Source,
    string GeoIpPath,
    long GeoIpSize,
    DateTime GeoIpDate,
    string GeoIpSha256,
    string GeoSitePath,
    long GeoSiteSize,
    DateTime GeoSiteDate,
    string GeoSiteSha256,
    string LastValidation,
    string Family,
    IReadOnlyList<string> GeoIpCategories,
    IReadOnlyList<string> GeoSiteCategories);

public sealed record SgGeoApplyOptions(
    bool ApplyRoscomPreset,
    bool BlockAds,
    bool BlockWindowsTelemetry,
    bool BlockTorrentCategory,
    bool ApplySafeGlobalPreset);

public sealed record SgGeoAnalysis(
    string Family,
    IReadOnlyList<string> GeoIpCategories,
    IReadOnlyList<string> GeoSiteCategories,
    IReadOnlyList<string> MissingActiveCategories);

public sealed class SgGeoCandidate : IDisposable
{
    public required string WorkDirectory { get; init; }
    public required string GeoIpPath { get; init; }
    public required string GeoSitePath { get; init; }
    public required long GeoIpSize { get; init; }
    public required DateTime GeoIpDate { get; init; }
    public required string GeoIpSha256 { get; init; }
    public required long GeoSiteSize { get; init; }
    public required DateTime GeoSiteDate { get; init; }
    public required string GeoSiteSha256 { get; init; }
    public required string SourceDescription { get; init; }
    public required SgGeoSourceKind SourceKind { get; init; }
    public required string Family { get; init; }
    public required IReadOnlyList<string> GeoIpCategories { get; init; }
    public required IReadOnlyList<string> GeoSiteCategories { get; init; }
    public required IReadOnlyList<string> MissingActiveCategories { get; init; }
    public string? GeoIpSource { get; init; }
    public string? GeoSiteSource { get; init; }

    public bool IsRoscomVpn =>
        string.Equals(Family, SgGeoFilesService.RoscomFamily, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkDirectory))
            {
                Directory.Delete(WorkDirectory, true);
            }
        }
        catch
        {
        }
    }
}

public sealed record SgGeoApplyResult(bool Success, string Message);

public sealed class SgGeoFilesService
{
    public const string StandardFamily = "Стандартный";
    public const string RoscomFamily = "RoscomVPN";
    public const string CustomFamily = "Пользовательский";

    private const long MinimumGeoFileSize = 4 * 1024;
    private const string RoscomGeoIpUrl =
        "https://github.com/hydraponique/roscomvpn-geoip/releases/latest/download/geoip.dat";
    private const string RoscomGeoSiteUrl =
        "https://github.com/hydraponique/roscomvpn-geosite/releases/latest/download/geosite.dat";

    private readonly Config _config = AppManager.Instance.Config;

    private static string GeoIpPath => Utils.GetBinPath("geoip.dat");
    private static string GeoSitePath => Utils.GetBinPath("geosite.dat");
    private static string MetadataPath => Utils.GetConfigPath("sg-geofiles-source.json");
    private static string BackupRoot => Utils.GetBackupPath("geofiles");
    private static string BundledRoot => Path.Combine(BackupRoot, "bundled");

    public async Task EnsureBundledBaselineAsync()
    {
        Directory.CreateDirectory(BundledRoot);
        var bundledGeoIp = Path.Combine(BundledRoot, "geoip.dat");
        var bundledGeoSite = Path.Combine(BundledRoot, "geosite.dat");

        if (!File.Exists(bundledGeoIp) && File.Exists(GeoIpPath))
        {
            File.Copy(GeoIpPath, bundledGeoIp, false);
        }

        if (!File.Exists(bundledGeoSite) && File.Exists(GeoSitePath))
        {
            File.Copy(GeoSitePath, bundledGeoSite, false);
        }

        if (File.Exists(bundledGeoIp) && File.Exists(bundledGeoSite))
        {
            await ValidatePairAsync(bundledGeoIp, bundledGeoSite);
        }
    }

    public async Task<SgGeoInstalledInfo> GetInstalledInfoAsync()
    {
        await EnsureBundledBaselineAsync();
        if (!File.Exists(GeoIpPath) || !File.Exists(GeoSitePath))
        {
            throw new FileNotFoundException("geoip.dat или geosite.dat отсутствует в папке bin.");
        }

        var metadata = await ReadMetadataAsync();
        var analysis = await AnalyzePairAsync(GeoIpPath, GeoSitePath);
        return new SgGeoInstalledInfo(
            metadata?.SourceDescription ?? DetectCurrentSource(),
            GeoIpPath,
            new FileInfo(GeoIpPath).Length,
            File.GetLastWriteTime(GeoIpPath),
            await ComputeSha256Async(GeoIpPath),
            GeoSitePath,
            new FileInfo(GeoSitePath).Length,
            File.GetLastWriteTime(GeoSitePath),
            await ComputeSha256Async(GeoSitePath),
            metadata?.LastValidation ?? "Не проверялось SG Client",
            analysis.Family,
            analysis.GeoIpCategories,
            analysis.GeoSiteCategories);
    }

    public async Task<SgGeoCandidate> PrepareAsync(
        SgGeoSourceRequest request,
        Action<string>? report = null)
    {
        await EnsureBundledBaselineAsync();
        var workDirectory = Utils.GetTempPath($"sg-geofiles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        var geoIpTarget = Path.Combine(workDirectory, "geoip.dat");
        var geoSiteTarget = Path.Combine(workDirectory, "geosite.dat");

        try
        {
            var (geoIpSource, geoSiteSource, description) = ResolveSource(request);

            report?.Invoke("Получение geoip.dat…");
            await CopyOrDownloadAsync(request.Kind, geoIpSource, geoIpTarget);

            report?.Invoke("Получение geosite.dat…");
            await CopyOrDownloadAsync(request.Kind, geoSiteSource, geoSiteTarget);

            ValidateFileSize(geoIpTarget, "geoip.dat");
            ValidateFileSize(geoSiteTarget, "geosite.dat");

            report?.Invoke("Расчёт SHA-256…");
            var geoIpSha = await ComputeSha256Async(geoIpTarget);
            var geoSiteSha = await ComputeSha256Async(geoSiteTarget);

            report?.Invoke("Чтение категорий и проверка файлов установленным Xray…");
            var analysis = await ValidatePairAsync(geoIpTarget, geoSiteTarget);

            return new SgGeoCandidate
            {
                WorkDirectory = workDirectory,
                GeoIpPath = geoIpTarget,
                GeoSitePath = geoSiteTarget,
                GeoIpSize = new FileInfo(geoIpTarget).Length,
                GeoIpDate = File.GetLastWriteTime(geoIpTarget),
                GeoIpSha256 = geoIpSha,
                GeoSiteSize = new FileInfo(geoSiteTarget).Length,
                GeoSiteDate = File.GetLastWriteTime(geoSiteTarget),
                GeoSiteSha256 = geoSiteSha,
                SourceDescription = description,
                SourceKind = request.Kind,
                Family = analysis.Family,
                GeoIpCategories = analysis.GeoIpCategories,
                GeoSiteCategories = analysis.GeoSiteCategories,
                MissingActiveCategories = analysis.MissingActiveCategories,
                GeoIpSource = geoIpSource,
                GeoSiteSource = geoSiteSource,
            };
        }
        catch
        {
            try
            {
                Directory.Delete(workDirectory, true);
            }
            catch
            {
            }

            throw;
        }
    }

    public async Task<SgGeoApplyResult> ApplyAsync(
        SgGeoCandidate candidate,
        SgGeoApplyOptions options,
        Action<string>? report = null)
    {
        if (!File.Exists(candidate.GeoIpPath) || !File.Exists(candidate.GeoSitePath))
        {
            return new SgGeoApplyResult(false, "Подготовленные GeoFiles больше недоступны.");
        }

        if (!string.Equals(
                await ComputeSha256Async(candidate.GeoIpPath),
                candidate.GeoIpSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                await ComputeSha256Async(candidate.GeoSitePath),
                candidate.GeoSiteSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SgGeoApplyResult(false, "Подготовленные GeoFiles изменились после проверки.");
        }

        if (candidate.MissingActiveCategories.Count > 0
            && !(candidate.IsRoscomVpn && options.ApplyRoscomPreset)
            && !options.ApplySafeGlobalPreset)
        {
            return new SgGeoApplyResult(
                false,
                "Текущая маршрутизация несовместима с выбранными GeoFiles. Не найдены категории: "
                + string.Join(", ", candidate.MissingActiveCategories)
                + ". Выберите другой источник или примените предложенный совместимый пресет.");
        }

        var previousMode = StatusBarViewModel.Instance.GetConnectionModeKey();
        var wasRunning = CoreManager.Instance.IsCoreRunning;
        var previousSmartRouting = JsonUtils.DeepCopy(_config.SgQuickSettingsItem.SmartRouting);
        var previousRoutingMode = _config.SgQuickSettingsItem.RoutingMode;
        var previousGeoSource = _config.ConstItem.GeoSourceUrl;
        var previousBaseRouting = await ConfigHandler.GetDefaultRouting(_config);

        var backupDirectory = Path.Combine(
            BackupRoot,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backupDirectory);
        var backupGeoIp = Path.Combine(backupDirectory, "geoip.dat");
        var backupGeoSite = Path.Combine(backupDirectory, "geosite.dat");
        var stopped = false;

        try
        {
            report?.Invoke("Остановка активного подключения…");
            await StatusBarViewModel.Instance.StopConnectionForMaintenanceAsync();
            stopped = true;

            report?.Invoke("Создание резервной копии текущих GeoFiles…");
            if (File.Exists(GeoIpPath))
            {
                File.Copy(GeoIpPath, backupGeoIp, false);
            }

            if (File.Exists(GeoSitePath))
            {
                File.Copy(GeoSitePath, backupGeoSite, false);
            }

            report?.Invoke("Атомарная замена GeoFiles…");
            await ReplaceFileAsync(candidate.GeoIpPath, GeoIpPath);
            await ReplaceFileAsync(candidate.GeoSitePath, GeoSitePath);

            if (candidate.IsRoscomVpn && options.ApplyRoscomPreset)
            {
                report?.Invoke("Применение совместимого пресета RoscomVPN…");
                await ApplyRoscomRoutingPresetAsync(options);
            }
            else if (options.ApplySafeGlobalPreset)
            {
                report?.Invoke("Удаление несовместимых Geo-категорий из маршрутизации…");
                await ApplySafeGlobalRoutingPresetAsync(candidate);
            }

            report?.Invoke("Контрольная проверка установленных файлов…");
            var installedAnalysis = await ValidatePairAsync(GeoIpPath, GeoSitePath);
            if (installedAnalysis.MissingActiveCategories.Count > 0)
            {
                throw new InvalidDataException(
                    "После замены текущей маршрутизации не хватает категорий: "
                    + string.Join(", ", installedAnalysis.MissingActiveCategories));
            }

            _config.ConstItem.GeoSourceUrl = candidate.SourceKind switch
            {
                SgGeoSourceKind.Loyalsoldier => Global.GeoUrl,
                SgGeoSourceKind.RunetFreedom => Global.GeoFilesSources[1],
                _ => string.Empty,
            };

            await ConfigHandler.SaveConfig(_config);
            await WriteMetadataAsync(new SgGeoMetadata
            {
                SourceDescription = candidate.SourceDescription,
                SourceKind = candidate.SourceKind.ToString(),
                Family = candidate.Family,
                GeoIpSource = candidate.GeoIpSource,
                GeoSiteSource = candidate.GeoSiteSource,
                GeoIpSha256 = candidate.GeoIpSha256,
                GeoSiteSha256 = candidate.GeoSiteSha256,
                GeoIpCategories = candidate.GeoIpCategories.ToList(),
                GeoSiteCategories = candidate.GeoSiteCategories.ToList(),
                AppliedAt = DateTimeOffset.Now,
                LastValidation = $"Успешно · {DateTime.Now:dd.MM.yyyy HH:mm}",
                BackupDirectory = backupDirectory,
            });

            var presetMessage = candidate.IsRoscomVpn && options.ApplyRoscomPreset
                ? " Совместимый пресет RoscomVPN применён."
                : options.ApplySafeGlobalPreset
                    ? " Несовместимые Geo-категории удалены; базовый выход оставлен VPN."
                    : string.Empty;

            return new SgGeoApplyResult(
                true,
                $"GeoFiles обновлены. Источник: {candidate.SourceDescription}.{presetMessage}");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgGeoFilesService.Apply", ex);
            try
            {
                report?.Invoke("Ошибка. Возврат предыдущих GeoFiles и маршрутизации…");

                if (File.Exists(backupGeoIp))
                {
                    await ReplaceFileAsync(backupGeoIp, GeoIpPath);
                }

                if (File.Exists(backupGeoSite))
                {
                    await ReplaceFileAsync(backupGeoSite, GeoSitePath);
                }

                _config.SgQuickSettingsItem.SmartRouting =
                    previousSmartRouting ?? new SgSmartRoutingItem();
                _config.SgQuickSettingsItem.RoutingMode = previousRoutingMode;
                _config.ConstItem.GeoSourceUrl = previousGeoSource;
                if (previousBaseRouting != null)
                {
                    await ConfigHandler.SetDefaultRouting(_config, previousBaseRouting);
                }
                await ConfigHandler.SaveConfig(_config);
            }
            catch (Exception rollbackError)
            {
                Logging.SaveLog("SgGeoFilesService.Rollback", rollbackError);
                return new SgGeoApplyResult(
                    false,
                    "Замена не выполнена, автоматический откат также завершился ошибкой. "
                    + rollbackError.Message);
            }

            return new SgGeoApplyResult(
                false,
                "GeoFiles не установлены. Предыдущие файлы и маршрутизация восстановлены. "
                + ex.Message);
        }
        finally
        {
            if (stopped && wasRunning)
            {
                try
                {
                    report?.Invoke("Восстановление прежнего режима подключения…");
                    await StatusBarViewModel.Instance.RestoreConnectionAfterMaintenanceAsync(previousMode);
                }
                catch (Exception restoreError)
                {
                    Logging.SaveLog("SgGeoFilesService.RestoreConnection", restoreError);
                }
            }
        }
    }

    public async Task<SgGeoCandidate> PrepareBundledAsync(Action<string>? report = null)
    {
        return await PrepareAsync(
            new SgGeoSourceRequest(SgGeoSourceKind.Bundled, null, null),
            report);
    }

    private static (string GeoIp, string GeoSite, string Description) ResolveSource(
        SgGeoSourceRequest request)
    {
        return request.Kind switch
        {
            SgGeoSourceKind.Bundled => (
                Path.Combine(BundledRoot, "geoip.dat"),
                Path.Combine(BundledRoot, "geosite.dat"),
                "Комплект SG Client"),
            SgGeoSourceKind.Loyalsoldier => (
                string.Format(Global.GeoUrl, "geoip"),
                string.Format(Global.GeoUrl, "geosite"),
                "Loyalsoldier"),
            SgGeoSourceKind.RunetFreedom => (
                string.Format(Global.GeoFilesSources[1], "geoip"),
                string.Format(Global.GeoFilesSources[1], "geosite"),
                "RunetFreedom"),
            SgGeoSourceKind.RoscomVpn => (
                RoscomGeoIpUrl,
                RoscomGeoSiteUrl,
                "RoscomVPN"),
            SgGeoSourceKind.CustomUrls => (
                RequireValue(request.GeoIpValue, "URL geoip.dat"),
                RequireValue(request.GeoSiteValue, "URL geosite.dat"),
                "Пользовательские URL"),
            SgGeoSourceKind.LocalFiles => (
                RequireValue(request.GeoIpValue, "локальный geoip.dat"),
                RequireValue(request.GeoSiteValue, "локальный geosite.dat"),
                "Локальные файлы"),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Kind)),
        };
    }

    private static string RequireValue(string? value, string title)
    {
        if (value.IsNullOrEmpty())
        {
            throw new InvalidOperationException($"Не указан {title}.");
        }

        return value!.Trim();
    }

    private static async Task CopyOrDownloadAsync(
        SgGeoSourceKind kind,
        string source,
        string target)
    {
        if (kind is SgGeoSourceKind.Bundled or SgGeoSourceKind.LocalFiles)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Файл не найден.", source);
            }

            File.Copy(source, target, true);
            return;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException($"Некорректный URL: {source}");
        }

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(4),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SG-Client/073");

        using var response = await client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            target,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await input.CopyToAsync(output);
    }

    private static void ValidateFileSize(string path, string name)
    {
        var size = new FileInfo(path).Length;
        if (size < MinimumGeoFileSize)
        {
            throw new InvalidDataException(
                $"{name} слишком мал ({size} байт) и не похож на рабочий GeoFile.");
        }
    }

    private async Task<SgGeoAnalysis> AnalyzePairAsync(string geoIp, string geoSite)
    {
        ValidateFileSize(geoIp, "geoip.dat");
        ValidateFileSize(geoSite, "geosite.dat");

        var geoIpCategories = await ReadCategoryCodesAsync(geoIp);
        var geoSiteCategories = await ReadCategoryCodesAsync(geoSite);

        if (geoIpCategories.Count == 0)
        {
            throw new InvalidDataException("В geoip.dat не найдено ни одной категории.");
        }

        if (geoSiteCategories.Count == 0)
        {
            throw new InvalidDataException("В geosite.dat не найдено ни одной категории.");
        }

        var family = DetectFamily(geoIpCategories, geoSiteCategories);
        var missing = await FindMissingActiveCategoriesAsync(
            geoIpCategories,
            geoSiteCategories);

        return new SgGeoAnalysis(
            family,
            geoIpCategories,
            geoSiteCategories,
            missing);
    }

    private async Task<SgGeoAnalysis> ValidatePairAsync(string geoIp, string geoSite)
    {
        var analysis = await AnalyzePairAsync(geoIp, geoSite);

        var directory = Path.GetDirectoryName(geoIp)
            ?? throw new InvalidOperationException("Не определена папка GeoFiles.");

        if (!string.Equals(
                directory,
                Path.GetDirectoryName(geoSite),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "geoip.dat и geosite.dat должны находиться в одной временной папке.");
        }

        var xray = Utils.GetBinPath("xray.exe", "xray");
        if (!File.Exists(xray))
        {
            throw new FileNotFoundException(
                "Для проверки GeoFiles не найден xray.exe.",
                xray);
        }

        var geoSiteCategory = SelectValidationCategory(
            analysis.GeoSiteCategories,
            "category-ads-all",
            "category-ads",
            "private",
            "whitelist",
            "category-ru");

        var geoIpCategory = SelectValidationCategory(
            analysis.GeoIpCategories,
            "private",
            "ru",
            "direct",
            "whitelist");

        var rules = new List<object>();
        if (geoSiteCategory.IsNotEmpty())
        {
            rules.Add(new
            {
                type = "field",
                domain = new[] { $"geosite:{geoSiteCategory}" },
                outboundTag = "direct",
            });
        }

        if (geoIpCategory.IsNotEmpty())
        {
            rules.Add(new
            {
                type = "field",
                ip = new[] { $"geoip:{geoIpCategory}" },
                outboundTag = "direct",
            });
        }

        var configPath = Path.Combine(directory, "sg-geofiles-test.json");
        var config = new
        {
            log = new { loglevel = "warning" },
            inbounds = new[]
            {
                new
                {
                    listen = "127.0.0.1",
                    port = 10888,
                    protocol = "socks",
                    settings = new { auth = "noauth", udp = true },
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

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config),
            new UTF8Encoding(false));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = xray,
                WorkingDirectory = directory,
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
            startInfo.Environment["XRAY_LOCATION_ASSET"] = directory;

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

                throw new TimeoutException(
                    "Проверка GeoFiles ядром Xray превысила 30 секунд.");
            }

            var output = string.Join(
                Environment.NewLine,
                new[] { await stdout, await stderr }
                    .Where(value => value.IsNotEmpty())
                    .Select(value => value.Trim()));

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    output.IsNotEmpty()
                        ? output
                        : $"Xray завершился с кодом {process.ExitCode}.");
            }

            return analysis;
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

    private static string SelectValidationCategory(
        IReadOnlyList<string> categories,
        params string[] preferred)
    {
        foreach (var item in preferred)
        {
            var match = categories.FirstOrDefault(value =>
                string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
            if (match.IsNotEmpty())
            {
                return match!;
            }
        }

        return categories.FirstOrDefault() ?? string.Empty;
    }

    private static async Task<IReadOnlyList<string>> ReadCategoryCodesAsync(string path)
    {
        var data = await File.ReadAllBytesAsync(path);
        return await Task.Run(() => ParseTopLevelCategoryCodes(data));
    }

    private static IReadOnlyList<string> ParseTopLevelCategoryCodes(byte[] data)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        while (offset < data.Length
               && TryReadVarint(data, ref offset, data.Length, out var key))
        {
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);

            if (field == 1
                && wire == 2
                && TryReadLength(data, ref offset, data.Length, out var start, out var length))
            {
                var code = ReadStringField(data, start, length, 1)
                    .Trim()
                    .ToLowerInvariant();

                if (Regex.IsMatch(
                        code,
                        "^[a-z0-9][a-z0-9_-]{0,127}$",
                        RegexOptions.CultureInvariant))
                {
                    result.Add(code);
                }

                offset = start + length;
            }
            else if (!SkipField(data, ref offset, data.Length, wire))
            {
                break;
            }
        }

        return result
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadStringField(
        byte[] data,
        int start,
        int length,
        int requestedField)
    {
        var end = start + length;
        var offset = start;

        while (offset < end
               && TryReadVarint(data, ref offset, end, out var key))
        {
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);

            if (field == requestedField
                && wire == 2
                && TryReadLength(
                    data,
                    ref offset,
                    end,
                    out var valueStart,
                    out var valueLength))
            {
                return Encoding.UTF8.GetString(data, valueStart, valueLength);
            }

            if (!SkipField(data, ref offset, end, wire))
            {
                break;
            }
        }

        return string.Empty;
    }

    private static bool TryReadLength(
        byte[] data,
        ref int offset,
        int limit,
        out int start,
        out int length)
    {
        start = 0;
        length = 0;

        if (!TryReadVarint(data, ref offset, limit, out var rawLength)
            || rawLength > int.MaxValue)
        {
            return false;
        }

        length = (int)rawLength;
        start = offset;
        return length >= 0 && start >= 0 && start + length <= limit;
    }

    private static bool TryReadVarint(
        byte[] data,
        ref int offset,
        int limit,
        out ulong value)
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

    private static bool SkipField(
        byte[] data,
        ref int offset,
        int limit,
        int wire)
    {
        switch (wire)
        {
            case 0:
                return TryReadVarint(data, ref offset, limit, out _);
            case 1:
                offset += 8;
                return offset <= limit;
            case 2:
                if (!TryReadVarint(data, ref offset, limit, out var rawLength)
                    || rawLength > int.MaxValue)
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

    private static string DetectFamily(
        IReadOnlyList<string> geoIpCategories,
        IReadOnlyList<string> geoSiteCategories)
    {
        var ip = geoIpCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var site = geoSiteCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ip.IsSupersetOf(new[] { "direct", "private", "whitelist" })
            && site.IsSupersetOf(
                new[] { "category-ru", "category-ads", "private", "whitelist" }))
        {
            return RoscomFamily;
        }

        if (ip.Contains("private")
            && (ip.Contains("ru") || ip.Contains("cn"))
            && site.Contains("category-ads-all"))
        {
            return StandardFamily;
        }

        return CustomFamily;
    }

    private async Task<IReadOnlyList<string>> FindMissingActiveCategoriesAsync(
        IReadOnlyList<string> geoIpCategories,
        IReadOnlyList<string> geoSiteCategories)
    {
        var ip = geoIpCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var site = geoSiteCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredCategories = GetActiveSmartRoutingCategories().ToList();
        requiredCategories.AddRange(await GetActiveBaseRoutingCategoriesAsync());
        var missing = new List<string>();

        foreach (var value in requiredCategories)
        {
            if (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
            {
                if (!ip.Contains(value.Substring("geoip:".Length)))
                {
                    missing.Add(value);
                }
            }
            else if (value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)
                     && !site.Contains(value.Substring("geosite:".Length)))
            {
                missing.Add(value);
            }
        }

        return missing
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> GetActiveSmartRoutingCategories()
    {
        var item = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        var requiredCategories = new List<string>();

        if (item.AdsAction != item.DefaultAction)
        {
            requiredCategories.Add("geosite:category-ads-all");
        }

        if (item.BlockedAction != item.DefaultAction)
        {
            requiredCategories.Add("geosite:ru-blocked");
            requiredCategories.Add("geoip:ru-blocked");
        }

        requiredCategories.AddRange(SgSmartRoutingHelper.GetRussiaDomainRules(item));
        requiredCategories.AddRange(SgSmartRoutingHelper.GetRussiaIpRules(item));

        foreach (var value in item.CustomDirectDomains
                     .Concat(item.CustomProxyDomains)
                     .Concat(item.CustomBlockDomains))
        {
            if (value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
            {
                requiredCategories.Add(value);
            }
        }

        foreach (var value in item.CustomDirectIps
                     .Concat(item.CustomProxyIps)
                     .Concat(item.CustomBlockIps))
        {
            if (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
            {
                requiredCategories.Add(value);
            }
        }

        return requiredCategories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetActiveBaseRoutingCategoriesAsync()
    {
        try
        {
            var routing = await ConfigHandler.GetDefaultRouting(_config);
            if (routing == null || routing.RuleSet.IsNullOrEmpty())
            {
                return [];
            }

            var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
            return rules
                .Where(rule => rule.Enabled)
                .SelectMany(rule =>
                    (rule.Domain ?? [])
                    .Concat(rule.Ip ?? []))
                .Where(value =>
                    value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Read active base routing categories", ex);
            return [];
        }
    }

    private async Task ApplyRoscomRoutingPresetAsync(SgGeoApplyOptions options)
    {
        var item = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);

        item.Preset = SgSmartRoutingHelper.PresetCustom;
        item.LocalNetworkAction = SgSmartRoutingHelper.ActionDirect;
        item.RussiaScopeMigrated = true;
        item.RussiaScope = SgSmartRoutingHelper.RussiaScopeNone;
        item.RussiaAction = SgSmartRoutingHelper.ActionProxy;
        item.BlockedAction = SgSmartRoutingHelper.ActionProxy;
        item.AdsAction = SgSmartRoutingHelper.ActionProxy;
        item.DefaultAction = SgSmartRoutingHelper.ActionProxy;

        item.CustomDirectDomains = PreservePortableDomainEntries(item.CustomDirectDomains);
        item.CustomProxyDomains = PreservePortableDomainEntries(item.CustomProxyDomains);
        item.CustomBlockDomains = PreservePortableDomainEntries(item.CustomBlockDomains);
        item.CustomDirectIps = PreservePortableIpEntries(item.CustomDirectIps);
        item.CustomProxyIps = PreservePortableIpEntries(item.CustomProxyIps);
        item.CustomBlockIps = PreservePortableIpEntries(item.CustomBlockIps);

        AddDistinct(
            item.CustomDirectDomains,
            "geosite:private",
            "geosite:whitelist",
            "geosite:category-ru",
            "geosite:apple",
            "geosite:microsoft",
            "geosite:steam",
            "geosite:epicgames",
            "geosite:riot",
            "geosite:escapefromtarkov",
            "geosite:faceit",
            "geosite:pinterest");

        AddDistinct(
            item.CustomDirectIps,
            "geoip:private",
            "geoip:whitelist",
            "geoip:direct");

        if (options.BlockAds)
        {
            AddDistinct(item.CustomBlockDomains, "geosite:category-ads");
        }

        if (options.BlockWindowsTelemetry)
        {
            AddDistinct(item.CustomBlockDomains, "geosite:win-spy");
        }

        if (options.BlockTorrentCategory)
        {
            AddDistinct(item.CustomBlockDomains, "geosite:torrent");
        }

        _config.SgQuickSettingsItem.RoutingMode =
            SgSmartRoutingHelper.PresetCustom;

        // Built-in Whitelist/Blacklist profiles contain standard categories
        // such as geosite:google which are absent from RoscomVPN. Preserve the
        // user's profiles, but activate an empty compatible base so the SG
        // Smart Routing rules below are not shadowed by a catch-all rule.
        await ActivateCompatibleBaseRoutingAsync();
    }

    private async Task ApplySafeGlobalRoutingPresetAsync(SgGeoCandidate candidate)
    {
        var item = SgSmartRoutingHelper.Normalize(_config.SgQuickSettingsItem);
        var geoSiteCategories = candidate.GeoSiteCategories
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var geoIpCategories = candidate.GeoIpCategories
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        item.Preset = SgSmartRoutingHelper.PresetCustom;
        item.LocalNetworkAction = SgSmartRoutingHelper.ActionDirect;
        item.RussiaScopeMigrated = true;
        item.RussiaScope = SgSmartRoutingHelper.RussiaScopeNone;
        item.RussiaAction = SgSmartRoutingHelper.ActionProxy;
        item.BlockedAction = SgSmartRoutingHelper.ActionProxy;
        item.AdsAction = SgSmartRoutingHelper.ActionProxy;
        item.DefaultAction = SgSmartRoutingHelper.ActionProxy;

        item.CustomDirectDomains = PreserveCompatibleDomainEntries(
            item.CustomDirectDomains,
            geoSiteCategories);
        item.CustomProxyDomains = PreserveCompatibleDomainEntries(
            item.CustomProxyDomains,
            geoSiteCategories);
        item.CustomBlockDomains = PreserveCompatibleDomainEntries(
            item.CustomBlockDomains,
            geoSiteCategories);
        item.CustomDirectIps = PreserveCompatibleIpEntries(
            item.CustomDirectIps,
            geoIpCategories);
        item.CustomProxyIps = PreserveCompatibleIpEntries(
            item.CustomProxyIps,
            geoIpCategories);
        item.CustomBlockIps = PreserveCompatibleIpEntries(
            item.CustomBlockIps,
            geoIpCategories);

        _config.SgQuickSettingsItem.RoutingMode = SgSmartRoutingHelper.PresetCustom;
        await ActivateCompatibleBaseRoutingAsync();
    }

    private async Task ActivateCompatibleBaseRoutingAsync()
    {
        const string remarks = "SG Client · GeoFiles compatible";
        var items = await AppManager.Instance.RoutingItems() ?? [];
        var compatibleRouting = items.FirstOrDefault(item =>
            string.Equals(item.Remarks, remarks, StringComparison.OrdinalIgnoreCase));

        if (compatibleRouting == null)
        {
            compatibleRouting = new RoutingItem
            {
                Remarks = remarks,
                Url = string.Empty,
                Sort = items.Count + 1,
                RuleSet = "[]",
                RuleNum = 0,
            };
        }
        else
        {
            // Keep this base intentionally empty. SG Smart Routing is appended
            // afterwards and must remain the only policy source; a catch-all
            // rule here would shadow every RoscomVPN rule.
            compatibleRouting.RuleSet = "[]";
            compatibleRouting.RuleNum = 0;
        }

        if (await ConfigHandler.SaveRoutingItem(_config, compatibleRouting) != 0)
        {
            throw new InvalidOperationException(
                "Не удалось создать совместимую базовую маршрутизацию GeoFiles.");
        }

        await ConfigHandler.SetDefaultRouting(_config, compatibleRouting);
    }

    private static List<string> PreserveCompatibleDomainEntries(
        IEnumerable<string>? values,
        IReadOnlySet<string> availableCategories)
    {
        return (values ?? [])
            .Where(value =>
                value.IsNotEmpty()
                && (!value.StartsWith(
                        "geosite:",
                        StringComparison.OrdinalIgnoreCase)
                    || availableCategories.Contains(
                        value.Substring("geosite:".Length))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> PreserveCompatibleIpEntries(
        IEnumerable<string>? values,
        IReadOnlySet<string> availableCategories)
    {
        return (values ?? [])
            .Where(value =>
                value.IsNotEmpty()
                && (!value.StartsWith(
                        "geoip:",
                        StringComparison.OrdinalIgnoreCase)
                    || availableCategories.Contains(
                        value.Substring("geoip:".Length))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> PreservePortableDomainEntries(
        IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(value =>
                value.IsNotEmpty()
                && !value.StartsWith(
                    "geosite:",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> PreservePortableIpEntries(
        IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(value =>
                value.IsNotEmpty()
                && !value.StartsWith(
                    "geoip:",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddDistinct(List<string> target, params string[] values)
    {
        foreach (var value in values)
        {
            if (!target.Any(existing =>
                    string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(value);
            }
        }
    }

    private static async Task ReplaceFileAsync(string source, string target)
    {
        var staged = target + ".sg-new";
        File.Copy(source, staged, true);

        await using (var stream = new FileStream(
            staged,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            await stream.FlushAsync();
        }

        File.Move(staged, target, true);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }

    private string DetectCurrentSource()
    {
        if (string.Equals(
                _config.ConstItem.GeoSourceUrl,
                Global.GeoUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Loyalsoldier";
        }

        if (Global.GeoFilesSources.Count > 1
            && string.Equals(
                _config.ConstItem.GeoSourceUrl,
                Global.GeoFilesSources[1],
                StringComparison.OrdinalIgnoreCase))
        {
            return "RunetFreedom";
        }

        return "Комплект SG Client или локальная замена";
    }

    private static async Task<SgGeoMetadata?> ReadMetadataAsync()
    {
        try
        {
            if (!File.Exists(MetadataPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SgGeoMetadata>(
                await File.ReadAllTextAsync(MetadataPath));
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteMetadataAsync(SgGeoMetadata metadata)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);

        await File.WriteAllTextAsync(
            MetadataPath,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private sealed class SgGeoMetadata
    {
        public string SourceDescription { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string? GeoIpSource { get; set; }
        public string? GeoSiteSource { get; set; }
        public string GeoIpSha256 { get; set; } = string.Empty;
        public string GeoSiteSha256 { get; set; } = string.Empty;
        public List<string> GeoIpCategories { get; set; } = [];
        public List<string> GeoSiteCategories { get; set; } = [];
        public DateTimeOffset AppliedAt { get; set; }
        public string LastValidation { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
    }
}
