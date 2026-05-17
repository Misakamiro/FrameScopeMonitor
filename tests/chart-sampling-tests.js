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
const start = source.indexOf("function samplingProfile");
const end = source.indexOf("function updateLegend", start);
assert(start > 0 && end > start, "embedded chart sampling functions not found");

let view = "fps";
let currentTimes = [];
let currentSeries = [];
let renderCache = new Map();
let readMode = { value: "raw" };

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

const raw = runMode("raw");
const spike = runMode("spike");

assert.strictEqual(raw.rawCount, 10000, "raw mode should report full source count");
assert.strictEqual(raw.y.length, 10000, "raw mode should draw dense source points for this fixture");
assert(spike.y.length <= 700, `spike mode should reduce normal dense points sharply, got ${spike.y.length}`);
assert(spike.y.length < raw.y.length / 10, "spike mode should be visually distinct from raw mode");
assert(spike.y.includes(7), "spike mode should preserve low FPS drop");
assert(spike.y.includes(240), "spike mode should preserve high FPS peak");
assert.notDeepStrictEqual(spike.y, raw.y, "spike mode must not reuse raw y array");

assert(source.includes("framescope-${view}-${readMode.value}.png"), "exported PNG name should include chart mode");
assert(source.includes("chartBox.addEventListener('wheel',zoomChart,{passive:false})"), "chart should wire mouse wheel zoom");
assert(source.includes("chartBox.addEventListener('mousedown',startPan)"), "chart should wire drag pan start");
assert(source.includes("window.addEventListener('mouseup',endPan)"), "chart should end drag pan outside chart");
assert(source.includes("document.getElementById('resetView').addEventListener('click',resetTimeView)"), "chart should wire reset view button");
assert(source.includes("function drawSpikeMarkers"), "spike mode should draw explicit peak/drop markers");
assert(source.includes("class='chart-shell'"), "report chart should be wrapped in rounded dashboard surface");
assert(source.includes("border-radius:18px"), "report UI should use rounded dashboard cards and chart surfaces");
assert(source.includes("canvasRoundRect(c,42,130,1516,820,26)"), "exported PNG should use rounded chart framing");

console.log("chart-sampling-tests: PASS");
