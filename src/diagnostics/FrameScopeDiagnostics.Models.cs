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

public sealed class FrameScopeDiagnosticReportResult
{
    public string DirectoryPath { get; set; }
    public string MarkdownPath { get; set; }
    public string JsonPath { get; set; }
    public FrameScopeDiagnosticCleanupResult Cleanup { get; set; }
}

public sealed class FrameScopeDiagnosticCleanupResult
{
    public int FilesDeleted { get; set; }
    public long BytesDeleted { get; set; }
}
