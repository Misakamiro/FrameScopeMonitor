import { useCallback, useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { open } from "@tauri-apps/plugin-dialog";

// === Design language shared with the main app ===
const PRIMARY = "#98DDD0";
const GLASS_BG = "rgba(8, 12, 18, 0.58)";
const GLASS_BORDER = "rgba(255, 255, 255, 0.14)";
const TEXT_MAIN = "#eef2f5";
const TEXT_DIM = "rgba(238, 242, 245, 0.55)";
const DANGER = "#ff7b72";
const WALLPAPERS = ["/backgrounds/preset1.jpg", "/backgrounds/preset2.jpg"];

type LaunchMode = "install" | "uninstall";
type InstallState = { installed: boolean; version: string; install_path: string; legacy_nsis: boolean };
type Phase = "idle" | "working" | "done" | "error";

async function cmd<T>(name: string, args: Record<string, unknown> = {}): Promise<T> {
  return invoke<T>(name, args);
}

export default function App() {
  const [mode, setMode] = useState<LaunchMode>("install");
  const [phase, setPhase] = useState<Phase>("idle");
  const [error, setError] = useState("");
  const [version, setVersion] = useState("");
  const [existing, setExisting] = useState<InstallState | null>(null);
  const [webview2Ok, setWebview2Ok] = useState(true);
  const [targetDir, setTargetDir] = useState("");
  const [desktopShortcut, setDesktopShortcut] = useState(true);
  const [autostart, setAutostart] = useState(false);
  const [deleteData, setDeleteData] = useState(false);
  const [progress, setProgress] = useState({ index: 0, total: 0, name: "" });
  const [wallpaper, setWallpaper] = useState(0);
  const bootedRef = useRef(false);

  // Boot: launch mode drives the skin; the rest is shared.
  useEffect(() => {
    if (bootedRef.current) return;
    bootedRef.current = true;
    (async () => {
      try {
        const m = await cmd<string>("get_launch_mode");
        setMode(m === "uninstall" ? "uninstall" : "install");
        const v = await cmd<string>("get_app_version");
        setVersion(v);
        const st = await cmd<InstallState>("get_state");
        setExisting(st.installed ? st : null);
        if (m !== "uninstall") {
          setTargetDir(await cmd<string>("get_default_install_path"));
          setWebview2Ok(await cmd<boolean>("check_webview2"));
        }
      } catch (e) {
        setError(String(e));
      }
    })();
  }, []);

  // Wallpaper crossfade carousel, same 10s cadence as the main app.
  useEffect(() => {
    const t = setInterval(() => setWallpaper((w) => (w + 1) % WALLPAPERS.length), 10_000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    if (mode !== "install") return;
    let unlisten: (() => void) | undefined;
    (async () => {
      unlisten = await listen<{ index: number; total: number; name: string }>("install-progress", (ev) => {
        setProgress(ev.payload);
      });
    })();
    return () => { unlisten?.(); };
  }, [mode]);

  const browse = useCallback(async () => {
    const picked = await open({ directory: true, multiple: false, title: "选择安装目录" });
    if (typeof picked === "string") setTargetDir(picked);
  }, []);

  const runInstall = useCallback(async () => {
    setPhase("working");
    setError("");
    try {
      await cmd("install", {
        targetDir,
        createDesktopShortcut: desktopShortcut,
        autostart,
      });
      setPhase("done");
    } catch (e) {
      setError(String(e));
      setPhase("error");
    }
  }, [targetDir, desktopShortcut, autostart]);

  const runUninstall = useCallback(async () => {
    setPhase("working");
    setError("");
    try {
      await cmd("uninstall", { deleteData });
      setPhase("done");
    } catch (e) {
      setError(String(e));
      setPhase("error");
    }
  }, [deleteData]);

  const closeApp = () => { void getCurrentWindow().close(); };
  const launchApp = async () => {
    try {
      const dir = existing?.install_path || targetDir;
      await cmd("launch_app", { installDir: dir });
    } catch (e) {
      console.error(e);
    }
    closeApp();
  };

  const busy = phase === "working";
  void busy;
  const pct = progress.total > 0 ? Math.round((progress.index / progress.total) * 100) : 0;

  // === Uninstall skin ===
  if (mode === "uninstall") {
    return (
      <Shell>
        <Card width={480}>
          <Logo size={56} />
          <Title>卸载 FrameScope</Title>
          <Sub>
            将从电脑中移除 FrameScope 及其监控组件
            {existing ? `（当前 v${existing.version}）` : ""}。
          </Sub>
          {phase === "done" ? (
            <DoneBlock text="卸载完成" />
          ) : phase === "error" ? (
            <ErrorBlock error={error} onBack={() => setPhase("idle")} />
          ) : phase === "working" ? (
            <ProgressArea pct={pct} name={progress.name} />
          ) : (
            <>
              <OptionArea>
                <Check checked={deleteData} onChange={setDeleteData}>
                  同时删除配置与历史报告数据
                </Check>
              </OptionArea>
              <ButtonRow style={{ marginTop: 22 }}>
                <PrimaryButton onClick={runUninstall} danger>
                  卸载
                </PrimaryButton>
                <GhostButton onClick={closeApp}>取消</GhostButton>
              </ButtonRow>
            </>
          )}
        </Card>
      </Shell>
    );
  }

  // === Install skin ===
  return (
    <Shell footer={<FooterNote>安装即代表同意 GPL-3.0 开源许可</FooterNote>}>
      <Card>
        <Logo />
        <Title>
          FrameScope <span style={{ color: TEXT_DIM, fontWeight: 400 }}>v{version}</span>
        </Title>
        <Sub>本地游戏性能监控 · 液态玻璃 OSD · 逐帧分析</Sub>

        {phase === "done" ? (
          <>
            <DoneBlock text={existing ? "升级完成" : "安装完成"} />
            <ButtonRow>
              <PrimaryButton onClick={launchApp}>立即体验</PrimaryButton>
              <GhostButton onClick={closeApp}>完成</GhostButton>
            </ButtonRow>
          </>
        ) : phase === "error" ? (
          <ErrorBlock error={error} onBack={() => setPhase("idle")} />
        ) : phase === "working" ? (
          <ProgressArea pct={pct} name={progress.name} />
        ) : (
          <>
            {existing && (
              <InfoLine>
                已安装 v{existing.version}
                {existing.legacy_nsis ? "（旧版安装器）" : ""}，将继续安装将升级覆盖
              </InfoLine>
            )}
            {!webview2Ok && (
              <InfoLine warn>
                未检测到 WebView2 运行时，安装完成后首次启动需要联网安装
              </InfoLine>
            )}
            <FieldLabel>安装位置</FieldLabel>
            <PathRow>
              <PathInput value={targetDir} onChange={(e) => setTargetDir(e.target.value)} spellCheck={false} />
              <BrowseButton onClick={browse}>浏览</BrowseButton>
            </PathRow>
            <OptionArea>
              <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                <Check checked={desktopShortcut} onChange={setDesktopShortcut}>
                  创建桌面快捷方式
                </Check>
                <Check checked={autostart} onChange={setAutostart}>
                  开机自动启动
                </Check>
              </div>
            </OptionArea>
            <ButtonRow style={{ marginTop: 18 }}>
              <PrimaryButton onClick={runInstall}>
                {existing ? "升级覆盖" : "立即安装"}
              </PrimaryButton>
              {existing && (
                <GhostButton
                  onClick={() => {
                    setMode("uninstall");
                    setPhase("idle");
                  }}
                >
                  卸载
                </GhostButton>
              )}
            </ButtonRow>
          </>
        )}
      </Card>
    </Shell>
  );

  // === Layout helpers ===

  function Shell({ children, footer }: { children: React.ReactNode; footer?: React.ReactNode }) {
    return (
      <div style={{ width: "100%", height: "100%", position: "relative", display: "flex", flexDirection: "column" }}>
        <WallpaperLayer index={wallpaper} />
        <TitleBar />
        <div
          style={{
            flex: 1,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            position: "relative",
            zIndex: 2,
            padding: "0 32px",
          }}
        >
          {children}
        </div>
        {footer}
      </div>
    );
  }

  function WallpaperLayer({ index }: { index: number }) {
    return (
      <>
        {WALLPAPERS.map((src, i) => (
          <img
            key={src}
            src={src}
            alt=""
            style={{
              position: "absolute",
              inset: 0,
              width: "100%",
              height: "100%",
              objectFit: "cover",
              opacity: i === index ? 1 : 0,
              transition: "opacity 0.5s ease",
            }}
          />
        ))}
        <div
          style={{
            position: "absolute",
            inset: 0,
            background:
              "linear-gradient(180deg, rgba(4,7,12,0.35) 0%, rgba(4,7,12,0.62) 55%, rgba(4,7,12,0.8) 100%)",
          }}
        />
      </>
    );
  }

  function TitleBar() {
    return (
      <div
        data-tauri-drag-region
        style={{
          height: 44,
          flexShrink: 0,
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "0 8px 0 16px",
          position: "relative",
          zIndex: 3,
        }}
      >
        <img src="/icon.png" alt="" style={{ width: 20, height: 20, borderRadius: 5 }} />
        <span style={{ fontSize: 13, color: TEXT_DIM }}>FrameScope 安装程序</span>
        <div style={{ flex: 1 }} data-tauri-drag-region />
        <CloseButton onClick={closeApp}>✕</CloseButton>
      </div>
    );
  }

  function CloseButton({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
    return (
      <button
        onClick={onClick}
        style={{
          width: 32,
          height: 32,
          borderRadius: 8,
          border: "none",
          background: "transparent",
          color: TEXT_DIM,
          fontSize: 14,
          cursor: "pointer",
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.background = "rgba(255,255,255,0.12)";
          e.currentTarget.style.color = TEXT_MAIN;
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.background = "transparent";
          e.currentTarget.style.color = TEXT_DIM;
        }}
      >
        {children}
      </button>
    );
  }

  function Card({ children, width }: { children: React.ReactNode; width?: number }) {
    return (
      <div
        style={{
          width: width ?? 520,
          maxWidth: "100%",
          borderRadius: 18,
          background: GLASS_BG,
          border: `1px solid ${GLASS_BORDER}`,
          backdropFilter: "blur(28px) saturate(1.35)",
          WebkitBackdropFilter: "blur(28px) saturate(1.35)",
          boxShadow:
            "0 24px 60px rgba(0,0,0,0.45), inset 0 1px 0 rgba(255,255,255,0.12), inset 0 -1px 0 rgba(255,255,255,0.04)",
          padding: "34px 40px 30px",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          textAlign: "center",
        }}
      >
        {children}
      </div>
    );
  }

  function Logo({ size }: { size?: number }) {
    const s = size ?? 64;
    return (
      <img
        src="/icon.png"
        alt=""
        style={{ width: s, height: s, borderRadius: Math.round(s * 0.23), marginBottom: 14, boxShadow: "0 8px 24px rgba(0,0,0,0.4)" }}
      />
    );
  }

  function OptionArea({ children }: { children: React.ReactNode }) {
    return (
      <div
        style={{
          width: "100%",
          marginTop: 22,
          padding: "13px 16px",
          borderRadius: 11,
          background: "rgba(255,255,255,0.06)",
          border: "1px solid rgba(255,255,255,0.1)",
          textAlign: "left",
        }}
      >
        {children}
      </div>
    );
  }

  function Title({ children }: { children: React.ReactNode }) {
    return (
      <div style={{ fontSize: 24, fontWeight: 700, color: TEXT_MAIN, letterSpacing: "0.02em" }}>{children}</div>
    );
  }

  function Sub({ children }: { children: React.ReactNode }) {
    return <div style={{ fontSize: 13, color: TEXT_DIM, marginTop: 8 }}>{children}</div>;
  }

  function InfoLine({ children, warn }: { children: React.ReactNode; warn?: boolean }) {
    return (
      <div
        style={{
          width: "100%",
          marginTop: 16,
          padding: "9px 14px",
          borderRadius: 10,
          fontSize: 12.5,
          lineHeight: 1.5,
          background: warn ? "rgba(255,123,114,0.12)" : "rgba(152,221,208,0.1)",
          color: warn ? DANGER : "rgba(152,221,208,0.95)",
          border: `1px solid ${warn ? "rgba(255,123,114,0.25)" : "rgba(152,221,208,0.2)"}`,
        }}
      >
        {children}
      </div>
    );
  }

  function FieldLabel({ children }: { children: React.ReactNode }) {
    return (
      <div style={{ width: "100%", textAlign: "left", marginTop: 22, marginBottom: 8, fontSize: 12.5, color: TEXT_DIM }}>
        {children}
      </div>
    );
  }

  function PathRow({ children }: { children: React.ReactNode }) {
    return <div style={{ width: "100%", display: "flex", gap: 8 }}>{children}</div>;
  }

  function PathInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
    return (
      <input
        {...props}
        style={{
          flex: 1,
          height: 38,
          borderRadius: 10,
          border: "1px solid rgba(255,255,255,0.14)",
          background: "rgba(255,255,255,0.07)",
          color: TEXT_MAIN,
          fontSize: 13,
          padding: "0 12px",
          outline: "none",
          fontFamily: "inherit",
        }}
        onFocus={(e) => {
          e.currentTarget.style.borderColor = "rgba(152,221,208,0.6)";
        }}
        onBlur={(e) => {
          e.currentTarget.style.borderColor = "rgba(255,255,255,0.14)";
        }}
      />
    );
  }

  function BrowseButton({ onClick, children }: { onClick: () => void | Promise<void>; children: React.ReactNode }) {
    return (
      <button
        onClick={() => { void onClick(); }}
        style={{
          height: 38,
          padding: "0 18px",
          borderRadius: 10,
          border: "1px solid rgba(255,255,255,0.16)",
          background: "rgba(255,255,255,0.08)",
          color: TEXT_MAIN,
          fontSize: 13,
          cursor: "pointer",
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.background = "rgba(255,255,255,0.14)";
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.background = "rgba(255,255,255,0.08)";
        }}
      >
        {children}
      </button>
    );
  }

  function Check({
    checked,
    onChange,
    children,
  }: {
    checked: boolean;
    onChange: (v: boolean) => void;
    children: React.ReactNode;
  }) {
    return (
      <button
        onClick={() => onChange(!checked)}
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          background: "transparent",
          border: "none",
          color: TEXT_MAIN,
          fontSize: 13.5,
          cursor: "pointer",
          padding: 0,
          textAlign: "left",
        }}
      >
        <span
          style={{
            width: 18,
            height: 18,
            borderRadius: 6,
            flexShrink: 0,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            background: checked ? PRIMARY : "rgba(255,255,255,0.08)",
            border: checked ? "none" : "1px solid rgba(255,255,255,0.25)",
            color: "#0a0f14",
            fontSize: 12,
            fontWeight: 800,
            transition: "all 0.15s ease",
          }}
        >
          {checked ? "✓" : ""}
        </span>
        {children}
      </button>
    );
  }

  function ButtonRow({
    children,
    style,
  }: {
    children: React.ReactNode;
    style?: React.CSSProperties;
  }) {
    return (
      <div style={{ width: "100%", display: "flex", gap: 12, ...style }}>{children}</div>
    );
  }

  function PrimaryButton({
    onClick,
    children,
    danger,
  }: {
    onClick: () => void | Promise<void>;
    children: React.ReactNode;
    danger?: boolean;
  }) {
    const bg = danger ? DANGER : PRIMARY;
    return (
      <button
        onClick={() => { void onClick(); }}
        style={{
          flex: 1,
          height: 42,
          borderRadius: 11,
          border: "none",
          background: bg,
          color: danger ? "#fff" : "#0a0f14",
          fontSize: 14.5,
          fontWeight: 700,
          cursor: "pointer",
          boxShadow: `0 6px 20px ${danger ? "rgba(255,123,114,0.3)" : "rgba(152,221,208,0.28)"}`,
          transition: "transform 0.12s ease, filter 0.12s ease",
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.filter = "brightness(1.08)";
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.filter = "none";
        }}
        onMouseDown={(e) => {
          e.currentTarget.style.transform = "scale(0.98)";
        }}
        onMouseUp={(e) => {
          e.currentTarget.style.transform = "none";
        }}
      >
        {children}
      </button>
    );
  }

  function GhostButton({ onClick, children }: { onClick: () => void | Promise<void>; children: React.ReactNode }) {
    return (
      <button
        onClick={() => { void onClick(); }}
        style={{
          height: 42,
          padding: "0 24px",
          borderRadius: 11,
          border: "1px solid rgba(255,255,255,0.18)",
          background: "rgba(255,255,255,0.07)",
          color: TEXT_MAIN,
          fontSize: 14,
          cursor: "pointer",
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.background = "rgba(255,255,255,0.13)";
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.background = "rgba(255,255,255,0.07)";
        }}
      >
        {children}
      </button>
    );
  }

  function ProgressArea({ pct, name }: { pct: number; name: string }) {
    return (
      <div style={{ width: "100%", marginTop: 22 }}>
        <div style={{ fontSize: 13, color: TEXT_MAIN, marginBottom: 10 }}>
          {pct >= 100 ? "正在完成配置…" : `正在安装 ${pct}%`}
        </div>
        <div
          style={{
            height: 8,
            borderRadius: 5,
            background: "rgba(255,255,255,0.1)",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              height: "100%",
              width: `${Math.max(pct, 4)}%`,
              borderRadius: 5,
              background: `linear-gradient(90deg, ${PRIMARY}, #6ee7d8)`,
              transition: "width 0.2s ease",
              boxShadow: `0 0 12px ${PRIMARY}66`,
            }}
          />
        </div>
        <div
          style={{
            marginTop: 10,
            fontSize: 11.5,
            color: TEXT_DIM,
            whiteSpace: "nowrap",
            overflow: "hidden",
            textOverflow: "ellipsis",
            direction: "rtl",
            textAlign: "left",
          }}
        >
          {name}
        </div>
      </div>
    );
  }

  function DoneBlock({ text }: { text: string }) {
    return (
      <div style={{ marginTop: 10, marginBottom: 6, display: "flex", flexDirection: "column", alignItems: "center" }}>
        <div
          style={{
            width: 54,
            height: 54,
            borderRadius: "50%",
            background: "rgba(152,221,208,0.14)",
            border: `1.5px solid ${PRIMARY}88`,
            color: PRIMARY,
            fontSize: 26,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginBottom: 10,
          }}
        >
          ✓
        </div>
        <div style={{ fontSize: 16, fontWeight: 600, color: TEXT_MAIN }}>{text}</div>
      </div>
    );
  }

  function ErrorBlock({ error, onBack }: { error: string; onBack: () => void }) {
    return (
      <>
        <div
          style={{
            width: "100%",
            marginTop: 18,
            padding: "12px 16px",
            borderRadius: 10,
            fontSize: 13,
            background: "rgba(255,123,114,0.12)",
            border: "1px solid rgba(255,123,114,0.3)",
            color: DANGER,
            wordBreak: "break-all",
          }}
        >
          {error}
        </div>
        <ButtonRow style={{ marginTop: 16 }}>
          <PrimaryButton onClick={onBack}>返回重试</PrimaryButton>
        </ButtonRow>
      </>
    );
  }

  function FooterNote({ children }: { children: React.ReactNode }) {
    return (
      <div
        style={{
          position: "relative",
          zIndex: 2,
          textAlign: "center",
          paddingBottom: 14,
          flexShrink: 0,
          fontSize: 11.5,
          color: "rgba(238,242,245,0.4)",
        }}
      >
        {children}
      </div>
    );
  }
}
