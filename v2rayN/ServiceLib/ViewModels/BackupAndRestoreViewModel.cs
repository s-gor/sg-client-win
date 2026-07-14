using System.IO.Compression;

namespace ServiceLib.ViewModels;

public class BackupAndRestoreViewModel : MyReactiveObject
{
    private readonly string _guiConfigs = "guiConfigs";
    private static string BackupFileName => $"backup_{DateTime.Now:yyyyMMddHHmmss}.zip";

    public ReactiveCommand<Unit, Unit> RemoteBackupCmd { get; }
    public ReactiveCommand<Unit, Unit> RemoteRestoreCmd { get; }
    public ReactiveCommand<Unit, Unit> WebDavCheckCmd { get; }

    [Reactive]
    public WebDavItem SelectedSource { get; set; }

    [Reactive]
    public string OperationMsg { get; set; }

    public BackupAndRestoreViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;

        WebDavCheckCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await WebDavCheck();
        });
        RemoteBackupCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoteBackup();
        });
        RemoteRestoreCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoteRestore();
        });

        SelectedSource = JsonUtils.DeepCopy(_config.WebDavItem);
    }

    private void DisplayOperationMsg(string msg = "")
    {
        OperationMsg = msg;
    }

    private async Task WebDavCheck()
    {
        DisplayOperationMsg();
        _config.WebDavItem = SelectedSource;
        _ = await ConfigHandler.SaveConfig(_config);

        var result = await WebDavManager.Instance.CheckConnection();
        if (result)
        {
            DisplayOperationMsg(ResUI.OperationSuccess);
        }
        else
        {
            DisplayOperationMsg(WebDavManager.Instance.GetLastError());
        }
    }

    private async Task RemoteBackup()
    {
        DisplayOperationMsg();
        var fileName = Utils.GetBackupPath(BackupFileName);
        var result = await CreateZipFileFromDirectory(fileName);
        if (result)
        {
            var result2 = await WebDavManager.Instance.PutFile(fileName);
            if (result2)
            {
                DisplayOperationMsg(ResUI.OperationSuccess);
                return;
            }
        }

        DisplayOperationMsg(WebDavManager.Instance.GetLastError());
    }

    private async Task RemoteRestore()
    {
        DisplayOperationMsg();
        var fileName = Utils.GetTempPath(Utils.GetGuid());
        var result = await WebDavManager.Instance.GetRawFile(fileName);
        if (result)
        {
            await LocalRestore(fileName);
            return;
        }

        DisplayOperationMsg(WebDavManager.Instance.GetLastError());
    }

    public async Task<bool> LocalBackup(string fileName)
    {
        DisplayOperationMsg();
        var result = await CreateZipFileFromDirectory(fileName);
        if (result)
        {
            DisplayOperationMsg(ResUI.OperationSuccess);
        }
        else
        {
            DisplayOperationMsg(WebDavManager.Instance.GetLastError());
        }

        return result;
    }

    public async Task LocalRestore(string fileName)
    {
        DisplayOperationMsg();
        if (fileName.IsNullOrEmpty())
        {
            return;
        }
        //exist
        if (!File.Exists(fileName))
        {
            return;
        }
        // Check both the current format (guiConfigs at ZIP root) and
        // SG Client 071 archives that contain one wrapper directory.
        try
        {
            using var archive = ZipFile.OpenRead(fileName);
            if (!TryValidateBackupArchive(archive, out _))
            {
                DisplayOperationMsg(ResUI.LocalRestoreInvalidZipTips);
                return;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("BackupAndRestoreViewModel.ValidateLocalRestore", ex);
            DisplayOperationMsg(ResUI.LocalRestoreInvalidZipTips);
            return;
        }

        // Create a safety backup first, then prepare the selected archive in an isolated
        // directory. Only a fully validated and prepared tree replaces the current state.
        var fileBackup = Utils.GetBackupPath(BackupFileName);
        var result = await CreateZipFileFromDirectory(fileBackup);
        if (!result)
        {
            DisplayOperationMsg(WebDavManager.Instance.GetLastError());
            return;
        }

        var restoreTemp = Utils.GetTempPath($"SGClientRestore_{DateTime.Now:yyyyMMddHHmmss}_{Utils.GetGuid()}");
        try
        {
            if (Directory.Exists(restoreTemp))
            {
                Directory.Delete(restoreTemp, true);
            }

            Directory.CreateDirectory(restoreTemp);
            if (!ExtractGuiConfigsFromBackup(fileName, restoreTemp))
            {
                DisplayOperationMsg(ResUI.LocalRestoreInvalidZipTips);
                return;
            }

            // A restored backup must never silently move DNS outside the VPN.
            if (!ApplySafeRestoredNetworkDefaults(restoreTemp))
            {
                DisplayOperationMsg("Копия проверена, но безопасные сетевые параметры применить не удалось.");
                return;
            }

            await AppManager.Instance.AppExitAsync(false);
            await SQLiteHelper.Instance.DisposeDbConnectionAsync();

            var toPath = Utils.GetConfigPath();
            if (Directory.Exists(toPath))
            {
                Directory.Delete(toPath, true);
            }

            CopyPersistentConfigTree(restoreTemp, toPath);

            if (Utils.IsWindows())
            {
                ProcUtils.RebootAsAdmin(false);
            }
            else if (Utils.UpgradeAppExists(out var upgradeFileName))
            {
                _ = ProcUtils.ProcessStart(upgradeFileName, Global.RebootAs, Utils.StartupPath());
            }

            AppManager.Instance.Shutdown(true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("BackupAndRestoreViewModel.LocalRestoreReplace", ex);
            DisplayOperationMsg("Не удалось заменить текущее состояние. Страховочная копия сохранена в разделе резервных копий.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(restoreTemp))
                {
                    Directory.Delete(restoreTemp, true);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("BackupAndRestoreViewModel.CleanupRestoreTemp", ex);
            }
        }
    }



    private static bool ApplySafeRestoredNetworkDefaults(string destinationRoot)
    {
        try
        {
            var configPath = Path.Combine(destinationRoot, Global.ConfigFileName);
            if (!File.Exists(configPath))
            {
                return false;
            }

            var root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
            if (root == null)
            {
                return false;
            }

            if (root["SgQuickSettingsItem"] is not JsonObject quickSettings)
            {
                quickSettings = new JsonObject();
                root["SgQuickSettingsItem"] = quickSettings;
            }

            quickSettings["DnsThroughTun"] = true;

            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(configPath, json, new UTF8Encoding(false));
            Logging.SaveLog("Backup restore safety: DNS route reset to VPN/TUN.");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("BackupAndRestoreViewModel.ApplySafeRestoredNetworkDefaults", ex);
            return false;
        }
    }

    private static bool ExtractGuiConfigsFromBackup(string fileName, string destinationRoot)
    {
        try
        {
            var rootFullPath = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(fileName);
            if (!TryGetGuiConfigsPrefix(archive, out var guiConfigsPrefix))
            {
                return false;
            }

            var extractedFiles = 0;
            foreach (var entry in archive.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (!normalized.StartsWith(guiConfigsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = normalized[guiConfigsPrefix.Length..];
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                if (relative.Contains("../", StringComparison.Ordinal)
                    || relative.StartsWith("/", StringComparison.Ordinal)
                    || Path.IsPathRooted(relative))
                {
                    return false;
                }

                var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destinationPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(destinationPath, true);
                extractedFiles++;
            }

            return extractedFiles > 0;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("BackupAndRestoreViewModel.ExtractGuiConfigsFromBackup", ex);
            return false;
        }
    }

    public static bool TryValidateBackupArchive(
        ZipArchive archive,
        out string guiConfigsPrefix)
    {
        guiConfigsPrefix = string.Empty;
        if (!TryGetGuiConfigsPrefix(archive, out guiConfigsPrefix))
        {
            return false;
        }

        var fileEntries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToList();
        if (fileEntries.Count > 10_000)
        {
            return false;
        }

        long totalUncompressedBytes = 0;
        foreach (var entry in fileEntries)
        {
            if (entry.Length < 0
                || entry.Length > 2L * 1024 * 1024 * 1024
                || totalUncompressedBytes > (2L * 1024 * 1024 * 1024) - entry.Length)
            {
                return false;
            }

            totalUncompressedBytes += entry.Length;
            using var stream = entry.Open();
            stream.CopyTo(Stream.Null);
        }

        return true;
    }

    public static bool TryGetGuiConfigsPrefix(ZipArchive archive, out string prefix)
    {
        prefix = string.Empty;
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.Contains("../", StringComparison.Ordinal)
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(normalized))
            {
                return false;
            }

            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && string.Equals(parts[0], "guiConfigs", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add("guiConfigs/");
            }
            else if (parts.Length >= 3
                && string.Equals(parts[1], "guiConfigs", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add($"{parts[0]}/guiConfigs/");
            }
        }

        if (prefixes.Count != 1)
        {
            return false;
        }

        var detectedPrefix = prefixes.Single();
        var hasPayload = archive.Entries.Any(entry =>
        {
            var normalized = entry.FullName.Replace('\\', '/');
            return normalized.StartsWith(detectedPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(normalized[detectedPrefix.Length..])
                && !string.IsNullOrEmpty(entry.Name);
        });

        if (!hasPayload)
        {
            return false;
        }

        prefix = detectedPrefix;
        return true;
    }


    private static void CopyPersistentConfigTree(string sourceRoot, string destinationRoot)
    {
        var source = new DirectoryInfo(sourceRoot);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {source.FullName}");
        }

        Directory.CreateDirectory(destinationRoot);
        CopyPersistentConfigDirectory(source, source.FullName, destinationRoot);
    }

    private static void CopyPersistentConfigDirectory(
        DirectoryInfo sourceDirectory,
        string sourceRoot,
        string destinationRoot)
    {
        foreach (var file in sourceDirectory.GetFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var relativeFile = Path.GetRelativePath(sourceRoot, file.FullName);
            var destinationFile = Path.Combine(destinationRoot, relativeFile);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            file.CopyTo(destinationFile, true);
        }

        foreach (var subDirectory in sourceDirectory.GetDirectories())
        {
            if ((subDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var relativeDirectory = Path.GetRelativePath(sourceRoot, subDirectory.FullName)
                .Replace('\\', '/');

            // Runtime files are recreated by AmneziaWG and must not be restored as user state.
            if (relativeDirectory.Equals("sg-awg/runtime", StringComparison.OrdinalIgnoreCase)
                || relativeDirectory.StartsWith("sg-awg/runtime/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyPersistentConfigDirectory(subDirectory, sourceRoot, destinationRoot);
        }
    }

    private async Task<bool> CreateZipFileFromDirectory(string fileName)
    {
        if (fileName.IsNullOrEmpty())
        {
            return false;
        }

        var configDir = Utils.GetConfigPath();
        var configDirZipTemp = Utils.GetTempPath($"SGClient_{DateTime.Now:yyyyMMddHHmmss}");
        var configDirTemp = Path.Combine(configDirZipTemp, _guiConfigs);

        try
        {
            CopyPersistentConfigTree(configDir, configDirTemp);
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            // Do not include the temporary SGClient_... wrapper directory.
            // New archives therefore start directly with guiConfigs/.
            ZipFile.CreateFromDirectory(
                configDirZipTemp,
                fileName,
                CompressionLevel.SmallestSize,
                includeBaseDirectory: false);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("BackupAndRestoreViewModel.CreateZipFileFromDirectory", ex);
            return await Task.FromResult(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(configDirZipTemp))
                {
                    Directory.Delete(configDirZipTemp, true);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("BackupAndRestoreViewModel.CleanupBackupTemp", ex);
            }
        }
    }
}
