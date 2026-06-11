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
3. `git mv` runtime files in bulk (script the move from the P1 table). Each `.cs` carries its `.meta`.
4. `git mv` shader/compute/hlsl assets.
5. Remove old runtime asmdefs + their `.meta`.
6. Editor + test asmdefs STILL reference `GpuTerrain`/`GrassInteract` by name → they now break. **Temporarily** point `GpuTerrain.Editor`/`GrassInteract.Editor`/both test asmdefs to reference `WorldPainter` so the project compiles. (These die in P4/P5.)
7. `refresh_unity(force)` → background-watch DLL mtime → `read_console` (all errors) → `run_tests` (301).

## Verification
`read_console` clean; `run_tests` = 301 green; `git status` shows moves as renames (R100). `find Assets -name 'GpuTerrain.asmdef' -o -name 'GrassInteract.asmdef'` for the runtime two → gone.

## Rollback
`git reset --hard <P2 sha>` — moves are mechanical; tree returns to pre-move compiling state.

## Risk
R2 (.meta loss) — verify rename detection; R3 mitigated by keeping old namespaces. Budget a LONG domain reload (full recompile).
