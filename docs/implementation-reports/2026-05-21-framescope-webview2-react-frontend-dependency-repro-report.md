# FrameScope WebView2 React Frontend Dependency Reproducibility Report

Date: 2026-05-21
Role: WebView2 React frontend dependency reproducibility owner
Scope: frontend dependency install and verification only

## Conclusion

Dependency reproducibility is PASS for this scope.

The previous P1 dependency blocker was real: this machine has no usable npm, pnpm, yarn, or corepack on PATH, and the Codex bundled Node runtime only provides `node.exe`. Existing `src/frontend/node_modules` could run checks, but that did not prove a clean install.

The fixed workflow is `tools\Run-Frontend.ps1`. It resolves a working Node executable, prefers the Codex bundled Node when available, bootstraps pinned `npm 10.9.2` into ignored `tools\.cache\frontend-npm`, runs `npm ci` from `src/frontend/package-lock.json`, and then runs frontend scripts without relying on npm being on PATH.

This does not clear the separate WebView2 UI motion P1 from the retest report. It only clears the dependency reproducibility P1.

## Root Cause

- `Get-Command node,npm,pnpm,yarn,corepack` found only the WindowsApps Codex `node.exe` shim on PATH.
- `npm`, `pnpm`, `yarn`, and `corepack` were not found on PATH.
- Codex bundled Node exists at `C:\Users\misakamiro\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe` and reports `v24.14.0`.
- The bundled runtime does not include `npm.cmd`, `npm`, `corepack.cmd`, `corepack`, `node_modules\npm\bin\npm-cli.js`, `node_modules\corepack\dist\corepack.js`, `node_modules\pnpm\bin\pnpm.cjs`, or `node_modules\yarn\bin\yarn.js`.
- `package.json` and `package-lock.json` dependency and devDependency entries match.

## Files Changed

- `tools\Run-Frontend.ps1`
  - New frontend entry point for install, typecheck, test, build, and full verify.
  - Uses a working Node path and pinned npm CLI instead of PATH npm.
  - Prepends the resolved Node directory to PATH so npm script bin shims use the same Node.
- `src\frontend\.npmrc`
  - Disables audit, fund, and update-notifier noise for reproducible verification output.
- `.gitignore`
  - Ignores `tools/.cache/`, where the script stores the bootstrapped npm tarball and npm cache.

No UI motion files, bridge files, backend files, monitoring/reporting/diagnostics files, GameLite files, packaging files, `package.json`, `package-lock.json`, `vite.config.ts`, or `build.ps1` were changed by this dependency fix.

## Clean Install Verification

1. Moved existing `src\frontend\node_modules` to a temporary backup:
   - `C:\Users\MISAKA~1\AppData\Local\Temp\framescope-frontend-node_modules-backups\node_modules-20260521-015615`
2. Ran plain `npm install` in `src\frontend`.
   - Result: FAIL as expected.
   - Reason: `npm` is not recognized.
3. Bootstrapped pinned `npm 10.9.2` with bundled Node.
   - Result: PASS.
4. Ran `npm ci` through the bootstrapped npm CLI after `node_modules` had been removed.
   - Result: PASS, `added 110 packages`.
5. Re-ran the project entry point:
   - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 install`
   - Result: PASS, `added 110 packages`.

## Verification Commands And Results

| Command | Result | Evidence |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify` | PASS | Bootstrapped npm 10.9.2, `npm ci`, typecheck, 2 test files / 7 tests, Vite build. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 install` | PASS | `added 110 packages`. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 typecheck` | PASS | `tsc --noEmit` exit 0. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test` | PASS | Vitest: 2 files, 7 tests passed. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build` | PASS | Vite built `dist/index.html`, CSS, JS, and sourcemap. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` | PASS | Built `dist\FrameScopeMonitor-Setup.exe`. |

`"C:\Program Files\Git\cmd\git.exe" diff --check` also passed with exit code 0. It printed only line-ending warnings for already dirty tracked files.

Residual process check found one pre-existing frontend dev server from 2026-05-20 13:15:21:

- `node.exe` PID 22388 running Vite on `--host 127.0.0.1 --port 5174 --strictPort true`.
- child `esbuild.exe` PID 20024.

These processes predate this dependency fix and were not started by the verification commands in this report. They were not terminated because they may belong to the parallel UI lane. The old temporary `node_modules` backup could not be fully removed while that pre-existing `esbuild.exe` process was holding a file handle.

## Tester Workflow

Use this exact flow from the repo root on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 install
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 typecheck
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 build
```

Or run the combined frontend gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-Frontend.ps1 verify
```

## Minimum Environment

- Windows PowerShell.
- A working Node.js executable compatible with the frontend toolchain. Node 18+ is required by Vite and Vitest dependencies. The current Codex bundled Node `v24.14.0` satisfies this.
- `tar.exe` for first-run npm bootstrap if npm is not already available through the script cache.
- Network access to `https://registry.npmjs.org/npm/-/npm-10.9.2.tgz` on first bootstrap, plus normal npm registry access for `npm ci` if the npm package cache is cold.

If a future machine has no Node.js at all, set `FRAMESCOPE_NODE_EXE` to a working `node.exe` path or install Node.js 18+. Do not treat an existing `node_modules` directory as a valid dependency reproducibility proof.
