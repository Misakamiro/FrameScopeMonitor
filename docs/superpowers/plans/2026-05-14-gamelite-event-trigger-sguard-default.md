# GameLite Event Trigger SGuard Default Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change GameLite so game start/stop is event-triggered, SGuard is throttled by default during game mode, and no default long-running PowerShell polling session remains.

**Architecture:** Keep GameLite independent under `scripts\lightweight\`. WMI permanent events launch short-lived PowerShell commands: process start runs Enter plus SGuard throttle; process stop runs Exit only after confirming no configured game remains. Root wrappers stay compatibility bridges only.

**Tech Stack:** Windows PowerShell 5.1, WMI permanent event filters/consumers, process priority/IO/page priority/affinity APIs through inline C#.

---

### Task 1: Update Regression Tests First

**Files:**
- Modify: `tests\lightweight-separation-tests.ps1`

- [ ] **Step 1: Change SGuard assertions**

Assert that `Enter-GameLite.ps1`, `GameLiteSession.ps1`, and `Invoke-GameLiteSGuardThrottle.ps1` expose `-DisableSGuardThrottle`, and that default paths do not require `-AllowSGuardThrottle`.

- [ ] **Step 2: Add WMI query assertions**

Assert `Install-GameLiteAutoTrigger.ps1` contains `Win32_ProcessStartTrace` and `Win32_ProcessStopTrace`, and that it installs separate game start and game stop filters.

- [ ] **Step 3: Add no-long-session assertion**

Assert the default install command line does not point at `GameLiteSession.ps1`; the old session script may remain as manual compatibility, but default auto trigger must not use it.

### Task 2: Make SGuard Default With Explicit Disable

**Files:**
- Modify: `scripts\lightweight\Enter-GameLite.ps1`
- Modify: `scripts\lightweight\Invoke-GameLiteSGuardThrottle.ps1`
- Modify: `scripts\lightweight\GameLiteSession.ps1`

- [ ] **Step 1: Add `-DisableSGuardThrottle`**

Add the switch to the three scripts. Keep `-AllowSGuardThrottle` accepted for compatibility, but do not require it.

- [ ] **Step 2: Default SGuard throttling**

In `Enter-GameLite.ps1`, include SGuard targets unless `-DisableSGuardThrottle` is passed. Default SGuard affinity is `LastTwo`; `-StrictSGuard` changes SGuard affinity to `LastOne`.

- [ ] **Step 3: Keep safe operation boundaries**

Do not kill, suspend, disable services, rename files, delete files, or apply Job Object CPU hard caps. Preserve the protected process list and game-process exclusions.

### Task 3: Replace Default Auto Trigger With Short-Lived Event Commands

**Files:**
- Modify: `scripts\lightweight\Install-GameLiteAutoTrigger.ps1`
- Modify: `scripts\lightweight\Check-GameLiteAutoTrigger.ps1`
- Modify: `scripts\lightweight\Remove-GameLiteAutoTrigger.ps1`

- [ ] **Step 1: Install game start event**

Install a `Win32_ProcessStartTrace` filter for configured game names. Consumer runs root/core `Enter-GameLite.ps1` with active game exclusions and default SGuard enabled. Do not pass `-ForceSnapshot` in the default WMI start consumer, because repeated or overlapping game-start events must not overwrite the original restore snapshot.

- [ ] **Step 2: Install game stop event**

Install a `Win32_ProcessStopTrace` filter for configured game names. Consumer runs `Exit-GameLite.ps1 -RequireNoActiveGame -ExitGraceSeconds 8`, so one game exit does not restore while another configured game remains active.

- [ ] **Step 3: Handle late SGuard process starts**

Keep or install a SGuard start filter only if it runs `Invoke-GameLiteSGuardThrottle.ps1 -RequireActiveGame -ThrottlePagePriority`, without requiring `-AllowSGuardThrottle`. It must no-op when no state file or no configured game exists.

- [ ] **Step 4: Remove old objects safely**

Remove script must include old and new filter/consumer names. Do not execute install/remove during normal verification without explicit user authorization.

### Task 4: Update Docs and Progress

**Files:**
- Modify: `docs\modules\lightweight-script.md`
- Modify: `docs\FrameScopeMonitor-progress.md`
- Modify: `docs\FrameScopeMonitor-next-prompt.md`
- Modify if needed: `AGENTS.md`, `README.md`

- [ ] **Step 1: Replace old SGuard opt-in wording**

Document SGuard default throttle, `-DisableSGuardThrottle`, default vs strict strategy, and safety limits.

- [ ] **Step 2: Document event-trigger lifecycle**

Document start WMI -> Enter, stop WMI -> Exit, optional SGuard start WMI -> no-op unless game mode active. Document no default long-running `GameLiteSession`.

### Task 5: Verify

**Files:**
- Test: `tests\lightweight-separation-tests.ps1`

- [ ] **Step 1: Run syntax parsing**

Run the required PowerShell parser command over 7 root wrappers and 7 core scripts.

- [ ] **Step 2: Run GameLite tests and checks**

Run `tests\lightweight-separation-tests.ps1`, root/direct `Check-GameLiteAutoTrigger.ps1`, wrapper argument forwarding, default SGuard dry-run/no-real-process check, `-DisableSGuardThrottle` dry-run check, and residual process check.

- [ ] **Step 3: Run FrameScope isolation checks**

Run `build.ps1` and `tests\Build-FrameScopeTests.ps1`. Run existing test exes and chart test if time allows.

- [ ] **Step 4: Report manual admin/game checks**

Do not claim WMI lifecycle or real game behavior verified unless admin install and a real/simulated matching game process lifecycle were actually run.
