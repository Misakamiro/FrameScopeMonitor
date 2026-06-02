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
    await waitMs(150);
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
    await waitMs(150);
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
    await waitMs(80);
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

async function setMotionMode(page, mode) {
  await page.send("Emulation.setEmulatedMedia", {
    features: [
      {
        name: "prefers-reduced-motion",
        value: mode === "reduced" ? "reduce" : "no-preference",
      },
    ],
  });
}

async function navigateApp(page, url, pageId) {
  await page.send("Page.navigate", { url });
  await waitForExpression(page, "document.readyState === 'complete' && !!document.querySelector('[data-smoke-nav]')", 15000);
  const metricsBefore = await perfMetrics(page);
  const nav = await evaluate(
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
  const metricsAfter = await perfMetrics(page);
  return { ...nav, metricDelta: diffMetrics(metricsBefore, metricsAfter) };
}

async function perfMetrics(page) {
  const metrics = await page.send("Performance.getMetrics");
  const map = {};
  for (const metric of metrics.metrics || []) map[metric.name] = metric.value;
  return {
    timestamp: numberOrNull(map.Timestamp),
    documents: numberOrNull(map.Documents),
    nodes: numberOrNull(map.Nodes),
    jsEventListeners: numberOrNull(map.JSEventListeners),
    layoutCount: numberOrNull(map.LayoutCount),
    recalcStyleCount: numberOrNull(map.RecalcStyleCount),
    layoutDurationMs: secondsToMs(map.LayoutDuration),
    recalcStyleDurationMs: secondsToMs(map.RecalcStyleDuration),
    scriptDurationMs: secondsToMs(map.ScriptDuration),
    taskDurationMs: secondsToMs(map.TaskDuration),
    jsHeapUsedMb: bytesToMb(map.JSHeapUsedSize),
    jsHeapTotalMb: bytesToMb(map.JSHeapTotalSize),
  };
}

function numberOrNull(value) {
  return Number.isFinite(value) ? Number(value.toFixed(3)) : null;
}

function secondsToMs(value) {
  return Number.isFinite(value) ? Number((value * 1000).toFixed(3)) : null;
}

function bytesToMb(value) {
  return Number.isFinite(value) ? Number((value / 1024 / 1024).toFixed(3)) : null;
}

function diffMetrics(before, after) {
  const delta = {};
  for (const key of Object.keys(after)) {
    if (typeof after[key] === "number" && typeof before[key] === "number") {
      delta[key] = Number((after[key] - before[key]).toFixed(3));
    }
  }
  return delta;
}

async function collectMotionInventory(page, label) {
  const inventory = await evaluate(
    page,
    `(() => {
      const parseTimeList = (value) => String(value || "").split(",").map((item) => {
        const text = item.trim();
        if (text.endsWith("ms")) return Number(text.slice(0, -2)) || 0;
        if (text.endsWith("s")) return (Number(text.slice(0, -1)) || 0) * 1000;
        return Number(text) || 0;
      });
      const hasPositiveTime = (value) => parseTimeList(value).some((time) => time > 0);
      const includesProp = (value, prop) => {
        const props = String(value || "").split(",").map((item) => item.trim());
        return props.includes("all") || props.includes(prop);
      };
      const sample = (el) => {
        const className = typeof el.className === "string" ? el.className : "";
        return {
          tag: el.tagName.toLowerCase(),
          className: className.slice(0, 120),
          text: (el.textContent || "").trim().replace(/\\s+/g, " ").slice(0, 80),
        };
      };
      const elements = Array.from(document.querySelectorAll("*"));
      const counts = {
        elements: elements.length,
        transitioned: 0,
        transitionAll: 0,
        boxShadowTransitioned: 0,
        transformTransitioned: 0,
        opacityTransitioned: 0,
        backgroundTransitioned: 0,
        animated: 0,
        infiniteAnimated: 0,
        filterActive: 0,
        backdropFilterActive: 0,
        blurActive: 0,
        boxShadowActive: 0,
        largeRowHoverCandidates: document.querySelectorAll(".process-result-row, .report-list-row, .target-list__row").length,
        framerInlineTransform: 0,
      };
      const samples = {
        transitionAll: [],
        boxShadowTransitioned: [],
        animated: [],
        filters: [],
        framerInlineTransform: [],
      };
      for (const el of elements) {
        const style = getComputedStyle(el);
        const transitionActive = hasPositiveTime(style.transitionDuration) && style.transitionProperty !== "none";
        if (transitionActive) {
          counts.transitioned += 1;
          if (includesProp(style.transitionProperty, "all")) {
            counts.transitionAll += 1;
            if (samples.transitionAll.length < 8) samples.transitionAll.push(sample(el));
          }
          if (includesProp(style.transitionProperty, "box-shadow")) {
            counts.boxShadowTransitioned += 1;
            if (samples.boxShadowTransitioned.length < 8) samples.boxShadowTransitioned.push(sample(el));
          }
          if (includesProp(style.transitionProperty, "transform")) counts.transformTransitioned += 1;
          if (includesProp(style.transitionProperty, "opacity")) counts.opacityTransitioned += 1;
          if (includesProp(style.transitionProperty, "background-color")) counts.backgroundTransitioned += 1;
        }
        if (style.animationName !== "none" && hasPositiveTime(style.animationDuration)) {
          counts.animated += 1;
          if (style.animationIterationCount === "infinite") counts.infiniteAnimated += 1;
          if (samples.animated.length < 8) {
            samples.animated.push({ ...sample(el), animationName: style.animationName, duration: style.animationDuration });
          }
        }
        const filter = style.filter || "none";
        const backdropFilter = style.backdropFilter || style.webkitBackdropFilter || "none";
        if (filter !== "none") counts.filterActive += 1;
        if (backdropFilter !== "none") counts.backdropFilterActive += 1;
        if (filter.includes("blur(") || backdropFilter.includes("blur(")) counts.blurActive += 1;
        if (filter !== "none" || backdropFilter !== "none") {
          if (samples.filters.length < 8) samples.filters.push({ ...sample(el), filter, backdropFilter });
        }
        if (style.boxShadow !== "none") counts.boxShadowActive += 1;
        const inlineTransform = el.getAttribute("style") || "";
        if (/transform\\s*:/.test(inlineTransform)) {
          counts.framerInlineTransform += 1;
          if (samples.framerInlineTransform.length < 8) samples.framerInlineTransform.push({ ...sample(el), style: inlineTransform.slice(0, 160) });
        }
      }
      const pageEl = document.querySelector("[data-smoke-page]");
      return {
        label: ${JSON.stringify(label)},
        page: pageEl ? pageEl.getAttribute("data-smoke-page") : "",
        reduceMotion: matchMedia("(prefers-reduced-motion: reduce)").matches,
        counts,
        samples,
        pageTransitionTag: document.querySelector(".page-transition") ? document.querySelector(".page-transition").tagName.toLowerCase() : "",
        pageTransitionStyle: document.querySelector(".page-transition") ? getComputedStyle(document.querySelector(".page-transition")).cssText : "",
      };
    })()`,
  );
  return { ...inventory, metrics: await perfMetrics(page) };
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

async function clickAndMeasure(page, selector, label) {
  const before = await perfMetrics(page);
  const result = await evaluate(
    page,
    `new Promise((resolve) => {
      const element = document.querySelector(${JSON.stringify(selector)});
      if (!element) {
        resolve({ label: ${JSON.stringify(label)}, available: false });
        return;
      }
      const started = performance.now();
      element.click();
      requestAnimationFrame(() => requestAnimationFrame(() => resolve({
        label: ${JSON.stringify(label)},
        available: true,
        elapsedMs: performance.now() - started,
      })));
    })`,
  );
  const after = await perfMetrics(page);
  return { ...result, metricDelta: diffMetrics(before, after), inventory: await collectMotionInventory(page, `${label}-after-click`) };
}

async function setInputValue(page, selector, value) {
  const missingMessage = `Missing input: ${selector}`;
  await evaluate(
    page,
    `(() => {
      const input = document.querySelector(${JSON.stringify(selector)});
      if (!input) throw new Error(${JSON.stringify(missingMessage)});
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
      setter.call(input, ${JSON.stringify(value)});
      input.dispatchEvent(new Event("input", { bubbles: true }));
      return input.value;
    })()`,
  );
}

async function clickSelector(page, selector) {
  const missingMessage = `Missing selector: ${selector}`;
  await evaluate(
    page,
    `(() => {
      const element = document.querySelector(${JSON.stringify(selector)});
      if (!element) throw new Error(${JSON.stringify(missingMessage)});
      element.click();
      return true;
    })()`,
  );
}

async function waitMs(ms) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

async function runSmoke(page, baseUrl, outputDir) {
  const smoke = { target: {}, settings: {}, reports: {} };

  await navigateApp(page, pageUrl(baseUrl), "targets");
  await waitForExpression(page, "document.querySelectorAll('.target-list__row').length > 0", 15000);
  const startRows = await evaluate(page, "document.querySelectorAll('.target-list__row').length");
  await clickSelector(page, '[data-smoke-action="add-target"]');
  await waitMs(120);
  await setInputValue(page, `[data-smoke-field="target-name-${startRows}"]`, "P2 Animation Target");
  await setInputValue(page, `[data-smoke-field="target-process-${startRows}"]`, "P2Animation.exe");
  await clickSelector(page, `[data-smoke-action="done-target-${startRows}"]`);
  await waitMs(120);
  await clickSelector(page, '[data-smoke-action="edit-target-0"]');
  await waitMs(120);
  await setInputValue(page, '[data-smoke-field="target-name-0"]', "P2 Animation Edited Target");
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
      finalRows: document.querySelectorAll(".target-list__row").length,
      addVisible: document.body.textContent.includes("P2 Animation Target"),
      editVisible: document.body.textContent.includes("P2 Animation Edited Target"),
      saveDisabled: document.querySelector('[data-smoke-action="save-targets"]').disabled,
      domNodes: document.querySelectorAll("*").length,
    }))()`,
  );
  smoke.target.screenshot = await captureScreenshot(page, outputDir, "smoke-targets");

  await navigateApp(page, pageUrl(baseUrl), "settings");
  await waitForExpression(page, '!!document.querySelector("[data-smoke-field=\\"global-telemetry-sample-interval\\"]")', 15000);
  await setInputValue(page, '[data-smoke-field="global-telemetry-sample-interval"]', "1375");
  await clickSelector(page, '[data-smoke-action="save-config"]');
  await waitForExpression(
    page,
    '!!document.querySelector("[data-smoke-state=\\"settings-config\\"]") && document.querySelector("[data-smoke-state=\\"settings-config\\"]").textContent.includes("Config saved.")',
    5000,
  );
  smoke.settings = await evaluate(
    page,
    `(() => ({
      saved: document.querySelector('[data-smoke-state="settings-config"]').textContent.includes("Config saved."),
      value: document.querySelector('[data-smoke-field="global-telemetry-sample-interval"]').value,
      domNodes: document.querySelectorAll("*").length,
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
      finalRows: document.querySelectorAll(".report-list-row").length,
      operationStatusVisible: document.querySelectorAll(".inline-status").length > 0,
      domNodes: document.querySelectorAll("*").length,
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

function staticScan(repoRoot) {
  const files = [
    "src/frontend/src/App.tsx",
    "src/frontend/src/main.tsx",
    "src/frontend/src/theme/motion.ts",
    "src/frontend/src/theme/tokens.css",
    "src/frontend/src/styles/global.css",
    "src/frontend/src/layout/PageTransition.tsx",
    "src/frontend/src/layout/layout.css",
    "src/frontend/src/components/Button.tsx",
    "src/frontend/src/components/ToolbarButton.tsx",
    "src/frontend/src/components/components.css",
    "src/frontend/src/pages/pages.css",
  ];
  const entries = [];
  const counts = {
    files: files.length,
    framerMotionImports: 0,
    motionElements: 0,
    whileTap: 0,
    motionConfig: 0,
    transitionAll: 0,
    transitionDeclarations: 0,
    animationDeclarations: 0,
    keyframes: 0,
    boxShadowTransitions: 0,
    transformTransitions: 0,
    opacityTransitions: 0,
    filterDeclarations: 0,
    backdropFilterDeclarations: 0,
    blurReferences: 0,
    prefersReducedMotionBlocks: 0,
  };
  for (const relative of files) {
    const absolute = path.join(repoRoot, relative);
    const text = fs.existsSync(absolute) ? fs.readFileSync(absolute, "utf8") : "";
    const lines = text.split(/\r?\n/);
    lines.forEach((line, index) => {
      const checks = [
        ["framerMotionImports", /from\s+["']framer-motion["']/],
        ["motionElements", /<motion\./],
        ["whileTap", /whileTap=/],
        ["motionConfig", /MotionConfig/],
        ["transitionAll", /transition\s*:\s*all|transition-property\s*:\s*all/],
        ["transitionDeclarations", /\btransition\s*:/],
        ["animationDeclarations", /\banimation\s*:/],
        ["keyframes", /@keyframes/],
        ["boxShadowTransitions", /transition[\s\S]*box-shadow|box-shadow[\s\S]*var\(--fs-motion/],
        ["transformTransitions", /transition[\s\S]*transform|transform[\s\S]*var\(--fs-motion/],
        ["opacityTransitions", /transition[\s\S]*opacity|opacity[\s\S]*var\(--fs-motion/],
        ["filterDeclarations", /\bfilter\s*:/],
        ["backdropFilterDeclarations", /backdrop-filter|-webkit-backdrop-filter/],
        ["blurReferences", /blur\(/],
        ["prefersReducedMotionBlocks", /prefers-reduced-motion\s*:\s*reduce/],
      ];
      for (const [name, pattern] of checks) {
        if (pattern.test(line)) {
          counts[name] += 1;
          entries.push({ type: name, file: relative, line: index + 1, text: line.trim().slice(0, 180) });
        }
      }
    });
  }
  return { counts, entries };
}

async function runScenario(page, baseUrl, outputDir, runIndex, mode, pageId) {
  const params = pageId === "targets" ? { visualFixture: "many-results" } : {};
  const nav = await navigateApp(page, pageUrl(baseUrl, params), pageId);
  const inventory = await collectMotionInventory(page, `${mode}-${pageId}-run${runIndex}`);
  const screenshot = runIndex === 1 ? await captureScreenshot(page, outputDir, `${mode}-${pageId}`) : null;
  return { run: runIndex, motionMode: mode, pageId, nav, inventory, screenshot };
}

async function runInteractions(page, baseUrl, mode) {
  const interactions = {};

  await navigateApp(page, pageUrl(baseUrl, { visualFixture: "many-results" }), "targets");
  interactions.targetsRefresh = await clickAndMeasure(page, '[data-smoke-action="refresh-processes"]', `${mode}-targets-refresh`);

  await navigateApp(page, pageUrl(baseUrl), "reports");
  await waitForExpression(page, "!!document.querySelector('.report-more-button')", 15000);
  interactions.reportsMenuOpen = await clickAndMeasure(page, ".report-more-button", `${mode}-reports-menu-open`);

  await navigateApp(page, pageUrl(baseUrl), "settings");
  await waitForExpression(page, '!!document.querySelector("[data-smoke-field=\\"global-telemetry-sample-interval\\"]")', 15000);
  await setInputValue(page, '[data-smoke-field="global-telemetry-sample-interval"]', "1425");
  interactions.settingsSave = await clickAndMeasure(page, '[data-smoke-action="save-config"]', `${mode}-settings-save`);

  return interactions;
}

async function main() {
  const repoRoot = path.resolve(__dirname, "..");
  const outputDir = path.resolve(readArg("--out", path.join(repoRoot, "artifacts", "frontend-ui-animation-probe")));
  const label = readArg("--label", "probe");
  const runs = numberArg("--runs", 2);
  const edgePath = readArg("--edge", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe");
  if (!fs.existsSync(edgePath)) throw new Error(`Edge executable not found: ${edgePath}`);
  fs.mkdirSync(outputDir, { recursive: true });

  const vitePort = await reservePort();
  const cdpPort = await reservePort();
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "framescope-ui-animation-probe-"));
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

    const staticResult = staticScan(repoRoot);
    const pages = ["overview", "targets", "reports", "settings"];
    const motionModes = ["ordinary", "reduced"];
    const results = [];
    const interactions = {};
    for (const mode of motionModes) {
      await setMotionMode(page, mode);
      interactions[mode] = [];
      for (let runIndex = 1; runIndex <= runs; runIndex += 1) {
        for (const pageId of pages) {
          results.push(await runScenario(page, baseUrl, outputDir, runIndex, mode, pageId));
        }
        interactions[mode].push(await runInteractions(page, baseUrl, mode));
      }
    }

    const smoke = hasArg("--include-smoke") ? await runSmoke(page, baseUrl, outputDir) : null;
    const output = {
      label,
      generatedAt: new Date().toISOString(),
      baseUrl,
      runs,
      staticScan: staticResult,
      results,
      interactions,
      smoke,
      viteOutput: viteOutput.join("").slice(-4000),
    };
    const outputPath = path.join(outputDir, `${label}-frontend-ui-animation-probe.json`);
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
