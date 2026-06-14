const fs = require("fs");
const os = require("os");
const path = require("path");
const net = require("net");
const { spawn, spawnSync } = require("child_process");

function readArg(name, fallback = "") {
  const index = process.argv.indexOf(name);
  return index >= 0 && index < process.argv.length - 1 ? process.argv[index + 1] : fallback;
}

function hasArg(name) {
  return process.argv.includes(name);
}

function stringifyJsonForPowerShell(value) {
  return JSON.stringify(value, null, 2).replace(/[\u007f-\uffff]/g, (char) => {
    const code = char.charCodeAt(0);
    return `\\u${code.toString(16).padStart(4, "0")}`;
  });
}

function quotePowerShellString(value) {
  return `'${String(value).replace(/'/g, "''")}'`;
}

function assertPowerShellJsonEvidence(jsonPath, allowOverflow) {
  const command = [
    "$ErrorActionPreference='Stop'",
    `$jsonPath=${quotePowerShellString(jsonPath)}`,
    "$probe=Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json",
    "if(-not $probe){ throw 'Probe JSON parsed to an empty value.' }",
    "if($probe.allNoOverflow -ne $true -and -not $allowOverflow){ throw 'Probe JSON allNoOverflow is not true.' }",
    "foreach($item in $probe.results){",
    "  if(-not $item.screenshot){ throw ('Missing screenshot path for scenario: ' + $item.label) }",
    "  $shot=Get-Item -LiteralPath $item.screenshot -ErrorAction Stop",
    "  if($shot.Length -le 0){ throw ('Empty screenshot file: ' + $item.screenshot) }",
    "}",
    "$true",
  ]
    .join("; ")
    .replace("$allowOverflow", allowOverflow ? "$true" : "$false");

  const result = spawnSync("powershell.exe", ["-NoProfile", "-Command", command], {
    encoding: "utf8",
    windowsHide: true,
  });

  if (result.status !== 0) {
    const message = [result.stdout, result.stderr].filter(Boolean).join("\n").trim();
    throw new Error(`PowerShell JSON evidence validation failed for ${jsonPath}: ${message}`);
  }
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
      "document.readyState === 'complete' && !!window.FRAMESCOPE_DATA && !!document.getElementById('chartBox')",
    );
    if (ready) return;
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error("Report page did not become ready.");
}

async function runScenario(page, scenario, outputDir) {
  await page.send("Emulation.setDeviceMetricsOverride", {
    width: scenario.width,
    height: scenario.height,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await page.send("Page.navigate", { url: fileUrl(scenario.report) });
  await waitForReady(page);

  if (scenario.view) {
    await evaluate(
      page,
      `(function(){var tab=document.querySelector('[data-view="${scenario.view}"]'); if(tab){tab.click();} return true;})()`,
    );
    await new Promise((resolve) => setTimeout(resolve, 350));
  }

  if (scenario.metricValue) {
    await evaluate(
      page,
      `(function(){var select=document.getElementById('metricSelect'); if(select){select.value=${JSON.stringify(
        scenario.metricValue,
      )}; select.dispatchEvent(new Event('change',{bubbles:true}));} return true;})()`,
    );
    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  if (scenario.view && !scenario.name.startsWith("report-menu")) {
    await evaluate(
      page,
      `(function(){var shell=document.querySelector('.chart-shell'); if(shell){shell.scrollIntoView({block:'start'});} return true;})()`,
    );
    await new Promise((resolve) => setTimeout(resolve, 200));
  }

  if (scenario.hover) {
    const point = await evaluate(
      page,
      `(() => {
        const box = document.getElementById('chartBox');
        if (!box) return null;
        const rect = box.getBoundingClientRect();
        return { x: Math.round(rect.left + rect.width * 0.52), y: Math.round(rect.top + rect.height * 0.52) };
      })()`,
    );
    if (point) {
      await page.send("Input.dispatchMouseEvent", {
        type: "mouseMoved",
        x: point.x,
        y: point.y,
      });
      await new Promise((resolve) => setTimeout(resolve, 350));
    }
  }

  const metrics = await evaluate(
    page,
    `(() => {
      const doc = document.documentElement;
      const body = document.body;
      const pageScrollWidth = Math.max(doc.scrollWidth || 0, body ? body.scrollWidth || 0 : 0);
      const clientWidth = doc.clientWidth || window.innerWidth;
      const chartScroll = document.getElementById('chartScroll');
      const tooltip = document.getElementById('tooltip');
      const tooltipStyle = tooltip ? window.getComputedStyle(tooltip) : null;
      const box = (selector) => {
        const el = document.querySelector(selector);
        if (!el) return null;
        const rect = el.getBoundingClientRect();
        return {
          scrollWidth: el.scrollWidth,
          clientWidth: el.clientWidth,
          offsetWidth: el.offsetWidth,
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          right: Math.round(rect.right),
          bottom: Math.round(rect.bottom),
          width: Math.round(rect.width),
          height: Math.round(rect.height),
        };
      };
      const metricSelect = document.getElementById('metricSelect');
      return {
        label: ${JSON.stringify(scenario.name)},
        viewport: { width: window.innerWidth, height: window.innerHeight },
        scrollWidth: pageScrollWidth,
        clientWidth,
        overflow: pageScrollWidth > clientWidth,
        chartScrollOverflowX: chartScroll ? chartScroll.scrollWidth > chartScroll.clientWidth : false,
        title: document.title,
        viewTitle: document.getElementById('viewTitle') ? document.getElementById('viewTitle').textContent : '',
        viewNote: document.getElementById('viewNote') ? document.getElementById('viewNote').textContent : '',
        target: window.FRAMESCOPE_DATA ? window.FRAMESCOPE_DATA.target : null,
        reportMenu: box('.tabs'),
        toolbar: box('.toolbar'),
        leftTools: box('.left-tools'),
        rightTools: box('.right-tools'),
        legend: box('#legend'),
        chartScroll: box('#chartScroll'),
        chartBox: box('#chartBox'),
        gauges: box('#gauges'),
        tooltip: box('#tooltip'),
        tooltipVisible: !!tooltipStyle && Number(tooltipStyle.opacity) > 0.5,
        tooltipText: tooltip ? tooltip.textContent : '',
        fpsDropdownOptions: metricSelect ? Array.from(metricSelect.options).map((option) => option.value + ':' + option.textContent) : [],
        fpsOptionPass: metricSelect ? ['all','avg','low1','low01'].every((value) => Array.from(metricSelect.options).some((option) => option.value === value)) : false,
        noMinInstantOption: metricSelect ? !Array.from(metricSelect.options).some((option) => /min|instant|最低瞬时/i.test(option.textContent || '')) : true,
      };
    })()`,
  );

  const screenshot = await page.send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  const screenshotPath = path.join(outputDir, `${scenario.name}.png`);
  fs.writeFileSync(screenshotPath, Buffer.from(screenshot.data, "base64"));
  metrics.screenshot = screenshotPath;
  return metrics;
}

async function main() {
  const report = readArg("--report");
  const diagnostic = readArg("--diagnostic", report);
  const outputDir = path.resolve(readArg("--out", path.join("artifacts", "report-overflow-probe")));
  const edgePath = readArg("--edge", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe");
  if (!report || !fs.existsSync(report)) throw new Error(`Report HTML not found: ${report}`);
  if (!diagnostic || !fs.existsSync(diagnostic)) throw new Error(`Diagnostic HTML not found: ${diagnostic}`);
  if (!fs.existsSync(edgePath)) throw new Error(`Edge executable not found: ${edgePath}`);
  fs.mkdirSync(outputDir, { recursive: true });

  const port = await reservePort();
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "framescope-report-probe-"));
  const edge = spawn(edgePath, [
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars=false",
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${userDataDir}`,
    "about:blank",
  ], { stdio: ["ignore", "pipe", "pipe"] });

  let page = null;
  try {
    await waitForJson(`http://127.0.0.1:${port}/json/version`, 15000);
    const wsUrl = await openTarget(port, "about:blank");
    page = await connectCdp(wsUrl);
    await page.send("Page.enable");
    await page.send("Runtime.enable");

    const scenarios = [
      { name: "report-menu-1280x720", report, width: 1280, height: 720, view: "fps" },
      { name: "fps-default-1280x720", report, width: 1280, height: 720, view: "fps" },
      { name: "fps-dropdown-control-1280x720", report, width: 1280, height: 720, view: "fps", metricValue: "all" },
      { name: "fps-tooltip-1280x720", report, width: 1280, height: 720, view: "fps", metricValue: "all", hover: true },
      { name: "cpu-core-frequency-1280x720", report, width: 1280, height: 720, view: "cpuCore" },
      { name: "cpu-voltage-1280x720", report, width: 1280, height: 720, view: "cpuVoltage" },
      { name: "cpu-core-vid-1280x720", report, width: 1280, height: 720, view: "cpuVid" },
      { name: "performance-chart-1280x720", report, width: 1280, height: 720, view: "perf" },
      { name: "system-usage-1280x720", report, width: 1280, height: 720, view: "system" },
      { name: "background-process-1280x720", report, width: 1280, height: 720, view: "process" },
      { name: "io-disk-net-1280x720", report, width: 1280, height: 720, view: "io", metricValue: "diskNet" },
      { name: "io-temperature-1280x720", report, width: 1280, height: 720, view: "io", metricValue: "temp" },
      { name: "diagnostic-report-1280x720", report: diagnostic, width: 1280, height: 720, view: "fps" },
      { name: "fps-default-900x760", report, width: 900, height: 760, view: "fps" },
      { name: "cpu-core-frequency-900x760", report, width: 900, height: 760, view: "cpuCore" },
      { name: "cpu-voltage-900x760", report, width: 900, height: 760, view: "cpuVoltage" },
      { name: "cpu-core-vid-900x760", report, width: 900, height: 760, view: "cpuVid" },
      { name: "performance-chart-900x760", report, width: 900, height: 760, view: "perf" },
      { name: "system-usage-900x760", report, width: 900, height: 760, view: "system" },
      { name: "background-process-900x760", report, width: 900, height: 760, view: "process" },
      { name: "io-disk-net-900x760", report, width: 900, height: 760, view: "io", metricValue: "diskNet" },
      { name: "io-temperature-900x760", report, width: 900, height: 760, view: "io", metricValue: "temp" },
      { name: "diagnostic-report-900x760", report: diagnostic, width: 900, height: 760, view: "fps" },
    ];

    const results = [];
    for (const scenario of scenarios) {
      results.push(await runScenario(page, scenario, outputDir));
    }

    const summary = {
      generatedAt: new Date().toISOString(),
      report,
      diagnostic,
      allNoOverflow: results.every((item) => item.scrollWidth <= item.clientWidth && !item.chartScrollOverflowX),
      results,
    };
    const jsonPath = path.join(outputDir, "report-overflow-probe.json");
    fs.writeFileSync(jsonPath, stringifyJsonForPowerShell(summary), "utf8");
    assertPowerShellJsonEvidence(jsonPath, hasArg("--allow-overflow"));
    process.stdout.write(`${jsonPath}\n`);

    if (!summary.allNoOverflow && !hasArg("--allow-overflow")) process.exitCode = 2;
  } finally {
    if (page) page.close();
    edge.kill();
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
