# Phase 3 — Create WorldPainter Assembly + Move Runtime (ATOMIC)

**Effort:** L · **Blocked by:** P2 · **Compiles at boundary:** YES

## Goal
Create `Assets/WorldPainter/` + the single `WorldPainter` runtime asmdef, and `git mv` ALL runtime KEEP files (both old runtime assemblies) into `WorldPainter/Runtime/<feature>/`. **Keep the OLD namespaces** (`GpuTerrain`/`GrassInteract`) for now — only the assembly membership changes. The old `GpuTerrain ← GrassInteract` cross-assembly reference becomes intra-assembly.

## File ownership
- CREATE: `Assets/WorldPainter/WorldPainter.asmdef` (name+rootNamespace `WorldPainter`, references `[]`, all platforms — per plan §2.1).
- `git mv` all GpuTerrain/Runtime + GrassInteract/Runtime KEEP `.cs` (+ their `.meta`) into `Runtime/{Terrain,Render,Scatter,Biome}/` per §2.2.
- `git mv` `Shaders/*` (terrain + grass compute/hlsl/shader, EXCEPT delete-targets) into `Assets/WorldPainter/Shaders/`.
- DELETE the 2 old runtime asmdefs (`GpuTerrain.asmdef`, `GrassInteract.asmdef`) — but only AFTER all their `.cs` moved, else orphaned files lose assembly.

## Steps (ATOMIC — one batch, one verify)
1. Pin instance; confirm clean tree.
2. Create `WorldPainter/` dirs + `WorldPainter.asmdef`.
3. `git mv` runtime files in bulk (script the move from the P1 table). **CRITICAL — `git mv X.cs` does NOT move `X.cs.meta`.** For EVERY file you MUST move the `.cs` AND its `.cs.meta` as a pair: `git mv old/X.cs new/X.cs && git mv old/X.cs.meta new/X.cs.meta`. Do ALL moves with the Unity editor NOT refreshing (do NOT call `refresh_unity` until every pair is moved) — if Unity refreshes while a `.cs` exists without its `.meta`, it regenerates a NEW GUID and breaks every asset/scene/prefab reference. This is the R2 failure that killed the first P3 attempt.
4. `git mv` shader/compute/hlsl assets — same `.asset`+`.meta` pair discipline.
5. Remove old runtime asmdefs + their `.meta`.
6. Editor + test asmdefs STILL reference `GpuTerrain`/`GrassInteract` by name → they now break. **Temporarily** point `GpuTerrain.Editor`/`GrassInteract.Editor`/both test asmdefs to reference `WorldPainter` so the project compiles. (These die in P4/P5.) Also ensure any `[assembly: InternalsVisibleTo(...)]` grants (4 AssemblyInfo files: `GpuTerrainAssemblyInfo.cs`, `GpuTerrainEditorAssemblyInfo.cs`, `GrassInteract/Editor/EditorAssemblyInfo.cs`, `Scatter/AssemblyInfo.cs`) still name the editor/test assemblies that exist in P3 (`GpuTerrain.Editor`, `GrassInteract.Editor`, `GpuTerrain.EditorTests`, `GrassInteract.EditorTests`) — the runtime `internal` members are now in the `WorldPainter` assembly, so the grant must live on `WorldPainter`'s AssemblyInfo. The first P3 attempt blocked here.
7. **GUID-preservation self-check BEFORE refreshing Unity:** for a few moved files, confirm `grep '^guid:' new/X.cs.meta` matches `git show HEAD:old/X.cs.meta | grep '^guid:'`. If ANY differs, STOP — a meta was lost; fix before refreshing (do NOT refresh, which locks in the damage).
8. `refresh_unity(force)` → background-watch DLL mtime → `read_console` (all errors) → `run_tests` (301).

## Verification
`read_console` clean; `run_tests` = 301 green; `git status` shows moves as renames (R100). `find Assets -name 'GpuTerrain.asmdef' -o -name 'GrassInteract.asmdef'` for the runtime two → gone.

## Rollback
`git reset --hard <P2 sha>` — moves are mechanical; tree returns to pre-move compiling state.

## Risk
R2 (.meta loss) — verify rename detection; R3 mitigated by keeping old namespaces. Budget a LONG domain reload (full recompile).
