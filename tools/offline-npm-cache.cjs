const fs = require("node:fs");
const path = require("node:path");
const { pipeline } = require("node:stream/promises");

async function main() {
  const [mode, npmModule, cache, archiveRoot] = process.argv.slice(2);
  const cacacheModule = require.resolve("cacache", { paths: [npmModule] });
  const cacache = require(cacacheModule);
  const packages = JSON.parse(fs.readFileSync(0, "utf8"));
  const results = [];
  for (const entry of packages) {
    if (mode === "inspect") {
      const content = await cacache.get.hasContent(cache, entry.integrity);
      const contentPath = require(path.join(path.dirname(cacacheModule), "content/path.js"));
      results.push(content ? { path: contentPath(cache, content.sri), size: content.size } : null);
    } else if (mode === "restore") {
      const source = path.resolve(archiveRoot, entry.path);
      if (!source.startsWith(path.resolve(archiveRoot) + path.sep)) {
        throw new Error("npm archive path escaped dependency root");
      }
      await pipeline(fs.createReadStream(source), cacache.put.stream(
        cache, `pacote:tarball:${entry.resolved}`, { integrity: entry.integrity }
      ));
      results.push({ integrity: entry.integrity, restored: true });
    } else {
      throw new Error("Expected inspect or restore");
    }
  }
  process.stdout.write(JSON.stringify(results));
}

main().catch(error => {
  console.error(error.message);
  process.exitCode = 1;
});
