import { Code2, Layers3, MonitorCheck } from "lucide-react";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import "./pages.css";

interface AboutPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function AboutPage({ bridgeState }: AboutPageProps) {
  return (
    <section className="page about-page" data-smoke-page="about">
      <div className="page__header">
        <div>
          <span className="mock-ribbon">FrameScope Web UI</span>
          <h2>WebView2 React UI</h2>
          <p>
            当前界面是 FrameScope Monitor 的默认界面。React 前端通过 typed bridge client 接入
            C# WebView2 bridge，所有系统操作仍由本机 C# host 校验和执行。
          </p>
        </div>
      </div>

      <div className="about-grid">
        <GlassCard>
          <div className="about-hero">
            <div className="about-hero__mark">
              <MonitorCheck aria-hidden="true" size={34} />
            </div>
            <div>
              <h3>真实 Bridge 交互</h3>
              <p>
                state、config、processes、reports、targets、monitor 和 diagnostics
                已接入真实 WebView2 bridge。普通浏览器预览只使用集中 mock adapter，并会显示 mock 标签。
              </p>
            </div>
          </div>
        </GlassCard>
        <GlassCard>
          <InlineStatus
            tone={bridgeState.isMockPreview ? "diagnostics" : "success"}
            title={bridgeState.isMockPreview ? "Mock adapter preview" : "WebView2 bridge live"}
            message={
              bridgeState.isMockPreview
                ? "当前不是 WebView2 环境，系统数据来自 mock adapter。"
                : "当前通过 window.chrome.webview 与 C# bridge 通信。"
            }
          />
          <div className="about-list">
            <span>
              <Layers3 aria-hidden="true" size={16} />
              Contract types in src/frontend/src/bridge/contract.ts
            </span>
            <span>
              <Code2 aria-hidden="true" size={16} />
              Request timeout and event subscriptions in webviewBridge.ts
            </span>
            <span>
              <MonitorCheck aria-hidden="true" size={16} />
              Local loading, success, and failure feedback
            </span>
          </div>
        </GlassCard>
      </div>

      <GlassCard>
        <div className="section-title">
          <div>
            <h3>仍保持 disabled 的边界</h3>
            <p>这些动作没有对应的后端语义时不会伪装可用。</p>
          </div>
          <StatusPill tone="warning">disabled</StatusPill>
        </div>
        <div className="scope-grid">
          <EmptyState
            icon={Layers3}
            title="从进程列表直接新增目标"
            description="目标保存已接 targets.save；从进程结果一键新增目标仍保持 disabled，避免前端自行定义写入语义。"
          />
          <EmptyState
            icon={Code2}
            title="搜索、通知和打开数据目录"
            description="顶栏搜索、通知和 Overview 数据目录打开没有对应 request 时继续保持 disabled。"
          />
        </div>
      </GlassCard>
    </section>
  );
}
