import { useCallback, useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import { reducedMotion } from "./report-motion";
import { createRoot } from "react-dom/client";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { listen } from "@tauri-apps/api/event";
import { invoke } from "@preview/native-core";
import { Minus, Square, X, RefreshCw } from "lucide-react";
import { ReportView, type ReportData } from "./ReportView";
import "./report.css";
import { startMotionStore } from '../lib/motion-store';
const call = <T,>(command: string, args: Record<string, unknown> = {}) =>
  invoke<T>("preview_native", { command, args });
function ReportWindow() {
  useEffect(startMotionStore, []);
  const id = new URLSearchParams(location.search).get("id") || "sample";
  const [data, setData] = useState<ReportData>(null),
    [title, setTitle] = useState("FrameScope 报告"),
    [error, setError] = useState(""),
    [deleted, setDeleted] = useState(false),
    [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  useEffect(() => {
    setProgress(null);
    if (!busy || id === "sample") return;
    let active = true, pending = false;
    const poll = async () => {
      if (pending) return;
      pending = true;
      try {
        const value = await call<{ active: boolean; percent?: number }>("report_progress", { id });
        if (active) setProgress(value.active && Number.isFinite(value.percent) ? value.percent : null);
      } catch { /* The report request owns the terminal error. */ }
      finally { pending = false; }
    };
    void poll();
    const timer = setInterval(() => void poll(), 500);
    return () => { active = false; clearInterval(timer); };
  }, [busy, id]);
  const generation = useRef(0),
    alive = useRef(true);
  const [material, setMaterial] = useState({
    preference: "glass",
    mode: "fallback",
    reason: "正在检查窗口材质",
  });
  const appearanceQueue = useRef(Promise.resolve()),
    materialQueue = useRef(Promise.resolve()),
    materialRef = useRef(material),
    veil = useRef<HTMLDivElement>(null),
    veilMotion = useRef<Animation>(null);
  const [appearanceBusy, setAppearanceBusy] = useState(false);
  const appearancePending = useRef(0);
  const refreshMaterial = useCallback(() => {
    if (document.hidden || appearancePending.current) return Promise.resolve();
    materialQueue.current = materialQueue.current.then(async () => {
      if (!alive.current) return;
      let covered = false;
      try {
        const next = await call<{ preference: string }>("report_material", {
          id,
          inspect: true,
        });
        if (!alive.current) return;
        covered =
          next.preference !== materialRef.current.preference &&
          !reducedMotion() &&
          !!veil.current;
        if (covered) {
          veilMotion.current = veil.current.animate(
            [{ opacity: 0 }, { opacity: 1 }],
            { duration: 120, easing: "ease-out", fill: "forwards" },
          );
          await veilMotion.current.finished;
        }
        const state = await call<{
          mode: string;
          preference: string;
          reason: string;
        }>("report_material", { id });
        if (!alive.current) return;
        materialRef.current = state;
        flushSync(() => setMaterial(state));
        // Commit the surface state while the native backdrop is covered.
        document.documentElement.dataset.reportMaterial = state.mode;
        if (covered && veil.current) {
          veilMotion.current?.cancel();
          veilMotion.current = veil.current.animate(
            [{ opacity: 1 }, { opacity: 0 }],
            { duration: 120, easing: "ease-out", fill: "forwards" },
          );
          await veilMotion.current.finished;
        }
      } catch (error) {
        if (alive.current) setError("报告外观应用失败：" + String(error));
      } finally {
        veilMotion.current?.cancel();
      }
    });
    return materialQueue.current;
  }, [id]);
  const changeAppearance = useCallback(
    (preference: string) => {
      appearancePending.current++;
      setAppearanceBusy(true);
      appearanceQueue.current = appearanceQueue.current.then(async () => {
        try {
          await call("report_appearance_set", { id, preference });
        } catch (e) {
          if (alive.current) setError("报告外观保存失败：" + String(e));
        } finally {
          appearancePending.current--;
          if (!appearancePending.current && alive.current) {
            setAppearanceBusy(false);
            await refreshMaterial();
          }
        }
      });
    },
    [id, refreshMaterial],
  );
  useEffect(() => {
    void refreshMaterial();
    let stop: (() => void) | undefined,
      active = true;
    void listen("report-appearance-changed", () => void refreshMaterial()).then(
      (fn) => {
        if (active) {
          stop = fn;
          void refreshMaterial();
        } else fn();
      },
    );
    const timer = window.setInterval(() => void refreshMaterial(), 3000);
    document.addEventListener("visibilitychange", refreshMaterial);
    return () => {
      active = false;
      stop?.();
      clearInterval(timer);
      document.removeEventListener("visibilitychange", refreshMaterial);
    };
  }, [refreshMaterial]);
  useEffect(() => {
    document.documentElement.dataset.reportMaterial = material.mode;
    return () => {
      delete document.documentElement.dataset.reportMaterial;
    };
  }, [material.mode]);
  const load = useCallback(async () => {
    const ticket = ++generation.current;
    setError("");
    setBusy(true);
    try {
      const result = await call<{ data: ReportData; title: string; upgradeWarning?: string }>(
        "report_overview",
        { id },
      );
      if (alive.current && generation.current === ticket) {
        setData(result.data);
        setTitle(result.title);
        setError(result.upgradeWarning || "");
      }
    } catch (e) {
      if (alive.current && generation.current === ticket) {
        setData(null);
        setError(String(e));
      }
    } finally {
      if (alive.current && generation.current === ticket) setBusy(false);
    }
  }, [id]);
  useEffect(() => {
    alive.current = true;
    void load();
    let stop: (() => void) | undefined;
    void listen<{ id: string; kind: string }>("report-changed", (e) => {
      if (e.payload.id !== id) return;
      if (e.payload.kind === "deleted") {
        generation.current++;
        setData(null);
        setDeleted(true);
        setBusy(false);
      } else void load();
    }).then((fn) => {
      if (alive.current) stop = fn;
      else fn();
    });
    return () => {
      alive.current = false;
      veilMotion.current?.cancel();
      stop?.();
    };
  }, [id, load]);
  const loadSection = useCallback((section: string) => call<ReportData>("report_section", {id, section}), [id]);
  const [inspectionNote, setInspectionNote] = useState("");
  useEffect(() => {
    if (!data || id === "sample") return;
    let active = true, timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const result = await call<{inspectionStatus: string; artifactStatus: string; artifactError?: string}>("report_inspection", {id});
        if (!active) return;
        setInspectionNote(result.artifactStatus === "inputs-changed" ? "原始输入已变化：当前显示已生成的报告。" :
          result.inspectionStatus === "unavailable" ? "完整校验失败：" + (result.artifactError || "请在报告列表刷新重试") :
          result.artifactStatus === "incomplete" ? "报告产物不完整：" + (result.artifactError || "请重新生成") : "");
        if (["pending", "checking"].includes(result.inspectionStatus)) timer = setTimeout(() => void poll(), 2000);
      } catch (e) { if (active) setInspectionNote(String(e)); }
    };
    void poll();
    return () => {active = false; clearTimeout(timer);};
  }, [data, id]);
  const savePng = useCallback(
    async (url: string, name: string) => {
      await call("report_save_png", { id, data: url, name });
    },
    [id],
  );
  const exportHtml = useCallback(async () => {
    try {
      setError("");
      await call("report_export_html", { id });
    } catch (e) {
      setError(String(e));
    }
  }, [id]);
  const ready = useCallback(() => {
    void call("report_ready", { id }).catch((e) => setError(String(e)));
  }, [id]);
  const regenerate = async () => {
    setBusy(true);
    setError("");
    try {
      await call("report_regenerate", { id });
      await load();
    } catch (e) {
      setError(String(e));
      setBusy(false);
    }
  };
  return (
    <div className="report-root desktop-report" data-material={material.mode}>
      <div className="material-background" aria-hidden="true">
        <div ref={veil} className="material-veil" />
      </div>
      <header className="report-titlebar" data-tauri-drag-region>
        <span className="caption" data-tauri-drag-region>
          {title}
        </span>
        <button
          aria-label="最小化"
          onClick={() => void getCurrentWindow().minimize()}
        >
          <Minus size={16} />
        </button>
        <button
          aria-label="最大化或还原"
          onClick={() => void getCurrentWindow().toggleMaximize()}
        >
          <Square size={14} />
        </button>
        <button
          aria-label="关闭报告"
          onClick={() => void getCurrentWindow().close()}
        >
          <X size={17} />
        </button>
      </header>
      {error && (
        <div role="alert" className="report-error">
          {error}
          {data && id !== "sample" && <button disabled={busy} onClick={() => void regenerate()}>重新生成</button>}
        </div>
      )}
      {inspectionNote && <div role="status" className="report-error">{inspectionNote}</div>}
      {busy && progress !== null && (
        <div role="status" className="report-error">正在更新报告统计：{progress}%</div>
      )}
      {deleted ? (
        <section className="report-status">
          <h1>报告已删除</h1>
          <p>本窗口不再展示已删除记录。需要恢复时，请从 Windows 回收站还原。</p>
        </section>
      ) : data ? (
        <ReportView
          material={material}
          onAppearanceChange={changeAppearance}
          appearanceBusy={appearanceBusy}
          data={data}
          loadSection={loadSection}
          savePng={savePng}
          onExport={exportHtml}
          onReady={ready}
          onError={setError}
          onLoadError={(message) => {
            setData(null);
            setError(message);
          }}
        />
      ) : (
        <section className="report-status">
          <h1>{busy ? "正在读取报告概览…" : "无法打开报告"}</h1>
          <p>先显示已生成的统计和图表，原始数据完整校验在后台进行。</p>
          {!busy && (
            <div className="report-actions">
              <button onClick={() => void load()}>重试</button>
              {id !== "sample" && (
                <button onClick={() => void regenerate()}>
                  <RefreshCw size={14} /> 重新生成
                </button>
              )}
            </div>
          )}
        </section>
      )}
    </div>
  );
}
export function bootReport() {
  document.documentElement.dataset.reportWindow = "true";
  createRoot(document.getElementById("root")!).render(<ReportWindow />);
}
