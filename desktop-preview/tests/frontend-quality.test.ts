import { ESLint } from "eslint";
import { describe, expect, it } from "vitest";

describe("frontend lint quality gates", () => {
  const eslint = new ESLint();

  it("checks production TypeScript for unsafe syntax and hook ordering", async () => {
    const [result] = await eslint.lintText(
      'import { useState } from "react";\nexport function Probe({ active }: { active: boolean }) { if (active) useState(0); return <div />; }\nexport const loose: any = 1;\n',
      { filePath: "src/quality-probe.tsx" },
    );
    expect(result.messages.map(message => message.ruleId)).toEqual(
      expect.arrayContaining(["react-hooks/rules-of-hooks", "@typescript-eslint/no-explicit-any"]),
    );
    expect(result.fatalErrorCount).toBe(0);
  });

  it("checks JavaScript runtime errors", async () => {
    const [result] = await eslint.lintText("missingQualityBinding();\n", {
      filePath: "src/quality-probe.js",
    });
    expect(result.messages.map(message => message.ruleId)).toContain("no-undef");
  });

  it("covers the entire production source tree including legacy components", async () => {
    for (const path of [
      "src/main.tsx", "src/contexts/font-context.tsx",
      "src/components/ImmersiveLyricsView.tsx", "src/preview/runtime/core.ts",
      "installer/src/App.tsx", "tests/frontend-quality.test.ts",
    ]) {
      expect(await eslint.isPathIgnored(path), path).toBe(false);
    }
  });

  it("ignores generated output and the frozen source snapshot only", async () => {
    for (const path of [
      "dist/assets/index.js", "installer/dist/assets/index.js",
      "src-tauri/target/debug/build/generated.js",
      "src-framescope-baseline/App.tsx",
    ]) {
      expect(await eslint.isPathIgnored(path), path).toBe(true);
    }
  });
});
