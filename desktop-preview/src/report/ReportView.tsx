import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  Activity,
  Cpu,
  Zap,
  Grid2X2,
  ChartNoAxesCombined,
  Gauge,
  ListTree,
  HardDrive,
  Thermometer,
  Search,
  X,
  Download,
  Image,
  FileCode,
  ChevronDown,
  SlidersHorizontal,
  Maximize,
  RotateCcw,
  Info,
  Sparkles,
} from "lucide-react";
import { mountReport } from "./engine.js";
import { ReportSelect } from "./ReportSelect";
import { useReportPresence } from "./report-motion";
import { flushSync } from "react-dom";
import "./report.css";
import { getMotionPreferences, playSurface, startContentTransition, startSurfaceMotion, surfaceFrames, useMotionPreferences } from '../lib/motion-preferences';
export type ReportData = Record<string, unknown>;
type Range = {
  start: number;
  end: number;
  span: number;
  full: { start: number; end: number; span: number };
};
type State = {
  view: string;
  metric: string;
  metrics: string[][];
  metricDisabled: boolean;
  readMode: string;
  widthScale: number;
  chartHeight: number;
  zoomActive: boolean;
  fpsFiltering: boolean;
  fpsFilterAvailable: boolean;
  filterInfo: string;
  range: Range;
  summary: Record<string, number>;
  sampled: boolean;
  sampledAverage: number;
};
const initial: State = {
  view: "fps",
  metric: "all",
  metrics: [],
  metricDisabled: false,
  readMode: "trend",
  widthScale: 1,
  chartHeight: 360,
  zoomActive: false,
  fpsFiltering: true,
  fpsFilterAvailable: false,
  filterInfo: "",
  range: { start: 0, end: 1, span: 1, full: { start: 0, end: 1, span: 1 } },
  summary: {},
  sampled: false,
  sampledAverage: null,
};
const navigation = [
  { id: "fps", name: "帧率", icon: Activity },
  { id: "cpuCore", name: "CPU 核心频率", icon: Cpu },
  { id: "cpuVoltage", name: "CPU 电压", icon: Cpu, badge: Zap },
  { id: "cpuVid", name: "分核心电压", icon: Cpu, badge: Grid2X2 },
  { id: "perf", name: "性能图表", icon: ChartNoAxesCombined },
  { id: "system", name: "系统占用", icon: Gauge },
  { id: "process", name: "后台进程", icon: ListTree },
  { id: "io", name: "IO / 温度", icon: HardDrive, badge: Thermometer },
];
const sectionKey = (view: string) => ['perf', 'io'].includes(view) ? 'system' : view;
const number = (v: unknown, digits = 0) =>
  v == null || !Number.isFinite(Number(v))
    ? "无数据"
    : Number(v).toFixed(digits);
const time = (s: number) =>
  [Math.floor(s / 60), Math.floor(s % 60)]
    .map((n) => String(n).padStart(2, "0"))
    .join(":");

function Popover({
  label,
  icon,
  children,
}: {
  label: string;
  icon: ReactNode;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(false),
    ref = useRef<HTMLDivElement>(null);
  const presence = useReportPresence<HTMLDivElement>(open);
  useEffect(() => {
    if (!open) return;
    const outside = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const escape = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
        ref.current?.querySelector("button")?.focus();
      }
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    ref.current
      ?.querySelector<HTMLElement>(".popover-panel button,.popover-panel input")
      ?.focus({ preventScroll: true });
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);
  return (
    <div
      className="report-popover"
      ref={ref}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false);
      }}
    >
      <button aria-expanded={open} onClick={() => setOpen(!open)}>
        {icon}
        {label}
        <ChevronDown size={14} />
      </button>
      {presence.present && (
        <div
          ref={presence.ref}
          inert={!open}
          className="popover-panel"
          role="group"
          aria-label={label}
          onClick={(e) => {
            if ((e.target as HTMLElement).closest("[data-dismiss]"))
              setOpen(false);
          }}
        >
          {children}
        </div>
      )}
    </div>
  );
}

export function ReportView({
  data,
  savePng,
  onExport,
  onReady,
  onError,
  onLoadError,
  material,
  onAppearanceChange,
  appearanceBusy,
  exportAllowed = true,
  loadSection,
}: {
  data: ReportData;
  savePng: (url: string, name: string) => Promise<void>;
  onExport?: () => Promise<void>;
  onReady?: () => void;
  onError?: (message: string) => void;
  onLoadError?: (message: string) => void;
  material?: { mode: string; reason: string; preference?: string };
  onAppearanceChange?: (preference: string) => void;
  appearanceBusy?: boolean;
  exportAllowed?: boolean;
  loadSection?: (section: string) => Promise<ReportData>;
}) {
  const root = useRef<HTMLDivElement>(null),
    engine = useRef<ReturnType<typeof mountReport>>(null);
  const [state, setState] = useState<State>(initial),
    [search, setSearch] = useState(""),
    [hardware, setHardware] = useState(false),
    [filterDetails, setFilterDetails] = useState(false),
    [exporting, setExporting] = useState(false);
  const drawer = useRef<HTMLDialogElement>(null),
    drawerOpener = useRef<HTMLElement>(null),
    animation = useRef<Animation>(null),
    viewTransition = useRef<ViewTransition | null>(null),
    switchTicket = useRef(0),
    pendingView = useRef<string>(null),
    navigationRef = useRef<HTMLElement>(null),
    indicator = useRef<HTMLSpanElement>(null),
    drag = useRef<{ x: number; width: number; range: Range }>(null);
  const [sectionLoading, setSectionLoading] = useState(false);
  const loadedSections = useRef(new Set<string>());
  const sectionRequests = useRef(new Map<string, Promise<ReportData>>());
  const callbacks = useRef({ onReady, onError, onLoadError });
  callbacks.current = { onReady, onError, onLoadError };
  useEffect(() => {
    try {
      engine.current = mountReport(root.current, data, savePng, setState);
      loadedSections.current.clear(); sectionRequests.current.clear();
      if (loadSection) {
        setSectionLoading(true);
        const mounted = engine.current;
        const request = loadSection("fps");
        sectionRequests.current.set("fps", request);
        void request.then(part => {
          if (engine.current !== mounted) return;
          mounted.updateData(part); loadedSections.current.add("fps");
          callbacks.current.onReady?.();
        }).catch(e => {if (engine.current === mounted) callbacks.current.onError?.(String(e));}).finally(() => {
          if (engine.current === mounted) {sectionRequests.current.delete("fps"); if (!pendingView.current) setSectionLoading(false);}
        });
      } else callbacks.current.onReady?.();
    } catch (e) {
      (callbacks.current.onLoadError || callbacks.current.onError)?.(String(e));
    }
    return () => {
      animation.current?.cancel();
      viewTransition.current?.skipTransition();
      switchTicket.current++;
      engine.current?.dispose();
      engine.current = null;
    };
  }, [data, savePng, loadSection]);
  const motionPreferences = useMotionPreferences();
  useEffect(() => {
    animation.current?.cancel();
    viewTransition.current?.skipTransition();
    switchTicket.current++;
    if (pendingView.current) engine.current?.setView(pendingView.current);
    pendingView.current = null;
  }, [motionPreferences]);
  useEffect(() => startSurfaceMotion(root.current || document.body), []);
  useLayoutEffect(() => {
    const element = drawer.current;
    if (!element) return;
    if (hardware && !element.open) {
      drawerOpener.current = document.activeElement as HTMLElement;
      element.showModal();
    }
    if (!element.open) return;
    let cancelled = false;
    const close = () => { if (!cancelled && !hardware) { element.close(); drawerOpener.current?.focus({preventScroll:true}); } };
    const motion = playSurface(element, hardware, 'drawer');
    const backdrop = !motionPreferences.reduced && motionPreferences.mode !== 'off' ? element.animate(
      [{opacity:hardware?0:1},{opacity:hardware?1:0}],
      {duration:180,easing:'ease-out',fill:'both',pseudoElement:'::backdrop'},
    ) : null;
    if (motion) void motion.finished.then(close, close); else close();
    return () => { cancelled=true;motion?.cancel();backdrop?.cancel(); };
  }, [hardware,motionPreferences]);
  useLayoutEffect(() => {
    const nav = navigationRef.current;
    const update = () => {
      const tab = nav?.querySelector<HTMLElement>(".tab.active");
      if (!tab || !indicator.current) return;
      indicator.current.style.left = tab.offsetLeft + 16 + "px";
      indicator.current.style.width = Math.max(0, tab.offsetWidth - 32) + "px";
    };
    update();
    const observer = new ResizeObserver(update);
    if (nav) observer.observe(nav);
    return () => observer.disconnect();
  }, [state.view]);
  const selectView = async (view: string) => {
    const key = sectionKey(view);
    if (
      (!pendingView.current && state.view === view && (!loadSection || loadedSections.current.has(key)))
    )
      return;
    pendingView.current = view;
    const ticket = ++switchTicket.current;
    setSectionLoading(!!loadSection && !loadedSections.current.has(key));
    if (loadSection && !loadedSections.current.has(key)) {
      setSectionLoading(true);
      try {
        let request = sectionRequests.current.get(key);
        if (!request) { request = loadSection(key); sectionRequests.current.set(key, request); }
        const part = await request;
        if (ticket !== switchTicket.current || !engine.current) return;
        engine.current.updateData(part); loadedSections.current.add(key);
      } catch (e) {
        if (ticket === switchTicket.current) { callbacks.current.onError?.(String(e)); pendingView.current = null; }
        return;
      } finally {
        sectionRequests.current.delete(key);
        if (ticket === switchTicket.current) setSectionLoading(false);
      }
    }
    animation.current?.cancel();
    viewTransition.current?.skipTransition();
    const panel = root.current?.querySelector<HTMLElement>(".analysis-content");
    const prefs = getMotionPreferences();
    if (!panel || prefs.reduced || prefs.mode === 'off') {
      engine.current?.setView(view);
      if (panel) playSurface(panel, true, 'control');
      pendingView.current = null;
      return;
    }
    try {
      if (prefs.mode === 'fade' && document.startViewTransition) {
        const transition = startContentTransition(() => {
          if (ticket === switchTicket.current) flushSync(() => engine.current?.setView(view));
        });
        viewTransition.current = transition;
        void transition.ready.catch(() => undefined);
        await transition.finished;
        if (ticket === switchTicket.current) playSurface(panel, true, 'control');
        return;
      }
      animation.current = panel.animate(surfaceFrames(prefs,false,'content',-1), {
        duration: 70,
        easing: "ease-out",
        fill: "both",
      });
      await animation.current.finished;
      if (ticket !== switchTicket.current) return;
      flushSync(() => engine.current?.setView(view));
      animation.current.cancel();
      animation.current = playSurface(panel,true,'content');
      await animation.current?.finished;
      if (ticket === switchTicket.current) animation.current?.cancel();
    } catch {
      /* A newer selection or unmount cancelled the transition. */
    } finally {
      if (ticket === switchTicket.current) pendingView.current = null;
    }
  };
  const exportPng = async () => {
    setExporting(true);
    try {
      await engine.current?.exportPng();
    } catch (e) {
      onError?.(String(e));
    } finally {
      setExporting(false);
    }
  };
  const exportHtml = async () => {
    setExporting(true);
    try {
      await onExport?.();
    } catch (e) {
      onError?.(String(e));
    } finally {
      setExporting(false);
    }
  };
  const range = state.range,
    full = range.full;
  const stats = [
    [
      state.sampled ? "FPS 采样均值" : "平均帧率",
      number(state.sampled ? state.sampledAverage : state.summary.average),
      "FPS",
    ],
    ["1% Low", number(state.summary.low1), "FPS"],
    ["0.1% Low", number(state.summary.low01), "FPS"],
    [
      "最高 / 最低",
      number(state.summary.maxInstant) +
        " / " +
        number(state.summary.minInstant),
      "FPS",
    ],
  ];
  return (
    <div ref={root} className="shell">
      <header className="report-session">
        <div className="game">
          <div className="game-icon" id="gameIcon">
            FS
          </div>
          <div className="session-text">
            <h1 id="gameName">性能报告</h1>
            <div className="meta">
              <span id="runStart" />
              <span id="runEnd" />
              <span id="runDuration" />
            </div>
          </div>
        </div>
        <div className="session-actions">
          {onAppearanceChange && (
            <div
              className="read-modes appearance-modes"
              role="group"
              aria-label="报告外观"
              aria-busy={appearanceBusy}
            >
              {[
                ["glass", "柔和玻璃"],
                ["normal", "普通"],
              ].map(([value, label]) => (
                <button
                  key={value}
                  aria-pressed={material?.preference === value}
                  onClick={() => onAppearanceChange(value)}
                >
                  {label}
                </button>
              ))}
            </div>
          )}
          <button onClick={() => setHardware(true)}>
            <Cpu size={16} />
            硬件信息
          </button>
          {exportAllowed && <Popover
            label={exporting ? "正在导出" : "导出报告"}
            icon={<Download size={16} />}
          >
            <button
              data-dismiss
              id="exportPng"
              disabled={exporting}
              onClick={() => void exportPng()}
            >
              <Image size={16} />
              导出 PNG <small>3200 × 2000</small>
            </button>
            {onExport && (
              <button
                data-dismiss
                disabled={exporting}
                onClick={() => void exportHtml()}
              >
                <FileCode size={16} />
                导出离线网页
              </button>
            )}
            {!onExport && (
              <span className="popover-note">
                此网页可直接复制分享，全部资源已内置。
              </span>
            )}
          </Popover>}
        </div>
      </header>
      <nav
        ref={navigationRef}
        className="tabs"
        role="tablist"
        aria-label="报告分析视图"
        onKeyDown={(e) => {
          const tabs = Array.from(
              e.currentTarget.querySelectorAll<HTMLButtonElement>("[role=tab]"),
            ),
            i = tabs.indexOf(document.activeElement as HTMLButtonElement);
          if (!["ArrowRight", "ArrowLeft", "Home", "End"].includes(e.key))
            return;
          e.preventDefault();
          const next =
            e.key === "Home"
              ? 0
              : e.key === "End"
                ? tabs.length - 1
                : (i + (e.key === "ArrowRight" ? 1 : -1) + tabs.length) %
                  tabs.length;
          tabs[next]?.focus();
          selectView(navigation[next].id);
        }}
      >
        {navigation.map(({ id, name, icon: Icon, badge: Badge }) => (
          <button
            key={id}
            id={"tab-" + id}
            role="tab"
            className={"tab " + (state.view === id ? "active" : "")}
            data-view={id}
            aria-selected={state.view === id}
            aria-controls="analysis-panel"
            tabIndex={state.view === id ? 0 : -1}
            onFocus={(e) =>
              e.currentTarget.scrollIntoView({
                block: "nearest",
                inline: "nearest",
              })
            }
            onClick={() => selectView(id)}
          >
            <span className="nav-icon">
              <Icon size={20} />
              {Badge && <Badge className="nav-icon-badge" size={12} />}
            </span>
            {name}
          </button>
        ))}
        <span ref={indicator} className="tab-indicator" aria-hidden="true" />
      </nav>
      <main className="report-scroll" id="report-scroll">
        <div className="alert" id="captureAlert" role="status" />
        <section className="primary-stats" aria-label="帧率摘要">
          {stats.map(([label, value, unit]) => (
            <div className="primary-stat" key={label}>
              <span>{label}</span>
              <div>
                <strong>{value}</strong>
                <small>{unit}</small>
              </div>
            </div>
          ))}
        </section>
        <section
          id="analysis-panel"
          role="tabpanel"
          aria-labelledby={"tab-" + state.view}
          className="analysis-content" aria-busy={sectionLoading}
        >
            {sectionLoading && <div role="status" className="report-section-loading">正在读取图表数据…</div>}
          <div className="chart-heading">
            <div>
              <h2 id="viewTitle">帧率与流畅度</h2>
              <p id="viewNote" />
            </div>
            <div className="chart-selection">
              <ReportSelect
                label="图表指标"
                choices={state.metrics}
                value={state.metric}
                disabled={state.metricDisabled}
                onChange={(value) => engine.current?.setMetric(value)}
              />
              <div className="read-modes" role="group" aria-label="阅读模式">
                {[
                  ["spike", "保留尖峰"],
                  ["trend", "趋势易读"],
                ].map(([value, label]) => (
                  <button
                    key={value}
                    aria-pressed={state.readMode === value}
                    onClick={() => engine.current?.setReadMode(value)}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </div>
          <div className="toolbar">
            <div className="left-tools">
              {state.fpsFilterAvailable && (
                <div className="filter-control">
                  <label>
                    <input
                      id="fpsFilter"
                      type="checkbox"
                      checked={state.fpsFiltering}
                      onChange={(e) =>
                        engine.current?.setFiltering(e.target.checked)
                      }
                    />
                    <span>排除过高 / 过低 FPS</span>
                  </label>
                  <button
                    className="quiet-button"
                    aria-expanded={filterDetails}
                    onClick={() => setFilterDetails(!filterDetails)}
                  >
                    <Info size={15} />
                    筛选说明
                  </button>
                </div>
              )}
              {state.view === "process" && (
                <div className="search-wrap">
                  <Search size={16} />
                  <input
                    id="processSearch"
                    aria-label="搜索后台进程"
                    placeholder="搜索后台进程…"
                    value={search}
                    onChange={(e) => {
                      setSearch(e.target.value);
                      engine.current?.search(e.target.value);
                    }}
                  />
                  {search && (
                    <button
                      aria-label="清除搜索"
                      onClick={() => {
                        setSearch("");
                        engine.current?.search("");
                        root.current
                          ?.querySelector<HTMLInputElement>("#processSearch")
                          ?.focus();
                      }}
                    >
                      <X size={15} />
                    </button>
                  )}
                </div>
              )}
              {state.view !== "fps" && state.view !== "process" && (
                <span className="toolbar-hint">
                  悬停查看数值 · 点击图例控制曲线
                </span>
              )}
            </div>
            <div className="right-tools">
              <button
                id="fitWidth"
                title="恢复自动尺寸和完整时间范围"
                onClick={() => engine.current?.fit()}
              >
                <Maximize size={15} />
                适合窗口
              </button>
              <button
                id="resetView"
                title="仅恢复完整时间范围，保留图表尺寸"
                onClick={() => engine.current?.reset()}
              >
                <RotateCcw size={15} />
                重置
              </button>
              <Popover label="视图" icon={<SlidersHorizontal size={15} />}>
                <label className="view-setting">
                  图表宽度 <b id="widthText">{state.widthScale.toFixed(2)}×</b>
                  <input
                    id="widthScale"
                    aria-label="图表宽度"
                    type="range"
                    min="1"
                    max="8"
                    step=".25"
                    value={state.widthScale}
                    onChange={(e) =>
                      engine.current?.setWidth(Number(e.target.value))
                    }
                  />
                </label>
                <label className="view-setting">
                  图表高度 <b>{state.chartHeight}px</b>
                  <input
                    aria-label="图表高度"
                    type="range"
                    min="240"
                    max="1000"
                    step="1"
                    value={state.chartHeight}
                    onChange={(e) => {
                      engine.current?.setHeight(Number(e.target.value));
                    }}
                  />
                </label>
                <p className="popover-note">
                  {material?.reason || "离线报告 · 深色分析材质"}
                </p>
              </Popover>
            </div>
          </div>
          {state.fpsFilterAvailable && (
            <div
              className={"filter-summary " + (filterDetails ? "expanded" : "")}
            >
              <span id="fpsFilterInfo">{state.filterInfo}</span>
              <div
                className="filter-copy"
                inert={!filterDetails}
                aria-hidden={!filterDetails}
              >
                <div>
                  <p>
                    筛选偏好按这份报告保存。统计沿用记录的数据与既有筛选口径，原始
                    CSV 不变；图表时间缩放不重新定义整份报告的摘要。
                  </p>
                </div>
              </div>
            </div>
          )}
          <div className="chart-scroll" id="chartScroll">
            <div
              className={"chartbox" + (state.zoomActive ? " zoom-active" : "")}
              id="chartBox"
              tabIndex={0}
              role="region"
              aria-label="交互图表"
              aria-describedby="chartInteractionHint"
            >
              <canvas id="chart" />
              <canvas id="overlay" />
              <div className="tooltip" id="tooltip" />
            </div>
          </div>
          <div className="time-toolbar">
            <span className="time-label">{time(range.start)}</span>
            <div className="time-range" aria-label="时间范围">
              <div
                className="time-selection"
                style={{
                  left: (range.start / full.span) * 100 + "%",
                  width: (range.span / full.span) * 100 + "%",
                }}
                onPointerDown={(e) => {
                  if (e.button !== 0) return;
                  drag.current = {
                    x: e.clientX,
                    width: e.currentTarget.parentElement.clientWidth,
                    range,
                  };
                  e.currentTarget.setPointerCapture(e.pointerId);
                }}
                onPointerMove={(e) => {
                  if (!drag.current) return;
                  const delta =
                    ((e.clientX - drag.current.x) / drag.current.width) *
                    drag.current.range.full.span;
                  engine.current?.setTimeRange(
                    drag.current.range.start + delta,
                    drag.current.range.end + delta,
                  );
                }}
                onPointerUp={() => {
                  drag.current = null;
                }}
                onPointerCancel={() => {
                  drag.current = null;
                }}
              />
              <input
                type="range"
                aria-label="时间范围起点"
                min="0"
                max={full.end}
                step={Math.max(0.001, full.span / 2000)}
                value={range.start}
                onChange={(e) =>
                  engine.current?.setTimeRange(
                    Math.min(
                      Number(e.target.value),
                      range.end - Math.min(0.5, full.span),
                    ),
                    range.end,
                  )
                }
              />
              <input
                type="range"
                aria-label="时间范围终点"
                min="0"
                max={full.end}
                step={Math.max(0.001, full.span / 2000)}
                value={range.end}
                onChange={(e) =>
                  engine.current?.setTimeRange(
                    range.start,
                    Math.max(
                      Number(e.target.value),
                      range.start + Math.min(0.5, full.span),
                    ),
                  )
                }
              />
            </div>
            <span className="time-label">{time(range.end)}</span>
          </div>
          <footer className="chart-footer">
            <div className="legend" id="legend" aria-label="图例" />
            <div className="chart-notes">
              <span id="modeStats" />
              <span id="chartInteractionHint">
                {state.zoomActive
                  ? "滚轮缩放 · 移出退出"
                  : "点击图表后滚轮缩放"}{" "}
                · 拖动平移 · 聚焦后 + / − 缩放
              </span>
            </div>
          </footer>
        </section>
        <section className="report-details">
          <div className="section-heading">
            <h2>分析详情</h2>
            <span>本次采集 · 原始记录保留</span>
          </div>
          <div className="gauges" id="gauges" />
          <div className="panelgrid">
            <section className="detail-card">
              <h3>
                <ListTree size={17} />
                后台进程峰值 <small>CPU 占用</small>
              </h3>
              <div className="rows" id="processRows" />
            </section>
            <section className="detail-card">
              <h3>
                <Activity size={17} />
                本次帧率摘要 <small>采集区间统计</small>
              </h3>
              <div className="rows" id="summaryRows" />
            </section>
          </div>
        </section>
      </main>
      <dialog
        ref={drawer}
        className="hardware-drawer"
        aria-labelledby="hardware-title"
        onCancel={(e) => {
          e.preventDefault();
          setHardware(false);
        }}
        onClose={() => setHardware(false)}
        onClick={(e) => {
          if (e.target === drawer.current) {
            const r = drawer.current.getBoundingClientRect();
            if (e.clientX < r.left) setHardware(false);
          }
        }}
      >
        <header>
          <div>
            <Cpu size={20} />
            <h2 id="hardware-title">本次记录的硬件</h2>
          </div>
          <button aria-label="关闭硬件信息" onClick={() => setHardware(false)}>
            <X size={19} />
          </button>
        </header>
        <p className="drawer-note">
          来自本次采集，历史报告不会使用当前电脑配置替换。
        </p>
        <section className="hardware-card">
          <h3>
            <Cpu size={16} />
            处理器
          </h3>
          <b id="hwCpu" />
          <dl>
            <dt>核心 / 线程</dt>
            <dd id="hwCore" />
            <dt>标称最大频率</dt>
            <dd id="hwCpuClock" />
          </dl>
        </section>
        <section className="hardware-card">
          <h3>
            <Gauge size={16} />
            显卡
          </h3>
          <b id="hwGpu" />
          <dl>
            <dt>驱动</dt>
            <dd id="hwDriver" />
            <dt>记录显存</dt>
            <dd id="hwVram" />
          </dl>
        </section>
        <section className="hardware-card">
          <h3>
            <HardDrive size={16} />
            系统与内存
          </h3>
          <b id="hwOs" />
          <dl>
            <dt>内存</dt>
            <dd id="hwMem" />
            <dt>采样</dt>
            <dd id="hwSamples" />
          </dl>
        </section>
        <section className="hardware-card">
          <h3>
            <Info size={16} />
            报告外观
          </h3>
          <p>
            {material?.reason || "离线网页使用深色底，与软件报告共用分析组件。"}
          </p>
        </section>
      </dialog>
    </div>
  );
}
