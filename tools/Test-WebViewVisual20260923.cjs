const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const sharp = require('sharp');

async function main() {
  const output = path.resolve(process.argv[2] || 'artifacts/20260922-ui/native-webview');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.connectOverCDP('http://127.0.0.1:9333');
  const page = browser.contexts()[0].pages().find(item => item.url().startsWith('http://tauri.localhost'));
  assert.ok(page, 'No isolated native main WebView');
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  assert.equal(await page.evaluate(() => !!window.__FRAMESCOPE_BROWSER_PREVIEW__), false);
  const native = (command, args = {}) => page.evaluate(({ command, args }) =>
    window.__TAURI_INTERNALS__.invoke('preview_native', { command, args }), { command, args });
  const config = await native('config_read');
  assert.ok(config && typeof config === 'object');
  await page.evaluate(() => {
    const key = 'framescope.preview.store.settings.json';
    const value = JSON.parse(localStorage.getItem(key) || '{}');
    Object.assign(value, { 'has-launched': true, 'background-mode': 'preset', 'active-preset-index': 0,
      'carousel-enabled': false, 'liquid-glass-enabled': true, 'liquid-glass-blur': 12,
      'liquid-glass-mode': 'normal', 'jelly-bounce-enabled': false, nexbox_page_transition: 'fade' });
    localStorage.setItem(key, JSON.stringify(value));
    localStorage.setItem('nexbox_page_transition', 'fade');
  });
  await page.reload();
  await page.locator('#main-sidebar').waitFor();
  await page.waitForTimeout(1500);
  if (process.env.FADE_BLEND_EXPERIMENT) {
    await page.addStyleTag({ content: `
      @keyframes framescopeCrossfadeIn { from { opacity: 0 } to { opacity: 1 } }
      ::view-transition-old(root) { animation: none !important; mix-blend-mode: normal !important; }
      ::view-transition-new(root) { animation: framescopeCrossfadeIn 0.18s ease-out both !important; mix-blend-mode: normal !important; }
    ` });
  }
  const results = [];
  for (const mode of ['normal', 'real']) {
    for (const route of ['/', '/hardware', '/targets', '/reports', '/overlay-panel', '/settings']) {
      await page.locator(`#main-sidebar a[href="${route}"]`).click();
      if (route === '/settings') await page.locator('.jelly-bounce-menu-item:visible').first().waitFor();
      if (route === '/') await page.locator('.jelly-bounce-card:visible').first().waitFor();
      await page.waitForTimeout(400);
      assert.equal(new URL(page.url()).pathname, route);
      const name = `${mode}-${route.slice(1) || 'home'}`;
      const png = await page.screenshot();
      await fs.writeFile(path.join(output, `${name}.png`), png);
      const surfaces = await page.evaluate(() => [...document.querySelectorAll('.jelly-bounce-card, .jelly-bounce-menu-item, #main-sidebar')]
        .filter(element => { const box = element.getBoundingClientRect(); return box.width && box.height; })
        .map(element => { const style = getComputedStyle(element); return {
          className: element.className, id: element.id, filter: style.backdropFilter,
          radius: style.borderTopLeftRadius, background: style.backgroundColor,
          before: getComputedStyle(element, '::before').backdropFilter,
          box: element.getBoundingClientRect().toJSON(),
        }; }));
      for (const surface of surfaces.filter(item => item.className.includes('jelly-bounce-card') || item.className.includes('jelly-bounce-menu-item'))) {
        assert.ok(parseFloat(surface.radius) > 0, `${name}: missing radius`);
        assert.equal(surface.filter, 'none', `${name}: root filter damages corner clipping`);
        assert.equal(surface.className.includes('real-liquid-glass'), mode === 'real', `${name}: glass mode mismatch`);
      }
      const corner = surfaces.find(item => route === '/settings' ? item.className.includes('jelly-bounce-menu-item') : item.className.includes('jelly-bounce-card'));
      if (corner && (route === '/' || route === '/settings')) {
        const dpr = await page.evaluate(() => window.devicePixelRatio);
        const size = Math.ceil(26 * dpr);
        await sharp(png).extract({ left: Math.max(0, Math.floor((corner.box.x - 2) * dpr)),
          top: Math.max(0, Math.floor((corner.box.y - 2) * dpr)), width: size, height: size })
          .resize(size * 8, size * 8, { kernel: 'nearest' }).toFile(path.join(output, `${name}-corner-8x.png`));
      }
      results.push({ mode, route, surfaces });
    }
    if (mode === 'normal') {
      await page.locator('.jelly-bounce-menu-item').getByText('外观', { exact: true }).click();
      await page.getByText('普通液态玻璃', { exact: true }).click();
      await page.getByText('透明液态玻璃', { exact: true }).click();
      await page.waitForFunction(() => JSON.parse(localStorage.getItem('framescope.preview.store.settings.json'))?.['liquid-glass-mode'] === 'real');
      await page.waitForTimeout(500);
    }
  }
  await page.locator('#main-sidebar a[href="/targets"]').click();
  await page.waitForTimeout(500);
  const cdp = await page.context().newCDPSession(page);
  const writes = [];
  const frames = [];
  cdp.on('Page.screencastFrame', ({ data, sessionId, metadata }) => {
    const name = `frame-${String(frames.length).padStart(4, '0')}.jpg`;
    frames.push({ name, timestamp: metadata.timestamp });
    writes.push(fs.writeFile(path.join(output, name), Buffer.from(data, 'base64')));
    void cdp.send('Page.screencastFrameAck', { sessionId });
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 95, everyNthFrame: 1 });
  let badAncestorFrames = 0;
  for (let i = 0; i < 20; i++) {
    const route = i % 2 ? '/targets' : '/reports';
    await page.locator(`#main-sidebar a[href="${route}"]`).click();
    await page.waitForTimeout(300);
    badAncestorFrames += await page.evaluate(() => [...document.querySelectorAll('.jelly-bounce-card')]
      .filter(card => { for (let parent = card.parentElement; parent; parent = parent.parentElement) {
        const opacity = Number(getComputedStyle(parent).opacity);
        if (opacity > 0 && opacity < 1) return true;
      } return false; }).length);
  }
  await cdp.send('Page.stopScreencast');
  await Promise.all(writes);
  await cdp.detach();
  assert.equal(badAncestorFrames, 0);
  assert.ok(frames.length > 100, `Only ${frames.length} captured WebView frames`);
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(output, 'results.json'), JSON.stringify({
    browser: browser.version(), environment: 'isolated native Tauri WebView2 debug host',
    routes: results, frames, badAncestorFrames, errors,
  }, null, 2));
  console.log(JSON.stringify({ routes: results.length, transitionFrames: frames.length, badAncestorFrames, errors }));
  process.exit(0);
}
main().catch(error => { console.error(error); process.exit(1); });
