# Phase 5 — Auto-Migration Tool

**Plan:** `plan.md` · **Brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md`
**Effort:** S (1d) · **Depends on:** Phases 1-4 (full new pipeline must be working before migration is safe)

## Goal

Convert legacy scatter assets — top-level `ScatterLayer` SOs and loose `Texture2D` density maps — into a single `TerrainScatterConfig` with everything as sub-assets. Runs automatically on first inspector open of a `ScatterField` that lacks a config. Confirmation dialog before commit; old loose assets left on disk for safety.

## Deliverables

1. `Editor/ScatterAssetMigrator.cs` — NEW. Contains:
   - `IsLegacy(ScatterField)` — heuristic: field has no `config` AND has legacy data somewhere reachable (per-project scan or scene scan).
   - `Migrate(ScatterField, bool confirmed)` — the actual conversion.
   - `[MenuItem("Tools/GrassInteract/Migrate Legacy ScatterField")]` fallback for manual invocation.
2. `Editor/ScatterFieldEditor.cs` — extend to detect legacy state and prompt + run migration on inspector open.
3. Demo migration verified: open old-style demo scene (snapshot from before Phase 2) → prompt → accept → demo renders byte-identical to post-Phase-4 baseline.

## File ownership

| Path | Owner | Action |
|---|---|---|
| `Assets/GrassInteract/Editor/ScatterAssetMigrator.cs` | NEW | Write |
| `Assets/GrassInteract/Editor/ScatterFieldEditor.cs` | EDIT | Detect legacy + invoke migrator |

**Out of scope:**
- Automatic cleanup of old loose assets (user does it manually after verifying)
- Batch migration across multiple scenes (manual one-by-one)

## Task breakdown

### T5.1 — Detection heuristic (~1 hr)

```csharp
internal static bool IsLegacy(ScatterField field)
{
    if (field == null) return false;
    if (field.Config != null) return false;       // already migrated

    // Legacy = old serialized state present.
    // Read via SerializedObject — the inline `layers`/`cullCompute`/`indirectMaterial`
    // fields were DELETED in Phase 2 source, BUT their serialized YAML may still
    // be loaded into the old scene (Unity preserves orphan fields until next save).
    using var so = new SerializedObject(field);
    var layersProp = so.FindProperty("layers");
    return layersProp != null && layersProp.isArray && layersProp.arraySize > 0;
}
```

Gotcha: Unity preserves orphan YAML fields on deserialize even after the C# field is removed. This is what makes migration possible — the YAML data is still in the scene; we just can't see it through public C# accessors.

### T5.2 — Migration core (~3 hr)

```csharp
internal static bool Migrate(ScatterField field)
{
    // Confirmation dialog
    if (!EditorUtility.DisplayDialog(
        "Migrate Legacy ScatterField",
        $"Convert legacy assets on '{field.name}' into a new TerrainScatterConfig?\n\n" +
        "• A new .asset file will be created next to the scene.\n" +
        "• All inline layers will be copied as sub-assets.\n" +
        "• Density textures will be copied (NOT moved) into the config.\n" +
        "• Old loose assets are left untouched on disk for safety.\n" +
        "• Rollback: revert scene + delete the new config asset.",
        "Migrate", "Cancel"))
        return false;

    string scenePath = field.gameObject.scene.path;
    string configPath = System.IO.Path.GetDirectoryName(scenePath) + "/"
        + field.gameObject.scene.name + "_ScatterConfig.asset";

    // Create empty config + save.
    var config = ScriptableObject.CreateInstance<TerrainScatterConfig>();
    AssetDatabase.CreateAsset(config, configPath);

    using var fso = new SerializedObject(field);

    // Copy cullCompute + indirectMaterial via SerializedObject.
    CopySerializedRef(fso, "cullCompute", config, "cullCompute");
    CopySerializedRef(fso, "indirectMaterial", config, "indirectMaterial");

    // Iterate legacy layers, clone into sub-assets.
    var legacyLayers = fso.FindProperty("layers");
    int n = legacyLayers.arraySize;
    var copiedLayers = new List<ScatterLayer>(n);

    for (int i = 0; i < n; ++i)
    {
        var legacy = legacyLayers.GetArrayElementAtIndex(i).objectReferenceValue as ScatterLayer;
        if (legacy == null) { copiedLayers.Add(null!); continue; }

        // Instantiate a copy of the legacy layer asset.
        var copy = UnityEngine.Object.Instantiate(legacy);
        copy.name = legacy.name;
        copy.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(copy, config);
        copy.hideFlags = HideFlags.HideInHierarchy;

        // Copy the density texture (pixels) into a fresh sub-asset.
        Texture2D? legacyTex = legacy.DensityMap;
        if (legacyTex != null && legacyTex.isReadable)
        {
            var texCopy = ClonePixelsR8(legacyTex, $"{copy.name}_DensityMap");
            texCopy.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(texCopy, config);

            using var lso = new SerializedObject(copy);
            lso.FindProperty("densityMap").objectReferenceValue = texCopy;
            lso.ApplyModifiedPropertiesWithoutUndo();
        }
        else if (legacyTex != null)
        {
            Debug.LogWarning(
                $"[Migrate] Layer '{legacy.name}' density texture not readable — " +
                "linking original texture instead of cloning. Enable Read/Write to copy as sub-asset.", field);
            // Keep reference to original loose texture (still works, just not sub-asset).
        }

        copiedLayers.Add(copy);
    }

    // Push copied layers into config.layers via SerializedObject.
    using (var cso = new SerializedObject(config))
    {
        var layersList = cso.FindProperty("layers");
        layersList.arraySize = copiedLayers.Count;
        for (int i = 0; i < copiedLayers.Count; ++i)
            layersList.GetArrayElementAtIndex(i).objectReferenceValue = copiedLayers[i];
        cso.ApplyModifiedPropertiesWithoutUndo();
    }

    // Wire config onto field; clear legacy YAML.
    fso.FindProperty("config").objectReferenceValue = config;
    fso.FindProperty("layers").arraySize = 0;
    var cullProp = fso.FindProperty("cullCompute");
    if (cullProp != null) cullProp.objectReferenceValue = null;
    var matProp = fso.FindProperty("indirectMaterial");
    if (matProp != null) matProp.objectReferenceValue = null;
    fso.ApplyModifiedPropertiesWithoutUndo();

    EditorUtility.SetDirty(field);
    EditorUtility.SetDirty(config);
    EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
    AssetDatabase.SaveAssets();

    Debug.Log($"[Migrate] Done. Created '{configPath}' with {copiedLayers.Count} layers. " +
              "Old loose assets remain on disk — delete them after verifying the demo runs.");

    field.Rebuild();
    return true;
}
```

`ClonePixelsR8` reuses the texture-clone helper from Phase 1 / Phase 4 (extract to a shared `ScatterTextureUtil` if duplication appears).

### T5.3 — Hook into `ScatterFieldEditor` (~30 min)

```csharp
public override void OnInspectorGUI()
{
    var field = this.target as ScatterField;
    if (field != null && ScatterAssetMigrator.IsLegacy(field))
    {
        EditorGUILayout.HelpBox(
            "This ScatterField has legacy inline layers. " +
            "Click below to migrate to a TerrainScatterConfig.",
            MessageType.Warning);
        if (GUILayout.Button("Migrate Now", GUILayout.Height(32)))
            ScatterAssetMigrator.Migrate(field);
        return;
    }
    base.OnInspectorGUI();
    /* ... Open Config button ... */
}
```

### T5.4 — Menu fallback (~15 min)

```csharp
[MenuItem("Tools/GrassInteract/Migrate Legacy ScatterField")]
private static void MigrateMenu()
{
    var field = Selection.activeGameObject?.GetComponent<ScatterField>();
    if (field == null)
    {
        EditorUtility.DisplayDialog("Migrate",
            "Select a GameObject with a ScatterField first.", "OK");
        return;
    }
    if (!ScatterAssetMigrator.IsLegacy(field))
    {
        EditorUtility.DisplayDialog("Migrate",
            "This ScatterField is not in legacy state (config already assigned or no inline data).", "OK");
        return;
    }
    ScatterAssetMigrator.Migrate(field);
}
```

### T5.5 — End-to-end test (~2 hr)

1. Git-stash all post-Phase-2 changes; check out an old demo scene from before the refactor.
2. (Or: keep a hand-rolled legacy scene file specifically for this test under `Assets/GrassInteract/Tests/Editor/MigrationTestScene.unity` — preferred so we don't have to git-time-travel.)
3. Open Unity; load the legacy scene.
4. Select the legacy ScatterField → "Migrate Now" prompt appears.
5. Accept → confirm:
   - New `<scene>_ScatterConfig.asset` created.
   - Old loose layer + density assets untouched on disk.
   - Demo renders byte-identical to baseline (Unity MCP screenshot diff).
   - Console: one Debug.Log "Migrate Done", no errors.
6. Reload the scene → ScatterField now opens with the slim editor (no legacy HelpBox).

### T5.6 — Documentation (~30 min)

Create `Assets/GrassInteract/Editor/ScatterAssetMigrator.md` (or a `HANDOFF.md` blurb) explaining:
- What migration does + does not do
- Rollback steps (delete new config asset + revert scene)
- Why old assets aren't auto-deleted
- How to clean up afterwards: "manually delete the old loose ScatterLayer assets + density Texture2D assets once you've verified everything renders correctly"

## Success criteria

- ✅ Compile clean.
- ✅ Legacy scene opens; HelpBox + Migrate button appears in `ScatterField` inspector.
- ✅ One click produces a new config asset; demo renders byte-identical.
- ✅ Old loose assets remain on disk (manual cleanup).
- ✅ Re-opening the scene: no legacy HelpBox; ScatterField shows normal slim editor.
- ✅ Console: only the "Migrate Done" Debug.Log; zero errors/warnings.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Migration runs without confirmation on auto-open (user has no chance to bail) | 2 | 5 | 10 | T5.2 ALWAYS prompts via `DisplayDialog`; no auto-confirmation path |
| Pixel copy mangles density data (precision loss in non-readable textures) | 2 | 4 | 8 | If source not readable, log warning + LINK original loose texture instead of cloning. User can fix import settings + re-migrate by deleting new config first |
| Sub-asset addition before parent save throws | 2 | 4 | 8 | T5.2 calls `CreateAsset(config)` FIRST, then `AddObjectToAsset(child, config)`. Order verified |
| Legacy field deserializes orphan YAML differently in Unity 6 | 2 | 3 | 6 | T5.5 tests on actual Unity 6000.3.13f1; fall back to menu-driven manual migration with explicit asset assignment if SerializedProperty read fails |
| User runs migration twice on the same scene → duplicate configs | 2 | 2 | 4 | After successful migrate, legacy fields are cleared in YAML; `IsLegacy` returns false; the prompt won't re-appear. If user manually re-runs the menu, "not legacy" dialog fires |
| Re-migration after partial failure leaves a half-populated config | 2 | 3 | 6 | T5.2 is not transactional — document in the rollback section: delete the new config asset (not its sub-assets individually) to fully discard. Then re-migrate |

## Verification commands (Unity MCP)

```
mcp__UnityMCP__set_active_instance(unity_instance="GrassInteract@<hash>")
mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Tests/Editor/MigrationTestScene.unity")
# Manual: select ScatterField → click Migrate Now → accept prompt
mcp__UnityMCP__read_console(types=["Error", "Warning"], count=50)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Demo/GrassInteractDemo.unity")
mcp__UnityMCP__rendering_stats()
```
