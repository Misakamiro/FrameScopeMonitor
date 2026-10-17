using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

internal sealed partial class FrameScopeWebBridge
{
    private Dictionary<string, object> BuildConfigPayload()
    {
        ValidateConfigPath();
        FrameScopeConfig config = FrameScopeConfigStore.Load(options.ConfigPath);
        return BuildConfigPayload(config, "loaded");
    }

    private string SaveConfig(FrameScopeWebBridgeRequest request)
    {
        ValidateConfigPath();
        using (FrameScopeConfigStore.AcquireMutationLock(options.ConfigPath))
        {
            return SaveConfigLocked(request);
        }
    }

    private string SaveConfigLocked(FrameScopeWebBridgeRequest request)
    {
        if (request.Payload.ContainsKey("path") || request.Payload.ContainsKey("configPath"))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "config.save always writes the host-validated FrameScope config path.");
        }

        object configValue;
        if (!request.Payload.TryGetValue("config", out configValue) || configValue == null)
        {
            return ErrorResponse(request.RequestId, "missing_config", "config.save requires payload.config.");
        }

        FrameScopeConfig config = DecodeConfig(configValue);
        // Settings sends a complete draft, including targets captured before
        // another targets.save. Only the targets endpoint edits that list.
        config.Targets = FrameScopeConfigStore.Load(options.ConfigPath).Targets;
        ValidateConfigDataRoot(config);
        FrameScopeConfigStore.Save(options.ConfigPath, config);
        FrameScopeConfig reloaded = FrameScopeConfigStore.Load(options.ConfigPath);
        PublishEvent("event.status", new Dictionary<string, object>
        {
            { "status", "config.saved" },
            { "requestId", request.RequestId },
            { "configPath", options.ConfigPath },
            { "enabledTargetCount", EnabledTargetCount(reloaded) }
        });
        return OkResponse(request.RequestId, BuildConfigPayload(reloaded, "saved"));
    }

    private Dictionary<string, object> BuildConfigPayload(FrameScopeConfig config, string status)
    {
        return new Dictionary<string, object>
        {
            { "status", status },
            { "configPath", options.ConfigPath },
            { "config", config },
            { "enabledTargetCount", EnabledTargetCount(config) },
            { "targetCount", config.Targets == null ? 0 : config.Targets.Count },
            { "resolvedDataRoot", ResolveDataRoot(config.DataRoot) },
            { "loadedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }
        };
    }

    private FrameScopeConfig DecodeConfig(object value)
    {
        string serialized = json.Serialize(value);
        FrameScopeConfig config = FrameScopeConfigStore.Deserialize(serialized);
        if (config == null) throw new InvalidOperationException("Config payload is empty.");
        FrameScopeConfigStore.Normalize(config);
        return config;
    }

    private void ValidateConfigPath()
    {
        if (string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            throw new InvalidOperationException("Config path is empty.");
        }

        if (!IsPathInsideRoot(options.ConfigPath))
        {
            throw new InvalidOperationException("Config path is outside the application root.");
        }
    }

    private void ValidateConfigDataRoot(FrameScopeConfig config)
    {
        if (config == null) throw new InvalidOperationException("Config payload is empty.");
        string dataRoot = ResolveDataRoot(config.DataRoot);
        if (string.IsNullOrWhiteSpace(dataRoot)) throw new InvalidOperationException("Data root is empty.");
        Path.GetFullPath(dataRoot);
    }
}
