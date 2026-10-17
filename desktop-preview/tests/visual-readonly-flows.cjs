const assert = require("node:assert/strict");
const { mkdir, writeFile } = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");

const url = process.env.PREVIEW_URL || "http://127.0.0.1:1431";
const output = path.resolve(process.env.FLOW_OUTPUT || "../artifacts/20260918-repair/visual-flows");
const report = {
  environment: "isolated Chromium browser fixtures; not actual collection or installed WebView2",
  operations: "navigation, search, read dialogs and settings tabs only; no save/start/install/export",
  assertions: [], console: [], pageerrors: [], requestFailures: [],
};

(async () => {
  assert.match(url, /^http:\/\/127\.0\.0\.1:\d+\/?$/);
  await mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
  });
  report.browser = browser.version();
  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    page.on("console", event => report.console.push({
      type: event.type(), text: event.text(), location: event.location(),
    }));
    page.on("pageerror", error => report.pageerrors.push(error.stack || error.message));
    page.on("requestfailed", request => report.requestFailures.push({
      url: request.url(), failure: request.failure(),
    }));
    await page.addInitScript(() => {
      localStorage.setItem("framescope.preview.store.settings.json", JSON.stringify({
        "has-launched": true, "background-mode": "preset", "active-preset-index": 0,
        "carousel-enabled": false, "liquid-glass-enabled": true,
        "liquid-glass-blur": 1, "liquid-glass-mode": "normal",
        "jelly-bounce-enabled": false, nexbox_page_transition: "fade",
      }));
      localStorage.setItem("nexbox_page_transition", "fade");
    });
    const check = async (name, run) => {
      const result = { name, status: "RUNNING" };
      report.assertions.push(result);
      try {
        await run();
        result.status = "PASS";
      } catch (error) {
        result.status = "FAIL";
        result.error = String(error);
        throw error;
      }
    };
    const navigate = async route => {
      await page.locator(`#main-sidebar a[href="${route}"]`).click();
      await page.waitForTimeout(500);
      assert.equal(new URL(page.url()).pathname, route);
    };
    const snapshot = name => page.screenshot({ path: path.join(output, `${name}.png`) });
    const state = () => page.evaluate(async () => {
      const { getPreviewState } = await import("/src/preview/runtime/state.ts");
      const value = getPreviewState();
      return {
        config: value.config, targets: value.targets, reports: value.reports,
        monitor: value.monitor, overlay: value.overlay,
      };
    });
    await page.goto(url);
    await page.locator('#main-sidebar a[href="/hardware"]').waitFor();
    await page.waitForTimeout(1000);
    report.before = await state();
    await check("isolated fixture mode with mock IPC, not the native backend", async () => {
      assert.deepEqual(await page.evaluate(async () => {
        const { isDesktop } = await import("/src/preview/runtime/platform.ts");
        return { desktop: isDesktop(), browserPreview: window.__FRAMESCOPE_BROWSER_PREVIEW__ };
      }), { desktop: false, browserPreview: true });
      assert.equal(report.before.monitor.phase, "idle");
      assert.ok(report.before.reports.every(item => item.simulated));
    });
    await check("home hardware summary and idle monitor controls", async () => {
      assert.ok(await page.getByRole("button", { name: "启动监控", exact: true }).isEnabled());
      assert.ok(await page.getByRole("button", { name: "停止", exact: true }).isDisabled());
      assert.match(await page.locator("body").innerText(), /7800X3D/);
      await snapshot("01-home");
    });
    await check("hardware renders fixture values without installing drivers", async () => {
      await navigate("/hardware");
      assert.match(await page.locator("body").innerText(), /硬件信息[\s\S]*温度 \d+°C[\s\S]*4070 SUPER/);
      await snapshot("02-hardware");
    });
    await check("targets search, empty state and cancel editor without mutation", async () => {
      await navigate("/targets");
      await page.getByRole("textbox", { name: "搜索目标" }).fill("nonexistent-target");
      assert.ok(await page.getByText("没有匹配的目标", { exact: true }).isVisible());
      await page.getByRole("textbox", { name: "搜索目标" }).fill("");
      assert.equal(await page.getByRole("checkbox", { name: /^启用 / }).count(), 2);
      await page.getByRole("button", { name: "编辑 Counter-Strike 2", exact: true }).click();
      await page.getByRole("dialog").waitFor();
      assert.match(await page.getByRole("dialog").innerText(), /编辑目标/);
      await page.getByRole("button", { name: "取消", exact: true }).click();
      await page.getByRole("dialog").waitFor({ state: "hidden" });
      await snapshot("03-targets");
    });
    await check("report search, four fixture kinds and read-only detail dialog", async () => {
      await navigate("/reports");
      assert.equal(await page.getByRole("button", { name: "查看详情", exact: true }).count(), 4);
      await page.getByRole("textbox", { name: "搜索报告" }).fill("nonexistent-report");
      assert.ok(await page.getByText("暂无匹配的报告", { exact: true }).isVisible());
      await page.getByRole("textbox", { name: "搜索报告" }).fill("");
      await page.getByRole("button", { name: "查看详情", exact: true }).first().click();
      const dialog = page.getByRole("dialog");
      await dialog.waitFor();
      assert.match(await dialog.innerText(), /报告详情[\s\S]*模拟[\s\S]*144[\s\S]*112[\s\S]*94/);
      await snapshot("04-report-detail");
      await page.keyboard.press("Escape");
      await dialog.waitFor({ state: "hidden" });
    });
    await check("OSD settings render disabled state and display options without enabling", async () => {
      await navigate("/overlay-panel");
      const text = await page.locator("body").innerText();
      assert.match(text, /启用悬浮框[\s\S]*已关闭/);
      assert.match(text, /1% Low[\s\S]*0.1% Low/);
      assert.match(text, /灵动岛[\s\S]*竖排面板/);
      await snapshot("05-osd-settings");
    });
    await check("settings general, appearance, FrameScope and about tabs without saving", async () => {
      await navigate("/settings");
      assert.match(await page.locator("body").innerText(), /通用设置[\s\S]*页面切换动画/);
      await page.getByText("外观", { exact: true }).click();
      assert.match(await page.locator("body").innerText(), /普通液态玻璃/);
      await snapshot("06-settings-appearance");
      await page.getByText("FrameScope", { exact: true }).last().click();
      assert.match(await page.locator("body").innerText(), /仅模拟数据，不启动硬件或游戏采集/);
      assert.ok(await page.getByRole("button", { name: "保存", exact: true }).isVisible());
      await snapshot("07-settings-framescope");
      await page.getByText("关于", { exact: true }).click();
      assert.match(await page.locator("body").innerText(), /FrameScope/);
      await snapshot("08-settings-about");
      await navigate("/");
      assert.ok(await page.getByRole("button", { name: "启动监控", exact: true }).isEnabled());
    });
    report.after = await state();
    await check("targets, reports, config, monitor and overlay unchanged", async () => {
      assert.deepEqual(report.after, report.before);
    });
    await check("no pageerror or console error", async () => {
      assert.deepEqual(report.pageerrors, []);
      assert.deepEqual(report.console.filter(entry => entry.type === "error"), []);
    });
    report.status = "PASS";
  } catch (error) {
    report.status = "FAIL";
    report.error = String(error);
    process.exitCode = 1;
  } finally {
    await writeFile(path.join(output, "report.json"), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({
      status: report.status, assertions: report.assertions,
      consoleErrors: report.console.filter(entry => entry.type === "error").length,
      consoleWarnings: report.console.filter(entry => entry.type === "warning").length,
      pageerrors: report.pageerrors, requestFailures: report.requestFailures,
    }, null, 2));
    await browser.close();
  }
})();
