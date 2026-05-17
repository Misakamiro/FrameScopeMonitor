using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

internal static partial class FrameScopeSystemSampler
{
    private static GpuSnapshot QueryGpu(string nvidiaSmi)
    {
        if (String.IsNullOrWhiteSpace(nvidiaSmi) || !File.Exists(nvidiaSmi)) return null;

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = nvidiaSmi,
                Arguments = "--query-gpu=utilization.gpu,utilization.memory,temperature.gpu,pstate,clocks.gr,clocks.mem,power.draw,memory.used,memory.total --format=csv,noheader,nounits",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(psi))
            {
                if (process == null) return null;
                string line = process.StandardOutput.ReadLine();
                if (!process.WaitForExit(2500))
                {
                    try { process.Kill(); }
                    catch { }
                    return null;
                }
                if (String.IsNullOrWhiteSpace(line)) return null;
                string[] parts = line.Split(',').Select(part => part.Trim()).ToArray();
                if (parts.Length < 9) return null;

                return new GpuSnapshot
                {
                    GpuUtilPct = ParseDouble(parts[0]),
                    GpuMemUtilPct = ParseDouble(parts[1]),
                    GpuTempC = ParseDouble(parts[2]),
                    GpuPState = parts[3],
                    GpuClockMHz = ParseDouble(parts[4]),
                    MemClockMHz = ParseDouble(parts[5]),
                    PowerW = ParseDouble(parts[6]),
                    VramUsedMiB = ParseDouble(parts[7]),
                    VramTotalMiB = ParseDouble(parts[8])
                };
            }
        }
        catch
        {
            return null;
        }
    }
}
