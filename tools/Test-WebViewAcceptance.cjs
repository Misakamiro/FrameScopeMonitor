const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");
const sharp = require("sharp");

async function main() {
  const endpoint = process.env.CDP_URL || "http://127.0.0.1:9333";
  const out = path.resolve(process.argv[2] || "artifacts/20260918-delivery/webview");
  await fs.mkdir(out, { recursive: true });
  const browser = await chromium.connectOverCDP(endpoint);
  const page = browser.contexts()[0].pages().find(p => !p.url().includes("vertical-overlay"));
  assert.ok(page);
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.evaluate(() => {
    const key = "framescope.preview.store.settings.json";
    const settings = JSON.parse(localStorage.getItem(key) || "{}");
    Object.assign(settings, {
      "has-launched": true, "background-mode": "preset", "active-preset-index": 0,
      "carousel-enabled": false, "liquid-glass-enabled": true, "liquid-glass-blur": 1,
      "liquid-glass-mode": "normal", "jelly-bounce-enabled": false, nexbox_page_transition: "fade",
    });
    localStorage.setItem(key, JSON.stringify(settings));
    localStorage.setItem("nexbox_page_transition", "fade");
  });
  await page.reload();
  await page.waitForFunction(() => [...document.images].some(i =>
    i.getAttribute("src") === "/backgrounds/preset1.jpg" && i.complete && i.naturalWidth > 0));
  await page.waitForTimeout(3000);
  const native = (command, args = {}) => page.evaluate(
    ({ command, args }) => window.__TAURI_INTERNALS__.invoke("preview_native", { command, args }),
    { command, args },
  );
  assert.equal(await page.evaluate(() => !!window.__FRAMESCOPE_BROWSER_PREVIEW__), false);
  const result = { browser: browser.version(), native: true, routes: [], transitions: [], corners: [], errors };
  result.config = await native("config_read");
  result.hardware = await native("get_overlay_hardware_data");
  assert.ok(result.hardware.cpu_usage >= 0 && result.hardware.memory_usage > 0);
  for (const route of ["/", "/hardware", "/targets", "/reports", "/overlay-panel", "/settings"]) {
    await page.locator(`#main-sidebar a[href="${route}"]`).click();
    await page.waitForTimeout(700);
    assert.equal(new URL(page.url()).pathname, route);
    const text = await page.locator("body").innerText();
    const expected = {
      "/": "FrameScope", "/hardware": "硬件", "/targets": "监控目标",
      "/reports": "报告", "/overlay-panel": "悬浮框", "/settings": "设置",
    };
    assert.ok(text.includes(expected[route]), `Missing route content: ${route}`);
    result.routes.push({ route, text: text.slice(0, 1500) });
    await page.screenshot({ path: path.join(out, `route-${route.replace(/\//g, "") || "home"}.png`) });
  }
  await page.locator('#main-sidebar a[href="/targets"]').click();
  await page.waitForTimeout(500);
  const session = await page.context().newCDPSession(page);
  const writes = [];
  const frames = [];
  session.on("Page.screencastFrame", ({ data, sessionId, metadata }) => {
    const file = `transition-${String(frames.length).padStart(4, "0")}.jpg`;
    frames.push({ file, timestamp: metadata.timestamp });
    writes.push(fs.writeFile(path.join(out, file), Buffer.from(data, "base64")));
    void session.send("Page.screencastFrameAck", { sessionId });
  });
  await session.send("Page.startScreencast", { format: "jpeg", quality: 95, everyNthFrame: 1 });
  for (let i = 0; i < 20; i++) {
    const route = i % 2 ? "/targets" : "/reports";
    const title = i % 2 ? "监控目标" : "报告";
    await page.evaluate(() => {
      window.__qaFrames = [];
      const sample = time => {
        window.__qaFrames.push({
          time,
          pages: [...document.querySelectorAll(".page-animating")].map(e => ({
            title: e.querySelector("h2")?.textContent,
            top: e.getBoundingClientRect().top,
            opacity: Number(getComputedStyle(e).opacity),
          })),
        });
        window.__qaRaf = requestAnimationFrame(sample);
      };
      window.__qaRaf = requestAnimationFrame(sample);
    });
    await page.locator(`#main-sidebar a[href="${route}"]`).click();
    await page.waitForTimeout(500);
    const samples = await page.evaluate(() => { cancelAnimationFrame(window.__qaRaf); return window.__qaFrames; });
    const tops = samples.flatMap(s => s.pages.filter(p => p.title === title && p.opacity > 0.02).map(p => p.top));
    assert.ok(tops.length > 0, `No animation samples: ${route}`);
    assert.ok(Math.max(...tops) - Math.min(...tops) < 1, `Incoming page jumped: ${route}`);
    result.transitions.push({ route, samples });
  }
  await session.send("Page.stopScreencast");
  await Promise.all(writes);
  result.frames = frames;
  await page.locator('#main-sidebar a[href="/"]').click();
  await page.waitForTimeout(800);
  for (const name of ["preset", "flat"]) {
    if (name === "flat") {
      const png = await sharp({ create: { width: 16, height: 16, channels: 3, background: "#101010" } }).png().toBuffer();
      await page.locator("img").first().evaluate((e, src) => { e.src = src; }, `data:image/png;base64,${png.toString("base64")}`);
      await page.waitForTimeout(200);
    }
    const png = await page.screenshot();
    await fs.writeFile(path.join(out, `${name}.png`), png);
    for (const [label, locator] of [
      ["card", page.locator(".jelly-bounce-card").first()],
      ["sidebar", page.locator("#main-sidebar")],
    ]) {
      const box = await locator.boundingBox();
      assert.ok(box);
      const crop = await sharp(png).extract({ left: Math.max(0, Math.round(box.x) - 2), top: Math.max(0, Math.round(box.y) - 2), width: 32, height: 32 }).png().toBuffer();
      await sharp(crop).resize(512, 512, { kernel: "nearest" }).toFile(path.join(out, `${name}-${label}-16x.png`));
      result.corners.push({ name, label, box });
    }
  }
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(out, "results.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify({ routes: result.routes.length, transitions: result.transitions.length, frames: frames.length, errors, out }));
  await session.detach();
  // Leave the native application running; closing the CDP browser would terminate it.
  process.exit(0);
}
main().catch(error => { console.error(error); process.exit(1); });
