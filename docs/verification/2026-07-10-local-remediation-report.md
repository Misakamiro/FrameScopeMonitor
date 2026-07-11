# Local Remediation Verification Report

Date: 2026-07-11

Branch: `codex/project-remediation`

Scope: local-only remediation; no push, tag, release, machine install, real game, or GameLite WMI validation.

## Result

Project remediation is complete for locally testable scope.

- Full verification run `artifacts/verification/20260711-105626-701`: 16/17 checks passed.
- Sole failure: frontend large-list probe lacked `VITE_FRAMESCOPE_VERSION` when starting Vite.
- Fix committed in `8b30cfb`; targeted large-list rerun passed with 3 results and no smoke failure.
- Package parity continuation passed after the fix.
- No third full run was performed because unchanged checks had already passed and the user requested reduced redundant testing.

## Focused verification

- Target lifecycle integration: passed (36.6 s).
- Report recovery, retention, manifest, web bridge, monitoring reliability, JSON file, and report status tests: passed.
- Full-verifier contracts: timeout, owned cleanup, finalization, workspace fingerprint, simulator exit code, simulator compile: passed.
- Simulator stable scenario (4 s): 240 frames; monitor exit 0; report exit 0; full report; cleanup successful.
- Frontend large-list probe: passed after environment fix.
- Package build/parity: passed.
- Diff whitespace checks and residual-process checks: passed at finalization.

## Package identity

- Build ID: `414dff88856c4895a38e32271160e26d`
- Payload SHA-256: `9CF76E0E6D2EABD3155B6F2AF53C1A5E31F5EF468DC833F69853814EB7AA1038`

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `FrameScopeMonitor-Setup.exe` | 2,762,240 | `2E5DA36D5D41DEAF79E719B168642678CA5601570A65BFA2B50009688C789EC0` |
| `FrameScopeMonitor-Full-Setup.exe` | 206,416,896 | `C8EC4D49BA310B6A814824816B3AB9E57695CF909BF74986ACAB81C7DD960566` |
| `FrameScopeMonitor-Installer.zip` | 209,108,575 | `068F6FEB4B1947AFEA4F89F180A00C3A54B66FA3B674821A6D2B08FB7F67B241` |
| `FrameScopeMonitor-LegacyCleanup.exe` | 59,392 | `2AFF1E2F3106A02FE07EC019990ACF53B020EE6C2BC3722260D53D06660C186E` |

## Workspace integrity

Original workspace fingerprint before and after verification:

`FC9EC5BB44A4395E34B10995E204D66F0601A467419C406217A1353911AE9142`

Original workspace remained unchanged. Final work stays in the separate local worktree and branch.

## Deferred external validation

Not executed by design: GitHub push/release, machine-wide installation, live game capture, GameLite WMI integration. These require explicit external or machine-level authorization and are not evidence gaps in the completed local remediation scope.
