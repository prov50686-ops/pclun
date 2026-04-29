using System.Threading.Tasks;
using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class ServerPingerTests
{
    [Fact]
    public async Task Ping_NonExistentHost_FailsGracefully()
    {
        var r = await ServerPinger.PingAsync("server-that-doesnt-exist.invalid.tld", timeoutMs: 1000);
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public async Task Ping_LocalUnused_TimesOut()
    {
        var r = await ServerPinger.PingAsync("127.0.0.1:65500", timeoutMs: 500);
        Assert.False(r.Ok);
    }
}
