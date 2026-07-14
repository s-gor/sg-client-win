namespace ServiceLib.Manager;

[SupportedOSPlatform("windows")]
public sealed class SgKillSwitchManager
{
    private static readonly Lazy<SgKillSwitchManager> _instance = new(() => new SgKillSwitchManager());
    public static SgKillSwitchManager Instance => _instance.Value;

    private const string RuleGroup = "SG Client Kill Switch";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string StatePath => Utils.GetConfigPath("sg-kill-switch-firewall.json");

    public bool IsEmergencyBlockActive => Utils.IsWindows() && File.Exists(StatePath);

    public async Task ActivateEmergencyBlockAsync(IEnumerable<string> endpointHosts, bool allowLocalNetwork)
    {
        if (!Utils.IsWindows() || IsEmergencyBlockActive)
        {
            return;
        }
        if (!Utils.IsAdministrator())
        {
            throw new InvalidOperationException("Для включения Kill Switch нужны права администратора.");
        }

        await _gate.WaitAsync();
        try
        {
            if (IsEmergencyBlockActive)
            {
                return;
            }

            var allowedAddresses = await ResolveAllowedAddressesAsync(endpointHosts);
            foreach (var dns in GetPhysicalDnsAddresses())
            {
                allowedAddresses.Add(dns);
            }

            var programs = GetAllowedPrograms()
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine($"$statePath = '{Ps(StatePath)}'");
            script.AppendLine($"$group = '{Ps(RuleGroup)}'");
            script.AppendLine("$profiles = Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Action = $_.DefaultOutboundAction.ToString() } }");
            script.AppendLine("$profiles | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath -Encoding UTF8");
            script.AppendLine("Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue");

            var index = 0;
            foreach (var program in programs)
            {
                index++;
                script.AppendLine($"New-NetFirewallRule -DisplayName 'SG Client KS Program {index}' -Group $group -Direction Outbound -Action Allow -Program '{Ps(program)}' -Profile Any | Out-Null");
            }

            if (allowedAddresses.Count > 0)
            {
                script.AppendLine($"New-NetFirewallRule -DisplayName 'SG Client KS Endpoints and DNS' -Group $group -Direction Outbound -Action Allow -RemoteAddress '{Ps(string.Join(',', allowedAddresses))}' -Profile Any | Out-Null");
            }
            if (allowLocalNetwork)
            {
                script.AppendLine("New-NetFirewallRule -DisplayName 'SG Client KS Local network' -Group $group -Direction Outbound -Action Allow -RemoteAddress LocalSubnet -Profile Any | Out-Null");
            }

            script.AppendLine("Get-NetFirewallProfile | Set-NetFirewallProfile -DefaultOutboundAction Block");
            await RunPowerShellAsync(script.ToString(), TimeSpan.FromSeconds(35));
            Logging.SaveLog($"SG Client Kill Switch emergency block enabled; allowed addresses={allowedAddresses.Count}; programs={programs.Count}");
        }
        catch
        {
            try
            {
                await DeactivateCoreAsync();
            }
            catch
            {
                // Preserve the original failure. The reset script in the package is a final recovery path.
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeactivateEmergencyBlockAsync()
    {
        if (!Utils.IsWindows())
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await DeactivateCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeactivateCoreAsync()
    {
        if (!Utils.IsAdministrator())
        {
            return;
        }

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$statePath = '{Ps(StatePath)}'");
        script.AppendLine($"$group = '{Ps(RuleGroup)}'");
        script.AppendLine("Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue");
        script.AppendLine("if (Test-Path -LiteralPath $statePath) {");
        script.AppendLine("  $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json");
        script.AppendLine("  @($saved) | ForEach-Object { Set-NetFirewallProfile -Name $_.Name -DefaultOutboundAction $_.Action }");
        script.AppendLine("  Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue");
        script.AppendLine("}");
        await RunPowerShellAsync(script.ToString(), TimeSpan.FromSeconds(25));
        Logging.SaveLog("SG Client Kill Switch emergency block disabled");
    }

    private static async Task<HashSet<string>> ResolveAllowedAddressesAsync(IEnumerable<string> hosts)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in hosts.Where(item => item.IsNotEmpty()))
        {
            var host = NormalizeHost(raw);
            if (host.IsNullOrEmpty())
            {
                continue;
            }
            if (IPAddress.TryParse(host, out var literal))
            {
                result.Add(literal.ToString());
                continue;
            }
            try
            {
                foreach (var address in await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(8)))
                {
                    result.Add(address.ToString());
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"Resolve Kill Switch endpoint {host}", ex);
            }
        }
        return result;
    }

    private static IEnumerable<string> GetPhysicalDnsAddresses()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up
                || network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }
            IPInterfaceProperties properties;
            try
            {
                properties = network.GetIPProperties();
            }
            catch
            {
                continue;
            }
            foreach (var dns in properties.DnsAddresses)
            {
                if (!IPAddress.IsLoopback(dns) && !dns.Equals(IPAddress.Any) && !dns.Equals(IPAddress.IPv6Any))
                {
                    yield return dns.ToString();
                }
            }
        }
    }

    private static IEnumerable<string> GetAllowedPrograms()
    {
        if (Environment.ProcessPath.IsNotEmpty())
        {
            yield return Environment.ProcessPath!;
        }
        var root = Utils.StartupPath();
        yield return Path.Combine(root, "bin", "xray", "xray.exe");
        yield return Path.Combine(root, "bin", "sing_box", "sing-box.exe");
        yield return Path.Combine(root, "bin", "awg", "amneziawg.exe");
        yield return Path.Combine(root, "bin", "awg", "awg.exe");
    }

    private static string NormalizeHost(string value)
    {
        var host = value.Trim();
        if (host.StartsWith('[') && host.Contains(']'))
        {
            return host[1..host.IndexOf(']')];
        }
        if (Uri.TryCreate($"dummy://{host}", UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }
        return host;
    }

    private static string Ps(string value) => value.Replace("'", "''");

    private static async Task RunPowerShellAsync(string script, TimeSpan timeout)
    {
        var scriptPath = Utils.GetConfigPath($"sg-kill-switch-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false));
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(timeout);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Windows Firewall: {error.Trim()} {output.Trim()}".Trim());
            }
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }
}
