using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

internal static partial class FrameScopeSystemSampler
{
    private static string Arg(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(args[i], name) && i + 1 < args.Length) return args[i + 1];
        }
        return fallback;
    }

    private static int ParseInt(string text, int fallback)
    {
        int value;
        if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
        return fallback;
    }

    private static bool ParseBool(string text, bool fallback)
    {
        if (String.IsNullOrWhiteSpace(text)) return fallback;
        bool value;
        if (Boolean.TryParse(text, out value)) return value;
        if (String.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (String.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return fallback;
    }

    private static double? ParseDouble(string text)
    {
        double value;
        if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return null;
    }

    private static string BaseName(string processName)
    {
        if (String.IsNullOrWhiteSpace(processName)) return "cs2";
        try { return Path.GetFileNameWithoutExtension(processName); }
        catch { return processName; }
    }

    private static List<string> ResolveTargetAliases(string[] args)
    {
        string fallback = BaseName(Arg(args, "--target", "cs2"));
        return FrameScopeTargetLifecycle.ParseBaseNameAliases(Arg(args, "--target-aliases", ""), fallback);
    }

    private static bool ShouldStopSampling(bool parentOwned, bool parentRunning, bool anyAliasRunning, bool stopRequested)
    {
        return stopRequested || FrameScopeTargetLifecycle.ShouldStopSampler(parentOwned, parentRunning, anyAliasRunning);
    }

    private static bool StopRequested(string stopFile)
    {
        if (String.IsNullOrWhiteSpace(stopFile)) return false;
        try { return File.Exists(stopFile); }
        catch { return false; }
    }

    private static void EnsureParent(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private static StreamWriter Writer(string path)
    {
        FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
        StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024 * 1024);
        writer.NewLine = "\r\n";
        return writer;
    }

    private static void WriteCsv(StreamWriter writer, IEnumerable<object> values)
    {
        writer.WriteLine(String.Join(",", values.Select(Csv)));
    }

    private static string Csv(object value)
    {
        if (value == null) return "";
        string text;
        IFormattable formattable = value as IFormattable;
        if (formattable != null) text = formattable.ToString(null, CultureInfo.InvariantCulture);
        else text = value.ToString();
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }

    private static object Round(double? value, int digits)
    {
        if (!value.HasValue) return "";
        return Math.Round(value.Value, digits);
    }
}
