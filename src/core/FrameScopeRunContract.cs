using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class FrameScopeSamplerEvidence
{
    public bool Required;
    public string ExecutablePath = "";
    public bool ExecutableAvailable;
    public bool Started;
    public int? Pid;
    public DateTime? StartedAt;
    public DateTime? ExitedAt;
    public int? ExitCode;
    public bool ExitedEarly;
    public bool StopRequested;
    public bool ForcedStop;
    public string CsvPath = "";
    public bool CsvExists;
    public long CsvBytes;
    public int ValidRows;
    public string ErrorTail = "";
    public string Status = "";
}

public static class FrameScopeRunContract
{
    public static void AddStatusFields(Dictionary<string, object> target, string prefix, FrameScopeSamplerEvidence evidence)
    {
        if (target == null || evidence == null || string.IsNullOrWhiteSpace(prefix)) return;
        target[prefix + "Required"] = evidence.Required;
        target[prefix + "Exe"] = evidence.ExecutablePath ?? "";
        target[prefix + "ExecutableAvailable"] = evidence.ExecutableAvailable;
        target[prefix + "Started"] = evidence.Started;
        target[prefix + "Pid"] = evidence.Pid;
        target[prefix + "StartedAt"] = evidence.StartedAt.HasValue ? evidence.StartedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "";
        target[prefix + "ExitedAt"] = evidence.ExitedAt.HasValue ? evidence.ExitedAt.Value.ToString("o", CultureInfo.InvariantCulture) : "";
        target[prefix + "ExitCode"] = evidence.ExitCode;
        target[prefix + "ExitedEarly"] = evidence.ExitedEarly;
        target[prefix + "StopRequested"] = evidence.StopRequested;
        target[prefix + "ForcedStop"] = evidence.ForcedStop;
        target[prefix + "CsvPath"] = evidence.CsvPath ?? "";
        target[prefix + "CsvExists"] = evidence.CsvExists;
        target[prefix + "CsvBytes"] = evidence.CsvBytes;
        target[prefix + "ValidRows"] = evidence.ValidRows;
        target[prefix + "Status"] = evidence.Status ?? NormalizeSamplerStatus(evidence);
        target[prefix + "ErrorTail"] = evidence.ErrorTail ?? "";
    }

    public static int CountValidCsvRows(string path, string[] requiredColumns)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
            {
                List<string> headers = ParseCsvLine(reader.ReadLine());
                if (headers == null || headers.Count == 0) return 0;
                Dictionary<string, int> indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++) indexes[headers[i].Trim()] = i;

                string[] required = requiredColumns ?? new string[0];
                foreach (string column in required)
                {
                    if (string.IsNullOrWhiteSpace(column) || !indexes.ContainsKey(column)) return 0;
                }

                int count = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    List<string> fields = ParseCsvLine(line);
                    if (fields == null) continue;
                    bool valid = true;
                    foreach (string column in required)
                    {
                        int index = indexes[column];
                        if (index >= fields.Count || string.IsNullOrWhiteSpace(fields[index]))
                        {
                            valid = false;
                            break;
                        }
                        if (string.Equals(column, "Time", StringComparison.OrdinalIgnoreCase))
                        {
                            DateTime parsed;
                            if (!DateTime.TryParse(fields[index], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out parsed))
                            {
                                valid = false;
                                break;
                            }
                        }
                    }
                    if (valid) count++;
                }
                return count;
            }
        }
        catch
        {
            return 0;
        }
    }

    public static string NormalizeSamplerStatus(FrameScopeSamplerEvidence evidence)
    {
        if (evidence == null) return "missing";
        if (evidence.Required && !evidence.ExecutableAvailable) return "missing";
        if (!evidence.Started) return "not-started";
        if (evidence.ExitedEarly) return "exited-early";
        if (evidence.ForcedStop) return "forced-stop";
        if (!evidence.Pid.HasValue || evidence.Pid.Value <= 0 ||
            !evidence.StartedAt.HasValue || !evidence.ExitedAt.HasValue ||
            !evidence.StopRequested || !evidence.ExitCode.HasValue || evidence.ExitCode.Value != 0)
        {
            return "failed";
        }
        if (!evidence.CsvExists || evidence.CsvBytes <= 0 || evidence.ValidRows <= 0) return "no-data";
        return "healthy";
    }

    public static bool IsSamplerHealthy(FrameScopeSamplerEvidence evidence)
    {
        return string.Equals(NormalizeSamplerStatus(evidence), "healthy", StringComparison.Ordinal);
    }

    public static string Classify(int validFrameRows, FrameScopeSamplerEvidence processSampler, FrameScopeSamplerEvidence systemSampler)
    {
        if (validFrameRows > 0)
        {
            return IsSamplerHealthy(processSampler) && IsSamplerHealthy(systemSampler) ? "full" : "partial";
        }

        int processRows = processSampler == null ? 0 : processSampler.ValidRows;
        int systemRows = systemSampler == null ? 0 : systemSampler.ValidRows;
        return processRows > 0 || systemRows > 0 ? "diagnostic" : "error";
    }

    private static List<string> ParseCsvLine(string line)
    {
        if (line == null) return null;
        List<string> fields = new List<string>();
        StringBuilder value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Length = 0;
            }
            else
            {
                value.Append(ch);
            }
        }
        if (quoted) return null;
        fields.Add(value.ToString());
        return fields;
    }
}
