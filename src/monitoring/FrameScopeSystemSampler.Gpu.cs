using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
                // ReadLine blocks until a line arrives; a hung driver (TDR)
                // would stall the whole sampler loop forever. Read on a pool
                // thread and bound the wait, mirroring the hardware-service
                // client. A killed process closes the pipe, releasing the
                // stranded read.
                Task<string> readTask = Task.Factory.StartNew(
                    delegate { return process.StandardOutput.ReadLine(); });
                if (!readTask.Wait(2500))
                {
                    try { process.Kill(); }
                    catch { }
                    return null;
                }
                string line = readTask.Result;
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
