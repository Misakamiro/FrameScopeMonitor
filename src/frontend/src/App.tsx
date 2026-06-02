import { useMemo, useState } from "react";
import { AppShell } from "./layout/AppShell";
import { PageTransition } from "./layout/PageTransition";
import { AboutPage } from "./pages/AboutPage";
import { OverviewPage } from "./pages/OverviewPage";
import { ReportsPage } from "./pages/ReportsPage";
import { SettingsPage } from "./pages/SettingsPage";
import { TargetsPage } from "./pages/TargetsPage";
import { useFrameScopeBridgeState } from "./state/useFrameScopeBridgeState";
import { useFrameScopeTheme } from "./theme/useFrameScopeTheme";
import type { AppPage } from "./types";

export default function App() {
  const [activePage, setActivePage] = useState<AppPage>("overview");
  const bridgeState = useFrameScopeBridgeState();
  useFrameScopeTheme(bridgeState.config.data?.config.ThemeMode);

  const page = useMemo(() => {
    switch (activePage) {
      case "targets":
        return <TargetsPage bridgeState={bridgeState} />;
      case "reports":
        return <ReportsPage bridgeState={bridgeState} />;
      case "settings":
        return <SettingsPage bridgeState={bridgeState} />;
      case "about":
        return <AboutPage bridgeState={bridgeState} />;
      case "overview":
      default:
        return <OverviewPage bridgeState={bridgeState} />;
    }
  }, [activePage, bridgeState]);

  return (
    <AppShell
      activePage={activePage}
      bridgeEnvironment={bridgeState.environment}
      monitorRunning={Boolean(bridgeState.monitorRuntime.running ?? bridgeState.snapshot.data?.watcher.running)}
      globalIssue={bridgeState.snapshot.data?.watcher.lastError || bridgeState.lastErrorEvent?.message || ""}
      snapshotStatus={bridgeState.snapshot.status}
      onNavigate={setActivePage}
    >
      <PageTransition key={activePage}>{page}</PageTransition>
    </AppShell>
  );
}
