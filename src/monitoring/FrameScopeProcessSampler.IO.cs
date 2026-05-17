using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

internal static partial class FrameScopeProcessSampler
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

    private static string BaseName(string processName)
    {
        if (String.IsNullOrWhiteSpace(processName)) return "cs2";
        try { return Path.GetFileNameWithoutExtension(processName); }
        catch { return processName; }
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

    private static double Round(double value, int digits)
    {
        return Math.Round(value, digits);
    }

    private static double Value(double? value)
    {
        return value.HasValue ? value.Value : 0.0;
    }
}
