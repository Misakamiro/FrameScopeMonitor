import path from "node:path";
import { realpathSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import { mkdir, writeFile } from "node:fs/promises";
import { parseArgs } from "node:util";
import { ESLint } from "eslint";
import { build } from "vite";

const relativePath = (root, file) => path.relative(root, file).replaceAll("\\", "/");
const isCode = file => /\.(?:[cm]?[jt]s|[jt]sx)$/.test(file);

export async function collectDeliveryGraph(root) {
  root = realpathSync.native(root);
  const files = new Set();
  const chunks = [];
  await build({
    root,
    logLevel: "error",
    build: { write: false },
    plugins: [{
      name: "framescope-lint-delivery-graph",
      generateBundle(_options, bundle) {
        for (const chunk of Object.values(bundle)) {
          if (chunk.type !== "chunk") continue;
          const modules = [];
          for (const [id, module] of Object.entries(chunk.modules)) {
            if (id.startsWith("\0") || module.renderedLength === 0) continue;
            const file = relativePath(root, id.split("?")[0]);
            if (file.startsWith("../") || path.isAbsolute(file)
              || file.includes("node_modules/") || !isCode(file)) continue;
            files.add(file);
            modules.push(file);
          }
          chunks.push({ file: chunk.fileName, isEntry: chunk.isEntry, modules: modules.sort() });
        }
      },
    }],
  });
  if (!files.size) throw new Error("No delivered source modules found in Vite output");
  return { files: [...files].sort(), chunks };
}

export function partitionResults(results, deliveryFiles, root) {
  const delivery = new Set(deliveryFiles);
  const checked = new Set(results.map(result => relativePath(root, result.filePath)));
  const missing = deliveryFiles.filter(file => !checked.has(file));
  if (missing.length) throw new Error(`Delivered modules missing from lint: ${missing.join(", ")}`);
  const groups = { delivery: [], retained: [], support: [] };
  for (const result of results) {
    const file = relativePath(root, result.filePath);
    const group = delivery.has(file) ? "delivery"
      : file.startsWith("src/") && !/\.(?:test|spec|browser)\.[cm]?[jt]sx?$/.test(file)
        ? "retained" : "support";
    groups[group].push(result);
  }
  return groups;
}

function summarize(results) {
  const byRule = {};
  let errors = 0;
  let warnings = 0;
  let fatalErrors = 0;
  for (const result of results) {
    errors += result.errorCount;
    warnings += result.warningCount;
    fatalErrors += result.fatalErrorCount;
    for (const message of result.messages) {
      const rule = message.ruleId || (message.fatal ? "parse-error" : "unused-disable");
      byRule[rule] ??= { errors: 0, warnings: 0 };
      byRule[rule][message.severity === 2 ? "errors" : "warnings"]++;
    }
  }
  return { files: results.length, errors, warnings, fatalErrors, byRule };
}

async function main() {
  const { values } = parseArgs({
    options: {
      scope: { type: "string", default: "all" },
      output: { type: "string" },
    },
  });
  if (!["all", "delivery", "retained", "support"].includes(values.scope)) {
    throw new Error("scope must be all, delivery, retained or support");
  }
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const graph = await collectDeliveryGraph(root);
  const eslint = new ESLint({ cwd: root });
  // Scan the same full set as `eslint .`; partitioning never suppresses findings.
  const results = await eslint.lintFiles(["."]);
  const groups = partitionResults(results, graph.files, root);
  const summary = Object.fromEntries(Object.entries(groups).map(([name, items]) => [name, summarize(items)]));
  const full = summarize(results);
  const report = {
    generatedAt: new Date().toISOString(),
    definition: "delivery = local JS/TS modules with nonzero renderedLength in fresh Vite output chunks; retained = remaining src production files; support = tests, tooling and installer",
    graph, full, summary,
    groups: Object.fromEntries(Object.entries(groups).map(([name, items]) => [name, items.map(result => ({
      file: relativePath(root, result.filePath), errorCount: result.errorCount,
      warningCount: result.warningCount, fatalErrorCount: result.fatalErrorCount, messages: result.messages,
    }))])),
  };
  if (values.output) {
    const output = path.resolve(values.output);
    await mkdir(path.dirname(output), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + "\n");
  }
  console.table(Object.fromEntries(Object.entries({ ...summary, full }).map(([name, counts]) =>
    [name, { files: counts.files, errors: counts.errors, warnings: counts.warnings, fatalErrors: counts.fatalErrors }])));
  const selected = values.scope === "all" ? results : groups[values.scope];
  const formatter = await eslint.loadFormatter("stylish");
  console.log(formatter.format(selected));
  if (selected.some(result => result.errorCount > 0)) process.exitCode = 1;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  main().catch(error => { console.error(error); process.exitCode = 1; });
}
