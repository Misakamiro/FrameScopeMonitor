import { RadioTower } from "lucide-react";
import type { BridgeEnvironment } from "../bridge/contract";
import { StatusPill } from "../components/StatusPill";
import { navigationItems } from "../data/mockPreview";
import type { AsyncStatus } from "../state/useFrameScopeBridgeState";
import type { AppPage } from "../types";
import "./layout.css";

interface TopStatusBarProps {
  page: AppPage;
  bridgeEnvironment: BridgeEnvironment;
  snapshotStatus: AsyncStatus;
}

export function TopStatusBar({ page, bridgeEnvironment, snapshotStatus }: TopStatusBarProps) {
  const item = navigationItems.find((nav) => nav.id === page) ?? navigationItems[0];
  const Icon = item.icon;
  const environmentLabel = bridgeEnvironment === "mock" ? "预览模式" : "本机连接";
  const connectionText =
    snapshotStatus === "loading" ? "正在读取" : snapshotStatus === "error" ? "连接异常" : "状态正常";

  return (
    <header className="topbar">
      <div className="topbar__title">
        <span className="topbar__icon">
          <Icon aria-hidden="true" size={20} />
        </span>
        <div>
          <h1>{item.label}</h1>
          <p>{item.description}</p>
        </div>
      </div>
      <div className="topbar__status">
        <StatusPill tone={bridgeEnvironment === "mock" ? "diagnostics" : "success"}>{environmentLabel}</StatusPill>
        <span className="connection-pill">
          <RadioTower aria-hidden="true" size={14} />
          {connectionText}
        </span>
      </div>
    </header>
  );
}
