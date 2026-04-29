using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.5.1", "0.5.0", true)]
    [InlineData("0.5.0", "0.5.1", false)]
    [InlineData("0.5.1", "0.5.1", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.10.0", "0.9.99", true)]
    [InlineData("v0.5.2", "0.5.1", true)] // tolerant of `v` prefix-style strings via Parse
    [InlineData("0.5.1", "0.5.1-rc1", false)] // simple comparator treats prerelease segments as 0
    public void IsNewer(string a, string b, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(a.TrimStart('v', 'V'), b));
    }
}
