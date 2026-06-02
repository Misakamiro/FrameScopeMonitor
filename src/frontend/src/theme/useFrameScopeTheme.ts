import { useEffect, useState } from "react";
import type { FrameScopeThemeMode } from "../bridge/contract";

export type ResolvedFrameScopeTheme = "light" | "dark";

export function useFrameScopeTheme(themeMode: FrameScopeThemeMode | undefined): ResolvedFrameScopeTheme {
  const normalizedMode = normalizeThemeMode(themeMode);
  const [systemTheme, setSystemTheme] = useState<ResolvedFrameScopeTheme>(() => resolveSystemTheme());
  const resolvedTheme: ResolvedFrameScopeTheme = normalizedMode === "system" ? systemTheme : normalizedMode;

  useEffect(() => {
    if (typeof window === "undefined" || typeof window.matchMedia !== "function") return;
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const handleChange = () => setSystemTheme(mediaQuery.matches ? "dark" : "light");
    handleChange();

    if (typeof mediaQuery.addEventListener === "function") {
      mediaQuery.addEventListener("change", handleChange);
      return () => mediaQuery.removeEventListener("change", handleChange);
    }

    mediaQuery.addListener(handleChange);
    return () => mediaQuery.removeListener(handleChange);
  }, []);

  useEffect(() => {
    if (typeof document === "undefined") return;
    document.documentElement.dataset.theme = resolvedTheme;
  }, [resolvedTheme]);

  return resolvedTheme;
}

function normalizeThemeMode(themeMode: FrameScopeThemeMode | undefined): FrameScopeThemeMode {
  if (themeMode === "light" || themeMode === "dark" || themeMode === "system") return themeMode;
  return "system";
}

function resolveSystemTheme(): ResolvedFrameScopeTheme {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return "light";
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}
