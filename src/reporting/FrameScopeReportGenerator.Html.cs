internal static partial class FrameScopeReportGenerator
{
    private static string MakeHtml()
    {
        return ReportHtmlDocumentStart()
            + ReportHtmlStyles()
            + ReportHtmlBodyStart()
            + ReportHtmlSidebar()
            + ReportHtmlMainHeader()
            + ReportHtmlChartToolbar()
            + ReportHtmlChartSurface()
            + ReportHtmlSummaryPanels()
            + ReportHtmlScripts()
            + ReportHtmlDocumentEnd();
    }
}