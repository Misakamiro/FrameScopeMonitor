import { CircleHelp, Database, FolderOpen, Info, MonitorCheck, ShieldCheck } from "lucide-react";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import { productVersion } from "../productVersion";
import "./pages.css";

interface AboutPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function AboutPage({ bridgeState }: AboutPageProps) {
  const dataRoot = bridgeState.config.data?.resolvedDataRoot || bridgeState.snapshot.data?.config.dataRoot || "";

  return (
    <section className="page about-page" data-smoke-page="about">
      <div className="page__header">
        <div>
          <h2>关于与帮助</h2>
          <p>了解用途、数据位置和常见问题。</p>
        </div>
      </div>

      <div className="about-grid about-grid--user">
        <GlassCard>
          <div className="about-hero">
            <div className="about-hero__mark">
              <MonitorCheck aria-hidden="true" size={34} />
            </div>
            <div>
              <h3>FrameScope Monitor</h3>
              <p>本地记录游戏帧表现和系统占用，帮助排查卡顿和掉帧。</p>
            </div>
          </div>
          <div className="about-facts">
            <span>版本：{productVersion}</span>
            <span>监控数据保存在本机，不会自动上传。</span>
            <span title={dataRoot}>数据位置：{formatPathTail(dataRoot)}</span>
          </div>
        </GlassCard>

        <GlassCard>
          <InlineStatus
            tone={bridgeState.isMockPreview ? "diagnostics" : "success"}
            title={bridgeState.isMockPreview ? "当前为预览" : "本机连接正常"}
            message={
              bridgeState.isMockPreview
                ? "预览不会读取真实系统数据。"
                : "可以读取目标、报告和设置。"
            }
          />
          <div className="about-action-list">
            <span>
              <FolderOpen aria-hidden="true" size={16} />
              数据目录可在设置页查看。
            </span>
            <span>
              <Database aria-hidden="true" size={16} />
              报告目录可在报告详情打开。
            </span>
            <span>
              <ShieldCheck aria-hidden="true" size={16} />
              诊断文件只在本机生成。
            </span>
          </div>
        </GlassCard>
      </div>

      <GlassCard>
        <div className="section-title">
          <div>
            <h3>常见问题</h3>
            <p>遇到问题时先看这里。</p>
          </div>
          <StatusPill tone="primary">帮助</StatusPill>
        </div>
        <div className="faq-grid">
          <EmptyState
            icon={CircleHelp}
            title="没有自动记录怎么办？"
            description="先确认目标已启用，再启动监控并保持软件运行。"
          />
          <EmptyState
            icon={Info}
            title="报告没有帧数据？"
            description="重新监控一次，确认游戏进程名和权限是否正确。"
          />
          <EmptyState
            icon={FolderOpen}
            title="报告打不开？"
            description="报告可能被移动。请在报告详情打开文件夹查看。"
          />
          <EmptyState
            icon={ShieldCheck}
            title="何时需要诊断？"
            description="报告异常或保存失败时，再生成诊断文件。"
          />
        </div>
      </GlassCard>

      <details className="advanced-details">
        <summary>高级信息</summary>
        <GlassCard>
          <div className="section-title">
            <div>
              <h3>技术信息</h3>
              <p>排查界面连接问题时使用。</p>
            </div>
            <StatusPill tone="diagnostics">高级</StatusPill>
          </div>
          <div className="about-list">
            <span>运行环境：{bridgeState.isMockPreview ? "浏览器预览" : "本机应用"}</span>
            <span>连接状态：{bridgeState.isMockPreview ? "预览模式" : "已连接"}</span>
            <span title={dataRoot}>数据目录：{formatPathTail(dataRoot)}</span>
            <span>问题定位：请同时提供报告时间和目标进程名。</span>
          </div>
        </GlassCard>
      </details>
    </section>
  );
}

function formatPathTail(value: string) {
  if (!value) return "设置页可查看";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts.slice(-3).join("\\") || value;
}
