namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private void GenStatistic()
    {
        // SG Client always enables inbound and outbound counters. In global TUN
        // mode the TUN inbound counter is the reliable source for UDP/QUIC
        // traffic (including YouTube/googlevideo), while outbound counters
        // remain available for split-routing diagnostics. The listener is
        // bound to loopback.
        Metrics4Ray metricsObj = new();
        Policy4Ray policyObj = new();
        SystemPolicy4Ray policySystemSetting = new();

        _coreConfig.stats = new Stats4Ray();

        metricsObj.listen = $"{Global.Loopback}:{AppManager.Instance.StatePort}";
        _coreConfig.metrics = metricsObj;

        policySystemSetting.statsInboundDownlink = true;
        policySystemSetting.statsInboundUplink = true;
        policySystemSetting.statsOutboundDownlink = true;
        policySystemSetting.statsOutboundUplink = true;
        policyObj.system = policySystemSetting;
        _coreConfig.policy = policyObj;
    }
}
