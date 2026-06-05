# R1 Report — IScatterPlacement Strategy

## Status: code on disk, gate deferred to main loop

## Files created
- Runtime/IScatterPlacement.cs (18 lines)
- Runtime/DensityPlacement.cs (207 lines)  — body mirrors GrassScatter.Build procedural branch (lines 82..235)
- Runtime/InstancePlacement.cs (108 lines) — body mirrors GrassScatter.BuildFromAuthored (lines 293..353)

## Pre-flight grep
- `class GrassLayer` reference count: 0 (legacy alias already retired)
- `GrassScatter.Build` reference count: N (consumers unchanged — wiring lands in R5)

## Notes / divergences from existing bodies
- `BuildFieldBounds` is `private static` in `GrassScatter` and cannot be called from outside the class without mutation.
  Per spec constraint "DO NOT edit GrassScatter.cs", the helper was duplicated as a `private static` in both
  `DensityPlacement` and `InstancePlacement`. R5 (which promotes abstract + deletes Obsolete) is the natural
  point to make `GrassScatter.BuildFieldBounds` `internal static` and remove the duplicates.
- `DensityPlacement.Build` re-derives `bounds` from the sampler at the end of the method for the `BuildFieldBounds`
  call (same expression as line 87 in GrassScatter.Build) — the local variable was already named `bounds` and
  covers the full method, so no extra variable is needed; the earlier local was reused directly.
- `InstancePlacement.Build` uses `this.layer.AuthoredInstances!.GetRuntimeRecords()` inline (non-null asserted)
  matching the pattern in `GrassScatter.Build`'s skip-path caller where the null check has already been done.
  The placement class itself does not re-check `HasAuthoredInstances` — the router (currently `GrassScatter.Build`,
  later R5 code) is responsible for dispatching to the correct implementation.
- All three files: `#nullable enable`, `this.` prefix on all member access, no `using UnityEditor`, camelCase
  private fields, no underscore prefix — per code-conventions-unity.md.
