using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceLib.Handler.Builder;

namespace v2rayN.Services;

public sealed class SgXrayUpdateService
{
    private readonly Config _config;

    public SgXrayUpdateService(Config config)
    {
        _config = config;
    }

    public Task<SgXrayCandidate> PrepareOfficialAsync(Action<string> report)
    {
        return PrepareOfficialAsync(
            SgXrayReleaseChannel.Stable,
            null,
            SgXrayNetworkMode.CurrentRoute,
            report);
    }

    public async Task<SgXrayCandidate> PrepareOfficialAsync(
        SgXrayReleaseChannel channel,
        string? exactVersion,
        SgXrayNetworkMode networkMode,
        Action<string> report)
    {
        var workDirectory = CreateWorkDirectory();
        try
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray)
                ?? throw new InvalidOperationException("Описание ядра Xray не найдено.");
            var apiBase = coreInfo.ReleaseApiUrl?.TrimEnd('/')
                ?? throw new InvalidOperationException("Официальный API Xray не настроен.");

            using var client = CreateHttpClient(networkMode);
            var root = await GetReleaseAsync(
                client,
                apiBase,
                channel,
                exactVersion,
                report);

            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            if (tag.IsNullOrEmpty())
            {
                throw new InvalidDataException(
                    "Официальный выпуск Xray не содержит номера версии.");
            }

            var archiveName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "Xray-windows-64.zip",
                Architecture.Arm64 => "Xray-windows-arm64-v8a.zip",
                _ => throw new PlatformNotSupportedException(
                    $"Архитектура {RuntimeInformation.ProcessArchitecture} не поддерживается ручным обновлением Xray."),
            };

            string? archiveUrl = null;
            string? digest = null;
            string? digestUrl = null;
            if (!root.TryGetProperty("assets", out var assets))
            {
                throw new InvalidDataException(
                    "В официальном выпуске Xray отсутствует список файлов.");
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                if (string.Equals(name, archiveName, StringComparison.OrdinalIgnoreCase))
                {
                    archiveUrl = url;
                    if (asset.TryGetProperty("digest", out var digestElement))
                    {
                        digest = digestElement.GetString();
                    }
                }
                else if (string.Equals(
                    name,
                    archiveName + ".dgst",
                    StringComparison.OrdinalIgnoreCase))
                {
                    digestUrl = url;
                }
            }

            if (archiveUrl.IsNullOrEmpty())
            {
                throw new FileNotFoundException(
                    $"В официальном выпуске {tag} не найден {archiveName}.");
            }

            var expectedSha256 = NormalizeGitHubDigest(digest);
            if (expectedSha256.IsNullOrEmpty() && digestUrl.IsNotEmpty())
            {
                report("Получение официальной контрольной суммы SHA-256…");
                var digestText = await client.GetStringAsync(digestUrl!);
                expectedSha256 = ParseDigestFile(digestText);
            }
            if (expectedSha256.IsNullOrEmpty())
            {
                throw new InvalidDataException(
                    "Официальная контрольная сумма SHA-256 для архива не найдена. Установка остановлена.");
            }

            var archivePath = Path.Combine(workDirectory, archiveName);
            report($"Загрузка официального Xray {tag}…");
            await DownloadFileAsync(client, archiveUrl!, archivePath);

            report("Проверка SHA-256 официального архива…");
            var packageSha256 = await ComputeSha256Async(archivePath);
            if (!string.Equals(
                packageSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 архива не совпадает. Ожидалось {expectedSha256}, получено {packageSha256}.");
            }

            var executablePath = Path.Combine(workDirectory, "xray.exe");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    string.Equals(
                        Path.GetFileName(item.FullName),
                        "xray.exe",
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    throw new InvalidDataException(
                        "В официальном архиве отсутствует xray.exe.");
                }
                entry.ExtractToFile(executablePath, true);
            }

            return await PrepareExecutableAsync(
                executablePath,
                workDirectory,
                GetSourceDescription(channel, tag, networkMode),
                true,
                packageSha256,
                expectedSha256,
                report);
        }
        catch (HttpRequestException ex)
            when (channel == SgXrayReleaseChannel.Exact
                && ex.StatusCode == HttpStatusCode.NotFound)
        {
            TryDeleteDirectory(workDirectory);
            throw new InvalidOperationException(
                $"Официальный выпуск Xray {NormalizeExactTag(exactVersion)} не найден.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            TryDeleteDirectory(workDirectory);
            throw new InvalidOperationException(
                GetNetworkError(networkMode, ex),
                ex);
        }
        catch (TaskCanceledException ex)
        {
            TryDeleteDirectory(workDirectory);
            throw new InvalidOperationException(
                GetNetworkError(networkMode, ex),
                ex);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private static async Task<JsonElement> GetReleaseAsync(
        HttpClient client,
        string apiBase,
        SgXrayReleaseChannel channel,
        string? exactVersion,
        Action<string> report)
    {
        switch (channel)
        {
            case SgXrayReleaseChannel.Stable:
                report("Получение последнего стабильного выпуска Xray…");
                return await GetJsonRootAsync(client, $"{apiBase}/latest");

            case SgXrayReleaseChannel.Prerelease:
                report("Поиск последнего предварительного выпуска Xray…");
                var releases = await GetJsonRootAsync(
                    client,
                    $"{apiBase}?per_page=30");
                foreach (var release in releases.EnumerateArray())
                {
                    var draft = release.TryGetProperty("draft", out var draftNode)
                        && draftNode.GetBoolean();
                    var prerelease = release.TryGetProperty(
                        "prerelease",
                        out var prereleaseNode)
                        && prereleaseNode.GetBoolean();
                    if (!draft && prerelease)
                    {
                        return release.Clone();
                    }
                }
                throw new InvalidDataException(
                    "Предварительный выпуск Xray не найден.");

            case SgXrayReleaseChannel.Exact:
                var tag = NormalizeExactTag(exactVersion);
                report($"Получение официального выпуска Xray {tag}…");
                return await GetJsonRootAsync(
                    client,
                    $"{apiBase}/tags/{Uri.EscapeDataString(tag)}");

            default:
                throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private static async Task<JsonElement> GetJsonRootAsync(
        HttpClient client,
        string url)
    {
        var json = await client.GetStringAsync(url);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string NormalizeExactTag(string? value)
    {
        var tag = value?.Trim() ?? string.Empty;
        if (tag.IsNullOrEmpty())
        {
            throw new ArgumentException("Точная версия Xray не указана.");
        }
        return tag.StartsWith('v') || tag.StartsWith('V')
            ? tag
            : "v" + tag;
    }

    private static string GetSourceDescription(
        SgXrayReleaseChannel channel,
        string tag,
        SgXrayNetworkMode networkMode)
    {
        var channelText = channel switch
        {
            SgXrayReleaseChannel.Stable => "стабильный выпуск",
            SgXrayReleaseChannel.Prerelease => "предварительный выпуск",
            SgXrayReleaseChannel.Exact => "точная версия",
            _ => "официальный выпуск",
        };
        var routeText = networkMode == SgXrayNetworkMode.DirectWithoutSystemProxy
            ? "без системного прокси"
            : "через текущий маршрут/VPN";
        return $"Официальный {channelText} {tag}, {routeText}";
    }

    private static string GetNetworkError(
        SgXrayNetworkMode mode,
        Exception exception)
    {
        var route = mode == SgXrayNetworkMode.DirectWithoutSystemProxy
            ? "без системного прокси"
            : "через текущий маршрут/VPN";
        return "Не удалось подключиться к официальному серверу релизов Xray "
            + $"{route}. Переключите маршрут загрузки и повторите проверку. "
            + $"Техническая причина: {exception.Message}";
    }

    public async Task<SgXrayCandidate> PrepareLocalAsync(string sourcePath, Action<string> report)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Выбранный xray.exe не найден.", sourcePath);
        }

        var workDirectory = CreateWorkDirectory();
        try
        {
            var executablePath = Path.Combine(workDirectory, "xray.exe");
            File.Copy(sourcePath, executablePath, true);
            return await PrepareExecutableAsync(
                executablePath,
                workDirectory,
                $"Локальный файл: {Path.GetFileName(sourcePath)}",
                false,
                null,
                null,
                report);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private async Task<SgXrayCandidate> PrepareExecutableAsync(
        string executablePath,
        string workDirectory,
        string sourceDescription,
        bool official,
        string? packageSha256,
        string? expectedPackageSha256,
        Action<string> report)
    {
        report("Проверка запуска xray.exe…");
        var versionResult = await RunXrayAsync(
            executablePath,
            ["-version"],
            null,
            TimeSpan.FromSeconds(15));
        if (!versionResult.Success)
        {
            throw new InvalidDataException(
                $"Выбранный файл не прошёл проверку xray -version:{Environment.NewLine}{versionResult.Output}");
        }

        var versionMatch = Regex.Match(
            versionResult.Output,
            @"Xray\s+([0-9]+(?:\.[0-9]+)+)",
            RegexOptions.IgnoreCase);
        if (!versionMatch.Success)
        {
            throw new InvalidDataException(
                "Файл запускается, но его версия Xray не распознана. Установка остановлена.");
        }

        var executableSha256 = await ComputeSha256Async(executablePath);
        report("Создание текущей конфигурации Xray для проверки…");
        var configPath = await CreateValidationConfigAsync(workDirectory);
        report("Проверка текущего профиля новым Xray…");
        var configResult = await RunXrayAsync(
            executablePath,
            ["run", "-test", "-config", configPath],
            configPath,
            TimeSpan.FromSeconds(25));
        if (!configResult.Success)
        {
            throw new InvalidDataException(
                $"Новый Xray отклонил текущую конфигурацию:{Environment.NewLine}{configResult.Output}");
        }

        return new SgXrayCandidate
        {
            ExecutablePath = executablePath,
            WorkDirectory = workDirectory,
            SourceDescription = sourceDescription,
            Version = versionMatch.Groups[1].Value,
            ExecutableSha256 = executableSha256,
            PackageSha256 = packageSha256,
            ExpectedPackageSha256 = expectedPackageSha256,
            IsOfficial = official,
        };
    }

    public async Task<SgXrayInstallResult> InstallAsync(
        SgXrayCandidate candidate,
        Action<string> report)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray)
            ?? throw new InvalidOperationException("Описание ядра Xray не найдено.");
        var targetPath = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var coreError);
        if (targetPath.IsNullOrEmpty() || !File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                coreError.IsNotEmpty() ? coreError : "Текущий xray.exe не найден.",
                targetPath);
        }

        var candidateSha = await ComputeSha256Async(candidate.ExecutablePath);
        if (!string.Equals(candidateSha, candidate.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Подготовленный xray.exe изменился после проверки.");
        }

        var previousMode = StatusBarViewModel.Instance.GetConnectionModeKey();
        var wasCoreRunning = CoreManager.Instance.IsCoreRunning;
        var currentVersion = await GetXrayVersionAsync(targetPath);
        var backupDirectory = Utils.GetBackupPath("xray-core");
        Directory.CreateDirectory(backupDirectory);
        var backupBase = $"xray-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{SanitizeVersion(currentVersion)}";
        var backupPath = Path.Combine(backupDirectory, backupBase + ".exe");
        var metadataPath = Path.Combine(backupDirectory, backupBase + ".json");
        var stagedPath = targetPath + ".sg-new";
        var replacementBackupPath = targetPath + ".sg-previous";
        var connectionStopped = false;
        var replacementStarted = false;

        try
        {
            report("Остановка активного подключения и системного прокси…");
            await StatusBarViewModel.Instance.StopConnectionForMaintenanceAsync();
            connectionStopped = true;

            report("Создание резервной копии текущего xray.exe…");
            File.Copy(targetPath, backupPath, false);
            await WriteMetadataAsync(
                metadataPath,
                new SgXrayBackupMetadata
                {
                    CreatedAt = DateTimeOffset.Now,
                    PreviousVersion = currentVersion,
                    NewVersion = candidate.Version,
                    PreviousSha256 = await ComputeSha256Async(backupPath),
                    NewSha256 = candidate.ExecutableSha256,
                    Source = candidate.SourceDescription,
                    Status = "backup-created",
                });

            File.Copy(candidate.ExecutablePath, stagedPath, true);
            if (File.Exists(replacementBackupPath))
            {
                File.Delete(replacementBackupPath);
            }

            report("Замена ядра Xray…");
            replacementStarted = true;
            try
            {
                File.Replace(stagedPath, targetPath, replacementBackupPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(targetPath, replacementBackupPath, true);
                File.Copy(stagedPath, targetPath, true);
                File.Delete(stagedPath);
            }
            catch (IOException)
            {
                File.Copy(targetPath, replacementBackupPath, true);
                File.Copy(stagedPath, targetPath, true);
                File.Delete(stagedPath);
            }

            var installedSha = await ComputeSha256Async(targetPath);
            if (!string.Equals(installedSha, candidate.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 установленного xray.exe не совпал с проверенным файлом.");
            }

            report("Повторная проверка установленного ядра и текущего профиля…");
            var installedVersion = await GetXrayVersionAsync(targetPath);
            if (!string.Equals(installedVersion, candidate.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"После замены ожидалась версия {candidate.Version}, определена {installedVersion}.");
            }

            var validationDirectory = Path.Combine(candidate.WorkDirectory, "installed-test");
            Directory.CreateDirectory(validationDirectory);
            var configPath = await CreateValidationConfigAsync(validationDirectory);
            var testResult = await RunXrayAsync(
                targetPath,
                ["run", "-test", "-config", configPath],
                configPath,
                TimeSpan.FromSeconds(25));
            if (!testResult.Success)
            {
                throw new InvalidDataException(
                    $"Установленный Xray отклонил текущую конфигурацию:{Environment.NewLine}{testResult.Output}");
            }

            await WriteMetadataAsync(
                metadataPath,
                new SgXrayBackupMetadata
                {
                    CreatedAt = DateTimeOffset.Now,
                    PreviousVersion = currentVersion,
                    NewVersion = installedVersion,
                    PreviousSha256 = await ComputeSha256Async(backupPath),
                    NewSha256 = installedSha,
                    Source = candidate.SourceDescription,
                    Status = "installed-and-tested",
                });

            if (File.Exists(replacementBackupPath))
            {
                File.Delete(replacementBackupPath);
            }

            if (wasCoreRunning)
            {
                report("Восстановление прежнего режима подключения…");
                await StatusBarViewModel.Instance.RestoreConnectionAfterMaintenanceAsync(previousMode);
            }

            return new SgXrayInstallResult
            {
                Success = true,
                Message = $"Xray обновлён: {currentVersion} → {installedVersion}. Резервная копия сохранена.",
                BackupPath = backupPath,
            };
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgXrayUpdateService.Install", ex);
            var rollbackMessage = string.Empty;
            if (replacementStarted && File.Exists(backupPath))
            {
                try
                {
                    report("Ошибка после замены. Автоматический возврат предыдущего Xray…");
                    await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(20));
                    File.Copy(backupPath, targetPath, true);
                    var restoredVersion = await GetXrayVersionAsync(targetPath);
                    rollbackMessage = $" Предыдущая версия {restoredVersion} восстановлена.";
                    await WriteMetadataAsync(
                        metadataPath,
                        new SgXrayBackupMetadata
                        {
                            CreatedAt = DateTimeOffset.Now,
                            PreviousVersion = currentVersion,
                            NewVersion = candidate.Version,
                            PreviousSha256 = await ComputeSha256Async(backupPath),
                            NewSha256 = candidate.ExecutableSha256,
                            Source = candidate.SourceDescription,
                            Status = "rolled-back",
                            Error = ex.Message,
                        });
                }
                catch (Exception rollbackException)
                {
                    Logging.SaveLog("SgXrayUpdateService.Rollback", rollbackException);
                    rollbackMessage =
                        $" Автоматический откат не завершён: {rollbackException.Message}. "
                        + $"Страховочная копия: {backupPath}";
                }
            }

            if (connectionStopped && wasCoreRunning)
            {
                try
                {
                    await StatusBarViewModel.Instance.RestoreConnectionAfterMaintenanceAsync(previousMode);
                }
                catch (Exception restoreException)
                {
                    Logging.SaveLog("SgXrayUpdateService.RestoreConnection", restoreException);
                    rollbackMessage += $" Подключение не восстановлено: {restoreException.Message}";
                }
            }

            return new SgXrayInstallResult
            {
                Success = false,
                Message = $"Обновление Xray не выполнено: {ex.Message}.{rollbackMessage}",
                BackupPath = File.Exists(backupPath) ? backupPath : null,
            };
        }
        finally
        {
            TryDeleteFile(stagedPath);
            TryDeleteFile(replacementBackupPath);
        }
    }

    private async Task<string> CreateValidationConfigAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var profile = await AppManager.Instance.GetProfileItem(_config.IndexId)
            ?? throw new InvalidOperationException(
                "Выберите профиль Xray перед обновлением ядра. Он нужен для проверки совместимости.");

        var validationConfig = JsonUtils.DeepCopy(_config)
            ?? throw new InvalidOperationException("Не удалось создать страховочную копию настроек.");
        validationConfig.TunModeItem.EnableTun = false;

        var builder = await CoreConfigContextBuilder.Build(validationConfig, profile);
        if (!builder.Success)
        {
            var errors = builder.ValidatorResult.Errors.Count > 0
                ? string.Join(Environment.NewLine, builder.ValidatorResult.Errors)
                : "Текущий профиль не прошёл внутреннюю проверку.";
            throw new InvalidDataException(errors);
        }
        if (builder.Context.RunCoreType != ECoreType.Xray)
        {
            throw new InvalidOperationException(
                "Текущий профиль использует не Xray. Выберите профиль VLESS/Xray для проверки обновления.");
        }

        var configPath = Path.Combine(directory, "xray-update-config.json");
        var result = await CoreConfigHandler.GenerateClientConfig(builder.Context, configPath);
        if (result.Success != true || !File.Exists(configPath))
        {
            throw new InvalidDataException(
                result.Msg.IsNotEmpty() ? result.Msg : "Не удалось создать временный config.json.");
        }
        return configPath;
    }

    private static async Task<SgProcessResult> RunXrayAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? configPath,
        TimeSpan timeout)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray)
            ?? throw new InvalidOperationException("Описание ядра Xray не найдено.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Utils.StartupPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var pair in coreInfo.Environment)
        {
            if (pair.Value != null)
            {
                startInfo.Environment[pair.Key] = string.Format(
                    pair.Value,
                    configPath ?? string.Empty);
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new SgProcessResult(false, "Не удалось запустить xray.exe.");
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
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
            return new SgProcessResult(false, $"Проверка превысила {timeout.TotalSeconds:0} секунд.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdoutTask, await stderrTask }
                .Where(value => value.IsNotEmpty())
                .Select(value => value.Trim()));
        return new SgProcessResult(
            process.ExitCode == 0,
            output.IsNotEmpty() ? output : $"Код завершения: {process.ExitCode}");
    }

    private static async Task<string> GetXrayVersionAsync(string executablePath)
    {
        var result = await RunXrayAsync(
            executablePath,
            ["-version"],
            null,
            TimeSpan.FromSeconds(15));
        if (!result.Success)
        {
            throw new InvalidDataException(result.Output);
        }
        var match = Regex.Match(result.Output, @"Xray\s+([0-9]+(?:\.[0-9]+)+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new InvalidDataException("Версия Xray не распознана.");
        }
        return match.Groups[1].Value;
    }

    private static HttpClient CreateHttpClient(SgXrayNetworkMode mode)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = mode != SgXrayNetworkMode.DirectWithoutSystemProxy,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SG-Client/055");
        return client;
    }

    private static async Task DownloadFileAsync(HttpClient client, string url, string path)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static string? NormalizeGitHubDigest(string? digest)
    {
        if (digest.IsNullOrEmpty())
        {
            return null;
        }
        var match = Regex.Match(digest!, @"(?:sha256:)?([A-Fa-f0-9]{64})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? ParseDigestFile(string text)
    {
        var match = Regex.Match(
            text ?? string.Empty,
            @"(?im)(?:SHA2-256|SHA-?256)(?:\s*\([^\r\n)]*\)|\s+[^\r\n=:]+)?\s*[:=]\s*([A-Fa-f0-9]{64})");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string CreateWorkDirectory()
    {
        var directory = Utils.GetTempPath($"xray-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizeVersion(string value)
    {
        return Regex.Replace(value.IsNotEmpty() ? value : "unknown", @"[^0-9A-Za-z._-]+", "_");
    }

    private static async Task WriteMetadataAsync(string path, SgXrayBackupMetadata metadata)
    {
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (path.IsNullOrEmpty())
        {
            return;
        }
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgXrayUpdateService.CleanupDirectory", ex);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgXrayUpdateService.CleanupFile", ex);
        }
    }
}

public enum SgXrayReleaseChannel
{
    Stable,
    Prerelease,
    Exact,
}

public enum SgXrayNetworkMode
{
    CurrentRoute,
    DirectWithoutSystemProxy,
}

public sealed class SgXrayCandidate
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string WorkDirectory { get; init; } = string.Empty;
    public string SourceDescription { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ExecutableSha256 { get; init; } = string.Empty;
    public string? PackageSha256 { get; init; }
    public string? ExpectedPackageSha256 { get; init; }
    public bool IsOfficial { get; init; }

    public string IntegrityDescription => IsOfficial
        ? "Официальная SHA-256 архива проверена и совпала."
        : "SHA-256 локального xray.exe рассчитана; официальное происхождение файла не подтверждается.";

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(WorkDirectory))
            {
                Directory.Delete(WorkDirectory, true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgXrayCandidate.Cleanup", ex);
        }
    }
}

public sealed class SgXrayInstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? BackupPath { get; init; }
}

public sealed class SgXrayBackupMetadata
{
    public DateTimeOffset CreatedAt { get; set; }
    public string PreviousVersion { get; set; } = string.Empty;
    public string NewVersion { get; set; } = string.Empty;
    public string PreviousSha256 { get; set; } = string.Empty;
    public string NewSha256 { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

internal sealed record SgProcessResult(bool Success, string Output);
