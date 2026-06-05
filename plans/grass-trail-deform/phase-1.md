# Phase 1 — `GrassTrailInteractor` + sampler + stroke breaks + gizmo

Effort: M. Depends on: nothing. Blocks: Phases 2, 3, 4.
Goal: a new MonoBehaviour that maintains a TrailRenderer-style FIFO of samples, supports stroke breaks via `Emitting`, and exposes a segment iterator the Phase 2 upload will consume. NO GPU buffer yet, NO shader change yet. Pure C# + Editor gizmo.

## Scope — file ownership

NEW:
- `Assets/GrassInteract/Runtime/GrassTrailInteractor.cs` — the component (see § Component API below).

UNCHANGED: every other file. `GrassInteractor.cs`, `GrassGpuEngine.cs`, `GrassInteractIndirect.shader` — DO NOT touch in this phase.

## Component API (LOCKED)

```csharp
#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    [ExecuteAlways] [DisallowMultipleComponent]
    public sealed class GrassTrailInteractor : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float trailDuration       = 5f;
        [Min(0f)] [SerializeField] private float minVertexDistance   = 0.25f;
        [Min(0f)] [SerializeField] private float worldRadius         = 2f;
        [Range(0f, 90f)] [SerializeField] private float maxBendDegrees    = 90f;
        [Range(0f, 1f)]  [SerializeField] private float centerZonePercent = 0.4f;
        [Range(0f, 1f)]  [SerializeField] private float strength          = 1f;

        [Tooltip("Optional. On Reset(), defaults are copied from this TrailRenderer's " +
                 "time / minVertexDistance / widthMultiplier. At runtime this field is " +
                 "NOT read - the trail deform runs independently.")]
        [SerializeField] private TrailRenderer? linkedTrailRenderer;

        public bool Emitting { get; set; } = true;

        public float TrailDuration       => this.trailDuration;
        public float WorldRadius         => this.worldRadius;
        public float MaxBendDegrees      => this.maxBendDegrees;
        public float CenterZonePercent   => this.centerZonePercent;
        public float Strength            => this.strength;

        // Per-sample state; see § Sampler algorithm.
        internal struct TrailSample
        {
            public Vector3 PosWS;
            public float   Age;
            public bool    StrokeStart;
        }

        // Pre-allocated to a hard cap (see § Capacity). Read-only view for Phase 2 upload.
        internal IReadOnlyList<TrailSample> Samples => this.samples;

        private static readonly List<GrassTrailInteractor> active = new();
        public  static IReadOnlyList<GrassTrailInteractor> Active => active;

        // ...sampler internals (see § Sampler algorithm)...
    }
}
```

XML doc on `linkedTrailRenderer` is mandatory and must include the "runtime not read" warning (R7 mitigation).

## Sampler algorithm (LateUpdate)

Single pre-allocated `List<TrailSample> samples` with `Capacity = MAX_SAMPLES_PER_TRAIL = 256`. Never grow at runtime.

```
private const int MAX_SAMPLES_PER_TRAIL = 256;
private readonly List<TrailSample> samples = new(MAX_SAMPLES_PER_TRAIL);
private bool wasEmittingLastFrame = true;
private bool pendingStrokeBreak;

private void LateUpdate()
{
    float dt = Application.isPlaying ? Time.deltaTime : Time.unscaledDeltaTime;

    // 1) Tick ages on ALL existing samples (regardless of Emitting).
    for (int i = 0; i < samples.Count; i++)
    {
        var s = samples[i]; s.Age += dt; samples[i] = s;
    }

    // 2) Evict samples whose age > trailDuration. Walk forward; first survivor index is N.
    int firstAlive = 0;
    while (firstAlive < samples.Count && samples[firstAlive].Age > trailDuration)
        firstAlive++;
    if (firstAlive > 0)
        samples.RemoveRange(0, firstAlive);

    // 3) Edge-detect Emitting (R5): if it just flipped false this frame, queue a stroke break.
    bool emittingNow = this.Emitting;
    if (this.wasEmittingLastFrame && !emittingNow)
        this.pendingStrokeBreak = true;

    // 4) If not emitting, skip sample emission (steps 5-6).
    if (emittingNow)
    {
        // 5) Emit a new sample if list empty OR moved > minVertexDistance.
        bool firstSample = samples.Count == 0;
        bool moved = !firstSample &&
                     Vector3.Distance(this.transform.position, samples[^1].PosWS) > this.minVertexDistance;
        if (firstSample || moved)
        {
            if (samples.Count >= MAX_SAMPLES_PER_TRAIL)
                samples.RemoveAt(0);                          // overflow = drop oldest

            samples.Add(new TrailSample
            {
                PosWS       = this.transform.position,
                Age         = 0f,
                StrokeStart = this.pendingStrokeBreak || firstSample,
            });
            this.pendingStrokeBreak = false;
        }
    }

    // 6) Cache for next frame's edge detection.
    this.wasEmittingLastFrame = emittingNow;
}
```

## Registry (mirrors `GrassInteractor`)

```csharp
private void OnEnable()
{
    if (!active.Contains(this)) active.Add(this);
}

private void OnDisable()
{
    active.Remove(this);
    samples.Clear();                  // dropped trail vanishes when component disabled
    pendingStrokeBreak = false;
    wasEmittingLastFrame = true;
}
```

## Reset() — `linkedTrailRenderer` defaults copy

```csharp
private void Reset()
{
    if (this.linkedTrailRenderer == null) return;
    this.trailDuration     = this.linkedTrailRenderer.time;
    this.minVertexDistance = this.linkedTrailRenderer.minVertexDistance;
    this.worldRadius       = this.linkedTrailRenderer.widthMultiplier * 0.5f;
}
```

Only runs once when the component is added (or Reset menu is invoked). Runtime never reads `linkedTrailRenderer`.

## CPU-tier warn (R9)

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
private bool hasValidated;
private void Update()
{
    if (this.hasValidated) return;
    this.hasValidated = true;

    ScatterField[] fields = FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
    bool anyGpu = false;
    foreach (var f in fields) if (f.isActiveAndEnabled && f.ActiveTierName == "GPU") { anyGpu = true; break; }
    if (!anyGpu)
        Debug.LogWarning($"[{nameof(GrassTrailInteractor)}] '{this.name}' is active but no " +
            "GPU-tier ScatterField exists. GrassTrailInteractor is GPU-only - " +
            "this trail will have no visual effect.", this);
}
#endif
```

(Cross-check `ScatterField.ActiveTierName` accessor — exists per project status. If not exactly named, use the equivalent.)

## Gizmo (Editor)

```csharp
#if UNITY_EDITOR
private void OnDrawGizmos()
{
    if (samples.Count == 0) return;

    // Polyline through all samples, alpha-tinted by 1 - age/duration.
    for (int i = 1; i < samples.Count; i++)
    {
        var prev = samples[i - 1];
        var cur  = samples[i];
        float aPrev = 1f - prev.Age / Mathf.Max(trailDuration, 1e-4f);
        float aCur  = 1f - cur.Age  / Mathf.Max(trailDuration, 1e-4f);
        float aSeg  = 0.5f * (aPrev + aCur);

        // Stroke-start sample = pen lift; draw a distinct color and SKIP the bridge segment.
        if (cur.StrokeStart)
        {
            UnityEditor.Handles.color = new Color(1f, 0.4f, 0.1f);  // orange tick
            UnityEditor.Handles.DrawSolidDisc(cur.PosWS, Vector3.up, 0.15f);
            continue;
        }

        UnityEditor.Handles.color = new Color(0.3f, 0.9f, 0.4f, aSeg);
        UnityEditor.Handles.DrawLine(prev.PosWS, cur.PosWS);
    }

    // Width disc at every sample.
    foreach (var s in samples)
    {
        float a = 1f - s.Age / Mathf.Max(trailDuration, 1e-4f);
        UnityEditor.Handles.color = new Color(0.3f, 0.9f, 0.4f, 0.25f * a);
        UnityEditor.Handles.DrawWireDisc(s.PosWS, Vector3.up, worldRadius);
    }
}
#endif
```

## Verification gate (live-editor evidence)

1. `set_active_instance GrassInteract` FIRST.
2. **Compile gate**: 0 C# errors / warnings on `manage_editor.read_console`.
3. **PlayMode test** (in-Editor, no scripted asset):
   - Place a `GrassTrailInteractor` on a moving cube (script that translates it linearly).
   - Confirm in `execute_code`: `GrassTrailInteractor.Active.Count == 1`.
   - After 1 s of motion at 2 m/s, `samples.Count ≈ 8` (2 m/s × 1 s / 0.25 m).
   - After `trailDuration + 1 s` of stillness: `samples.Count == 0`.
4. **Stroke-break test**:
   - Toggle `Emitting = false` for 0.5 s mid-motion, then back to `true`.
   - Inspect samples list: exactly ONE sample after the toggle has `StrokeStart == true`.
   - Toggle twice within one frame: collapses to single transition (R5 mitigation).
5. **Gizmo gate**: Scene-view screenshot shows the polyline, the width discs, and the orange stroke-start tick at the gap location.

Pass = component registers correctly, sampler grows/evicts as spec, stroke-start bit set on resume only, gizmo readable.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Sampler allocates each frame (List<>.Add growth) | 2 | 3 | 6 | Pre-allocate `List<TrailSample>` with capacity 256; cap evicts oldest before adding; never call ctor in hot path. Profiler gate in verification step 3. |
| Edge detection misses Emitting double-toggle in one frame | 2 | 2 | 4 | Cache `wasEmittingLastFrame` AFTER all decisions; explicit test in verification step 4. |
| FindObjectsByType<ScatterField> in Update is expensive | 3 | 1 | 3 | Editor-only + DEVELOPMENT_BUILD gated, single-shot (`hasValidated` latch). Same pattern existing `GrassInteractor.Update` uses. |
| Gizmo allocates per-frame (UnityEditor.Handles) | 1 | 1 | 1 | Editor-only; not in player builds; not in hot path. |
| ScatterField.ActiveTierName accessor doesn't exist under that exact name | 2 | 2 | 4 | Grep before writing the warn block; use whatever the existing accessor is (project status confirms an accessor exists). |

## Rollback

Delete `Assets/GrassInteract/Runtime/GrassTrailInteractor.cs` and its `.meta`. No other files reference it. Scene Inspector references go red (missing script) but field rebuilds and renders unchanged.
