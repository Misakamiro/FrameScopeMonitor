const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");
const translations = require("../src/locales/zh.json");

const labels = translations.settings.appearanceSettings;
const key = "framescope.preview.store.settings.json";
const hooksPattern = /order of Hooks|Rendered (more|fewer) hooks|Invalid hook call/i;

async function main() {
  const { createServer } = await import("vite");
  const output = path.resolve(process.argv[2] || "../artifacts/20260918-delivery/frontend-quality/hooks/browser");
  await fs.mkdir(output, { recursive: true });
  const server = await createServer({
    root: path.resolve(__dirname, ".."),
    server: { host: "127.0.0.1", port: 0, strictPort: false },
  });
  let browser;
  const results = [];
  try {
    await server.listen();
    browser = await chromium.launch({
      headless: true,
      ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
    });
    for (const theme of ["light", "dark"]) {
      for (const scenario of ["picker", "search", "glass", "image", "dynamic"]) {
        const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
        const page = await context.newPage();
        page.setDefaultTimeout(8000);
        const result = { scenario, theme, status: "RUNNING", consoleErrors: [], pageErrors: [], httpErrors: [], styles: {} };
        results.push(result);
        page.on("console", event => {
          if (event.type() === "error") result.consoleErrors.push(event.text());
        });
        page.on("pageerror", error => result.pageErrors.push(String(error)));
        page.on("response", response => {
          if (response.status() >= 400) result.httpErrors.push({ status: response.status(), url: response.url() });
        });
        try {
          await context.addInitScript(({ theme, key }) => {
            localStorage.setItem("i18nextLng", "zh");
            localStorage.setItem("nexbox-theme-mode", theme);
            localStorage.setItem("chakra-ui-color-mode", theme);
            localStorage.setItem(key, JSON.stringify({
              "has-launched": true, "background-mode": "none",
              "liquid-glass-enabled": true, "liquid-glass-mode": "normal",
              "liquid-glass-blur": 1, "carousel-enabled": false, "jelly-bounce-enabled": false,
            }));
          }, { theme, key });
          const mode = ["picker", "search"].includes(scenario) ? scenario : "settings";
          await page.goto(`${server.resolvedUrls.local[0]}tests/frontend-hooks-fixture.html?mode=${mode}`);
          await page.waitForFunction(theme => document.body.classList.contains(`chakra-ui-${theme}`), theme);
          assert.equal(await page.evaluate(() => window.__FRAMESCOPE_BROWSER_PREVIEW__), true);
          const persisted = async (name, value) => page.waitForFunction(({ key, name, value }) =>
            JSON.parse(localStorage.getItem(key))[name] === value, { key, name, value });
          const style = async locator => locator.evaluate(element => {
            const computed = getComputedStyle(element);
            return {
              color: computed.color, backgroundColor: computed.backgroundColor,
              borderColor: computed.borderColor, borderRadius: computed.borderRadius,
              width: computed.width, height: computed.height,
            };
          });
          if (scenario === "picker") {
            const input = page.locator("#picker input");
            await input.waitFor();
            const trigger = input.locator("xpath=preceding-sibling::*[1]");
            result.styles.trigger = await style(trigger);
            for (let round = 0; round < 2; round++) {
              await trigger.click();
              const preview = page.locator(".chakra-portal").getByText("#98DDD0", { exact: true });
              await preview.waitFor();
              result.styles.preview = await style(preview);
              await page.locator("#outside").click();
              await preview.waitFor({ state: "hidden" });
            }
            await input.fill("#336699");
            await trigger.click();
            await page.locator(".chakra-portal").getByText("#336699", { exact: true }).waitFor();
          } else if (scenario === "search") {
            const input = page.locator("#global-search input");
            await input.waitFor();
            result.styles.keyboard = await style(page.locator("#global-search kbd").first());
            for (let round = 0; round < 2; round++) {
              await page.locator("#outside").click();
              await input.click();
              await page.locator("#global-search kbd").first().waitFor({ state: "hidden" });
              await input.fill("设置");
              await page.getByText(translations.search.typeToSearch, { exact: true })
                .waitFor({ state: "hidden" });
              await page.keyboard.press("Escape");
              await page.locator("#global-search kbd").first().waitFor();
              await page.waitForFunction(() => document.querySelector("#global-search input").value === "");
            }
            await page.keyboard.press("Control+k");
            assert.ok(await input.evaluate(element => document.activeElement === element));
            await page.locator("#outside").click();
            await page.locator("#global-search kbd").first().waitFor();
          } else {
            await page.getByText(labels.liquidGlassLabel, { exact: true }).waitFor();
            if (scenario === "glass") {
              const toggle = page.getByText(labels.liquidGlassLabel, { exact: true })
                .locator("xpath=../..").locator("label.chakra-switch");
              for (let round = 0; round < 2; round++) {
                await toggle.click();
                await persisted("liquid-glass-enabled", false);
                await page.getByText(labels.liquidGlassModeLabel, { exact: true }).waitFor({ state: "hidden" });
                await toggle.click();
                await persisted("liquid-glass-enabled", true);
                await page.getByText(labels.liquidGlassModeLabel, { exact: true }).waitFor();
              }
              await page.getByText(labels.liquidGlassModeNormal, { exact: true }).click();
              await page.getByText(labels.liquidGlassModeReal, { exact: true }).click();
              await persisted("liquid-glass-mode", "real");
              await page.getByText(labels.liquidGlassBlurLabel, { exact: true }).waitFor({ state: "hidden" });
              await page.getByText(labels.liquidGlassModeReal, { exact: true }).click();
              await page.getByText(labels.liquidGlassModeNormal, { exact: true }).click();
              await persisted("liquid-glass-mode", "normal");
              result.styles.glassBorder = await style(page.getByText(labels.liquidGlassModeLabel, { exact: true })
                .locator("xpath=../.."));
              result.styles.slider = await style(page.locator(".chakra-slider__track").first());
            } else {
              await page.getByText(labels.customBackgroundLabel, { exact: true })
                .locator("xpath=..").locator("label.chakra-switch").click();
              await persisted("background-mode", "image");
              await page.getByText(labels.presetBackground, { exact: true }).click();
              await persisted("background-mode", "preset");
              for (let round = 0; round < 2; round++) {
                await page.getByText(scenario === "image" ? labels.imageBackground : labels.dynamicBackground, { exact: true }).click();
                await persisted("background-mode", scenario);
                const target = scenario === "image"
                  ? page.locator("#bg-upload-new").locator("xpath=preceding-sibling::*[1]")
                  : page.getByText(labels.uploadVideo, { exact: true }).locator("xpath=..");
                await target.hover();
                result.styles.emptySlot = await style(target);
                await page.getByText(labels.presetBackground, { exact: true }).click();
                await persisted("background-mode", "preset");
              }
              await page.getByText(translations.settings.general, { exact: true }).click();
              await page.getByText(translations.settings.appearance, { exact: true }).click();
              await page.getByText(labels.liquidGlassLabel, { exact: true }).waitFor();
            }
          }
          await page.screenshot({ path: path.join(output, `${theme}-${scenario}.png`), fullPage: true });
          result.hookErrors = result.consoleErrors.filter(message => hooksPattern.test(message));
          assert.deepEqual(result.hookErrors, [], "Real component interaction changed React hook order");
          assert.deepEqual(result.pageErrors, []);
          assert.deepEqual(result.consoleErrors, []);
          assert.deepEqual(result.httpErrors, []);
          result.status = "PASS";
        } catch (error) {
          result.hookErrors = result.consoleErrors.filter(message => hooksPattern.test(message));
          result.status = "FAIL";
          result.error = String(error);
        } finally {
          await context.close();
        }
      }
    }
    await fs.writeFile(path.join(output, "results.json"), JSON.stringify({
      evidence: "Real production components and providers, isolated Edge/Chromium preview; no native process or CDP attachment",
      browser: browser.version(), results,
    }, null, 2));
    console.log(JSON.stringify(results.map(({ scenario, theme, status, error, hookErrors }) => ({
      scenario, theme, status, error, hookErrors,
    })), null, 2));
    if (results.some(result => result.status !== "PASS")) process.exitCode = 1;
  } finally {
    await browser?.close();
    await server.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
