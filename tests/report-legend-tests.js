const fs = require("fs");
const path = require("path");
const vm = require("vm");
const assert = require("assert");

const source = fs.readFileSync(path.join(__dirname, "../src/reporting/FrameScopeReportGenerator.Html.Scripts.cs"), "utf8");
const names = ["esc", "n", "finiteOrNull", "seriesStats", "unitDigits", "updateLegend"];
const functions = names.map(name => {
  const line = source.split(/\r?\n/).find(line => line.startsWith(`function ${name}(`));
  assert(line, `Missing production function: ${name}`);
  return line;
}).join("\n");
const context = vm.createContext({
  DATA: {}, view: "process", currentUnit: "%", legendKey: "", legend: { innerHTML: "" },
  currentSeries: [{ key: "browser", name: "browser", color: "#29e6ff", data: [null, ...Array(498).fill(0), 19.41, 1.54] }],
});
vm.runInContext(functions, context);
vm.runInContext("updateLegend()", context);
assert(context.legend.innerHTML.includes("平均 0.04"), "Small nonzero process CPU averages must remain visible");
assert(context.legend.innerHTML.includes("当前 1.5"), "Current CPU precision stays unchanged");
assert(context.legend.innerHTML.includes("峰值 19.4"), "Peak CPU precision stays unchanged");
assert.strictEqual(vm.runInContext("seriesStats(currentSeries[0]).avg", context), (19.41 + 1.54) / 500,
  "Measured zero samples must remain in the average, while missing samples are excluded");

function render(view, unit, data) {
  context.view = view;
  context.currentUnit = unit;
  context.legendKey = "";
  context.currentSeries[0].data = data;
  vm.runInContext("updateLegend()", context);
  return context.legend.innerHTML;
}
assert(render("process", "%", [0, 0]).includes("平均 0.00"), "Measured all-zero CPU stays zero");
assert(render("process", "%", [null, null]).includes("平均 无数据"), "Missing CPU stays unavailable");
assert(render("system", "%", [1.54]).includes("平均 1.5"), "System CPU precision stays unchanged");
assert(render("process", "MB", [123.456]).includes("平均 123.46"), "Process memory precision stays unchanged");
console.log("report-legend-tests: PASS");
