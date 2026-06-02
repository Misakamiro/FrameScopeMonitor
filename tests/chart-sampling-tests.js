const fs = require("fs");
const path = require("path");
const assert = require("assert");

const reportSourceDir = path.join(__dirname, "..", "src", "reporting");
const source = fs
  .readdirSync(reportSourceDir)
  .filter((name) => /^FrameScopeReportGenerator.*\.cs$/.test(name))
  .sort()
  .map((name) => fs.readFileSync(path.join(reportSourceDir, name), "utf8"))
  .join("\n");
const start = source.indexOf("function hms");
const end = source.indexOf("function syncVidTab", start);
assert(start > 0 && end > start, "embedded chart sampling functions not found");

let view = "fps";
let currentTimes = [];
let currentSeries = [];
let renderCache = new Map();
let readMode = { value: "spike" };
const PROCESS_TOP_N = 10;
const PAD = { l: 64, r: 24, t: 34, b: 46 };
let DATA = null;

eval(source.slice(start, end));

function runMode(mode, len = 10000, pixelWidth = 800) {
  readMode.value = mode;
  currentTimes = Array.from({ length: len }, (_, i) => i / 10);
  const data = Array.from({ length: len }, () => 60);
  data[1234] = 7;
  data[5678] = 240;
  currentSeries = [{ key: "fps", name: "FPS", data }];
  renderCache = new Map();
  return getRenderablePoints(currentSeries[0], pixelWidth);
}

const spike = runMode("spike");
const trend = runMode("trend");

assert(spike.y.length <= 700, `spike mode should reduce normal dense points sharply, got ${spike.y.length}`);
assert(trend.y.length <= 420, `trend mode should keep drawn points bounded, got ${trend.y.length}`);
assert(spike.y.includes(7), "spike mode should preserve low FPS drop");
assert(spike.y.includes(240), "spike mode should preserve high FPS peak");

const shortRange = { start: 0, end: 1.79, span: 1.79, full: { start: 0, end: 1.79, span: 1.79 } };
const shortTickLabels = timeTicks(shortRange, 812).map((tick) => formatTimeTick(tick, shortRange.span));
assert(shortTickLabels.length >= 3 && shortTickLabels.length <= 8, `short range tick count should be controlled, got ${shortTickLabels.length}`);
assert(shortTickLabels.every((label) => !/^0:0[01]$/.test(label)), `short range labels should not collapse to m:ss labels: ${shortTickLabels.join(", ")}`);
assert(new Set(shortTickLabels).size === shortTickLabels.length, `short range labels should be unique: ${shortTickLabels.join(", ")}`);

const gameppDurationRange = { start: 0, end: 122, span: 122, full: { start: 0, end: 122, span: 122 } };
assert.strictEqual(formatTimeTick(0, gameppDurationRange.span), "00:00:00", "fps chart should show a GamePP-style zero start time");
assert.strictEqual(formatTimeTick(122, gameppDurationRange.span), "00:02:02", "fps chart should show readable HH:MM:SS end time");

view = "process";
const processSpike = runMode("spike", 20000, 900);
assert(
  processSpike.y.length <= 900,
  `process spike mode should keep each process series bounded for large reports, got ${processSpike.y.length}`,
);

assert(source.includes("framescope-${view}-${readMode.value}.png"), "exported PNG name should include chart mode");
assert(source.includes("function decodeRleSeries"), "process chart should decode lossless RLE process series");
assert(source.includes("DATA.process.codec"), "process chart should branch on encoded process payloads");
assert(source.includes("chartBox.addEventListener('wheel',zoomChart,{passive:false})"), "chart should wire mouse wheel zoom");
assert(source.includes("chartBox.addEventListener('mousedown',startPan)"), "chart should wire drag pan start");
assert(source.includes("window.addEventListener('mouseup',endPan)"), "chart should end drag pan outside chart");
assert(source.includes("document.getElementById('resetView').addEventListener('click',resetTimeView)"), "chart should wire reset view button");
assert(!source.includes("<option value='raw'>"), "report should not expose raw dense mode");
assert(!source.includes("drawSpikeMarkers"), "fps chart should not draw mixed red/yellow spike markers");
assert(!source.includes("fpsDisplayDomainMax"), "fps chart should not use robust display-domain clipping");
assert(!source.includes("fpsAnomalyPoints"), "fps chart should not compute slow-frame red anomaly points");
assert(!source.includes("drawFpsAnomalyMarkers"), "fps chart should not draw red anomaly point overlays");
assert(!source.includes("红色异常帧点"), "fps chart should not label or tooltip red anomaly points");
assert(!source.includes("color='#ff4f78'"), "fps chart should not keep the red anomaly marker color path");
assert(source.includes("function drawFpsArea"), "fps chart should draw a blue filled area under the primary FPS line");
assert(source.includes("function drawFpsReferenceLines"), "fps chart should draw Min/Max/Average horizontal reference lines");
assert(source.includes("function drawGameppArea"), "non-FPS charts should use a shared GamePP area helper");
assert(source.includes("function drawGameppReferenceLines"), "non-FPS charts should draw shared dashed reference lines and right-side labels");
assert(source.includes("function fixedYAxisMax"), "percentage charts should support fixed Y-axis domains");
assert(source.includes("return view==='system'?100:null"), "system usage chart Y-axis should be fixed to 0-100 percent");
assert(source.includes("function aggregateCoreSeries"), "CPU core frequency and VID should support average/max/min summaries");
assert(source.includes("cpuCoreMetric='summary'"), "CPU core frequency should default to a readable summary instead of all core lines");
assert(source.includes("cpuVidMetric='summary'"), "CPU VID should default to a readable summary instead of all VID lines");
assert(source.includes("const PROCESS_TOP_N=10"), "background process chart should default to a bounded Top N line set");
assert(source.includes("processSearchIndex"), "process search should use a precomputed lower-case search index");
assert(!source.includes("encoded.split(';')"), "RLE decoder should stream tokens without allocating a split array");
assert(source.includes("function warmProcessTopSeries"), "process chart should prewarm reusable Top N decoded series");
assert(source.includes("currentSeries.length>PROCESS_TOP_N"), "process search rendering should use a bounded multi-match sampling profile");
assert.deepStrictEqual(decodeRleSeries("2*n;3*4.5;7;bad;2*8", 10), [null, null, 4.5, 4.5, 4.5, 7, NaN, 8, 8, null]);
assert.deepStrictEqual(decodeRleSeries("4*1;2", 3), [1, 1, 1], "RLE decoder should still truncate to expected length");
assert.deepStrictEqual(decodeRleSeries("2*1", 5), [1, 1, null, null, null], "RLE decoder should still pad missing samples with nulls");
assert(source.includes("ioMetric='diskNet'"), "IO chart should default to a single-unit disk/network view");
assert(!source.includes("ioMetric==='powerTemp'"), "IO chart should not combine GPU power and temperature on one mixed axis");
assert(source.includes("Max: "), "fps legend should include Max");
assert(source.includes("Average: "), "fps legend should include Average");
assert(source.includes("Min: "), "fps legend should include Min");
assert(source.includes("Sample count"), "fps tooltip should include bucket sample count");
assert(source.includes("<option value=all>"), "fps dropdown should expose combined average/low view");
assert(source.includes("<option value=avg>"), "fps dropdown should expose average-only view");
assert(source.includes("<option value=low1>"), "fps dropdown should expose 1% low-only view");
assert(source.includes("<option value=low01>"), "fps dropdown should expose 0.1% low-only view");
assert(!source.includes("<option value=min>"), "fps dropdown should not expose minimum instant FPS view");
assert(source.includes("DATA.fps.low1"), "fps chart should be able to render the 1% Low series");
assert(source.includes("DATA.fps.low01"), "fps chart should be able to render the 0.1% Low series");
assert(!source.includes("DATA.fps.min"), "fps chart should not read a minimum instant/anomaly marker series");
assert(source.includes("<button class='tab' data-view='cpuCore'"), "report should expose CPU core frequency chart");
assert(source.includes("data-view='cpuVoltage'"), "report should expose CPU Voltage / Vcore as its own chart tab");
assert(source.includes("DATA.cpuVoltage"), "CPU Voltage chart should read DATA.cpuVoltage instead of CPU Core VID");
assert(source.includes("CPU Voltage / Vcore"), "CPU Voltage chart should use an explicit Vcore title/note");
assert(source.includes("data-view='cpuVid'"), "report should expose CPU Core VID chart");
assert(!source.includes("tab-disabled' data-view='cpuVid'"), "CPU Core VID chart tab should not be disabled");
assert(source.includes("CPU Core VID") || source.includes("CPU 核心 VID"), "CPU VID chart should use an explicit VID title");
assert(source.includes("DATA.cpuVid"), "CPU VID chart should read DATA.cpuVid instead of CPU voltage series");
assert(source.includes("cpuVidMetric"), "CPU VID chart should keep its own metric selection state");
assert(source.includes("request/target") || source.includes("请求/目标"), "CPU VID chart note should describe VID as request/target voltage");
assert(source.includes("class='chart-shell'"), "report chart should be wrapped in rounded dashboard surface");
assert(source.includes("border-radius:18px"), "report UI should use rounded dashboard cards and chart surfaces");
assert(source.includes("min-width:min(900px,100%)"), "chart box should shrink to the viewport before the user expands the width slider");
assert(source.includes("Math.max(minWidth,Math.round(base*scale))"), "default chart width should avoid narrow-viewport horizontal overflow");
assert(source.includes("canvasRoundRect(c,42,130,1516,820,26)"), "exported PNG should use rounded chart framing");

console.log("chart-sampling-tests: PASS");
