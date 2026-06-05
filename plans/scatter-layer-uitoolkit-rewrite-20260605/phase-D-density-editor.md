# Phase D -- DensityScatterLayerEditor (procedural-scatter inspector)

- Effort: M
- Parallel-safe with: E (after C). Different files; no shared serialized properties.
- Blocks: F (LOD section + DensityPaintWindow plug into D's panel)

## Scope

Build the UIToolkit inspector for DensityScatterLayer. All sections per brainstorm panel layout: Deform (affectedByWind/affectedByInteractors), Density (map field + Paint button stub), Placement (fieldBounds, scaleRange, seed, slopeRange, splat mask, groundSnapMask), Orientation, Rendering (material + shadowCastingMode), Wind (Sine/Perlin toggle with conditional sub-fields), Trample, Bounds & GPU (maxBladeHeight, bendHeadroom, chunkSize).

LOD section is added in F (not here -- D leaves a slot).

DensityTextureField is filled here; Paint button opens DensityPaintWindow (built in F) -- D stubs the click handler.

## File ownership

- NEW: `Editor/DensityScatterLayerEditor.cs` (registers `[CustomEditor(typeof(DensityScatterLayer))]`)
- Fill stub from B: `Editor/UI/Components/DensityTextureField.cs`
- NEW UXML: `Editor/UI/UXML/DensityLayer.uxml` (root layout), `KindAndDeformSection.uxml`, `DensityMapSection.uxml`, `PlacementSection.uxml`, `OrientationSection.uxml`, `RenderingSection.uxml`, `WindSection.uxml`, `TrampleSection.uxml`, `BoundsAndGpuSection.uxml`

## Pre-conditions

- Phase C merged (LayerInspectorPanel exists).
- DensityTextureField stub exists in B.
- Default_Material seed asset exists in Editor/Defaults/ (Phase B).
- Default_DensityMap_512_white.png seed exists in Editor/Defaults/ (Phase B). Used as a SHAPE/FORMAT template only -- runtime density textures are generated in-code at 512x512 R8 white-filled per layer (Phase A.5 CreateDensityLayer).

## Step-by-step tasks

### D.1 -- DensityScatterLayerEditor.cs

1. `[CustomEditor(typeof(DensityScatterLayer))]` + `[CanEditMultipleObjects]` (the latter optional; explicitly fine to skip multi-edit for now).
2. `CreateInspectorGUI()` returns `new DensityScatterLayerPanel(this.serializedObject)`.
3. DensityScatterLayerPanel : BindablePanel -- loads `DensityLayer.uxml`, adds it to root, then calls per-section `Bind()` after every section is in the tree.

### D.2 -- DensityLayer.uxml root layout

Order of sections (top-down):

1. Header section (rename + kind icon -- already in tile, but this gives focus context in inspector).
2. Deform section (Kind + Deform combined per brainstorm naming -- `KindAndDeformSection.uxml`).
3. DensityMapSection (the density map + Paint button + targetInstances).
4. PlacementSection (fieldBounds, seed, scaleRange, groundSnapMask, slopeRange, splatLayerIndex, splatThreshold).
5. OrientationSection (rotationOffsetEuler, randomPitchRange, randomRollRange, alignToNormal).
6. RenderingSection (material, shadowCastingMode).
7. WindSection (windMode toggle + conditional Sine vs Perlin sub-fields).
8. TrampleSection (bendStrength, flatten, recoveryRate).
9. BoundsAndGpuSection (maxBladeHeight, bendHeadroom, chunkSize).
10. LodSection slot -- empty Visualelement with id `lod-section-slot`; Phase F injects LodDistanceBar + LodCards here.

### D.3 -- KindAndDeformSection

1. Kind row: read-only label "Density (procedural scatter)" (since type is fixed by class).
2. Toggle affectedByWind -- bind to property.
3. Toggle affectedByInteractors -- bind to property.
4. Helper text: "Engine routing = Grass pipeline (when either toggle is on)" or "Static-prop pipeline (when both off)".

### D.4 -- DensityMapSection (uses DensityTextureField)

1. `DensityTextureField` component (filled here): an ObjectField bound to `densityMap` SerializedProperty, with:
   - Inline 64x64 preview of the current texture (use `EditorGUIUtility.LoadIcon` for null state).
   - `Paint` button (right of field) -- click handler opens DensityPaintWindow (in F; D wires the click + temp shows a `EditorUtility.DisplayDialog("DensityPaintWindow opens in Phase F")` until F lands).
   - ValidationBadge: red if null, yellow if compressed, yellow if non-readable -- with auto-fix buttons for each: "Generate fresh density texture" (calls CreateDensityLayer-style code to make a new 512x512 R8 white sub-asset; seeds the format from Editor/Defaults/Default_DensityMap_512_white.png as a shape template only, never assigns the seed itself), "Mark readable" (toggles TextureImporter.isReadable), "Convert to R8" (rewrites the texture via uncompressed R8 GraphicsFormat).
2. targetInstances IntField bound to property; clamped >= 1.
3. Section helper text: "Density map drives where instances appear. White = full density, black = none. Paint to author."

### D.5 -- PlacementSection

1. Vector2Field fieldBounds (X = width, Y = depth).
2. IntField seed.
3. Vector2Field scaleRange (min, max).
4. LayerMaskField groundSnapMask.
5. MinMaxSlider for slopeRange (0..90 degrees) with two FloatField companions.
6. Splat mask: IntField splatLayerIndex (-1 to disable) + Slider splatThreshold (0..1) -- hidden when splatLayerIndex < 0 via change listener.

### D.6 -- OrientationSection

1. Vector3Field rotationOffsetEuler.
2. MinMaxSlider randomPitchRange (-180..180) + companion fields.
3. MinMaxSlider randomRollRange (-180..180) + companion fields.
4. Toggle alignToNormal.
5. Helper label "Oriented mode: <on/off>" reflecting derived `IsOriented` value -- recompute on any change.

### D.7 -- RenderingSection

1. ObjectField material (Material type).
2. EnumField shadowCastingMode.
3. ValidationBadge: red if material is null; auto-fix "Assign Default_Material" assigns the in-place reference at `Assets/GrassInteract/Editor/Defaults/Default_Material.mat` (per the D2 seed model -- material is the shareable in-place seed).

### D.8 -- WindSection (conditional sub-fields per windMode)

1. EnumField windMode.
2. Vector2Field windDirection (auto-normalize on change via TrackPropertyValue).
3. Slider windStrength (0..2).
4. Sine sub-fields (windFrequency, windNoiseScale) -- container with `style.display` toggled to None when windMode != Sine.
5. Perlin sub-fields (windGustScale, windRippleScale, windGustSpeed, windRippleSpeed, windRippleWeight) -- same conditional display, opposite mode.
6. Implement display switching via `TrackPropertyValue(windModeProp, _ => RefreshDisplay())` -- avoid OnGUI polling.

### D.9 -- TrampleSection + BoundsAndGpuSection

1. Trample: Sliders bendStrength (0..4), flatten (0..1), recoveryRate (>=0).
2. Bounds & GPU: FloatField maxBladeHeight (min 0.01), FloatField bendHeadroom (>=0), IntField chunkSize (min 1).
3. All standard SerializedProperty binds.

### D.10 -- DensityPaintWindow stub

1. The Paint button click handler in D.4: stub call to `DensityPaintWindow.Open(densityMap)`. F creates the window class with the actual paint logic.
2. Until F lands, the stub shows a DisplayDialog "Phase F not yet merged" and returns. After F merges, the call is real (no D edit needed -- F adds the static method on the new window class).

## Validation criteria

1. Compile clean.
2. Open a freshly created DensityScatterLayer in the inspector -- every section appears, every field binds, no console warnings.
3. Wind mode switch: change EnumField to Perlin -- Sine fields hide, Perlin fields appear; switch back -- the opposite.
4. Splat layer index = -1 hides splatThreshold; >= 0 shows it.
5. Validation badge on missing material is red; click auto-fix; material assigned; badge turns green.
6. Light + dark theme: render both, no contrast issues.
7. Edit any value -> ScatterField.RebuildLayer fires via the existing OnValidate -> NotifyChanged path (visible in scene).
8. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Conditional display via TrackPropertyValue doesn't fire on Undo/Redo | 3 | 3 | 9 | Also subscribe to `Undo.undoRedoPerformed` in BindablePanel and call RefreshDisplay; named handler, unsubscribed on detach. |
| MinMaxSlider doesn't bind cleanly to Vector2 -- known UIToolkit quirk | 3 | 3 | 9 | Use a TwoFloatField + MinMaxSlider combo (composite widget) with explicit value-change wiring; document the pattern in EDITOR-UI-GUIDE.md. |
| Density texture preview leaks GC by re-creating Image every refresh | 2 | 2 | 4 | Reuse a single `Image` element; only swap `image.image = texture` on change. |
| EnumField initial value not synced from SerializedProperty on first show | 2 | 2 | 4 | After cloning UXML, call `panel.Bind(serializedObject)` BEFORE first paint; UIToolkit will sync. |

## Effort: M

Estimate 3-5 hours. Mostly mechanical UXML + bind plumbing; conditional display is the only finesse.
