using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

internal static partial class FrameScopeReportGenerator
{
    private sealed class CsvTable : IDisposable
    {
        private readonly StreamReader reader;
        public readonly Dictionary<string, int> Headers;

        private CsvTable(string path)
        {
            reader = new StreamReader(path, Encoding.UTF8, true, 1024 * 1024);
            List<string> headers = ParseLine(reader.ReadLine() ?? "");
            Headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++) Headers[headers[i]] = i;
        }

        public static CsvTable Open(string path)
        {
            return new CsvTable(path);
        }

        public List<string> ReadRow()
        {
            string line = reader.ReadLine();
            if (line == null) return null;
            return ParseLine(line);
        }

        public void Dispose()
        {
            reader.Dispose();
        }

        private static List<string> ParseLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
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
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    if (ch == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else if (ch == '"')
                    {
                        quoted = true;
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
