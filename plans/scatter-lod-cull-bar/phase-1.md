# Phase 1 — Data model + OnValidate migration

Effort: **S** · Blocks Phase 2 and Phase 3 · No blocker.

## Objective

Add an explicit `renderCullDistance` to `ScatterRenderConfig` and a migration default on each concrete layer so existing
serialized assets (which have `renderCullDistance == 0`) keep their current visible range instead of culling everything at 0.

> Field name is `renderCullDistance` (NOT `cullDistance`) to avoid colliding with the EXISTING collider-culling
> `cullDistance` field already on `InstanceScatterLayer`. The new field is purely the render/LOD far-cull boundary.

## Files owned

- `Assets/GrassInteract/Runtime/ScatterRenderConfig.cs` — add field + accessor.
- `Assets/GrassInteract/Runtime/InstanceScatterLayer.cs` — add `OnValidate` migration.
- `Assets/GrassInteract/Runtime/DensityScatterLayer.cs` — add `OnValidate` migration.

> The migration lives on the LAYER, not the struct: `ScatterRenderConfig` is a `[Serializable] struct` and cannot run
> `OnValidate` itself. Both layers serialize the config as a private field named `render` (`InstanceScatterLayer.cs:22`,
> `DensityScatterLayer.cs:21`), exposed via `public override ScatterRenderConfig Render => this.render;`.

## Change instructions

### 1. `ScatterRenderConfig.cs` — add the field, accessor, and update the ctor

Current `lods` field block (lines 24-26):
```csharp
        [BoxGroup("LOD Render")]
        [Tooltip("Per-LOD mesh + switch distance pairs. LOD0 (highest detail) first.")]
        [SerializeField] private ScatterLod[] lods;
```
Add directly AFTER it:
```csharp
        [BoxGroup("LOD Render")]
        [Tooltip("Hard render cull distance (metres). Instances beyond this distance are not rendered. " +
                 "The last LOD covers [last LOD switch distance .. renderCullDistance); past it = CULLED.")]
        [Min(0f)]
        [SerializeField] private float renderCullDistance;
```

Update the constructor (lines 28-33) to take and assign `renderCullDistance`:
```csharp
        public ScatterRenderConfig(Material? material, ShadowCastingMode shadowCastingMode, ScatterLod[] lods, float renderCullDistance)
        {
            this.material = material;
            this.shadowCastingMode = shadowCastingMode;
            this.lods = lods;
            this.renderCullDistance = renderCullDistance;
        }
```
> Grep for the ctor call site before editing: `grep -rn "new ScatterRenderConfig(" Assets/GrassInteract/`. If any caller
> exists, add the new arg there (likely zero callers — the struct is normally serialized, not constructed). If zero
> callers, the ctor signature change is internal-only.

Add the accessor next to the existing ones (after line 37 `public ScatterLod[] Lods => ...`):
```csharp
        /// <summary>Hard render cull distance (metres). Instances past this distance do not render.</summary>
        public float RenderCullDistance => this.renderCullDistance;
```

> `LodMaxDistances` (lines 51-62) stays UNCHANGED — it still returns `lods.Length - 1` switch distances. The last
> LOD's own `maxDistance` is still intentionally not a "switch"; the new `renderCullDistance` is the far boundary. Phase 2
> reads `RenderCullDistance` for the far cull and `LodMaxDistances` for the LOD0→1 / LOD1→2 switches exactly as today.

### 2. `InstanceScatterLayer.cs` and `DensityScatterLayer.cs` — add migration

Add this method to EACH layer class (both serialize `this.render`). Place near the other Unity lifecycle/validation
members; if the class already has a `Validate(out string)` override, put `OnValidate` adjacent to it.

> Method name `MigrateRenderCullDistance` is intentional — it migrates the render-config `renderCullDistance`. Do NOT
> touch or rename the unrelated `cullDistance` collider field on `InstanceScatterLayer`.

```csharp
#if UNITY_EDITOR
        private void OnValidate()
        {
            this.MigrateRenderCullDistance();
        }

        /// <summary>
        /// Back-fills <see cref="ScatterRenderConfig.RenderCullDistance"/> for assets serialized before the field existed
        /// (renderCullDistance == 0 → everything would cull at 0). Defaults to max(2 * second-last LOD switch, 500) to
        /// preserve the legacy derived-formula far cull. Idempotent: only writes when renderCullDistance is still 0.
        /// </summary>
        private void MigrateRenderCullDistance()
        {
            if (this.render.RenderCullDistance > 0f)
                return;

            float[] dists = this.render.LodMaxDistances; // length == lods.Length - 1
            // Legacy far cull was max(2 * secondLastLODdistance, 500). secondLastLODdistance == last switch distance.
            float lastSwitch = dists.Length > 0 ? dists[dists.Length - 1] : 0f;
            float migrated = Mathf.Max(2f * lastSwitch, 500f); // <2 LODs (dists empty) → 500m floor

            this.render = new ScatterRenderConfig(
                this.render.Material,
                this.render.ShadowCastingMode,
                this.render.Lods,
                migrated);

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
```

> Rationale for `max(2*lastSwitch, 500)`: the legacy formula was `max(lod1MaxSqrDist * 4, minCull)` in SQUARED space,
> i.e. `max(2 * lod1, 500/10000)` in linear space. We mirror the **play-mode** 500m floor (the edit-mode 10000m floor
> was a SceneView workaround, not a real look — preserving it would over-extend migrated assets). `<2 LODs` → `dists`
> is empty → fall to the 500m floor, which is correct and never throws.

> Add `using UnityEngine;` is already present in both files (they use `[SerializeField]`). The `#if UNITY_EDITOR` guard
> keeps `EditorUtility` out of player builds.

## Verification steps

1. Compile clean (`read_console` → 0 errors). `renderCullDistance` appears in the Render foldout of both layer types.
2. Open an EXISTING scatter layer asset (one serialized before this change). Confirm `renderCullDistance` auto-fills to a
   non-zero value (≥ 500) after the asset is touched/selected (OnValidate fires on load/inspect). It must NOT be 0.
3. Create a NEW layer, set renderCullDistance explicitly — confirm `OnValidate` does NOT clobber a user-set non-zero value
   (idempotent guard `> 0f`).
4. Confirm an asset with only 1 LOD (empty `dists`) migrates to exactly 500 without error.

## Per-phase risk

**Migration is the single highest project risk (score 20).** If `OnValidate` does not fire on an asset before Phase 2's
hard-cull goes live, that asset culls at 0. Mitigations: the `> 0f` idempotency guard means re-running is safe; Phase 4
adds an automated parity test; and `SetDirty` ensures the migrated value persists. Edge case to verify explicitly:
assets with `< 2` LODs (the `dists` array is empty) — covered by the 500m floor branch.
