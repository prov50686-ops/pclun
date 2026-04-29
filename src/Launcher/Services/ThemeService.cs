using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PcLun.Services;

/// <summary>
/// Theme + accent color management.
/// Themes: dark (default), light, system.
/// Accent: hex color applied to ThemeAccent resource.
/// </summary>
public static class ThemeService
{
    public enum Mode { Dark, Light, System }

    public static void Apply(Mode mode, string accentHex)
    {
        try
        {
            if (Application.Current is null) return;
            Application.Current.RequestedThemeVariant = mode switch
            {
                Mode.Light => ThemeVariant.Light,
                Mode.System => ThemeVariant.Default,
                _ => ThemeVariant.Dark,
            };
            if (TryParseColor(accentHex, out var color))
            {
                Application.Current.Resources["ThemeAccent"] = new SolidColorBrush(color);
                Application.Current.Resources["AccentColor"] = color;
            }
        }
        catch (Exception ex) { AppLogger.Warn("ThemeService.Apply failed: " + ex.Message); }
    }

    public static bool TryParseColor(string hex, out Color color)
    {
        color = Colors.LimeGreen;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = Color.Parse(hex.StartsWith("#") ? hex : "#" + hex); return true; }
        catch { return false; }
    }
}
