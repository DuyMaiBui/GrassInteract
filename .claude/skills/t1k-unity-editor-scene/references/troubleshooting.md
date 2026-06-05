---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---
# Scene Setup Troubleshooting

## Menu Item Not Found

**Symptom:** `execute_menu_item` throws "menu item not found"

**Causes & Fixes:**
1. Editor asmdef missing reference → check `{Demo}/Editor/*.asmdef` includes `Unity.Entities`
2. Script compilation error → `read_console(filter:"Error")` first, fix errors before running menu
3. Wrong path format → verify exact path via Unity Editor menu bar manually
4. Unity not in Edit mode → ensure not in Play mode when running setup tools

## Entities Invisible After Setup

**Symptom:** `rendering_stats` shows batches=0 or entities exist but nothing renders

**Cause:** Lightmap baking corrupts DOTS `ChunkWorldRenderBounds` → NaN → entities culled

**Fix:**
1. Do NOT bake lighting with DOTS entities in scene
2. Use realtime lighting only during development
3. Clear `Library/EntityScenes/` → re-enter Play mode
4. See `unity-light-baking` skill for DOTS-safe bake workflow

## Troops Not Moving

**Symptom:** Units spawn but stand still (no pathfinding)

**Cause priority:**
1. **BDP trees missing** — `Create Unit Prefabs` wipes prefabs, destroying BDP trees
   - Fix: always run `Build Behavior Trees` AFTER `Create Unit Prefabs`
2. **DetectionRadius too small** — units can't see enemies
   - Fix: check `{Demo}UnitPrefabCreator` → `DetectionRadius` >= spawn gap distance
3. **NavMesh not baked** — obstacles block entire surface
   - Fix: re-run `Setup Scene`, verify `GameObjectUtility.SetNavMeshArea(obs, 1)` for obstacles

## Stale Entity Cache

**Symptom:** Old component data persists after code changes, baking doesn't reflect new authoring

**Fix:**
```bash
rm -rf Library/EntityScenes/
```
Or via MCP: `manage_editor(action: "clear_entity_cache")` if available.

Must clear after:
- Adding/renaming `IComponentData` fields
- Changing `Baker<T>` logic
- Modifying SubScene content

## Prefab Regeneration Order (CRITICAL)

```
1. Create Unit Prefabs   ← sets components
2. Build Behavior Trees  ← adds BDP to prefabs (MUST run after step 1)
3. Setup Scene           ← places prefabs in SubScene
4. Clear Library/EntityScenes/
5. Enter Play mode
```

Skipping step 2 after step 1 → units idle forever.

## Silent prefab-wiring + invisible-baker regressions (Bug C / Bug D, 2026-05-18)

Scene setup is **THREE steps, not two**. Missing the third step bakes silently to an empty Game view.

```
Step A: Create the asset files          (the prefab .prefab files on disk)
Step B: Place authoring GameObjects     (drop a node into the SubScene with the *Authoring component)
Step C: WIRE the authoring fields       (set [SerializeField] GameObject slots to point at Step A's assets)
```

**Bug C (silent unwired prefab slots):** `Create All Prefabs` creates the asset files, `Setup Scene` places the authoring GameObjects, neither auto-wires the `[SerializeField] GameObject` slots → all slots stay `{fileID: 0}` → Baker resolves `Entity.Null` for every archetype → spawn system silently produces no enemies → empty Game view. Diagnosed in RushTank session 2026-05-18; fixed in commit `057ad293`.

**Bug D (declared-but-unread visual prefab):** Authoring declares `[SerializeField] private GameObject? chassisVisualPrefab` but the Baker code never reads the field; the SubScene GameObject also has no MeshFilter/MeshRenderer → entity bakes without a Renderer → invisible at runtime. Diagnosed in RushTank session 2026-05-18; fix by either reading the field in `Bake()` or attaching MeshFilter+MeshRenderer in scene-setup.

### Recurrence-prevention recipe (apply to every demo)

Any `[SerializeField] GameObject` / `[SerializeField] GameObject[]` field on a SubScene authoring component is a HIGH-RISK wiring slot. Apply **both** safeguards.

1. **Baker-time null warning (NOT OnValidate)** — editor-only, fires once per bake. Wrap in `#if UNITY_EDITOR`, emit `Debug.LogWarning(message, authoring)` from inside `Baker<T>.Bake()` so clicking the warning highlights the GameObject. Emit ONE warning per missing field per bake.

   **DO NOT use `OnValidate` for this purpose.** `OnValidate()` fires when `AddComponent<T>()` lands on a GameObject — which is BEFORE scene-setup tooling has a chance to call `SerializedObject.ApplyModifiedPropertiesWithoutUndo()` to wire the asset refs. The result is a noisy 10+ false-positive warning burst on every Setup Scene run even though the saved `.unity` file contains correct GUIDs. We caught this exact failure mode in May 2026: a validator agent read the false-positive warnings and concluded the SubScene was broken, when in fact every GUID was already on disk. Commit `ec3cf427` migrated the RushTank authorings from OnValidate to Baker. Use that as the canonical pattern. See `dots-core/ecs-core/baking-guide.md` § "Null-Prefab Safeguards" for the full code template.

2. **EditMode test that opens the SubScene asset and asserts every slot is wired.** Pattern:
   ```csharp
   [UnityTest] public IEnumerator SubScene_AllPrefabSlots_AreWired() {
       var scene = EditorSceneManager.OpenScene(SubScenePath, OpenSceneMode.Additive);
       try {
           var auth = Object.FindObjectsByType<MyAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
           var so = new SerializedObject(auth[0]);
           foreach (var field in new[] { "fooPrefab", "barPrefab" })
               Assert.That(so.FindProperty(field)!.objectReferenceValue, Is.Not.Null, $"{field} unwired");
       } finally { EditorSceneManager.CloseScene(scene, true); }
       yield break;
   }
   ```
   Example: `Assets/Demos/RPG/RushTankDemo/Tests/EditMode/RushTankDemo.Tests/RushTankSubSceneWiringTests.cs`.

## Empty controller GameObject — main-scene host MonoBehaviour never attached (Bug H, 2026-05-19)

**Symptom:** Play mode loads with the visual scene rendering correctly (HUD, camera, lights) but no actual gameplay happens — no enemies spawn, no phase transitions, no ECS run state. Console is clean of compile and baking errors.

**Diagnosis:** The main-scene "controller" GameObject exists in the hierarchy but has only a `Transform` component — the actual `RunController` (or equivalent) MonoBehaviour is never attached. Setup Scene created a bare placeholder. Discovered in BackpackBattlefield where `BackpackBattlefieldSceneSetup.Shell.cs:CreateOrReplaceRunController` left this trailing comment: *"P3 will attach a MonoBehaviour that drives the Arrange→Battle→Shop loop."* — P3 never landed it.

```csharp
// BUG H pattern — visible as "RunController" GameObject with only Transform
private static void CreateOrReplaceRunController(Scene scene)
{
    DestroyGameObjectByName(scene, RunControllerGoName);
    var go = new GameObject(RunControllerGoName);
    SceneManager.MoveGameObjectToScene(go, scene);
    // ❌ MonoBehaviour never attached. Comment promises "later". Later never happens.
}
```

**Fix recipe:**

1. Setup Scene MUST attach the controller MonoBehaviour AND wire its SerializeField refs in the same pass (same pattern as RushTank's `WireEnemyPrefabRegistry`).
2. Add a **main-scene wiring regression test** (separate from SubScene wiring tests):
   ```csharp
   [UnityTest] public IEnumerator MainScene_RunController_IsAttached() {
       var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
       try {
           var c = Object.FindFirstObjectByType<MyRunController>(FindObjectsInactive.Include);
           Assert.That(c, Is.Not.Null, "Main scene RunController GameObject has no controller MonoBehaviour attached.");
           var so = new SerializedObject(c);
           foreach (var f in new[] { "economyConfig", "shopConfig", "stringsConfig", "hud" })
               Assert.That(so.FindProperty(f)!.objectReferenceValue, Is.Not.Null, $"{f} unwired");
       } finally { EditorSceneManager.CloseScene(scene, true); }
       yield break;
   }
   ```
3. Existing SubScene wiring tests do NOT catch this — they only inspect SubScene authoring components. The main-scene host is invisible to them.

## Phase-driven UI never appears — Update() can't reactivate own GameObject (Bug I, 2026-05-19)

**Symptom:** A phase-driven UI panel (`MapUI`, `ShopUI`, `EventUI`, etc.) is supposed to appear when `RunPhase == MapSelect`, but the panel is stuck `activeSelf=False` even when the phase is correct. The Setup Scene created the panel as active — it deactivated itself and never came back.

**Root cause — Unity Update lifecycle pitfall:**

```csharp
public sealed class RushTankMapUI : MonoBehaviour
{
    private void Update()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (!em.CreateEntityQuery(typeof(RunPhase)).TryGetSingleton<RunPhase>(out var phase)) return;
        this.gameObject.SetActive(phase.Value == RunPhaseValue.MapSelect);  // ❌ chicken-and-egg
    }
}
```

- Frame 0: Setup Scene's GameObject is active. Update runs. RunPhase has some transient initial value (e.g., the default for the struct, NOT yet MapSelect). The check fails → `SetActive(false)`.
- Frame 0+: GameObject is inactive. Unity does NOT fire `Update` on inactive GameObjects. The panel is stranded.
- Later: a system sets `RunPhase.Value = MapSelect`, but no Update fires to notice.

**Fix patterns (pick one, NOT all):**

1. **Central always-active phase controller** (recommended). A single `PhaseUIController` MonoBehaviour on `HUDCanvas` (which never deactivates) inspects RunPhase each frame and toggles each panel via `SetActive`. The panels themselves are passive views; they don't gate their own activation.

2. **Visibility via CanvasGroup, not GameObject.SetActive.** Keep the GameObject permanently active so Update keeps running; toggle `CanvasGroup.alpha + interactable + blocksRaycasts` instead. Costs a tiny per-frame Update tick on every phase UI but avoids the chicken-and-egg entirely.

3. **Event-driven activation via Unity Action / R3 / SignalBus.** When the RunPhase ECS singleton's value changes, fire a managed event; all phase-UIs subscribe. Subscribers can re-enable themselves because the dispatcher is always active.

**Anti-pattern to detect in code review:** any `private void Update() { ...; this.gameObject.SetActive(...); }` is suspicious. Search `grep -nP 'Update.*\n.*gameObject\.SetActive' demos/`.

## Partial-class scene-setup is a 3-file dance — silent dead code on mismatch (2026-05-19)

Partial classes are the canonical pattern for large scene-setup files (e.g., `RushTankSceneSetup` has 12 partials). Adding a new partial requires THREE matching edits or the body compiles silently but is never called:

1. **Declaration** in the orchestrator: `static partial void Setup_X();`
2. **Body** in the new partial file: `static partial void Setup_X() { ... }`
3. **Invocation** inside `SetupScene()` in the orchestrator: `Setup_X();`

If the signature in the body file does not exactly match the declaration (e.g., wrong method name, wrong parameter list), C# compiles without error — the body simply becomes dead code. The partial-class mechanism has no warning for unimplemented declarations.

Evidence: `0fa92a9e` — `BackpackBattlefieldSceneSetup.Core.cs` required 4 new partial declarations + invocations for the Coordinator, EnemyVisuals, Environment, and HeroVisuals partials. Missing any one of the three sites silently leaves that subsystem uncalled.

## AssetDatabase.CreateAsset at tool-time creates .mat files that are NOT in git on fresh clone (2026-05-19)

`GetOrCreateMaterial` + `AssetDatabase.CreateAsset` generates `.mat` files during the first `Setup Scene` run. On a fresh clone these files don't exist — Materials/ folder is empty — so the demo renders with Unity's default magenta-error material until someone runs the tool.

Two clean patterns; pick one per demo and document it in the partial file:

- **(a) Generate-and-commit**: run the tool once, `git add Assets/Demos/{Demo}/Materials/`, commit. Materials are in SCM. Downside: every color-tweak becomes a separate commit.
- **(b) Generate-at-runtime**: use `new Material(shader)` without `AssetDatabase.CreateAsset` — no file is written, materials are re-created on every Setup Scene run. No SCM noise, but no Inspector tweakability.

Evidence: `8343b5d8` — BackpackBattlefield EnemyVisuals partial called `CreateAsset` at tool time; materials were missing on the first post-clone run.

## Triage checklist when "the Game view is empty"

Updated checklist (now covering Bug C / D / H / I):

1. Open the SubScene asset and inspect every `*Authoring` component — are all `[SerializeField] GameObject` slots wired? If any is `None (Game Object)`, that's **Bug C**.
2. Check the TankChassis / hero / cart GameObject for a MeshFilter + MeshRenderer. If missing, that's **Bug D** (or a scene-setup pass never attached one).
3. Open the MAIN scene (not SubScene) and inspect the controller GameObject — does it have the RunController MonoBehaviour, or just Transform? If only Transform, that's **Bug H**.
4. In Play mode, query `FindObjectsByType<MyPhaseUI>(FindObjectsInactive.Include)` and check each one's `activeSelf`. If a phase-UI is inactive even when RunPhase matches its target phase, that's **Bug I**.
5. Re-run `Tools/{Demo}/Setup Scene` — modern scene-setup utilities wire prefab refs via `SerializedObject + FindProperty + objectReferenceValue`. If wiring is still missing, the scene-setup code itself is buggy — search for the assignment.
6. Clear `Library/EntityScenes/` (per the safe-clear procedure in SKILL.md) and re-enter Play mode.
