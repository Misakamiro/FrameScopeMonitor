const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs/promises");
const { chromium } = require("playwright");
(async () => {
  const { createServer } = await import("vite");
  const server = await createServer({ root: path.resolve(__dirname, ".."), plugins: [{
    name: "overlay-save-controlled-api", enforce: "pre", transform(code, id) {
      if (!id.endsWith("/contexts/app-startup-context.tsx")) return;
      return code.replaceAll('"@tauri-apps/api/core"', '"/tests/overlay-save-api-fixture.ts"')
        .replaceAll('"@/preview/runtime"', '"/tests/overlay-save-api-fixture.ts"');
    },
  }], server: { host: "127.0.0.1", port: 1449, strictPort: true, watch: null, hmr: false } });
  await server.listen();
  const browser = await chromium.launch({ headless: true, channel: "msedge" });
  const results = [];
  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(3000);
    await page.goto(server.resolvedUrls.local[0] + "tests/overlay-save-fixture.html");
    await page.locator("output").waitFor();
    await page.evaluate(() => { window.saves = [window.saveOpacity(0), window.saveOpacity(64), window.saveOpacity(255)]; });
    await page.waitForTimeout(30);
    async function run(name, task) {
      try { await task(); results.push({ name, status: "PASS" }); }
      catch (error) { results.push({ name, status: "FAIL", error: String(error) }); }
    }
    await run("rapid saves dispatch one native request at a time", async () => {
      assert.deepEqual(await page.evaluate(() => window.overlaySaveProbe.requests.map(r => r.opacity)), [0]);
    });
    await run("latest opacity remains visible while earlier save responds", async () => {
      assert.equal(await page.locator("output").textContent(), "255");
      await page.evaluate(() => window.overlaySaveProbe.requests[0].complete());
      await page.waitForTimeout(30);
      assert.equal(await page.locator("output").textContent(), "255");
    });
    await run("serialized persistence finishes in user selection order", async () => {
      await page.evaluate(() => window.overlaySaveProbe.requests[0].complete());
      await page.waitForTimeout(20);
      await page.evaluate(() => window.overlaySaveProbe.requests[1].complete());
      await page.waitForTimeout(20);
      await page.evaluate(() => window.overlaySaveProbe.requests[2].complete());
      await page.evaluate(() => Promise.all(window.saves));
      assert.deepEqual(await page.evaluate(() => window.overlaySaveProbe.requests.map(r => r.opacity)), [0, 64, 255]);
      assert.equal(await page.locator("output").textContent(), "255");
    });
    console.log(JSON.stringify(results, null, 2));
    if (process.argv[2]) await fs.writeFile(process.argv[2], JSON.stringify(results, null, 2));
    if (results.some(r => r.status === "FAIL")) process.exitCode = 1;
  } finally { await browser.close(); await server.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
