using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PcLun.Services;

public static class SystemInfo
{
    public static long TotalRamMb => GetTotalRamMb();

    public static int RecommendedRamMb()
    {
        var total = TotalRamMb;
        if (total <= 0) return 2048;
        if (total <= 2048) return 1024;       // <=2 GB
        if (total <= 4096) return 1536;       // 4 GB
        if (total <= 6144) return 2048;       // 6 GB
        if (total <= 8192) return 2560;       // 8 GB
        if (total <= 12288) return 3584;      // 12 GB
        return 4096;                          // 16+ GB
    }

    public static string OsName => RuntimeInformation.OSDescription;
    public static string Arch => RuntimeInformation.ProcessArchitecture.ToString();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private static long GetTotalRamMb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var s = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(s))
                    return (long)(s.ullTotalPhys / 1024UL / 1024UL);
            }
            else if (File.Exists("/proc/meminfo"))
            {
                foreach (var line in File.ReadAllLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                            return kb / 1024;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
        return 0;
    }
}
