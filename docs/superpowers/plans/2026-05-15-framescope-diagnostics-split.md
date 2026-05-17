# FrameScope Diagnostics Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `src/diagnostics/FrameScopeDiagnostics.cs` into focused diagnostics files without changing report content, retention behavior, or redaction semantics.

**Architecture:** Convert `FrameScopeDiagnostics` to a partial static class. Keep public entry points in the original file, move result models, report section builders, markdown/redaction, and filesystem/JSON helpers into dedicated files.

**Tech Stack:** C# static diagnostics module, `JavaScriptSerializer`, existing diagnostics tests built by `tests/Build-FrameScopeTests.ps1`.

---

### Task 1: Split Diagnostics Module

**Files:**
- Create: `src/diagnostics/FrameScopeDiagnostics.Models.cs`
- Create: `src/diagnostics/FrameScopeDiagnostics.Sections.cs`
- Create: `src/diagnostics/FrameScopeDiagnostics.Markdown.cs`
- Create: `src/diagnostics/FrameScopeDiagnostics.Redaction.cs`
- Create: `src/diagnostics/FrameScopeDiagnostics.Retention.cs`
- Create: `src/diagnostics/FrameScopeDiagnostics.IO.cs`
- Modify: `src/diagnostics/FrameScopeDiagnostics.cs`

- [ ] **Step 1: Move result models**

Move `FrameScopeDiagnosticReportResult` and `FrameScopeDiagnosticCleanupResult` unchanged into `.Models.cs`.

- [ ] **Step 2: Keep public entry points**

Keep `DefaultDiagnosticRoot`, `AppendLogAsync`, `QueueGenerateReport`, `GenerateReport`, `BuildReport`, and `ApplyRetentionPolicy` in `FrameScopeDiagnostics.cs`.

- [ ] **Step 3: Move section builders**

Move `BuildSoftwareSection`, `BuildSystemSection`, `BuildSettingsSection`, `BuildTargetDetectionSection`, `BuildRecentSessionSection`, `BuildFpsSummary`, `BuildReportGenerationSection`, `BuildPerformanceSection`, `BuildErrorsSection`, and `BuildCaptureChainSection` into `.Sections.cs`.

- [ ] **Step 4: Move markdown renderer**

Move `BuildMarkdown` and `AppendSection` into `.Markdown.cs`.

- [ ] **Step 5: Move redaction**

Move `RedactForPrivacy`, `RedactMap`, `RedactObject`, and `IsSensitiveKey` into `.Redaction.cs`.

- [ ] **Step 6: Move retention**

Move `CleanupDiagnosticReports`, `TrimLogFile`, and `DeleteEmptyDirectories` into `.Retention.cs`.

- [ ] **Step 7: Move IO helpers**

Move `ResolveRoot`, `FindLatestRun`, `LoadJsonMap`, map/getter helpers, `FileSize`, `SafeGetProcesses`, `SafeEnumerateFiles`, and `AddFilteredTail` into `.IO.cs`.

### Task 2: Update Build Inputs

**Files:**
- Modify: `build.ps1`
- Modify: `tests/Build-FrameScopeTests.ps1`

- [ ] **Step 1: Compile diagnostics partial files into the app**

Add all `FrameScopeDiagnostics*.cs` files to the main executable source list.

- [ ] **Step 2: Compile diagnostics partial files into diagnostics tests**

Add all diagnostics partial files to `FrameScopeDiagnosticsTests.exe` sources.

### Task 3: Verify

Run `build.ps1`, rebuild tests, and run diagnostics tests immediately after this split.
