const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const sharp = require('sharp');

async function main() {
  const mode = process.env.GLASS_MODE || 'normal';
  const transition = process.env.TRANSITION_MODE || 'fade';
  assert.ok(['normal', 'real'].includes(mode));
  assert.ok(['fade', 'slide'].includes(transition));
  const output = path.resolve(process.env.CAPTURE_OUTPUT || `../artifacts/native-${transition}-${mode}-${Date.now()}`);
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.connectOverCDP('http://127.0.0.1:9333');
  const page = browser.contexts()[0].pages().find(p => p.url().startsWith('http://tauri.localhost'));
  assert.ok(page);
  assert.equal(await page.evaluate(() => !!window.__FRAMESCOPE_BROWSER_PREVIEW__), false);
  await page.evaluate(({ mode, transition }) => {
    const key = 'framescope.preview.store.settings.json';
    const settings = JSON.parse(localStorage.getItem(key) || '{}');
    Object.assign(settings, { 'liquid-glass-mode': mode, nexbox_page_transition: transition });
    localStorage.setItem(key, JSON.stringify(settings));
    localStorage.setItem('nexbox_page_transition', transition);
  }, { mode, transition });
  await page.reload();
  await page.locator('#main-sidebar a[href="/targets"]').click();
  await page.waitForTimeout(700);
  const cdp = await page.context().newCDPSession(page);
  const frames = [];
  const writes = [];
  cdp.on('Page.screencastFrame', ({ data, sessionId, metadata }) => {
    const file = `frame-${String(frames.length).padStart(4, '0')}.jpg`;
    frames.push({ file, timestamp: metadata.timestamp });
    writes.push(fs.writeFile(path.join(output, file), Buffer.from(data, 'base64')));
    void cdp.send('Page.screencastFrameAck', { sessionId });
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 95, everyNthFrame: 1 });
  for (let i = 0; i < 20; i++) {
    await page.locator(`#main-sidebar a[href="${i % 2 ? '/targets' : '/reports'}"]`).click();
    await page.waitForTimeout(300);
  }
  await cdp.send('Page.stopScreencast');
  await Promise.all(writes);
  await cdp.detach();
  const means = [];
  const region = { left: 400, top: 10, width: 160, height: 40 };
  for (const frame of frames) {
    const pixels = await sharp(path.join(output, frame.file)).extract(region).removeAlpha().raw().toBuffer();
    means.push(pixels.reduce((sum, value) => sum + value, 0) / pixels.length);
  }
  const maxAdjacent = Math.max(...means.slice(1).map((value, i) => Math.abs(value - means[i])));
  const report = { environment: 'native WebView2 diagnostic host', browser: browser.version(), mode, transition,
    switches: 20, region, frames, maxAdjacent, minimum: Math.min(...means), maximum: Math.max(...means) };
  await fs.writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  assert.ok(frames.length > 100);
  assert.ok(maxAdjacent < 1, `Static wallpaper brightness jumped by ${maxAdjacent}`);
  console.log(JSON.stringify({ mode, frames: frames.length, maxAdjacent }));
  process.exit(0);
}
main().catch(error => { console.error(error); process.exit(1); });
