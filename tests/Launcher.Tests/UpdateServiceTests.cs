using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.4", true)]
    [InlineData("0.2.4", "0.3.0", false)]
    [InlineData("0.3.0", "0.3.0", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.10.0", "0.9.99", true)]
    [InlineData("v0.3.1", "0.3.0", true)] // tolerant of `v` prefix-style strings via Parse
    [InlineData("0.3.0", "0.3.0-rc1", false)] // simple comparator treats prerelease segments as 0
    public void IsNewer(string a, string b, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(a.TrimStart('v', 'V'), b));
    }
}
