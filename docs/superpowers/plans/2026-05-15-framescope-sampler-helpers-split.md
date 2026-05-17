# FrameScope Sampler Helpers Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the remaining large monitoring sampler executables into clearer helper files without changing sampling semantics or CSV output.

**Architecture:** Convert each sampler class to a partial static class. Keep `Main` and `RunLoop` in the original executable file, then move passive models, platform helpers, GPU/performance-counter helpers, process selection helpers, and CSV/argument utilities into focused files.

**Tech Stack:** C# .NET Framework console/windowless executables compiled by `build.ps1`, validated through the existing build and stable PUBG simulator.

---

### File Structure

- Modify: `src/monitoring/FrameScopeProcessSampler.cs`
  - Keep `Main`, `RunLoop`, `WriteGroupedRows`, and `WriteAlerts`.
- Create: `src/monitoring/FrameScopeProcessSampler.Models.cs`
  - Own `ProcRow`, `GroupStats`, `IoCounters`, and `GetProcessIoCounters`.
- Create: `src/monitoring/FrameScopeProcessSampler.Selection.cs`
  - Own process-running checks, top CPU/IO row selection, IO counter read, and dictionary pruning.
- Create: `src/monitoring/FrameScopeProcessSampler.IO.cs`
  - Own argument parsing, base-name normalization, file creation, CSV writing, rounding, and nullable value helpers.
- Modify: `src/monitoring/FrameScopeSystemSampler.cs`
  - Keep `Main` and `RunLoop`.
- Create: `src/monitoring/FrameScopeSystemSampler.Models.cs`
  - Own `GpuSnapshot` and `PerfCounters`.
- Create: `src/monitoring/FrameScopeSystemSampler.PerfCounters.cs`
  - Own performance-counter creation, priming, next value, and network summing.
- Create: `src/monitoring/FrameScopeSystemSampler.Gpu.cs`
  - Own `nvidia-smi` querying and GPU snapshot parsing.
- Create: `src/monitoring/FrameScopeSystemSampler.Processes.cs`
  - Own process count and running checks.
- Create: `src/monitoring/FrameScopeSystemSampler.IO.cs`
  - Own argument parsing, file creation, CSV writing, parsing, and rounding helpers.
- Modify: `build.ps1`
  - Compile all new sampler partial files into their respective sampler exes.

### Task 1: Split Process Sampler

- [ ] Move passive models and Win32 IO counter declaration.
- [ ] Move process selection and pruning helpers.
- [ ] Move CSV/argument/value helpers.
- [ ] Update the process sampler compile list.
- [ ] Run `build.ps1`.

### Task 2: Split System Sampler

- [ ] Move models and disposable counter container.
- [ ] Move performance-counter helpers.
- [ ] Move GPU query helper.
- [ ] Move process-running helpers.
- [ ] Move CSV/argument/value helpers.
- [ ] Update the system sampler compile list.
- [ ] Run `build.ps1`.

### Task 3: Verification

- [ ] Run full build/test/simulator verification.
- [ ] Confirm no CSV schema or sampler executable names changed.

