using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PcLun.Services;

/// <summary>
/// Multiple game profiles, each with isolated .minecraft folder.
/// Stored under {LauncherRoot}/instances/{name}/minecraft.
/// </summary>
public static class InstanceManager
{
    private static string Root => Path.Combine(Paths.LauncherRoot, "instances");
    private static string Index => Path.Combine(Paths.LauncherRoot, "instances.json");

    public class Instance
    {
        public string Name { get; set; } = "default";
        public string McVersion { get; set; } = "1.16.5";
        public string Loader { get; set; } = "optifine"; // optifine | forge | fabric | vanilla
        public string LoaderVersion { get; set; } = "";
        public bool IsDefault { get; set; }
    }

    public static IReadOnlyList<Instance> List()
    {
        try
        {
            if (!File.Exists(Index)) return new[] { Default() };
            var data = JsonSerializer.Deserialize<List<Instance>>(File.ReadAllText(Index));
            if (data is null || data.Count == 0) return new[] { Default() };
            return data;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("InstanceManager.List failed: " + ex.Message);
            return new[] { Default() };
        }
    }

    public static Instance Default() => new()
    {
        Name = "default",
        McVersion = "1.16.5",
        Loader = "optifine",
        IsDefault = true,
    };

    public static void Save(IEnumerable<Instance> all)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Index, JsonSerializer.Serialize(all,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Instance Create(string name, string mcVersion, string loader)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "instance" + DateTime.Now.Ticks;
        var inst = new Instance { Name = SafeName(name), McVersion = mcVersion, Loader = loader };
        var list = List().ToList();
        if (list.Any(i => i.Name == inst.Name)) inst.Name += "_" + DateTime.Now.Ticks;
        list.Add(inst);
        Save(list);
        Directory.CreateDirectory(GameDirOf(inst));
        return inst;
    }

    public static void Delete(string name)
    {
        var list = List().ToList();
        list.RemoveAll(i => i.Name == name && !i.IsDefault);
        Save(list);
        try
        {
            var dir = Path.Combine(Root, name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) { AppLogger.Warn("InstanceManager.Delete: " + ex.Message); }
    }

    public static string GameDirOf(Instance inst)
    {
        if (inst.IsDefault) return Paths.GameDir;
        var dir = Path.Combine(Root, inst.Name, "minecraft");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeName(string name)
    {
        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "instance" : safe.ToLowerInvariant();
    }
}
