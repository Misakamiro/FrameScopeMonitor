internal static partial class FrameScopeReportGenerator
{
    private static string ReportHtmlSidebar()
    {
        return @"<aside class='left'>
    <div class='brand'>FrameScope</div><div class='brand-sub'>原生 C# 生成的单页性能报告</div>
    <div class='block'><h3>处理器</h3><div class='line'><span id='hwCpu'>-</span></div><div class='line'><span>核心/线程</span><b id='hwCore'>-</b></div><div class='line'><span>标称最大频率</span><b id='hwCpuClock'>-</b></div></div>
    <div class='block'><h3>显卡</h3><div class='line'><span id='hwGpu'>-</span></div><div class='line'><span>驱动版本</span><b id='hwDriver'>-</b></div><div class='line'><span>记录显存</span><b id='hwVram'>-</b></div></div>
    <div class='block'><h3>系统</h3><div class='line'><span id='hwOs'>-</span></div><div class='line'><span>内存</span><b id='hwMem'>-</b></div><div class='line'><span>采样点</span><b id='hwSamples'>-</b></div></div>
    <div class='block'><h3>图表操作</h3><div class='note'>图表数据保留原始采样点；宽度滑块只改变画布绘制密度，不改变 data.js 原始数据。</div></div>
  </aside>
";
    }
    private static string ReportHtmlMainHeader()
    {
        return @"  <main class='main'>
    <div class='topbar'><div class='game'><div class='game-icon' id='gameIcon'>FS</div><div><div class='game-name' id='gameName'>-</div><div class='meta'><span id='runStart'>-</span><span id='runEnd'>-</span><span id='runDuration'>-</span></div></div></div>
      <div class='tabs'><button class='tab active' data-view='fps'>帧率</button><button class='tab' data-view='cpuCore'>CPU 核心频率</button><button class='tab' data-view='cpuVoltage'>CPU 电压 / Vcore</button><button class='tab' data-view='cpuVid'>CPU 核心 VID（请求电压）</button><button class='tab' data-view='perf'>性能图表</button><button class='tab' data-view='system'>系统占用</button><button class='tab' data-view='process'>后台进程</button><button class='tab' data-view='io'>IO/温度</button></div>
    </div>
    <div class='title'><h2 id='viewTitle'>帧率波动</h2><span id='viewNote'>完整数据保留在本地 data.js，图表按宽度自适应绘制。</span></div>
    <div class='alert' id='captureAlert'></div>
    <div class='gauges' id='gauges'></div>
";
    }
    private static string ReportHtmlChartToolbar()
    {
        return @"    <div class='toolbar'>
      <div class='left-tools'><select id='metricSelect'></select><select id='readMode'><option value='spike'>保留尖峰</option><option value='trend'>趋势易读</option></select><input id='processSearch' class='search' placeholder='搜索进程，留空显示全部后台进程'></div>
      <div class='right-tools'><div class='mode-stats' id='modeStats'>-</div><div class='range'>图表宽度 <input id='widthScale' type='range' min='1' max='8' step='0.25' value='1'><b id='widthText'>1.00x</b></div><button class='tool' id='fitWidth'>适合窗口</button><button class='tool' id='resetView'>重置视图</button><button class='tool' id='exportPng'>导出 PNG</button><div class='legend' id='legend'></div></div>
    </div>
";
    }
    private static string ReportHtmlChartSurface()
    {
        return @"    <div class='chart-shell'><div class='chart-scroll' id='chartScroll'><div class='chartbox' id='chartBox'><canvas id='chart'></canvas><canvas id='overlay'></canvas><div class='tooltip' id='tooltip'></div></div></div></div>
";
    }
    private static string ReportHtmlSummaryPanels()
    {
        return @"    <div class='panelgrid'><div class='card'><h3>后台进程峰值</h3><div class='rows' id='processRows'></div></div><div class='card'><h3>本次帧率摘要</h3><div class='rows' id='summaryRows'></div></div></div>
  </main>
</div>
";
    }}
