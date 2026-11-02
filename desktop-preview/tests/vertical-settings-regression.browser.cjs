const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs/promises");
const { chromium } = require(require.resolve("playwright",{paths:[process.env.FRAMESCOPE_BROWSER_DEPS || process.cwd()]}));
(async () => {
  process.chdir(path.resolve(__dirname,".."));
  const { createServer } = await import(require("node:url").pathToFileURL(require.resolve("vite",{paths:[process.cwd()]})).href);
  const server = await createServer({ root: path.resolve(__dirname, ".."), plugins: [{
    name: "vertical-settings-controlled-api", enforce: "pre",
    transform(code, id) {
      if (!id.endsWith("/pages/VerticalOverlayPage.tsx")) return;
      return code.replaceAll('"@tauri-apps/api/core"', '"/tests/vertical-settings-api-fixture.ts"')
        .replaceAll('"@tauri-apps/api/event"', '"/tests/vertical-settings-api-fixture.ts"')
        .replaceAll('"@tauri-apps/api/window"', '"/tests/vertical-settings-api-fixture.ts"');
    },
  }], server: { host: "127.0.0.1", port: 1448, strictPort: true, watch: null, hmr: false } });
  await server.listen();
  const browser = await chromium.launch({ headless: true, channel: "msedge" });
  const results = [];
  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(3000);
    const url = server.resolvedUrls.local[0] + "tests/vertical-settings-fixture.html";
    async function run(name, task) {
      try { await task(); results.push({ name, status: "PASS" }); }
      catch (error) { results.push({ name, status: "FAIL", error: String(error) }); }
    }
    await page.goto(url);
    await page.waitForFunction(() => window.verticalProbe?.order.includes("listen-start"));
    await run("register settings listener before initial request and ready", async () => {
      const order = await page.evaluate(() => window.verticalProbe.order);
      assert.equal(order.includes("get_overlay_current_settings"), false);
      assert.equal(order.includes("vertical_overlay_ready"), false);
    });
    await page.evaluate(() => window.verticalProbe.register());
    await page.waitForFunction(() => window.verticalProbe.order.includes("get_overlay_current_settings"));
    await page.evaluate(() => window.verticalProbe.update(0));
    await page.waitForTimeout(40);
    await run("new event displays immediately and rejects late initial opacity", async () => {
      const panel = page.locator("#root > div");
      assert.equal(await panel.evaluate(el => getComputedStyle(el).visibility), "visible");
      assert.equal(await panel.evaluate(el => getComputedStyle(el).backgroundColor), "rgba(17, 17, 17, 0)");
      await page.evaluate(() => window.verticalProbe.initial(200));
      await page.waitForFunction(() => window.verticalProbe.order.includes("vertical_overlay_ready"));
      const order=await page.evaluate(() => window.verticalProbe.order);
      assert.ok(order.indexOf("resize_vertical_overlay")<order.indexOf("vertical_overlay_ready"));
      await page.waitForTimeout(40);
      assert.equal(await panel.evaluate(el => getComputedStyle(el).backgroundColor), "rgba(17, 17, 17, 0)");
    });
    await run("unmount before late registration immediately releases listener", async () => {
      await page.goto(url);
      await page.waitForFunction(() => window.verticalProbe?.order.includes("listen-start"));
      await page.evaluate(() => { window.unmountOverlay(); window.verticalProbe.register(); });
      await page.waitForTimeout(40);
      assert.equal(await page.evaluate(() => window.verticalProbe.unlistens), 1);
      assert.equal(await page.evaluate(() => window.verticalProbe.order.includes("get_overlay_current_settings")), false);
    });
    await run("click-through failure is reported instead of acknowledging a ready window", async () => {
      await page.goto(url + "?input-failure");
      await page.waitForFunction(() => window.verticalProbe?.order.includes("listen-start"));
      await page.evaluate(() => window.verticalProbe.register());
      await page.waitForTimeout(100);
      await page.evaluate(() => window.verticalProbe.initial(100));
      await page.waitForTimeout(150);
      assert.ok(await page.evaluate(() => window.verticalProbe.failures.includes("input")), "input failure was silently discarded");
      assert.equal(await page.evaluate(() => window.verticalProbe.order.includes("resize_vertical_overlay")), false);
    });
    await run("layout and ready wait for the native input acknowledgment", async () => {
      await page.goto(url + "?input-pending");
      await page.waitForFunction(() => window.verticalProbe?.order.includes("listen-start"));
      await page.evaluate(() => window.verticalProbe.register());
      await page.waitForFunction(() => window.verticalProbe.order.includes("get_overlay_current_settings"));
      await page.evaluate(() => window.verticalProbe.initial(100));
      await page.waitForTimeout(100);
      assert.equal(await page.evaluate(() => window.verticalProbe.order.includes("vertical_overlay_ready")), false);
      await page.evaluate(() => window.verticalProbe.inputReady());
      await page.waitForFunction(() => window.verticalProbe.order.includes("vertical_overlay_ready"));
      assert.equal(await page.evaluate(() => window.verticalProbe.failures.length), 0);
    });
    console.log(JSON.stringify(results, null, 2));
    if (process.argv[2]) await fs.writeFile(process.argv[2], JSON.stringify(results, null, 2));
    if (results.some(r => r.status === "FAIL")) process.exitCode = 1;
  } finally { await browser.close(); await server.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
