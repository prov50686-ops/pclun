using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class StreamerModeTests
{
    [Fact]
    public void Disabled_PassesThrough()
    {
        StreamerMode.Enabled = false;
        Assert.Equal("Steve", StreamerMode.MaskNick("Steve"));
        Assert.Equal("play.hypixel.net", StreamerMode.MaskAddress("play.hypixel.net"));
    }

    [Fact]
    public void Enabled_MasksValues()
    {
        StreamerMode.Enabled = true;
        Assert.NotEqual("Steve", StreamerMode.MaskNick("Steve"));
        Assert.NotEqual("play.hypixel.net", StreamerMode.MaskAddress("play.hypixel.net"));
        Assert.Contains("•", StreamerMode.MaskToken("abc-token-12345"));
        StreamerMode.Enabled = false;
    }
}
