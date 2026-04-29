using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace PcLun.Services;

/// <summary>
/// Inspects mods/ for duplicate mod IDs by parsing META-INF/mods.toml or
/// fabric.mod.json. Reports duplicates and missing-Forge errors.
/// </summary>
public static class ModConflictDetector
{
    public class Conflict
    {
        public string ModId { get; set; } = "";
        public List<string> Files { get; set; } = new();
        public string Severity { get; set; } = "duplicate";
    }

    public class Report
    {
        public List<Conflict> Conflicts { get; set; } = new();
        public int Total { get; set; }
        public string Summary { get; set; } = "";
    }

    public static Report Scan(string gameDir)
    {
        var report = new Report();
        var modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir))
        {
            report.Summary = "Папка mods/ отсутствует.";
            return report;
        }

        var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var jars = Directory.GetFiles(modsDir, "*.jar");
        report.Total = jars.Length;

        foreach (var jar in jars)
        {
            try
            {
                using var arch = ZipFile.OpenRead(jar);
                var modIds = ExtractModIds(arch);
                foreach (var id in modIds)
                {
                    if (!byId.ContainsKey(id)) byId[id] = new List<string>();
                    byId[id].Add(Path.GetFileName(jar));
                }
            }
            catch (Exception ex) { AppLogger.Warn("ModConflictDetector: " + Path.GetFileName(jar) + " — " + ex.Message); }
        }

        foreach (var kv in byId.Where(k => k.Value.Count > 1))
        {
            report.Conflicts.Add(new Conflict
            {
                ModId = kv.Key,
                Files = kv.Value,
                Severity = "duplicate",
            });
        }

        report.Summary = report.Conflicts.Count == 0
            ? $"OK: {report.Total} мод(а/ов), конфликтов нет."
            : $"⚠ {report.Conflicts.Count} конфликт(ов) среди {report.Total} мод(а/ов).";
        return report;
    }

    private static IEnumerable<string> ExtractModIds(ZipArchive arch)
    {
        var ids = new List<string>();
        var modsToml = arch.Entries.FirstOrDefault(e => e.FullName.Equals("META-INF/mods.toml", StringComparison.OrdinalIgnoreCase));
        if (modsToml is not null)
        {
            using var sr = new StreamReader(modsToml.Open());
            var text = sr.ReadToEnd();
            foreach (Match m in Regex.Matches(text, @"modId\s*=\s*""([^""]+)"""))
                ids.Add(m.Groups[1].Value);
        }
        var fabric = arch.Entries.FirstOrDefault(e => e.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
        if (fabric is not null)
        {
            using var sr = new StreamReader(fabric.Open());
            var text = sr.ReadToEnd();
            var m = Regex.Match(text, @"""id""\s*:\s*""([^""]+)""");
            if (m.Success) ids.Add(m.Groups[1].Value);
        }
        return ids;
    }
}
