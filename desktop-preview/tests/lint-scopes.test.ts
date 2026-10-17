import { afterEach, describe, expect, it } from "vitest";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { realpathSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { ESLint } from "eslint";

const temporaryRoots: string[] = [];
afterEach(async () => {
  for (const root of temporaryRoots.splice(0)) {
    expect(path.dirname(root)).toBe(realpathSync.native(tmpdir()));
    expect(path.basename(root)).toMatch(/^framescope-lint-scope-/);
    await rm(root, { recursive: true, force: true });
  }
});

describe("lint delivery boundaries", () => {
  it("uses emitted Vite modules, including dynamic imports but not unused upstream sources", async () => {
    const { collectDeliveryGraph, partitionResults } = await import("../scripts/lint-scopes.mjs");
    const root = realpathSync.native(await mkdtemp(path.join(tmpdir(), "framescope-lint-scope-")));
    temporaryRoots.push(root);
    await mkdir(path.join(root, "src"));
    await mkdir(path.join(root, "tests"));
    await mkdir(path.join(root, "dist"));
    const fixtures = {
      "index.html": '<script type="module" src="/src/main.js"></script>',
      "src/main.js": 'import { label } from "./active.js"; import { unused } from "./unused.js"; document.title = label; window.loadPanel = () => import("./lazy.js");',
      "src/active.js": 'export const label = "FrameScope";\nif (false) {}\n',
      "src/lazy.js": 'export const lazy = "Panel";\nif (false) {}\n',
      "src/upstream.js": "const = invalid syntax;",
      "src/unused.js": 'export const unused = "Not shipped";',
      "src/runtime.test.js": "if (false) {}\n",
      "tests/probe.js": "if (false) {}\n",
      "dist/preserved.txt": "Existing build must not be overwritten",
    };
    for (const [file, source] of Object.entries(fixtures)) {
      await writeFile(path.join(root, file), source);
    }
    const graph = await collectDeliveryGraph(root);
    expect(graph.files).toEqual(["src/active.js", "src/lazy.js", "src/main.js"]);
    expect(await readFile(path.join(root, "dist/preserved.txt"), "utf8"))
      .toBe("Existing build must not be overwritten");
    const eslint = new ESLint({
      cwd: root, overrideConfigFile: true,
      overrideConfig: [{ rules: { "no-empty": "error" } }],
    });
    const results = await eslint.lintFiles(["src/**/*.js", "tests/**/*.js"]);
    const groups = partitionResults(results, graph.files, root);
    expect(groups.delivery.map(result => path.basename(result.filePath)).sort())
      .toEqual(["active.js", "lazy.js", "main.js"]);
    expect(groups.retained.map(result => path.basename(result.filePath)).sort()).toEqual(["unused.js", "upstream.js"]);
    expect(groups.support.map(result => path.basename(result.filePath)).sort())
      .toEqual(["probe.js", "runtime.test.js"]);
    expect(groups.delivery.reduce((count, result) => count + result.errorCount, 0)).toBe(2);
    expect(groups.retained.reduce((count, result) => count + result.fatalErrorCount, 0)).toBe(1);
    expect(Object.values(groups).flat()).toHaveLength(results.length);
  });

  it("fails closed when a delivered file is omitted by lint configuration", async () => {
    const { partitionResults } = await import("../scripts/lint-scopes.mjs");
    expect(() => partitionResults([], ["src/main.tsx"], process.cwd()))
      .toThrow("Delivered modules missing from lint");
  });

  it("keeps the full npm lint command unchanged", async () => {
    const pkg = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
    expect(pkg.scripts.lint).toBe("eslint .");
  });
});
