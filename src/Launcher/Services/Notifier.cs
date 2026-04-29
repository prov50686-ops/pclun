using System;
using System.Diagnostics;

namespace PcLun.Services;

/// <summary>
/// Cross-platform desktop notifications. Windows uses BurntToast-free path
/// via msg.exe / built-in toast as best effort; Linux uses notify-send;
/// macOS uses osascript.
/// </summary>
public static class Notifier
{
    public static void Show(string title, string body)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Run("notify-send", $"\"{Escape(title)}\" \"{Escape(body)}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("osascript", $"-e 'display notification \"{Escape(body)}\" with title \"{Escape(title)}\"'");
            }
            else if (OperatingSystem.IsWindows())
            {
                // Built-in shell toast via PowerShell BurntToast-free.
                var ps =
                    "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] | Out-Null;" +
                    "[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime] | Out-Null;" +
                    "$x=[xml]'<toast><visual><binding template=\"ToastGeneric\"><text>" + Escape(title) +
                    "</text><text>" + Escape(body) + "</text></binding></visual></toast>';" +
                    "$xd=New-Object Windows.Data.Xml.Dom.XmlDocument;$xd.LoadXml($x.OuterXml);" +
                    "$t=[Windows.UI.Notifications.ToastNotification]::new($xd);" +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('PcLun').Show($t);";
                Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps.Replace("\"", "\\\"")}\"");
            }
        }
        catch (Exception ex) { AppLogger.Warn("Notifier.Show failed: " + ex.Message); }
    }

    private static void Run(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
        }
        catch { /* notifier missing — ignore */ }
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"").Replace("'", "\\'");
}
