const fs = require("fs");
const os = require("os");
const path = require("path");
const net = require("net");
const { spawn } = require("child_process");

function readArg(name, fallback = "") {
  const index = process.argv.indexOf(name);
  return index >= 0 && index < process.argv.length - 1 ? process.argv[index + 1] : fallback;
}

function numberArg(name, fallback) {
  const value = Number(readArg(name, ""));
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function fileUrl(filePath) {
  return `file:///${path.resolve(filePath).replace(/\\/g, "/").replace(/ /g, "%20")}`;
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
  const result = await page.send("Runtime.evaluate", {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result ? result.result.value : null;
}

async function waitForReady(page) {
  const started = Date.now();
  while (Date.now() - started < 15000) {
    const ready = await evaluate(
      page,
      "document.readyState === 'complete' && !!window.FRAMESCOPE_DATA && typeof draw === 'function' && !!document.getElementById('chartBox')",
    );
    if (ready) return;
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error("Report page did not become ready.");
}

async function performanceMetrics(page) {
  const metrics = await page.send("Performance.getMetrics");
  const map = {};
  for (const metric of metrics.metrics || []) map[metric.name] = metric.value;
  return {
    jsHeapUsedMb: Number.isFinite(map.JSHeapUsedSize) ? Number((map.JSHeapUsedSize / 1024 / 1024).toFixed(3)) : null,
    jsHeapTotalMb: Number.isFinite(map.JSHeapTotalSize) ? Number((map.JSHeapTotalSize / 1024 / 1024).toFixed(3)) : null,
    nodes: Number.isFinite(map.Nodes) ? map.Nodes : null,
    documents: Number.isFinite(map.Documents) ? map.Documents : null,
  };
}

async function installInstrumentation(page) {
  await evaluate(
    page,
    `(() => {
      if (window.__framescopeProcessProbeInstalled) return true;
      window.__framescopeProcessProbeInstalled = true;
      window.__framescopeProcessProbe = {
        draws: [],
        hovers: [],
        decodes: [],
        searches: [],
        renderCalls: 0,
        visibleCalls: 0,
        reset(label) {
          this.label = label || '';
          this.draws = [];
          this.hovers = [];
          this.decodes = [];
          this.searches = [];
          this.renderCalls = 0;
          this.visibleCalls = 0;
        },
      };

      const originalDraw = draw;
      draw = function() {
        const started = performance.now();
        const beforeNodes = document.querySelectorAll('*').length;
        const beforeHeap = performance.memory ? performance.memory.usedJSHeapSize : null;
        try {
          return originalDraw.apply(this, arguments);
        } finally {
          const afterHeap = performance.memory ? performance.memory.usedJSHeapSize : null;
          window.__framescopeProcessProbe.draws.push({
            durationMs: performance.now() - started,
            view: typeof view === 'undefined' ? null : view,
            metric: typeof processMetric === 'undefined' ? null : processMetric,
            search: processSearch ? processSearch.value : '',
            raw: lastRenderStats ? lastRenderStats.raw : null,
            drawn: lastRenderStats ? lastRenderStats.drawn : null,
            buckets: lastRenderStats ? lastRenderStats.buckets : null,
            currentSeries: Array.isArray(currentSeries) ? currentSeries.length : null,
            domNodesBefore: beforeNodes,
            domNodesAfter: document.querySelectorAll('*').length,
            heapDeltaBytes: beforeHeap !== null && afterHeap !== null ? afterHeap - beforeHeap : null,
          });
        }
      };

      const originalHover = hover;
      hover = function(evt) {
        const started = performance.now();
        try {
          return originalHover.apply(this, arguments);
        } finally {
          window.__framescopeProcessProbe.hovers.push({
            durationMs: performance.now() - started,
            view: typeof view === 'undefined' ? null : view,
            tooltipVisible: Number(getComputedStyle(tooltip).opacity) > 0.5,
            tooltipLength: tooltip ? tooltip.textContent.length : 0,
          });
        }
      };

      const originalDecode = decodeRleSeries;
      decodeRleSeries = function(encoded, expectedLength) {
        const started = performance.now();
        const result = originalDecode.apply(this, arguments);
        window.__framescopeProcessProbe.decodes.push({
          durationMs: performance.now() - started,
          encodedLength: typeof encoded === 'string' ? encoded.length : 0,
          expectedLength,
          resultLength: Array.isArray(result) ? result.length : null,
        });
        return result;
      };

      const originalVisible = visibleProcessIndexes;
      visibleProcessIndexes = function() {
        const started = performance.now();
        const result = originalVisible.apply(this, arguments);
        window.__framescopeProcessProbe.visibleCalls += 1;
        window.__framescopeProcessProbe.searches.push({
          durationMs: performance.now() - started,
          query: processSearch ? processSearch.value : '',
          resultCount: Array.isArray(result) ? result.length : null,
          results: Array.isArray(result) ? result.slice(0, 30) : [],
        });
        return result;
      };

      const originalRenderable = getRenderablePoints;
      getRenderablePoints = function() {
        window.__framescopeProcessProbe.renderCalls += 1;
        return originalRenderable.apply(this, arguments);
      };
      return true;
    })()`,
  );
}

function waitForNextDrawExpression(actionExpression, queryLiteral = "''") {
  return `new Promise((resolve, reject) => {
      const started = performance.now();
      const timeout = setTimeout(() => reject(new Error('Timed out waiting for chart draw')), 4000);
      const dispatchStarted = performance.now();
      ${actionExpression}
      const dispatchSyncMs = performance.now() - dispatchStarted;
      const poll = () => {
        const draws = window.__framescopeProcessProbe.draws;
        if (draws.length) {
          clearTimeout(timeout);
          const search = window.__framescopeProcessProbe.searches[window.__framescopeProcessProbe.searches.length - 1] || null;
          resolve({ query: ${queryLiteral}, dispatchSyncMs, elapsedMs: performance.now() - started, draw: draws[draws.length - 1], search });
        } else {
          setTimeout(poll, 5);
        }
      };
      poll();
    })`;
}

async function runInteraction(page, runIndex) {
  await evaluate(page, "window.__framescopeProcessProbe.reset('tab'); true");
  const tabResult = await evaluate(
    page,
    waitForNextDrawExpression("document.querySelector('[data-view=\"process\"]').click();"),
  );
  await new Promise((resolve) => setTimeout(resolve, 120));

  const searches = [];
  for (const query of ["", "e", "a", "edge", "CODEX", "__NO_PROCESS_MATCH__"]) {
    await evaluate(page, "window.__framescopeProcessProbe.reset('search'); true");
    const result = await evaluate(
      page,
      waitForNextDrawExpression(
        `processSearch.value = ${JSON.stringify(query)};
        processSearch.dispatchEvent(new Event('input', { bubbles: true }));`,
        JSON.stringify(query),
      ),
    );
    result.names = await evaluate(
      page,
      `(() => {
        const idxs = visibleProcessIndexes();
        const names = (DATA.process.names || []);
        return idxs.map((idx) => names[idx]);
      })()`,
    );
    searches.push(result);
  }

  await evaluate(page, "processSearch.value = ''; processSearch.dispatchEvent(new Event('input', { bubbles: true })); true");
  await new Promise((resolve) => setTimeout(resolve, 80));
  await evaluate(page, "document.querySelector('.chart-shell').scrollIntoView({ block: 'start' }); true");
  await new Promise((resolve) => setTimeout(resolve, 120));
  await evaluate(page, "window.__framescopeProcessProbe.reset('hover'); true");
  const point = await evaluate(
    page,
    `(() => {
      const box = document.getElementById('chartBox');
      const rect = box.getBoundingClientRect();
      return { x: Math.round(rect.left + rect.width * 0.52), y: Math.round(rect.top + rect.height * 0.52) };
    })()`,
  );
  const hoverStarted = Date.now();
  await page.send("Input.dispatchMouseEvent", {
    type: "mouseMoved",
    x: point.x,
    y: point.y,
  });
  const hoverResult = await evaluate(
    page,
    `new Promise((resolve, reject) => {
      const started = performance.now();
      const timeout = setTimeout(() => reject(new Error('Timed out waiting for hover')), 4000);
      const poll = () => {
        const hovers = window.__framescopeProcessProbe.hovers;
        if (hovers.length) {
          clearTimeout(timeout);
          resolve({ elapsedMs: performance.now() - started, hover: hovers[hovers.length - 1] });
        }
        else setTimeout(poll, 5);
      };
      poll();
    })`,
  );
  hoverResult.wallMs = Date.now() - hoverStarted;

  const correctness = await evaluate(
    page,
    `(() => {
      const names = DATA.process.names || [];
      const stats = DATA.process.stats || [];
      const expectedTop = names.slice(0, PROCESS_TOP_N);
      const emptyTop = (() => {
        processSearch.value = '';
        return visibleProcessIndexes().map((idx) => names[idx]);
      })();
      const edgeLower = (() => {
        processSearch.value = 'edge';
        return visibleProcessIndexes().map((idx) => names[idx]);
      })();
      const edgeUpper = (() => {
        processSearch.value = 'EDGE';
        return visibleProcessIndexes().map((idx) => names[idx]);
      })();
      const noResult = (() => {
        processSearch.value = '__NO_PROCESS_MATCH__';
        return visibleProcessIndexes().map((idx) => names[idx]);
      })();
      processSearch.value = '';
      return {
        processNames: names.length,
        stats: stats.length,
        timeSamples: (DATA.process.t || []).length,
        cpuSeries: (DATA.process.cpu || []).length,
        memSeries: (DATA.process.mem || []).length,
        codec: DATA.process.codec,
        expectedTop,
        emptyTop,
        emptyTopMatches: JSON.stringify(expectedTop) === JSON.stringify(emptyTop),
        edgeMatchesExpected: edgeLower.every((name) => String(name).toLowerCase().includes('edge')),
        caseInsensitiveSame: JSON.stringify(edgeLower) === JSON.stringify(edgeUpper),
        noResultCount: noResult.length,
        fpsBucketMs: DATA.fps && DATA.fps.bucketMs,
        cpuVoltageSeries: DATA.cpuVoltage && DATA.cpuVoltage.series ? DATA.cpuVoltage.series.length : null,
        cpuVidSeries: DATA.cpuVid && DATA.cpuVid.series ? DATA.cpuVid.series.length : null,
        chartNonBlank: (() => {
          const canvas = document.getElementById('chart');
          const ctx = canvas.getContext('2d');
          const sample = ctx.getImageData(Math.floor(canvas.width / 2), Math.floor(canvas.height / 2), 1, 1).data;
          return sample[3] > 0;
        })(),
        tooltipVisible: Number(getComputedStyle(tooltip).opacity) > 0.5,
        tooltipTextLength: tooltip ? tooltip.textContent.length : 0,
        domNodes: document.querySelectorAll('*').length,
        processRows: document.querySelectorAll('#processRows .row').length,
      };
    })()`,
  );

  const counters = await evaluate(
    page,
    `(() => ({
      draws: window.__framescopeProcessProbe.draws,
      hovers: window.__framescopeProcessProbe.hovers,
      decodes: window.__framescopeProcessProbe.decodes,
      searches: window.__framescopeProcessProbe.searches,
      renderCalls: window.__framescopeProcessProbe.renderCalls,
      visibleCalls: window.__framescopeProcessProbe.visibleCalls,
      processSeriesCacheSize: typeof processSeriesCache === 'undefined' ? null : processSeriesCache.size,
      renderCacheSize: typeof renderCache === 'undefined' ? null : renderCache.size,
      hoverCacheSize: typeof processHoverCache === 'undefined' ? null : processHoverCache.size,
    }))()`,
  );

  return {
    run: runIndex,
    tab: tabResult,
    searches,
    hover: hoverResult,
    correctness,
    counters,
    perfMetrics: await performanceMetrics(page),
  };
}

function summarizeRun(run) {
  const searchMax = Math.max(...run.searches.map((item) => item.draw.durationMs));
  const searchMaxDispatch = Math.max(...run.searches.map((item) => item.dispatchSyncMs || 0));
  return {
    run: run.run,
    tabDrawMs: Number(run.tab.draw.durationMs.toFixed(3)),
    tabElapsedMs: Number(run.tab.elapsedMs.toFixed(3)),
    searchMaxDrawMs: Number(searchMax.toFixed(3)),
    searchMaxDispatchMs: Number(searchMaxDispatch.toFixed(3)),
    hoverMs: Number(run.hover.hover.durationMs.toFixed(3)),
    domNodes: run.correctness.domNodes,
    processNames: run.correctness.processNames,
    processSamples: run.correctness.timeSamples,
    jsHeapUsedMb: run.perfMetrics.jsHeapUsedMb,
    decodeCount: run.counters.decodes.length,
    renderCalls: run.counters.renderCalls,
  };
}

async function main() {
  const report = readArg("--report");
  const outputDir = path.resolve(readArg("--out", path.join("artifacts", "process-interaction-probe")));
  const label = readArg("--label", "probe");
  const runs = numberArg("--runs", 2);
  const edgePath = readArg("--edge", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe");
  if (!report || !fs.existsSync(report)) throw new Error(`Report HTML not found: ${report}`);
  if (!fs.existsSync(edgePath)) throw new Error(`Edge executable not found: ${edgePath}`);
  fs.mkdirSync(outputDir, { recursive: true });

  const port = await reservePort();
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "framescope-process-probe-"));
  const edge = spawn(edgePath, [
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars=false",
    "--enable-precise-memory-info",
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${userDataDir}`,
    "about:blank",
  ], {
    stdio: ["ignore", "pipe", "pipe"],
  });

  let page = null;
  try {
    await waitForJson(`http://127.0.0.1:${port}/json/version`, 10000);
    const wsUrl = await openTarget(port, fileUrl(report));
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
    await waitForReady(page);
    await installInstrumentation(page);

    const results = [];
    for (let i = 1; i <= runs; i += 1) {
      if (i > 1) {
        await page.send("Page.navigate", { url: fileUrl(report) });
        await waitForReady(page);
        await installInstrumentation(page);
      }
      results.push(await runInteraction(page, i));
    }

    const output = {
      label,
      report: path.resolve(report),
      generatedAt: new Date().toISOString(),
      runs: results,
      summary: results.map(summarizeRun),
    };
    const outputPath = path.join(outputDir, `${label}-process-interaction.json`);
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2));
    console.log(JSON.stringify({ output: outputPath, summary: output.summary }, null, 2));
  } finally {
    if (page) page.close();
    edge.kill();
  }
}

main().catch((error) => {
  console.error(error && error.stack ? error.stack : error);
  process.exitCode = 1;
});
