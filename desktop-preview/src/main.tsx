import "./preview/browser";
import React, { useEffect, useState } from "react";
import ReactDOM from "react-dom/client";
import { ChakraProvider, Spinner, Center } from "@chakra-ui/react";
import { CacheProvider } from "@emotion/react";
import createCache from "@emotion/cache";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import "./index.css";
import { I18nextProvider } from "react-i18next";
import i18n from "./lib/i18n";
import { BackgroundProvider } from "./contexts/background-context";
import { ThemeColorProvider } from "./contexts/theme-color-context";
import { AppStartupProvider } from "./contexts/app-startup-context";
import { FontProvider } from "./contexts/font-context";
import { ThemeModeProvider } from "./contexts/theme-mode-context";
import { UpdateProvider } from "./contexts/update-context";
import { LiquidGlassSvgFilter } from "./components/special/liquid-glass-svg-filter";
import { RootErrorBoundary } from "./components/RootErrorBoundary";
import theme, {readInitialScheme} from "./lib/theme";
import { startPreviewRuntime } from "./preview/runtime";
import { startWindowActivity } from "./lib/window-activity";
import {traceStartup} from './lib/startup-trace';
import {UiSurfaceTokens} from './components/UiSurfaceTokens';
const emotionCache = createCache({ key: "css", prepend: true });
const colorModeManager = {
  type:'localStorage' as const,
  get:readInitialScheme,
  set:(mode:string)=>{try{localStorage.setItem('chakra-ui-color-mode',mode);}catch{/* Keep the resolved in-memory theme. */}},
};
if (window.location.pathname === "/vertical-overlay") {
  document.documentElement.dataset.overlayWindow = "vertical";
}

function Root() {
  const [isI18nReady, setIsI18nReady] = useState(i18n.isInitialized);
  useEffect(() => {
    traceStartup('react-mounted');
    const ready = () => setIsI18nReady(true);
    i18n.on("initialized", ready);
    if (i18n.isInitialized) ready();
    const stopActivity=startWindowActivity();
    const stop = startPreviewRuntime();
    return () => { stop();stopActivity(); i18n.off("initialized", ready); };
  }, []);
  if (!isI18nReady) return <Center h="100vh"><Spinner size="xl" /></Center>;
  return <CacheProvider value={emotionCache}>
    <I18nextProvider i18n={i18n}>
      <ChakraProvider theme={theme} colorModeManager={colorModeManager}>
        <BrowserRouter>
          <AppStartupProvider>
            <BackgroundProvider>
              <ThemeColorProvider>
                <UiSurfaceTokens/>
                <FontProvider>
                  <LiquidGlassSvgFilter />
                  <ThemeModeProvider><UpdateProvider><App /></UpdateProvider></ThemeModeProvider>
                </FontProvider>
              </ThemeColorProvider>
            </BackgroundProvider>
          </AppStartupProvider>
        </BrowserRouter>
      </ChakraProvider>
    </I18nextProvider>
  </CacheProvider>;
}
ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <RootErrorBoundary>
      <Root />
    </RootErrorBoundary>
  </React.StrictMode>
);
