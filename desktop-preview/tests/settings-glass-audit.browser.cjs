const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");
const labels = require("../src/locales/zh.json").settings;

async function main() {
  const output = path.resolve(process.argv[2] || "../artifacts/settings-glass/audit");
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true,
    executablePath: process.env.CHROMIUM_EXECUTABLE || chromium.executablePath() });
  const captures = [];
  try {
    for (const scenario of [
      { mode: "normal", width: 1280 }, { mode: "real", width: 1280 },
      { mode: "real", width: 960 }, { mode: "real", width: 1280, fallback: true },
    ]) {
      const context = await browser.newContext({ viewport: { width: scenario.width, height: 900 },
        ...(scenario.fallback ? { userAgent: "Mozilla/5.0 Version/17.0 Safari/605.1.15" } : {}) });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on("pageerror", error => errors.push(error.message));
        await context.addInitScript(mode => {
          localStorage.setItem("i18nextLng", "zh");
          localStorage.setItem("nexbox-theme-mode", "light");
          localStorage.setItem("chakra-ui-color-mode", "light");
          localStorage.setItem("framescope.preview.store.settings.json", JSON.stringify({
            "has-launched": true, "background-mode": "preset", "active-preset-index": 0,
            "carousel-enabled": false, "liquid-glass-enabled": true, "liquid-glass-mode": mode,
            "liquid-glass-blur": 1, "jelly-bounce-enabled": false, nexbox_page_transition: "off",
          }));
          localStorage.setItem("nexbox_page_transition", "off");
        }, scenario.mode);
        await page.goto(`${process.env.PREVIEW_URL || "http://127.0.0.1:1430"}/settings`);
        const menus = page.locator(".jelly-bounce-menu-item");
        await menus.first().waitFor();
        await page.waitForTimeout(1500);
        const name = `${scenario.mode}-${scenario.width}${scenario.fallback ? "-fallback" : ""}`;
        for (const section of ["general", "appearance"]) {
          await menus.getByText(labels[section], { exact: true }).click();
          await page.waitForTimeout(700);
          await page.screenshot({ path: path.join(output, `${name}-${section}.png`) });
          const surfaces = await page.evaluate(() => [...document.querySelectorAll("body *")]
            .filter(element => {
              const rect = element.getBoundingClientRect();
              const style = getComputedStyle(element);
              return rect.width && rect.height && style.visibility !== "hidden" && (
                style.backdropFilter !== "none" || element.id === "main-sidebar" ||
                element.classList.contains("jelly-bounce-menu-item") || element.classList.contains("jelly-bounce-button"));
            }).map(element => {
              const style = getComputedStyle(element);
              const rect = element.getBoundingClientRect();
              return { tag: element.tagName, id: element.id, className: element.className,
                text: element.textContent.slice(0, 80), bg: style.backgroundColor,
                filter: style.backdropFilter, transform: style.transform,
                beforeFilter: getComputedStyle(element, "::before").backdropFilter,
                rect: rect.toJSON() };
            }));
          captures.push({ scenario, section, surfaces, errors });
          const menu = await menus.nth(2).evaluate(element => ({
            filter: getComputedStyle(element).backdropFilter,
            bg: getComputedStyle(element).backgroundColor,
            edges: [...element.children].filter(child => getComputedStyle(child).backdropFilter.includes("url(")).length,
          }));
          assert.equal(menu.filter, "none");
          if (scenario.mode === "real") {
            assert.equal(menu.bg, "rgba(0, 0, 0, 0)");
            assert.equal(menu.edges, scenario.fallback ? 0 : 4);
          }
        }
        assert.deepEqual(errors, []);
      } finally { await context.close(); }
    }
    await fs.writeFile(path.join(output, "surfaces.json"), JSON.stringify({ browser: browser.version(),
      note: "Read-only full-app surface audit. Safari UA only simulates unsupported SVG branch in Edge, not Safari rendering.", captures }, null, 2));
    console.log(`PASS: ${captures.length} full-app captures, including unsupported-SVG branch; no page errors`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
