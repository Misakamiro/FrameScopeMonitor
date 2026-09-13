import { Routes, Route, useLocation, Navigate } from "react-router-dom";
import { AnimatePresence, motion } from "framer-motion";
import { ReactNode, useState } from "react";
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
  const isHome = location.pathname === "/";
  // 离开主页时先淡出、淡出完成后才真正 display:none —— 常驻挂载的主页
  // 若瞬间消失，新页面从透明淡入期间会露出裸壁纸（与路由页同源的闪烁）。
  const [homeFadedOut, setHomeFadedOut] = useState(false);

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

  let home: ReactNode;
  if (mode === "off") {
    home = (
      <Box display={isHome ? "block" : "none"}>
        <AnimatedPage><HomePage /></AnimatedPage>
      </Box>
    );
  } else {
    home = (
      <motion.div
        initial={false}
        animate={{ opacity: isHome ? 1 : 0 }}
        transition={{ duration: 0.18, ease: "easeOut" }}
        onAnimationComplete={() => setHomeFadedOut(!isHome)}
        style={{
          display: isHome || !homeFadedOut ? "block" : "none",
          position: isHome ? "relative" : "absolute",
          inset: isHome ? undefined : 0,
          width: "100%",
          pointerEvents: isHome ? "auto" : "none",
        }}
      >
        <AnimatedPage><HomePage /></AnimatedPage>
      </motion.div>
    );
  }

  return (
    <>
      {!isStartupComplete && <SplashScreen />}
      <MainLayout>
        {home}
        {/* popLayout: 退出的页面绝对定位原地淡出、新页面立即挂载在下层淡入 ——
            页面是玻璃透明设计（壁纸透出），mode="wait" 的"先淡出完再淡入"会让
            合成不透明度在中间归零、露出裸壁纸形成闪烁；popLayout 交叉淡化无空窗。 */}
        {mode === "off" ? pages : <AnimatePresence mode="popLayout" initial={false}>{pages}</AnimatePresence>}
      </MainLayout>
      <DynamicIslandHost />
    </>
  );
}
