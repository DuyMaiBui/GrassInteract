# Brainstorm — "grass can't interact" root cause + debug-logging design (2026-06-02)

## Problem statement
User: grass won't flatten under a `GrassInteractor`; add debug logging to find root cause "if missing any step". User confirmed symptom occurs **even in the demo scene** (regression vs prior verified-working sessions).

## Investigation (live, GrassInteract@de203215, GrassInteractDemo.unity)
Walked the full 7-link interaction chain against the live editor. **Every C# link passed:**

| # | Link | Live result |
|---|------|-------------|
| 1 | Interactor registered | ✓ "Effector", r=2.5, s=1, at origin, enabled |
| 2 | Enabled `GrassTrampleMap` | ✓ HasActiveInstance=true |
| 3 | `GrassInteractField` binds `_GrassFieldRect` | ✓ (-20,-20,40,40) |
| 4 | Interactor inside field | ✓ origin = center |
| 5 | radius/strength > 0 | ✓ |
| 6 | Trample RT hot + published | ✓ `GrassTrampleRT` RHalf 512, max **0.9775** (0.999 when saturated), global bound to live RT |
| 7 | Grass material = deform shader | ✓ `GrassInteractDemo` on `GrassInteract/InstancedGrass`, both shaders found+supported |

Then forced a field-wide fold (interactor radius→60, whole RT saturated to 0.999) and screenshotted: **grass stayed fully upright.** Data path flawless, no fold.

### Isolation ladder (shader side)
1. Cleared `Library/ShaderCache/shader/` + `EditorEncounteredVariants`, reimport, domain reload → still upright.
2. Confirmed blade mesh UVs `uv.y=[0..1]` → `heightT` is fine (not the cause).
3. Debug-viz (hijack frag to output sampled trample, red=hot/green=0): **whole field RED** → the GPU sample of the global RHalf RT returns ~1.0 correctly. Sampling works.
4. Unconditional `posWS.y = pivotWS.y + vertical*0.1` inline in `.shader` vert → **field went flat** → geometry-write path works.
5. **Inlined the exact `ApplyDeform` fold logic into the `.shader` vert → field folds correctly. Calling `GrassInteract_ApplyDeform()` from the `GrassInteractDeform.hlsl` include under identical conditions → does NOT fold.**

## Root cause (isolated)
The blade fold delivered through the **`.hlsl` include** is not applied; identical **inline** `.shader` code works. Matches the project's documented **stale-include shader-cache trap** (editing the `.shader` recompiles, but a stale preprocessed copy of the include is fed to the compiler). Confirmation blocked because **`Library/ShaderCache.db` is locked by the running editor** and cannot be deleted mid-session — a full editor restart is the canonical cache-clear.

**Key reframe:** a C# "debug log per step" would NOT have caught this — every C# link passed; the break is purely GPU/shader-include side.

## Resolution plan (approved)
1. **User restarts Unity** (clears locked `ShaderCache.db`). [BLOCKING — agent must not kill/restart editor per `unity-forbidden-operations`.]
2. Shader reverted to clean source (no debug edits remain — verified via grep). After restart, agent re-verifies whether `ApplyDeform` folds:
   - Folds → root cause was purely stale-include cache; **zero code change needed.**
   - Still broken → genuine include bug; apply inline-into-all-3-passes fix (forward/shadow/depth).

## Diagnostic deliverable design (approved: C# + shader-side)
Consolidated one-shot **interaction-chain self-check** (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`, auto-run once on enable + `Tools/GrassInteract/Diagnose Interaction Chain` menu item):
- Walks all 7 links, prints single PASS/FAIL report naming the broken link.
- Fills the 3 currently-silent gaps: (a) interactor register confirmation, (b) **missing `GrassInteractField`** (today: `_GrassFieldRect`=zero → off-field check early-returns silently), (c) **grass material's shader doesn't sample `_GrassTrampleMap`**.
- **Shader-side detector:** "RT is hot but blades not folding" — GPU-readback of trample max + a menu-triggered shader debug-viz (red/green sample viz) so the include-cache class is catchable next time.

## Next steps
- [ ] User: restart Unity editor.
- [ ] Agent: re-verify `ApplyDeform` fold post-restart; decide code-change-needed or not.
- [ ] `/t1k:plan` the diagnostic deliverable (C# chain self-check + shader-side detector).
