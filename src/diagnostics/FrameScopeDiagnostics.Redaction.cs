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
    public static string RedactForPrivacy(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        string redacted = value;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            redacted = redacted.Replace(userProfile, "%USERPROFILE%");
            redacted = redacted.Replace(userProfile.Replace("\\", "\\\\"), "%USERPROFILE%");
        }
        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            redacted = Regex.Replace(redacted, Regex.Escape(userName), "%USERNAME%", RegexOptions.IgnoreCase);
        }
        redacted = Regex.Replace(redacted, "(?i)(token|password|secret|apikey|api_key|access_key)\\s*[:=]\\s*[^\\s,;\\]\\}\\\"]+", "$1=[redacted]");
        redacted = Regex.Replace(redacted, "(?i)\"(token|password|secret|apikey|api_key|access_key)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"[redacted]\"");
        return redacted;
    }

    private static Dictionary<string, object> RedactMap(Dictionary<string, object> map)
    {
        return RedactObject(map, "") as Dictionary<string, object>;
    }

    private static object RedactObject(object value, string key)
    {
        if (IsSensitiveKey(key)) return "[redacted]";
        if (value == null) return null;
        string text = value as string;
        if (text != null) return RedactForPrivacy(text);

        IDictionary<string, object> generic = value as IDictionary<string, object>;
        if (generic != null)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> pair in generic)
            {
                result[pair.Key] = RedactObject(pair.Value, pair.Key);
            }
            return result;
        }

        IDictionary dict = value as IDictionary;
        if (dict != null)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (object rawKey in dict.Keys)
            {
                string childKey = Convert.ToString(rawKey, CultureInfo.InvariantCulture);
                result[childKey] = RedactObject(dict[rawKey], childKey);
            }
            return result;
        }

        IEnumerable enumerable = value as IEnumerable;
        if (enumerable != null && !(value is string))
        {
            List<object> list = new List<object>();
            foreach (object item in enumerable) list.Add(RedactObject(item, key));
            return list;
        }

        return value;
    }

    private static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
