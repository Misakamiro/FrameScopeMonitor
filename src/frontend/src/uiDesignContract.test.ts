import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import appShellSource from "./layout/AppShell.tsx?raw";
import sidebarNavSource from "./layout/SidebarNav.tsx?raw";
import topStatusBarSource from "./layout/TopStatusBar.tsx?raw";
import overviewPageSource from "./pages/OverviewPage.tsx?raw";
import targetsPageSource from "./pages/TargetsPage.tsx?raw";
import reportsPageSource from "./pages/ReportsPage.tsx?raw";
import settingsPageSource from "./pages/SettingsPage.tsx?raw";
import aboutPageSource from "./pages/AboutPage.tsx?raw";
import chartShellSource from "./components/ChartShell.tsx?raw";
import appSource from "./App.tsx?raw";
import { navigationItems, visualFixtureModes } from "./data/mockPreview";

const layoutCssSource = readFileSync(new URL("./layout/layout.css", import.meta.url), "utf8");
const pagesCssSource = readFileSync(new URL("./pages/pages.css", import.meta.url), "utf8");
const componentsCssSource = readFileSync(new URL("./components/components.css", import.meta.url), "utf8");
const tokensCssSource = readFileSync(new URL("./theme/tokens.css", import.meta.url), "utf8");

function withoutImports(text: string) {
  return text
    .split("\n")
    .filter((line) => !line.trimStart().startsWith("import "))
    .join("\n");
}

function withoutImplementationIdentifiers(text: string) {
  return withoutImports(text)
    .replace(/bridgeState\./g, "")
    .replace(/\bbridgeState\b/g, "")
    .replace(/\bbridgeEnvironment\b/g, "")
    .replace(/\bBridgeEnvironment\b/g, "")
    .replace(/\bFrameScopeBridgeViewState\b/g, "")
    .replace(/data-smoke-[\w-]+="[^"]+"/g, "")
    .replace(/data-smoke-[\w-]+=\{[^}]*\}/g, "")
    .replace(/"webview2"/gi, '""');
}

describe("FrameScope UI design contract", () => {
  it("keeps the application shell native to the WebView client area", () => {
    const shellSources = [
      appShellSource,
      sidebarNavSource,
      layoutCssSource,
    ].join("\n");

    expect(shellSources).not.toMatch(/mac-window|window-controls/);
    expect(shellSources).not.toMatch(/Search|Bell|Notifications are not connected|not connected in this phase/);
  });

  it("pins the sidebar and leaves scrolling to the right-side page viewport", () => {
    expect(layoutCssSource).toMatch(/\.app-shell\s*{[^}]*height:\s*100dvh/s);
    expect(layoutCssSource).toMatch(/\.app-shell\s*{[^}]*overflow:\s*hidden/s);
    expect(layoutCssSource).toMatch(/\.sidebar\s*{[^}]*position:\s*sticky/s);
    expect(layoutCssSource).toMatch(/\.sidebar\s*{[^}]*height:\s*100dvh/s);
    expect(layoutCssSource).toMatch(/\.workspace\s*{[^}]*min-height:\s*0/s);
    expect(layoutCssSource).toMatch(/\.page-viewport\s*{[^}]*overflow-y:\s*auto/s);
  });

  it("uses the approved user-facing navigation labels", () => {
    expect(navigationItems.map((item) => item.label)).toEqual([
      "监控",
      "目标",
      "报告",
      "设置",
      "帮助",
    ]);
  });

  it("keeps the global top bar free of duplicate page titles", () => {
    const source = withoutImports(topStatusBarSource);
    expect(source).not.toMatch(/navigationItems\.find|<h1>|item\.label|item\.description/);
    expect(source).toMatch(/本机连接|正在监控|需要注意/);
  });

  it("keeps technical bridge details out of the main workflow pages", () => {
    const workflowSources = [
      overviewPageSource,
      targetsPageSource,
      reportsPageSource,
      settingsPageSource,
    ].map(withoutImplementationIdentifiers).join("\n");

    expect(workflowSources).not.toMatch(
      /WebView2|\bbridge\b|mock adapter|requestId|state\.snapshot|config\.get|config\.save|targets\.get|targets\.save|reports\.list/i,
    );
  });

  it("uses user-facing navigation descriptions instead of implementation notes", () => {
    expect(navigationItems.map((item) => item.description).join("\n")).not.toMatch(
      /静态预览|视觉结构|前端边界|版本|mock|bridge|WebView2/i,
    );
  });

  it("makes overview a current monitoring decision page", () => {
    expect(overviewPageSource).toContain("<h2>当前监控</h2>");
    expect(overviewPageSource).toContain("已启用目标");
    expect(overviewPageSource).toContain("打开最新报告");
    expect(overviewPageSource).toMatch(/primaryAction/);
    expect(overviewPageSource.match(/data-smoke-action=\{primaryAction\.smokeAction\}/g)).toHaveLength(1);
    expect(overviewPageSource).not.toContain("primaryAction.smokeAction}-hero");
    expect(overviewPageSource).not.toContain("metric-grid");
  });

  it("keeps target management in read mode until a row is edited", () => {
    expect(targetsPageSource).toContain("editingTargetIndex");
    expect(targetsPageSource).toContain("开始编辑");
    expect(targetsPageSource).toContain("添加目标");
    expect(targetsPageSource).toContain('variant={dirty ? "primary" : "secondary"}');
    expect(targetsPageSource).not.toContain("无修改");
    expect(targetsPageSource).not.toContain("读取目标");
  });

  it("keeps process search iconography tied to real search actions", () => {
    expect(targetsPageSource).not.toContain('<Search aria-hidden="true" size={18} />');
    expect(targetsPageSource).toContain("handleProcessSearchKeyDown");
    expect(targetsPageSource).toContain("process-search-field");
    expect(targetsPageSource).toContain('<Search aria-hidden="true" size={16}');
    expect(targetsPageSource).toContain("void bridgeState.refreshProcesses(query)");
  });

  it("presents process lookup as a compact toolbar and data list", () => {
    expect(targetsPageSource).toContain("process-search-toolbar");
    expect(targetsPageSource).toContain("process-search-field");
    expect(targetsPageSource).toContain("process-search-status");
    expect(targetsPageSource).toContain("process-result-summary");
    expect(targetsPageSource).toContain("process-result-list__head");
    expect(targetsPageSource).toContain("process-result-row--selected");
    expect(targetsPageSource).toContain('<Search aria-hidden="true" size={16}');
    expect(targetsPageSource).toContain('variant={query.trim() ? "tonal" : "secondary"}');
    expect(targetsPageSource).not.toContain('title="鏌ユ壘鐘舵€?');

    expect(pagesCssSource).toMatch(/\.process-search-toolbar\s*{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*116px/s);
    expect(pagesCssSource).toMatch(/\.process-search-field\s*{[^}]*position:\s*relative/s);
    expect(pagesCssSource).toMatch(/\.process-search-field input\s*{[^}]*padding-left:\s*38px/s);
    expect(pagesCssSource).toMatch(/\.process-result-list\s*{[^}]*max-height:\s*min\(360px,\s*46vh\)/s);
    expect(pagesCssSource).toMatch(/\.process-result-list\s*{[^}]*overflow-y:\s*auto/s);
    expect(pagesCssSource).toMatch(/\.process-result-row\s*{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*76px\s*minmax\(0,\s*1fr\)/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.process-search-toolbar\s*{[^}]*grid-template-columns:\s*1fr/s);
  });

  it("renders targets as labeled compact rows below 980px instead of compressed table cells", () => {
    expect(targetsPageSource).toContain("target-list__row--readonly");
    expect(targetsPageSource).toContain("target-list__identity");
    expect(targetsPageSource).toContain("target-list__process");
    expect(targetsPageSource).toContain("target-list__meta-grid");
    expect(targetsPageSource).toContain("compact-field-label");
    expect(targetsPageSource).toContain("报告");

    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.target-list__row--readonly\s*{[^}]*grid-template-columns:\s*1fr/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.target-list__meta-grid\s*{[^}]*grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.compact-field-label\s*{[^}]*display:\s*block/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.target-list__status\s*{[^}]*justify-self:\s*start/s);
  });

  it("structures reports as list plus selected detail instead of button-heavy rows", () => {
    expect(reportsPageSource).toContain("selectedReportId");
    expect(reportsPageSource).toContain("`open-directory-${smokeIndex}`");
    expect(reportsPageSource).toContain("`regenerate-report-${smokeIndex}`");
    expect(reportsPageSource).toContain('aria-hidden="true"');
    expect(reportsPageSource).toContain("tabIndex={-1}");
    expect(reportsPageSource).toContain("报告详情");
    expect(reportsPageSource).not.toContain('data-smoke-action="open-latest-report"');
    expect(reportsPageSource).not.toContain("报告编号");
    expect(reportsPageSource).not.toContain("内部编号");
  });

  it("turns report row more controls into an accessible action menu", () => {
    expect(reportsPageSource).toContain("openReportMenuId");
    expect(reportsPageSource).toContain('aria-haspopup="menu"');
    expect(reportsPageSource).toContain('role="menu"');
    expect(reportsPageSource).toContain('role="menuitem"');
    expect(reportsPageSource).toContain("handleReportMenuKeyDown");
    expect(reportsPageSource).toContain("bridgeState.openReportDirectory(report.reportId)");
    expect(reportsPageSource).toContain("bridgeState.regenerateReport(report.reportId)");
    expect(reportsPageSource).toContain("bridgeState.generateDiagnostics(report.reportId)");
  });

  it("keeps report more as a compact anchored MenuFlyout command", () => {
    expect(reportsPageSource).toContain('className="report-more-button report-more-button--icon"');
    expect(reportsPageSource).toContain('aria-label="报告更多操作"');
    expect(reportsPageSource).toContain('title="更多操作"');
    expect(reportsPageSource).not.toContain(">更多</Button>");
    expect(reportsPageSource).toContain("report-more-menu__divider");
    expect(reportsPageSource).toContain('data-menu-danger-zone="diagnostics"');
    expect(reportsPageSource).not.toMatch(/role="menuitem"[\s\S]{0,120}openReport\(report\.reportId\)/);

    expect(pagesCssSource).toMatch(/\.report-more-button--icon\s*{[^}]*width:\s*34px/s);
    expect(pagesCssSource).toMatch(/\.report-more-button--icon\s*{[^}]*min-width:\s*34px/s);
    expect(pagesCssSource).toMatch(/\.report-more-button--icon\s*{[^}]*display:\s*inline-flex/s);
    expect(pagesCssSource).toMatch(/\.report-more-button--icon\s*{[^}]*align-items:\s*center/s);
    expect(pagesCssSource).toMatch(/\.report-more-button--icon\s*{[^}]*justify-content:\s*center/s);
    expect(pagesCssSource).toMatch(/\.report-more-button--icon svg\s*{[^}]*display:\s*block/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[^}]*width:\s*204px/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[^}]*border-radius:\s*var\(--fs-menu-radius\)/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[^}]*right:\s*0/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[^}]*top:\s*calc\(100%\s*\+\s*var\(--fs-space-1\)\)/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu button\s*{[^}]*min-height:\s*38px/s);
  });

  it("labels compact report values and keeps detail as a low-weight inspector", () => {
    expect(reportsPageSource).toContain("report-list-row__metric");
    expect(reportsPageSource).toContain("report-list-row__status");
    expect(reportsPageSource).toContain("compact-field-label");
    expect(reportsPageSource).toContain("帧数");
    expect(reportsPageSource).toContain("大小");
    expect(reportsPageSource).not.toMatch(/function ReportDetail[\s\S]*bridgeState\.openReport\(report\.reportId\)/);

    expect(pagesCssSource).toMatch(/\.report-detail-panel\s*{[^}]*background:\s*var\(--fs-inspector-bg\)/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.report-list-row__select\s*{[^}]*grid-template-columns:\s*1fr/s);
    expect(pagesCssSource).toMatch(/@media \(max-width:\s*980px\)[\s\S]*\.report-list-row__metric\s*{[^}]*display:\s*grid/s);
  });

  it("gives sidebar navigation clear hover active and keyboard focus affordances", () => {
    expect(layoutCssSource).toMatch(/\.nav-item\s*{[^}]*cursor:\s*pointer/s);
    expect(layoutCssSource).toContain(".nav-item:hover");
    expect(layoutCssSource).toContain(".nav-item:focus-visible");
    expect(layoutCssSource).toMatch(/\.nav-item--active\s*{[^}]*border-color:\s*transparent/s);
    expect(layoutCssSource).toMatch(/@media \(max-width: 1080px\)[\s\S]*\.nav-item\s*{[^}]*min-height:\s*48px/s);
  });

  it("styles sidebar as a stable navigation rail instead of a button wall", () => {
    expect(sidebarNavSource).toContain("nav-item__rail");
    expect(sidebarNavSource).toContain("data-compact-label={item.label}");
    expect(sidebarNavSource).toContain("sidebar__status-dot");
    expect(sidebarNavSource).not.toContain("nav-active-indicator");
    expect(sidebarNavSource).not.toContain("data-active-index");
    expect(sidebarNavSource).not.toContain("nav-item__active-bg");
    expect(layoutCssSource).not.toContain(".nav-active-indicator");
    expect(layoutCssSource).toMatch(/\.nav-item\s*{[^}]*min-height:\s*50px/s);
    expect(layoutCssSource).toMatch(/\.nav-item\s*{[^}]*background:\s*transparent/s);
    expect(layoutCssSource).toMatch(/\.nav-item--active\s*{[^}]*background:\s*var\(--fs-nav-active-bg\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item__rail\s*{[^}]*width:\s*3px/s);
    expect(layoutCssSource).toMatch(/\.nav-item__rail\s*{[^}]*top:\s*var\(--fs-nav-rail-inset\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item__rail\s*{[^}]*bottom:\s*var\(--fs-nav-rail-inset\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item--active\s+\.nav-item__rail\s*{[^}]*background:\s*var\(--fs-accent-primary\)/s);
    expect(layoutCssSource).toMatch(/\.sidebar__status\s*{[^}]*border-color:\s*transparent/s);
    expect(layoutCssSource).toMatch(/@media \(max-width: 1080px\)[\s\S]*\.nav-item\s*{[^}]*width:\s*56px/s);
    expect(layoutCssSource).toMatch(/@media \(max-width: 1080px\)[\s\S]*\.nav-item strong\s*{[^}]*display:\s*block/s);
    expect(layoutCssSource).toMatch(/@media \(max-width: 1080px\)[\s\S]*\.nav-item__rail\s*{[^}]*left:\s*0/s);
    expect(layoutCssSource).toMatch(/@media \(max-width: 1080px\)[\s\S]*\.sidebar__status\s*{[^}]*background:\s*transparent/s);
  });

  it("splits settings into the approved user-facing groups", () => {
    expect(settingsPageSource).toContain("数据与报告");
    expect(settingsPageSource).toContain("日志与诊断");
    expect(settingsPageSource).toContain("采样间隔");
    expect(settingsPageSource).toContain('variant={dirty ? "primary" : "secondary"}');
    expect(settingsPageSource).not.toContain("无修改");
    expect(settingsPageSource).not.toContain("基础配置");
  });

  it("does not expose the legacy watcher poll interval in normal Settings", () => {
    expect(settingsPageSource).not.toContain("监控刷新");
    expect(settingsPageSource).not.toContain("状态刷新间隔");
    expect(settingsPageSource).not.toContain('data-smoke-field="poll-interval"');
    expect(settingsPageSource).not.toContain("PollIntervalMs");
  });

  it("does not expose real CPU Vcore configuration in normal Settings", () => {
    expect(settingsPageSource).not.toContain("CPU 核心电压采集");
    expect(settingsPageSource).not.toContain("CollectCpuVoltage");
    expect(settingsPageSource).not.toContain("VoltageProvider");
    expect(settingsPageSource).toContain("CPU Core VID");
    expect(settingsPageSource).toContain("请求/目标电压");
  });

  it("keeps sampling configuration global instead of per-target", () => {
    expect(settingsPageSource).toContain("TelemetrySampleIntervalMs");
    expect(settingsPageSource).toContain('smokeField="global-telemetry-sample-interval"');
    expect(targetsPageSource).not.toContain('data-smoke-field={`target-sample-${index}`}');
    expect(targetsPageSource).not.toMatch(/onUpdate\(index,\s*"SampleIntervalMs"/);
    expect(targetsPageSource).not.toMatch(/target\.SampleIntervalMs/);
  });

  it("adds appearance and window behavior controls to Settings", () => {
    expect(settingsPageSource).toContain("外观与窗口行为");
    expect(settingsPageSource).toContain("主题");
    expect(settingsPageSource).toContain("浅色");
    expect(settingsPageSource).toContain("深色");
    expect(settingsPageSource).toContain("跟随系统");
    expect(settingsPageSource).toContain("关闭窗口");
    expect(settingsPageSource).toContain("直接退出");
    expect(settingsPageSource).toContain("退出到托盘");
    expect(settingsPageSource).toContain("SegmentedControl");
  });

  it("keeps dark mode token-driven from documentElement data-theme", () => {
    expect(appSource).toContain("useFrameScopeTheme");
    expect(tokensCssSource).toContain(':root[data-theme="dark"]');
    expect(tokensCssSource).toContain("color-scheme: light");
    expect(tokensCssSource).toContain("color-scheme: dark");

    const componentThemeForks = [
      layoutCssSource,
      pagesCssSource,
      componentsCssSource,
    ].join("\n");
    expect(componentThemeForks).not.toMatch(/\[data-theme=["']dark["']\]/);
    expect(componentThemeForks).not.toMatch(/prefers-color-scheme/);
  });

  it("uses a dedicated path control for long settings paths without fake open or copy controls", () => {
    expect(settingsPageSource).toContain("PathControl");
    expect(settingsPageSource).toContain("path-control");
    expect(settingsPageSource).toContain("path-control__preview");
    expect(settingsPageSource).toContain("formatPathPreview");
    expect(settingsPageSource).not.toContain("copyDataRoot");
    expect(settingsPageSource).not.toContain("openDataRoot");

    expect(pagesCssSource).toMatch(/\.path-control\s*{[^}]*min-width:\s*0/s);
    expect(pagesCssSource).toMatch(/\.path-control__preview\s*{[^}]*grid-template-columns:\s*auto\s+minmax\(0,\s*1fr\)/s);
    expect(pagesCssSource).toMatch(/\.path-control input:focus\s*{[^}]*overflow-x:\s*auto/s);
  });

  it("places save and search failures next to the triggering controls with retry affordances", () => {
    expect(targetsPageSource).toContain("target-save-error");
    expect(targetsPageSource).toContain("process-search-error");
    expect(targetsPageSource).toContain("重试保存");
    expect(targetsPageSource).toContain("重试查找");
    expect(settingsPageSource).toContain("settings-save-error");
    expect(settingsPageSource).toContain("重试保存");
    expect(pagesCssSource).toMatch(/\.action-feedback\s*{[^}]*grid-column:\s*1\s*\/\s*-1/s);
    expect(pagesCssSource).toMatch(/\.action-feedback--danger\s*{[^}]*background:\s*var\(--fs-tint-danger\)/s);
  });

  it("separates process lookup idle loading empty failure and result states", () => {
    expect(targetsPageSource).toContain("processLookupState");
    expect(targetsPageSource).toContain("process-result-empty");
    expect(targetsPageSource).toContain("尚未查找进程");
    expect(targetsPageSource).not.toContain("尚未刷新进程");
    expect(targetsPageSource).toContain("输入关键字后点击查找进程，不会自动读取系统进程。");
    expect(targetsPageSource).toContain("正在查找匹配进程");
    expect(targetsPageSource).toContain("没有找到匹配进程");
    expect(targetsPageSource).toContain("查找进程失败");
    expect(targetsPageSource).toContain("找到匹配进程");
    expect(pagesCssSource).toMatch(/\.process-result-empty\s*{[^}]*margin-top:\s*var\(--fs-space-3\)/s);
    expect(pagesCssSource).toMatch(/\.process-result-empty\s*{[^}]*min-height:\s*96px/s);
    expect(pagesCssSource).toMatch(/\.process-result-empty\s*{[^}]*grid-template-columns:\s*auto\s+minmax\(0,\s*1fr\)/s);
  });

  it("does not render disabled empty-state controls that look clickable", () => {
    const emptyStateSource = readFileSync(new URL("./components/EmptyState.tsx", import.meta.url), "utf8");

    expect(emptyStateSource).toContain("onAction");
    expect(emptyStateSource).toContain("empty-state__note");
    expect(emptyStateSource).not.toMatch(/<button[\s\S]*disabled/);
    expect(componentsCssSource).not.toMatch(/\.empty-state__action:disabled/);
  });

  it("exposes frontend-only visual fixture modes for recurring visual QA", () => {
    expect(visualFixtureModes).toEqual([
      "empty",
      "loading",
      "success",
      "failure",
      "dirty",
      "saving",
      "saved",
      "many-results",
      "long-strings",
    ]);
  });

  it("moves implementation terms out of the first-screen about page", () => {
    const primaryAbout = withoutImplementationIdentifiers(aboutPageSource.split("<details")[0] ?? aboutPageSource);
    expect(primaryAbout).toContain("关于与帮助");
    expect(primaryAbout).toContain("本地记录游戏帧表现和系统占用");
    expect(primaryAbout).not.toMatch(/WebView2|bridge|requestId|smoke|mock adapter|contract/i);
  });

  it("keeps advanced and shared visible labels product-facing", () => {
    const visibleSources = [
      withoutImplementationIdentifiers(aboutPageSource),
      withoutImplementationIdentifiers(sidebarNavSource),
      withoutImplementationIdentifiers(chartShellSource),
    ].join("\n");

    expect(visibleSources).toContain("运行环境");
    expect(visibleSources).toContain("趋势预览");
    expect(visibleSources).toContain("操作由本机程序执行");
    expect(visibleSources).not.toMatch(/WebView2|bridge|requestId|smoke|mock adapter|contract|SVG mock|宿主应用/i);
  });
});
