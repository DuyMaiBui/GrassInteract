# Phase 2 — ScatterField Slim-down + RebuildLayer Fast-path

**Plan:** `plan.md` · **Brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md`
**Effort:** M (3d) · **Depends on:** Phase 1 (config sub-asset APIs exist)

## Goal

Strip `ScatterField` to its essentials (`config + boundTerrain`) and add `RebuildLayer(int idx)` — a fast path that disposes + rebuilds exactly one engine instead of the whole field. Replace the `delayCall` chain with a synchronous re-entry-guarded rebuild. Phase 1's `ScatterLayer.NotifyChanged()` is rewired to call this new fast path.

## Deliverables

1. `Runtime/ScatterField.cs` — drop `layers`, `cullCompute`, `indirectMaterial` inline fields (now Required from config). Add `RebuildLayer(int)`. Extract tier-selection into a shared helper used by both `Rebuild` and `RebuildLayer`. Replace `OnValidate` + `delayCall` with a guarded synchronous path. `config` becomes `[Required]`.
2. `Runtime/ScatterLayer.cs` — rewrite `NotifyChanged()` to find dependent fields and call `RebuildLayer(idx)`.
3. `Runtime/TerrainScatterConfig.cs` — `NotifyDependents` (already there) routes to the new fast path where possible.
4. Demo renders byte-stable. No console errors. Editing a `ScatterLayer.slopeRange` value updates the field within one editor frame, **no domain reload**.

## File ownership

| Path | Owner | Action |
|---|---|---|
| `Assets/GrassInteract/Runtime/ScatterField.cs` | EDIT | Drop inline fields, add RebuildLayer, replace delayCall |
| `Assets/GrassInteract/Runtime/ScatterLayer.cs` | EDIT | Rewire NotifyChanged → field.RebuildLayer |
| `Assets/GrassInteract/Runtime/TerrainScatterConfig.cs` | EDIT | NotifyDependents routes through fast path |

**Out of scope:**
- Editor scripts (Phase 3)
- AssetPostprocessor (Phase 4)
- Removing the demo's old loose `cullCompute` / `indirectMaterial` references — those move during migration (Phase 5). For this phase, the demo must already have a `TerrainScatterConfig` assigned (manual one-time fix in the demo if needed; **never** ship the demo broken between phases).

## Task breakdown

### T2.1 — Demo prep: ensure demo uses Config (~20 min)

Audit `GrassInteractDemo.unity`'s `ScatterField`:
- If `config` is already assigned → nothing to do.
- If not → assign `Demo/GrassInteractDemoScatterConfig.asset` to the field; verify the config has `cullCompute` + `indirectMaterial` + `layers` filled in; save scene.

This is a **manual one-time fix**, NOT migration. It's required so Phase 2 can delete the inline fields without breaking the demo.

### T2.2 — Slim down `ScatterField` (~2 hr)

Remove inline serialized fields:

```csharp
// DELETE these:
[SerializeField] private List<ScatterLayer> layers = new();
[SerializeField] private ComputeShader? cullCompute;
[SerializeField] private Material? indirectMaterial;

// KEEP and tighten:
[SerializeField, Required("Assign a TerrainScatterConfig — required after Phase 2 refactor.")]
private TerrainScatterConfig? config;
[SerializeField] private Terrain? boundTerrain;
[SerializeField] private GrassTierMode forceTier = GrassTierMode.Auto;
[SerializeField] private float extraCullMargin = 0f;
[SerializeField, Min(0)] private int prewarmSlabs = 0;
```

Update `Layers` accessor:

```csharp
public IReadOnlyList<ScatterLayer> Layers =>
    this.config != null ? this.config.Layers : System.Array.Empty<ScatterLayer>();
```

Update `Rebuild()` to read all shared resources from `config` (no fallback to inline):

```csharp
public void Rebuild()
{
    this.rebuilding = true;
    try
    {
        if (this.config == null)
        {
            Debug.LogError($"[{nameof(ScatterField)}] No TerrainScatterConfig assigned.", this);
            return;
        }

        IReadOnlyList<ScatterLayer> activeLayers = this.config.Layers;
        ComputeShader? activeCullCompute = this.config.CullCompute;
        Material? activeIndirectMaterial = this.config.IndirectMaterial;
        // ...rest unchanged
    }
    finally { this.rebuilding = false; }
}
```

### T2.3 — Add `RebuildLayer(int idx)` (~3 hr)

Extract tier selection + sampler/origin into a private helper:

```csharp
private struct FieldBuildContext
{
    public IReadOnlyList<ScatterLayer> Layers;
    public ComputeShader? CullCompute;
    public Material? IndirectMaterial;
    public ISurfaceSampler Sampler;
    public Vector3 Origin;
    public bool GpuCapable;
    public string ProbeReason;
}

private FieldBuildContext BuildContext()
{
    /* gather sampler/origin/tier exactly as Rebuild does today */
}
```

Then:

```csharp
public void RebuildLayer(int idx)
{
    if (this.rebuilding) return;
    this.rebuilding = true;
    try
    {
        if (this.config == null || idx < 0 || idx >= this.config.Layers.Count) return;
        var ctx = this.BuildContext();

        // Ensure parallel list size; pad with nulls if engines list shorter than layers.
        while (this.engines.Count <= idx) this.engines.Add(null);

        // Dispose existing engine at this slot.
        this.engines[idx]?.Dispose();
        this.engines[idx] = null;

        ScatterLayer? layer = ctx.Layers[idx];
        if (layer == null) return;

        // Build one engine using the SAME helpers Rebuild() uses.
        IGrassEngine? engine = (layer.Kind == ScatterLayer.ScatterKind.Mesh)
            ? this.TryBuildMeshEngine(idx, layer, ctx.Origin, ctx.Sampler, ctx.CullCompute)
            : this.BuildGrassEngine(idx, layer, ctx);

        this.engines[idx] = engine;
    }
    finally { this.rebuilding = false; }
}
```

Risk: `BuildGrassEngine` must be a strict factoring of the inline grass branch in `Rebuild()`. Lift the grass branch into a private method, call it from both. Self-test: after this phase, calling `field.Rebuild()` once vs calling `field.RebuildLayer(0..N)` in sequence must produce equal engine instance counts and equal `WorldBounds` per slot.

### T2.4 — Replace `OnValidate` `delayCall` chain (~1 hr)

```csharp
#if UNITY_EDITOR
private bool rebuilding;

private void OnValidate()
{
    if (this.rebuilding) return;
    if (!this.isActiveAndEnabled) return;
    UnityEditor.EditorApplication.delayCall += this.DeferredRebuildOnce;
}

private void DeferredRebuildOnce()
{
    UnityEditor.EditorApplication.delayCall -= this.DeferredRebuildOnce;
    if (this == null || !this.isActiveAndEnabled) return;
    this.Rebuild();
}
#endif
```

The `delayCall` is still needed because `OnValidate` runs during serialization where `Rebuild`'s GPU resource calls (CommandBuffer, ComputeBuffer) are not safe to invoke. BUT — only ONE `delayCall` is queued via the unsubscribe-on-fire pattern (no rebuild storm during slider drag). `rebuilding` flag prevents re-entry from inside.

### T2.5 — Wire ScatterLayer.NotifyChanged → RebuildLayer (~1 hr)

```csharp
internal void NotifyChanged()
{
#if UNITY_EDITOR
    var fields = UnityEngine.Object.FindObjectsByType<ScatterField>(
        UnityEngine.FindObjectsSortMode.None);
    foreach (var f in fields)
    {
        if (f == null || !f.isActiveAndEnabled || f.Config == null) continue;
        int idx = f.Config.Layers.IndexOf(this);
        if (idx >= 0)
            UnityEditor.EditorApplication.delayCall += () => {
                if (f != null && f.isActiveAndEnabled) f.RebuildLayer(idx);
            };
    }
#endif
}
```

Replace the existing `OnValidate` body of `ScatterLayer` to call `NotifyChanged()` directly (the existing `delayCall` chain can stay, deduplicated by the `rebuilding` flag on the field).

### T2.6 — Self-test harness (~1 hr)

Add `Editor/ScatterFieldRebuildLayerHarness.cs` as a `[MenuItem("Tools/GrassInteract/Self-Test/RebuildLayer Parity")]`:

1. Capture `Rebuild()` engine count, `ActiveTierName`, and each engine's `WorldBounds`.
2. Force-rebuild via N calls to `RebuildLayer(i)`.
3. Capture same outputs.
4. Assert equality. Log "PASS" / "FAIL with details".

Run once at end of phase. Keep the file (lightweight, doesn't pollute runtime asmdef).

## Success criteria

- ✅ Compile clean.
- ✅ `GrassInteractDemo` renders byte-identical to baseline (Unity MCP screenshot vs Phase-1 baseline).
- ✅ ScatterField inspector shows only: `config (Required)`, `boundTerrain`, `forceTier`, `extraCullMargin`, `prewarmSlabs`. No `layers` / `cullCompute` / `indirectMaterial` inline.
- ✅ Editing `ScatterLayer.slopeRange` in the inspector updates the visible density within ~1 editor frame, no domain reload.
- ✅ Parity harness passes.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Tier-selection extraction loses the existing "Auto: device-capable but compute/material missing → CPU + warn" branch | 3 | 4 | 12 | T2.6 parity harness explicitly tests every `forceTier` × (compute present/absent) combination |
| `RebuildLayer` rebuilds an engine that shares a pool slab with another engine → slab reuse breaks | 3 | 4 | 12 | InstanceBatchPool is field-owned across rebuilds (verified Phase 0 baseline); rebuild-one doesn't `pool.Clear()`. Dispose returns the slab to the free list; rebuild draws a fresh one |
| Layer-index reordering in the config invalidates indices held by NotifyChanged delegates | 3 | 3 | 9 | NotifyChanged re-resolves the index at call time; do not cache idx in the delegate (use the layer ref + `IndexOf`) |
| Demo scene's existing ScatterField has no config → opens broken | 2 | 4 | 8 | T2.1 pre-flight fix; commit demo scene change as a separate commit before the field refactor commit |
| Removing inline fields breaks user scenes outside the demo | 2 | 3 | 6 | This is a library-mandate-driven breaking change; surfaced via `[Required]` HelpBox; full fix is Phase 5 auto-migration |

## Verification commands (Unity MCP)

```
mcp__UnityMCP__set_active_instance(unity_instance="GrassInteract@<hash>")
mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Demo/GrassInteractDemo.unity")
mcp__UnityMCP__execute_menu_item(menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity")
mcp__UnityMCP__read_console(types=["Error", "Warning"], count=50)
mcp__UnityMCP__rendering_stats()
```
