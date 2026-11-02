"use client";

import { useColorMode } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";
import { glassTextUsesDarkBranch } from "@/lib/ui-effects";

export interface AdaptiveTextColor {
  text: string;
  shadow: string;
}

// Only the text branch changes: the MR background remains dark when the
// selected UI theme is light and the original glass surface lets it through.
export function useGlassTextColor(light: string, dark: string, transparencyOverride?: number): string {
  const { backgroundMode, liquidGlassEnabled, liquidGlassMode, liquidGlassTransparency } = useBackground();
  const { colorMode } = useColorMode();
  if (liquidGlassEnabled && /^(gray\.|#|rgba?\(|white|black)/.test(light)) {
    return /^gray\.[456]/.test(light) ? 'var(--framescope-surface-muted)' : 'var(--framescope-surface-fg)';
  }
  return glassTextUsesDarkBranch(colorMode,backgroundMode,liquidGlassEnabled,liquidGlassMode,transparencyOverride??liquidGlassTransparency??100) ? dark : light;
}

export function useBackgroundTextColor(light: string, dark: string): string {
  const { backgroundMode } = useBackground();
  const { colorMode } = useColorMode();
  return /^(gray\.|#|rgba?\(|white|black)/.test(light) ? "var(--framescope-surface-fg)" : colorMode === "dark" || backgroundMode === "mr" ? dark : light;
}

/** Shared background sampling is owned by UiSurfaceTokens, including portal surfaces. */
export function useAdaptiveTextColor(): AdaptiveTextColor {
  return {text:'var(--framescope-surface-fg)',shadow:'none'};
}
