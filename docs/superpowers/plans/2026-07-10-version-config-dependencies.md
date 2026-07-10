# Version, Configuration, and Dependency Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `1.2.1`, default configuration, and native/frontend build dependencies deterministic and verifiably consistent across every local artifact.

**Architecture:** A root `VERSION` file generates C# assembly metadata and injects the React build version. Runtime and installer compile the same `FrameScopeConfigStore` defaults; the example file is regenerated and parity-tested. A dependency lock manifest names exact native package versions/hashes while npm remains locked by `package-lock.json` and audited during verification.

**Tech Stack:** PowerShell, C# compiler, NuGet/dotnet restore, npm lockfile, Vite/Vitest.

---

### Task 1: Single product version source and assembly metadata

**Files:**
- Create: `VERSION`
- Create: `tools/Write-FrameScopeBuildMetadata.ps1`
- Create: `src/frontend/src/productVersion.ts`
- Create: `tests/FrameScopeVersionTests.cs`
- Modify: `build.ps1`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Modify: `src/diagnostics/FrameScopeDiagnostics.cs:16`
- Modify: `src/diagnostics/FrameScopeDiagnostics.Sections.cs:13-27`
- Modify: `packaging/FrameScopeSetupNative.cs:17,480-500`
- Modify: `src/frontend/src/pages/AboutPage.tsx:30-45`
- Modify: `src/frontend/src/vite-env.d.ts`
- Modify: `src/frontend/package.json`
- Modify: `src/frontend/package-lock.json`
- Modify: `tools/Run-Frontend.ps1`
- Test: `tests/FrameScopeVersionTests.exe`

- [ ] **Step 1: Add `VERSION` and failing consistency tests**

`VERSION` contains exactly:

```text
1.2.1
```

```csharp
private static void GeneratedVersionMatchesRootVersion()
{
    string expected = File.ReadAllText(Path.Combine(Root, "VERSION")).Trim();
    AssertEqual("1.2.1", expected, "local remediation version");
    AssertEqual(expected, FrameScopeProductInfo.Version, "generated C# version");
}

private static void BuiltAssembliesUseProductVersion()
{
    foreach (string name in new[] { "FrameScopeMonitor.exe", "FrameScopeProcessSampler.exe", "FrameScopeSystemSampler.exe", "FrameScopeReportGenerator.exe", "FrameScopeUninstaller.exe", "FrameScopeLegacyCleanup.exe" })
    {
        string version = FileVersionInfo.GetVersionInfo(Path.Combine(Root, name)).FileVersion;
        AssertTrue(version.StartsWith("1.2.1", StringComparison.Ordinal), name + " version=" + version);
    }
}
```

- [ ] **Step 2: Run the metadata test to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeVersionTests.exe`

Expected: compilation fails because generated `FrameScopeProductInfo` does not exist; existing binaries report `0.0.0.0`.

- [ ] **Step 3: Generate C# metadata from `VERSION`**

```powershell
$version = (Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid VERSION: $version" }
$assemblyVersion = "$version.0"
$source = @"
using System.Reflection;
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$version")]
internal static class FrameScopeProductInfo { public const string Version = "$version"; }
"@
[IO.File]::WriteAllText($OutputPath, $source, [Text.UTF8Encoding]::new($false))
```

Call the generator from both build scripts and include the generated source in every C# executable compilation. Replace installer and diagnostics literals with `FrameScopeProductInfo.Version`.

- [ ] **Step 4: Inject the same version into Vite**

```powershell
$env:VITE_FRAMESCOPE_VERSION = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'VERSION')).Trim()
```

```ts
export const productVersion = import.meta.env.VITE_FRAMESCOPE_VERSION;
if (!/^\d+\.\d+\.\d+$/.test(productVersion)) throw new Error("VITE_FRAMESCOPE_VERSION is invalid");
```

Use `productVersion` on the About page. Set the npm package and lockfile top-level version to `1.2.1`; a parity test must compare them to `VERSION` so they cannot silently drift.

- [ ] **Step 5: Build and verify every version surface**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build; powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeVersionTests.exe`

Expected: all binaries and generated UI metadata report `1.2.1`; installer registry `DisplayVersion` uses the same value.

- [ ] **Step 6: Commit version unification**

```powershell
git add VERSION tools/Write-FrameScopeBuildMetadata.ps1 src/frontend/src/productVersion.ts build.ps1 tests/Build-FrameScopeTests.ps1 tests/FrameScopeVersionTests.cs src/diagnostics/FrameScopeDiagnostics.cs src/diagnostics/FrameScopeDiagnostics.Sections.cs packaging/FrameScopeSetupNative.cs src/frontend/src/pages/AboutPage.tsx src/frontend/src/vite-env.d.ts src/frontend/package.json src/frontend/package-lock.json tools/Run-Frontend.ps1
git commit -m "fix: unify product version at 1.2.1"
```

### Task 2: One default configuration model

**Files:**
- Create: `tools/FrameScopeConfigExporter.cs`
- Create: `tools/Export-FrameScopeDefaultConfig.ps1`
- Modify: `src/core/FrameScopeConfigStore.cs:34-96`
- Modify: `packaging/FrameScopeSetupNative.cs:370-402`
- Modify: `build.ps1:220-260`
- Modify: `framescope-config.example.json`
- Modify: `tests/FrameScopeConfigStoreTests.cs`
- Modify: `tests/Build-FrameScopeTests.ps1`
- Test: `tests/FrameScopeConfigStoreTests.exe`

- [ ] **Step 1: Add failing semantic parity tests**

```csharp
private static void ExampleMatchesRuntimeDefault()
{
    FrameScopeConfig expected = FrameScopeConfigStore.CreateDefaultConfig();
    expected.DataRoot = "framescope-runs";
    FrameScopeConfig actual = Json.Deserialize<FrameScopeConfig>(File.ReadAllText(Path.Combine(Root, "framescope-config.example.json")));
    AssertEqual(Json.Serialize(expected), Json.Serialize(actual), "example is generated from runtime defaults");
}

private static void InstallerHasNoHandwrittenTargetJson()
{
    string source = File.ReadAllText(Path.Combine(Root, "packaging", "FrameScopeSetupNative.cs"));
    AssertFalse(source.Contains("CreateDefaultConfigJson"), "installer must call FrameScopeConfigStore");
    AssertFalse(source.Contains("HogwartsLegacy.exe\""), "installer must not duplicate target rows");
}
```

- [ ] **Step 2: Run config tests to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeConfigStoreTests.exe`

Expected: targets/enabled flags and missing fields differ, and installer source still contains handwritten JSON.

- [ ] **Step 3: Compile the installer against `FrameScopeConfigStore`**

```csharp
FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
config.DataRoot = NormalizeDataRoot(dataRoot);
FrameScopeConfigStore.Save(configPath, config);
```

Remove `CreateDefaultConfigJson` from the installer. Add `System.Web.Extensions.dll`, `FrameScopeConfigStore.cs`, and `FrameScopeJsonFile.cs` to both Setup compilations so first install uses the runtime model directly.

- [ ] **Step 4: Generate and verify the example**

```csharp
FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
config.DataRoot = "framescope-runs";
FrameScopeConfigStore.Save(args[0], config);
```

`Export-FrameScopeDefaultConfig.ps1` compiles this exporter to `obj`, writes `framescope-config.example.json`, then immediately deserializes and compares it with a fresh runtime default. Invoke it from `build.ps1` before assembling the payload and copy the generated example into the payload as `framescope-default-config.json` for inspection.

- [ ] **Step 5: Verify parity and installer compilation**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Export-FrameScopeDefaultConfig.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeConfigStoreTests.exe; powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`

Expected: semantic JSON parity passes and Setup/Full Setup compile without embedded target literals.

- [ ] **Step 6: Commit the default model**

```powershell
git add tools/FrameScopeConfigExporter.cs tools/Export-FrameScopeDefaultConfig.ps1 src/core/FrameScopeConfigStore.cs packaging/FrameScopeSetupNative.cs build.ps1 framescope-config.example.json tests/FrameScopeConfigStoreTests.cs tests/Build-FrameScopeTests.ps1
git commit -m "fix: generate every default config from one model"
```

### Task 3: Remove high and critical frontend vulnerabilities

**Files:**
- Modify: `src/frontend/package.json`
- Modify: `src/frontend/package-lock.json`
- Test: frontend typecheck, Vitest, build, npm audit

- [ ] **Step 1: Capture the vulnerable baseline**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 npm audit --json`

Expected before update: one high Vite advisory and one critical Vitest advisory are present.

- [ ] **Step 2: Update the smallest compatible dependency set**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 npm install --save-dev vite@6.4.3 vitest@3.2.7 @babel/core@7.29.7`

Expected: package and lockfile update without changing React/runtime dependencies.

- [ ] **Step 3: Verify the frontend after the lockfile change**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify`

Expected: clean npm install, typecheck, all Vitest files, and production Vite build pass.

- [ ] **Step 4: Enforce the audit gate**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 npm audit --audit-level=high`

Expected: exit code 0; high vulnerabilities = 0 and critical vulnerabilities = 0.

- [ ] **Step 5: Commit dependency remediation**

```powershell
git add src/frontend/package.json src/frontend/package-lock.json
git commit -m "fix: update vulnerable frontend build dependencies"
```

### Task 4: Pin and verify native build dependencies

**Files:**
- Create: `dependencies.lock.json`
- Create: `tools/Test-FrameScopeBuildContract.ps1`
- Modify: `build.ps1:1-93,212-260`
- Test: `tools/Test-FrameScopeBuildContract.ps1`

- [ ] **Step 1: Add the lock manifest and a failing build-contract test**

```json
{
  "microsoftWebView2": "1.0.3967.48",
  "libreHardwareMonitorLib": "0.9.6",
  "presentMon": {
    "file": "tools/PresentMon-2.4.1-x64.exe",
    "sha256": "D74183E7AE630F72CD3690BE0373ECBFDC6CBB86578148AAB8FA2A7166068F34"
  },
  "webView2StandaloneInstaller": {
    "file": "packaging/MicrosoftEdgeWebView2RuntimeInstallerX64.exe",
    "sha256": "3A08103BED8A3D9AEFDFC9AC10A672EA69605163F2DCB08D76CFD3E0444511C9"
  }
}
```

The contract test must fail when build.ps1 contains `Select-Object -Last 1`, a package version not equal to the lock, an unlisted `src/**/*.cs` production file, or a mismatched pinned-file hash.

- [ ] **Step 2: Run the contract test to verify RED**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopeBuildContract.ps1`

Expected: failure identifying latest-local WebView2 selection.

- [ ] **Step 3: Restore exact NuGet package versions**

```powershell
$lock = Get-Content -Raw -LiteralPath (Join-Path $root 'dependencies.lock.json') | ConvertFrom-Json
$webView2Version = [string]$lock.microsoftWebView2
$libreHardwareMonitorVersion = [string]$lock.libreHardwareMonitorLib
```

Generate the temporary restore project with these exact values and set `RestorePackagesPath` to `tools\.cache\nuget`; locate WebView2 only at `$cache\microsoft.web.webview2\$webView2Version`. Verify required DLLs and locked SHA256 values before compilation. Print resolved package versions and hashes in build output.

- [ ] **Step 4: Centralize compiler source lists**

Define named arrays (`$commonCoreSources`, `$monitorSources`, `$reportSources`, `$samplerSources`) and pass them to one `Invoke-CSharpBuild` helper. The contract test enumerates production C# files and requires each file to be either included by a target array or explicitly listed as packaging-only; newly added core files cannot be silently omitted.

- [ ] **Step 5: Verify deterministic dependency resolution**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopeBuildContract.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`

Expected: contract PASS; build reports WebView2 `1.0.3967.48`, LibreHardwareMonitor `0.9.6`, matching PresentMon/standalone-installer hashes, and completes all four local artifacts.

- [ ] **Step 6: Commit native dependency locks**

```powershell
git add dependencies.lock.json tools/Test-FrameScopeBuildContract.ps1 build.ps1
git commit -m "build: pin and verify native dependencies"
```

### Task 5: Stage D consistency verification

**Files:**
- Verify only

- [ ] **Step 1: Run build-contract, frontend, and audit gates**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FrameScopeBuildContract.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify; powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 npm audit --audit-level=high`

Expected: all commands exit 0; high/critical audit counts are zero.

- [ ] **Step 2: Fresh native build and version/config tests**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Build-FrameScopeTests.ps1; .\tests\FrameScopeVersionTests.exe; .\tests\FrameScopeConfigStoreTests.exe`

Expected: build completes and both consistency executables pass.

- [ ] **Step 3: Check all version strings**

Run: `rg -n "1\.1\.3|0\.0\.0\.0" src packaging build.ps1 tools tests -g '!src/frontend/node_modules/**' -g '!src/frontend/dist/**'`

Expected: no production occurrence; fixture text is allowed only when explicitly labeled as legacy compatibility data.

