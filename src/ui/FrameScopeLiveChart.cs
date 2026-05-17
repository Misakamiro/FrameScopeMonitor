using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class FrameScopeLiveSnapshot
{
    public bool HasRealData;
    public string RunDir = "";
    public string TargetName = "";
    public string SourceLabel = "";
    public string ProcessName = "";
    public string MemoryLabel = "";
    public double CurrentFps;
    public double AverageFps;
    public double Low1Fps;
    public double Low01Fps;
    public double AverageFrameMs;
    public double CpuPct;
    public double GpuPct;
    public int FrameCount;
    public readonly List<double> FpsValues = new List<double>();
    public readonly List<double> FrameMsValues = new List<double>();
    public readonly List<string> LogLines = new List<string>();
}

internal sealed class FrameScopeMiniChartPanel : Panel
{
    public string ChartTitle { get; set; }
    public string ChartSubtitle { get; set; }
    public string Unit { get; set; }
    public Color Accent { get; set; }
    public List<double> Values { get; set; }

    public FrameScopeMiniChartPanel()
    {
        DoubleBuffered = true;
        ChartTitle = "";
        ChartSubtitle = "";
        Unit = "";
        Accent = Color.FromArgb(41, 230, 255);
        Values = new List<double>();
        BackColor = Color.FromArgb(8, 24, 42);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255)))
        using (var subBrush = new SolidBrush(Color.FromArgb(185, 200, 216)))
        using (var gridPen = new Pen(Color.FromArgb(45, 66, 104), 1f))
        using (var axisPen = new Pen(Color.FromArgb(90, 100, 150, 190), 1f))
        using (var linePen = new Pen(Accent, 2.2f))
        {
            using (var titleFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
            using (var subFont = new Font("Microsoft YaHei UI", 8.5f))
            {
                e.Graphics.DrawString(ChartTitle, titleFont, titleBrush, 4, 2);
                e.Graphics.DrawString(ChartSubtitle, subFont, subBrush, 4, 26);
            }

            var plot = new Rectangle(8, 56, Math.Max(1, Width - 18), Math.Max(1, Height - 70));
            for (int i = 0; i < 4; i++)
            {
                int y = plot.Top + (int)Math.Round(plot.Height * (i / 3.0));
                e.Graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            }
            e.Graphics.DrawRectangle(axisPen, plot);

            var finite = Values == null ? new List<double>() : Values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (finite.Count < 2)
            {
                using (var emptyFont = new Font("Microsoft YaHei UI", 10f))
                {
                    e.Graphics.DrawString("暂无可绘制数据", emptyFont, subBrush, plot.Left + 12, plot.Top + 18);
                }
                return;
            }

            double min = finite.Min();
            double max = finite.Max();
            if (Math.Abs(max - min) < 0.001)
            {
                max += 1;
                min -= 1;
            }

            var points = new List<PointF>();
            int count = finite.Count;
            for (int i = 0; i < count; i++)
            {
                double normalized = (finite[i] - min) / (max - min);
                float x = plot.Left + (float)(i * (plot.Width / Math.Max(1.0, count - 1.0)));
                float y = plot.Bottom - (float)(normalized * plot.Height);
                points.Add(new PointF(x, y));
            }
            if (points.Count > 1) e.Graphics.DrawLines(linePen, points.ToArray());
            using (var axisFont = new Font("Segoe UI", 8f))
            {
                e.Graphics.DrawString(max.ToString("0.#", CultureInfo.InvariantCulture) + Unit, axisFont, subBrush, plot.Left + 4, plot.Top + 3);
                e.Graphics.DrawString(min.ToString("0.#", CultureInfo.InvariantCulture) + Unit, axisFont, subBrush, plot.Left + 4, plot.Bottom - 18);
            }
        }
    }
}
