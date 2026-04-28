using System.Collections.Generic;
using System.IO;

namespace PcLun.Services;

public enum FpsProfile
{
    UltraFps = 0,   // самый низкий ресурс, максимум FPS
    Balanced = 1,   // средние настройки
    Quality = 2     // для мощных ПК
}

public static class OptimizationProfile
{
    public static List<string> GetJvmArgs(int ramMb)
    {
        // Aikar-style flags, адаптированные для Minecraft client + low-RAM.
        // -Xms = -Xmx, чтобы JVM не пересоздавала heap.
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
            "-Dfml.ignoreInvalidMinecraftCertificates=true",
            "-Dfml.ignorePatchDiscrepancies=true",
            "-Dlog4j2.formatMsgNoLookups=true",
            "-Djava.net.preferIPv4Stack=true",
            "-Dfile.encoding=UTF-8"
        };
        return args;
    }

    public static void WriteOptionsTxt(string gameDir, FpsProfile profile)
    {
        var path = Path.Combine(gameDir, "options.txt");

        // Не перезаписываем, если юзер уже что-то поменял
        if (File.Exists(path)) return;

        Dictionary<string, string> opts = profile switch
        {
            FpsProfile.UltraFps => UltraFps(),
            FpsProfile.Balanced => Balanced(),
            FpsProfile.Quality => Quality(),
            _ => UltraFps()
        };

        Directory.CreateDirectory(gameDir);
        using var sw = new StreamWriter(path, append: false);
        foreach (var (k, v) in opts) sw.WriteLine($"{k}:{v}");
    }

    public static void WriteOptifineTxt(string gameDir, FpsProfile profile)
    {
        var path = Path.Combine(gameDir, "optionsof.txt");
        if (File.Exists(path)) return;

        Dictionary<string, string> opts = profile switch
        {
            FpsProfile.UltraFps => UltraOptifine(),
            FpsProfile.Balanced => BalancedOptifine(),
            FpsProfile.Quality => QualityOptifine(),
            _ => UltraOptifine()
        };

        Directory.CreateDirectory(gameDir);
        using var sw = new StreamWriter(path, append: false);
        foreach (var (k, v) in opts) sw.WriteLine($"{k}:{v}");
    }

    // -------- options.txt presets --------
    private static Dictionary<string, string> UltraFps() => new()
    {
        { "version", "2586" },
        { "renderDistance", "4" },
        { "graphicsMode", "0" },           // 0=fast, 1=fancy, 2=fabulous
        { "ao", "0" },                      // ambient occlusion off
        { "renderClouds", "false" },
        { "particles", "2" },               // minimal
        { "fancyGraphics", "false" },
        { "useVbo", "true" },
        { "mipmapLevels", "0" },
        { "biomeBlendRadius", "0" },
        { "maxFps", "260" },
        { "enableVsync", "false" },
        { "entityShadows", "false" },
        { "entityDistanceScaling", "0.5" },
        { "fov", "0.0" },
        { "gamma", "1.0" },
        { "guiScale", "2" },
        { "showSubtitles", "false" },
        { "fullscreen", "false" }
    };

    private static Dictionary<string, string> Balanced() => new()
    {
        { "version", "2586" },
        { "renderDistance", "8" },
        { "graphicsMode", "0" },
        { "ao", "1" },
        { "renderClouds", "true" },
        { "particles", "1" },
        { "fancyGraphics", "false" },
        { "useVbo", "true" },
        { "mipmapLevels", "2" },
        { "biomeBlendRadius", "2" },
        { "maxFps", "180" },
        { "enableVsync", "false" },
        { "entityShadows", "true" },
        { "entityDistanceScaling", "1.0" },
        { "fov", "0.5" },
        { "gamma", "1.0" },
        { "guiScale", "2" }
    };

    private static Dictionary<string, string> Quality() => new()
    {
        { "version", "2586" },
        { "renderDistance", "12" },
        { "graphicsMode", "1" },
        { "ao", "2" },
        { "renderClouds", "true" },
        { "particles", "0" },
        { "fancyGraphics", "true" },
        { "useVbo", "true" },
        { "mipmapLevels", "4" },
        { "biomeBlendRadius", "5" },
        { "maxFps", "120" },
        { "enableVsync", "false" },
        { "entityShadows", "true" },
        { "entityDistanceScaling", "1.0" }
    };

    // -------- optionsof.txt presets (OptiFine) --------
    private static Dictionary<string, string> UltraOptifine() => new()
    {
        { "ofFastRender", "true" },
        { "ofFastMath", "true" },
        { "ofSmartAnimations", "false" },
        { "ofSmoothFps", "false" },
        { "ofDynamicFps", "true" },
        { "ofChunkUpdates", "1" },
        { "ofAoLevel", "0.0" },
        { "ofAaLevel", "0" },
        { "ofAfLevel", "1" },
        { "ofClouds", "3" },                // off
        { "ofCloudsHeight", "0.0" },
        { "ofTrees", "1" },
        { "ofRain", "3" },                  // off
        { "ofAnimatedWater", "2" },
        { "ofAnimatedLava", "2" },
        { "ofAnimatedFire", "false" },
        { "ofAnimatedPortal", "false" },
        { "ofAnimatedRedstone", "false" },
        { "ofAnimatedExplosion", "false" },
        { "ofAnimatedFlame", "false" },
        { "ofAnimatedSmoke", "false" },
        { "ofVoidParticles", "false" },
        { "ofWaterParticles", "false" },
        { "ofRainSplash", "false" },
        { "ofPortalParticles", "false" },
        { "ofPotionParticles", "false" },
        { "ofFireworkParticles", "false" },
        { "ofDroppedItems", "1" },
        { "ofVignette", "1" },
        { "ofShowFps", "true" },
        { "ofRenderRegions", "true" },
        { "ofLazyChunkLoading", "true" }
    };

    private static Dictionary<string, string> BalancedOptifine() => new()
    {
        { "ofFastRender", "true" },
        { "ofFastMath", "true" },
        { "ofSmartAnimations", "true" },
        { "ofDynamicFps", "true" },
        { "ofChunkUpdates", "2" },
        { "ofAoLevel", "1.0" },
        { "ofClouds", "2" },                // fast
        { "ofTrees", "2" },                 // fast
        { "ofRain", "2" },                  // fast
        { "ofVignette", "1" },
        { "ofShowFps", "true" }
    };

    private static Dictionary<string, string> QualityOptifine() => new()
    {
        { "ofFastRender", "false" },
        { "ofFastMath", "false" },
        { "ofSmartAnimations", "true" },
        { "ofDynamicFps", "false" },
        { "ofChunkUpdates", "3" },
        { "ofAoLevel", "1.0" },
        { "ofClouds", "0" },
        { "ofTrees", "3" },
        { "ofVignette", "1" }
    };
}
