using System;
using System.IO;

namespace PcLun.Services;

public static class AppLogger
{
    private static readonly object _lock = new();

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Error(string msg, Exception ex) => Write("ERROR", msg + " :: " + ex);

    private static void Write(string level, string msg)
    {
        lock (_lock)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
                Console.WriteLine(line);
                File.AppendAllText(Paths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // never throw from logger
            }
        }
    }
}
