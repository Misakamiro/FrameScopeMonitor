const fs = require("fs");
const os = require("os");
const path = require("path");
const net = require("net");
const { spawn } = require("child_process");

function readArg(name, fallback = "") {
  const index = process.argv.indexOf(name);
  return index >= 0 && index < process.argv.length - 1 ? process.argv[index + 1] : fallback;
}

function hasArg(name) {
  return process.argv.includes(name);
}

function numberArg(name, fallback) {
  const value = Number(readArg(name, ""));
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

async function reservePort() {
  return await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = address && typeof address === "object" ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

async function waitForHttp(url, timeoutMs) {
  const started = Date.now();
  let lastError = null;
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError ? lastError.message : "no response"}`);
}

async function waitForJson(url, timeoutMs) {
  const started = Date.now();
  let lastError = null;
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) return await response.json();
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError ? lastError.message : "no response"}`);
}

async function openTarget(port, url) {
  const endpoint = `http://127.0.0.1:${port}/json/new?${encodeURIComponent(url)}`;
  const response = await fetch(endpoint, { method: "PUT" });
  if (!response.ok) throw new Error(`Failed to open target: HTTP ${response.status}`);
  const target = await response.json();
  if (!target.webSocketDebuggerUrl) throw new Error("Target did not expose a debugger websocket.");
  return target.webSocketDebuggerUrl;
}

function connectCdp(wsUrl) {
  if (typeof WebSocket !== "function") {
    throw new Error("This probe requires a Node runtime with global WebSocket support.");
  }

  const ws = new WebSocket(wsUrl);
  let nextId = 1;
  const pending = new Map();

  ws.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (!message.id || !pending.has(message.id)) return;
    const item = pending.get(message.id);
    pending.delete(message.id);
    if (message.error) item.reject(new Error(JSON.stringify(message.error)));
    else item.resolve(message.result || {});
  });

  return new Promise((resolve, reject) => {
    ws.addEventListener("open", () => {
      resolve({
        send(method, params = {}) {
          const id = nextId++;
          const payload = JSON.stringify({ id, method, params });
          return new Promise((sendResolve, sendReject) => {
            pending.set(id, { resolve: sendResolve, reject: sendReject });
            ws.send(payload);
          });
        },
        close() {
          try {
            ws.close();
          } catch {
            // ignored
          }
        },
      });
    });
    ws.addEventListener("error", (error) => reject(error));
  });
}

async function evaluate(page, expression) {
  let result;
  try {
    result = await page.send("Runtime.evaluate", {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
  } catch (error) {
    const preview = expression.replace(/\s+/g, " ").slice(0, 220);
    throw new Error(`${error.message || String(error)} while evaluating: ${preview}`);
  }
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result ? result.result.value : null;
}

async function waitForExpression(page, expression, timeoutMs = 10000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (await evaluate(page, expression)) return;
    await new Promise((resolve) => setTimeout(resolve, 80));
  }
  throw new Error(`Timed out waiting for expression: ${expression}`);
}

function startVite(repoRoot, port) {
  const frontendRoot = path.join(repoRoot, "src", "frontend");
  const viteBin = path.join(frontendRoot, "node_modules", "vite", "bin", "vite.js");
  if (!fs.existsSync(viteBin)) throw new Error(`Vite binary not found: ${viteBin}`);
  return spawn(process.execPath, [
    viteBin,
    "--host",
    "127.0.0.1",
    "--port",
    String(port),
    "--strictPort",
  ], {
    cwd: frontendRoot,
    stdio: ["ignore", "pipe", "pipe"],
  });
}

async function killProcessTree(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === "win32") {
    await new Promise((resolve) => {
      const killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
      killer.on("exit", resolve);
      killer.on("error", resolve);
    });
    return;
  }
  child.kill("SIGTERM");
}

function pageUrl(baseUrl, params = {}) {
  const url = new URL(baseUrl);
  for (const [key, value] of Object.entries(params)) {
    if (value !== "" && value !== null && value !== undefined) url.searchParams.set(key, String(value));
  }
  return url.toString();
}

async function navigateApp(page, url, pageId) {
  await page.send("Page.navigate", { url });
  await waitForExpression(page, "document.readyState === 'complete' && !!document.querySelector('[data-smoke-nav]')", 15000);
  return await evaluate(
    page,
    `new Promise((resolve) => {
      const started = performance.now();
      document.querySelector('[data-smoke-nav="${pageId}"]').click();
      const poll = () => {
        if (document.querySelector('[data-smoke-page="${pageId}"]')) {
          requestAnimationFrame(() => requestAnimationFrame(() => resolve({
            pageId: ${JSON.stringify(pageId)},
            elapsedMs: performance.now() - started,
          })));
          return;
        }
        setTimeout(poll, 20);
      };
      poll();
    })`,
  );
}

async function perfMetrics(page) {
  const metrics = await page.send("Performance.getMetrics");
  const map = {};
  for (const metric of metrics.metrics || []) map[metric.name] = metric.value;
  return {
    cdpNodes: Number.isFinite(map.Nodes) ? map.Nodes : null,
    documents: Number.isFinite(map.Documents) ? map.Documents : null,
    jsHeapUsedMb: Number.isFinite(map.JSHeapUsedSize) ? Number((map.JSHeapUsedSize / 1024 / 1024).toFixed(3)) : null,
    jsHeapTotalMb: Number.isFinite(map.JSHeapTotalSize) ? Number((map.JSHeapTotalSize / 1024 / 1024).toFixed(3)) : null,
    layoutCount: Number.isFinite(map.LayoutCount) ? map.LayoutCount : null,
    recalcStyleCount: Number.isFinite(map.RecalcStyleCount) ? map.RecalcStyleCount : null,
  };
}

async function collectDomMetrics(page, label) {
  const dom = await evaluate(
    page,
    `(() => {
      const processList = document.querySelector('.process-result-list');
      const processSummary = document.querySelector('.process-result-summary strong');
      const processTotalRaw = processList ? processList.getAttribute('data-process-total') : null;
      const processRenderedRaw = processList ? processList.getAttribute('data-rendered-row-count') : null;
      const processTotalAttr = processTotalRaw === null ? NaN : Number(processTotalRaw);
      const processRenderedAttr = processRenderedRaw === null ? NaN : Number(processRenderedRaw);
      const processRows = document.querySelectorAll('.process-result-row');
      const targetRows = document.querySelectorAll('.target-list__row');
      const reportRows = document.querySelectorAll('.report-list-row');
      const page = document.querySelector('[data-smoke-page]');
      const pageScrollWidth = Math.max(document.documentElement.scrollWidth || 0, document.body ? document.body.scrollWidth || 0 : 0);
      const clientWidth = document.documentElement.clientWidth || window.innerWidth;
      const parseCount = (text) => {
        const match = String(text || '').match(/\\d+/);
        return match ? Number(match[0]) : 0;
      };
      return {
        label: ${JSON.stringify(label)},
        page: page ? page.getAttribute('data-smoke-page') : '',
        domNodes: document.querySelectorAll('*').length,
        buttons: document.querySelectorAll('button').length,
        inputs: document.querySelectorAll('input').length,
        transitioned: Array.from(document.querySelectorAll('*')).filter((el) => {
          const style = getComputedStyle(el);
          return style.transitionDuration !== '0s' || style.animationName !== 'none';
        }).length,
        targetRows: targetRows.length,
        reportRows: reportRows.length,
        processRenderedRows: Number.isFinite(processRenderedAttr) ? processRenderedAttr : processRows.length,
        processDomRows: processRows.length,
        processTotalRows: Number.isFinite(processTotalAttr) ? processTotalAttr : Math.max(processRows.length, parseCount(processSummary ? processSummary.textContent : '')),
        processWindowed: processList ? processList.getAttribute('data-windowed') === 'true' : false,
        processScrollTop: processList ? Math.round(processList.scrollTop) : 0,
        processScrollHeight: processList ? Math.round(processList.scrollHeight) : 0,
        processClientHeight: processList ? Math.round(processList.clientHeight) : 0,
        overflowX: pageScrollWidth > clientWidth,
        activeText: document.activeElement ? document.activeElement.textContent || document.activeElement.getAttribute('aria-label') || document.activeElement.tagName : '',
      };
    })()`,
  );
  return { ...dom, ...(await perfMetrics(page)) };
}

async function captureScreenshot(page, outputDir, name) {
  const result = await page.send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  const screenshotPath = path.join(outputDir, `${name}.png`);
  fs.writeFileSync(screenshotPath, Buffer.from(result.data, "base64"));
  return screenshotPath;
}

async function setProcessSearchInput(page, value) {
  return await evaluate(
    page,
    `new Promise((resolve) => {
      const input = document.querySelector('.process-search-field input');
      if (!input) {
        resolve({ available: false });
        return;
      }
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      const started = performance.now();
      setter.call(input, ${JSON.stringify(value)});
      input.dispatchEvent(new Event('input', { bubbles: true }));
      const dispatchMs = performance.now() - started;
      requestAnimationFrame(() => requestAnimationFrame(() => resolve({
        available: true,
        value: input.value,
        dispatchMs,
        frameMs: performance.now() - started,
        domNodes: document.querySelectorAll('*').length,
        processRows: document.querySelectorAll('.process-result-row').length,
      })));
    })`,
  );
}

async function clickProcessRefresh(page) {
  return await evaluate(
    page,
    `new Promise((resolve) => {
      const button = document.querySelector('[data-smoke-action="refresh-processes"]');
      const started = performance.now();
      button.click();
      const poll = () => {
        const state = document.querySelector('[data-smoke-state="process-refresh"]');
        if (state) {
          requestAnimationFrame(() => requestAnimationFrame(() => resolve({
            elapsedMs: performance.now() - started,
            domNodes: document.querySelectorAll('*').length,
            processRows: document.querySelectorAll('.process-result-row').length,
            processTotal: Number(document.querySelector('.process-result-list')?.getAttribute('data-process-total') || document.querySelectorAll('.process-result-row').length),
          })));
          return;
        }
        setTimeout(poll, 20);
      };
      poll();
    })`,
  );
}

async function scrollProcessList(page) {
  return await evaluate(
    page,
    `new Promise((resolve) => {
      const list = document.querySelector('.process-result-list');
      if (!list) {
        resolve({ available: false });
        return;
      }
      const started = performance.now();
      list.scrollTop = Math.max(0, list.scrollHeight - list.clientHeight);
      list.dispatchEvent(new Event('scroll', { bubbles: true }));
      requestAnimationFrame(() => requestAnimationFrame(() => {
        const rows = Array.from(document.querySelectorAll('.process-result-row strong')).map((el) => el.textContent || '');
        resolve({
          available: true,
          elapsedMs: performance.now() - started,
          scrollTop: Math.round(list.scrollTop),
          scrollHeight: Math.round(list.scrollHeight),
          clientHeight: Math.round(list.clientHeight),
          renderedRows: rows.length,
          firstRendered: rows[0] || '',
          lastRendered: rows[rows.length - 1] || '',
          domNodes: document.querySelectorAll('*').length,
        });
      }));
    })`,
  );
}

async function runPerfScenario(page, baseUrl, outputDir, runIndex, scenario) {
  const url = pageUrl(baseUrl, scenario.params || {});
  const nav = await navigateApp(page, url, scenario.pageId);
  if (scenario.waitExpression) await waitForExpression(page, scenario.waitExpression, 15000);
  const initial = await collectDomMetrics(page, `${scenario.name}-initial`);
  const screenshot = await captureScreenshot(page, outputDir, `${scenario.name}-run${runIndex}`);
  const result = {
    run: runIndex,
    scenario: scenario.name,
    rowCountKind: scenario.rowCountKind,
    url,
    nav,
    initial,
    screenshot,
  };

  if (scenario.processInteractions) {
    result.scrollAll = await scrollProcessList(page);
    result.afterScrollAll = await collectDomMetrics(page, `${scenario.name}-after-scroll-all`);
    result.input = await setProcessSearchInput(page, scenario.inputValue || "FixtureProcess-2");
    result.refreshFiltered = await clickProcessRefresh(page);
    result.afterFiltered = await collectDomMetrics(page, `${scenario.name}-after-filter`);
  }

  return result;
}

async function runSmoke(page, baseUrl, outputDir) {
  const smoke = { target: {}, settings: {}, reports: {} };

  await navigateApp(page, pageUrl(baseUrl), "targets");
  await waitForExpression(page, "document.querySelectorAll('.target-list__row').length > 0", 15000);
  smoke.target = await evaluate(
    page,
    `new Promise((resolve) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      const click = (selector) => document.querySelector(selector).click();
      const setInput = (selector, value) => {
        const input = document.querySelector(selector);
        setter.call(input, value);
        input.dispatchEvent(new Event('input', { bubbles: true }));
      };
      const startRows = document.querySelectorAll('.target-list__row').length;
      click('[data-smoke-action="add-target"]');
      setTimeout(() => {
        const addedIndex = startRows;
        setInput('[data-smoke-field="target-name-' + addedIndex + '"]', 'P2 Synthetic Target');
        setInput('[data-smoke-field="target-process-' + addedIndex + '"]', 'P2Synthetic.exe');
        click('[data-smoke-action="done-target-' + addedIndex + '"]');
        setTimeout(() => {
          click('[data-smoke-action="edit-target-0"]');
          setTimeout(() => {
            setInput('[data-smoke-field="target-name-0"]', 'P2 Edited Target');
            click('[data-smoke-action="done-target-0"]');
            setTimeout(() => {
              click('[data-smoke-action="delete-target-1"]');
              setTimeout(() => {
                click('[data-smoke-action="save-targets"]');
                setTimeout(() => {
                  resolve({
                    startRows,
                    finalRows: document.querySelectorAll('.target-list__row').length,
                    addVisible: document.body.textContent.includes('P2 Synthetic Target'),
                    editVisible: document.body.textContent.includes('P2 Edited Target'),
                    saveDisabled: document.querySelector('[data-smoke-action="save-targets"]').disabled,
                    domNodes: document.querySelectorAll('*').length,
                  });
                }, 700);
              }, 60);
            }, 60);
          }, 60);
        }, 60);
      }, 80);
    })`,
  );
  smoke.target.screenshot = await captureScreenshot(page, outputDir, "smoke-targets");

  await navigateApp(page, pageUrl(baseUrl), "settings");
  await waitForExpression(page, "!!document.querySelector('[data-smoke-field=\"global-telemetry-sample-interval\"]')", 15000);
  smoke.settings = await evaluate(
    page,
    `new Promise((resolve) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      const input = document.querySelector('[data-smoke-field="global-telemetry-sample-interval"]');
      setter.call(input, '1375');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      document.querySelector('[data-smoke-action="save-config"]').click();
      const started = performance.now();
      const poll = () => {
        const state = document.querySelector('[data-smoke-state="settings-config"]');
        if ((state && state.textContent.includes('Config saved.')) || performance.now() - started > 3000) {
          resolve({
            saved: !!state && state.textContent.includes('Config saved.'),
            value: input.value,
            domNodes: document.querySelectorAll('*').length,
          });
          return;
        }
        setTimeout(poll, 40);
      };
      poll();
    })`,
  );
  smoke.settings.screenshot = await captureScreenshot(page, outputDir, "smoke-settings");

  await navigateApp(page, pageUrl(baseUrl), "reports");
  await waitForExpression(page, "document.querySelectorAll('.report-list-row').length > 0", 15000);
  smoke.reports = await evaluate(
    page,
    `new Promise((resolve) => {
      const click = (selector) => document.querySelector(selector).click();
      const startRows = document.querySelectorAll('.report-list-row').length;
      click('[data-smoke-action="refresh-reports"]');
      setTimeout(() => {
        click('[data-smoke-action="open-report-0"]');
        setTimeout(() => {
          click('[data-smoke-action="open-directory-0"]');
          setTimeout(() => {
            click('[data-smoke-action="regenerate-report-0"]');
            setTimeout(() => {
              resolve({
                startRows,
                finalRows: document.querySelectorAll('.report-list-row').length,
                openStatusVisible: document.body.textContent.includes('opened') || document.body.textContent.includes('open'),
                regenerateStatusVisible: document.body.textContent.includes('regenerate') || document.body.textContent.includes('completed'),
                domNodes: document.querySelectorAll('*').length,
              });
            }, 900);
          }, 220);
        }, 220);
      }, 220);
    })`,
  );
  smoke.reports.screenshot = await captureScreenshot(page, outputDir, "smoke-reports");

  smoke.success =
    smoke.target.finalRows === smoke.target.startRows &&
    smoke.target.addVisible &&
    smoke.target.editVisible &&
    smoke.target.saveDisabled &&
    smoke.settings.saved &&
    smoke.reports.finalRows > 0;
  return smoke;
}

async function waitMs(ms) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

async function clickSelector(page, selector) {
  await evaluate(
    page,
    `(() => {
      const element = document.querySelector(${JSON.stringify(selector)});
      if (!element) throw new Error('Missing selector: ${selector}');
      element.click();
      return true;
    })()`,
  );
}

async function setInputValue(page, selector, value) {
  await evaluate(
    page,
    `(() => {
      const input = document.querySelector(${JSON.stringify(selector)});
      if (!input) throw new Error('Missing input: ${selector}');
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      setter.call(input, ${JSON.stringify(value)});
      input.dispatchEvent(new Event('input', { bubbles: true }));
      return input.value;
    })()`,
  );
}

async function runSmokeStable(page, baseUrl, outputDir) {
  const smoke = { target: {}, settings: {}, reports: {} };

  await navigateApp(page, pageUrl(baseUrl), "targets");
  await waitForExpression(page, "document.querySelectorAll('.target-list__row').length > 0", 15000);
  const startRows = await evaluate(page, "document.querySelectorAll('.target-list__row').length");
  await clickSelector(page, '[data-smoke-action="add-target"]');
  await waitMs(120);
  await setInputValue(page, `[data-smoke-field="target-name-${startRows}"]`, "P2 Synthetic Target");
  await setInputValue(page, `[data-smoke-field="target-process-${startRows}"]`, "P2Synthetic.exe");
  await clickSelector(page, `[data-smoke-action="done-target-${startRows}"]`);
  await waitMs(120);
  await clickSelector(page, '[data-smoke-action="edit-target-0"]');
  await waitMs(120);
  await setInputValue(page, '[data-smoke-field="target-name-0"]', "P2 Edited Target");
  await clickSelector(page, '[data-smoke-action="done-target-0"]');
  await waitMs(120);
  await clickSelector(page, '[data-smoke-action="edit-target-1"]');
  await waitMs(120);
  await clickSelector(page, '[data-smoke-action="delete-target-1"]');
  await waitMs(120);
  await clickSelector(page, '[data-smoke-action="save-targets"]');
  await waitMs(900);
  smoke.target = await evaluate(
    page,
    `(() => ({
      startRows: ${JSON.stringify(startRows)},
      finalRows: document.querySelectorAll('.target-list__row').length,
      addVisible: document.body.textContent.includes('P2 Synthetic Target'),
      editVisible: document.body.textContent.includes('P2 Edited Target'),
      saveDisabled: document.querySelector('[data-smoke-action="save-targets"]').disabled,
      domNodes: document.querySelectorAll('*').length,
    }))()`,
  );
  smoke.target.screenshot = await captureScreenshot(page, outputDir, "smoke-targets");

  await navigateApp(page, pageUrl(baseUrl), "settings");
  await waitForExpression(page, "!!document.querySelector('[data-smoke-field=\"global-telemetry-sample-interval\"]')", 15000);
  await setInputValue(page, '[data-smoke-field="global-telemetry-sample-interval"]', "1375");
  await clickSelector(page, '[data-smoke-action="save-config"]');
  await waitForExpression(
    page,
    "!!document.querySelector('[data-smoke-state=\"settings-config\"]') && document.querySelector('[data-smoke-state=\"settings-config\"]').textContent.includes('Config saved.')",
    5000,
  );
  smoke.settings = await evaluate(
    page,
    `(() => ({
      saved: document.querySelector('[data-smoke-state="settings-config"]').textContent.includes('Config saved.'),
      value: document.querySelector('[data-smoke-field="global-telemetry-sample-interval"]').value,
      domNodes: document.querySelectorAll('*').length,
    }))()`,
  );
  smoke.settings.screenshot = await captureScreenshot(page, outputDir, "smoke-settings");

  await navigateApp(page, pageUrl(baseUrl), "reports");
  await waitForExpression(page, "document.querySelectorAll('.report-list-row').length > 0", 15000);
  const reportStartRows = await evaluate(page, "document.querySelectorAll('.report-list-row').length");
  await clickSelector(page, '[data-smoke-action="refresh-reports"]');
  await waitMs(500);
  await clickSelector(page, '[data-smoke-action="open-report-0"]');
  await waitMs(350);
  await clickSelector(page, '[data-smoke-action="open-directory-0"]');
  await waitMs(350);
  await clickSelector(page, '[data-smoke-action="regenerate-report-0"]');
  await waitMs(1000);
  smoke.reports = await evaluate(
    page,
    `(() => ({
      startRows: ${JSON.stringify(reportStartRows)},
      finalRows: document.querySelectorAll('.report-list-row').length,
      operationStatusVisible: document.querySelectorAll('.inline-status').length > 0,
      domNodes: document.querySelectorAll('*').length,
    }))()`,
  );
  smoke.reports.screenshot = await captureScreenshot(page, outputDir, "smoke-reports");

  smoke.success =
    smoke.target.finalRows === smoke.target.startRows &&
    smoke.target.addVisible &&
    smoke.target.editVisible &&
    smoke.target.saveDisabled &&
    smoke.settings.saved &&
    smoke.reports.finalRows > 0 &&
    smoke.reports.operationStatusVisible;
  return smoke;
}

async function main() {
  const repoRoot = path.resolve(__dirname, "..");
  const outputDir = path.resolve(readArg("--out", path.join(repoRoot, "artifacts", "frontend-large-list-probe")));
  const label = readArg("--label", "probe");
  const runs = numberArg("--runs", 2);
  const edgePath = readArg("--edge", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe");
  if (!fs.existsSync(edgePath)) throw new Error(`Edge executable not found: ${edgePath}`);
  fs.mkdirSync(outputDir, { recursive: true });

  const vitePort = await reservePort();
  const cdpPort = await reservePort();
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "framescope-frontend-list-probe-"));
  const vite = startVite(repoRoot, vitePort);
  const edge = spawn(edgePath, [
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars=false",
    "--enable-precise-memory-info",
    `--remote-debugging-port=${cdpPort}`,
    `--user-data-dir=${userDataDir}`,
    "about:blank",
  ], {
    stdio: ["ignore", "pipe", "pipe"],
  });
  const viteOutput = [];
  vite.stdout.on("data", (chunk) => viteOutput.push(chunk.toString()));
  vite.stderr.on("data", (chunk) => viteOutput.push(chunk.toString()));

  let page = null;
  try {
    const baseUrl = `http://127.0.0.1:${vitePort}/`;
    await waitForHttp(baseUrl, 20000);
    await waitForJson(`http://127.0.0.1:${cdpPort}/json/version`, 15000);
    const wsUrl = await openTarget(cdpPort, "about:blank");
    page = await connectCdp(wsUrl);
    await page.send("Page.enable");
    await page.send("Runtime.enable");
    await page.send("Performance.enable");
    await page.send("Emulation.setDeviceMetricsOverride", {
      width: 1280,
      height: 720,
      deviceScaleFactor: 1,
      mobile: false,
    });

    const scenarios = [
      {
        name: "reports-normal",
        pageId: "reports",
        rowCountKind: "normal-report-list",
        waitExpression: "document.querySelectorAll('.report-list-row').length > 0",
      },
      {
        name: "targets-small-process",
        pageId: "targets",
        rowCountKind: "small-process-list",
        waitExpression: "document.querySelectorAll('.target-list__row').length > 0",
        processInteractions: true,
        inputValue: "VALORANT",
      },
      {
        name: "targets-large-process-250",
        pageId: "targets",
        rowCountKind: "large-process-list",
        params: { visualFixture: "many-results" },
        waitExpression: "document.querySelectorAll('.process-result-row').length > 0",
        processInteractions: true,
        inputValue: "FixtureProcess-2",
      },
    ];

    const results = [];
    for (let runIndex = 1; runIndex <= runs; runIndex += 1) {
      for (const scenario of scenarios) {
        results.push(await runPerfScenario(page, baseUrl, outputDir, runIndex, scenario));
      }
    }

    const smoke = hasArg("--include-smoke") ? await runSmokeStable(page, baseUrl, outputDir) : null;
    const output = {
      label,
      generatedAt: new Date().toISOString(),
      baseUrl,
      runs,
      results,
      smoke,
      viteOutput: viteOutput.join("").slice(-4000),
    };
    const outputPath = path.join(outputDir, `${label}-frontend-large-list-probe.json`);
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2));
    console.log(JSON.stringify({ output: outputPath, results: results.length, smokeSuccess: smoke ? smoke.success : null }, null, 2));
    if (smoke && !smoke.success) process.exitCode = 2;
  } finally {
    if (page) page.close();
    edge.kill();
    await killProcessTree(vite);
    try {
      fs.rmSync(userDataDir, { recursive: true, force: true });
    } catch {
      // ignored
    }
  }
}

main().catch((error) => {
  console.error(error && error.stack ? error.stack : String(error));
  process.exit(1);
});
