namespace ServiceLib.Manager;

/// <summary>
/// Core process processing class
/// </summary>
public class CoreManager
{
    private static readonly Lazy<CoreManager> _instance = new(() => new());
    public static CoreManager Instance => _instance.Value;
    private Config _config;
    [SupportedOSPlatform("windows")]
    private WindowsJobService? _processJob;
    private ProcessService? _processService;
    private ProcessService? _processPreService;
    private bool _linuxSudo = false;
    private Func<bool, string, Task>? _updateFunc;
    private const string _tag = "CoreHandler";

    public bool IsCoreRunning => _processService is { HasExited: false };

    public async Task Init(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        //Copy the bin folder to the storage location (for init)
        if (Environment.GetEnvironmentVariable(Global.LocalAppData) == "1")
        {
            var fromPath = Utils.GetBaseDirectory("bin");
            var toPath = Utils.GetBinPath("");
            if (fromPath != toPath)
            {
                FileUtils.CopyDirectory(fromPath, toPath, true, false);
            }
        }

        if (Utils.IsNonWindows())
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
            foreach (var it in coreInfo)
            {
                if (it.CoreType == ECoreType.v2rayN)
                {
                    if (Utils.UpgradeAppExists(out var upgradeFileName))
                    {
                        await Utils.SetLinuxChmod(upgradeFileName);
                    }
                    continue;
                }

                foreach (var name in it.CoreExes)
                {
                    var exe = Utils.GetBinPath(Utils.GetExeName(name), it.CoreType.ToString());
                    if (File.Exists(exe))
                    {
                        await Utils.SetLinuxChmod(exe);
                    }
                }
            }
        }
    }

    /// <param name="mainContext">Resolved main context (with pre-socks ports already merged if applicable).</param>
    /// <param name="preContext">Optional pre-socks context passed to <see cref="CoreStartPreService"/>.</param>
    public async Task LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext)
    {
        if (mainContext == null)
        {
            await UpdateFunc(false, ResUI.CheckServerSettings);
            return;
        }

        var node = mainContext.Node;
        var fileName = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        Logging.SaveLog($"Core config generation begins: core={mainContext.RunCoreType}; profile={node.GetSummary()}; path={fileName}");
        var result = await CoreConfigHandler.GenerateClientConfig(mainContext, fileName);
        Logging.SaveLog($"Core config generation completed: success={result.Success}; exists={File.Exists(fileName)}; path={fileName}; message={result.Msg}");
        if (result.Success != true)
        {
            await UpdateFunc(true, result.Msg);
            return;
        }

        if (!await ValidateGeneratedXrayConfig(mainContext, fileName))
        {
            return;
        }

        await UpdateFunc(false, $"{node.GetSummary()}");
        await UpdateFunc(false, $"{Utils.GetRuntimeInfo()}");
        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await CoreStop();
        await Task.Delay(100);

        if (Utils.IsWindows() && _config.TunModeItem.EnableTun)
        {
            await Task.Delay(100);
            await WindowsUtils.RemoveTunDevice();
        }

        await CoreStart(mainContext);
        await CoreStartPreService(preContext);

        AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;

        if (_processService != null)
        {
            await UpdateFunc(true, $"{node.GetSummary()}");
        }
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(List<ServerTestItem> selecteds)
    {
        var coreType = selecteds.FirstOrDefault()?.CoreType == ECoreType.sing_box ? ECoreType.sing_box : ECoreType.Xray;
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, configPath, selecteds, coreType);
        await UpdateFunc(false, result.Msg);
        if (result.Success != true)
        {
            return null;
        }

        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await UpdateFunc(false, configPath);

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(ServerTestItem testItem)
    {
        var node = await AppManager.Instance.GetProfileItem(testItem.IndexId);
        if (node is null)
        {
            return null;
        }

        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var (context, _) = await CoreConfigContextBuilder.Build(_config, node);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, context, testItem, configPath);
        if (result.Success != true)
        {
            return null;
        }

        var coreType = context.RunCoreType;
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task CoreStop()
    {
        try
        {
            if (_linuxSudo)
            {
                await CoreAdminManager.Instance.KillProcessAsLinuxSudo();
                _linuxSudo = false;
            }

            if (_processService != null)
            {
                await _processService.StopAsync();
                _processService.Dispose();
                _processService = null;
            }

            if (_processPreService != null)
            {
                await _processPreService.StopAsync();
                _processPreService.Dispose();
                _processPreService = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #region Private

    private async Task CoreStart(CoreConfigContext context)
    {
        var node = context.Node;
        var coreType = AppManager.Instance.GetCoreType(node, node.ConfigType);
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);

        var displayLog = node.ConfigType != EConfigType.Custom || node.DisplayLog;
        var proc = await RunProcess(coreInfo, Global.CoreConfigFileName, displayLog, true);
        if (proc is null)
        {
            return;
        }
        _processService = proc;
    }

    private async Task CoreStartPreService(CoreConfigContext? preContext)
    {
        if (_processService is { HasExited: false } && preContext != null)
        {
            var preCoreType = preContext?.Node?.CoreType ?? ECoreType.sing_box;
            var fileName = Utils.GetBinConfigPath(Global.CorePreConfigFileName);
            var result = await CoreConfigHandler.GenerateClientConfig(preContext, fileName);
            if (result.Success)
            {
                var coreInfo = CoreInfoManager.Instance.GetCoreInfo(preCoreType);
                var proc = await RunProcess(coreInfo, Global.CorePreConfigFileName, true, true);
                if (proc is null)
                {
                    return;
                }
                _processPreService = proc;
            }
        }
    }

    private async Task<bool> ValidateGeneratedXrayConfig(CoreConfigContext context, string configPath)
    {
        if (context.RunCoreType != ECoreType.Xray)
        {
            return true;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray);
        var executable = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var message);
        if (executable.IsNullOrEmpty() || coreInfo is null)
        {
            await UpdateFunc(true, message);
            return false;
        }

        string? validationPath = null;
        var reportPath = Utils.GetBinConfigPath("xray-config-test.log");
        var removedTunInbounds = 0;
        Process? process = null;

        try
        {
            // Xray 26.5.9 implements `run -test` by loading the config and calling
            // core.New() before it prints "Configuration OK.". A live TUN inbound
            // can therefore try to create the same Wintun adapter during validation.
            // Test an exact temporary copy with only TUN inbounds removed. Outbounds,
            // routing, REALITY/XHTTP and FinalMask stay unchanged and are still tested.
            (validationPath, removedTunInbounds) = await CreateXrayValidationConfig(configPath);

            Logging.SaveLog(
                $"Xray config test begins: original={configPath}; validation={validationPath}; " +
                $"removedTunInbounds={removedTunInbounds}; currentCoreRunning={IsCoreRunning}");

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Utils.GetBinConfigPath(),
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
            startInfo.ArgumentList.Add(validationPath);

            foreach (var pair in coreInfo.Environment)
            {
                if (pair.Value is not null)
                {
                    startInfo.Environment[pair.Key] = string.Format(pair.Value, validationPath);
                }
            }

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                var report = BuildXrayValidationReport(
                    configPath,
                    validationPath,
                    removedTunInbounds,
                    null,
                    false,
                    string.Empty,
                    "Не удалось запустить процесс проверки Xray.");
                await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(false));
                await UpdateFunc(true, "Не удалось запустить проверку конфигурации Xray. Подробности: binConfigs\\xray-config-test.log");
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                    catch (Exception killException)
                    {
                        Logging.SaveLog(_tag, killException);
                    }
                }
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            int? exitCode = timedOut ? null : process.ExitCode;
            var reportText = BuildXrayValidationReport(
                configPath,
                validationPath,
                removedTunInbounds,
                exitCode,
                timedOut,
                stdout,
                stderr);
            await File.WriteAllTextAsync(reportPath, reportText, new UTF8Encoding(false));

            Logging.SaveLog(
                $"Xray config test completed: exitCode={(exitCode?.ToString() ?? "TIMEOUT")}; " +
                $"removedTunInbounds={removedTunInbounds}; report={reportPath}");

            if (timedOut)
            {
                await UpdateFunc(true, "Проверка конфигурации Xray превысила 20 секунд. Подробности: binConfigs\\xray-config-test.log");
                return false;
            }

            if (exitCode != 0)
            {
                var output = string.Join(
                    Environment.NewLine,
                    new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
                var summary = GetXrayValidationErrorSummary(output, exitCode ?? -1);
                await UpdateFunc(
                    true,
                    $"Конфигурация Xray отклонена: {summary}{Environment.NewLine}" +
                    "Подробности: binConfigs\\xray-config-test.log");
                return false;
            }

            await UpdateFunc(false, $"Проверка конфигурации Xray: OK (TUN исключён только из тестовой копии, удалено: {removedTunInbounds})");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            var report = BuildXrayValidationReport(
                configPath,
                validationPath ?? string.Empty,
                removedTunInbounds,
                null,
                false,
                string.Empty,
                ex.ToString());
            try
            {
                await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(false));
            }
            catch (Exception reportException)
            {
                Logging.SaveLog(_tag, reportException);
            }
            await UpdateFunc(true, $"Ошибка проверки конфигурации Xray: {ex.Message}. Подробности: binConfigs\\xray-config-test.log");
            return false;
        }
        finally
        {
            process?.Dispose();
            if (validationPath.IsNotEmpty())
            {
                try
                {
                    File.Delete(validationPath);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }
        }
    }

    private static async Task<(string Path, int RemovedTunInbounds)> CreateXrayValidationConfig(string configPath)
    {
        var content = await File.ReadAllTextAsync(configPath, Encoding.UTF8);
        var root = JsonUtils.ParseJson(content) as JsonObject
                   ?? throw new InvalidDataException("Сгенерированный config.json не является корректным JSON-объектом.");

        var removedTunInbounds = 0;
        if (root["inbounds"] is JsonArray inbounds)
        {
            for (var index = inbounds.Count - 1; index >= 0; index--)
            {
                if (inbounds[index] is not JsonObject inbound)
                {
                    continue;
                }

                var protocol = inbound["protocol"]?.GetValue<string>();
                if (!string.Equals(protocol, "tun", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                inbounds.RemoveAt(index);
                removedTunInbounds++;
            }
        }

        var validationPath = Utils.GetBinConfigPath($"xray-config-test-{Utils.GetGuid(false)}.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        await File.WriteAllTextAsync(validationPath, root.ToJsonString(jsonOptions), new UTF8Encoding(false));
        return (validationPath, removedTunInbounds);
    }

    private static string BuildXrayValidationReport(
        string originalPath,
        string validationPath,
        int removedTunInbounds,
        int? exitCode,
        bool timedOut,
        string stdout,
        string stderr)
    {
        var report = new StringBuilder();
        report.AppendLine($"Timestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        report.AppendLine($"OriginalConfig={originalPath}");
        report.AppendLine($"ValidationConfig={validationPath}");
        report.AppendLine($"RemovedTunInbounds={removedTunInbounds}");
        report.AppendLine($"TimedOut={(timedOut ? "YES" : "NO")}");
        report.AppendLine($"ExitCode={(exitCode?.ToString() ?? "N/A")}");
        report.AppendLine();
        report.AppendLine("--- STDOUT ---");
        report.AppendLine(stdout?.TrimEnd() ?? string.Empty);
        report.AppendLine();
        report.AppendLine("--- STDERR ---");
        report.AppendLine(stderr?.TrimEnd() ?? string.Empty);
        return report.ToString();
    }

    private static string GetXrayValidationErrorSummary(string output, int exitCode)
    {
        var lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Xray ", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("A unified platform", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("Configuration OK.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var summary = lines.LastOrDefault(line => line.Contains("Failed to start", StringComparison.OrdinalIgnoreCase))
                      ?? lines.LastOrDefault()
                      ?? $"Xray завершил проверку с кодом {exitCode}";

        const int maxLength = 320;
        return summary.Length <= maxLength ? summary : summary[..maxLength] + "…";
    }

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }

    #endregion Private

    #region Process

    private async Task<ProcessService?> RunProcess(CoreInfo? coreInfo, string configPath, bool displayLog, bool mayNeedSudo)
    {
        var fileName = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var msg);
        if (fileName.IsNullOrEmpty())
        {
            await UpdateFunc(false, msg);
            return null;
        }

        try
        {
            if (mayNeedSudo
                && _config.TunModeItem.EnableTun
                && (coreInfo.CoreType is ECoreType.sing_box or ECoreType.mihomo)
                && Utils.IsNonWindows())
            {
                _linuxSudo = true;
                await CoreAdminManager.Instance.Init(_config, _updateFunc);
                return await CoreAdminManager.Instance.RunProcessAsLinuxSudo(fileName, coreInfo, configPath);
            }

            return await RunProcessNormal(fileName, coreInfo, configPath, displayLog);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(mayNeedSudo, ex.Message);
            return null;
        }
    }

    private async Task<ProcessService?> RunProcessNormal(string fileName, CoreInfo? coreInfo, string configPath, bool displayLog)
    {
        var environmentVars = new Dictionary<string, string>();
        foreach (var kv in coreInfo.Environment)
        {
            environmentVars[kv.Key] = string.Format(kv.Value, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);
        }

        var procService = new ProcessService(
            fileName: fileName,
            arguments: string.Format(coreInfo.Arguments, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath),
            workingDirectory: Utils.GetBinConfigPath(),
            displayLog: displayLog,
            redirectInput: false,
            environmentVars: environmentVars,
            updateFunc: _updateFunc
        );

        await procService.StartAsync();

        await Task.Delay(100);

        if (procService is null or { HasExited: true })
        {
            throw new Exception(ResUI.FailedToRunCore);
        }
        AddProcessJob(procService.Handle);

        return procService;
    }

    private void AddProcessJob(nint processHandle)
    {
        if (Utils.IsWindows())
        {
            _processJob ??= new();
            try
            {
                _processJob?.AddProcess(processHandle);
            }
            catch { }
        }
    }

    #endregion Process
}
