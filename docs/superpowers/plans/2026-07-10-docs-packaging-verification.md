# Documentation, Packaging, and Full Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make project documentation describe the current React/WebView2 implementation, prove every local release artifact comes from one build payload, and run a reproducible end-to-end acceptance suite.

**Architecture:** Documentation receives an automated current-path/stale-term gate. `build.ps1` writes one build provenance manifest and embeds the same payload ZIP in Setup and Full Setup; a package verifier extracts resources and compares file hashes. One full-verification script runs every build, test, simulator, probe, package, residue, and repository-integrity check without launching a real game or changing GameLite WMI triggers.

**Tech Stack:** Markdown, PowerShell, SHA256 manifests, .NET reflection/ZIP APIs, existing C#/Vitest/simulator test suites.

---

### Task 1: Current architecture documentation and stale-reference gate

**Files:**
- Create: `tools/Test-CurrentDocumentation.ps1`
- Modify: `AGENTS.md`
- Modify: `README.md`
- Modify: `docs/FrameScopeMonitor-Project-Overview.md`
- Modify: `docs/modules/backend-monitoring.md`
- Modify: `docs/modules/software-ui.md`
- Modify: `docs/modules/ui-interactions.md`
- Modify: `docs/modules/lightweight-script.md`
- Modify: `packaging/README-FrameScopeMonitor.txt`
- Test: `tools/Test-CurrentDocumentation.ps1`

- [ ] **Step 1: Add a failing documentation contract**

```powershell
$currentDocs = @(
  'AGENTS.md',
  'README.md',
  'docs/FrameScopeMonitor-Project-Overview.md',
  'docs/modules/backend-monitoring.md',
  'docs/modules/software-ui.md',
  'docs/modules/ui-interactions.md',
  'docs/modules/lightweight-script.md',
  'packaging/README-FrameScopeMonitor.txt'
)
$forbidden = @('src/ui', 'DataGridView', '旧 WinForms 主界面仍', '每个目标.*独立采样率')
foreach ($file in $currentDocs) {
  $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $file)
  foreach ($pattern in $forbidden) {
    if ($text -match $pattern) { throw "$file contains stale production guidance: $pattern" }
  }
}
```

Also extract backtick-wrapped repository paths beginning with `src/`, `tools/`, `tests/`, or `packaging/` and require each referenced path to exist.

- [ ] **Step 2: Run the contract to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-CurrentDocumentation.ps1`

Expected: failures identify deleted `src/ui`, old WinForms pages/live charts, or per-target sampling claims.

- [ ] **Step 3: Rewrite current-maintenance documents**

Document the actual chain exactly:

```text
src/frontend (React + Vite)
  -> WebView2 host in src/app
  -> FrameScopeWebBridge request/event contract
  -> native watcher and one monitor-session worker per active target
  -> PresentMon + ProcessSampler + SystemSampler
  -> run status/summary/raw CSV
  -> bounded ReportGenerator
  -> data.js + HTML + manifest
```

State that `TelemetrySampleIntervalMs` is the global persisted interval; legacy per-target interval fields are normalized for compatibility. Explain `full`, `partial`, `diagnostic`, and `error`; describe GameLite scripts as separate utilities not installed or changed by the normal build. Keep historical implementation/test reports as historical records and do not present them as current maintenance entry points.

- [ ] **Step 4: Verify every documented current path**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-CurrentDocumentation.ps1`

Expected: `Current documentation contract passed.`

- [ ] **Step 5: Commit current documentation**

```powershell
git add AGENTS.md README.md docs/FrameScopeMonitor-Project-Overview.md docs/modules/backend-monitoring.md docs/modules/software-ui.md docs/modules/ui-interactions.md docs/modules/lightweight-script.md packaging/README-FrameScopeMonitor.txt tools/Test-CurrentDocumentation.ps1
git commit -m "docs: align guidance with the current architecture"
```

### Task 2: Single-payload package provenance and parity verifier

**Files:**
- Create: `tools/Test-FrameScopePackages.ps1`
- Modify: `build.ps1:220-315`
- Modify: `packaging/FrameScopeSetupNative.cs`
- Test: `tools/Test-FrameScopePackages.ps1`

- [ ] **Step 1: Add the package verifier before changing the build**

```powershell
$required = @(
  'dist/FrameScopeMonitor-Setup.exe',
  'dist/FrameScopeMonitor-Full-Setup.exe',
  'dist/FrameScopeMonitor-Installer.zip',
  'dist/FrameScopeMonitor-LegacyCleanup.exe',
  'dist/FrameScopeMonitor-payload/FrameScopeBuildManifest.json'
)
foreach ($path in $required) {
  if (-not (Test-Path -LiteralPath (Join-Path $root $path))) { throw "missing package: $path" }
}
```

The verifier loads each Setup assembly, extracts `FrameScopePayload`, expands it, computes normalized `relative-path -> SHA256` maps, and compares both maps to `dist/FrameScopeMonitor-payload`. It expands `FrameScopeMonitor-Installer.zip` and asserts its Setup, Full Setup, LegacyCleanup, and README hashes equal the sibling `dist` files. It validates version/build ID in the payload manifest and assembly metadata.

- [ ] **Step 2: Run the verifier to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopePackages.ps1`

Expected: failure because `FrameScopeBuildManifest.json` and build provenance are absent.

- [ ] **Step 3: Generate one payload provenance manifest**

```powershell
$buildId = [guid]::NewGuid().ToString('N')
$payloadRootUri = [Uri]((Resolve-Path -LiteralPath $payloadRoot).Path.TrimEnd('\') + '\')
$payloadEntries = Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | ForEach-Object {
  $relative = [Uri]::UnescapeDataString($payloadRootUri.MakeRelativeUri([Uri]$_.FullName).ToString())
  [ordered]@{
    path = $relative.Replace('\','/')
    length = $_.Length
    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
}
$manifest = [ordered]@{
  product = 'FrameScope Monitor'
  version = $version
  buildId = $buildId
  dependencies = $lock
  files = @($payloadEntries | Sort-Object path)
}
```

Write the manifest atomically into the payload, then create `payload.zip` once. Both Setup executables embed that exact ZIP path. Compile LegacyCleanup during the same invocation using the same generated assembly metadata. Build the release ZIP only from the four verified sibling outputs; never regenerate a second payload between artifacts.

- [ ] **Step 4: Verify package extraction and hash parity**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopePackages.ps1`

Expected: `FrameScope package parity passed.` and both embedded payload hashes are identical.

- [ ] **Step 5: Commit provenance verification**

```powershell
git add build.ps1 packaging/FrameScopeSetupNative.cs tools/Test-FrameScopePackages.ps1
git commit -m "build: verify release artifacts from one payload"
```

### Task 3: Reproducible full local verification command

**Files:**
- Create: `tools/Invoke-FrameScopeFullVerification.ps1`
- Create: `docs/verification/2026-07-10-local-remediation-report.md`
- Test: `tools/Invoke-FrameScopeFullVerification.ps1`

- [ ] **Step 1: Implement a fail-fast verification runner**

```powershell
$checks = @(
  @{ Name='documentation'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-CurrentDocumentation.ps1 } },
  @{ Name='build-contract'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopeBuildContract.ps1 } },
  @{ Name='frontend'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify } },
  @{ Name='frontend-audit'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 npm audit --audit-level=high } },
  @{ Name='native-build'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 } },
  @{ Name='test-build'; Command={ & powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1 } }
)
foreach ($check in $checks) {
  & $check.Command
  if ($LASTEXITCODE -ne 0) { throw "$($check.Name) failed with exit code $LASTEXITCODE" }
}
```

Continue with every `tests\FrameScope*Tests.exe`, `chart-sampling-tests.js`, `lightweight-separation-tests.ps1`, PUBG/FakePresentMon simulation, report layout/process/large-data probes, package parity, `git diff --check`, and residue checks. Write a timestamped UTF-8 log and machine-readable JSON result under ignored `artifacts\verification`; each entry contains name, start/end, duration, exit code, and result.

- [ ] **Step 2: Add strict scope and residue guards**

The runner must reject switches that install/remove GameLite triggers and must not call `Install-GameLiteAutoTrigger.ps1`, `Remove-GameLiteAutoTrigger.ps1`, or real game executables. At the end, fail if any process name matches:

```powershell
@('FrameScopeMonitor','FrameScopeProcessSampler','FrameScopeSystemSampler','FrameScopeReportGenerator','FakePresentMon','PubgGameSimulator')
```

Query `logman.exe query -ets` and fail if a line begins with `FrameScopeNativePresentMon_`. Record the original workspace's `git status --porcelain=v1` before and after and require exact equality.

- [ ] **Step 3: Run the complete acceptance suite**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-FrameScopeFullVerification.ps1 -OriginalWorkspace 'C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d'`

Expected: every check records `passed`; no residual process/session remains; original workspace status is byte-for-byte unchanged.

- [ ] **Step 4: Write the verified local remediation report**

The report records:

```markdown
- Branch and final local commit
- Root cause and fix for every confirmed issue
- Exact verification commands and pass counts
- npm audit high/critical counts
- Setup, Full Setup, Installer ZIP, and LegacyCleanup size/SHA256
- Embedded payload SHA256 and build ID parity
- Original workspace before/after status hash
- Explicitly unperformed: real-game launch, GameLite WMI trigger changes, GitHub push/tag/Release update
```

Populate values only from the completed verification JSON and `Get-FileHash`; do not write a pass claim before the commands finish.

- [ ] **Step 5: Commit verification tooling and evidence report**

```powershell
git add tools/Invoke-FrameScopeFullVerification.ps1 docs/verification/2026-07-10-local-remediation-report.md
git commit -m "test: add full local remediation verification"
```

### Task 4: Final independent review and acceptance rerun

**Files:**
- Verify only

- [ ] **Step 1: Review all remediation commits against the design**

Run: `git log --oneline fd9a336..HEAD; git diff --stat fd9a336..HEAD; git diff --check fd9a336..HEAD`

Expected: only local remediation/design/plan commits are present; whitespace check passes.

- [ ] **Step 2: Run an independent code review**

Use the `requesting-code-review` workflow on `fd9a336..HEAD`. Resolve every Critical or Important finding, add a regression test for each behavioral correction, then repeat the review until approved.

- [ ] **Step 3: Rerun the full verifier after review fixes**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-FrameScopeFullVerification.ps1 -OriginalWorkspace 'C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d'`

Expected: all acceptance checks pass on the final commit, not on an earlier tree.

- [ ] **Step 4: Confirm local-only delivery boundary**

Run: `git status --short --branch; git remote -v; git reflog show --format='%gs' --since='2026-07-10 00:00:00'`

Expected: clean `codex/project-remediation` worktree; no push/tag/Release operation; original workspace status unchanged.
