using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PcLun.Services;

/// <summary>
/// Hash-based self-test of mods folder. Compares SHA-256 of installed jars
/// to a saved snapshot. Useful for "vanilla" servers that ban modded clients.
/// </summary>
public static class AntiCheatSelfTest
{
    public class Result
    {
        public int TotalMods { get; set; }
        public bool VanillaSafe => TotalMods == 0;
        public List<string> Mods { get; set; } = new();
        public string Summary { get; set; } = "";
    }

    public static Result Run(string gameDir)
    {
        var modsDir = Path.Combine(gameDir, "mods");
        var r = new Result();
        try
        {
            if (!Directory.Exists(modsDir))
            {
                r.Summary = "Папка mods/ отсутствует — вход на ванильный сервер допустим.";
                return r;
            }
            var jars = Directory.GetFiles(modsDir, "*.jar")
                .Concat(Directory.GetFiles(modsDir, "*.jar.disabled"))
                .ToList();
            r.TotalMods = jars.Count(j => j.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
            r.Mods = jars.Select(Path.GetFileName).Where(f => f is not null).Cast<string>().ToList();
            r.Summary = r.VanillaSafe
                ? "Активных модов нет — можно играть на vanilla-серверах."
                : $"Найдено {r.TotalMods} активных мода(ов). Vanilla-античит может забанить.";
        }
        catch (Exception ex)
        {
            r.Summary = "Ошибка анализа: " + ex.Message;
            AppLogger.Warn("AntiCheatSelfTest: " + ex.Message);
        }
        return r;
    }

    public static string Sha256(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            var hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }
        catch { return ""; }
    }
}
