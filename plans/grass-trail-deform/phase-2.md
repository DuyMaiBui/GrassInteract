# Phase 2 — `GrassTrailBuffer` + GPU upload

Effort: S. Depends on: Phase 1 (`GrassTrailInteractor.Active` + `Samples` accessor). Blocks: Phase 3.
Goal: snapshot all active trail samples into a `StructuredBuffer<TrailSegmentGpu>` each frame, skip pairs separated by a stroke break, set the count global. No shader read yet — Phase 3 wires the VS loop. Mirrors `GrassInteractorBuffer` lifecycle exactly.

## Scope — file ownership

NEW:
- `Assets/GrassInteract/Runtime/GrassTrailBuffer.cs` — owns a `GraphicsBuffer(Target.Structured)` sized `MAX_TRAIL_SEGMENTS = 128`, a `TrailSegmentGpu[]` staging array, an `Upload(IReadOnlyList<GrassTrailInteractor>)` that flattens + skips stroke-start pairs + SetData, and `Dispose`. Defines blittable `TrailSegmentGpu` (48 B, 16-byte aligned).

MODIFIED:
- `Assets/GrassInteract/Runtime/GrassGpuEngine.cs` — own a `GrassTrailBuffer`; in `Step(dt)` (after the existing interactor upload) call `trailBuffer.Upload(GrassTrailInteractor.Active)`; bind the buffer as a shader global (`_GrassTrailSegments`) once in `Build()`, set count global (`_GrassTrailSegmentCount`) each frame.

UNCHANGED: `GrassInteractor.cs`, `GrassInteractorBuffer.cs`, `GrassInteractIndirect.shader` (Phase 3 owns the shader edit), `GrassBendSimulator.cs` (CPU tier).

## GPU type (LOCKED — match HLSL byte-for-byte in Phase 3)

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct TrailSegmentGpu              // 48 B total
{
    public Vector3 PosA;        // 12
    public float   Radius;      //  4  -> 16
    public Vector3 PosB;        // 12
    public float   Alpha;       //  4  -> 32
    public float   MaxBendRad;  //  4
    public float   CenterPct;   //  4
    public float   Strength;    //  4
    public float   Pad;         //  4  -> 48 (16-byte aligned)
}
```

Verify `Marshal.SizeOf<TrailSegmentGpu>() == 48` in a one-line `Debug.Assert` at engine Build (mirrors Phase-6 `GrassInteractorBuffer` discipline). HLSL struct in Phase 3 MUST mirror field order + types.

## Capacity

- `MAX_TRAIL_SEGMENTS = 128` (UPPER_SNAKE_CASE constant). 128 × 48 B = 6 144 B GPU. Buffer allocated once at engine Build, reused every frame.
- Cross-interactor total (not per-interactor). 1 interactor × 100 segs ✓; 4 interactors × 30 segs ✓; 10 interactors × 12 segs ✓.
- Overflow handling: stop appending; `warn-once` log: `"[GrassTrailBuffer] >{MAX_TRAIL_SEGMENTS} trail segments active - dropping overflow"`.

## Upload algorithm

```csharp
internal void Upload(IReadOnlyList<GrassTrailInteractor> interactors)
{
    int segCount = 0;
    for (int t = 0; t < interactors.Count && segCount < MAX_TRAIL_SEGMENTS; t++)
    {
        var trail = interactors[t];
        if (trail == null) continue;                      // fake-null guard (edit-mode reload)
        var s = trail.Samples;
        if (s.Count < 2) continue;

        float maxBendRad = Mathf.Deg2Rad * trail.MaxBendDegrees;
        float radius     = trail.WorldRadius;
        float centerPct  = trail.CenterZonePercent;
        float strength   = trail.Strength;
        float duration   = Mathf.Max(trail.TrailDuration, 1e-4f);

        for (int i = 1; i < s.Count && segCount < MAX_TRAIL_SEGMENTS; i++)
        {
            if (s[i].StrokeStart) continue;               // pen lift - do NOT bridge

            float alphaA = 1f - s[i - 1].Age / duration;
            float alphaB = 1f - s[i    ].Age / duration;
            staging[segCount++] = new TrailSegmentGpu
            {
                PosA       = s[i - 1].PosWS,
                Radius     = radius,
                PosB       = s[i    ].PosWS,
                Alpha      = 0.5f * (alphaA + alphaB),
                MaxBendRad = maxBendRad,
                CenterPct  = centerPct,
                Strength   = strength,
                Pad        = 0f,
            };
        }
    }

    if (segCount > 0) buffer.SetData(staging, 0, 0, segCount);
    if (interactors.Count > 0 && segCount == MAX_TRAIL_SEGMENTS && !warnedOverflow)
    {
        Debug.LogWarning($"[GrassTrailBuffer] >{MAX_TRAIL_SEGMENTS} trail segments active - dropping overflow.");
        warnedOverflow = true;
    }

    Shader.SetGlobalInteger(GrassTrailSegmentCountId, segCount);
}
```

- `staging` is a pre-allocated `TrailSegmentGpu[MAX_TRAIL_SEGMENTS]` field. Never reallocated.
- Global IDs cached as `int` via `Shader.PropertyToID("_GrassTrailSegmentCount")` etc. Names use `_GrassTrail` prefix (R2 mitigation).
- `Shader.SetGlobalBuffer(_GrassTrailSegments, buffer)` is called ONCE in `Build()` (the same `GraphicsBuffer` ref is reused — SetData updates contents, not binding).

## GrassGpuEngine wiring

```csharp
// in class fields:
private GrassTrailBuffer? trailBuffer;

// in Build():
this.trailBuffer = new GrassTrailBuffer();
this.trailBuffer.BindGlobal();      // SetGlobalBuffer once

// in Step(float dt):
this.interactorBuffer.Upload(GrassInteractor.Active);       // existing
this.trailBuffer.Upload(GrassTrailInteractor.Active);       // NEW

// in Dispose():
this.trailBuffer?.Dispose();
this.trailBuffer = null;
```

## Verification gate (live-editor evidence)

1. `set_active_instance GrassInteract` FIRST.
2. **Compile gate**: 0 C# errors. `manage_editor.read_console`.
3. **Marshal size**: `execute_code` → `System.Runtime.InteropServices.Marshal.SizeOf<GrassTrailBuffer.TrailSegmentGpu>()` returns `48`.
4. **Round-trip**: spawn 1 `GrassTrailInteractor` with `samples.Count = 5` (4 segments expected). After one `Step()`, read `_GrassTrailSegmentCount` global via `Shader.GetGlobalInteger` → 4.
5. **Stroke-break upload skip**: same trail but `samples[2].StrokeStart = true`. After `Step()`: count → 3 (skip the bridge from samples[1] to samples[2]).
6. **Overflow**: spawn enough samples across interactors to total ≥ 129. Console logs ONE overflow warning; count returns exactly 128; no NRE.
7. **Profiler — 0 GC alloc**: Profiler window shows zero GC.Alloc on `GrassGpuEngine.Step` after warm-up. The staging array is reused; SetData(T[], int, int, int) does not allocate.
8. **Regression — interactor unchanged**: existing GPU render of the demo orbit effector unchanged (Phase 3 has not added the shader read yet, so trails have zero visual effect). Top-down screenshot identical to pre-Phase-2.
9. **No-trail no-op**: with `GrassTrailInteractor.Active.Count == 0`, `Step()` sets count = 0; no SetData call. Profiler shows no GPU upload.

Pass = correct segment count + stroke-skip + cap respected + 0 GC + interactor render unchanged.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| `TrailSegmentGpu` stride mismatch CPU vs HLSL | 2 | 4 | 8 | `Marshal.SizeOf == 48` Debug.Assert at Build. HLSL Phase 3 reviewer signs off the field-order parity. |
| Per-frame allocation in segment flatten | 3 | 3 | 9 | Pre-allocated `staging[MAX_TRAIL_SEGMENTS]`; for-loop over `IReadOnlyList<T>` does NOT enumerate-via-IEnumerator (indexer access is alloc-free). Profiler gate in verification step 7. |
| Global name collision (`_TrailSegments`) | 2 | 4 | 8 | Use `_GrassTrail` prefix. Grep before commit. |
| `Shader.SetGlobalBuffer` on every Step burns CPU | 1 | 1 | 1 | Bind ONCE in Build; SetData per frame. SetGlobalInteger every frame is fine (it's a cheap uniform set). |
| Overflow warn fires every frame when capped | 2 | 1 | 2 | `warnedOverflow` latch. Reset only if buffer is disposed + rebuilt. |
| Fake-null interactor after domain reload | 2 | 2 | 4 | `if (trail == null) continue;` in upload loop. Same pattern existing `GrassInteractorBuffer.Upload` uses. |

## Rollback

Delete `Assets/GrassInteract/Runtime/GrassTrailBuffer.cs` and its `.meta`. In `GrassGpuEngine.cs` remove: the field, the Build line (`new GrassTrailBuffer().BindGlobal()`), the Step line (`trailBuffer.Upload(...)`), the Dispose line. Phase 1 component becomes a dead-but-harmless registry entry; field renders unchanged.
