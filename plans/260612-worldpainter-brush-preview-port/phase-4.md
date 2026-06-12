# Phase 4 — Cleanup (delete BrushDecal shader, prove zero refs, final verify)

Effort: **S** · Blocks: none · Blocked by: Phase 3

## Goal

Now that Phase 3 removed the last referencer of the decal shader, delete
`Assets/WorldPainter/Shaders/BrushDecal.shader` and its `.meta`, prove there are zero dangling
references across the project, then run a final compile + full `TerrainBrushMathTests` pass to
confirm the whole change set is green.

## File ownership

- `Assets/WorldPainter/Shaders/BrushDecal.shader` (delete)
- `Assets/WorldPainter/Shaders/BrushDecal.shader.meta` (delete)

## Exact steps

### 1. Prove zero references BEFORE deleting

Grep the whole `Assets/` tree for any remaining mention of the shader by file name AND by its
`Shader.Find` string `WorldPainter/BrushDecal`:

```bash
grep -rn "BrushDecal" Assets/WorldPainter/ Assets/ 2>/dev/null
grep -rn "WorldPainter/BrushDecal" Assets/ 2>/dev/null
```

Expected after Phase 3: the ONLY hits are the two files being deleted (`BrushDecal.shader` and its
`.meta`). The prior referencers were `TerrainBrushPreview.cs` (rewritten in Phase 3 — no longer
references it) and `WorldPainterSculptTool.cs` (only the `Set` call site, which never named the
shader). If grep shows any OTHER source file still referencing `BrushDecal` or
`WorldPainter/BrushDecal`, STOP — that is a dangling reference; resolve it before deleting.

Also confirm no material asset references the shader GUID (shaders are referenced by GUID in
`.mat`/`.asset` files). Read `BrushDecal.shader.meta` for its GUID, then:

```bash
SHADER_GUID=$(grep -m1 "guid:" Assets/WorldPainter/Shaders/BrushDecal.shader.meta | awk '{print $2}')
grep -rln "$SHADER_GUID" Assets/ 2>/dev/null
```

Expected: only `BrushDecal.shader.meta` itself (no `.mat`/`.asset` references the GUID, because the
old material was created at runtime via `Shader.Find`, never serialized).

### 2. Delete the shader + meta

```bash
git rm "Assets/WorldPainter/Shaders/BrushDecal.shader" "Assets/WorldPainter/Shaders/BrushDecal.shader.meta"
```

(Use `git rm` so the deletion is staged atomically with the rest of the change set. If the files
are untracked-modified, fall back to `rm` + `git add -A` on those explicit paths.)

### 3. Final verification

1. Let Unity recompile / refresh assets. `read_console` MUST be clean — specifically NO
   "Shader 'WorldPainter/BrushDecal' not found" warning (that warning would prove a lingering
   `Shader.Find` call survived Phase 3).
2. Run the full `TerrainBrushMathTests` suite — ALL tests green (circle + the 3 square parity tests
   from Phase 2).
3. Re-run the grep from step 1 — `grep -rn "BrushDecal" Assets/` returns ZERO hits (files gone, no
   source references).

## Verify gate

- `grep -rn "BrushDecal" Assets/` → zero hits.
- Final compile clean, no shader-not-found warning in the console.
- Full `TerrainBrushMathTests` run green (circle + square).

## Rollback

`git checkout` restores both deleted files. Because Phase 3's rewrite no longer calls
`Shader.Find("WorldPainter/BrushDecal")`, restoring the shader alone is inert (nothing references
it) — a full rollback of this feature requires reverting Phase 3 as well.
