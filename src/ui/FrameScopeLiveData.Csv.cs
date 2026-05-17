using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

internal static partial class FrameScopeNativeMonitor
{
    private static string ReadFirstLine(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            return reader.ReadLine() ?? "";
        }
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        var result = new List<string>();
        if (maxLines <= 0) return result;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long length = stream.Length;
            int bytes = (int)Math.Min(length, 1024 * 1024);
            byte[] buffer = new byte[bytes];
            stream.Seek(-bytes, SeekOrigin.End);
            int read = stream.Read(buffer, 0, bytes);
            string text = Encoding.UTF8.GetString(buffer, 0, read);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (length > bytes && lines.Count > 0) lines.RemoveAt(0);
            return lines.Skip(Math.Max(0, lines.Count - maxLines)).ToList();
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < (line ?? "").Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else sb.Append(ch);
            }
            else if (ch == ',')
            {
                fields.Add(sb.ToString());
                sb.Length = 0;
            }
            else if (ch == '"') quoted = true;
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static int HeaderIndex(List<string> header, string name)
    {
        if (header == null) return -1;
        for (int i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static bool TryCsvDouble(List<string> row, int index, out double value)
    {
        value = 0;
        if (row == null || index < 0 || index >= row.Count) return false;
        return double.TryParse(row[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
