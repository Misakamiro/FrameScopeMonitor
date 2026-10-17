import "./preview/browser";
import React, { useEffect, useState } from "react";
import ReactDOM from "react-dom/client";
import { ChakraProvider, ColorModeScript, Spinner, Center } from "@chakra-ui/react";
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
import { LiquidGlassSvgFilter } from "./components/special/liquid-glass-svg-filter";
import { RootErrorBoundary } from "./components/RootErrorBoundary";
import theme from "./lib/theme";
import { startPreviewRuntime } from "./preview/runtime";
const emotionCache = createCache({ key: "css", prepend: true });
if (window.location.pathname === "/vertical-overlay") {
  document.documentElement.dataset.overlayWindow = "vertical";
}

function Root() {
  const [isI18nReady, setIsI18nReady] = useState(i18n.isInitialized);
  useEffect(() => {
    const ready = () => setIsI18nReady(true);
    i18n.on("initialized", ready);
    if (i18n.isInitialized) ready();
    const stop = startPreviewRuntime();
    return () => { stop(); i18n.off("initialized", ready); };
  }, []);
  if (!isI18nReady) return <Center h="100vh"><Spinner size="xl" /></Center>;
  return <CacheProvider value={emotionCache}>
    <ColorModeScript initialColorMode={theme.config.initialColorMode} />
    <I18nextProvider i18n={i18n}>
      <ChakraProvider theme={theme}>
        <BrowserRouter>
          <AppStartupProvider>
            <BackgroundProvider>
              <ThemeColorProvider>
                <FontProvider>
                  <LiquidGlassSvgFilter />
                  <ThemeModeProvider><App /></ThemeModeProvider>
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
