using System;
using System.Collections.Generic;
using System.Globalization;

internal sealed partial class FrameScopeWebBridge
{
    private Dictionary<string, object> BuildTargetsPayload(string status)
    {
        ValidateConfigPath();
        FrameScopeConfig config = FrameScopeConfigStore.Load(options.ConfigPath);
        return new Dictionary<string, object>
        {
            { "status", status },
            { "configPath", options.ConfigPath },
            { "dataRoot", config.DataRoot },
            { "resolvedDataRoot", ResolveDataRoot(config.DataRoot) },
            { "openReportOnComplete", config.OpenReportOnComplete },
            { "enabledTargetCount", EnabledTargetCount(config) },
            { "targetCount", config.Targets == null ? 0 : config.Targets.Count },
            { "targets", config.Targets ?? new List<FrameScopeTarget>() },
            { "loadedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }
        };
    }

    private string SaveTargets(FrameScopeWebBridgeRequest request)
    {
        ValidateConfigPath();
        if (PayloadContainsPathAuthority(request.Payload) || request.Payload.ContainsKey("configPath"))
        {
            return ErrorResponse(request.RequestId, "path_not_allowed", "targets.save only accepts editable target fields, not filesystem authority.");
        }

        object targetsValue;
        if (!request.Payload.TryGetValue("targets", out targetsValue) || targetsValue == null)
        {
            return ErrorResponse(request.RequestId, "missing_targets", "targets.save requires payload.targets.");
        }

        FrameScopeConfig existing = FrameScopeConfigStore.Load(options.ConfigPath);
        List<FrameScopeTarget> targets = DecodeTargets(targetsValue);
        string dataRoot = request.Payload.ContainsKey("dataRoot") ? ReadString(request.Payload, "dataRoot") : existing.DataRoot;
        bool openReportOnComplete = ReadBool(request.Payload, "openReportOnComplete", existing.OpenReportOnComplete);
        FrameScopeConfig merged = FrameScopeConfigStore.BuildConfigFromEditableTargets(existing, dataRoot, openReportOnComplete, targets);
        ValidateConfigDataRoot(merged);
        FrameScopeConfigStore.Save(options.ConfigPath, merged);

        FrameScopeConfig reloaded = FrameScopeConfigStore.Load(options.ConfigPath);
        PublishEvent("event.status", new Dictionary<string, object>
        {
            { "status", "targets.saved" },
            { "requestId", request.RequestId },
            { "configPath", options.ConfigPath },
            { "enabledTargetCount", EnabledTargetCount(reloaded) }
        });
        return OkResponse(request.RequestId, BuildTargetsPayload("saved"));
    }

    private List<FrameScopeTarget> DecodeTargets(object value)
    {
        string serialized = json.Serialize(value);
        List<FrameScopeTarget> targets = json.Deserialize<List<FrameScopeTarget>>(serialized);
        if (targets == null) targets = new List<FrameScopeTarget>();
        foreach (FrameScopeTarget target in targets)
        {
            if (target == null) continue;
            target.ProcessName = FrameScopeTargetEditRules.NormalizeProcessName(target.ProcessName);
        }
        return targets;
    }
}
