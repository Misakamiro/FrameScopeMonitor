import { build } from "esbuild";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const result = await build({
  absWorkingDir: root,
  entryPoints: ["src/report/offline.tsx"],
  bundle: true,
  write: false,
  outfile: "report.js",
  format: "iife",
  platform: "browser",
  target: "es2020",
  minify: true,
  jsx: "automatic",
  jsxImportSource: "react",
  define: { "process.env.NODE_ENV": '"production"' },
  metafile: true,
});
const script = result.outputFiles
  .find((f) => f.path.endsWith(".js"))
  .text.replace(/<\/script/gi, "<\\/script");
const css = result.outputFiles.find((f) => f.path.endsWith(".css")).text;
const html = `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>FrameScope 报告</title><style>${css}</style><body><div id="root"></div><script src="framescope-interactive-data.js"></script><script>${script}</script></body></html>`;
await mkdir(path.join(root, "report-dist"), { recursive: true });
await writeFile(path.join(root, "report-dist/report.html"), html);
await writeFile(
  path.join(root, "report-dist/inputs.json"),
  JSON.stringify(Object.keys(result.metafile.inputs), null, 2),
);
console.log(`Shared offline report: ${Buffer.byteLength(html)} bytes`);
