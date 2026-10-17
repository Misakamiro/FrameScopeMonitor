const fs = require('node:fs');
const path = require('node:path');
const {pathToFileURL} = require('node:url');
const {createRequire} = require('node:module');
const assert = require('node:assert/strict');
const {chromium} = createRequire('C:/Users/misakamiro/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/browser-entry.cjs')('playwright');

(async () => {
  const run = path.resolve(process.argv[2]);
  const output = path.resolve(process.argv[3]);
  const browser = await chromium.launch({headless:true,channel:'msedge'});
  try {
    const page = await browser.newPage({viewport:{width:1280,height:850}});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(pathToFileURL(path.join(run,'charts/framescope-interactive-report.html')).href);
    await page.locator('[data-view="cpuVid"]').click();
    await page.waitForFunction(() => document.getElementById('viewTitle').textContent === 'CPU 分核心电压（OSD）');
    const result = await page.evaluate(() => ({
      title:document.getElementById('viewTitle').textContent,
      selected:document.getElementById('metricSelect').value,
      chart:window.FRAMESCOPE_DATA.cpuOsdVoltage,
      drawn:currentSeries,
      tooltip:sourceRowsForTooltip(0),
    }));
    assert.equal(result.chart.sourceField,'CpuCoreVoltages');
    const csv = fs.readFileSync(path.join(run,'hardware-samples.csv'),'utf8').trim().split(/\r?\n/).map(r => r.split(','));
    const index = csv.shift().indexOf('CpuCoreVoltages');
    let raw = [];
    if (index < 0) {
      assert.equal(result.chart.available,false);
      assert.equal(result.drawn.length,0);
      assert.equal(result.selected,'none');
      assert.match(result.chart.reason,/重新采集/);
    } else {
      assert.equal(result.chart.available,true);
      assert.equal(result.selected,'summary');
      assert.equal(result.drawn.length,3);
      raw = csv.slice(-result.chart.t.length).map(row => (row[index] || '').split(';').map(v => {
        const n = Number(v); return v.trim() !== '' && Number.isFinite(n) && n > .2 && n < 5 ? n : null;
      }));
      for (const [core,series] of result.chart.series.entries()) {
        assert.equal(series.key,`cpu-osd-voltage:${core}`);
        assert.equal(series.name,`核心 ${core+1} 电压`);
        assert.deepEqual(series.data,raw.map(row => row[core] ?? null),`Core ${core+1} must match recorded OSD array`);
        await page.locator('#metricSelect').selectOption(series.key);
        await page.waitForFunction(key => currentSeries.length === 1 && currentSeries[0].key === key,series.key);
        assert.deepEqual(await page.evaluate(() => currentSeries[0].data),series.data);
      }
      for (let i=0;i<raw.length;i++) {
        const values=raw[i].filter(v=>v!==null);
        const round=v=>Number(v.toFixed(3));
        const expected=values.length?[round(values.reduce((a,b)=>a+b,0)/values.length),round(Math.max(...values)),round(Math.min(...values))]:[null,null,null];
        assert.deepEqual(result.drawn.map(s=>s.data[i]),expected,'Average/max/min must ignore missing cores');
      }
      assert.deepEqual(result.tooltip.map(s=>s.value),result.chart.series.map(s=>s.data[0]));
      await page.locator('#metricSelect').selectOption('summary');
      await page.waitForFunction(() => currentSeries.length === 3);
    }
    assert.deepEqual(errors,[]);
    await page.screenshot({path:path.join(output,'osd-voltage-report.png'),fullPage:true});
    fs.writeFileSync(path.join(output,'browser-verification.json'),JSON.stringify({passed:true,title:result.title,source:result.chart.sourceField,available:result.chart.available,coreCount:result.chart.series.length,plottedSamples:result.chart.t.length,tooltip:result.tooltip,errors},null,2));
    console.log('PASS: per-core report tab, every core selector, CSV parity, summaries, tooltip and browser errors');
  } finally { await browser.close(); }
})().catch(e => {console.error(e);process.exitCode=1});
