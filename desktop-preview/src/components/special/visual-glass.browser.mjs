import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import test from "node:test";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright");
const sharp = require("sharp");
const url = process.env.PREVIEW_URL ?? "http://127.0.0.1:1431";
const output = path.resolve(process.env.GLASS_OUTPUT ?? "../artifacts/20260918-repair/glass");

test("glass geometry and corner captures (not an anti-aliasing acceptance gate)", async () => {
  assert.match(url, /^http:\/\/127\.0\.0\.1:\d+\/?$/);
  await mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
  });
  const captures = [];
  try {
    for (const scenario of [
      { mode: "normal", blur: 1, dpr: 1, theme: "light" },
      { mode: "normal", blur: 1, dpr: 1.5, theme: "light" },
      { mode: "normal", blur: 1, dpr: 2, theme: "light" },
      { mode: "normal", blur: 12, dpr: 1, theme: "light" },
      { mode: "normal", blur: 1, dpr: 1, theme: "dark" },
      { mode: "real", blur: 1, dpr: 1, theme: "light" },
    ]) {
      const context = await browser.newContext({
        viewport: { width: 1280, height: 900 }, deviceScaleFactor: scenario.dpr,
      });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        await page.addInitScript(scenario => {
          localStorage.setItem("chakra-ui-color-mode", scenario.theme);
          localStorage.setItem("framescope.preview.store.settings.json", JSON.stringify({
            "has-launched": true, "background-mode": "preset", "active-preset-index": 0,
            "carousel-enabled": false, "liquid-glass-enabled": true,
            "liquid-glass-blur": scenario.blur, "liquid-glass-mode": scenario.mode,
            "jelly-bounce-enabled": false, nexbox_page_transition: "off",
          }));
          localStorage.setItem("nexbox_page_transition", "off");
        }, scenario);
        await page.goto(url);
        await page.locator(".jelly-bounce-card").first().waitFor();
        await page.waitForTimeout(1200);
        const image = await page.screenshot();
        const name = `${scenario.mode}-blur${scenario.blur}-dpr${scenario.dpr}-${scenario.theme}`;
        await writeFile(path.join(output, `${name}.png`), image);
        const geometry = [];
        for (const [label, selector] of [["card", ".jelly-bounce-card"], ["sidebar", "#main-sidebar"]]) {
          const surface = await page.locator(selector).first().evaluate(element => {
            const rect = element.getBoundingClientRect();
            return {
              rect: rect.toJSON(),
              radius: getComputedStyle(element).borderTopLeftRadius,
              underlayFilter: getComputedStyle(element, "::before").backdropFilter,
            };
          });
          assert.ok(parseFloat(surface.radius) > 0);
          assert.ok(surface.rect.width > 0 && surface.rect.height > 0);
          const left = Math.floor((surface.rect.left - 3) * scenario.dpr);
          const top = Math.floor((surface.rect.top - 3) * scenario.dpr);
          const size = Math.ceil(26 * scenario.dpr);
          const corner = `${name}-${label}-8x.png`;
          await sharp(image).extract({ left, top, width: size, height: size })
            .resize(size * 8, size * 8, { kernel: "nearest" })
            .toFile(path.join(output, corner));
          geometry.push({ label, ...surface, corner });
        }
        assert.equal(errors.length, 0, errors.join("\n"));
        if (scenario.mode === "normal") {
          assert.equal(geometry[0].underlayFilter, `blur(${scenario.blur}px) saturate(1.3)`);
        }
        captures.push({ scenario, geometry, errors });
      } finally {
        await context.close();
      }
    }
    await writeFile(path.join(output, "captures.json"), JSON.stringify({
      browser: browser.version(),
      environment: "isolated Chromium preview; not installed WebView2",
      limitation: "geometry assertions and 8x nearest-neighbor captures do not certify smooth corners",
      captures,
    }, null, 2));
  } finally {
    await browser.close();
  }
});
