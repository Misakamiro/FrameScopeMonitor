const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");

async function main() {
  const { createServer } = await import("vite");
  const root = path.resolve(__dirname, "..");
  const output = path.resolve(process.argv[2] || "../artifacts/20260918-delivery/frontend-quality/font-browser");
  await fs.mkdir(output, { recursive: true });
  const server = await createServer({
    root,
    server: { host: "127.0.0.1", port: 0, strictPort: false },
  });
  const results = [];
  let browser;
  try {
    await server.listen();
    const url = server.resolvedUrls.local[0];
    browser = await chromium.launch({
      headless: true,
      ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
    });
    const key = "framescope.preview.store.settings.json";
    const custom = {
      value: "NotoSerifSC-900", label: "User imported font", format: "truetype",
      data: (await fs.readFile(path.join(root, "public/fonts/Cubic.ttf"))).toString("base64"),
    };
    const cases = [
      { name: "retired-built-in-falls-back", saved: "NotoSerifSC-900", expected: "MiSans" },
      { name: "existing-built-in-is-preserved", saved: "Cubic", expected: "Cubic" },
      { name: "fresh-profile-uses-default", expected: "MiSans" },
      { name: "same-name-custom-font-is-preserved", saved: custom.value, expected: custom.value, custom },
    ];
    for (const fixture of cases) {
      const context = await browser.newContext();
      const page = await context.newPage();
      const errors = [];
      const requests = [];
      page.on("pageerror", error => errors.push(String(error)));
      page.on("request", request => requests.push(request.url()));
      const result = { name: fixture.name, status: "RUNNING" };
      results.push(result);
      try {
        await context.addInitScript(({ key, fixture }) => {
          if (localStorage.getItem(key) === null) {
            localStorage.setItem(key, JSON.stringify({
              ...(fixture.saved ? { "app-font-family": fixture.saved } : {}),
              "app-custom-fonts": fixture.custom ? [fixture.custom] : [],
            }));
          }
        }, { key, fixture });
        await page.goto(`${url}tests/frontend-font-fixture.html`);
        await page.waitForFunction(({ key }) => {
          const saved = JSON.parse(localStorage.getItem(key));
          return document.documentElement.style.getPropertyValue("--app-font-family") !== ""
            && Boolean(saved["app-font-family"]);
        }, { key });
        result.font = await page.locator("#current-font").textContent();
        assert.equal(result.font, fixture.expected);
        assert.equal(await page.evaluate(() =>
          document.documentElement.style.getPropertyValue("--app-font-family")), `"${fixture.expected}"`);
        assert.equal(await page.evaluate(key =>
          JSON.parse(localStorage.getItem(key))["app-font-family"], key), fixture.expected);
        await page.reload();
        await page.waitForFunction(expected =>
          document.documentElement.style.getPropertyValue("--app-font-family") === `"${expected}"`,
        fixture.expected);
        assert.equal(await page.locator("#current-font").textContent(), fixture.expected);
        const options = await page.locator("#font-options option").evaluateAll(nodes =>
          nodes.map(node => node.value));
        assert.deepEqual(options, [
          "MiSans", "Sinter-Regular", "AiDianFengYaHei", "Cubic",
          ...(fixture.custom ? [custom.value] : []),
        ]);
        const faces = await page.evaluate(() => [...document.styleSheets]
          .flatMap(sheet => [...sheet.cssRules])
          .filter(rule => rule instanceof CSSFontFaceRule)
          .map(rule => ({ family: rule.style.fontFamily, src: rule.style.getPropertyValue("src") })));
        assert.equal(faces.length, 4);
        assert.ok(faces.every(face => !face.src.includes("NotoSerifSC-900")));
        if (fixture.custom) {
          assert.equal(await page.evaluate(() => [...document.fonts].some(face =>
            face.family === "NotoSerifSC-900" && face.status === "loaded")), true);
          await page.locator("#remove").click();
          await page.waitForFunction(() =>
            document.getElementById("current-font").textContent === "MiSans");
        }
        for (const family of ["MiSans", "Sinter-Regular", "AiDianFengYaHei", "Cubic"]) {
          await page.selectOption("#font-options", family);
          await page.waitForFunction(family =>
            document.documentElement.style.getPropertyValue("--app-font-family") === `"${family}"`, family);
          assert.ok(await page.evaluate(async family =>
            (await document.fonts.load(`16px "${family}"`)).length > 0, family));
        }
        await page.locator("#reset").click();
        await page.waitForFunction(({ key }) =>
          JSON.parse(localStorage.getItem(key))["app-font-family"] === "MiSans", { key });
        assert.ok(!requests.some(url => url.includes("/fonts/NotoSerifSC-900.woff2")));
        assert.deepEqual(errors, []);
        result.status = "PASS";
      } catch (error) {
        result.status = "FAIL";
        result.error = String(error);
      } finally {
        await context.close();
      }
    }
    await fs.writeFile(path.join(output, "results.json"), JSON.stringify({
      evidence: "Isolated Chromium, real FontProvider, preview store and FontFace API; not installed WebView2",
      browser: browser.version(), results,
    }, null, 2));
    console.log(JSON.stringify(results, null, 2));
    if (results.some(result => result.status !== "PASS")) process.exitCode = 1;
  } finally {
    await browser?.close();
    await server.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
