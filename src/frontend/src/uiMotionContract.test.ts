import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import appSource from "./App.tsx?raw";
import buttonSource from "./components/Button.tsx?raw";
import chartShellSource from "./components/ChartShell.tsx?raw";
import inlineStatusSource from "./components/InlineStatus.tsx?raw";
import toolbarButtonSource from "./components/ToolbarButton.tsx?raw";
import pageTransitionSource from "./layout/PageTransition.tsx?raw";
import sidebarNavSource from "./layout/SidebarNav.tsx?raw";
import mainSource from "./main.tsx?raw";
import reportsPageSource from "./pages/ReportsPage.tsx?raw";
import targetsPageSource from "./pages/TargetsPage.tsx?raw";
import motionSource from "./theme/motion.ts?raw";

const componentCssSource = readFileSync(new URL("./components/components.css", import.meta.url), "utf8");
const layoutCssSource = readFileSync(new URL("./layout/layout.css", import.meta.url), "utf8");
const pagesCssSource = readFileSync(new URL("./pages/pages.css", import.meta.url), "utf8");
const tokenCssSource = readFileSync(new URL("./theme/tokens.css", import.meta.url), "utf8");

describe("FrameScope motion contract", () => {
  it("keeps page navigation synchronous without page fade, slide, scale, blur, or exit", () => {
    expect(pageTransitionSource).not.toContain("AnimatePresence");
    expect(pageTransitionSource).not.toContain("exit=");
    expect(pageTransitionSource).not.toContain("initial={{ opacity: 0");
    expect(pageTransitionSource).not.toContain("initial=\"initial\"");
    expect(motionSource).not.toMatch(/pageVariants[\s\S]*opacity:\s*0/);
    expect(motionSource).not.toMatch(/pageVariants[\s\S]*\b[xy]:\s*-?\d/);
    expect(motionSource).not.toMatch(/pageVariants[\s\S]*scale:/);
    expect(layoutCssSource).not.toMatch(/\.page-transition[\s\S]*opacity:\s*0/);
    expect(layoutCssSource).not.toMatch(/\.page-transition[\s\S]*transform:/);
    expect(layoutCssSource).not.toMatch(/\.page-transition[\s\S]*filter:/);
  });

  it("uses a single restrained token vocabulary instead of crossfade or stagger helpers", () => {
    expect(motionSource).toContain("micro");
    expect(motionSource).toContain("state");
    expect(motionSource).toContain("content");
    expect(motionSource).toContain("navCommit");
    expect(motionSource).toContain("press");
    expect(motionSource).not.toContain("crossfade");
    expect(motionSource).not.toContain("springRoute");
    expect(motionSource).not.toContain("listItemVariants");
    expect(motionSource).not.toMatch(/stagger|delay:/);
    expect(tokenCssSource).toContain("--fs-motion-micro");
    expect(tokenCssSource).toContain("--fs-motion-state");
    expect(tokenCssSource).toContain("--fs-motion-content");
    expect(tokenCssSource).toContain("--fs-motion-nav");
    expect(tokenCssSource).toContain("--fs-motion-press");
  });

  it("keeps buttons and sidebar hover stable without desktop-control drift", () => {
    const buttonSources = [buttonSource, toolbarButtonSource, sidebarNavSource].join("\n");
    expect(buttonSources).not.toMatch(/whileHover=\{[^}]*[xy]:/);
    expect(buttonSources).not.toMatch(/whileTap=\{[^}]*scale:\s*0\.9[0-7]/);
    expect(buttonSources).not.toContain('layoutId="activeNav"');
    expect(componentCssSource).toContain("var(--fs-motion-micro)");
    expect(componentCssSource).toContain("var(--fs-motion-press)");
  });

  it("keeps static page commits and button press feedback out of the JS motion runtime", () => {
    const staticFeedbackSources = [appSource, mainSource, pageTransitionSource, buttonSource, toolbarButtonSource, motionSource].join("\n");
    expect(staticFeedbackSources).not.toContain('from "framer-motion"');
    expect(staticFeedbackSources).not.toContain("<motion.");
    expect(staticFeedbackSources).not.toContain("whileTap=");
    expect(staticFeedbackSources).not.toContain("MotionConfig");
    expect(appSource).not.toContain("useReducedMotion");
    expect(componentCssSource).toMatch(/\.fs-button:active:not\(:disabled\)[\s\S]*transform:\s*scale\(0\.992\)/s);
  });

  it("keeps sidebar active state inside the clickable item while keeping focus separate", () => {
    expect(sidebarNavSource).toContain("nav-item__rail");
    expect(sidebarNavSource).not.toContain("nav-active-indicator");
    expect(sidebarNavSource).not.toContain("data-active-index");
    expect(layoutCssSource).not.toContain(".nav-active-indicator");
    expect(layoutCssSource).toMatch(/\.nav-item__rail\s*{[\s\S]*top:\s*var\(--fs-nav-rail-inset\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item__rail\s*{[\s\S]*bottom:\s*var\(--fs-nav-rail-inset\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item--active\s+\.nav-item__rail\s*{[\s\S]*background:\s*var\(--fs-accent-primary\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item:focus-visible\s*{[\s\S]*outline:\s*2px solid var\(--fs-focus-ring\)/s);
    expect(layoutCssSource).toMatch(/\.nav-item--active:focus-visible\s*{[\s\S]*box-shadow:\s*inset 0 0 0 1px var\(--fs-nav-active-border\)/s);
  });

  it("animates report menus as anchored command layers without bounce or lingering focus state", () => {
    expect(reportsPageSource).toContain("report-more-menu--closing");
    expect(reportsPageSource).toContain("closeReportMenu");
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[\s\S]*transform-origin:\s*top right/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu\s*{[\s\S]*animation:\s*report-menu-enter var\(--fs-motion-state\)/s);
    expect(pagesCssSource).toMatch(/\.report-more-menu--closing\s*{[\s\S]*animation:\s*report-menu-exit var\(--fs-motion-micro\)/s);
    expect(pagesCssSource).not.toMatch(/report-menu-[\w-]+[\s\S]*scale\(0\.[0-8]/);
    expect(pagesCssSource).not.toMatch(/report-menu-[\w-]+[\s\S]*blur/);
    expect(pagesCssSource).not.toMatch(/report-menu-[\w-]+[\s\S]*bounce/);
  });

  it("keeps target process search contextual with local busy and row-update feedback", () => {
    expect(targetsPageSource).toContain("lastProcessRefreshKey");
    expect(targetsPageSource).toContain("process-result-list--refreshing");
    expect(targetsPageSource).toContain("process-result-row--updated");
    expect(targetsPageSource).toContain("process-result-list__status");
    expect(targetsPageSource).not.toContain("bridgeState.processRefresh.status === \"loading\" ? null");
    expect(pagesCssSource).toMatch(/\.process-result-row--updated\s*{[\s\S]*animation:\s*process-row-settle var\(--fs-motion-content\)/s);
    expect(pagesCssSource).toMatch(/\.process-result-list--refreshing\s+\.process-result-row\s*{[\s\S]*background-color:/s);
  });

  it("does not draw or stagger charts like a demo animation", () => {
    expect(chartShellSource).not.toContain("pathLength");
    expect(chartShellSource).not.toMatch(/delay:\s*index/);
    expect(chartShellSource).not.toContain("<motion.g");
  });

  it("turns busy rotation into static feedback under reduced motion", () => {
    expect(inlineStatusSource).toContain("inline-status__busy");
    expect(componentCssSource).toMatch(/\.inline-status__busy\s*{[\s\S]*animation:\s*fs-spin/);
    expect(componentCssSource).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.inline-status__busy\s*{[\s\S]*animation:\s*none/);
    expect(componentCssSource).toMatch(
      /@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.fs-button:active:not\(:disabled\)[\s\S]*\.toolbar-button:active:not\(:disabled\)[\s\S]*\.empty-state__action:active:not\(:disabled\)[\s\S]*transform:\s*none/s,
    );
  });
});
