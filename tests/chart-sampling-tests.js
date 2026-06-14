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
const end = source.indexOf("function draw(){", start);
assert(start > 0 && end > start, "embedded chart sampling functions not found");

let view = "fps";
let currentTimes = [];
let currentSeries = [];
let renderCache = new Map();
let readMode = { value: "spike" };
let fpsMetric = "all";
let perfMetric = "all";
let systemMetric = "all";
let ioMetric = "diskNet";
let processMetric = "cpu";
let cpuCoreMetric = "summary";
let cpuVidMetric = "summary";
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

view = "perf";
readMode.value = "spike";
currentTimes = [0, 1, 2, 3, 4, 5];
currentSeries = [{ key: "perf:cpu", name: "CPU frequency", data: [4200, null, 4300, null, 4400, 4500] }];
renderCache = new Map();
const gapSample = getRenderablePoints(currentSeries[0], 6, { start: 0, end: 5, span: 5, full: { start: 0, end: 5, span: 5 } });
assert(gapSample.y.includes(null), "downsample should keep null gaps for invalid telemetry points");
assert(!gapSample.y.includes(0), "downsample should not turn invalid telemetry null gaps into zero-value spikes");

function countFiniteSameXJumps(points) {
  let count = 0;
  for (let i = 1; i < points.y.length; i++) {
    if (
      Number(points.t[i]) === Number(points.t[i - 1]) &&
      Number.isFinite(Number(points.y[i])) &&
      Number.isFinite(Number(points.y[i - 1])) &&
      Number(points.y[i]) !== Number(points.y[i - 1])
    ) {
      count++;
    }
  }
  return count;
}

function screenXForTime(t, range, pixelWidth) {
  return Math.round(((Number(t) - range.start) / range.span) * pixelWidth);
}

function countFiniteSameScreenXJumps(points, range, pixelWidth) {
  let count = 0;
  for (let i = 1; i < points.y.length; i++) {
    if (
      screenXForTime(points.t[i], range, pixelWidth) === screenXForTime(points.t[i - 1], range, pixelWidth) &&
      Number.isFinite(Number(points.y[i])) &&
      Number.isFinite(Number(points.y[i - 1])) &&
      Number(points.y[i]) !== Number(points.y[i - 1])
    ) {
      count++;
    }
  }
  return count;
}

function countDuplicateAdjacentScreenX(points, range, pixelWidth) {
  let count = 0;
  for (let i = 1; i < points.y.length; i++) {
    if (screenXForTime(points.t[i], range, pixelWidth) === screenXForTime(points.t[i - 1], range, pixelWidth)) count++;
  }
  return count;
}

view = "cpuVoltage";
readMode.value = "spike";
currentTimes = Array.from({ length: 180 }, (_, i) => i);
const stableVoltageWithShortInvalids = Array.from({ length: 180 }, (_, i) => 1.08 + (i % 5) * 0.003);
stableVoltageWithShortInvalids[41] = null;
stableVoltageWithShortInvalids[97] = null;
currentSeries = [{ key: "cpuVoltage:vcore", name: "CPU Voltage / Vcore", data: stableVoltageWithShortInvalids }];
renderCache = new Map();
const stableVoltageRange = {
  start: 0,
  end: 179,
  span: 179,
  full: { start: 0, end: 179, span: 179 },
};
const stableVoltageSample = getRenderablePoints(currentSeries[0], 90, {
  start: 0,
  end: 179,
  span: 179,
  full: { start: 0, end: 179, span: 179 },
});
assert.strictEqual(
  countFiniteSameXJumps(stableVoltageSample),
  0,
  "stable Vcore downsample should not create same-x min/max vertical artifacts",
);
assert.strictEqual(
  countFiniteSameScreenXJumps(stableVoltageSample, stableVoltageRange, 90),
  0,
  "stable Vcore render points should not create same-screen-x vertical artifacts",
);
assert.strictEqual(
  countDuplicateAdjacentScreenX(stableVoltageSample, stableVoltageRange, 90),
  0,
  "stable Vcore render points should be compacted to one draw point per adjacent screen x",
);
assert(
  stableVoltageSample.y.filter((v) => v === null).length <= 2,
  "isolated invalid Vcore samples should not create repeated vertical fill cuts",
);

view = "cpuVoltage";
readMode.value = "spike";
currentTimes = Array.from({ length: 946 }, (_, i) => i);
const denseVcore = Array.from({ length: 946 }, (_, i) => 1.08 + Math.sin(i / 12) * 0.018 + (i % 7) * 0.001);
currentSeries = [{ key: "cpuVoltage:vcore-dense", name: "CPU Voltage / Vcore", data: denseVcore }];
renderCache = new Map();
const denseVcoreRange = {
  start: 0,
  end: 945,
  span: 945,
  full: { start: 0, end: 945, span: 945 },
};
const denseVcoreSample = getRenderablePoints(currentSeries[0], 533, denseVcoreRange);
assert.strictEqual(
  countFiniteSameScreenXJumps(denseVcoreSample, denseVcoreRange, 533),
  0,
  "dense Vcore data should be screen-space compacted before line/area drawing",
);
assert.strictEqual(
  countDuplicateAdjacentScreenX(denseVcoreSample, denseVcoreRange, 533),
  0,
  "dense Vcore data should not produce adjacent duplicate screen x render points",
);

const voltageWithLongGap = Array.from({ length: 180 }, () => 1.08);
for (let i = 70; i < 84; i++) voltageWithLongGap[i] = null;
currentSeries = [{ key: "cpuVoltage:vcore-gap", name: "CPU Voltage / Vcore", data: voltageWithLongGap }];
renderCache = new Map();
const longGapVoltageSample = getRenderablePoints(currentSeries[0], 90, {
  start: 0,
  end: 179,
  span: 179,
  full: { start: 0, end: 179, span: 179 },
});
assert(longGapVoltageSample.y.includes(null), "long invalid Vcore gaps should still break the rendered segment");

const amdLhmRejectedReason =
  "AMD LibreHardwareMonitor Core VID samples were rejected because this sensor source does not match the validated CPU Voltage / Vcore reading on this system; CPU Voltage / Vcore remains separate and is not used as VID.";
const expectedAmdLhmNoDataMessage =
  "\u8fd9\u4e0d\u662f\u8f6f\u4ef6\u6f0f\u753b\u56fe\u3002\u5f53\u524d\u786c\u4ef6\u7684 AMD LibreHardwareMonitor Core VID \u6765\u6e90\u4e0d\u53ef\u4fe1\uff0c\u5df2\u505c\u6b62\u663e\u793a\u8be5\u9519\u8bef VID\uff08\u7ea6 0.5V\uff09\u3002CPU Voltage / Vcore \u4ecd\u53ef\u5728 CPU \u7535\u538b / Vcore \u56fe\u8868\u4e2d\u67e5\u770b\uff1bVcore \u4e0d\u4f1a\u5192\u5145 VID\u3002\u672a\u6765\u68c0\u6d4b\u5230\u5408\u6cd5 VID \u6765\u6e90\u65f6\uff0cCPU Core VID \u56fe\u8868\u4f1a\u6b63\u5e38\u663e\u793a\u3002";

DATA = {
  fps: { t: [], avg: [], low1: [], low01: [] },
  cpuCore: { t: [], series: [] },
  cpuVoltage: {
    available: true,
    t: [0, 1],
    series: [{ key: "cpu-voltage:vcore", name: "CPU Voltage / Vcore", data: [1.074, 1.092] }],
  },
  cpuVid: {
    available: false,
    status: "unavailable",
    reason: amdLhmRejectedReason,
    source: "builtin-librehardwaremonitor",
    rejectedSampleCount: 496,
    t: [],
    series: [],
  },
  system: { t: [], usage: {}, perf: {}, io: {} },
  process: { t: [], names: [], cpu: [], mem: [] },
  frameStats: {},
  notes: {},
};
view = "cpuVid";
cpuVidMetric = "summary";
assert.strictEqual(
  emptyMessage(),
  expectedAmdLhmNoDataMessage,
  "AMD LibreHardwareMonitor rejected CPU VID should show the clear Chinese no-data reason",
);
const emptyMessageMeasureContext = { measureText: (text) => ({ width: Array.from(String(text)).length * 8 }) };
const emptyMessageWrappedLines = emptyMessageLines(expectedAmdLhmNoDataMessage, 360, emptyMessageMeasureContext);
assert(emptyMessageWrappedLines.length > 1, "long CPU VID no-data reason should be wrapped across multiple canvas lines");
assert(
  emptyMessageWrappedLines.every((line) => emptyMessageMeasureContext.measureText(line).width <= 360),
  "each wrapped CPU VID no-data line should fit inside the available canvas width",
);
buildSeries();
assert.strictEqual(currentSeries.length, 0, "unavailable CPU VID should still avoid drawing rejected AMD LHM series");
assert(source.includes("data-view='cpuVid'"), "CPU Core VID tab should still exist when CPU VID data is unavailable");

view = "cpuVoltage";
buildSeries();
assert.strictEqual(currentSeries.length, 1, "CPU Voltage / Vcore should still render from DATA.cpuVoltage");
assert.strictEqual(currentSeries[0].key, "cpu-voltage:vcore", "Vcore should stay in the CPU Voltage chart");
assert.strictEqual(currentSeries[0].data[0], 1.074, "Vcore sample should not be copied through DATA.cpuVid");

DATA.cpuVid = {
  available: true,
  status: "core-vid-available",
  reason: "",
  note: "VID is CPU request/target voltage, not real Vcore.",
  t: [0, 1],
  series: [{ key: "cpu-vid:0", name: "Core #1 VID", color: "#009dff", data: [1.112, 1.12] }],
};
view = "cpuVid";
cpuVidMetric = "summary";
buildSeries();
assert.strictEqual(currentSeries.length, 3, "valid CPU VID fixture should still display summary series");
assert.deepStrictEqual(currentSeries[0].data, [1.112, 1.12], "valid CPU VID values should still reach the rendered series");

view = "process";
readMode.value = "spike";
currentTimes = Array.from({ length: 1000 }, (_, i) => i);
const sparseProcess = Array.from({ length: 1000 }, () => 0);
for (const [idx, value] of [
  [120, 1.2],
  [350, 5.5],
  [351, 3.2],
  [700, 2.4],
]) {
  sparseProcess[idx] = value;
}
currentSeries = [{ key: "process:cpu:0", name: "Sparse process", data: sparseProcess }];
renderCache = new Map();
const sparseProcessSample = getRenderablePoints(currentSeries[0], 180, {
  start: 0,
  end: 999,
  span: 999,
  full: { start: 0, end: 999, span: 999 },
});
assert(sparseProcessSample.y.includes(5.5), "process sparse spike downsample should preserve the true peak");
assert.strictEqual(
  countFiniteSameXJumps(sparseProcessSample),
  0,
  "process sparse spike downsample should not turn low occupancy into same-x needle artifacts",
);
assert.strictEqual(
  countFiniteSameScreenXJumps(sparseProcessSample, { start: 0, end: 999, span: 999 }, 180),
  0,
  "process sparse spike drawing should not contain same-screen-x vertical segments",
);

view = "process";
readMode.value = "spike";
currentTimes = Array.from({ length: 951 }, (_, i) => i);
const denseProcess = Array.from({ length: 951 }, () => 0);
for (const [idx, value] of [
  [120, 1.2],
  [350, 5.5],
  [351, 3.2],
  [514, 7.9],
  [515, 0.3],
  [700, 2.4],
]) {
  denseProcess[idx] = value;
}
currentSeries = [{ key: "process:cpu:0", name: "Dense process", data: denseProcess }];
renderCache = new Map();
const denseProcessRange = {
  start: 0,
  end: 950,
  span: 950,
  full: { start: 0, end: 950, span: 950 },
};
const denseProcessSample = getRenderablePoints(currentSeries[0], 328, denseProcessRange);
assert(denseProcessSample.y.includes(7.9), "process screen compaction should preserve the visible peak value");
assert.strictEqual(
  countFiniteSameScreenXJumps(denseProcessSample, denseProcessRange, 328),
  0,
  "dense process series should be screen-space compacted before line drawing",
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
assert(source.includes("if(view==='process')return;"), "background process chart should avoid filled area needles by drawing lines only");
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
assert(source.includes("最大值："), "fps legend should include localized maximum label");
assert(source.includes("平均值："), "fps legend should include localized average label");
assert(source.includes("最小值："), "fps legend should include localized minimum label");
assert(source.includes("样本数"), "fps tooltip should include localized bucket sample count");
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
assert(source.includes("CPU 电压 / Vcore"), "CPU Voltage chart should use an explicit localized Vcore title/note");
assert(source.includes("data-view='cpuVid'"), "report should expose CPU Core VID chart");
assert(!source.includes("tab-disabled' data-view='cpuVid'"), "CPU Core VID chart tab should not be disabled");
assert(source.includes("CPU 核心 VID（请求电压）"), "CPU VID chart should use an explicit localized VID title");
assert(source.includes("DATA.cpuVid"), "CPU VID chart should read DATA.cpuVid instead of CPU voltage series");
assert(source.includes("cpuVidMetric"), "CPU VID chart should keep its own metric selection state");
assert(source.includes("request/target") || source.includes("请求/目标"), "CPU VID chart note should describe VID as request/target voltage");
assert(source.includes("bucketMs=Number(fps.bucketMs)||1000"), "fps tooltip should keep reading the English bucketMs machine key");
assert(source.includes("平均 FPS / 1% Low / 0.1% Low"), "FPS metric dropdown should be localized");
assert(source.includes("后台进程 CPU"), "process metric dropdown should be localized");
assert(source.includes("前 ${PROCESS_TOP_N} 个进程"), "process Top N note should be localized");
assert(source.includes("性能频率"), "performance chart title should be localized");
assert(source.includes("无可绘制数据"), "empty chart state should be localized");
assert(source.includes("开始："), "run start label should be localized");
assert(source.includes("结束："), "run end label should be localized");
assert(source.includes("时长："), "run duration label should be localized");
assert(source.includes("平均 / 最高 / 最低核心频率"), "CPU core summary option should be localized");
assert(source.includes("平均 / 最高 / 最低 VID"), "CPU VID summary option should be localized");
assert(source.includes("GamePP 的 CPU 电压指标"), "CPU voltage note should localize the visible GamePP voltage wording");
assert(source.includes("最大帧时间"), "frame summary labels should be localized");
assert(source.includes("VID 是 CPU 每核心请求/目标电压"), "CPU VID chart note should explicitly separate VID from Vcore");
assert(!source.includes("Average FPS only"), "report should not expose English FPS metric labels");
assert(!source.includes("Background process CPU"), "report should not expose English process metric labels");
assert(!source.includes("No drawable data for this chart."), "report should not expose English empty chart labels");
assert(!source.includes("FPS data unavailable"), "report should not expose English FPS unavailable title");
assert(!source.includes("Sample count:"), "report should not expose English FPS tooltip sample label");
assert(!source.includes("Performance clocks"), "report should not expose English performance chart title");
assert(!source.includes("System usage"), "report should not expose English system chart title");
assert(!source.includes("绘制 Top ${PROCESS_TOP_N} 进程"), "report should not expose English Top N wording");
assert(!source.includes("GamePP CPU Voltage"), "report should not expose English CPU Voltage wording in the visible note");
assert(source.includes("class='chart-shell'"), "report chart should be wrapped in rounded dashboard surface");
assert(source.includes("border-radius:18px"), "report UI should use rounded dashboard cards and chart surfaces");
assert(source.includes("min-width:min(900px,100%)"), "chart box should shrink to the viewport before the user expands the width slider");
assert(source.includes("Math.max(minWidth,Math.round(base*scale))"), "default chart width should avoid narrow-viewport horizontal overflow");
assert(source.includes("canvasRoundRect(c,42,130,1516,820,26)"), "exported PNG should use rounded chart framing");

console.log("chart-sampling-tests: PASS");
