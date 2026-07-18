# FrameScopeMonitor GitHub Publication Design

## Goal

Publish the completed FrameScopeMonitor remediation to the repository's `main` branch with a clean public file tree and a rewritten Chinese README.

## Local Safety Boundary

- Do not delete, edit, install, stop, or otherwise change the local GameLite project, scripts, configuration, scheduled tasks, or WMI state.
- Do not remove GameLite files from the current FrameScopeMonitor worktree.
- Create a separate temporary publication worktree. Repository removals happen only in that worktree and in the resulting GitHub commit.
- Do not push from the original workspace.

## Published Tree

Keep FrameScopeMonitor source, frontend, tests, packaging, build tooling, dependency locks, required runtime assets, current architecture guidance, and user-relevant diagnostics.

Remove from the published tree:

- GameLite scripts and GameLite-only documentation.
- Local logs, caches, generated artifacts, test evidence, installation state, and build output.
- Internal orchestration prompts, handoff notes, progress journals, obsolete implementation reports, and redundant historical test reports.

## README

Replace the corrupted README with a concise Chinese document covering:

- Product purpose and core capabilities.
- System requirements and installation choices.
- Basic monitoring workflow and report meanings.
- Architecture and repository layout.
- Source build and focused verification commands.
- Local-data/privacy behavior and troubleshooting.

## Verification And Publication

Before pushing:

1. Confirm the temporary worktree contains no tracked GameLite path or text reference except an explicit statement that GameLite is not part of this repository.
2. Confirm generated/local-only paths are ignored and absent from the staged diff.
3. Run README/documentation checks, focused build contracts, and `git diff --check`.
4. Review the exact commit tree and files changed.
5. Push the sanitized publication branch to `origin/main` without changing the local GameLite project.
