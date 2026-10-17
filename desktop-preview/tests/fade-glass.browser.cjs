const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const sharp = require('sharp');

async function main() {
  const output = path.resolve(process.env.FADE_OUTPUT || '../artifacts/20260922-ui/fade-red');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE });
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const results = [];
  try {
    await page.addInitScript(() => {
      localStorage.setItem('nexbox_page_transition', 'fade');
      localStorage.setItem('chakra-ui-color-mode', 'light');
      localStorage.setItem('framescope.preview.store.settings.json', JSON.stringify({
        'has-launched': true, 'background-mode': 'preset', 'active-preset-index': 0,
        'carousel-enabled': false, 'liquid-glass-enabled': true, 'liquid-glass-blur': 12,
        'liquid-glass-mode': 'normal', 'jelly-bounce-enabled': false, nexbox_page_transition: 'fade',
      }));
    });
    await page.goto(process.env.PREVIEW_URL || 'http://127.0.0.1:1430/targets');
    await page.locator('#main-sidebar a[href="/reports"]').waitFor();
    await page.waitForTimeout(1500);
    await page.evaluate(() => {
      const start = document.startViewTransition.bind(document);
      window.__snapshotTransitions = [];
      document.startViewTransition = callback => {
        const transition = start(callback);
        const record = { ready: false, finished: false };
        window.__snapshotTransitions.push(record);
        transition.ready.then(() => { record.ready = true; }, () => {});
        transition.finished.then(() => { record.finished = true; });
        return transition;
      };
    });
    // Controlled probe: only the opacity of the real page ancestor changes.
    if (process.env.FADE_PROBE) {
      await page.addStyleTag({ content: 'body { background: repeating-linear-gradient(90deg, #111 0 8px, #eee 8px 16px) !important; }' });
      const card = page.locator('.jelly-bounce-card:visible').filter({ has: page.locator('[aria-label="启用 Counter-Strike 2"]') }).first();
      const clip = await card.boundingBox();
      const pixels = [];
      for (const opacity of ['0.98', '0.99', '1']) {
        await card.evaluate((element, opacity) => {
          let parent = element.parentElement;
          while (parent && !(parent.style.width === '100%' && parent.style.height === '100%')) parent = parent.parentElement;
          if (!parent) throw new Error('Page wrapper missing');
          parent.style.setProperty('opacity', opacity, 'important');
          return { tag: parent.tagName, style: parent.getAttribute('style'), opacity: getComputedStyle(parent).opacity };
        }, opacity);
        await page.waitForTimeout(50);
        const buffer = await page.screenshot({ clip });
        await fs.writeFile(path.join(output, `opacity-${opacity}.png`), buffer);
        pixels.push(await sharp(buffer).removeAlpha().raw().toBuffer());
      }
      const difference = (a, b) => a.reduce((sum, value, index) => sum + Math.abs(value - b[index]), 0) / a.length;
      console.log(JSON.stringify({ probe: { adjacent: difference(pixels[0], pixels[1]), boundary: difference(pixels[1], pixels[2]) } }));
      await page.reload();
      await page.waitForTimeout(1200);
    }
    await page.evaluate(() => {
      window.__fadeSamples = [];
      const sample = () => {
        const bad = [];
        for (const card of document.querySelectorAll('.jelly-bounce-card')) {
          if (!card.getBoundingClientRect().width) continue;
          for (let p = card.parentElement; p; p = p.parentElement) {
            const opacity = Number(getComputedStyle(p).opacity);
            if (opacity > 0 && opacity < 1) bad.push({ opacity, className: p.className });
          }
        }
        window.__fadeSamples.push({ time: performance.now(), bad });
        window.__fadeRaf = requestAnimationFrame(sample);
      };
      window.__fadeRaf = requestAnimationFrame(sample);
    });
    for (const route of ['/reports', '/settings', '/', '/hardware', '/targets', '/overlay-panel', '/reports']) {
      await page.locator(`#main-sidebar a[href="${route}"]`).click();
      await page.waitForTimeout(450);
      results.push({ route, url: page.url() });
    }
    const samples = await page.evaluate(() => { cancelAnimationFrame(window.__fadeRaf); return window.__fadeSamples; });
    const badFrames = samples.filter(sample => sample.bad.length);
    await page.waitForFunction(() => window.__snapshotTransitions.every(item => item.finished));
    const snapshots = await page.evaluate(() => window.__snapshotTransitions);
    // Seek the browser's actual snapshot animations to verify the rendered blend,
    // including the final boundary where the old live-opacity approach flashed.
    await page.evaluate(() => {
      const start = document.startViewTransition.bind(document);
      document.startViewTransition = callback => {
        const transition = start(callback);
        transition.ready.then(() => {
          window.__snapshotAnimations = document.getAnimations().filter(animation =>
            animation.effect.pseudoElement?.startsWith('::view-transition'));
          for (const animation of window.__snapshotAnimations) { animation.pause(); animation.currentTime = 0; }
        });
        return transition;
      };
    });
    await page.locator('#main-sidebar a[href="/targets"]').click();
    await page.waitForFunction(() => window.__snapshotAnimations?.length >= 1);
    const pixelFrames = [];
    for (const time of [0, 45, 90, 135, 179]) {
      await page.evaluate(time => { for (const animation of window.__snapshotAnimations) animation.currentTime = time; }, time);
      const buffer = await page.screenshot();
      await fs.writeFile(path.join(output, `snapshot-${time}.png`), buffer);
      pixelFrames.push(await sharp(buffer).removeAlpha().raw().toBuffer());
    }
    await page.evaluate(() => { for (const animation of window.__snapshotAnimations) animation.finish(); });
    await page.waitForTimeout(100);
    const settled = await page.screenshot();
    await fs.writeFile(path.join(output, 'snapshot-settled.png'), settled);
    const end = await sharp(settled).removeAlpha().raw().toBuffer();
    const last = pixelFrames.at(-1);
    const finalJump = end.reduce((sum, value, i) => sum + Math.abs(value - last[i]), 0) / end.length;
    const bounds = pixelFrames.slice(1, -1).map(frame => {
      const first = pixelFrames[0];
      let outside = 0;
      for (let i = 0; i < frame.length; i++) {
        if (frame[i] < Math.min(first[i], end[i]) - 5 || frame[i] > Math.max(first[i], end[i]) + 5) outside++;
      }
      return outside / frame.length;
    });
    // Restore the normal API for rapid navigation and browser history coverage.
    await page.reload();
    await page.waitForTimeout(1000);
    for (const route of ['/settings', '/hardware', '/', '/reports', '/targets']) {
      await page.locator(`#main-sidebar a[href="${route}"]`).click({ force: true });
      await page.waitForTimeout(25);
    }
    await page.waitForTimeout(500);
    assert.equal(new URL(page.url()).pathname, '/targets');
    await page.getByRole('heading', { name: '监控目标', exact: true }).waitFor();
    await page.goBack();
    await page.getByRole('heading', { name: '报告', exact: true }).waitFor();
    await fs.writeFile(path.join(output, 'results.json'), JSON.stringify({ browser: browser.version(), results, errors, badFrames, samples: samples.length, snapshots, finalJump, bounds }, null, 2));
    assert.deepEqual(errors, []);
    assert.equal(badFrames.length, 0, `${badFrames.length} frames changed the backdrop root by fading a live glass ancestor`);
    assert.ok(snapshots?.length >= 7 && snapshots.every(item => item.ready && item.finished), 'All route fades must actually animate');
    assert.ok(finalJump < 1, `Snapshot completion flashed: mean pixel jump ${finalJump}`);
    assert.ok(bounds.every(ratio => ratio < 0.01), `Snapshot frames exceeded endpoint pixel bounds: ${bounds}`);
    console.log(JSON.stringify({ samples: samples.length, snapshotTransitions: snapshots.length, finalJump, bounds, errors }));
  } finally {
    await browser.close();
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
