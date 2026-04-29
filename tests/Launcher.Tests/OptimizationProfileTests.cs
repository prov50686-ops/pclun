using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class OptimizationProfileTests
{
    [Fact]
    public void GetJvmArgs_StartsWith_XmsAndXmx()
    {
        var args = OptimizationProfile.GetJvmArgs(2048);
        Assert.Contains("-Xms2048M", args);
        Assert.Contains("-Xmx2048M", args);
    }

    [Fact]
    public void GetJvmArgs_IncludesG1GC()
    {
        var args = OptimizationProfile.GetJvmArgs(2048);
        Assert.Contains("-XX:+UseG1GC", args);
        Assert.Contains("-XX:+AlwaysPreTouch", args);
    }

    [Fact]
    public void GetJvmArgs_AppendsCustomArgs()
    {
        var args = OptimizationProfile.GetJvmArgs(2048, "-XX:+UseShenandoahGC -Dfoo=bar");
        Assert.Contains("-XX:+UseShenandoahGC", args);
        Assert.Contains("-Dfoo=bar", args);
    }

    [Fact]
    public void GetJvmArgs_HandlesEmptyCustomArgs()
    {
        var argsEmpty = OptimizationProfile.GetJvmArgs(1024, "");
        var argsNull = OptimizationProfile.GetJvmArgs(1024);
        Assert.Equal(argsEmpty.Count, argsNull.Count);
    }
}
