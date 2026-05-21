import { useEffect, useRef, type ReactNode } from "react";
import { Bell, MonitorCheck, RefreshCw, Search } from "lucide-react";
import type { BridgeEnvironment } from "../bridge/contract";
import { ToolbarButton } from "../components/ToolbarButton";
import type { AsyncStatus } from "../state/useFrameScopeBridgeState";
import type { AppPage } from "../types";
import { SidebarNav } from "./SidebarNav";
import { TopStatusBar } from "./TopStatusBar";
import "./layout.css";

interface AppShellProps {
  activePage: AppPage;
  bridgeEnvironment: BridgeEnvironment;
  snapshotStatus: AsyncStatus;
  onNavigate: (page: AppPage) => void;
  children: ReactNode;
}

export function AppShell({
  activePage,
  bridgeEnvironment,
  snapshotStatus,
  onNavigate,
  children,
}: AppShellProps) {
  const viewportRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (viewportRef.current) {
      viewportRef.current.scrollTop = 0;
      viewportRef.current.scrollLeft = 0;
    }
  }, [activePage]);

  return (
    <div className="app-root">
      <div className="mac-window">
        <SidebarNav activePage={activePage} bridgeEnvironment={bridgeEnvironment} onNavigate={onNavigate} />
        <div className="workspace">
          <TopStatusBar page={activePage} bridgeEnvironment={bridgeEnvironment} snapshotStatus={snapshotStatus}>
            <div className="topbar__tools">
              <ToolbarButton icon={Search} label="Search is not connected in this phase" disabled />
              <ToolbarButton icon={RefreshCw} label="Use page refresh controls" disabled />
              <ToolbarButton icon={Bell} label="Notifications are not connected in this phase" disabled />
            </div>
          </TopStatusBar>
          <main ref={viewportRef} className="page-viewport" aria-live="polite">
            {children}
          </main>
        </div>
        <div className="mock-banner" aria-label={bridgeEnvironment === "mock" ? "Mock bridge adapter" : "WebView2 bridge"}>
          <MonitorCheck aria-hidden="true" size={14} />
          <span>{bridgeEnvironment === "mock" ? "Mock adapter" : "WebView2 bridge"}</span>
        </div>
      </div>
    </div>
  );
}
