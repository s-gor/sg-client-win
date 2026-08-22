namespace ServiceLib.Services;

public class ProcessService : IDisposable
{
    private readonly Process _process;
    private readonly Func<bool, string, Task>? _updateFunc;
    private bool _isDisposed;
    private readonly string? _cleanupDirectory;

    public int Id => _process.Id;
    public IntPtr Handle => _process.Handle;
    public bool HasExited => _process.HasExited;

    public ProcessService(
        string fileName,
        string arguments,
        string workingDirectory,
        bool displayLog,
        bool redirectInput,
        Dictionary<string, string>? environmentVars,
        Func<bool, string, Task>? updateFunc,
        string? cleanupDirectory = null)
    {
        _updateFunc = updateFunc;
        _cleanupDirectory = cleanupDirectory;

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = displayLog,
                RedirectStandardError = displayLog,
                CreateNoWindow = true,
                StandardOutputEncoding = displayLog ? Encoding.UTF8 : null,
                StandardErrorEncoding = displayLog ? Encoding.UTF8 : null,
            },
            EnableRaisingEvents = true
        };

        if (environmentVars != null)
        {
            foreach (var kv in environmentVars)
            {
                _process.StartInfo.Environment[kv.Key] = kv.Value;
            }
        }

        if (displayLog)
        {
            RegisterEventHandlers();
        }
    }

    public async Task StartAsync(string pwd = null)
    {
        _process.Start();

        if (_process.StartInfo.RedirectStandardOutput)
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        if (_process.StartInfo.RedirectStandardInput)
        {
            await Task.Delay(10);
            await _process.StandardInput.WriteLineAsync(pwd);
        }
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (_process.HasExited)
        {
            TryCleanupDirectory();
            return;
        }

        var stopTimeout = timeout ?? TimeSpan.FromSeconds(3);
        try
        {
            if (_process.StartInfo.RedirectStandardOutput)
            {
                try
                {
                    _process.CancelOutputRead();
                }
                catch { }
                try
                {
                    _process.CancelErrorRead();
                }
                catch { }
            }

            // SG_RECOVERY_109: a VPN core may spawn helper/transport processes.
            // Kill the complete tree on every OS; a plain Kill() on Windows can
            // leave descendants alive and make the next connect/disconnect hang.
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"Process tree kill failed: pid={Id}", ex);
                try
                {
                    _process.Kill();
                }
                catch (Exception fallbackEx)
                {
                    Logging.SaveLog($"Process kill fallback failed: pid={Id}", fallbackEx);
                }
            }

            if (!_process.HasExited)
            {
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(stopTimeout);
                }
                catch (TimeoutException)
                {
                    Logging.SaveLog($"Process stop timeout: pid={Id}; timeout={stopTimeout.TotalSeconds:0.0}s");
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Process stop failed: pid={Id}", ex);
            if (_updateFunc is not null)
            {
                await _updateFunc.Invoke(true, ex.Message);
            }
        }
        finally
        {
            TryCleanupDirectory();
        }
    }

    private void TryCleanupDirectory()
    {
        if (_cleanupDirectory.IsNullOrEmpty())
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                return;
            }
            if (Directory.Exists(_cleanupDirectory))
            {
                Directory.Delete(_cleanupDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Temporary process directory cleanup failed: {_cleanupDirectory}", ex);
        }
    }

    private void RegisterEventHandlers()
    {
        void dataHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data.IsNotEmpty())
            {
                _ = _updateFunc?.Invoke(false, e.Data + Environment.NewLine);
            }
        }

        _process.OutputDataReceived += dataHandler;
        _process.ErrorDataReceived += dataHandler;

        _process.Exited += (s, e) =>
        {
            try
            {
                _process.OutputDataReceived -= dataHandler;
                _process.ErrorDataReceived -= dataHandler;
            }
            catch
            {
            }
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.CancelOutputRead();
                }
                catch { }
                try
                {
                    _process.CancelErrorRead();
                }
                catch { }

                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try
                    {
                        _process.Kill();
                    }
                    catch { }
                }
            }

            TryCleanupDirectory();
            _process.Dispose();
        }
        catch (Exception ex)
        {
            _updateFunc?.Invoke(true, ex.Message);
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
