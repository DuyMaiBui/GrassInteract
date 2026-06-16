# Phase 1 — Packing core + asmdef scaffold

Effort: M · Blocks: P2, P3, P4 · Blocked by: nothing (start here)

## Goal

Stand up the `Assets/MeshAtlas/` package with both asmdefs, and implement the two pure-C# components that have zero Editor dependencies and are fully EditMode-unit-testable: `AtlasPacker` (MaxRects) and `UVRemapper`. Also implement the pure UV-range helper the P4 guard will call. This phase produces the testable core and the public contracts (`Dictionary<Material, Rect>` sub-rects, UV remap math) every downstream phase consumes.

## File ownership (exact paths)

- `Assets/MeshAtlas/Editor/MeshAtlas.Editor.asmdef`
- `Assets/MeshAtlas/Editor/Packing/AtlasPacker.cs`
- `Assets/MeshAtlas/Editor/Packing/UVRemapper.cs`
- `Assets/MeshAtlas/Editor/Packing/UvRangeInspector.cs` (pure helper: detect UV0 outside [0,1] for a mesh; returns offending mesh list to P4)
- `Assets/MeshAtlas/Tests/Editor/MeshAtlas.Tests.asmdef`
- `Assets/MeshAtlas/Tests/Editor/AtlasPackerTests.cs`
- `Assets/MeshAtlas/Tests/Editor/UVRemapperTests.cs`
- `Assets/MeshAtlas/Tests/Editor/UvRangeInspectorTests.cs`

## asmdef contents (mirror the WorldPainter pattern)

`MeshAtlas.Editor.asmdef`:
```json
{
    "name": "MeshAtlas.Editor",
    "rootNamespace": "MeshAtlas.Editor",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`MeshAtlas.Tests.asmdef`:
```json
{
    "name": "MeshAtlas.Tests",
    "rootNamespace": "MeshAtlas.Tests",
    "references": [
        "MeshAtlas.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> Single Editor asmdef for the whole tool (the design's "MeshAtlas.Editor" + tests). No runtime asmdef — the tool is Editor-only.

## Implementation notes

- **`AtlasPacker`**: input = list of `(key, int width, int height)` source-texture sizes (key is generic, not `Material` — keep packer free of Unity material coupling; the window maps Material→key). Output = `Dictionary<TKey, Rect>` of normalized 0–1 sub-rects in a power-of-two atlas. Config: `padding` (px, default 4), `forcePowerOfTwo` (default true), `maxAtlasSize` (px, default 4096). MaxRects best-area-fit heuristic, free-rectangle list, guillotine-free splits. On overflow (cannot fit within `maxAtlasSize`) → return failure result (NOT throw silently; surface a typed result the window reports).
- **`UVRemapper`**: pure function `Vector2 Remap(Vector2 uvOld, Rect subRect) => subRect.position + Vector2.Scale(uvOld, subRect.size)`. Plus a batch overload over a `Vector2[]`.
- **`UvRangeInspector`**: given mesh UV0 array, return whether any uv falls outside `[0,1]` (with a small epsilon), and a count — P4 uses this to warn + skip.
- Conventions: `camelCase` private fields (no underscore), mandatory `this.` prefix, `PascalCase` public, `UPPER_SNAKE_CASE` constants (`DEFAULT_PADDING = 4`, `DEFAULT_MAX_ATLAS = 4096`). One responsibility per file, ≤200 lines.

## Success criteria

- Both asmdefs compile clean (verify via `read_console` after touching a `.cs`).
- `AtlasPacker` packs N rects into non-overlapping POT atlas with padding honored; overflow returns a failure result, not a crash.
- `UVRemapper` maps `(0,0)→subRect.pos`, `(1,1)→subRect.pos+subRect.size`, midpoint correct.
- `UvRangeInspector` flags a UV at `1.5` and passes a fully in-range mesh.

## Verification step

EditMode tests via Test Runner (`run_tests`, EditMode). All three test files green, zero failures. These are pure C# — no live render needed.

> asmdef gotcha: after creating the asmdefs, `refresh_unity` may no-op. Touch any `.cs` in `MeshAtlas.Editor` to force the compile, then `read_console`.

## Rollback

Delete the `Assets/MeshAtlas/` folder (+ `.meta`). No other code references it yet — zero cascade.
