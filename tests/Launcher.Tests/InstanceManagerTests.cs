using System.IO;
using System.Linq;
using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class InstanceManagerTests
{
    [Fact]
    public void List_AlwaysContainsDefault()
    {
        var list = InstanceManager.List();
        Assert.NotEmpty(list);
    }

    [Fact]
    public void Default_HasIsDefaultFlag()
    {
        var d = InstanceManager.Default();
        Assert.True(d.IsDefault);
        Assert.Equal("1.16.5", d.McVersion);
    }

    [Fact]
    public void GameDirOf_DefaultUsesPathsGameDir()
    {
        var d = InstanceManager.Default();
        Assert.Equal(Paths.GameDir, InstanceManager.GameDirOf(d));
    }
}
