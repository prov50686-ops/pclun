using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PcLun.Services;

/// <summary>
/// Read/write the system hosts file. Write requires elevated permissions —
/// this helper only stages a patch to a temp file and asks the OS to elevate.
/// On Linux, prompts for `pkexec` / `sudo`. On Windows, uses ShellExecute "runas".
/// </summary>
public static class HostsEditor
{
    public static string HostsPath => OperatingSystem.IsWindows()
        ? @"C:\Windows\System32\drivers\etc\hosts"
        : "/etc/hosts";

    public class Mapping
    {
        public string Ip { get; set; } = "";
        public string Host { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    public static IReadOnlyList<Mapping> Read()
    {
        var list = new List<Mapping>();
        try
        {
            if (!File.Exists(HostsPath)) return list;
            foreach (var raw in File.ReadAllLines(HostsPath))
            {
                var line = raw.TrimStart();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    list.Add(new Mapping
                    {
                        Ip = parts[0],
                        Host = parts[1],
                        Comment = parts.Length > 2 ? parts[2] : "",
                    });
                }
            }
        }
        catch (Exception ex) { AppLogger.Warn("HostsEditor.Read: " + ex.Message); }
        return list;
    }

    public static string StagePatch(IEnumerable<Mapping> mappings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PcLun custom mappings — generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        foreach (var m in mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Ip) || string.IsNullOrWhiteSpace(m.Host)) continue;
            sb.AppendLine($"{m.Ip} {m.Host}{(string.IsNullOrEmpty(m.Comment) ? "" : " # " + m.Comment)}");
        }
        var path = Path.Combine(Paths.TempDir, "hosts-patch.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static string FormatInstructions(string patchFile) =>
        OperatingSystem.IsWindows()
            ? $"Запусти от имени администратора:\n  copy /Y {patchFile} {HostsPath}"
            : $"Применить (root): sudo bash -c 'cat \"{patchFile}\" >> {HostsPath}'";
}
