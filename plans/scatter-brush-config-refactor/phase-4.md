# Phase 4 — Brush Stamps + WYSIWYG Preview + AssetPostprocessor

**Plan:** `plan.md` · **Brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md`
**Effort:** M (3d) · **Depends on:** Phase 3 (editor + brush controls already in config editor)

## Goal

Add Photoshop-style brush stamps (grayscale shape textures) replacing the procedural circular falloff when assigned. Render a **WYSIWYG** scene-view cursor preview that bakes opacity + falloff into the cursor texture itself. Listen for external texture-edit reimports via `AssetPostprocessor` so artists editing density maps in Krita/Photoshop see the update in-editor without restarting.

## Deliverables

1. `Editor/ScatterBrush.cs` — `Stamp(...)` accepts a `BrushStamp?` (null = procedural). Stamp sampling uses bilinear lookup into `stamp.Shape`.
2. `Editor/ScatterAssetPostprocessor.cs` — NEW `AssetPostprocessor.OnPostprocessAllAssets`. For each imported asset, if it's a sub-asset of any `TerrainScatterConfig` and is a `Texture2D` referenced as a layer's `densityMap`, rebuild that layer in every dependent `ScatterField`. Coalesces multi-asset imports through a `delayCall` dedup.
3. `Editor/TerrainScatterConfigEditor.cs` — add **import-stamp** button in Brushes tab; add `[PreviewField]` per stamp; per-layer **import-density-PNG** button. Scene-view preview replaces wire-disc with textured cursor (bake stamp × opacity × falloff color into a transparent quad).
4. Demo paints with both procedural and imported stamps. Editing a density texture's pixels externally and saving triggers a field rebuild within ~1 editor frame.

## File ownership

| Path | Owner | Action |
|---|---|---|
| `Assets/GrassInteract/Editor/ScatterBrush.cs` | EDIT | Stamp shape lookup, textured cursor preview |
| `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` | EDIT | Stamp dropdown per layer, import buttons |
| `Assets/GrassInteract/Editor/ScatterAssetPostprocessor.cs` | NEW | Watch sub-asset density-texture reimports |

**Out of scope:**
- Migration (Phase 5)
- Cross-config brush-stamp sharing (each config owns its own stamps for now; cross-config sharing is a v2 ask)

## Task breakdown

### T4.1 — `BrushStamp` sub-asset import (~1 hr)

Add `TerrainScatterConfig.ImportBrushStamp(string sourcePath, string displayName)`:

```csharp
internal BrushStamp ImportBrushStamp(string sourcePath, string displayName)
{
    var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
    if (source == null) throw new System.Exception($"Stamp source not found: {sourcePath}");

    // Read source pixels (must be readable in import settings).
    if (!source.isReadable)
        throw new System.Exception($"Stamp source '{source.name}' is not readable. Enable Read/Write in import settings.");

    int w = source.width, h = source.height;
    var copy = new Texture2D(w, h,
        UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
        UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
    copy.name = $"{displayName}_Stamp";
    copy.wrapMode = TextureWrapMode.Clamp;
    copy.filterMode = FilterMode.Bilinear;

    Color[] src = source.GetPixels();
    for (int i = 0; i < src.Length; ++i)
        src[i] = new Color(src[i].grayscale, src[i].grayscale, src[i].grayscale, 1f);
    copy.SetPixels(src);
    copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    copy.hideFlags = HideFlags.HideInHierarchy;
    UnityEditor.AssetDatabase.AddObjectToAsset(copy, this);

    var stamp = ScriptableObject.CreateInstance<BrushStamp>();
    stamp.name = displayName;
    stamp.hideFlags = HideFlags.None;
    UnityEditor.AssetDatabase.AddObjectToAsset(stamp, this);
    stamp.hideFlags = HideFlags.HideInHierarchy;
    stamp.SetShape(copy, displayName);

    this.brushStamps.Add(stamp);
    UnityEditor.EditorUtility.SetDirty(this);
    UnityEditor.AssetDatabase.SaveAssets();
    return stamp;
}
```

### T4.2 — Editor: import buttons (~1.5 hr)

In `TerrainScatterConfigEditor`'s Brushes tab:

```csharp
if (GUILayout.Button("Import Stamp from PNG/EXR..."))
{
    string path = EditorUtility.OpenFilePanel(
        "Import Brush Stamp", Application.dataPath, "png,jpg,jpeg,exr,tga");
    if (string.IsNullOrEmpty(path)) return;

    // Convert absolute path → asset-relative
    string relative = "Assets" + path.Substring(Application.dataPath.Length).Replace("\\", "/");
    string displayName = System.IO.Path.GetFileNameWithoutExtension(path);
    config.ImportBrushStamp(relative, displayName);
}
```

In each layer's BoxGroup, add:

```csharp
if (GUILayout.Button("Import Density Map from PNG..."))
{
    // Overwrite the layer's densityMap sub-asset pixels from a source PNG.
    // Open file panel, GetPixels from source, SetPixels into the existing sub-asset Texture2D.
    // Triggers normal RebuildLayer via NotifyChanged.
}
```

### T4.3 — `ScatterBrush.Stamp` with stamp shape (~2 hr)

Refactor the existing procedural circular falloff to support optional stamp sampling:

```csharp
internal void Stamp(
    Vector3 worldHit, ScatterField field, ScatterLayer layer, bool paint,
    float brushRadius, float brushStrength, float brushFalloff,
    BrushStamp? stamp)                          // NEW param (null = procedural)
{
    /* ... cx, cy, prx, pry as before ... */

    Texture2D? shape = stamp?.Shape;            // null → procedural
    float sign = paint ? 1f : -1f;

    for (int y = minY; y <= maxY; ++y)
    {
        for (int x = minX; x <= maxX; ++x)
        {
            float ndx = (x - cx) / prx;
            float ndy = (y - cy) / pry;
            float nd  = Mathf.Sqrt(ndx * ndx + ndy * ndy);
            if (nd > 1f) continue;

            float weight;
            if (shape != null)
            {
                // Map (ndx, ndy) ∈ [-1,1]² to stamp UV [0,1]²; sample bilinear grayscale.
                float u = (ndx + 1f) * 0.5f;
                float v = (ndy + 1f) * 0.5f;
                weight = shape.GetPixelBilinear(u, v).r;       // R8 → r channel
                // Multiply by procedural falloff so falloff slider still works on top of stamp.
                weight *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, brushFalloff, nd));
            }
            else
            {
                weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, brushFalloff, nd));
            }

            int idx = y * this.texWidth + x;
            this.buffer[idx] = Mathf.Clamp01(this.buffer[idx] + sign * brushStrength * weight);
        }
    }
    this.bufferDirty = true;
}
```

### T4.4 — WYSIWYG scene-view cursor (~3 hr)

Replace the wire-disc preview in `TerrainScatterConfigEditor.OnSceneGUI` with a textured quad that shows the stamp shape, opacity, and falloff baked in.

Approach: cache a `Material` (using `Hidden/Internal-GUITexture` or a custom unlit transparent shader) + a `RenderTexture` per stamp. On hit:

```csharp
private void DrawTexturedCursor(Vector3 hitPoint, Vector3 hitNormal)
{
    Texture preview = this.activeStamp != null
        ? (Texture)this.activeStamp.Shape!
        : this.GetProceduralFalloffTexture();   // cached radial-gradient tex

    Color tint = this.activeTool == PaintTool.Paint
        ? new Color(0.3f, 1f, 0.4f, this.brushStrength)
        : new Color(1f, 0.4f, 0.3f, this.brushStrength);

    Handles.color = tint;
    Matrix4x4 m = Matrix4x4.TRS(hitPoint,
        Quaternion.LookRotation(Vector3.forward, hitNormal),    // billboard to normal
        Vector3.one * (this.brushRadius * 2f));
    using (new Handles.DrawingScope(m))
    {
        // Draw a unit quad with the preview texture.
        Graphics.DrawTexture(new Rect(-0.5f, -0.5f, 1f, 1f), preview, this.cachedTintMat);
    }
}
```

`GetProceduralFalloffTexture()` generates a 128×128 R8 radial gradient once, caches it, and rebuilds when `brushFalloff` changes (`if (Mathf.Abs(falloff - this.cachedFalloff) > 0.01f) Regenerate();`).

Falls back to the existing wire-disc if texture rendering errors out (try/catch).

### T4.5 — `ScatterAssetPostprocessor` (~2 hr)

```csharp
internal sealed class ScatterAssetPostprocessor : AssetPostprocessor
{
    private static readonly HashSet<ScatterField> dirtyFields = new();
    private static bool scheduled;

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Length == 0) return;

        // Find all TerrainScatterConfig assets touched by these imports.
        foreach (string path in imported)
        {
            if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) continue;

            var config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(path);
            if (config == null) continue;

            // For each scene ScatterField referencing this config, schedule a rebuild.
            var fields = UnityEngine.Object.FindObjectsByType<ScatterField>(
                UnityEngine.FindObjectsSortMode.None);
            foreach (var f in fields)
                if (f != null && f.isActiveAndEnabled && f.Config == config)
                    dirtyFields.Add(f);
        }

        if (dirtyFields.Count > 0 && !scheduled)
        {
            scheduled = true;
            EditorApplication.delayCall += FlushRebuilds;
        }
    }

    private static void FlushRebuilds()
    {
        scheduled = false;
        foreach (var f in dirtyFields)
            if (f != null && f.isActiveAndEnabled)
                f.Rebuild();    // full rebuild — postprocessor doesn't know which layer changed
        dirtyFields.Clear();
        SceneView.RepaintAll();
    }
}
```

Optimization: track which texture inside the config was the actual import, look up which layer references it, and call `RebuildLayer(idx)` instead. For Phase-4 ship-now, full rebuild is acceptable; mark as TODO for v2.

### T4.6 — Stamp dropdown in active layer's brush block (~1 hr)

```csharp
// In the active layer's brush BoxGroup:
var stamps = config.BrushStamps;
string[] names = new string[stamps.Count + 1];
names[0] = "Procedural";
for (int i = 0; i < stamps.Count; ++i) names[i + 1] = stamps[i].DisplayName;

int idx = this.activeStamp == null ? 0 : stamps.IndexOf(this.activeStamp) + 1;
int newIdx = EditorGUILayout.Popup("Stamp", idx, names);
this.activeStamp = newIdx == 0 ? null : stamps[newIdx - 1];
```

### T4.7 — Smoke (~1 hr)

1. Import a soft-round PNG via Brushes tab → stamp appears in dropdown.
2. Paint with it → density follows the stamp shape, not a circle.
3. Edit the demo's density texture in Krita, save → field rebuilds within ~1 second of Unity regaining focus.
4. Procedural fallback still works when stamp = null.
5. Wire-disc → textured cursor visible in scene view, color reflects paint/erase + opacity.

## Success criteria

- ✅ Compile clean.
- ✅ Imported stamp lives as a sub-asset (visible in Project view when config is expanded).
- ✅ Paint stroke shape visibly matches the imported stamp's grayscale shape.
- ✅ Scene cursor IS the stamp (with tint + opacity), not a wire ring.
- ✅ External edit of a sub-asset density texture triggers a rebuild within ~1s after Unity refocuses.
- ✅ Demo renders byte-identical to Phase-3 baseline when no stamp painting was done.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Imported stamp source is non-readable → silent fallback to procedural without notifying user | 3 | 3 | 9 | Import throws + surfaces dialog "Enable Read/Write in import settings of source PNG"; do NOT silently fall back |
| Postprocessor rebuild storm during bulk reimport | 3 | 3 | 9 | T4.5 dedups via HashSet + single delayCall; profile after Reimport All (NEVER triggered by us — but artist might) |
| Textured cursor uses per-frame `Graphics.DrawTexture` → editor frame stutter | 2 | 3 | 6 | Cache tint material once; do not allocate Color array per frame; profile in T4.7 smoke |
| Procedural-falloff texture regeneration every slider tick | 3 | 2 | 6 | Only regen when falloff delta > 0.01f, AND limit to once per editor frame |
| Stamp `GetPixelBilinear` per pixel inside brush loop dominates Stamp cost | 3 | 2 | 6 | Pre-cache `Color[] stampPixels = stamp.GetPixels()` per active stamp; sample manually with `(int u, int v)` lookup |
| `EditorUtility.OpenFilePanel` returns absolute path outside Assets/ — assets created outside the project | 2 | 2 | 4 | Validate `path.StartsWith(Application.dataPath)` before relative conversion; else `EditorUtility.DisplayDialog` "Source must live under Assets/" and abort |

## Verification commands (Unity MCP)

```
mcp__UnityMCP__set_active_instance(unity_instance="GrassInteract@<hash>")
mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Demo/GrassInteractDemo.unity")
# Manual stamp import + paint test → screenshot scene view
mcp__UnityMCP__read_console(types=["Error", "Warning"], count=50)
```
