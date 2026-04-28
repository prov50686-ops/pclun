using System;
using System.IO;

namespace PcLun.Services;

public static class Paths
{
    public static string LauncherRoot { get; } = ResolveRoot();

    public static string GameDir => Path.Combine(LauncherRoot, "minecraft");
    public static string LibrariesDir => Path.Combine(GameDir, "libraries");
    public static string AssetsDir => Path.Combine(GameDir, "assets");
    public static string AssetsIndexes => Path.Combine(AssetsDir, "indexes");
    public static string AssetsObjects => Path.Combine(AssetsDir, "objects");
    public static string VersionsDir => Path.Combine(GameDir, "versions");
    public static string NativesDir => Path.Combine(GameDir, "natives");

    public static string RuntimeDir => Path.Combine(LauncherRoot, "runtime");
    public static string JavaDir => Path.Combine(RuntimeDir, "jre8");
    public static string JavaExe
    {
        get
        {
            var exeName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
            // Adoptium archives put the jre under jdk8u<...>-jre/bin
            var binDir = FindBinDir(JavaDir);
            return binDir is null ? Path.Combine(JavaDir, "bin", exeName) : Path.Combine(binDir, exeName);
        }
    }

    public static string ConfigFile => Path.Combine(LauncherRoot, "launcher.json");
    public static string LogFile => Path.Combine(LauncherRoot, "launcher.log");
    public static string AccountsFile => Path.Combine(LauncherRoot, "accounts.json");

    public static string TempDir
    {
        get
        {
            var p = Path.Combine(LauncherRoot, "temp");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    private static string ResolveRoot()
    {
        // %APPDATA%\PcLun on Windows, ~/.pclun elsewhere
        string root;
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            root = Path.Combine(appData, "PcLun");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(home, ".pclun");
        }

        Directory.CreateDirectory(root);
        return root;
    }

    private static string? FindBinDir(string root)
    {
        if (!Directory.Exists(root)) return null;
        var direct = Path.Combine(root, "bin");
        if (Directory.Exists(direct)) return direct;
        foreach (var sub in Directory.GetDirectories(root))
        {
            var candidate = Path.Combine(sub, "bin");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
