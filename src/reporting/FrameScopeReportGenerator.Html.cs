internal static partial class FrameScopeReportGenerator
{
    private static string MakeHtml()
    {
        using (var stream = typeof(FrameScopeReportGenerator).Assembly.GetManifestResourceStream("FrameScopeReportApplication"))
        {
            if (stream != null) using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8)) return reader.ReadToEnd();
        }
        throw new System.InvalidOperationException("Shared report application resource is missing; rebuild the report generator before publishing.");
    }
}
