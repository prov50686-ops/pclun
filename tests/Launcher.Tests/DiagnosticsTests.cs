using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void RecommendProfile_LowRam_PicksPotato()
    {
        var gpu = new Diagnostics.GpuInfo { Name = "Intel HD Graphics 4000" };
        Assert.Equal(0, Diagnostics.RecommendProfile(4000, gpu));
    }

    [Fact]
    public void RecommendProfile_HighEndGpu_PicksQuality()
    {
        var gpu = new Diagnostics.GpuInfo { Name = "NVIDIA GeForce RTX 4090" };
        Assert.Equal(3, Diagnostics.RecommendProfile(32000, gpu));
    }

    [Fact]
    public void RecommendProfile_MidRange_PicksBalanced()
    {
        var gpu = new Diagnostics.GpuInfo { Name = "NVIDIA GTX 1060" };
        Assert.Equal(2, Diagnostics.RecommendProfile(16000, gpu));
    }

    [Fact]
    public void NetworkJvmFlags_IncludesIPv4Pref()
    {
        var flags = Diagnostics.NetworkJvmFlags();
        Assert.Contains("-Djava.net.preferIPv4Stack=true", flags);
    }

    [Fact]
    public void DetectGpu_AlwaysReturnsSomething()
    {
        var info = Diagnostics.DetectGpu();
        Assert.NotNull(info.Name);
    }
}
