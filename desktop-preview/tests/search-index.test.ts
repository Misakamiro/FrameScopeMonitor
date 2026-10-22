import { beforeAll, describe, expect, it } from "vitest";
import { createInstance } from "i18next";
import { readFileSync } from "node:fs";
import zh from "../src/locales/zh.json";
import en from "../src/locales/en.json";
import { searchIndex, findSearchResults } from "../src/config/search-index";

const i18n = createInstance();
beforeAll(async () => { await i18n.init({ lng: "zh", resources: { zh: { translation: zh }, en: { translation: en } } }); });

describe("global search coverage", () => {
  it("indexes real secondary menus and settings, not only their parent pages", () => {
    const labels = searchIndex.map(item => i18n.t(item.nameKey));
    for (const label of ["通用", "FrameScope 采样与报告", "随 Windows 启动", "手动启动时也直接进入托盘",
      "全局采样间隔（毫秒）", "页面切换动画", "背景模糊", "玻璃模式", "使用 GitHub 加速下载",
      "启动时检查更新", "CPU分核电压", "单行宽度", "启动软件时自动开启悬浮窗", "启动监控", "添加目标", "重新生成报告"]) {
      expect(labels, `missing searchable setting: ${label}`).toContain(label);
    }
  });
  it("keeps existing page identities and valid routes", () => {
    expect(new Set(searchIndex.map(item => item.id)).size).toBe(searchIndex.length);
    for (const id of ["home", "hardware", "targets", "reports", "overlay", "settings", "appearance", "about"])
      expect(searchIndex.some(item => item.id === id)).toBe(true);
    for (const item of searchIndex) expect(["/", "/hardware", "/targets", "/reports", "/overlay-panel", "/settings"])
      .toContain(new URL(item.path, "http://localhost").pathname);
  });
  it("covers the labels actually used by the settings components and every OSD metric", () => {
    const source = (name: string) => readFileSync(new URL(`../src/${name}`, import.meta.url), "utf8");
    const settings = source("pages/SettingsPage.tsx");
    const names = new Set(searchIndex.map(item => item.nameKey));
    for (const match of settings.matchAll(/t\("(settings\.(?:generalSettings|appearanceSettings)\.[^"]*Label)"\)/g))
      expect(names, `missing rendered label ${match[1]}`).toContain(match[1]);
    const framescope = source("preview/settings/FrameScopeSettings.tsx");
    for (const match of framescope.matchAll(/aria-label="([^"]+)"/g)) expect(names).toContain(match[1]);
    const metrics = source("preview/runtime/state.ts").split("].map")[0];
    for (const match of metrics.matchAll(/\["[^"]+", "([^"]+)"\]/g)) expect(names).toContain(match[1]);
  });
  it("matches aliases, separated words, full-width input and current-language labels", () => {
    const t = (key: string) => i18n.t(key);
    const ids = (query: string) => findSearchResults(query, searchIndex, t).map(item => item.id);
    expect(ids("开机自启")).toContain("fs-windows-startup");
    expect(ids("常规")).toContain("general");
    expect(ids("  ＣＰＵ　电压  ")).toContain("overlay-metric-cpu_voltage");
    expect(ids("竖排 透明度")).toContain("overlay-opacity");
    expect(ids("ＰａｗｎＩＯ")).toContain("hardware-driver");
    expect(ids("GitHub加速")).toContain("update-accelerated");
    expect(ids("Windows启动")).toContain("fs-windows-startup");
    expect(ids("1%Low")).toContain("overlay-metric-fps_1low");
    expect(ids("  ")).toEqual([]);
    expect(ids("不存在的设置xyz")).toEqual([]);
    const english = i18n.getFixedT("en");
    expect(findSearchResults(english("settings.appearanceSettings.fontLabel"), searchIndex, english)
      .some(item => item.id === "appearance-font")).toBe(true);
  });
  it("ranks an exact setting ahead of broad page keywords and links to its owning section", () => {
    const result = findSearchResults("透明度", searchIndex, key => i18n.t(key));
    expect(result[0].id).toBe("overlay-opacity");
    for (const item of searchIndex.filter(item => item.targetId)) {
      expect(new URL(item.path, "http://localhost").searchParams.get("focus")).toBe(item.id);
      expect(item.action ?? "navigate").toBe("navigate");
    }
  });
});
