namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private void GenLog()
    {
        try
        {
            if (_config.CoreBasicItem.LogEnabled)
            {
                var dtNow = DateTime.Now;

                // SG Connections correlates an access-log destination with Xray's
                // per-connection sniffing and DNS information. Those records are
                // emitted at info level, so a file log explicitly enabled by the
                // user must be at least info. Debug remains debug.
                _coreConfig.log.loglevel = GetConnectionsLogLevel(_config.CoreBasicItem.Loglevel);
                _coreConfig.log.dnsLog = true;
                _coreConfig.log.access = Utils.GetLogPath($"Vaccess_{dtNow:yyyy-MM-dd}.txt");
                _coreConfig.log.error = Utils.GetLogPath($"Verror_{dtNow:yyyy-MM-dd}.txt");
            }
            else
            {
                _coreConfig.log.loglevel = _config.CoreBasicItem.Loglevel;
                _coreConfig.log.dnsLog = false;
                _coreConfig.log.access = null;
                _coreConfig.log.error = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private static string GetConnectionsLogLevel(string? configured)
    {
        return string.Equals(configured, "debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : "info";
    }
}
