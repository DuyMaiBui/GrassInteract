---
phase: R2
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: S (~0.5 day)
unity instance: GrassInteract@de203215 (port 6403)
depends on: R1 complete
---

# R2 — DensityScatterLayer + InstanceScatterLayer Concrete Subclasses

## Goal

Add two concrete `ScatterLayer` subclasses alongside the still-concrete `ScatterLayer` base. Both new types start with `[CreateAssetMenu]` attributes and empty / minimal bodies — their type-specific fields (density-map + targetInstances on `DensityScatterLayer`; authoredInstances + placeSpacing on `InstanceScatterLayer`) are populated later in R4 once consumers narrow to these types. For R2 the goal is solely that the two MonoScript GUIDs exist on disk so the migration menu in R3 has targets to swap to.

CRITICAL: `ScatterLayer` MUST REMAIN CONCRETE after this phase. Demo asset still uses base `ScatterLayer` MonoScript GUID; turning the base abstract would corrupt the demo (risk score 20). The `abstract` promotion lands only in R5 AFTER R3 migrates.

## Scope

**IN:**
- New file `Runtime/DensityScatterLayer.cs` — empty subclass of `ScatterLayer` with `[CreateAssetMenu(menuName = "GrassInteract/Density Scatter Layer", order = ...)]`.
- New file `Runtime/InstanceScatterLayer.cs` — empty subclass of `ScatterLayer` with `[CreateAssetMenu(menuName = "GrassInteract/Instance Scatter Layer", order = ...)]`.
- Provisional `CreatePlacement()` virtual stub on base `ScatterLayer.cs` ONLY IF needed for R2 to compile cleanly. If R1+R2 can coexist without touching the base, prefer that — but reading the report from R1 will tell. Default: no edit to `ScatterLayer.cs` in R2.

**OUT:**
- Field population on new subclasses (R4 owns this — once consumers narrow, fields move down the hierarchy).
- Making `ScatterLayer` abstract (R5).
- Wiring `CreatePlacement` to consumers (R5).
- Migration menu (R3).
- Any consumer edit (R4).

## File Ownership

| File | Action | Notes |
|---|---|---|
| Runtime/DensityScatterLayer.cs | CREATE | `public sealed class DensityScatterLayer : ScatterLayer`. `[CreateAssetMenu(menuName = "GrassInteract/Density Scatter Layer")]`. Class body empty initially OR contains an override stub for `CreatePlacement()` returning `new DensityPlacement(this)` if base exposes a virtual. `#nullable enable`. |
| Runtime/InstanceScatterLayer.cs | CREATE | `public sealed class InstanceScatterLayer : ScatterLayer`. `[CreateAssetMenu(menuName = "GrassInteract/Instance Scatter Layer")]`. Same shape. `#nullable enable`. |
| Runtime/ScatterLayer.cs | conditional minimal edit | Add `public virtual IScatterPlacement CreatePlacement() => new DensityPlacement(this);` ONLY IF needed to wire R5 cleanly. Stays virtual (not abstract) so base class remains concrete and demo asset still deserializes. |

## Step-by-Step Tasks

1. **Read R1 report** at `plans/scatter-layer-placement-split/phase-1-report.md` — confirm IScatterPlacement signature and that `DensityPlacement`/`InstancePlacement` take `ScatterLayer` (not concrete subtype).
2. **Read `Runtime/ScatterLayer.cs`** in full — identify existing `[CreateAssetMenu]` on the base (if any). Brainstorm noted the base currently carries one; we may need to remove it now OR leave it until R5. Default: leave existing base `[CreateAssetMenu]` until R5 — creating a base-typed asset still works while base is concrete.
3. **Author `Runtime/DensityScatterLayer.cs`:**
   - `#nullable enable`.
   - Namespace matches `ScatterLayer.cs`.
   - `[CreateAssetMenu(menuName = "GrassInteract/Density Scatter Layer", order = 1)]` — choose `order` distinct from existing.
   - `public sealed class DensityScatterLayer : ScatterLayer { }` — empty body for now.
   - Override `CreatePlacement()` ONLY IF base has a virtual: `public override IScatterPlacement CreatePlacement() => new DensityPlacement(this);`
4. **Author `Runtime/InstanceScatterLayer.cs`:**
   - Same pattern as DensityScatterLayer.
   - `[CreateAssetMenu(menuName = "GrassInteract/Instance Scatter Layer", order = 2)]`.
   - `public override IScatterPlacement CreatePlacement() => new InstancePlacement(this);` (if base virtual exists).
5. **Conditional ScatterLayer.cs edit:** If R1 left a hook point waiting (e.g. an abstract method or virtual stub), wire it now. Otherwise add ONE minimal virtual:
   ```
   public virtual IScatterPlacement CreatePlacement() => this.HasAuthoredInstances
       ? new InstancePlacement(this)
       : new DensityPlacement(this);
   ```
   This keeps base concrete + lets new sub-classes override. **Do NOT** remove or modify any existing field, attribute, or method on the base.
6. **Code conventions:** camelCase no-underscore, mandatory `this.`, `[SerializeField] private`, `#nullable enable` in new files.
7. **Write `phase-2-report.md`:** files created, base ScatterLayer.cs touched yes/no (with diff if yes), CreateAssetMenu paths chosen.

## Verification Gate

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` — FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=30)` — 0 NEW project errors.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` — no `[Parity] ERROR`.
5. Screenshot game-view -> `plans/scatter-layer-placement-split/screenshots/phase-2-render.png` — visual parity vs baseline.
6. Asset-presence check: SKIP (R2 does not migrate assets yet; demo still carries base `ScatterLayer` MonoScript GUID).

**Bonus gate (manual, optional):** In Unity Editor, right-click in Project window -> verify `Create > GrassInteract > Density Scatter Layer` and `Create > GrassInteract > Instance Scatter Layer` menu items appear.

## Exit Criteria

- Both new subclass types compile.
- `ScatterLayer` is still CONCRETE (`abstract` keyword not present).
- Demo asset still deserializes (visual parity confirms).
- Parity harness PASS.
- `phase-2-report.md` written.

## Rollback Plan

Delete `Runtime/DensityScatterLayer.cs` + `Runtime/InstanceScatterLayer.cs`. Revert any minimal `CreatePlacement` virtual stub added to `Runtime/ScatterLayer.cs`. No asset migrations yet, so rollback is mechanical.

## Anti-Stall Guard Reminders

- **First MCP call = `set_active_instance unity_instance="GrassInteract@de203215"`.**
- **No progress narration.** Read → edit → next.
- **150K commit checkpoint.** If context approaches ~150K, write `phase-2-report.md` (in-progress) and exit.
- **Do NOT promote `ScatterLayer` to abstract.** That is R5 work. If you find yourself even considering it, STOP — that change breaks the demo asset.
- **No Unity restart, no `Assets/Reimport All`.** `refresh_unity` only.
- **Edits-only-in-subagent.** Verification gate = main-loop responsibility.
