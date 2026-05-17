internal static partial class FrameScopeReportGenerator
{
    private static string ReportHtmlDocumentStart()
    {
        return @"<!doctype html>
<html lang='zh-CN'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>FrameScope 性能报告</title>
";
    }
    private static string ReportHtmlBodyStart()
    {
        return @"</head>
<body>
<div class='shell'>
  ";
    }
    private static string ReportHtmlDocumentEnd()
    {
        return @"</body>
</html>";
    }}
