using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcLun.Services;

/// <summary>
/// Экспорт/импорт всех пользовательских настроек одним JSON.
/// Аккаунт намеренно не экспортируется (содержит токен).
/// </summary>
public static class SettingsExport
{
    public class ExportBundle
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("exported_at")] public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("settings")] public LauncherSettings Settings { get; set; } = new();
    }

    public static string Export(LauncherSettings settings, string destPath)
    {
        var bundle = new ExportBundle { Settings = settings };
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(destPath, json);
        return destPath;
    }

    public static LauncherSettings Import(string srcPath)
    {
        if (!File.Exists(srcPath)) throw new FileNotFoundException(srcPath);
        var text = File.ReadAllText(srcPath);
        var bundle = JsonSerializer.Deserialize<ExportBundle>(text)
                     ?? throw new InvalidDataException("Не удалось распарсить файл настроек.");
        if (bundle.Settings is null)
            throw new InvalidDataException("В файле нет блока settings.");
        bundle.Settings.Save();
        return bundle.Settings;
    }
}
