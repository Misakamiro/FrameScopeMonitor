import type { ReactNode } from "react";
import { RadioTower } from "lucide-react";
import type { BridgeEnvironment } from "../bridge/contract";
import { StatusPill } from "../components/StatusPill";
import { navigationItems } from "../data/mockPreview";
import type { AsyncStatus } from "../state/useFrameScopeBridgeState";
import type { AppPage, Tone } from "../types";
import "./layout.css";

interface TopStatusBarProps {
  page: AppPage;
  bridgeEnvironment: BridgeEnvironment;
  snapshotStatus: AsyncStatus;
  children?: ReactNode;
}

export function TopStatusBar({ page, bridgeEnvironment, snapshotStatus, children }: TopStatusBarProps) {
  const item = navigationItems.find((nav) => nav.id === page) ?? navigationItems[0];
  const Icon = item.icon;
  const statusTone: Tone =
    snapshotStatus === "error" ? "danger" : snapshotStatus === "success" ? "success" : "diagnostics";
  const bridgeLabel = bridgeEnvironment === "mock" ? "Mock preview" : "WebView2";
  const connectionText =
    snapshotStatus === "loading" ? "Loading" : snapshotStatus === "error" ? "Bridge error" : "Bridge ready";

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
        <StatusPill tone={statusTone}>{bridgeLabel}</StatusPill>
        <span className="connection-pill">
          <RadioTower aria-hidden="true" size={14} />
          {connectionText}
        </span>
        {children}
      </div>
    </header>
  );
}
