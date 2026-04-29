using System;

namespace PcLun.Services;

/// <summary>
/// Streamer-mode masking. Accessor returns either the real value or a redacted
/// placeholder when the user has streamer-mode enabled.
/// </summary>
public static class StreamerMode
{
    public static bool Enabled { get; set; }

    public static string MaskNick(string real) =>
        !Enabled || string.IsNullOrEmpty(real) ? real : "Player***";

    public static string MaskAddress(string real) =>
        !Enabled || string.IsNullOrEmpty(real) ? real : "***hidden***";

    public static string MaskToken(string real) =>
        !Enabled || string.IsNullOrEmpty(real) ? real : new string('•', Math.Min(real.Length, 16));
}
