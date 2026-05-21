using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

internal sealed partial class FrameScopeWebBridge
{
    private Dictionary<string, object> BuildStateSnapshotPayload()
    {
        FrameScopeConfig config = FrameScopeConfigStore.Load(options.ConfigPath);
        int watcherPid;
        bool watcherRunning = IsWatcherRunning(out watcherPid);
        Dictionary<string, object> watcherState = ReadJsonFile(options.StatePath);

        return new Dictionary<string, object>
        {
            { "bridgeStatus", "ready" },
            { "bridgeVersion", 1 },
            { "generatedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) },
            { "root", options.Root },
            { "watcher", new Dictionary<string, object>
                {
                    { "running", watcherRunning },
                    { "pid", watcherPid },
                    { "statePath", options.StatePath },
                    { "completedRuns", ReadInt(watcherState, "CompletedRuns", 0) },
                    { "lastReport", ReadString(watcherState, "LastReport") },
                    { "lastError", ReadString(watcherState, "LastError") }
                }
            },
            { "config", new Dictionary<string, object>
                {
                    { "exists", File.Exists(options.ConfigPath) },
                    { "path", options.ConfigPath },
                    { "enabledTargetCount", EnabledTargetCount(config) },
                    { "targetCount", config.Targets == null ? 0 : config.Targets.Count },
                    { "dataRoot", ResolveDataRoot(config.DataRoot) }
                }
            },
            { "reports", new Dictionary<string, object>
                {
                    { "historyPath", options.HistoryPath },
                    { "historyExists", File.Exists(options.HistoryPath) }
                }
            }
        };
    }

    private bool IsWatcherRunning(out int pid)
    {
        pid = 0;
        Dictionary<string, object> state = ReadJsonFile(options.StatePath);
        pid = ReadInt(state, "WatcherPid", 0);
        if (pid <= 0) return false;
        try
        {
            using (Process.GetProcessById(pid))
            {
            }
            return true;
        }
        catch
        {
            pid = 0;
            return false;
        }
    }

    private Dictionary<string, object> ReadJsonFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var parsed = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            return parsed == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string ResolveDataRoot(string configuredRoot)
    {
        string dataRoot = configuredRoot;
        if (string.IsNullOrWhiteSpace(dataRoot)) dataRoot = FrameScopeConfigStore.DefaultDataRoot;
        try
        {
            if (!Path.IsPathRooted(dataRoot)) dataRoot = Path.Combine(options.Root, dataRoot);
            return Path.GetFullPath(dataRoot);
        }
        catch
        {
            return FrameScopeConfigStore.DefaultDataRoot;
        }
    }

    private static int EnabledTargetCount(FrameScopeConfig config)
    {
        if (config == null || config.Targets == null) return 0;
        int count = 0;
        foreach (FrameScopeTarget target in config.Targets)
        {
            if (target != null && target.Enabled) count++;
        }
        return count;
    }
}
