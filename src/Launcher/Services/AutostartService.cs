using System;
using System.IO;
using System.Runtime.Versioning;

namespace PcLun.Services;

/// <summary>
/// Autostart with the OS. Windows: HKCU Run key; Linux: ~/.config/autostart desktop entry;
/// macOS: ~/Library/LaunchAgents plist (best-effort).
/// </summary>
public static class AutostartService
{
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsIsEnabled();
            if (OperatingSystem.IsLinux()) return File.Exists(LinuxDesktopEntry());
            if (OperatingSystem.IsMacOS()) return File.Exists(MacPlistPath());
        }
        catch (Exception ex) { AppLogger.Warn("Autostart.IsEnabled: " + ex.Message); }
        return false;
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsSet(enabled);
            else if (OperatingSystem.IsLinux()) LinuxSet(enabled);
            else if (OperatingSystem.IsMacOS()) MacSet(enabled);
        }
        catch (Exception ex) { AppLogger.Warn("Autostart.SetEnabled: " + ex.Message); }
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsIsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue("PcLun") is not null;
    }

    [SupportedOSPlatform("windows")]
    private static void WindowsSet(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key?.SetValue("PcLun", $"\"{Environment.ProcessPath}\"");
        else key?.DeleteValue("PcLun", false);
    }

    private static string LinuxDesktopEntry() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".config", "autostart", "pclun.desktop");

    private static void LinuxSet(bool enabled)
    {
        var path = LinuxDesktopEntry();
        if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "[Desktop Entry]\nType=Application\nName=PcLun\n" +
            $"Exec=\"{Environment.ProcessPath}\"\nIcon=pclun\nX-GNOME-Autostart-enabled=true\n");
    }

    private static string MacPlistPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "Library", "LaunchAgents", "com.mrdomik.pclun.plist");

    private static void MacSet(bool enabled)
    {
        var path = MacPlistPath();
        if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\"><dict>\n" +
            "  <key>Label</key><string>com.mrdomik.pclun</string>\n" +
            "  <key>ProgramArguments</key><array><string>" + Environment.ProcessPath + "</string></array>\n" +
            "  <key>RunAtLoad</key><true/>\n" +
            "</dict></plist>\n");
    }
}
