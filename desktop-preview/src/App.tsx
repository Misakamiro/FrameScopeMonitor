import { Routes, Route, useLocation, Navigate } from "react-router-dom";
import { AnimatePresence } from "framer-motion";
import { Box } from "@chakra-ui/react";
import { MainLayout } from "./components/ui/main-layout";
import { AnimatedPage, useTransitionMode } from "./components/ui/animated-page";
import HomePage from "./pages/HomePage";
import HardwarePage from "./pages/HardwarePage";
import SettingsPage from "./pages/SettingsPage";
import OverlayPanelPage from "./pages/OverlayPanelPage";
import VerticalOverlayPage from "./pages/VerticalOverlayPage";
import SensorMonitorPage from "./pages/SensorMonitorPage";
import TargetsPage from "./preview/pages/TargetsPage";
import ReportsPage from "./preview/pages/ReportsPage";
import { useAppStartup } from "./contexts/app-startup-context";
import { SplashScreen } from "./components/SplashScreen";
import { DynamicIslandHost } from "./components/ui/dynamic-island";

export default function App() {
  const location = useLocation();
  const { isStartupComplete } = useAppStartup();
  const mode = useTransitionMode();
  if (location.pathname === "/sensor-monitor") return <SensorMonitorPage />;
  if (location.pathname === "/vertical-overlay") return <VerticalOverlayPage />;

  const pages = (
    <Routes location={location} key={mode === "off" ? undefined : location.pathname}>
      <Route path="/" element={null} />
      <Route path="/hardware" element={<AnimatedPage><HardwarePage /></AnimatedPage>} />
      <Route path="/targets" element={<AnimatedPage><TargetsPage /></AnimatedPage>} />
      <Route path="/reports" element={<AnimatedPage><ReportsPage /></AnimatedPage>} />
      <Route path="/overlay-panel" element={<AnimatedPage><OverlayPanelPage /></AnimatedPage>} />
      <Route path="/settings" element={<AnimatedPage><SettingsPage /></AnimatedPage>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
  return (
    <>
      {!isStartupComplete && <SplashScreen />}
      <MainLayout>
        <Box display={location.pathname === "/" ? "block" : "none"}>
          <AnimatedPage><HomePage /></AnimatedPage>
        </Box>
        {/* popLayout: 退出的页面绝对定位原地淡出、新页面立即挂载在下层淡入 ——
            页面是玻璃透明设计（壁纸透出），mode="wait" 的"先淡出完再淡入"会让
            合成不透明度在中间归零、露出裸壁纸形成闪烁；popLayout 交叉淡化无空窗。 */}
        {mode === "off" ? pages : <AnimatePresence mode="popLayout" initial={false}>{pages}</AnimatePresence>}
      </MainLayout>
      <DynamicIslandHost />
    </>
  );
}
