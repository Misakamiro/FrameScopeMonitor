using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal static partial class FrameScopeNativeMonitor
{
    private static Control LogPanelCard(FrameScopeLiveSnapshot snapshot)
    {
        var card = GlassCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(18);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = card.BackColor, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        header.Controls.Add(new Label { Text = "实时日志", Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        liveLogPauseButton = CompactDashboardButton(liveLogPaused ? "继续" : "暂停", liveLogPaused ? "primary" : "secondary");
        liveLogPauseButton.Click += (_, __) =>
        {
            liveLogPaused = !liveLogPaused;
            if (liveLogPauseButton != null) liveLogPauseButton.Text = liveLogPaused ? "继续" : "暂停";
            SetStatus(liveLogPaused ? "实时日志显示已暂停。" : "实时日志显示已继续。");
        };
        header.Controls.Add(liveLogPauseButton, 1, 0);

        liveLogClearButton = CompactDashboardButton("清空", "secondary");
        header.Controls.Add(liveLogClearButton, 2, 0);
        layout.Controls.Add(header, 0, 0);

        string signature = (snapshot == null ? "" : snapshot.SourceLabel) + "|" + string.Join("\n", snapshot == null ? new List<string>() : snapshot.LogLines);
        if (liveLogCleared && !string.Equals(signature, liveLogClearSignature, StringComparison.Ordinal))
        {
            liveLogCleared = false;
            liveLogClearSignature = "";
        }
        if (!liveLogPaused && !liveLogCleared)
        {
            liveLogDisplayLines = snapshot == null || snapshot.LogLines.Count == 0
                ? new List<string> { "[INFO] 等待监测启动", "[INFO] 暂无活动会话" }
                : snapshot.LogLines.Take(8).ToList();
        }

        string logText = liveLogCleared
            ? ""
            : string.Join("\r\n", liveLogDisplayLines.Take(8).Select(line => "● " + line));
        liveLogLabel = new Label { Text = logText, Dock = DockStyle.Fill, ForeColor = UiSubText, TextAlign = ContentAlignment.TopLeft };
        layout.Controls.Add(liveLogLabel, 0, 1);

        liveLogClearButton.Click += (_, __) =>
        {
            liveLogDisplayLines.Clear();
            liveLogCleared = true;
            liveLogClearSignature = signature;
            if (liveLogLabel != null) liveLogLabel.Text = "";
            SetStatus("已清空当前日志面板，持久化日志文件未删除。");
        };
        card.Controls.Add(layout);
        return card;
    }
}
