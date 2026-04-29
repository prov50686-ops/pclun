using PcLun.Services;
using Xunit;

namespace PcLun.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void Analyze_OutOfMemory_ReturnsRamSuggestion()
    {
        var (diag, sug) = CrashAnalyzer.Analyze(
            "java.lang.OutOfMemoryError: Java heap space\n  at net.minecraft...");
        Assert.Contains("памяти", diag);
        Assert.Contains("RAM", sug);
    }

    [Fact]
    public void Analyze_BadJavaVersion_ReturnsJavaHint()
    {
        var (diag, _) = CrashAnalyzer.Analyze(
            "java.lang.UnsupportedClassVersionError: ClassFile has wrong version 55");
        Assert.Contains("Java", diag);
    }

    [Fact]
    public void Analyze_OptiFineMissing_PointsToCacheCleanup()
    {
        var (_, sug) = CrashAnalyzer.Analyze(
            "java.lang.ClassNotFoundException: net.optifine.Config");
        Assert.Contains("кэш", sug, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_OpenGl_PointsToDrivers()
    {
        var (diag, sug) = CrashAnalyzer.Analyze(
            "GLFW error 65543: WGL: OpenGL profile requested but WGL_ARB_create_context_profile is unavailable");
        Assert.Contains("видеодрайвер", diag);
        Assert.Contains("драйверы", sug);
    }

    [Fact]
    public void Analyze_UnknownText_ReturnsGenericMessage()
    {
        var (diag, _) = CrashAnalyzer.Analyze("totally unrelated text");
        Assert.Contains("Неизвестная", diag);
    }
}
