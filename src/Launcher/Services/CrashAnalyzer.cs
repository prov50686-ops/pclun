using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PcLun.Services;

/// <summary>
/// Простой эвристический анализатор `crash-reports/`. Цель — за 1 секунду
/// сказать пользователю "что не так и что нажать", а не выдать stacktrace.
/// </summary>
public static class CrashAnalyzer
{
    public record CrashEntry(string FileName, DateTime Timestamp, string Diagnosis, string Suggestion, string FullPath, string Snippet);

    private static readonly (Regex pattern, string diagnosis, string suggestion)[] _rules = new[]
    {
        (new Regex(@"java\.lang\.OutOfMemoryError|GC overhead limit exceeded", RegexOptions.IgnoreCase),
            "Не хватает оперативной памяти, выделенной игре.",
            "Зайдите во вкладку «Java и JVM» и поставьте RAM хотя бы 2048 МБ (или больше)."),
        (new Regex(@"UnsupportedClassVersionError|class file version", RegexOptions.IgnoreCase),
            "Java неправильной версии (нужна 8 для 1.16.5).",
            "Удалите свой кастомный путь к Java на вкладке «Java и JVM» — лаунчер сам скачает Java 8."),
        (new Regex(@"OptiFine.*not (installed|found)|net\.optifine.*ClassNotFoundException|ClassNotFoundException.*optifine", RegexOptions.IgnoreCase),
            "OptiFine не установился или сломан.",
            "Нажмите «Очистить кэш» в Инструментах и запустите игру снова — лаунчер переустановит OptiFine."),
        (new Regex(@"GLFW error|Pixel format not accelerated|OpenGL", RegexOptions.IgnoreCase),
            "Проблема с видеодрайвером / OpenGL.",
            "Обновите драйверы видеокарты. Если игра запускается, переключите профиль на «Potato» — он не использует продвинутые шейдеры."),
        (new Regex(@"BrandLoader|fml\.|forge|FabricLoader", RegexOptions.IgnoreCase),
            "Конфликт модов / loader (Forge или Fabric).",
            "Откройте вкладку «Контент» → «Моды» и временно отключите все моды (или удалите проблемный)."),
        (new Regex(@"Missing or corrupted .* (jar|asset)", RegexOptions.IgnoreCase),
            "Поврежденный jar или ассет.",
            "В «Инструментах» нажмите «Очистить кэш» — лаунчер перекачает файлы."),
        (new Regex(@"java\.io\.FileNotFoundException", RegexOptions.IgnoreCase),
            "Не найден файл (мог быть удалён антивирусом).",
            "Добавьте папку лаунчера в исключения антивируса и перезапустите игру."),
    };

    public static List<CrashEntry> ScanRecent(int maxEntries = 10)
    {
        var dir = Path.Combine(Paths.GameDir, "crash-reports");
        if (!Directory.Exists(dir)) return new List<CrashEntry>();

        var files = new DirectoryInfo(dir)
            .GetFiles("crash-*.txt")
            .OrderByDescending(f => f.LastWriteTime)
            .Take(maxEntries);

        var result = new List<CrashEntry>();
        foreach (var f in files)
        {
            try
            {
                var text = File.ReadAllText(f.FullName);
                var (diag, sug) = Analyze(text);
                var snippet = ExtractSnippet(text);
                result.Add(new CrashEntry(f.Name, f.LastWriteTime, diag, sug, f.FullName, snippet));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("CrashAnalyzer: failed to read " + f.Name + ": " + ex.Message);
            }
        }
        return result;
    }

    public static (string diagnosis, string suggestion) Analyze(string text)
    {
        foreach (var (pattern, diag, sug) in _rules)
        {
            if (pattern.IsMatch(text)) return (diag, sug);
        }
        return ("Неизвестная ошибка.", "Откройте сам файл краша — там подробный stacktrace.");
    }

    private static string ExtractSnippet(string text)
    {
        // Первая строка после "Description:" или первые 6 строк.
        var idx = text.IndexOf("Description:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var slice = text.Substring(idx, Math.Min(text.Length - idx, 800));
            return string.Join("\n", slice.Split('\n').Take(8)).Trim();
        }
        return string.Join("\n", text.Split('\n').Take(6)).Trim();
    }
}
