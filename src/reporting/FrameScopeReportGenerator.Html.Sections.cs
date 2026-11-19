internal static partial class FrameScopeReportGenerator
{
    private static string ReportHtmlSidebar()
    {
        return @"<aside class='left'>
    <a class='brand' href='#report-top' aria-label='返回报告顶部'><span class='brand-mark'>F</span>FrameScope</a>
    <div class='brand-sub'>性能分析报告</div>
    <div class='nav-label'>分析视图</div>
    <nav class='tabs' aria-label='报告分析视图'>
      <button class='tab active' data-view='fps' aria-pressed='true'><span class='nav-index'>01</span>帧率</button>
      <button class='tab' data-view='cpuCore' aria-pressed='false'><span class='nav-index'>02</span>CPU 核心频率</button>
      <button class='tab' data-view='cpuVoltage' aria-pressed='false'><span class='nav-index'>03</span>CPU 电压 / Vcore</button>
      <button class='tab' data-view='cpuVid' aria-pressed='false'><span class='nav-index'>04</span>分核心电压（OSD）</button>
      <button class='tab' data-view='perf' aria-pressed='false'><span class='nav-index'>05</span>性能图表</button>
      <button class='tab' data-view='system' aria-pressed='false'><span class='nav-index'>06</span>系统占用</button>
      <button class='tab' data-view='process' aria-pressed='false'><span class='nav-index'>07</span>后台进程</button>
      <button class='tab' data-view='io' aria-pressed='false'><span class='nav-index'>08</span>IO / 温度</button>
    </nav>
    <div class='nav-help'><b>查看细节</b><p>滚轮缩放时间范围<br>按住鼠标拖动图表<br>悬停查看采样数值</p><span>本地报告 · 原始记录保留</span></div>
  </aside>
";
    }
    private static string ReportHtmlMainHeader()
    {
        return @"  <main class='main'>
    <header class='topbar' id='report-top'><div class='game'><div class='game-icon' id='gameIcon'>FS</div><div><div class='eyebrow'>SESSION REPORT</div><h1 class='game-name' id='gameName'>-</h1><div class='meta'><span id='runStart'>-</span><span id='runEnd'>-</span><span id='runDuration'>-</span></div></div></div><span class='report-badge'>性能报告</span></header>
    <section class='hardware-grid' aria-label='本次记录的硬件信息'>
      <div class='hardware-card'><h3>处理器</h3><div class='hardware-name' id='hwCpu'>-</div><div class='line'><span>核心 / 线程</span><b id='hwCore'>-</b></div><div class='line'><span>标称最大频率</span><b id='hwCpuClock'>-</b></div></div>
      <div class='hardware-card'><h3>显卡</h3><div class='hardware-name' id='hwGpu'>-</div><div class='line'><span>驱动</span><b id='hwDriver'>-</b></div><div class='line'><span>记录显存</span><b id='hwVram'>-</b></div></div>
      <div class='hardware-card'><h3>系统与内存</h3><div class='hardware-name' id='hwOs'>-</div><div class='line hardware-memory'><span>内存</span><b id='hwMem'>-</b></div><div class='line'><span>采样</span><b id='hwSamples'>-</b></div></div>
    </section>
    <div class='alert' id='captureAlert'></div>
    <section class='gauges' id='gauges' aria-label='本次采集摘要'></section>
    <div class='title'><div><div class='eyebrow'>ANALYSIS</div><h2 id='viewTitle'>帧率波动</h2></div><span id='viewNote'>原始采样保留，图表按宽度自适应绘制。</span></div>
";
    }
    private static string ReportHtmlChartToolbar()
    {
        return @"    <div class='toolbar'>
      <div class='left-tools'><select id='metricSelect' aria-label='图表指标'></select><select id='readMode' aria-label='阅读模式'><option value='spike'>保留尖峰</option><option value='trend'>趋势易读</option></select><label id='fpsFilterControl' hidden><input id='fpsFilter' type='checkbox' checked> 排除过高/过低 FPS</label><span id='fpsFilterInfo' class='mode-stats'></span><input id='processSearch' aria-label='搜索后台进程' class='search' placeholder='搜索进程，留空显示全部后台进程'></div>
      <div class='right-tools'><div class='range'>图表宽度 <input id='widthScale' aria-label='图表宽度' type='range' min='1' max='8' step='0.25' value='1'><b id='widthText'>1.00x</b></div><button class='tool' id='fitWidth'>适合窗口</button><button class='tool' id='resetView'>重置视图</button><button class='tool' id='exportPng'>导出 PNG</button></div>
    </div>
";
    }
    private static string ReportHtmlChartSurface()
    {
        return @"    <div class='chart-shell'><div class='chart-scroll' id='chartScroll'><div class='chartbox' id='chartBox'><canvas id='chart'></canvas><canvas id='overlay'></canvas><div class='tooltip' id='tooltip'></div></div></div><div class='chart-footer'><div class='legend' id='legend' aria-label='图例'></div><div class='mode-stats' id='modeStats'>-</div></div></div>
";
    }
    private static string ReportHtmlSummaryPanels()
    {
        return @"    <div class='panelgrid'><div class='card'><div class='section-heading'><h3>后台进程峰值</h3><span>CPU 占用</span></div><div class='rows' id='processRows'></div></div><div class='card'><div class='section-heading'><h3>本次帧率摘要</h3><span>采集区间统计</span></div><div class='rows' id='summaryRows'></div></div></div>
  </main>
</div>
";
    }}
