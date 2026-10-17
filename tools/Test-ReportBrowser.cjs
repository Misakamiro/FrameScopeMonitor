const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");

async function main() {
  assert.ok(process.argv[2], "Pass the fresh FrameScopeSampledFpsAudit audit-result.json");
  const manifest = JSON.parse(await fs.readFile(process.argv[2], "utf8"));
  assert.equal(manifest.failures, 0);
  const output = path.resolve(process.argv[3] || "artifacts/20260918-repair/report-browser");
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
  });
  const results = [];
  try {
    for (const name of ["sampled", "raw", "sampled-gaps", "empty"]) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
      const errors = [];
      page.on("pageerror", error => errors.push(error.message));
      const html = path.join(manifest.fixtures[name], "charts", "framescope-interactive-report.html");
      await page.goto(pathToFileURL(html).href);
      await page.locator("#chart").waitFor();
      await page.waitForTimeout(300);
      const result = await page.evaluate(() => {
        const canvas = document.querySelector("#chart");
        const pixels = canvas.getContext("2d").getImageData(0, 0, canvas.width, canvas.height).data;
        let nonblank = 0;
        for (let i = 3; i < pixels.length; i += 4) if (pixels[i] !== 0) nonblank++;
        const data = window.FRAMESCOPE_DATA;
        return {
          title: document.querySelector("#viewTitle").textContent,
          legend: document.querySelector("#legend").textContent,
          captureAlert: document.querySelector("#captureAlert").textContent,
          counts: data.counts, frameStats: data.frameStats, source: data.fps.source,
          sampleStats: data.fpsSampleStats,
          metricOptions: [...document.querySelector("#metricSelect").options].map(option => option.text),
          nonblank, body: document.body.innerText,
        };
      });
      assert.deepEqual(errors, [], `${name}: script errors`);
      assert.ok(result.nonblank > 100, `${name}: blank canvas`);
      assert.ok(!result.body.includes("NaN"), `${name}: NaN rendered`);
      if (name.startsWith("sampled")) {
        assert.equal(result.source, "etw-fps-sampled");
        assert.equal(result.counts.frames, 0);
        assert.equal(result.frameStats.low1, null);
        assert.equal(result.frameStats.low01, null);
        assert.ok(result.title.includes("采样"));
        assert.ok(result.captureAlert.includes("并非逐帧报告"));
        assert.ok(result.legend.includes("算术平均"));
        assert.ok(!result.legend.includes("Low"));
        assert.equal(result.metricOptions.length, 1);
      } else if (name === "raw") {
        assert.equal(result.source, "presentmon");
        assert.ok(result.legend.includes("Low"));
        assert.equal(result.counts.frames, 2);
      }
      await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: true });
      delete result.body;
      results.push({ name, status: "PASS", errors, ...result });
      await page.close();
    }
    await fs.writeFile(path.join(output, "results.json"), JSON.stringify(results, null, 2));
    console.log(JSON.stringify({ status: "PASS", reports: results.length, output }));
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
