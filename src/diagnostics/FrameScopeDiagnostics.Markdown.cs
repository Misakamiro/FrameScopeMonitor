using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static partial class FrameScopeDiagnostics
{
    private static string BuildMarkdown(Dictionary<string, object> report)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# FrameScope Diagnostic Report");
        AppendSection(sb, "Software", GetMap(report, "software"));
        AppendSection(sb, "System", GetMap(report, "system"));
        AppendSection(sb, "Settings", GetMap(report, "settings"));
        AppendSection(sb, "Target Detection", report.ContainsKey("targetDetection") ? report["targetDetection"] : null);
        AppendSection(sb, "Recent Session", GetMap(report, "recentSession"));
        AppendSection(sb, "FPS Summary", GetMap(report, "fpsSummary"));
        AppendSection(sb, "Report Generation", GetMap(report, "reportGeneration"));
        AppendSection(sb, "Performance", GetMap(report, "performance"));
        AppendSection(sb, "Errors", GetMap(report, "errors"));
        AppendSection(sb, "Capture Chain", GetMap(report, "captureChain"));
        return RedactForPrivacy(sb.ToString());
    }

    private static void AppendSection(StringBuilder sb, string title, object value)
    {
        sb.AppendLine();
        sb.AppendLine("## " + title);
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(Json.Serialize(value ?? new Dictionary<string, object>()));
        sb.AppendLine("```");
    }
}
