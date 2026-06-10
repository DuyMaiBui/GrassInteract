# Phase 4 — Validation (boundary tests + migration parity)

Effort: **S** · Blocked by Phase 2 (asserts the cull boundary) · Benefits from Phase 1 (migration parity).

## Objective

Prove the cull fix with automated EditMode boundary tests (edit == play, cull=500 → present at 499 / culled at 501) and
confirm an existing layer asset migrated by Phase 1 keeps visual parity.

## Files owned

- `Assets/GrassInteract/Tests/EditMode/ScatterLodCullTests.cs` — **new** EditMode test.

> Confirm the test asmdef host first: `find Assets/GrassInteract -name "*.asmdef"` and locate the existing EditMode test
> assembly (it must reference the GrassInteract runtime + `UnityEditor`). If none exists, add a `Tests/EditMode/` folder
> with an asmdef referencing the runtime asmdef, `UnityEngine.TestRunner`, and `UnityEditor.TestRunner`. Match the
> project's existing test layout if one is present.

## Test instructions

### 1. Cull-boundary band test (pure math — no GPU)

The squared-distance bands are deterministic. Test the cull semantics that all three engines share by asserting on the
config + the band logic directly (no Unity render needed):

```csharp
[Test]
public void RenderCullDistance500_BandsAreCorrect()
{
    // Arrange: 3-LOD config, switches 12 / 30, cull 500.
    var lods = new[]
    {
        new ScatterLod { mesh = null, maxDistance = 12f },
        new ScatterLod { mesh = null, maxDistance = 30f },
        new ScatterLod { mesh = null, maxDistance = 0f }, // last LOD bounded by cull
    };
    var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 500f);

    // maxSqrDistance computed exactly as the engines now do: cull * cull.
    float maxSqr = cfg.RenderCullDistance * cfg.RenderCullDistance;
    Assert.AreEqual(500f * 500f, maxSqr, 1e-3f);

    // Present just inside cull, culled just outside.
    Assert.Less(499f * 499f, maxSqr);   // 499m → rendered
    Assert.Greater(501f * 501f, maxSqr); // 501m → culled
}
```

### 2. Edit == play parity test

Assert the cull value has NO `Application.isPlaying` dependence — the same `RenderCullDistance²` is produced regardless of mode.
Since the engines now read `cfg.RenderCullDistance` with no branch, this is a regression guard that the `isPlaying` formula did
not creep back:

```csharp
[Test]
public void RenderCullDistance_IsModeIndependent()
{
    var lods = new[] { new ScatterLod { maxDistance = 30f }, new ScatterLod { maxDistance = 0f } };
    var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 750f);
    float maxSqr = cfg.RenderCullDistance * cfg.RenderCullDistance;
    Assert.AreEqual(750f * 750f, maxSqr, 1e-3f); // no 250000f play floor, no 1e8f editor floor
}
```

### 3. Migration default test (Phase 1 guard)

```csharp
[Test]
public void Migration_BackfillsRenderCullDistance_PreservingLegacyFarCull()
{
    // Legacy asset: cull == 0, last switch == 30 → expect max(2*30, 500) == 500.
    var lods = new[] { new ScatterLod { maxDistance = 12f }, new ScatterLod { maxDistance = 30f }, new ScatterLod() };
    var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 0f);

    float[] dists = cfg.LodMaxDistances;           // length == 2 → [12, 30]
    float lastSwitch = dists.Length > 0 ? dists[^1] : 0f;
    float migrated = Mathf.Max(2f * lastSwitch, 500f);
    Assert.AreEqual(500f, migrated, 1e-3f);

    // Large last switch → 2*lastSwitch wins.
    var lods2 = new[] { new ScatterLod { maxDistance = 50f }, new ScatterLod { maxDistance = 400f }, new ScatterLod() };
    var cfg2 = new ScatterRenderConfig(null, default, lods2, 0f);
    Assert.AreEqual(800f, Mathf.Max(2f * cfg2.LodMaxDistances[^1], 500f), 1e-3f);

    // <2 LODs → empty dists → 500 floor, no throw.
    var lods1 = new[] { new ScatterLod { maxDistance = 0f } };
    var cfg1 = new ScatterRenderConfig(null, default, lods1, 0f);
    Assert.AreEqual(500f, Mathf.Max(2f * (cfg1.LodMaxDistances.Length > 0 ? cfg1.LodMaxDistances[^1] : 0f), 500f), 1e-3f);
}
```

> These tests duplicate the migration formula deliberately as a SPEC LOCK — if the engine/layer formula drifts, the test
> and the implementation disagree and someone must reconcile. They are the executable definition of the bands + migration.

## Manual in-editor verification (the visual gate)

1. Compile clean; run the EditMode suite via Test Runner — ALL pass, zero failures.
2. Pick ONE pre-existing layer asset. Before Phase 1 it would show `renderCullDistance` unset/0; after migration confirm a
   non-zero default and that the scatter looks the SAME as before the change (no near-field popping, no missing far
   instances within the old range). This is the migration parity check.
3. With `renderCullDistance = 500`, confirm the in-editor distance check from Phase 2 (present ~499m, culled ~501m, edit == play)
   for both a grass and a prop layer.

## Per-phase risk

- Tests are pure-math/spec-lock — low flake risk, no GPU dependency, fast under domain reload.
- The only judgment item is the manual visual parity check (step 2); it is the human gate that the migration default did
  not change the look. If parity fails, revisit the Phase 1 default formula (not the tests).
- Test-pass gate (project rule): zero failures before this plan is "done"; run the FULL suite, not just compilation.
