using System.Collections.Generic;

internal static partial class FrameScopeReportGenerator
{
    private static object GetDiagnostic(Dictionary<string, object> map, string key)
    {
        return map.ContainsKey(key) ? map[key] : null;
    }
}
