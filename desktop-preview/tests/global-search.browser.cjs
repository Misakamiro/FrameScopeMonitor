const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire(path.join(process.env.FRAMESCOPE_BROWSER_DEPS, 'package.json'))('playwright');

(async () => {
  const output = path.resolve(process.argv[2]);
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const mode = process.env.SEARCH_TRANSITION_MODE || 'off';
    const page = await browser.newPage({ viewport: mode === 'off' ? { width: 1400, height: 900 } : { width: 1140, height: 715 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(mode => {
      localStorage.setItem('i18nextLng', 'zh');
      localStorage.setItem('nexbox_page_transition', mode);
    }, mode);
    await page.goto('http://127.0.0.1:1441/');
    const input = page.locator('#global-search input');
    await input.waitFor({ state: 'visible' });
    await page.waitForFunction(() => !document.querySelector('[data-page-transition]'));
    await page.evaluate(async () => { await import('/src/config/search-index.ts'); });
    const snapshot = () => page.evaluate(async () => {
      const { getPreviewState } = await import('/src/preview/runtime/index.ts');
      const state = getPreviewState();
      return { config: state.config, overlay: state.overlay.settings, enabled: state.overlay.enabled,
        monitorPhase: state.monitor.phase, targets: state.targets, reports: state.reports.map(report => report.id) };
    });
    const before = await snapshot();
    const results = [];
    async function search(query, id, keyboard = false) {
      await input.fill(query);
      await page.waitForFunction(query => document.getElementById('global-search-results')?.dataset.searchQuery === query, query);
      const option = page.locator(`#search-result-${id}`);
      await option.waitFor({ state: 'visible' });
      assert(await option.getAttribute('role') === 'option');
      if (keyboard) {
        assert.equal(await input.getAttribute('aria-activedescendant'), `search-result-${id}`);
        await input.press('Enter');
      } else { await option.click(); }
      await page.locator(`[data-search-highlight="${id}"]`).waitFor({ state: 'visible' });
      await page.waitForFunction(id => document.querySelector(`[data-search-highlight="${id}"]`) === document.activeElement, id);
      const visible = await page.locator(`[data-search-highlight="${id}"]`).evaluate(element => {
        const rect = element.getBoundingClientRect();
        return { top: rect.top, bottom: rect.bottom, height: innerHeight, focused: document.activeElement === element };
      });
      assert(visible.top < visible.height && visible.bottom > 60, `${id}: target must be scrolled into view`);
      assert(visible.focused, `${id}: target must receive keyboard focus`);
      results.push({ query, id, url: new URL(page.url()).pathname + new URL(page.url()).search, visible });
    }
    await search('全局采样间隔', 'fs-sample-interval', true);
    await search('GitHub 加速', 'update-accelerated');
    await search('GitHub加速', 'update-accelerated', true);
    await search('玻璃模式', 'appearance-glass-mode', true);
    // Manual submenu changes must update the URL. Searching the same setting
    // again must return to its owner instead of keeping the last local tab.
    await page.getByText('通用', { exact: true }).click();
    assert.equal(new URL(page.url()).searchParams.get('section'), 'general');
    await search('玻璃模式', 'appearance-glass-mode', true);
    await page.goBack();
    await page.locator('#search-settings-general').waitFor({ state: 'visible' });
    await page.goForward();
    await page.locator('[data-search-highlight="appearance-glass-mode"]').waitFor({ state: 'visible' });
    await search('竖排 透明度', 'overlay-opacity');
    await search('单行宽度', 'overlay-width', true); // Conditional: locate the group without changing style.
    await search('CPU 分核电压', 'overlay-metric-cpu_core_voltages', true);
    await search('MR自定义颜色', 'appearance-mr-color');
    assert(await page.locator('[data-search-highlight="appearance-mr-color"]').evaluate(element =>
      !!element.closest('#search-appearance-background')), 'MR color must locate its background group, not the unrelated theme color picker');
    await search('手动启动', 'fs-tray-start', true);
    await search('Windows启动', 'fs-windows-startup', true);
    await search('PawnIO', 'hardware-driver', true);
    await search('重新生成报告', 'reports-regenerate', true);
    await search('添加目标', 'targets-add', true);
    await search('启动监控', 'home-monitor-start', true);
    const catalog = await page.evaluate(async () => {
      const { searchIndex } = await import('/src/config/search-index.ts');
      const { default: i18n } = await import('/src/lib/i18n.ts');
      return searchIndex.filter(item => item.targetId).map(item => ({ id: item.id, label: i18n.t(item.nameKey) }));
    });
    if (mode === 'off') for (const item of catalog) await search(item.label, item.id);
    await search('透明度', 'overlay-opacity', true);
    await input.fill('CPU');
    await page.waitForFunction(() => document.getElementById('global-search-results')?.dataset.searchQuery === 'CPU');
    await page.locator('#global-search-results [role="option"]').first().waitFor();
    for (let i = 0; i < 12; i++) await input.press('ArrowDown');
    const active = await input.getAttribute('aria-activedescendant');
    // Keep the selection across the background hardware poll, rather than
    // resetting it whenever the startup context publishes a new sensor value.
    await page.waitForTimeout(1300);
    assert.equal(await input.getAttribute('aria-activedescendant'), active, 'background refresh or a stationary pointer must not reset keyboard selection');
    const inList = await page.locator(`#${active}`).evaluate(element => {
      const rect = element.getBoundingClientRect(), list = document.getElementById('global-search-results').getBoundingClientRect();
      return rect.top >= list.top && rect.bottom <= list.bottom + 1;
    });
    assert(inList, 'keyboard selection must scroll inside the results popup');
    const beforeEscape = page.url();
    await input.press('Escape');
    assert.equal(await page.locator('#global-search-results').count(), 0);
    assert.equal(page.url(), beforeEscape, 'Escape must close search without navigating away from OSD settings');
    assert.deepEqual(await snapshot(), before, 'search/navigation must not alter configuration or overlay state');
    assert.deepEqual(errors, []);
    await page.screenshot({ path: path.join(output, 'after-search.png') });
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ passed: true, mode, catalogTargets: catalog.length, results, errors, settingsUnchanged: true }, null, 2));
    console.log(`Search UI: ${results.length} navigation cases, keyboard scrolling, Escape and no setting changes: PASS`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
