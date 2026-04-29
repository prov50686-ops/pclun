using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcLun.Services;

public enum ContentKind { Mods, Shaderpacks, Resourcepacks }

public record ContentItem(string Name, string FullPath, long Size, bool Enabled, ContentKind Kind);

/// <summary>
/// Менеджер модов / шейдеров / ресурспаков. Включает/выключает
/// переименованием `.jar` ↔ `.jar.disabled` (универсальный трюк, понимаемый
/// Forge/Fabric/OptiFine).
/// </summary>
public static class ContentManager
{
    public static string FolderFor(ContentKind k) => k switch
    {
        ContentKind.Mods => Path.Combine(Paths.GameDir, "mods"),
        ContentKind.Shaderpacks => Path.Combine(Paths.GameDir, "shaderpacks"),
        ContentKind.Resourcepacks => Path.Combine(Paths.GameDir, "resourcepacks"),
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    public static IEnumerable<string> ExtensionsFor(ContentKind k) => k switch
    {
        ContentKind.Mods => new[] { ".jar" },
        ContentKind.Shaderpacks => new[] { ".zip", ".jar" },
        ContentKind.Resourcepacks => new[] { ".zip" },
        _ => Array.Empty<string>()
    };

    public static List<ContentItem> List(ContentKind kind)
    {
        var folder = FolderFor(kind);
        Directory.CreateDirectory(folder);
        var exts = ExtensionsFor(kind).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<ContentItem>();
        foreach (var f in new DirectoryInfo(folder).GetFiles())
        {
            // Учитываем .jar и .jar.disabled. Для шейдеров/RP — .zip и .zip.disabled.
            var ext = Path.GetExtension(f.Name);
            var isDisabled = ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
            var realExt = isDisabled
                ? Path.GetExtension(Path.GetFileNameWithoutExtension(f.Name))
                : ext;
            if (!exts.Contains(realExt)) continue;

            items.Add(new ContentItem(
                Name: Path.GetFileNameWithoutExtension(isDisabled ? Path.GetFileNameWithoutExtension(f.Name) : f.Name),
                FullPath: f.FullName,
                Size: f.Length,
                Enabled: !isDisabled,
                Kind: kind));
        }
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ContentItem Toggle(ContentItem item)
    {
        var newPath = item.Enabled ? item.FullPath + ".disabled" : StripDisabled(item.FullPath);
        if (File.Exists(newPath)) File.Delete(newPath);
        File.Move(item.FullPath, newPath);
        return item with { FullPath = newPath, Enabled = !item.Enabled };
    }

    public static void Delete(ContentItem item)
    {
        if (File.Exists(item.FullPath)) File.Delete(item.FullPath);
    }

    public static ContentItem ImportFile(string sourcePath, ContentKind kind)
    {
        var folder = FolderFor(kind);
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, Path.GetFileName(sourcePath));
        if (File.Exists(dest))
        {
            // append timestamp to avoid clobbering
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            dest = Path.Combine(folder, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        }
        File.Copy(sourcePath, dest);
        var fi = new FileInfo(dest);
        return new ContentItem(Path.GetFileNameWithoutExtension(dest), dest, fi.Length, true, kind);
    }

    private static string StripDisabled(string path)
    {
        return path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path;
    }
}
