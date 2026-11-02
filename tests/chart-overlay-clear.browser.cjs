const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire(path.join(process.env.FRAMESCOPE_BROWSER_DEPS, 'package.json'))('playwright');

// Execute the actual report function on a real Canvas2D backing store. This
// catches trails left when CSS/browser scaling yields devicePixelRatio < 1.
(async () => {
  const source = fs.readFileSync(path.join(__dirname, '../src/reporting/FrameScopeReportGenerator.Html.Scripts.cs'), 'utf8');
  const clear = source.match(/function clearOverlay\(\)\{[^\n]+\}/)?.[0];
  assert(clear, 'production overlay clearing function missing');
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  const results = [];
  try {
    const page = await browser.newPage();
    for (const scale of [.5, .8, 1, 1.25, 2]) {
      const result = await page.evaluate(({ clear, scale }) => {
        const overlay = document.createElement('canvas');
        overlay.width = Math.round(1000 * scale);
        overlay.height = Math.round(500 * scale);
        const octx = overlay.getContext('2d');
        octx.setTransform(scale, 0, 0, scale, 0, 0);
        octx.fillStyle = 'white';
        octx.fillRect(0, 0, 1000, 500);
        eval(clear + '; clearOverlay();');
        const pixels = octx.getImageData(0, 0, overlay.width, overlay.height).data;
        let residual = 0;
        for (let i = 3; i < pixels.length; i += 4) if (pixels[i] !== 0) residual++;
        const transform = octx.getTransform();
        return { scale, residual, transform: [transform.a, transform.d], size: [overlay.width, overlay.height] };
      }, { clear, scale });
      results.push(result);
      console.log(JSON.stringify(result));
    }
    const out = process.argv[2];
    if (out) fs.writeFileSync(out, JSON.stringify(results, null, 2));
    for (const r of results) {
      assert.equal(r.residual, 0, `scale=${r.scale}: old mouse guides remain outside the cleared rectangle`);
      assert(r.transform.every(value => Math.abs(value - r.scale) < 1e-6), 'clearing must preserve subsequent drawing coordinates');
    }
    if (process.argv[3]) {
      const { pathToFileURL } = require('node:url');
      const generated = [];
      for (const scale of [.8, 1.25]) {
        const context = await browser.newContext({ deviceScaleFactor: scale, viewport: { width: 1500, height: 1000 } });
        const report = await context.newPage();
        const errors = [];
        report.on('pageerror', error => errors.push(error.message));
        await report.goto(pathToFileURL(path.resolve(process.argv[3])).href);
        await report.waitForFunction(() => typeof hover === 'function' && currentTimes.length > 0);
        const result = await report.evaluate(() => {
          const rect = chartBox.getBoundingClientRect(), { pw, ph } = chartDims();
          for (let i = 1; i < 100; i++) hover({ clientX: rect.left + PAD.l + pw * i / 100, clientY: rect.top + PAD.t + ph / 2 });
          hover({ clientX: rect.left, clientY: rect.top }); // Leaving the plot clears every guide.
          const pixels = octx.getImageData(0, 0, overlay.width, overlay.height).data;
          let residual = 0;
          for (let i = 3; i < pixels.length; i += 4) if (pixels[i] !== 0) residual++;
          return { dpr: devicePixelRatio, residual, points: currentTimes.length };
        });
        assert.equal(result.residual, 0, `generated report retains hover trails at DPR=${scale}`);
        assert.deepEqual(errors, []);
        generated.push(result);
        await context.close();
      }
      if (out) fs.writeFileSync(out.replace(/\.json$/, '-generated.json'), JSON.stringify(generated, null, 2));
      console.log('generated report hover: PASS', JSON.stringify(generated));
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
