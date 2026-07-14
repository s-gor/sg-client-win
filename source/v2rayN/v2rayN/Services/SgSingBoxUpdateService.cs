using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceLib.Handler.Builder;

namespace v2rayN.Services;

public sealed class SgSingBoxUpdateService
{
    private readonly Config _config;

    public SgSingBoxUpdateService(Config config)
    {
        _config = config;
    }

    public async Task<SgSingBoxCandidate> PrepareOfficialAsync(
        SgXrayReleaseChannel channel,
        string? exactVersion,
        SgXrayNetworkMode networkMode,
        Action<string> report)
    {
        var workDirectory = CreateWorkDirectory();
        try
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box)
                ?? throw new InvalidOperationException("Описание ядра sing-box не найдено.");
            var apiBase = coreInfo.ReleaseApiUrl?.TrimEnd('/')
                ?? throw new InvalidOperationException("Официальный API sing-box не настроен.");

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
                    "Официальный выпуск sing-box не содержит номера версии.");
            }

            var version = tag.TrimStart('v', 'V');
            var archiveName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => $"sing-box-{version}-windows-amd64.zip",
                Architecture.Arm64 => $"sing-box-{version}-windows-arm64.zip",
                _ => throw new PlatformNotSupportedException(
                    $"Архитектура {RuntimeInformation.ProcessArchitecture} не поддерживается ручным обновлением sing-box."),
            };

            if (!root.TryGetProperty("assets", out var assets))
            {
                throw new InvalidDataException(
                    "В официальном выпуске sing-box отсутствует список файлов.");
            }

            string? archiveUrl = null;
            string? expectedSha256 = null;
            var checksumAssets = new List<(string Name, string Url)>();

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString() ?? string.Empty
                    : string.Empty;

                if (string.Equals(name, archiveName, StringComparison.OrdinalIgnoreCase))
                {
                    archiveUrl = url;
                    if (asset.TryGetProperty("digest", out var digestElement))
                    {
                        expectedSha256 = NormalizeGitHubDigest(digestElement.GetString());
                    }
                }

                if (url.IsNotEmpty() && IsChecksumAsset(name, archiveName))
                {
                    checksumAssets.Add((name, url));
                }
            }

            if (archiveUrl.IsNullOrEmpty())
            {
                throw new FileNotFoundException(
                    $"В официальном выпуске {tag} не найден {archiveName}.");
            }

            if (expectedSha256.IsNullOrEmpty())
            {
                foreach (var checksumAsset in checksumAssets)
                {
                    report($"Получение официальной контрольной суммы из {checksumAsset.Name}…");
                    var checksumText = await client.GetStringAsync(checksumAsset.Url);
                    expectedSha256 = ParseChecksumText(checksumText, archiveName);
                    if (expectedSha256.IsNotEmpty())
                    {
                        break;
                    }
                }
            }

            if (expectedSha256.IsNullOrEmpty())
            {
                throw new InvalidDataException(
                    "Официальная контрольная сумма SHA-256 для архива sing-box не найдена. Установка остановлена.");
            }

            var archivePath = Path.Combine(workDirectory, archiveName);
            report($"Загрузка официального sing-box {tag}…");
            await DownloadFileAsync(client, archiveUrl!, archivePath);

            report("Проверка SHA-256 официального архива sing-box…");
            var packageSha256 = await ComputeSha256Async(archivePath);
            if (!string.Equals(
                packageSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 архива не совпадает. Ожидалось {expectedSha256}, получено {packageSha256}.");
            }

            var executablePath = Path.Combine(workDirectory, "sing-box.exe");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    string.Equals(
                        Path.GetFileName(item.FullName),
                        "sing-box.exe",
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    throw new InvalidDataException(
                        "В официальном архиве отсутствует sing-box.exe.");
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
                $"Официальный выпуск sing-box {NormalizeExactTag(exactVersion)} не найден.",
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

    public async Task<SgSingBoxCandidate> PrepareLocalAsync(
        string sourcePath,
        Action<string> report)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Выбранный sing-box.exe не найден.",
                sourcePath);
        }

        var workDirectory = CreateWorkDirectory();
        try
        {
            var executablePath = Path.Combine(workDirectory, "sing-box.exe");
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

    private async Task<SgSingBoxCandidate> PrepareExecutableAsync(
        string executablePath,
        string workDirectory,
        string sourceDescription,
        bool official,
        string? packageSha256,
        string? expectedPackageSha256,
        Action<string> report)
    {
        report("Проверка запуска sing-box.exe…");
        var versionResult = await RunSingBoxAsync(
            executablePath,
            ["version"],
            TimeSpan.FromSeconds(15));
        if (!versionResult.Success)
        {
            throw new InvalidDataException(
                $"Выбранный файл не прошёл проверку sing-box version:{Environment.NewLine}{versionResult.Output}");
        }

        var version = ParseVersion(versionResult.Output);
        var executableSha256 = await ComputeSha256Async(executablePath);

        report("Поиск профиля, который фактически использует sing-box…");
        var validation = await CreateValidationConfigAsync(workDirectory);
        report(validation.HasProfileValidation
            ? $"Проверка рабочей конфигурации новым sing-box: {validation.Description}…"
            : "Профиль sing-box не найден. Проверка запуска и минимальной конфигурации…");
        var configResult = await RunSingBoxAsync(
            executablePath,
            ["check", "-c", validation.ConfigPath],
            TimeSpan.FromSeconds(30));
        if (!configResult.Success)
        {
            throw new InvalidDataException(
                $"Новый sing-box отклонил проверочную конфигурацию:{Environment.NewLine}{configResult.Output}");
        }

        return new SgSingBoxCandidate
        {
            ExecutablePath = executablePath,
            WorkDirectory = workDirectory,
            SourceDescription = sourceDescription,
            Version = version,
            ExecutableSha256 = executableSha256,
            PackageSha256 = packageSha256,
            ExpectedPackageSha256 = expectedPackageSha256,
            IsOfficial = official,
            HasProfileValidation = validation.HasProfileValidation,
            ValidationDescription = validation.Description,
        };
    }

    public async Task<SgSingBoxInstallResult> InstallAsync(
        SgSingBoxCandidate candidate,
        Action<string> report)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box)
            ?? throw new InvalidOperationException("Описание ядра sing-box не найдено.");
        var targetPath = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var coreError);
        if (targetPath.IsNullOrEmpty() || !File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                coreError.IsNotEmpty() ? coreError : "Текущий sing-box.exe не найден.",
                targetPath);
        }

        var candidateSha = await ComputeSha256Async(candidate.ExecutablePath);
        if (!string.Equals(
            candidateSha,
            candidate.ExecutableSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Подготовленный sing-box.exe изменился после проверки.");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Папка sing-box не определена.");
        var previousMode = StatusBarViewModel.Instance.GetConnectionModeKey();
        var wasCoreRunning = CoreManager.Instance.IsCoreRunning;
        var currentVersion = await GetSingBoxVersionAsync(targetPath);
        var backupDirectory = Utils.GetBackupPath("sing-box-core");
        Directory.CreateDirectory(backupDirectory);
        var backupBase = $"sing-box-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{SanitizeVersion(currentVersion)}";
        var backupPath = Path.Combine(backupDirectory, backupBase + ".zip");
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

            report("Создание резервной копии всей папки sing-box…");
            ZipFile.CreateFromDirectory(
                targetDirectory,
                backupPath,
                CompressionLevel.Optimal,
                false);
            await WriteMetadataAsync(
                metadataPath,
                new SgSingBoxBackupMetadata
                {
                    CreatedAt = DateTimeOffset.Now,
                    PreviousVersion = currentVersion,
                    NewVersion = candidate.Version,
                    PreviousSha256 = await ComputeSha256Async(targetPath),
                    NewSha256 = candidate.ExecutableSha256,
                    Source = candidate.SourceDescription,
                    Status = "backup-created",
                });

            File.Copy(candidate.ExecutablePath, stagedPath, true);
            TryDeleteFile(replacementBackupPath);

            report("Замена ядра sing-box…");
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
            if (!string.Equals(
                installedSha,
                candidate.ExecutableSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "SHA-256 установленного sing-box.exe не совпал с проверенным файлом.");
            }

            report("Повторная проверка установленного ядра и текущего профиля…");
            var installedVersion = await GetSingBoxVersionAsync(targetPath);
            if (!string.Equals(
                installedVersion,
                candidate.Version,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"После замены ожидалась версия {candidate.Version}, определена {installedVersion}.");
            }

            var validationDirectory = Path.Combine(
                candidate.WorkDirectory,
                "installed-test");
            Directory.CreateDirectory(validationDirectory);
            var validation = await CreateValidationConfigAsync(validationDirectory);
            var testResult = await RunSingBoxAsync(
                targetPath,
                ["check", "-c", validation.ConfigPath],
                TimeSpan.FromSeconds(30));
            if (!testResult.Success)
            {
                throw new InvalidDataException(
                    $"Установленный sing-box отклонил текущую конфигурацию:{Environment.NewLine}{testResult.Output}");
            }

            await WriteMetadataAsync(
                metadataPath,
                new SgSingBoxBackupMetadata
                {
                    CreatedAt = DateTimeOffset.Now,
                    PreviousVersion = currentVersion,
                    NewVersion = installedVersion,
                    PreviousSha256 = await ComputeSha256FromBackupAsync(
                        backupPath,
                        "sing-box.exe"),
                    NewSha256 = installedSha,
                    Source = candidate.SourceDescription,
                    Status = "installed-and-tested",
                });

            TryDeleteFile(replacementBackupPath);

            if (wasCoreRunning)
            {
                report("Восстановление прежнего режима подключения…");
                await StatusBarViewModel.Instance.RestoreConnectionAfterMaintenanceAsync(
                    previousMode);
            }

            return new SgSingBoxInstallResult
            {
                Success = true,
                Message = $"sing-box обновлён: {currentVersion} → {installedVersion}. Резервная копия всей папки сохранена.",
                BackupPath = backupPath,
            };
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgSingBoxUpdateService.Install", ex);
            var rollbackMessage = string.Empty;
            if (replacementStarted && File.Exists(backupPath))
            {
                try
                {
                    report("Ошибка после замены. Автоматический возврат предыдущей папки sing-box…");
                    await CoreManager.Instance.CoreStop().WaitAsync(TimeSpan.FromSeconds(20));
                    RestoreDirectoryFromZip(backupPath, targetDirectory);
                    var restoredVersion = await GetSingBoxVersionAsync(targetPath);
                    rollbackMessage = $" Предыдущая версия {restoredVersion} восстановлена.";
                    await WriteMetadataAsync(
                        metadataPath,
                        new SgSingBoxBackupMetadata
                        {
                            CreatedAt = DateTimeOffset.Now,
                            PreviousVersion = currentVersion,
                            NewVersion = candidate.Version,
                            PreviousSha256 = await ComputeSha256Async(targetPath),
                            NewSha256 = candidate.ExecutableSha256,
                            Source = candidate.SourceDescription,
                            Status = "rolled-back",
                            Error = ex.Message,
                        });
                }
                catch (Exception rollbackException)
                {
                    Logging.SaveLog(
                        "SgSingBoxUpdateService.Rollback",
                        rollbackException);
                    rollbackMessage =
                        $" Автоматический откат не завершён: {rollbackException.Message}. "
                        + $"Страховочная копия: {backupPath}";
                }
            }

            if (connectionStopped && wasCoreRunning)
            {
                try
                {
                    await StatusBarViewModel.Instance.RestoreConnectionAfterMaintenanceAsync(
                        previousMode);
                }
                catch (Exception restoreException)
                {
                    Logging.SaveLog(
                        "SgSingBoxUpdateService.RestoreConnection",
                        restoreException);
                    rollbackMessage +=
                        $" Подключение не восстановлено: {restoreException.Message}";
                }
            }

            return new SgSingBoxInstallResult
            {
                Success = false,
                Message = $"Обновление sing-box не выполнено: {ex.Message}.{rollbackMessage}",
                BackupPath = File.Exists(backupPath) ? backupPath : null,
            };
        }
        finally
        {
            TryDeleteFile(stagedPath);
            TryDeleteFile(replacementBackupPath);
        }
    }

    private sealed record ValidationConfigResult(
        string ConfigPath,
        bool HasProfileValidation,
        string Description);

    private async Task<ValidationConfigResult> CreateValidationConfigAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        var profiles = new List<ProfileItem>();
        var active = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (active != null)
        {
            profiles.Add(active);
        }

        var allProfiles = await AppManager.Instance.ProfileItems(string.Empty);
        if (allProfiles != null)
        {
            foreach (var item in allProfiles)
            {
                if (profiles.Any(existing => string.Equals(
                        existing.IndexId,
                        item.IndexId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                profiles.Add(item);
            }
        }

        var skipped = new List<string>();
        foreach (var profile in profiles)
        {
            try
            {
                var validationConfig = JsonUtils.DeepCopy(_config)
                    ?? throw new InvalidOperationException(
                        "Не удалось создать страховочную копию настроек.");
                validationConfig.TunModeItem.EnableTun = false;

                var builder = await CoreConfigContextBuilder.Build(
                    validationConfig,
                    profile);
                if (!builder.Success)
                {
                    var reason = builder.ValidatorResult.Errors.Count > 0
                        ? string.Join(" · ", builder.ValidatorResult.Errors)
                        : "внутренняя проверка не пройдена";
                    skipped.Add($"{profile.Remarks}: {reason}");
                    continue;
                }
                if (builder.Context.RunCoreType != ECoreType.sing_box)
                {
                    skipped.Add($"{profile.Remarks}: фактически используется {builder.Context.RunCoreType}");
                    continue;
                }

                var profileConfigPath = Path.Combine(
                    directory,
                    "sing-box-update-profile-config.json");
                var result = await CoreConfigHandler.GenerateClientConfig(
                    builder.Context,
                    profileConfigPath);
                if (result.Success != true || !File.Exists(profileConfigPath))
                {
                    skipped.Add(
                        $"{profile.Remarks}: "
                        + (result.Msg.IsNotEmpty()
                            ? result.Msg
                            : "не удалось создать config.json"));
                    continue;
                }

                return new ValidationConfigResult(
                    profileConfigPath,
                    true,
                    $"{profile.Remarks} · {profile.ConfigType}");
            }
            catch (Exception ex)
            {
                Logging.SaveLog(
                    $"SgSingBoxUpdateService.ValidationProfile.{profile.IndexId}",
                    ex);
                skipped.Add($"{profile.Remarks}: {ex.Message}");
            }
        }

        var minimalPath = Path.Combine(
            directory,
            "sing-box-update-minimal-config.json");
        const string minimalConfig = """
        {
          "log": {
            "level": "error",
            "timestamp": true
          },
          "inbounds": [],
          "outbounds": [
            {
              "type": "direct",
              "tag": "direct"
            }
          ],
          "route": {
            "rules": []
          }
        }
        """;
        await File.WriteAllTextAsync(minimalPath, minimalConfig);

        var detail = skipped.Count == 0
            ? "Профилей, фактически назначенных ядру sing-box, нет"
            : "Профили sing-box не найдены; просмотрено: "
                + string.Join(" | ", skipped.Take(4));
        return new ValidationConfigResult(
            minimalPath,
            false,
            detail + ". Проверены запуск ядра и минимальная конфигурация, но не рабочий профиль.");
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
                report("Получение последнего стабильного выпуска sing-box…");
                return await GetJsonRootAsync(client, $"{apiBase}/latest");

            case SgXrayReleaseChannel.Prerelease:
                report("Поиск последнего предварительного выпуска sing-box…");
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
                    "Предварительный выпуск sing-box не найден.");

            case SgXrayReleaseChannel.Exact:
                var tag = NormalizeExactTag(exactVersion);
                report($"Получение официального выпуска sing-box {tag}…");
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

    private static async Task<SgProcessResult> RunSingBoxAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
                ?? Utils.StartupPath(),
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

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new SgProcessResult(
                false,
                "Не удалось запустить sing-box.exe.");
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
            return new SgProcessResult(
                false,
                $"Проверка превысила {timeout.TotalSeconds:0} секунд.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdoutTask, await stderrTask }
                .Where(value => value.IsNotEmpty())
                .Select(value => value.Trim()));
        return new SgProcessResult(
            process.ExitCode == 0,
            output.IsNotEmpty()
                ? output
                : $"Код завершения: {process.ExitCode}");
    }

    private static string ParseVersion(string output)
    {
        var match = Regex.Match(
            output,
            @"sing-box\s+version\s+([0-9]+(?:\.[0-9]+)+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new InvalidDataException(
                "Версия sing-box не распознана.");
        }
        return match.Groups[1].Value;
    }

    private static async Task<string> GetSingBoxVersionAsync(
        string executablePath)
    {
        var result = await RunSingBoxAsync(
            executablePath,
            ["version"],
            TimeSpan.FromSeconds(15));
        if (!result.Success)
        {
            throw new InvalidDataException(result.Output);
        }
        return ParseVersion(result.Output);
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

    private static string NormalizeExactTag(string? value)
    {
        var tag = value?.Trim() ?? string.Empty;
        if (tag.IsNullOrEmpty())
        {
            throw new ArgumentException("Точная версия sing-box не указана.");
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
        return "Не удалось подключиться к официальному серверу релизов sing-box "
            + $"{route}. Переключите маршрут загрузки и повторите проверку. "
            + $"Техническая причина: {exception.Message}";
    }

    private static bool IsChecksumAsset(string name, string archiveName)
    {
        if (name.IsNullOrEmpty())
        {
            return false;
        }
        return name.Equals(archiveName + ".sha256", StringComparison.OrdinalIgnoreCase)
            || name.Equals(archiveName + ".sha256sum", StringComparison.OrdinalIgnoreCase)
            || name.Contains("checksum", StringComparison.OrdinalIgnoreCase)
            || name.Contains("sha256", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseChecksumText(
        string text,
        string archiveName)
    {
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            if (!line.Contains(archiveName, StringComparison.OrdinalIgnoreCase)
                && (text ?? string.Empty).Contains('\n'))
            {
                continue;
            }
            var match = Regex.Match(line, @"\b([A-Fa-f0-9]{64})\b");
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }
        }

        var single = Regex.Match(text ?? string.Empty, @"\b([A-Fa-f0-9]{64})\b");
        return single.Success
            ? single.Groups[1].Value.ToUpperInvariant()
            : null;
    }

    private static string? NormalizeGitHubDigest(string? digest)
    {
        if (digest.IsNullOrEmpty())
        {
            return null;
        }
        var match = Regex.Match(
            digest!,
            @"(?:sha256:)?([A-Fa-f0-9]{64})",
            RegexOptions.IgnoreCase);
        return match.Success
            ? match.Groups[1].Value.ToUpperInvariant()
            : null;
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        string path)
    {
        using var response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await input.CopyToAsync(output);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static async Task<string> ComputeSha256FromBackupAsync(
        string zipPath,
        string fileName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(
                Path.GetFileName(item.FullName),
                fileName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"В резервной копии отсутствует {fileName}.");
        await using var stream = entry.Open();
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static void RestoreDirectoryFromZip(
        string zipPath,
        string targetDirectory)
    {
        var restoreDirectory = Utils.GetTempPath(
            $"sing-box-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(restoreDirectory);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, restoreDirectory, true);
            Directory.CreateDirectory(targetDirectory);

            foreach (var file in Directory.GetFiles(
                targetDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
            foreach (var directory in Directory.GetDirectories(
                targetDirectory,
                "*",
                SearchOption.AllDirectories)
                .OrderByDescending(value => value.Length))
            {
                Directory.Delete(directory, false);
            }

            foreach (var sourceFile in Directory.GetFiles(
                restoreDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(
                    restoreDirectory,
                    sourceFile);
                var targetFile = Path.Combine(targetDirectory, relative);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(targetFile) ?? targetDirectory);
                File.Copy(sourceFile, targetFile, true);
            }
        }
        finally
        {
            TryDeleteDirectory(restoreDirectory);
        }
    }

    private static string CreateWorkDirectory()
    {
        var directory = Utils.GetTempPath(
            $"sing-box-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizeVersion(string value)
    {
        return Regex.Replace(
            value.IsNotEmpty() ? value : "unknown",
            @"[^0-9A-Za-z._-]+",
            "_");
    }

    private static async Task WriteMetadataAsync(
        string path,
        SgSingBoxBackupMetadata metadata)
    {
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (path.IsNullOrEmpty() || !Directory.Exists(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path!, true);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}

public sealed class SgSingBoxCandidate
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string WorkDirectory { get; init; } = string.Empty;
    public string SourceDescription { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ExecutableSha256 { get; init; } = string.Empty;
    public string? PackageSha256 { get; init; }
    public string? ExpectedPackageSha256 { get; init; }
    public bool IsOfficial { get; init; }
    public bool HasProfileValidation { get; init; }
    public string ValidationDescription { get; init; } = string.Empty;

    public string IntegrityDescription => IsOfficial
        ? "Официальный SHA-256 архива совпал"
        : "Локальный файл: SHA-256 рассчитан, официальный источник не подтверждается";

    public void Cleanup()
    {
        if (!Directory.Exists(WorkDirectory))
        {
            return;
        }
        try
        {
            Directory.Delete(WorkDirectory, true);
        }
        catch
        {
        }
    }
}

public sealed class SgSingBoxInstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? BackupPath { get; init; }
}

public sealed class SgSingBoxBackupMetadata
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
