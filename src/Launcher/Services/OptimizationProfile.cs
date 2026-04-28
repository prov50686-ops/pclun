using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PcLun.Services;

public enum FpsProfile
{
    Potato = 0,     // 🥔 — для самых-самых слабых ПК (2 чанка, 0 анимаций)
    UltraFps = 1,   // максимум FPS на слабом ПК
    Balanced = 2,
    Quality = 3
}

public static class OptimizationProfile
{
    public static List<string> GetJvmArgs(int ramMb, string customArgs = "")
    {
        // Aikar-style flags — оптимально для Minecraft client + low-RAM.
        var args = new List<string>
        {
            $"-Xms{ramMb}M",
            $"-Xmx{ramMb}M",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MaxGCPauseMillis=50",
            "-XX:G1HeapRegionSize=32M",
            "-XX:+ParallelRefProcEnabled",
            "-XX:+DisableExplicitGC",
            "-XX:+AlwaysPreTouch",
            "-XX:+UseStringDeduplication",
            "-XX:+UseFastUnorderedTimeStamps",
            "-XX:+OptimizeStringConcat",
            "-XX:+UseCompressedOops",
            "-Dfml.ignoreInvalidMinecraftCertificates=true",
            "-Dfml.ignorePatchDiscrepancies=true",
            "-Dlog4j2.formatMsgNoLookups=true",
            "-Djava.net.preferIPv4Stack=true",
            "-Dfile.encoding=UTF-8",
        };

        if (!string.IsNullOrWhiteSpace(customArgs))
        {
            foreach (var token in customArgs.Split(' ', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            {
                args.Add(token);
            }
        }
        return args;
    }

    /// <summary>
    /// Перезаписывает options.txt и optionsof.txt, основываясь на пользовательских настройках.
    /// </summary>
    public static void WriteAll(string gameDir, LauncherSettings s)
    {
        Directory.CreateDirectory(gameDir);
        WriteVanilla(gameDir, s);
        WriteOptifine(gameDir, s);
    }

    private static void WriteVanilla(string gameDir, LauncherSettings s)
    {
        var path = Path.Combine(gameDir, "options.txt");
        var ci = CultureInfo.InvariantCulture;
        var opts = new Dictionary<string, string>
        {
            { "version", "2586" },
            { "renderDistance", s.RenderDistance.ToString(ci) },
            { "simulationDistance", s.SimulationDistance.ToString(ci) },
            { "graphicsMode", s.GraphicsMode.ToString(ci) },
            { "ao", s.SmoothLighting.ToString(ci) },
            { "renderClouds", s.Clouds ? "true" : "false" },
            { "particles", s.Particles.ToString(ci) },
            { "fancyGraphics", s.GraphicsMode > 0 ? "true" : "false" },
            { "useVbo", "true" },
            { "mipmapLevels", s.MipmapLevels.ToString(ci) },
            { "biomeBlendRadius", s.BiomeBlend.ToString(ci) },
            { "maxFps", s.MaxFps.ToString(ci) },
            { "enableVsync", s.VSync ? "true" : "false" },
            { "entityShadows", s.EntityShadows ? "true" : "false" },
            { "entityDistanceScaling", s.EntityDistance.ToString("0.0", ci) },
            { "fov", "0.0" },
            { "gamma", "1.0" },
            { "guiScale", s.GuiScale.ToString(ci) },
            { "showSubtitles", "false" },
            { "fullscreen", s.Fullscreen ? "true" : "false" },
            { "bobView", s.ViewBobbing ? "true" : "false" },
            { "soundCategory_master", s.MasterVolume.ToString("0.0", ci) },
            { "lang", "ru_ru" }
        };

        using var sw = new StreamWriter(path, append: false);
        foreach (var (k, v) in opts) sw.WriteLine($"{k}:{v}");
    }

    private static void WriteOptifine(string gameDir, LauncherSettings s)
    {
        var path = Path.Combine(gameDir, "optionsof.txt");
        var ci = CultureInfo.InvariantCulture;
        var opts = new Dictionary<string, string>
        {
            { "ofFastRender", s.OfFastRender ? "true" : "false" },
            { "ofFastMath", s.OfFastMath ? "true" : "false" },
            { "ofSmartAnimations", s.OfSmartAnimations ? "true" : "false" },
            { "ofSmoothFps", "false" },
            { "ofDynamicFps", s.OfDynamicFps ? "true" : "false" },
            { "ofLazyChunkLoading", s.OfLazyChunkLoading ? "true" : "false" },
            { "ofRenderRegions", s.OfRenderRegions ? "true" : "false" },
            { "ofChunkUpdates", s.OfChunkUpdates.ToString(ci) },
            { "ofAaLevel", s.OfAaLevel.ToString(ci) },
            { "ofAfLevel", s.OfAfLevel.ToString(ci) },
            { "ofAnimatedWater", s.OfAnimatedWater ? "0" : "2" },
            { "ofAnimatedLava", s.OfAnimatedLava ? "0" : "2" },
            { "ofAnimatedFire", s.OfAnimatedFire ? "true" : "false" },
            { "ofAnimatedPortal", s.OfAnimatedPortal ? "true" : "false" },
            { "ofAnimatedRedstone", s.OfAnimatedRedstone ? "true" : "false" },
            { "ofAnimatedExplosion", s.OfAnimatedExplosion ? "true" : "false" },
            { "ofAnimatedFlame", s.OfAnimatedFire ? "true" : "false" },
            { "ofAnimatedSmoke", s.OfAnimatedExplosion ? "true" : "false" },
            { "ofVoidParticles", "false" },
            { "ofWaterParticles", "false" },
            { "ofRainSplash", "false" },
            { "ofPortalParticles", "false" },
            { "ofPotionParticles", "false" },
            { "ofFireworkParticles", "false" },
            { "ofDrippingWaterLava", "false" },
            { "ofAnimatedTerrain", s.OfAnimatedTextures ? "true" : "false" },
            { "ofAnimatedTextures", s.OfAnimatedTextures ? "true" : "false" },
            { "ofAnimatedItems", s.OfAnimatedTextures ? "true" : "false" },
            { "ofRandomEntities", "false" },
            { "ofCustomFonts", "false" },
            { "ofCustomColors", "false" },
            { "ofCustomItems", "false" },
            { "ofCustomEntityModels", "false" },
            { "ofCustomGuis", "false" },
            { "ofShowGlErrors", "false" },
            { "ofShowFps", s.OfShowFps ? "true" : "false" },
            { "ofTrees", "1" },
            { "ofRain", "1" },
            { "ofSky", "false" },
            { "ofStars", "false" },
            { "ofSunMoon", "false" },
            { "ofClouds", s.Clouds ? "1" : "3" }, // 3 = off
            { "ofCloudsHeight", "0.0" },
            { "ofTime", "0" },
            { "ofClearWater", "false" },
            { "ofBetterGrass", "3" },
            { "ofBetterSnow", "false" },
            { "ofTranslucentBlocks", "1" },
            { "ofDroppedItems", "1" },
            { "ofVignette", "1" },
            { "ofFogType", "1" },
            { "ofFogStart", "0.8" }
        };

        using var sw = new StreamWriter(path, append: false);
        foreach (var (k, v) in opts) sw.WriteLine($"{k}:{v}");
    }
}
