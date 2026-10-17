const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const sharp = require('sharp');

async function main() {
  const output = path.resolve(process.argv[2] || '../artifacts/20260922-ui/glass-routes');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_EXECUTABLE });
  const results = [];
  try {
    for (const dpr of [1, 1.5, 2]) {
      const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: dpr });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await context.addInitScript(() => {
        localStorage.setItem('i18nextLng', 'zh');
        localStorage.setItem('nexbox_page_transition', 'fade');
        localStorage.setItem('framescope.preview.store.settings.json', JSON.stringify({
          'has-launched': true, 'background-mode': 'preset', 'active-preset-index': 0,
          'carousel-enabled': false, 'liquid-glass-enabled': true, 'liquid-glass-blur': 12,
          'liquid-glass-mode': 'normal', 'jelly-bounce-enabled': false, nexbox_page_transition: 'fade',
        }));
      });
      await page.goto(process.env.PREVIEW_URL || 'http://127.0.0.1:1430/');
      await page.locator('#main-sidebar').waitFor();
      await page.waitForTimeout(700);
      for (const mode of ['normal', 'real']) {
        for (const route of ['/', '/hardware', '/targets', '/reports', '/overlay-panel', '/settings']) {
          if (new URL(page.url()).pathname !== route) {
            await page.locator(`#main-sidebar a[href="${route}"]`).click();
          }
          assert.equal(new URL(page.url()).pathname, route);
          if (route === '/settings') await page.locator('.jelly-bounce-menu-item:visible').first().waitFor();
          if (route === '/') await page.locator('.jelly-bounce-card:visible').first().waitFor();
          await page.waitForTimeout(250);
          await page.waitForFunction(() => !document.getAnimations().some(animation =>
            animation.effect?.pseudoElement?.startsWith('::view-transition')));
          const name = `${mode}-dpr${dpr}-${route.slice(1) || 'home'}`;
          await page.screenshot({ path: path.join(output, `${name}.png`) });
          const surfaces = await page.evaluate(() => [...document.querySelectorAll('.jelly-bounce-card, .jelly-bounce-menu-item, #main-sidebar')]
            .filter(element => { const r = element.getBoundingClientRect(); return r.width > 0 && r.height > 0; })
            .map(element => {
              const css = getComputedStyle(element);
              return { className: element.className, id: element.id,
                radius: css.borderTopLeftRadius, filter: css.backdropFilter,
                underlay: getComputedStyle(element, '::before').backdropFilter,
                overflow: css.overflow, background: css.backgroundColor,
                box: element.getBoundingClientRect().toJSON() };
            }));
          assert.ok(surfaces.length > 0);
          for (const surface of surfaces.filter(item => item.className.includes('jelly-bounce-card') || item.className.includes('jelly-bounce-menu-item'))) {
            assert.ok(parseFloat(surface.radius) > 0, `${name} lost radius`);
            assert.equal(surface.filter, 'none', `${name} has a filtered clipping root`);
            assert.ok(surface.overflow.startsWith('hidden'), `${name} did not clip rounded corners`);
            assert.equal(surface.className.includes('real-liquid-glass'), mode === 'real', `${name} failed mode switch`);
            if (surface.className.includes('jelly-bounce-card')) {
              assert.equal(surface.background === 'rgba(0, 0, 0, 0)', mode === 'real', `${name} card opacity did not switch`);
            }
          }
          results.push({ dpr, mode, route, surfaces });
          if (route === '/' || route === '/settings') {
            const target = surfaces.find(surface => route === '/settings' ? surface.className.includes('jelly-bounce-menu-item') : surface.className.includes('jelly-bounce-card'));
            assert.ok(target);
            const image = await page.screenshot();
            const left = Math.max(0, Math.floor((target.box.x - 2) * dpr));
            const top = Math.max(0, Math.floor((target.box.y - 2) * dpr));
            const size = Math.ceil(26 * dpr);
            await sharp(image).extract({ left, top, width: size, height: size }).resize(size * 8, size * 8, { kernel: 'nearest' })
              .toFile(path.join(output, `${name}-corner-8x.png`));
          }
        }
        if (mode === 'normal') {
          await page.locator('.jelly-bounce-menu-item').getByText('外观', { exact: true }).click();
          await page.getByText('玻璃模式', { exact: true }).waitFor();
          await page.getByText('普通液态玻璃', { exact: true }).click();
          await page.getByText('透明液态玻璃', { exact: true }).click();
          await page.waitForFunction(() => JSON.parse(localStorage.getItem('framescope.preview.store.settings.json'))['liquid-glass-mode'] === 'real');
          await page.waitForTimeout(500);
        }
      }
      assert.deepEqual(errors, []);
      await context.close();
    }
    await fs.writeFile(path.join(output, 'results.json'), JSON.stringify({ browser: browser.version(), environment: 'isolated Chromium preview', results }, null, 2));
    console.log(JSON.stringify({ pages: results.length, modes: 2, dprs: 3, errors: 0 }));
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
