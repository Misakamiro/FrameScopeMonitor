import { useMemo, useState } from "react";
import { useReducedMotion } from "framer-motion";
import { AppShell } from "./layout/AppShell";
import { PageTransition } from "./layout/PageTransition";
import { AboutPage } from "./pages/AboutPage";
import { OverviewPage } from "./pages/OverviewPage";
import { ReportsPage } from "./pages/ReportsPage";
import { SettingsPage } from "./pages/SettingsPage";
import { TargetsPage } from "./pages/TargetsPage";
import { useFrameScopeBridgeState } from "./state/useFrameScopeBridgeState";
import type { AppPage } from "./types";

export default function App() {
  const [activePage, setActivePage] = useState<AppPage>("overview");
  const reduceMotion = useReducedMotion();
  const bridgeState = useFrameScopeBridgeState();

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
      snapshotStatus={bridgeState.snapshot.status}
      onNavigate={setActivePage}
    >
      <PageTransition key={activePage} reduceMotion={Boolean(reduceMotion)}>
        {page}
      </PageTransition>
    </AppShell>
  );
}
