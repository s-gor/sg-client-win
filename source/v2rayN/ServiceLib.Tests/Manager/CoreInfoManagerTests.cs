using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests.Manager;

public class CoreInfoManagerTests
{
    [Fact]
    public void UpdateWhitelist_ShouldContainOnlyShippedReplaceableCores()
    {
        var types = CoreInfoManager.Instance.GetCheckUpdateCoreTypes();

        types.Should().Equal(ECoreType.Xray, ECoreType.sing_box);
        types.Should().NotContain(ECoreType.mihomo);
        CoreInfoManager.Instance.IsCheckUpdateSupported(ECoreType.Xray).Should().BeTrue();
        CoreInfoManager.Instance.IsCheckUpdateSupported(ECoreType.sing_box).Should().BeTrue();
        CoreInfoManager.Instance.IsCheckUpdateSupported(ECoreType.mihomo).Should().BeFalse();
    }
}
