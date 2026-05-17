using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class FrameScopePresentMonPlan
{
    public FrameScopePresentMonPlan()
    {
        CaptureMode = "";
        CaptureTarget = "";
        Arguments = new List<string>();
    }

    public string CaptureMode { get; set; }
    public string CaptureTarget { get; set; }
    public List<string> Arguments { get; set; }
}

public static class FrameScopeCapturePlanner
{
    public static List<string> BuildTargetProcessBaseNames(string processNames, string displayName)
    {
        List<string> result = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (processNames ?? "").Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string baseName = GetTargetBaseName(part);
            if (string.IsNullOrWhiteSpace(baseName)) continue;
            if (seen.Add(baseName)) result.Add(baseName);
        }

        if (IsPubgTarget(processNames, displayName, result))
        {
            if (seen.Add("TslGame")) result.Add("TslGame");
            if (seen.Add("TslGame-Win64-Shipping")) result.Add("TslGame-Win64-Shipping");
        }

        return result;
    }

    public static bool ShouldUseProcessNameCapture(List<string> processBaseNames, string configuredProcessName, string displayName)
    {
        if (IsPubgTarget(configuredProcessName, displayName, processBaseNames)) return true;
        return processBaseNames != null && processBaseNames.Count > 1;
    }

    public static FrameScopePresentMonPlan CreatePresentMonPlan(
        List<string> processBaseNames,
        string configuredProcessName,
        string displayName,
        int selectedPid,
        string outputFile,
        string sessionName,
        bool timedCapture,
        int captureSeconds)
    {
        List<string> aliases = processBaseNames == null
            ? new List<string>()
            : processBaseNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool useProcessNameCapture = ShouldUseProcessNameCapture(aliases, configuredProcessName, displayName);
        FrameScopePresentMonPlan plan = new FrameScopePresentMonPlan
        {
            CaptureMode = useProcessNameCapture ? "process_name" : "process_id",
            CaptureTarget = useProcessNameCapture
                ? string.Join(";", aliases.Select(EnsureExe).ToArray())
                : selectedPid.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (useProcessNameCapture)
        {
            foreach (string alias in aliases)
            {
                plan.Arguments.Add("--process_name");
                plan.Arguments.Add(EnsureExe(alias));
            }
        }
        else
        {
            plan.Arguments.Add("--process_id");
            plan.Arguments.Add(plan.CaptureTarget);
        }

        plan.Arguments.AddRange(new[]
        {
            "--output_file", outputFile ?? "",
            "--date_time",
            "--terminate_on_proc_exit",
            "--no_console_stats",
            "--stop_existing_session",
            "--session_name", sessionName ?? ""
        });

        if (timedCapture && captureSeconds > 0)
        {
            plan.Arguments.AddRange(new[] { "--timed", captureSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--terminate_after_timed" });
        }

        return plan;
    }

    public static Dictionary<string, object> CreateTargetNotFoundDiagnostic(IEnumerable<string> processBaseNames, int initialTargetPid, string displayName, int waitSeconds)
    {
        string candidates = string.Join(";", (processBaseNames ?? Enumerable.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray());
        string target = string.IsNullOrWhiteSpace(displayName) ? candidates : displayName.Trim();
        bool pubg = IsPubgTarget(candidates, target, processBaseNames == null ? null : processBaseNames.ToList());
        string message = pubg
            ? "等待超时前没有找到 PUBG 渲染进程或稳定游戏窗口。请确认 PUBG 已进入最终游戏窗口，FrameScope 与游戏权限级别一致；仍失败时以管理员身份运行 FrameScope，并优先使用无边框或窗口化全屏。"
            : "等待超时前没有找到目标进程或稳定游戏窗口。请检查进程名配置、启动时序和权限级别。";

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            { "TargetProcessCandidates", candidates },
            { "InitialTargetPid", initialTargetPid },
            { "WaitSeconds", waitSeconds },
            { "WindowWaitStatus", "waiting-timeout" },
            { "FrameCaptureStatus", "target-not-found" },
            { "FrameCaptureMessage", message }
        };
    }

    private static bool IsPubgTarget(string processNames, string displayName, IEnumerable<string> aliases)
    {
        string aliasText = aliases == null ? "" : string.Join(";", aliases.ToArray());
        string probe = ((processNames ?? "") + " " + (displayName ?? "") + " " + aliasText).ToLowerInvariant();
        return probe.Contains("pubg") || probe.Contains("tslgame");
    }

    private static string EnsureExe(string baseName)
    {
        string value = (baseName ?? "").Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : value + ".exe";
    }

    private static string GetTargetBaseName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "";
        try { return Path.GetFileNameWithoutExtension(processName.Trim()); }
        catch { return processName.Trim(); }
    }
}
