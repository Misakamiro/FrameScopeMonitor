using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

internal sealed partial class FrameScopeWebBridge
{
    private string StartProcessRefresh(FrameScopeWebBridgeRequest request)
    {
        string query = ReadString(request.Payload, "query");
        if (Interlocked.CompareExchange(ref processRefreshInFlight, 1, 0) != 0)
        {
            PublishEvent("event.status", new Dictionary<string, object>
            {
                { "status", "processes.in_flight" },
                { "requestId", request.RequestId },
                { "query", query }
            });
            return OkResponse(request.RequestId, new Dictionary<string, object>
            {
                { "status", "in_flight" },
                { "requestId", request.RequestId },
                { "message", "A process refresh is already running." }
            });
        }

        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                PublishEvent("event.status", new Dictionary<string, object>
                {
                    { "status", "processes.refreshing" },
                    { "requestId", request.RequestId },
                    { "query", query }
                });

                List<FrameScopeProcessPickerItem> items = FrameScopeProcessPicker.FilterAndSortItems(
                    FrameScopeProcessPicker.EnumerateRunningProcesses(false),
                    query,
                    FrameScopeProcessPicker.SortRecent);

                List<Dictionary<string, object>> processPayload = new List<Dictionary<string, object>>();
                foreach (FrameScopeProcessPickerItem item in items.Take(250))
                {
                    processPayload.Add(new Dictionary<string, object>
                    {
                        { "processName", item.ProcessName ?? "" },
                        { "processId", item.ProcessId },
                        { "windowTitle", item.WindowTitle ?? "" },
                        { "displayText", item.DisplayText ?? "" }
                    });
                }

                PublishEvent("event.processesRefreshed", new Dictionary<string, object>
                {
                    { "requestId", request.RequestId },
                    { "status", "completed" },
                    { "query", query },
                    { "count", processPayload.Count },
                    { "truncated", items.Count > processPayload.Count },
                    { "processes", processPayload },
                    { "refreshedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) }
                });
            }
            catch (Exception ex)
            {
                PublishEvent("event.error", new Dictionary<string, object>
                {
                    { "requestId", request.RequestId },
                    { "code", "process_refresh_failed" },
                    { "message", ex.Message }
                });
            }
            finally
            {
                Interlocked.Exchange(ref processRefreshInFlight, 0);
            }
        });

        return OkResponse(request.RequestId, new Dictionary<string, object>
        {
            { "status", "accepted" },
            { "requestId", request.RequestId },
            { "message", "Process refresh accepted. Results will be pushed as event.processesRefreshed." }
        });
    }
}
