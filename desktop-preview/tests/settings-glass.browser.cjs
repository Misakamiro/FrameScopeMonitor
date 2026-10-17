const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");
const sharp = require("sharp");
const translations = require("../src/locales/zh.json");

const url = process.env.PREVIEW_URL || "http://127.0.0.1:1430";
const output = path.resolve(process.argv[2] || "../artifacts/settings-glass/green");
const key = "framescope.preview.store.settings.json";

async function styles(locator) {
  return locator.evaluate(element => {
    const css = getComputedStyle(element);
    const underlay = getComputedStyle(element, "::before");
    return {
      bg: css.backgroundColor, filter: css.backdropFilter, transform: css.transform,
      backface: css.backfaceVisibility, overflow: css.overflow, zIndex: css.zIndex,
      radius: css.borderRadius, text: css.color, beforeFilter: underlay.backdropFilter,
      beforeZ: underlay.zIndex,
      edges: [...element.children].filter(child => getComputedStyle(child).backdropFilter.includes("url("))
        .map(child => ({ width: child.getBoundingClientRect().width,
          height: child.getBoundingClientRect().height, pointer: getComputedStyle(child).pointerEvents })),
    };
  });
}

async function main() {
  assert.match(url, /^http:\/\/127\.0\.0\.1:\d+\/?$/);
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true,
    executablePath: process.env.CHROMIUM_EXECUTABLE || chromium.executablePath() });
  const results = [];
  try {
    for (const theme of ["light", "dark"]) {
      for (const dpr of [1, 1.5, 2]) {
        const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: dpr });
        const page = await context.newPage();
        const result = { theme, dpr, checks: [], snapshots: [], errors: [] };
        results.push(result);
        const check = (name, fn) => {
          try { fn(); result.checks.push({ name, status: "PASS" }); }
          catch (error) { result.checks.push({ name, status: "FAIL", error: error.message }); }
        };
        page.on("pageerror", error => result.errors.push(error.message));
        page.on("console", event => { if (event.type() === "error") result.errors.push(event.text()); });
        try {
          await context.addInitScript(({ theme, key }) => {
            localStorage.setItem("i18nextLng", "zh");
            localStorage.setItem("nexbox-theme-mode", theme);
            localStorage.setItem("chakra-ui-color-mode", theme);
            localStorage.setItem(key, JSON.stringify({
              "has-launched": true, "background-mode": "none", "liquid-glass-enabled": true,
              "liquid-glass-mode": "normal", "liquid-glass-blur": 1,
              "jelly-bounce-enabled": false, nexbox_page_transition: "fade",
            }));
          }, { theme, key });
          await page.goto(`${url}/tests/settings-glass-fixture.html`);
          const menus = page.locator(".jelly-bounce-menu-item");
          await menus.first().waitFor();
          const segment = page.locator('button[aria-pressed]').first().locator("..");
          let activeBg;
          for (const mode of ["normal", "real", "off", "normal", "real"]) {
            await page.getByRole("button", { name: `Fixture ${mode}`, exact: true }).click();
            await page.waitForFunction(mode => document.querySelector("#fixture-state").textContent === `${mode}:1`, mode);
            await page.mouse.move(5, 5);
            await page.waitForTimeout(550);
            const inactive = await styles(menus.nth(1));
            const active = await styles(menus.first());
            const segmented = await styles(segment);
            result.snapshots.push({ mode, inactive, active, segmented });
            activeBg ??= active.bg;
            check(`${mode}: active identity`, () => assert.equal(active.bg, activeBg));
            check(`${mode}: active glass mode`, () => {
              assert.equal(active.filter, "none");
              assert.equal(active.beforeFilter, mode === "normal" ? "blur(1px)" : "none");
              assert.equal(active.edges.length, mode === "real" ? 4 : 0);
            });
            check(`${mode}: root clipping`, () => {
              assert.equal(inactive.filter, "none");
              assert.equal(inactive.transform, "none");
              assert.equal(inactive.backface, "visible");
              assert.equal(inactive.overflow, "hidden");
              assert.equal(inactive.zIndex, "0");
              assert.equal(inactive.beforeZ, "-1");
            });
            check(`${mode}: surface mode`, () => {
              assert.equal(inactive.beforeFilter, mode === "normal" ? "blur(1px)" : "none");
              assert.equal(inactive.bg, mode === "real" ? "rgba(0, 0, 0, 0)" : mode === "off"
                ? theme === "light" ? "rgb(255, 255, 255)" : "rgb(26, 26, 26)"
                : theme === "light" ? "rgba(255, 255, 255, 0.25)" : "rgba(0, 0, 0, 0.25)");
            });
            check(`${mode}: edge refraction`, () => {
              assert.equal(inactive.edges.length, mode === "real" ? 4 : 0);
              for (const edge of inactive.edges) {
                assert.ok(edge.width === 10 || edge.height === 10);
                assert.equal(edge.pointer, "none");
              }
            });
            check(`${mode}: segmented mode`, () => {
              assert.equal(segmented.filter, "none");
              assert.equal(segmented.beforeFilter, mode === "normal" ? "blur(1px) saturate(1.6)" : mode === "off" ? "blur(14px)" : "none");
              if (mode === "real") assert.equal(segmented.bg, "rgba(0, 0, 0, 0)");
            });
            if (mode === "real") {
              const inactiveText = await page.locator('button[aria-pressed="false"]').first()
                .evaluate(element => getComputedStyle(element).color);
              check("real: inactive segment uses readable label color", () => assert.equal(inactiveText,
                theme === "light" ? "rgb(45, 55, 72)" : "rgb(255, 255, 255)"));
            }
            const name = `${theme}-dpr${dpr}-${mode}`;
            const image = await page.screenshot({ path: path.join(output, `${name}.png`) });
            const rect = await menus.nth(1).boundingBox();
            const size = Math.ceil(22 * dpr);
            await sharp(image).extract({ left: Math.floor((rect.x - 2) * dpr),
              top: Math.floor((rect.y - 2) * dpr), width: size, height: size })
              .resize(size * 8, size * 8, { kernel: "nearest" }).toFile(path.join(output, `${name}-corner-8x.png`));
            await menus.nth(1).hover();
            await page.waitForTimeout(550);
            const hover = await styles(menus.nth(1));
            check(`${mode}: hover readable`, () => {
              assert.notEqual(hover.text, hover.bg);
              if (mode === "real") assert.equal(hover.bg, "rgba(0, 0, 0, 0)");
              else assert.notEqual(hover.bg, inactive.bg);
            });
          }
          await page.getByRole("button", { name: "Fixture normal", exact: true }).click();
          await page.getByRole("button", { name: "Fixture blur12", exact: true }).click();
          await page.waitForTimeout(550);
          const blurred = await styles(menus.nth(1));
          check("blur12 underlay", () => assert.equal(blurred.beforeFilter, "blur(12px)"));
          for (const name of ["pageTransitionSlide", "pageTransitionOff", "pageTransitionFade"]) {
            const button = page.getByRole("button", { name: translations.settings.generalSettings[name], exact: true });
            await button.click();
            assert.equal(await button.getAttribute("aria-pressed"), "true");
            result.checks.push({ name: `${name}: selected`, status: "PASS" });
          }
          await menus.nth(1).click();
          const labels = translations.settings.appearanceSettings;
          await page.getByText(labels.liquidGlassModeNormal, { exact: true }).click();
          await page.getByText(labels.liquidGlassModeReal, { exact: true }).click();
          await page.waitForFunction(() => document.querySelector("#fixture-state").textContent === "real:12");
          await menus.first().click();
          await page.waitForTimeout(550);
          const restored = await styles(segment);
          check("return segment transparent", () => assert.equal(restored.bg, "rgba(0, 0, 0, 0)"));
          check("no browser errors", () => assert.deepEqual(result.errors, []));
        } catch (error) {
          result.checks.push({ name: "interaction", status: "FAIL", error: String(error) });
        } finally { await context.close(); }
      }
    }
    await fs.writeFile(path.join(output, "results.json"), JSON.stringify({ browser: browser.version(),
      scope: "Isolated browser fixture, production SettingsPage/providers; not installed WebView2 acceptance", results }, null, 2));
    for (const result of results) console.log(JSON.stringify({ theme: result.theme, dpr: result.dpr,
      passed: result.checks.filter(c => c.status === "PASS").length,
      failures: result.checks.filter(c => c.status === "FAIL") }));
    if (results.some(r => r.checks.some(c => c.status === "FAIL"))) process.exitCode = 1;
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
