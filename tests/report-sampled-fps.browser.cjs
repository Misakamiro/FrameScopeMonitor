const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");

const evidence = path.resolve(process.argv[2]);
const results = { scope: "isolated generated reports; no installed app or live capture", checks: [], pageErrors: [] };

async function check(name, fn) {
  try {
    await fn();
    results.checks.push({ name, passed: true });
    console.log(`PASS: ${name}`);
  } catch (error) {
    results.checks.push({ name, passed: false, error: error.stack });
    console.error(`FAIL: ${name}: ${error.message}`);
  }
}

(async () => {
  const audit = JSON.parse(await fs.readFile(path.join(evidence, "audit-result.json"), "utf8"));
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
  });
  results.browser = browser.version();
  try {
    for (const [name, run] of Object.entries(audit.fixtures)) {
      for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
        const context = await browser.newContext({ viewport, acceptDownloads: true });
        const page = await context.newPage();
        const label = `${name}-${viewport.width}`;
        page.on("pageerror", error => results.pageErrors.push({ label, error: error.stack }));
        try {
          await page.goto(pathToFileURL(path.join(run, "charts", "framescope-interactive-report.html")).href);
          await page.locator("#gauges .gauge").first().waitFor();
          const sampled = name.startsWith("sampled");
          await check(`${label}: summary and chart contract`, async () => {
            const state = await page.evaluate(() => ({
              title: document.getElementById("viewTitle").textContent,
              samples: document.getElementById("hwSamples").textContent,
              summary: [...document.querySelectorAll("#summaryRows .row")].map(row =>
                [...row.querySelectorAll("span")].map(span => span.textContent)),
              gaugeValues: [...document.querySelectorAll("#gauges .ring b")].map(node => node.textContent),
              legend: document.getElementById("legend").textContent,
              options: document.getElementById("metricSelect").textContent,
              alert: document.getElementById("captureAlert").textContent,
              stats: fpsFrameStats(),
              series: currentSeries.map(s => ({ name: s.name, data: s.data })),
              counts: DATA.counts,
              missing: finiteOrNull(null),
              zero: finiteOrNull(0),
            }));
            assert.equal(state.missing, null);
            assert.equal(state.zero, 0);
            if (sampled) {
              assert.match(state.title, /ETW FPS/);
              assert.equal(state.counts.frames, 0);
              assert.equal(state.stats.average, name === "sampled" ? 90 : 420);
              assert.equal(state.series.length, 1);
              assert.equal(state.stats.low1, null);
              assert.equal(state.stats.low01, null);
              assert.doesNotMatch(state.legend, /Low/);
              assert.doesNotMatch(state.options, /Low/);
              assert.match(state.alert, /\u7b97\u672f\u5e73\u5747/);
              assert.match(state.alert, /PID/);
              for (const row of state.summary.slice(1)) assert.match(row[1], /\u65e0\u6570\u636e/);
              assert.equal(state.gaugeValues.at(-1), "\u65e0\u6570\u636e");
            } else if (name === "raw") {
              assert.equal(state.counts.frames, 2);
              assert.equal(state.stats.average, 66.67);
              assert.equal(state.stats.low1, 50);
              assert.equal(state.series.length, 3);
            } else {
              assert.deepEqual(state.stats, { low1: null, low01: null, min: null, max: null, average: null });
              assert.equal(state.gaugeValues.at(-1), "\u65e0\u6570\u636e");
            }
            await fs.writeFile(path.join(evidence, `${label}-dom.json`), JSON.stringify(state, null, 2));
          });
          if (sampled) {
            await check(`${label}: sampled labels fit their actual containers`, async () => {
              const layout = await page.evaluate(() => {
                const labels = [];
                const original = ctx.fillText;
                ctx.fillText = function (text, x, y, ...rest) {
                  if (String(text).startsWith("\u91c7\u6837")) {
                    labels.push({ text, width: ctx.measureText(text).width, available: PAD.r - 26, right: x + ctx.measureText(text).width, canvasWidth: chartDims().w });
                  }
                  return original.call(this, text, x, y, ...rest);
                };
                try { draw(); } finally { ctx.fillText = original; }
                const rows = [...document.querySelectorAll("#summaryRows .row")].map(row => {
                  const box = row.getBoundingClientRect();
                  const spans = [...row.querySelectorAll("span")];
                  return { rowRight: box.right, spans: spans.map(span => ({
                    text: span.textContent, left: span.getBoundingClientRect().left, right: span.getBoundingClientRect().right,
                    clientWidth: span.clientWidth, scrollWidth: span.scrollWidth,
                  })) };
                });
                return { labels, rows };
              });
              assert.equal(layout.labels.length, 3);
              for (const text of layout.labels) {
                assert(text.width <= text.available + 0.5, `canvas label overflows: ${JSON.stringify(text)}`);
                assert(text.right <= text.canvasWidth, `canvas label clipped: ${text.text}`);
              }
              for (const row of layout.rows) {
                assert(row.spans[0].right <= row.spans[1].left, "summary label overlaps its value");
                for (const span of row.spans) {
                  assert(span.scrollWidth <= span.clientWidth + 1, `summary text truncated: ${span.text}`);
                  assert(span.right <= row.rowRight + 1, `summary value overflows: ${span.text}`);
                }
              }
              await fs.writeFile(path.join(evidence, `${label}-layout.json`), JSON.stringify(layout, null, 2));
            });
            await check(`${label}: actual canvas and hover`, async () => {
              await page.locator("#chartBox").scrollIntoViewIfNeeded();
              const box = await page.locator("#chartBox").boundingBox();
              const dims = await page.evaluate(() => chartDims());
              await page.mouse.move(box.x + 64 + dims.pw * 0.02, box.y + 36 + dims.ph * 0.5);
              await page.waitForFunction(() => document.getElementById("tooltip").style.opacity === "1");
              const text = await page.locator("#tooltip").innerText();
              assert.match(text, /ETW FPS/);
              assert.doesNotMatch(text, /Low/);
              assert.match(text, /\u975e\u5e27\u6570/);
              assert.doesNotMatch(text, /bucketMs/);
              const bluePixels = await page.evaluate(() => {
                const canvas = document.getElementById("chart");
                const bytes = canvas.getContext("2d").getImageData(0, 0, canvas.width, canvas.height).data;
                let count = 0;
                for (let i = 0; i < bytes.length; i += 4)
                  if (bytes[i] < 70 && bytes[i + 1] > 75 && bytes[i + 2] > 140 && bytes[i + 3] > 0) count++;
                return count;
              });
              assert(bluePixels > 100, `expected visible blue FPS chart, got ${bluePixels} pixels`);
            });
          }
          await page.screenshot({ path: path.join(evidence, `${label}.png`), fullPage: true });
          if (sampled && viewport.width === 1440) {
            await check(`${label}: PNG export`, async () => {
              await page.evaluate(() => {
                window.exportLabels = [];
                const original = CanvasRenderingContext2D.prototype.fillText;
                CanvasRenderingContext2D.prototype.fillText = function (text, x, y, ...rest) {
                  if (this.canvas.width === 3200) window.exportLabels.push({ text, right: x + this.measureText(text).width });
                  return original.call(this, text, x, y, ...rest);
                };
              });
              const downloadPromise = page.waitForEvent("download");
              await page.locator("#exportPng").click();
              const download = await downloadPromise;
              const file = path.join(evidence, `${name}-export.png`);
              await download.saveAs(file);
              const bytes = await fs.readFile(file);
              assert.equal(bytes.readUInt32BE(16), 3200);
              assert.equal(bytes.readUInt32BE(20), 2000);
              const labels = await page.evaluate(() => window.exportLabels);
              assert(labels.some(label => label.text.includes("\u7b97\u672f\u5e73\u5747")), "export retains the sample-mean definition");
              assert(labels.every(label => label.right <= 1558), "export header text fits the image");
            });
          }
        } finally {
          await context.close();
        }
      }
    }
    await check("no browser JavaScript errors", async () => assert.deepEqual(results.pageErrors, []));
  } finally {
    await browser.close();
    await fs.writeFile(path.join(evidence, "browser-results.json"), JSON.stringify(results, null, 2));
  }
  process.exitCode = results.checks.some(check => !check.passed) ? 1 : 0;
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
