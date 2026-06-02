import { AlertTriangle, CircleDot, RadioTower } from "lucide-react";
import type { BridgeEnvironment } from "../bridge/contract";
import { StatusPill } from "../components/StatusPill";
import type { AsyncStatus } from "../state/useFrameScopeBridgeState";
import "./layout.css";

interface TopStatusBarProps {
  bridgeEnvironment: BridgeEnvironment;
  snapshotStatus: AsyncStatus;
  monitorRunning: boolean;
  globalIssue: string;
}

export function TopStatusBar({
  bridgeEnvironment,
  snapshotStatus,
  monitorRunning,
  globalIssue,
}: TopStatusBarProps) {
  const environmentLabel = bridgeEnvironment === "mock" ? "预览模式" : "本机连接";
  const connectionText =
    snapshotStatus === "loading" ? "正在读取" : snapshotStatus === "error" ? "连接异常" : "状态正常";
  const attentionText = globalIssue ? "需要注意" : "未发现问题";

  return (
    <header className="topbar">
      <div className="topbar__status">
        <StatusPill tone={bridgeEnvironment === "mock" ? "diagnostics" : "success"}>{environmentLabel}</StatusPill>
        <span className="connection-pill">
          <RadioTower aria-hidden="true" size={14} />
          {connectionText}
        </span>
        <span className="connection-pill">
          <CircleDot aria-hidden="true" size={14} />
          {monitorRunning ? "正在监控" : "未启动"}
        </span>
        <span className={["connection-pill", globalIssue ? "connection-pill--warning" : ""].join(" ")}>
          <AlertTriangle aria-hidden="true" size={14} />
          {attentionText}
        </span>
      </div>
    </header>
  );
}
