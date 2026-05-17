# FrameScope Backend Prompt Ownership Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the Backend Prompt owner documents so future backend implementation dialogs receive precise scope, skills, file boundaries, and verification requirements.

**Architecture:** This is a documentation and orchestration plan only. It creates the backend prompt role, records the current worklog, and defines a reusable downstream backend implementation prompt without changing production source code.

**Tech Stack:** Markdown orchestration docs, FrameScope Monitor C# WinForms project documentation, simulator-based validation requirements.

---

### Task 1: Create Backend Prompt Role Document

**Files:**
- Create: `docs/orchestration/FrameScopeMonitor-BackendPrompt-Role.md`

- [x] **Step 1: Define role and non-role**

Write that this dialog owns backend prompt design, task decomposition, skill allocation, and downstream file boundaries. Write that it does not default to direct backend implementation.

- [x] **Step 2: Define required reading**

List `AGENTS.md`, orchestration docs, project overview, backend, UI interaction, software UI, progress, next prompt, and conditional lightweight docs.

- [x] **Step 3: Define backend decision questions**

Record the eight required questions about real backend action, status/config/log files, watcher/session/report/sampler/report HTML impact, real game versus simulator validation, installed sync, and GameLite/WMI/SGuard boundary.

- [x] **Step 4: Define file boundaries**

List allowed backend monitoring, sampler, diagnostics, report data, report-template, and GameLite files. List forbidden UI, GameLite, WMI, build, packaging, and high-conflict files.

- [x] **Step 5: Define skill and verification rules**

Record required skills and minimum verification commands, plus extra report-data and watcher/session/sampler validation.

### Task 2: Create Backend Prompt Worklog

**Files:**
- Create: `docs/orchestration/FrameScopeMonitor-BackendPrompt-Worklog.md`

- [x] **Step 1: Record current user request**

Record that the user requested this dialog to become the backend prompt and skill allocation owner, without a concrete backend implementation task yet.

- [x] **Step 2: Record read documents**

List the required project docs and optional UI design/interaction/report plans that were read.

- [x] **Step 3: Record UI-to-backend mapping**

Map start/stop monitoring, config save, live FPS, system/process data, logs, report generation, report open, diagnostics, PUBG capture, and GameLite/SGuard to backend responsibilities.

- [x] **Step 4: Record downstream prompt template**

Add `backend-implementation-template-v1` with project path, required docs, skills, allowed files, forbidden files, phases, verification commands, simulator checks, and PUBG manual validation.

- [x] **Step 5: Record open questions and next handoff point**

Record that no concrete backend demand is confirmed yet, and the next conversation must first classify the task and choose exact file boundaries.

### Task 3: Self-Check Documentation-Only Change

**Files:**
- Check: `docs/orchestration/FrameScopeMonitor-BackendPrompt-Role.md`
- Check: `docs/orchestration/FrameScopeMonitor-BackendPrompt-Worklog.md`
- Check: `docs/superpowers/plans/2026-05-15-framescope-backend-prompt-plan.md`

- [x] **Step 1: Verify changed files**

Run:

```powershell
& "C:\Program Files\Git\cmd\git.exe" diff --name-only
```

Expected: only backend prompt orchestration docs and this plan are newly changed by this task.

Observed: `git diff --name-only` only reports tracked changes and does not include new untracked docs. A focused `git status --short --untracked-files=all -- <three new docs>` confirmed the three backend prompt docs are new untracked files.

- [x] **Step 2: Verify no whitespace errors**

Run:

```powershell
& "C:\Program Files\Git\cmd\git.exe" diff --check
```

Expected: no whitespace errors from the new backend prompt docs. Existing LF/CRLF warnings elsewhere may be noted separately if present.

Observed: `git diff --check` reported only existing LF/CRLF warnings for `README.md`, `build.ps1`, and `framescope-config.example.json`. A direct trailing-whitespace scan of the three new backend prompt docs returned `no trailing whitespace`.

- [x] **Step 3: Confirm no implementation files changed**

Run:

```powershell
& "C:\Program Files\Git\cmd\git.exe" diff --name-only -- src tests build.ps1 scripts/lightweight packaging
```

Expected: no output.

Observed: output included `build.ps1`, which is an existing tracked working-tree change outside this documentation task. This task only wrote:

- `docs/orchestration/FrameScopeMonitor-BackendPrompt-Role.md`
- `docs/orchestration/FrameScopeMonitor-BackendPrompt-Worklog.md`
- `docs/superpowers/plans/2026-05-15-framescope-backend-prompt-plan.md`
