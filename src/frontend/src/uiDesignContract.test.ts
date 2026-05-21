import { describe, expect, it } from "vitest";
import appShellSource from "./layout/AppShell.tsx?raw";
import sidebarNavSource from "./layout/SidebarNav.tsx?raw";
import layoutCssSource from "./layout/layout.css?raw";
import overviewPageSource from "./pages/OverviewPage.tsx?raw";
import targetsPageSource from "./pages/TargetsPage.tsx?raw";
import reportsPageSource from "./pages/ReportsPage.tsx?raw";
import settingsPageSource from "./pages/SettingsPage.tsx?raw";
import { navigationItems } from "./data/mockPreview";

function withoutImports(text: string) {
  return text
    .split("\n")
    .filter((line) => !line.trimStart().startsWith("import "))
    .join("\n");
}

function withoutImplementationIdentifiers(text: string) {
  return withoutImports(text).replace(/bridgeState\./g, "");
}

describe("FrameScope UI design contract", () => {
  it("keeps the application shell native to the WebView client area", () => {
    const shellSources = [
      appShellSource,
      sidebarNavSource,
      layoutCssSource,
    ].join("\n");

    expect(shellSources).not.toMatch(/mac-window|window-controls/);
    expect(shellSources).not.toMatch(/Search|Bell|Notifications are not connected|not connected in this phase/);
  });

  it("keeps technical bridge details out of the main workflow pages", () => {
    const workflowSources = [
      overviewPageSource,
      targetsPageSource,
      reportsPageSource,
      settingsPageSource,
    ].map(withoutImplementationIdentifiers).join("\n");

    expect(workflowSources).not.toMatch(
      /WebView2|\bbridge\b|mock adapter|requestId|state\.snapshot|config\.get|config\.save|targets\.get|targets\.save|reports\.list/i,
    );
  });

  it("uses user-facing navigation descriptions instead of implementation notes", () => {
    expect(navigationItems.map((item) => item.description).join("\n")).not.toMatch(
      /静态预览|视觉结构|前端边界|版本|mock|bridge|WebView2/i,
    );
  });
});
