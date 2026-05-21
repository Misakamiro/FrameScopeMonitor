import { useEffect, useRef, type ReactNode } from "react";
import type { BridgeEnvironment } from "../bridge/contract";
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
      <div className="app-shell">
        <SidebarNav activePage={activePage} bridgeEnvironment={bridgeEnvironment} onNavigate={onNavigate} />
        <div className="workspace">
          <TopStatusBar page={activePage} bridgeEnvironment={bridgeEnvironment} snapshotStatus={snapshotStatus} />
          <main ref={viewportRef} className="page-viewport" aria-live="polite">
            {children}
          </main>
        </div>
      </div>
    </div>
  );
}
