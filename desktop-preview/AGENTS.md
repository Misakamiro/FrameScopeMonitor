# FrameScope UI Preview

## Scope
- This directory is a standalone simulated desktop UI. Never use production configuration or data directories.
- Upstream NexBox is frozen at c800dc575eb8832086ed4e4bfb223f0a008deed9 in ../reference/nexbox.
- FrameScope baseline is 2c480312a6e883f46e2f5b0c529f84d5c08ecbf3.
- Directly reuse upstream UI, layouts, themes, CSS, animations and assets. Do not create replacement shells or override global styles to imitate upstream.
- Allowed changes: approved unsupported-feature removal, FrameScope business content, narrow preview data/platform adaptation.
- No hardware DLLs, real game detection, PresentMon, PawnIO installation, system optimization, updater or network service calls.
- Preserve GPL notices and document each changed upstream file.
- Unhandled preview commands must fail explicitly. Mock operations must update meaningful state, not return blanket success.
- Native overlay must reuse upstream rendering code with deterministic simulated input.
- Do not edit ../reference, old worktrees or installed FrameScope.
- Use apply_patch for manual edits. No commits or destructive cleanup without coordinator approval.
