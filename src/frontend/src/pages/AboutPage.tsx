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
          <span className="mode-ribbon">技术说明</span>
          <h2>关于 FrameScope Web UI</h2>
          <p>
            这里集中说明界面运行环境、WebView2 bridge 边界和仍未接入的功能，避免主流程页面被技术细节打断。
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
              <h3>本机宿主负责真实操作</h3>
              <p>
                React 只负责展示和收集用户输入。启动监控、保存配置、打开报告、读取进程等系统操作仍由 C# host 校验和执行。
              </p>
            </div>
          </div>
        </GlassCard>
        <GlassCard>
          <InlineStatus
            tone={bridgeState.isMockPreview ? "diagnostics" : "success"}
            title={bridgeState.isMockPreview ? "浏览器预览模式" : "WebView2 实时连接"}
            message={
              bridgeState.isMockPreview
                ? "当前不在 WebView2 宿主中，页面只使用 mock adapter 预览状态。"
                : "当前通过 window.chrome.webview 与 C# WebView2 bridge 通信。"
            }
          />
          <div className="about-list">
            <span>
              <Layers3 aria-hidden="true" size={16} />
              请求与响应类型集中在 src/frontend/src/bridge/contract.ts
            </span>
            <span>
              <Code2 aria-hidden="true" size={16} />
              requestId、超时和事件订阅由 webviewBridge.ts 管理
            </span>
            <span>
              <MonitorCheck aria-hidden="true" size={16} />
              主流程页面只显示用户需要的加载、成功和失败反馈
            </span>
          </div>
        </GlassCard>
      </div>

      <GlassCard>
        <div className="section-title">
          <div>
            <h3>仍未伪装的功能边界</h3>
            <p>没有明确后端语义的动作不会做成可点击的假功能。</p>
          </div>
          <StatusPill tone="warning">未接入</StatusPill>
        </div>
        <div className="scope-grid">
          <EmptyState
            icon={Layers3}
            title="从进程列表直接新增目标"
            description="目标保存已经有明确接口；从进程结果一键新增目标还没有确认写入规则，因此不在主流程里放假按钮。"
          />
          <EmptyState
            icon={Code2}
            title="搜索、通知和数据目录快捷入口"
            description="这些快捷入口还没有完整语义时不会占据顶栏位置。需要接入时，应先补齐真实行为再进入主界面。"
          />
        </div>
      </GlassCard>
    </section>
  );
}
