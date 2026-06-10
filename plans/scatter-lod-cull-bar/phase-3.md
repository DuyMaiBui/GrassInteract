# Phase 3 — Distance bar drawer (read-only render → draggable handles)

Effort: **M** · Blocked by Phase 1 (`RenderCullDistance`) · Parallel-safe with Phase 2 (Editor-only files).

## Objective

Add a Unity-LODGroup-style segmented distance bar to the editor: LOD0 / LOD1 / LOD2 colored segments + a final red
**Culled** segment, with **closeness %** labels (`1 - distance/renderCullDistance`, 100% at camera, 0% at cull) and absolute
metres on hover. Implement the READ-ONLY render first; THEN add draggable handles that edit the LOD switch distances +
`renderCullDistance` through `SerializedProperty` (SSOT) and repaint.

## Files owned

- `Assets/GrassInteract/Editor/ScatterStudio/LodDistanceBar.cs` — **new** self-contained IMGUI drawer (the bar logic).
- `Assets/GrassInteract/Editor/ScatterStudio/LayerPanelView.cs` — host the bar inside the existing "Render" foldout card.
- `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — optional secondary host (inspector summary), if cheap.

## Host decision (resolved)

Scatter Studio uses **UI Toolkit** (`PropertyField` foldout cards — `LayerPanelView.AddFoldoutCard("Render","render")`,
line 76). The bar is fiddly IMGUI, so build it as a **self-contained IMGUI block** (`LodDistanceBar.Draw(Rect, SerializedProperty renderProp)`)
and host it from UI Toolkit via an `IMGUIContainer`. This keeps ONE bar implementation usable from both the UI-Toolkit
Scatter Studio and any IMGUI inspector — no Odin `OdinValueDrawer<ScatterRenderConfig>` needed (avoids Odin-vs-UITK
host coupling; the drawer reads `SerializedProperty` directly, which both hosts provide).

## Change instructions — Step A: read-only render FIRST

### 1. New `LodDistanceBar.cs`

Static drawer keyed off the serialized `render` property. Read sub-properties by relative path:
- LOD switch distances: `render.lods` array → element `[i].maxDistance` for `i in [0 .. lods.Length-2]` (the last LOD's
  `maxDistance` is NOT a switch — it's bounded by cull; mirror `LodMaxDistances` which returns `length-1` switches).
- Cull: `render.renderCullDistance` (the new field; NOT the collider `cullDistance`).

```csharp
#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// IMGUI segmented LOD distance bar (Unity-LODGroup style). Step A = read-only render.
    /// Segment width = band span / renderCullDistance. Closeness % label = 1 - dist/cull. Metres on hover.
    /// </summary>
    internal static class LodDistanceBar
    {
        private static readonly Color[] LOD_COLORS =
        {
            new Color(0.36f, 0.62f, 0.30f), // LOD0 green
            new Color(0.85f, 0.70f, 0.25f), // LOD1 amber
            new Color(0.70f, 0.45f, 0.25f), // LOD2 brown
        };
        private static readonly Color CULLED_COLOR = new Color(0.65f, 0.20f, 0.20f); // red

        public const float BAR_HEIGHT = 26f;

        /// <summary>Draws the read-only bar. renderProp is the SerializedProperty for the "render" struct.</summary>
        public static void Draw(Rect rect, SerializedProperty renderProp)
        {
            SerializedProperty lods = renderProp.FindPropertyRelative("lods");
            float cull = renderProp.FindPropertyRelative("renderCullDistance").floatValue;
            if (cull <= 0f) { EditorGUI.HelpBox(rect, "renderCullDistance is 0 — set it to draw the LOD bar.", MessageType.Info); return; }

            int lodCount = lods != null ? lods.arraySize : 0;
            // Switch distances: lods[0..lodCount-2].maxDistance, ascending. Last band ends at cull.
            float prev = 0f;
            for (int i = 0; i < lodCount; ++i)
            {
                float bandEnd = (i < lodCount - 1)
                    ? lods.GetArrayElementAtIndex(i).FindPropertyRelative("maxDistance").floatValue
                    : cull; // last LOD bounded by cull
                Rect seg = SliceByDistance(rect, prev, bandEnd, cull);
                EditorGUI.DrawRect(seg, LOD_COLORS[Mathf.Min(i, LOD_COLORS.Length - 1)]);
                DrawSegLabel(seg, $"LOD{i}", prev, bandEnd, cull);
                prev = bandEnd;
            }
            // Final red Culled segment is conceptually [cull..∞); show a thin cap at the far edge.
            // (cull maps to the bar's right edge, so the Culled marker is the right boundary label.)
        }

        // Map a distance to an x within rect (0m → left, cull → right).
        private static Rect SliceByDistance(Rect rect, float dStart, float dEnd, float cull)
        {
            float x0 = rect.x + rect.width * Mathf.Clamp01(dStart / cull);
            float x1 = rect.x + rect.width * Mathf.Clamp01(dEnd / cull);
            return new Rect(x0, rect.y, Mathf.Max(1f, x1 - x0), rect.height);
        }

        private static void DrawSegLabel(Rect seg, string lod, float dStart, float dEnd, float cull)
        {
            // Closeness % at the band's NEAR edge (1 - dist/cull). Metres shown on hover via tooltip.
            float closeNear = 1f - Mathf.Clamp01(dStart / cull);
            var content = new GUIContent($"{lod}\n{closeNear * 100f:0}%", $"{dStart:0}–{dEnd:0} m");
            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(seg, content, style);
        }
    }
}
```
> Closeness convention is applied exactly as approved: `closeness = 1 - distance/renderCullDistance`. The hover tooltip carries
> absolute metres (`dStart–dEnd m`). Segment width is `bandSpan / cull` via `SliceByDistance`.

### 2. Host in `LayerPanelView.cs` (UI Toolkit)

In `AddFoldoutCard("Render", "render")` (line 76), after the render `PropertyField` is added, append an `IMGUIContainer`
that draws the bar from the layer's `SerializedObject`:
```csharp
            // After the "Render" foldout/card is built, attach the LOD distance bar.
            SerializedProperty renderProp = this.layerSO.FindProperty("render");
            if (renderProp != null)
            {
                var bar = new UnityEngine.UIElements.IMGUIContainer(() =>
                {
                    Rect r = UnityEngine.GUILayoutUtility.GetRect(0f, LodDistanceBar.BAR_HEIGHT,
                        UnityEngine.GUILayout.ExpandWidth(true));
                    LodDistanceBar.Draw(r, renderProp);
                });
                // add `bar` to the same foldout/card that hosts the "render" PropertyField.
            }
```
> Implementer note: `AddFoldoutCard` currently builds the card locally and adds it to `panelContainer`. Either return the
> `Foldout` from `AddFoldoutCard` for the Render case, or add a dedicated `AddRenderCardWithBar()` so the `IMGUIContainer`
> lands inside the Render foldout. Keep the change surgical — do not refactor the generic `AddFoldoutCard` signature for
> the other cards.

### Step A verification
- Open Scatter Studio on a layer with cull set. The bar renders LOD0/LOD1/LOD2 colored segments sized by distance, with
  closeness % labels; hovering a segment shows the metre range. No interaction yet. Editing a numeric LOD/cull field and
  re-selecting the layer updates the bar.

## Change instructions — Step B: draggable handles (only after Step A renders correctly)

Add handle hit-testing + drag to `LodDistanceBar`:
1. Compute handle x-positions at each switch distance and at `cull` (the bar's right edge).
2. On `EventType.MouseDown` within a handle's grab rect (±4px), capture the handle index (`GUIUtility.hotControl`).
3. On `MouseDrag`, convert `Event.current.mousePosition.x` → distance (`(mx - rect.x)/rect.width * cull`), clamp between
   the neighboring handles (monotonic: each switch > previous, < next; the cull handle > last switch), and write back:
   ```csharp
   SerializedProperty target = isCullHandle
       ? renderProp.FindPropertyRelative("renderCullDistance")
       : lods.GetArrayElementAtIndex(handleIndex).FindPropertyRelative("maxDistance");
   target.floatValue = newDistance;
   renderProp.serializedObject.ApplyModifiedProperties(); // SSOT write-back → numeric fields sync automatically
   ```
4. On `MouseUp`, release `hotControl`. Call `EditorUtility.SetDirty` / rely on `ApplyModifiedProperties` (it records undo
   and marks dirty), then request a repaint of the host `IMGUIContainer` (`MarkDirtyRepaint`).
5. Dragging the cull handle past the last switch is clamped (cull ≥ lastSwitch); dragging a switch is clamped within its
   neighbors so bands never invert.

> SSOT is enforced by writing ONLY through `SerializedProperty` + `ApplyModifiedProperties`. The numeric inspector fields
> read the same serialized data, so they update with no extra sync code. Never mutate the `ScatterRenderConfig` struct
> directly from the drawer.

### Step B verification
- Drag each switch handle: the matching numeric `maxDistance` field updates live; bands resize; cannot cross neighbors.
- Drag the cull handle: `renderCullDistance` numeric field updates; the Culled boundary moves; cannot go below the last switch.
- Undo (Ctrl+Z) reverts a drag (recorded by `ApplyModifiedProperties`). The runtime cull (Phase 2) honors the new value.

## Per-phase risk

- **Draggable hit-testing / SSOT break (score 9):** mitigated by the read-only-first split (Step A ships and is verified
  before any drag code) and by writing back exclusively through `SerializedProperty.ApplyModifiedProperties`.
- **Host mismatch (score 4):** the bar is a host-agnostic IMGUI block taking a `SerializedProperty`; usable from the
  UI-Toolkit Scatter Studio (via `IMGUIContainer`) and any IMGUI inspector. No Odin dependency.
- Off the critical path — slipping Phase 3 does not block Phase 2/4.
