using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcLun.Services;

/// <summary>
/// Все пользовательские настройки лаунчера. Сериализуются в launcher.json.
/// </summary>
public class LauncherSettings
{
    // ---- Игрок ----
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = "Player" + new Random().Next(100, 999);
    [JsonPropertyName("auth_mode")] public int AuthMode { get; set; } = 0; // 0=offline, 1=msa

    // ---- Производительность / графика ----
    [JsonPropertyName("ram_mb")] public int RamMb { get; set; } = 2048;
    [JsonPropertyName("fps_profile")] public int FpsProfile { get; set; } = 1; // 0=Potato 1=Ultra 2=Balanced 3=Quality
    [JsonPropertyName("render_distance")] public int RenderDistance { get; set; } = 4;
    [JsonPropertyName("simulation_distance")] public int SimulationDistance { get; set; } = 4;
    [JsonPropertyName("max_fps")] public int MaxFps { get; set; } = 260;
    [JsonPropertyName("graphics_mode")] public int GraphicsMode { get; set; } = 0;     // 0=Fast 1=Fancy 2=Fabulous
    [JsonPropertyName("particles")] public int Particles { get; set; } = 2;            // 0=All 1=Decreased 2=Minimal
    [JsonPropertyName("smooth_lighting")] public int SmoothLighting { get; set; } = 0;  // 0=Off 1=Min 2=Max
    [JsonPropertyName("clouds")] public bool Clouds { get; set; } = false;
    [JsonPropertyName("entity_shadows")] public bool EntityShadows { get; set; } = false;
    [JsonPropertyName("vsync")] public bool VSync { get; set; } = false;
    [JsonPropertyName("mipmap_levels")] public int MipmapLevels { get; set; } = 0;
    [JsonPropertyName("biome_blend")] public int BiomeBlend { get; set; } = 0;
    [JsonPropertyName("entity_distance")] public double EntityDistance { get; set; } = 0.5;
    [JsonPropertyName("view_bobbing")] public bool ViewBobbing { get; set; } = false;
    [JsonPropertyName("fov")] public int Fov { get; set; } = 70;
    [JsonPropertyName("gui_scale")] public int GuiScale { get; set; } = 2;
    [JsonPropertyName("master_volume")] public double MasterVolume { get; set; } = 1.0;

    // ---- OptiFine ----
    [JsonPropertyName("install_optifine")] public bool InstallOptifine { get; set; } = true;
    [JsonPropertyName("of_fast_render")] public bool OfFastRender { get; set; } = true;
    [JsonPropertyName("of_fast_math")] public bool OfFastMath { get; set; } = true;
    [JsonPropertyName("of_smart_animations")] public bool OfSmartAnimations { get; set; } = false;
    [JsonPropertyName("of_dynamic_fps")] public bool OfDynamicFps { get; set; } = true;
    [JsonPropertyName("of_lazy_chunk_loading")] public bool OfLazyChunkLoading { get; set; } = true;
    [JsonPropertyName("of_render_regions")] public bool OfRenderRegions { get; set; } = true;
    [JsonPropertyName("of_aa_level")] public int OfAaLevel { get; set; } = 0;
    [JsonPropertyName("of_af_level")] public int OfAfLevel { get; set; } = 1;
    [JsonPropertyName("of_chunk_updates")] public int OfChunkUpdates { get; set; } = 1;
    [JsonPropertyName("of_animated_water")] public bool OfAnimatedWater { get; set; } = false;
    [JsonPropertyName("of_animated_lava")] public bool OfAnimatedLava { get; set; } = false;
    [JsonPropertyName("of_animated_fire")] public bool OfAnimatedFire { get; set; } = false;
    [JsonPropertyName("of_animated_portal")] public bool OfAnimatedPortal { get; set; } = false;
    [JsonPropertyName("of_animated_redstone")] public bool OfAnimatedRedstone { get; set; } = false;
    [JsonPropertyName("of_animated_explosion")] public bool OfAnimatedExplosion { get; set; } = false;
    [JsonPropertyName("of_animated_textures")] public bool OfAnimatedTextures { get; set; } = false;
    [JsonPropertyName("of_show_fps")] public bool OfShowFps { get; set; } = true;

    // ---- Java / JVM ----
    [JsonPropertyName("custom_java_path")] public string CustomJavaPath { get; set; } = "";
    [JsonPropertyName("use_optimized_flags")] public bool UseOptimizedFlags { get; set; } = true;
    [JsonPropertyName("custom_jvm_args")] public string CustomJvmArgs { get; set; } = "";
    [JsonPropertyName("pre_launch_command")] public string PreLaunchCommand { get; set; } = "";
    [JsonPropertyName("post_launch_action")] public int PostLaunchAction { get; set; } = 0; // 0=keep open, 1=hide, 2=close
    [JsonPropertyName("auto_connect_server")] public string AutoConnectServer { get; set; } = "";

    // ---- Окно игры ----
    [JsonPropertyName("window_width")] public int WindowWidth { get; set; } = 1280;
    [JsonPropertyName("window_height")] public int WindowHeight { get; set; } = 720;
    [JsonPropertyName("fullscreen")] public bool Fullscreen { get; set; } = false;
    [JsonPropertyName("custom_game_dir")] public string CustomGameDir { get; set; } = "";

    // ---- Сохранение / загрузка ----
    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
            {
                var text = File.ReadAllText(Paths.ConfigFile);
                var settings = JsonSerializer.Deserialize<LauncherSettings>(text);
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load settings", ex);
        }
        return new LauncherSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Paths.ConfigFile, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Применяет один из готовых FPS-пресетов поверх текущих значений.
    /// </summary>
    public void ApplyProfile(int profileIndex)
    {
        FpsProfile = profileIndex;
        switch (profileIndex)
        {
            case 0: // Potato 🥔 — экстрим
                RenderDistance = 2;
                SimulationDistance = 2;
                MaxFps = 260;
                GraphicsMode = 0;
                Particles = 2;
                SmoothLighting = 0;
                Clouds = false;
                EntityShadows = false;
                VSync = false;
                MipmapLevels = 0;
                BiomeBlend = 0;
                EntityDistance = 0.5;
                ViewBobbing = false;
                OfFastRender = true;
                OfFastMath = true;
                OfSmartAnimations = false;
                OfDynamicFps = true;
                OfLazyChunkLoading = true;
                OfRenderRegions = true;
                OfAaLevel = 0;
                OfAfLevel = 1;
                OfChunkUpdates = 1;
                OfAnimatedWater = false;
                OfAnimatedLava = false;
                OfAnimatedFire = false;
                OfAnimatedPortal = false;
                OfAnimatedRedstone = false;
                OfAnimatedExplosion = false;
                OfAnimatedTextures = false;
                break;
            case 1: // Ultra FPS
                RenderDistance = 4;
                SimulationDistance = 4;
                MaxFps = 260;
                GraphicsMode = 0;
                Particles = 2;
                SmoothLighting = 0;
                Clouds = false;
                EntityShadows = false;
                VSync = false;
                MipmapLevels = 0;
                BiomeBlend = 0;
                EntityDistance = 0.5;
                ViewBobbing = false;
                OfFastRender = true;
                OfFastMath = true;
                OfSmartAnimations = false;
                OfDynamicFps = true;
                OfLazyChunkLoading = true;
                OfRenderRegions = true;
                OfAnimatedWater = false;
                OfAnimatedLava = false;
                OfAnimatedFire = false;
                OfAnimatedPortal = false;
                OfAnimatedRedstone = false;
                OfAnimatedExplosion = false;
                OfAnimatedTextures = false;
                OfChunkUpdates = 1;
                break;
            case 2: // Balanced
                RenderDistance = 8;
                SimulationDistance = 8;
                MaxFps = 180;
                GraphicsMode = 0;
                Particles = 1;
                SmoothLighting = 1;
                Clouds = true;
                EntityShadows = true;
                VSync = false;
                MipmapLevels = 2;
                BiomeBlend = 2;
                EntityDistance = 1.0;
                ViewBobbing = true;
                OfFastRender = true;
                OfFastMath = true;
                OfSmartAnimations = true;
                OfDynamicFps = true;
                OfLazyChunkLoading = true;
                OfRenderRegions = true;
                OfAnimatedWater = true;
                OfAnimatedLava = true;
                OfChunkUpdates = 2;
                break;
            case 3: // Quality
                RenderDistance = 12;
                SimulationDistance = 10;
                MaxFps = 120;
                GraphicsMode = 1;
                Particles = 0;
                SmoothLighting = 2;
                Clouds = true;
                EntityShadows = true;
                VSync = false;
                MipmapLevels = 4;
                BiomeBlend = 5;
                EntityDistance = 1.0;
                ViewBobbing = true;
                OfFastRender = false;
                OfFastMath = false;
                OfSmartAnimations = true;
                OfDynamicFps = false;
                OfLazyChunkLoading = false;
                OfRenderRegions = true;
                OfAnimatedWater = true;
                OfAnimatedLava = true;
                OfAnimatedFire = true;
                OfAnimatedPortal = true;
                OfAnimatedRedstone = true;
                OfAnimatedExplosion = true;
                OfAnimatedTextures = true;
                OfChunkUpdates = 3;
                break;
        }
    }
}
