const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");

(async () => {
  const { createServer } = await import("vite");
  const server = await createServer({ root: path.resolve(__dirname, ".."), server: { host: "127.0.0.1", port: 1447, strictPort: true, watch: null, hmr: false } });
  await server.listen();
  const browser = await chromium.launch({ headless: true, channel: "msedge" });
  const results = [];
  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(3000);
    const base = server.resolvedUrls.local[0];
    async function run(name, task) {
      try { await task(); results.push({ name, status: "PASS" }); }
      catch (error) { results.push({ name, status: "FAIL", error: String(error) }); }
    }
    await page.goto(base + "tests/page-transition-fixture.html");
    await page.locator("#mode").waitFor();
    await run("setting event beats delayed store hydration and updates local cache", async () => {
      await page.evaluate(() => window.dispatchEvent(new CustomEvent("page-transition-setting-changed", { detail: "slide" })));
      await page.waitForFunction(() => document.querySelector("#mode").textContent === "slide");
      await page.evaluate(() => window.resolveStore("fade"));
      await page.waitForTimeout(50);
      assert.equal(await page.locator("#mode").textContent(), "slide");
      assert.equal(await page.evaluate(() => localStorage.getItem("nexbox_page_transition")), "slide");
    });
    await page.evaluate(() => {
      localStorage.setItem("nexbox_page_transition", "fade");
      window.dispatchEvent(new CustomEvent("page-transition-setting-changed", { detail: "fade" }));
      window.navigate("/b");
    });
    await page.waitForFunction(() => window.transitions.length === 1);
    await run("pointer movement in navigation preserves transition", async () => {
      await page.evaluate(() => document.dispatchEvent(new PointerEvent("pointermove", { clientX: 30, clientY: 30 })));
      assert.equal(await page.evaluate(() => window.transitions[0].skipped), false);
    });
    await run("old callback and completion cannot replace rapid navigation state", async () => {
      await page.evaluate(() => window.navigate("/c"));
      await page.waitForFunction(() => window.transitions.length === 2);
      await page.evaluate(() => { window.transitions[0].commit(); window.transitions[0].finish(); });
      assert.equal(await page.evaluate(() => document.documentElement.dataset.pageTransition), "fade");
      await page.evaluate(() => window.transitions[1].commit());
      await page.waitForFunction(() => document.querySelector("#path").textContent === "/c");
      await page.evaluate(() => window.transitions[1].finish());
    });
    await run("slide moves the page without a backdrop-isolating snapshot", async () => {
      const count = await page.evaluate(() => window.transitions.length);
      await page.evaluate(() => {
        window.dispatchEvent(new CustomEvent("page-transition-setting-changed", { detail: "slide" }));
        window.navigate("/slide");
      });
      await page.waitForFunction(() => document.querySelector("#path").textContent === "/slide");
      assert.equal(await page.evaluate(() => window.transitions.length), count);
    });
    await run("vertical document stays transparent under light and dark themes", async () => {
      await page.goto(base + "vertical-overlay");
      await page.waitForTimeout(1500);
      for (const mode of ["light", "dark"]) {
        await page.evaluate(mode => { localStorage.setItem("chakra-ui-color-mode", mode); location.reload(); }, mode);
        await page.waitForTimeout(800);
        const colors = await page.evaluate(() => [document.documentElement, document.body, document.getElementById("root")].map(el => getComputedStyle(el).backgroundColor));
        assert.deepEqual(colors, ["rgba(0, 0, 0, 0)", "rgba(0, 0, 0, 0)", "rgba(0, 0, 0, 0)"]);
      }
    });
    await run("off commits without starting a snapshot", async () => {
      await page.goto(base + "tests/page-transition-fixture.html");
      await page.locator("#mode").waitFor();
      await page.evaluate(() => {
        window.dispatchEvent(new CustomEvent("page-transition-setting-changed", { detail: "off" }));
        window.navigate("/off");
      });
      await page.waitForFunction(() => document.querySelector("#path").textContent === "/off");
      assert.equal(await page.evaluate(() => window.transitions.length), 0);
    });
    for (const mode of ["fade", "off"]) {
      await run(`real ${mode} snapshot keyframes from persisted settings`, async () => {
        await page.evaluate(mode => {
          localStorage.setItem("nexbox_page_transition", mode);
          localStorage.setItem("framescope.preview.store.settings.json", JSON.stringify({
            "has-launched": true, "background-mode": "preset", "active-preset-index": 0,
            "carousel-enabled": false, "liquid-glass-enabled": true, "liquid-glass-blur": 12,
            "liquid-glass-mode": "normal", "jelly-bounce-enabled": false, nexbox_page_transition: mode,
          }));
        }, mode);
        await page.goto(base + "targets");
        await page.locator('#main-sidebar a[href="/reports"]').waitFor();
        await page.waitForTimeout(500);
        assert.equal(await page.evaluate(() => document.documentElement.dataset.overlayWindow), undefined);
        await page.evaluate(() => {
          window.records = [];
          const start = document.startViewTransition.bind(document);
          document.startViewTransition = callback => {
            const transition = start(callback);
            const record = { mode: document.documentElement.dataset.pageTransition };
            window.records.push(record);
            transition.ready.then(() => {
              record.animations = document.getAnimations().filter(a => a.effect?.pseudoElement?.includes("view-transition"))
                .map(a => ({ pseudo: a.effect.pseudoElement, timing: a.effect.getTiming(), frames: a.effect.getKeyframes() }));
              record.liveOpacity = [...document.querySelectorAll('[style*="width: 100%"]')].map(e => getComputedStyle(e).opacity);
            }, () => { record.skipped = true; });
            return transition;
          };
        });
        for (let i = 0; i < 4; i++) {
          await page.locator(`#main-sidebar a[href="${i % 2 ? "/targets" : "/reports"}"]`).click();
          await page.waitForTimeout(280);
        }
        const records = await page.evaluate(() => window.records);
        if (mode === "off") assert.equal(records.length, 0);
        else {
          assert.equal(records.length, 4);
          for (const record of records) {
            assert.equal(record.mode, mode);
            assert.equal(record.skipped, undefined);
            assert.ok(record.animations.every(animation => animation.timing.duration === 180));
            const incoming = record.animations.find(a => a.pseudo === "::view-transition-new(root)");
            assert.ok(incoming, JSON.stringify(record));
            assert.equal(incoming.timing.duration, 180);
            assert.equal(incoming.frames[0].opacity, "0");
            assert.ok(incoming.frames.every(frame => frame.transform === undefined));
            assert.ok(record.liveOpacity.every(opacity => opacity === "1"));
          }
        }
        results.push({ name: `${mode} real snapshot evidence`, records });
      });
    }
    await run("real settings controls persist each mode across page reload", async () => {
      await page.goto(base + "settings");
      for (const [mode, label] of [["slide", "滑动"], ["fade", "淡化"], ["off", "关闭"]]) {
        const button = page.locator("button[aria-pressed]").filter({ hasText: new RegExp(`^${label}$`) });
        await button.click();
        await page.waitForFunction(mode => localStorage.getItem("nexbox_page_transition") === mode &&
          JSON.parse(localStorage.getItem("framescope.preview.store.settings.json")).nexbox_page_transition === mode, mode);
        assert.equal(await button.getAttribute("aria-pressed"), "true");
        await page.reload();
        await page.waitForFunction(label => [...document.querySelectorAll("button[aria-pressed=true]")].some(b => b.textContent.trim() === label), label);
      }
    });
    console.log(JSON.stringify(results.filter(r => r.status), null, 2));
    const output = process.argv[2];
    if (output) { await fs.mkdir(path.dirname(output), { recursive: true }); await fs.writeFile(output, JSON.stringify(results, null, 2)); }
    if (results.some(r => r.status === "FAIL")) process.exitCode = 1;
  } finally { await browser.close(); await server.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
