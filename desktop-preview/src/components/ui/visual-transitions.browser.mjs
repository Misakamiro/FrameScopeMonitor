import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright");
const url = process.env.PREVIEW_URL ?? "http://127.0.0.1:1431";
const output = path.resolve(process.env.VISUAL_OUTPUT ?? "../artifacts/20260918-repair/visual");
const mode = process.env.VISUAL_MODE ?? "fade";

test(`${mode} keeps incoming pages at their settled position throughout 20 switches`, async () => {
  assert.match(url, /^http:\/\/127\.0\.0\.1:\d+\/?$/);
  assert.ok(["fade", "slide", "off"].includes(mode));
  await mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
  });
  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.addInitScript(mode => {
      localStorage.setItem("framescope.preview.store.settings.json", JSON.stringify({
        "has-launched": true,
        "background-mode": "preset",
        "active-preset-index": 0,
        "carousel-enabled": false,
        "liquid-glass-enabled": true,
        "liquid-glass-blur": 1,
        "liquid-glass-mode": "normal",
        "jelly-bounce-enabled": false,
        nexbox_page_transition: mode,
      }));
      localStorage.setItem("nexbox_page_transition", mode);
    }, mode);
    await page.goto(`${url}/targets`);
    await page.locator('#main-sidebar a[href="/reports"]').waitFor();
    await page.waitForTimeout(1000);
    const cdp = await context.newCDPSession(page);
    const frames = [];
    const writes = [];
    cdp.on("Page.screencastFrame", ({ data, sessionId, metadata }) => {
      const file = `frame-${String(frames.length).padStart(4, "0")}.jpg`;
      frames.push({ file, timestamp: metadata.timestamp });
      writes.push(writeFile(path.join(output, file), Buffer.from(data, "base64")));
      void cdp.send("Page.screencastFrameAck", { sessionId });
    });
    await cdp.send("Page.startScreencast", { format: "jpeg", quality: 85, everyNthFrame: 1 });
    const results = [];
    for (let index = 0; index < 20; index++) {
      const destination = index % 2 === 0 ? "/reports" : "/targets";
      await page.evaluate(mode => {
        window.__visualSamples = [];
        const sample = time => {
          const wrappers = mode === "fade"
            ? [...document.querySelectorAll("h2")].map(heading => {
                let wrapper = heading;
                while (wrapper && !(wrapper.style.width === "100%" && wrapper.style.height === "100%")) {
                  wrapper = wrapper.parentElement;
                }
                return wrapper;
              }).filter(Boolean)
            : [...document.querySelectorAll(".page-animating")];
          window.__visualSamples.push({
            time,
            pages: wrappers.map(element => ({
              title: element.querySelector("h2")?.textContent ?? element.innerText.split("\n")[0],
              top: element.getBoundingClientRect().top,
              opacity: Number(getComputedStyle(element).opacity),
            })),
          });
          window.__visualRaf = requestAnimationFrame(sample);
        };
        window.__visualRaf = requestAnimationFrame(sample);
      }, mode);
      await page.locator(`#main-sidebar a[href="${destination}"]`).click();
      await page.waitForTimeout(450);
      assert.equal(await page.locator("h2").filter({
        hasText: destination === "/reports" ? /^报告$/ : /^监控目标$/,
      }).count(), 1);
      results.push(await page.evaluate(destination => {
        cancelAnimationFrame(window.__visualRaf);
        let wrapper = [...document.querySelectorAll("h2")]
          .find(element => element.textContent === (destination === "/reports" ? "报告" : "监控目标"));
        while (wrapper && !(wrapper.style.width === "100%" && wrapper.style.height === "100%")) {
          wrapper = wrapper.parentElement;
        }
        return {
          destination,
          samples: window.__visualSamples,
          settledTop: wrapper?.getBoundingClientRect().top,
        };
      }, destination));
    }
    await cdp.send("Page.stopScreencast");
    await Promise.all(writes);
    await writeFile(path.join(output, "sequence.json"), JSON.stringify({
      browser: browser.version(),
      mode,
      timeOrigin: await page.evaluate(() => performance.timeOrigin),
      environment: "isolated Chromium preview; not installed WebView2",
      capture: "CDP screencast with timestamps; not guaranteed lossless 60fps",
      frames, results, errors,
    }, null, 2));
    assert.equal(errors.length, 0, errors.join("\n"));
    for (const result of results) {
      const title = result.destination === "/reports" ? "报告" : "监控目标";
      const incoming = result.samples.flatMap(sample => sample.pages.filter(p => p.title === title));
      assert.ok(Number.isFinite(result.settledTop), `missing settled page: ${result.destination}`);
      if (mode === "off") {
        assert.equal(incoming.length, 0, "disabled transitions must not animate");
        continue;
      }
      assert.ok(incoming.length >= 2, `missing animation samples: ${result.destination}`);
      const positions = incoming.map(sample => sample.top);
      assert.ok(positions.every(top => Math.abs(top - result.settledTop) <= 1),
        `${result.destination} incoming page jumped: ${Math.min(...positions)}..${Math.max(...positions)}`);
    }
  } finally {
    await browser.close();
  }
});
