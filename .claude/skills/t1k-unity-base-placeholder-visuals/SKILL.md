---
name: t1k:unity:base:placeholder-visuals
description: Build playable demos without art assets — primitives + URP/Unlit + procedural materials + UI swatches. Includes a 12-point Playability Validation Checklist so you can prove a demo is actually playable. Use for POCs, prototypes, vertical slices, and gameplay-first demos before art lands.
effort: medium
context: fork
triggers:
  - placeholder
  - greybox
  - prototype
  - POC
  - vertical slice
  - no art
  - art-free
  - playable proof
  - demo visualization
  - primitive prefab
  - URP Unlit material
  - colored quad
  - GetOrCreateMaterial
  - Setup Scene
  - humanoid sprite
  - PlaceholderSpriteUtility
  - stickman sprite
  - DrawMeleeHumanoid
  - DrawRangerHumanoid
keywords: [placeholder, greybox, prototype, POC, demo, primitives, URP, validation]
version: 2.3.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Placeholder Visuals — Playable Demos Without Art Assets

Build complete, validated, playable demos using only Unity primitives + URP shaders + procedural materials. Replace any placeholder with a real asset later without touching gameplay code — visuals are decoupled by design.

**SSOT for the pattern audit + per-perspective playbooks:** `references/`. **Treat the Playability Validation Checklist below as a release gate** — a demo that fails any P0 row is not playable.

## When to use

| Use it when... | Skip it when... |
|---|---|
| Building a vertical slice / POC / prototype | Shipping production with final art |
| Art team is downstream of gameplay | Synty/asset packs already wired and proven |
| You need to verify gameplay loops before committing to art | The demo already has all the validation rows green |
| Demoing a new system in isolation | The visuals issue is "make art prettier" — not "make game playable" |

## Core pattern (universal across 22 demos audited — 14 RPG + 8 Puzzle, 260520-R5)

1. **Primitives only** — `GameObject.CreatePrimitive(PrimitiveType.{Quad,Cube,Capsule,Cylinder,Sphere})`. No 3D models, no Synty.
2. **URP shaders** — `Universal Render Pipeline/Unlit` (2D / side / iso) or `Universal Render Pipeline/Lit` (3D). No custom shaders.
3. **Procedural materials saved to `.mat`** — `GetOrCreateMaterial(name, color)` writes once, reuses on re-runs. Naming `{Category}_{Subject}.mat` (e.g. `Item_MGCannon.mat`, `Enemy_ScrapRunner.mat`).
4. **Tool menu pair** — `Tools/{DemoName}/Create All Prefabs` + `Tools/{DemoName}/Setup Scene`. Always re-runnable.
5. **Idempotent destroy-by-name scene setup** — re-running setup MUST pick up authoring field changes. Never `if (exists) return` — always destroy and recreate.
6. **Partial-class file split** — keep every file under 200 lines. Split by responsibility: `.cs` (entry), `.SubScene.cs` (ECS authoring), `.UI.cs` (canvases), `.UI.HUD.cs`, `.UI.Shop.cs`, etc.
7. **Color-as-identity** — rarity tints (Common=grey, Rare=blue, Epic=purple, Legendary=gold), team tints (red/blue), archetype tints (scrap=orange-red, drone=dusty-red).
8. **Authoring components attached at prefab-creation time** — `instance.AddComponent<XxxAuthoring>()` then `PrefabUtility.SaveAsPrefabAsset` so bakers see the right SerializeField values.

## Procedural sprite generators — go beyond primitives for visualization

Primitives + colored materials get you 60% of the way. For the remaining 40% — items, characters, currencies, abilities — **generate procedural sprite PNGs**. They cost minutes to write and weeks to replace with real art, but they let a designer/playtester *recognize what they're looking at* without text labels.

**Always pair an editor generator with the demo so visuals can be regenerated idempotently.** Designers should never hand-paint placeholders.

### The 3-generator pattern (canonical: ChaosForge demo, 2026-05-25)

| Generator | What it makes | Output path | Naming |
|---|---|---|---|
| **Character/unit sprite generator** | Stickman silhouettes — hero, enemy variants, boss | `Sprites/Stickman/` | `Stickman_{Role}_{Variant}.png` (Hero, Enemy_LightningArcher, Boss_StormcallerZephyrix) |
| **Item icon generator** | Weapon/armor/accessory/consumable icons, rarity-tinted | `Sprites/Items/` | `Item_{Era}_{Name}.png` (Item_Modern_SteelBlade, Item_Modern_ArcCoat) |
| **Currency icon generator** | Soft + hard + special currency icons | `Sprites/Currency/` | `Currency_{Type}.png` (Gold, Gem, Soul, RealmShard) |

Each generator is a static Editor class with a `[MenuItem]` entry. Re-runnable. Idempotent. Reads from the same SSOT data files (CSVs / SOs) the runtime uses, so a CSV-driven name change auto-flows to sprite filenames.

**Library utility:** `DOTSCore.Editor.PlaceholderSpriteUtility` (`com.the1studio.dots-core/Editor/Utilities/`) provides canonical drawing primitives, shape generators, humanoid silhouette draws (`DrawMeleeHumanoid`, `DrawRangerHumanoid`, `DrawMageHumanoid`, `DrawBossHumanoid`, `DrawArrow`), and I/O helpers. Always call these instead of duplicating in demo Editor asmdefs. Full API: [references/placeholder-sprite-utility.md](references/placeholder-sprite-utility.md).

### MANDATORY PRINCIPLE — Icons over text fallbacks

**Whenever a UI element CAN be represented as an icon, USE the icon. Text fallbacks for visual concepts (lock states, currencies, modifier types, skill kinds, rarity tiers, status effects) are NOT acceptable as a final state — they are placeholders that read as "incomplete UI" to the player.**

Concrete rule: if a UI cell has an `Image` component but no `sprite` assigned, the engine renders a white square. White squares are a 100%-reliable visual TELL that icon-wiring is incomplete. Audit any demo for white squares before declaring UI done.

Symptoms of the anti-pattern (all real bugs found in ChaosForge 2026-05-25):

1. **Lock-emoji → "LOCKED" red text fallback.** Emoji had no font fallback in TMP; was replaced with red text. Correct fix: generate a red padlock sprite, not a text fallback.
2. **Realm-card modifier rows showing "+50% XpGainMult" with a white-square Image before each row.** The Image cells were placed in scene-setup with no sprite assignment; runtime renders them blank. Each `RealmModifierType` (XpGainMult, DropRateMult, EnemyHpMult, EnemyDamageMult, CurrencyDropMult, etc.) needs a dedicated icon sprite.
3. **4 semi-active skill slots all rendering as grey placeholders.** No sprites assigned; user can't tell which slot is which ability. Generate a skill-icon registry parallel to NamedItemRegistry.
4. **Placeholder squares in top HUD** for currencies (now fixed with currency icons).
5. **Item-text overflow in narrow equipment cells** producing "LRRR / Eeee / Caaa" character-per-line wrap. Replace TMP_Text labels with Image sprites resolved by item-id convention.

**Audit gate:** before declaring any demo UI done, enter Play mode and screenshot every panel. Any white square or text label that *could* be an icon is a violation. File the icon as a follow-up generator task before claiming "done".

### Why generated sprites > pure colored quads

- **Silhouette readability** — a stickman silhouette reads as "human" instantly; a colored quad reads as "thing". Crucial for unit-type recognition under combat conditions.
- **Rarity affordance** — rarity-tinted item icons (Common grey → Legendary gold) communicate value at a glance without tooltip text.
- **No placeholder squares in HUD** — currency icons fill the empty square slots in top HUD so the player can read `[Gold 47] [Gem 3]` not `[square 47] [square 3]`. This was a real bug found in ChaosForge wow-battle (2026-05-25 user screenshot).
- **No item-text overflow in equipment grids** — item icons in equipment cells avoid the "LRRR / Eeee / Caaa" character-per-line wrap bug when long item names hit narrow cells. Replace TMP_Text labels with Image sprites resolved by item-id convention. Real ChaosForge bug, same date.

### Procedural sprite generator pattern (paste-and-adapt)

```csharp
public static class ChaosForgeItemIconGenerator
{
    private const string ItemFolder = "Assets/Demos/RPG/ChaosForgeDemo/Sprites/Items";
    private const int Size = 128;

    [MenuItem("Tools/ChaosForge/Generate Item Icons")]
    public static void Generate()
    {
        Directory.CreateDirectory(ItemFolder);
        foreach (var (era, name, type, rarityTint) in EnumerateItems())
            WriteSpritePng($"{ItemFolder}/Item_{era}_{name}.png", DrawItem(type, rarityTint));
        AssetDatabase.Refresh();
        ApplySpriteImportSettings(ItemFolder);
    }

    private static Color[] DrawItem(ItemType type, Color rarityTint) { /* procedural draw */ }
    private static void WriteSpritePng(string path, Color[] pixels) { /* Texture2D + EncodeToPNG */ }
    private static void ApplySpriteImportSettings(string folder) { /* TextureImporter: Sprite, point filter, no compression */ }
}
```

Keep each draw routine simple (silhouette + 1-2 accent colors). The goal is *recognizability*, not art.

### When to swap to real art

The generator outputs become the **swap-in surface** for the artist. When real art lands:
1. Artist drops new PNG into the same folder with the same filename.
2. Sprite import settings already match (pixels per unit, filter, format).
3. Demo immediately picks up the new art — zero code change.
4. Delete the generator OR keep it for regenerating on schema change (new era, new rarity tier).

### Anti-patterns for sprite generation

| Anti-pattern | Fix |
|---|---|
| Hand-paint placeholders in Photoshop, drop in `Sprites/` | Always write a generator. Regeneration is part of the demo's reproducibility contract. |
| Generator outputs to `Sprites/Items/` but runtime loads from `Sprites/Item/` (singular/plural drift) | Generator writes a `manifest.json` listing all generated sprites; runtime SO reads the manifest at edit-time |
| Inline `Resources.Load<Sprite>("Item_Modern_SteelBlade")` at every call site | Centralize in a single `ItemIconResolver` ScriptableObject — one entry per item, sprite reference assigned via Editor introspection of the generator's output folder |
| Generator writes BMP / TGA / huge PNGs | 64×64 to 128×128 PNG, point filter, no compression, mode RGBA32 — keeps repo small and pixel-art-feel |
| Generator hardcodes color list | Read tints from the same CSV / RarityTint SO chain the runtime uses |

## Perspective playbooks

Pick the matching playbook for the demo's perspective. Full details in `references/perspective-playbooks.md`.

| Perspective | Body primitive | Camera | Shader | Notable |
|---|---|---|---|---|
| **3D** (BattleDemo) | Capsule/Cylinder/Sphere + mesh-child pivot | Perspective | URP/Lit | LODGroup + Impostor fallback |
| **2D top-down** (BattleDemo2D) | Quad XZ-plane | Ortho top-down | URP/Unlit cutout | Alpha-clip 0.5 |
| **2D side-view** (RushTank, BattleDemoSideView) | Quad XY-plane | Ortho -Z | URP/Unlit | Tank rolls, world scrolls |
| **Isometric** (BattleDemoIso) | Quad + billboard shader | Perspective 30° tilt | Billboard shader | Auto-rotates to camera |

## Playability Validation Checklist (12 P0 rows — release gate)

A demo is **NOT playable** until every P0 row is green. Run this before declaring any demo "done".

### A. World readability (player can see what's happening)

- [ ] **P0-A1 Player avatar visible.** The player character/tank/unit is on screen at game start, NOT a single colored block — it has a recognizable silhouette built from 3+ primitives (body + tracks + turret, body + cape + head, etc.).
- [ ] **P0-A2 Enemies visually distinguishable.** Every enemy archetype reads at-a-glance from every other. NOT all red squares. Body shape + accent parts (barrel/spike/rotor/turret) per archetype.
- [ ] **P0-A3 Ground line + horizon backdrop.** Scene has at least 3 environment layers: ground bar, distant silhouettes (mountains/walls/objects), sky. NOT a solid-color void.
- [ ] **P0-A4 Items / pickups visible + distinct.** Each item type has a distinct color OR shape. NOT all identical squares.

### B. HUD (player knows the game state)

- [ ] **P0-B1 HP bar visible.** Player HP rendered as a slider with fill + numeric overlay. Updates on damage.
- [ ] **P0-B2 Phase / objective text visible.** Current phase (Arrange, Rolling, Combat, Shop, Won, Lost…) shown as on-screen text. Updates on phase change.
- [ ] **P0-B3 Currency / resource counters.** Scrap / Gold / XP / Cores — whatever the player earns, shown numerically with a label ("Scrap: 47", not just "47").

### C. Interaction (player knows what to do)

- [ ] **P0-C1 At least one clickable button per active phase.** Every screen the player faces MUST have at least one interactive control with a labeled action ("GO ▶", "REROLL", "EXIT SHOP", "NEW RUN ▶"). NOT silent screens that require unknown input.
- [ ] **P0-C2 Phase-intro feedback.** When a new phase starts, the player gets a visible signal — either a transient banner overlay ("ARRANGE — Drag items to tank") or a clear UI change. NO silent transitions.
- [ ] **P0-C3 End-state screens.** Win and Lose paths each show a dedicated panel with stats + a "New Run" button. NOT just "game ends, scene freezes".

### D. Setup / re-runability (other developers can run the demo)

- [ ] **P0-D1 One-button scene setup.** `Tools/{DemoName}/Setup Scene` builds the entire scene from scratch with zero Inspector touch-ups required. Re-runnable on every commit.
- [ ] **P0-D2 No null SerializeFields after setup.** Every `[SerializeField]` ref on every authoring/UI MonoBehaviour is wired by code via `SerializedObject` — Inspector should show zero `None (...)` slots after `Setup Scene`.
- [ ] **P0-D3 Scene + prefab assets committed.** `Scenes/` and `Prefabs/` folders are non-empty in git — a demo showing "Implemented" status with empty asset folders is NOT playable. Audit (260520-R4) found 6/23 demos with empty Scenes/Prefabs despite "Implemented" in Demos.md. Source: review-260520-round4-design-placeholder.md §D3

### Anti-patterns that FAIL the checklist

| Anti-pattern | Why it fails |
|---|---|
| Comment in setup code: "user wires refs in Inspector after re-running setup" | Fails P0-D2. The UI never renders because refs are null. **This shipped in RushTank — broke the whole demo until fixed.** |
| `Image` + `Text` on the same GameObject (constructed with `typeof(Image), typeof(Text)`) | Unity rejects — only one Graphic per GO. Use parent panel + child text. |
| Solid-color clear with no environment quads | Fails P0-A3. Player can't tell what's foreground vs background. |
| All enemies same Quad + same red material with only stat differences | Fails P0-A2. Player can't strategize. Add multi-part silhouettes. |
| Single-quad player avatar | Fails P0-A1. Compose 3+ primitives (body + tracks + turret). |
| Silent phase transitions | Fails P0-C2. Add a `RushTankPhaseBanner`-style overlay watching the phase singleton. |

## Pre-flight

Spawning fix agents from Step 7 of `references/validation-script.md` requires the Agent tool to be in scope.

```
ToolSearch(query="select:Agent,TeamCreate", max_results=2)
```

If Agent is absent from both active scope and the deferred-tools listing, bail per `skills/t1k-team/references/fork-context-bail.md`.

## Gotchas

1. **Image + Text cannot share a GameObject.** Unity allows only one `UnityEngine.UI.Graphic`-derived component per GO. Panel-with-text → parent (Image) + child stretched (Text).
2. **`??` and `?.` are BANNED on `UnityEngine.Object`.** Unity's fake-null bypasses C# operators. Use `if (x == null)` explicitly.
3. **Setup Scene must be destroy-and-recreate, not skip-if-exists.** Otherwise newly added authoring fields never reach pre-existing GameObjects.
4. **Procedural prefabs need `PrefabUtility.SaveAsPrefabAsset` round-trip** so SerializeField changes persist. `AddComponent` then save then destroy the in-scene instance.
5. **Private SerializeField needs `SerializedObject.FindProperty` + `ApplyModifiedPropertiesWithoutUndo`.** Direct reflection works inconsistently.
6. **Material naming = swap-in surface.** Use `{Category}_{Subject}.mat` (e.g. `Enemy_Boss_Eye.mat`). When real art lands, the artist only swaps materials — gameplay code never changes.
7. **One Tool/Setup Scene menu per demo.** Don't share scene setups across demos. Each demo owns its prefab folder + materials folder + scene asset.
8. **Never count SerializeFields from commit text, wiki, or audit rows — always READ the script.** Field counts derived from secondary sources are unreliable. Today's example: RecapHUD audit said 9 SerializeFields; the actual script had 10 (the 10th was `runController`, injected from the Shell.cs partial chain and not visible from inspecting the HUD file alone). Before generating any setup/wiring code, READ the target MonoBehaviour first. Evidence: `4d174b85`.
9. **Partial-class file splits must stay under 200 lines each.** SceneSetup files that accumulate Phase A-F wiring sections grow past 200 lines quickly. Pattern: `{DemoName}SceneSetup.cs` (shell + RunSetup), `SceneSetup.Environment.cs`, `SceneSetup.UI.HUD.cs`, `SceneSetup.SubScene.Units.cs`, etc. — one concern per file. Source: review-260520-round5-extraction-skills.md §C
10. **Per-camera culling-mask layer assignment is MANDATORY for sub-camera env geometry.** When a demo uses a secondary camera (RT-bound combat strip, mini-map, overlay) with a restrictive `cullingMask` (e.g., `cullingMask = 1 << CombatStripWorldLayer`), EVERY env tile / backdrop quad / prop built by the scene-setup tool MUST be assigned that layer with a recursive walk:

    ```csharp
    private static void SetLayerRecursively(Transform t, int layer) {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i), layer);
    }

    // After building env composition, BEFORE SaveScene:
    SetLayerRecursively(envRoot.transform, MyDemoConstants.CombatStripWorldLayer);
    ```

    **Failure mode:** if you forget, the env renders on layer `Default` (bit 0) and the sub-camera (mask bit 8) skips every quad — the user sees only the backdrop clear color and reports *"the sky / backdrop covers all the grass / ground / hero"*. The bug is invisible in the Scene view (Editor cameras render all layers); only the Game view through the sub-camera shows it. Real incident 2026-05-26 in ChaosForge: the backdrop builder set the layer correctly but the env-tile builder did NOT → grass / dirt / props rendered on `Default` → strip camera skipped them → only the dark navy SkyLayer was visible → user saw a solid blue rectangle. Fix: commit `02f2c4e9` in `ChaosForgeCombatSceneSetup.cs` (DOTS-AI).

    **Audit pattern when adding a new sub-camera demo:**
    1. Grep the scene-setup files for `cullingMask` — identify every restrictive sub-camera.
    2. For each, grep for every `new GameObject(...)` or `GameObject.CreatePrimitive(...)` call that produces visible geometry the sub-camera should see.
    3. Confirm a matching `SetLayerRecursively(..., theLayer)` call exists after the construction. If not → bug.

    **Why a builder-side helper, not a runtime fix:** assigning the layer at edit-time bakes into the scene asset, so SubScene-baked entities inherit the correct layer hash too. A LateUpdate runtime reassign would race with rendering and miss frame 0.

10b. **URP/Unlit Opaque IGNORES alpha but STILL writes depth — alpha=0 is NOT invisible.** A common "hide this backdrop quad" trick is to set `_BaseColor.a = 0` on a URP/Unlit material. Looks transparent in the inspector, but in the **Opaque** Surface mode (`_Surface = 0`, the default for URP/Unlit), the fragment shader writes the RGB to the color buffer (alpha ignored) AND writes depth. Result: a wide "invisible" quad at low Z (closer to camera) **occludes every renderer behind it** even though it has no visible pixels. The bug looks like a render-order issue but is actually depth-buffer occlusion.

    **Symptom:** user sees the camera clear color (often URP's bright cornflower blue `#4CA6FF`) wherever the depth-blocking quad covers, instead of the intended geometry behind it. Grass / dirt / hero / enemies all "disappear" inside the rectangle.

    **Fix options (pick one):**
    - **Disable the renderer entirely** if the quad is purely for scene-graph wiring (e.g., parallax script reads `transform`, not the renderer): `renderer.enabled = false;`
    - **Switch to Transparent surface mode** so the fragment shader actually respects alpha:
      ```csharp
      mat.SetFloat("_Surface", 1f);  // Transparent
      mat.SetFloat("_Blend", 0f);    // Alpha-blend
      mat.SetFloat("_ZWrite", 0f);   // Don't write depth
      mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
      ```
    - **Move the quad to a higher Z** (behind all visible geometry) so even if it depth-writes, it can't occlude.

    **Audit pattern:** after building any "transparent" backdrop / overlay quad, sample a pixel from the strip RT (`Texture2D.ReadPixels` on the RT) at the quad's center. If the color matches the URP clear color and not the intended geometry behind it, the quad is depth-occluding. Real incident 2026-05-26 in ChaosForge: `ForegroundLayer` at Z=1 with `alpha=0` opaque shader → blocked grass (Z=3) + dirt (Z=2.5) across the entire combat strip. Fix in `ChaosForgeSceneSetup.cs:983` — `fore.renderer.enabled = false`.

11. **TMP descendants of a Button GameObject default `raycastTarget=true` and SWALLOW clicks — `Button.onClick` never fires.** Unity UI raycast finds the topmost `Graphic` whose `raycastTarget=true` under the cursor and dispatches the pointer event to THAT GameObject only. There is **NO bubbling** to the parent Button — if the TMP child has no `IPointerClickHandler`, the click is silently consumed and the parent's `onClick` UnityEvent is never invoked. TMP's default `raycastTarget=true` makes this a latent trap for every `CreateLabel` / `CreateButton` helper in a scene-setup tool.

    **Symptom:** the UI looks correct, the button visually highlights on press (because `Button.Selectable` machinery still runs from the Image's raycast), but `onClick.AddListener(...)` never fires. Manually invoking the handler from code (`view.InvokeTapForTests()` in EditMode tests) works perfectly — only the live tap is broken. EditMode tests pass; live demo is silently dead.

    **Originating incident (2026-05-25 ChaosForge BLOCKER):** `RealmCardView.HandleTap` never ran on live realm-card taps because the card's `ModifiersLabel` TMP (anchored 0.06-0.51 vertically, covering the bottom half of the card) defaulted `raycastTarget=true` and intercepted every click in that region. The Editor builder's `CreateLabel` helper used by every label, close-X, lock icon, and button text also forgot to disable raycastTarget, so the bug was latent in dozens of buttons across the demo. EditMode tests bypassed `Button.onClick` via an `InvokeTapForTests()` test helper — they passed for weeks while the live tap was broken. Manual `EntityManager.SetComponentEnabled<CombatSpawnRequest>(player, true)` made enemies spawn (proving the spawner + publisher + bake were all correct) — only the click path was broken.

    **Fix — helper-level (preferred):** make `CreateLabel` set `tmp.raycastTarget = false` by default. Decorative labels are non-interactive; callers that genuinely need raycast (rare: rich-text hyperlink, click-on-text widget) opt in explicitly. One-line fix wipes out the entire class of bugs.

    ```csharp
    private static TMP_Text CreateLabel(Transform parent, string text, int fontSize) {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.raycastTarget = false; // ← canonical decorative default
        ...
        return tmp;
    }
    ```

    **Fix — site-level (defensive):** for any TMP added inline (not via `CreateLabel`), explicitly set `raycastTarget = false` immediately after `AddComponent<TextMeshProUGUI>()`. Treat it as part of the construction triple: `text`, `fontSize`, `raycastTarget=false`.

    **Audit pattern (run on every Editor scene-setup file):**
    1. `grep -n 'AddComponent<TextMeshProUGUI>\|AddComponent<TMP_Text>' Editor/*.cs`
    2. For each hit, verify a `raycastTarget = false` line follows within 5 lines.
    3. If missing AND the TMP is parented under a GameObject carrying `Button`/`Toggle`/`Selectable` → BUG (latent click swallower).
    4. Make the EditMode test fixture exercise the actual `Button.onClick.Invoke()` path via `ExecuteEvents.Execute(button.gameObject, ..., ExecuteEvents.pointerClickHandler)` rather than calling internal `InvokeTapForTests()` helpers — the helpers bypass the raycast layer and hide this exact bug class.

    **Why this is the right place for the rule:** scene-setup tools are the canonical owner of placeholder UI. Demos that follow `placeholder-visuals` build dozens of labels via shared helpers; one default decision (`raycastTarget=false` in `CreateLabel`) prevents the entire bug class for every demo. See companion gotcha in `t1k:unity:ui:ugui` skill for the runtime-side rule (Mono drag handlers, IPointerClickHandler subclasses).

    **Note on numbering:** this gotcha sits alongside in-flight gotchas #10/#10b/#10c/#10d (PRs #119/#120/#121/#123 — sub-camera culling-mask layer, URP/Unlit opaque alpha-depth, DOTS prefab layer inheritance, opaque parallax backdrop depth-occlusion). Numbering will be normalized whichever PR merges last.

10. **HP-bar `HealthBarAuthoring.Offset` MUST scale with the unit's quad height — fixed-constant offsets break on every scale bump.** The library's GPU HP-bar renderer (`DOTSCore.UI.HealthBarRenderer`) draws the bar at `unit.position + (0, Offset, 0)`. When a demo's placeholder sprite-on-quad unit lifts its quad's local position by `scale * 0.5f` so feet sit at root Y=0 (the canonical pattern documented elsewhere in this skill), the quad's head TOP lands at root `Y = scale`. A fixed `Offset = 1.2f` then puts the HP bar AT chest level on a `scale=4.4` unit — visually inside the body, not above the head.

    **Symptom:** HP bars look like they're invisible until enemies take damage (they were always there, just clipped behind the sprite), or worse, they appear at unit chest level and read like a "belt" instead of an HP bar.

    **Fix — derive the offset from the quad scale:**

    ```csharp
    private const float HealthBarHeadMargin = 0.4f;
    private static float ComputeHealthBarOffset(float quadScale) =>
        quadScale + HealthBarHeadMargin;

    // In the prefab creator's attach helper, take the scale parameter:
    private static void AttachHealthBar(GameObject go, float quadScale) {
        var hb = go.AddComponent<HealthBarAuthoring>();
        hb.Offset = ComputeHealthBarOffset(quadScale);
    }
    ```

    This auto-tracks future scale bumps without manual constant edits. Per-archetype scales (hero 4.4, enemy 4.4, boss 5.5) each yield the right per-unit offset (4.8 / 4.8 / 5.9) from the same helper.

    **Don't forget to patch live `.prefab` assets:** the creator change applies on the next "Create All Unit Prefabs" tool run, but the COMMITTED prefab assets cache the old `Offset` value in YAML. For an in-place fix without forcing a tool re-run, sed/python the `Offset: <old>` line in each `.prefab` file. Both fixes belong in the same commit so the SSOT (creator) and the live demo (prefab assets) match.

    **Audit pattern (run on any sprite-on-quad demo):**
    1. Grep the prefab creator for `HealthBarAuthoring` instantiation; verify `Offset` is derived from a scale variable, not a constant.
    2. Verify the live `.prefab` YAML matches what the creator produces (`grep -A 1 HealthBarAuthoring Prefabs/*.prefab | grep Offset` should show consistent values matching `scale + 0.4`).
    3. If the quad pivot is NOT center-lifted-by-half-scale, the formula differs — but the principle (offset scales with visible unit height) still holds.

## Reference implementation

`Assets/Demos/RPG/RushTankDemo/` is the canonical example. Files to study:
- `Editor/RushTankPrefabCreator.cs` + `RushTankPrefabCreator.EnemyParts.cs` — primitive prefabs + multi-part silhouettes
- `Editor/RushTankSceneSetup.cs` + 9 partial files — scene wiring
- `Editor/RushTankSceneSetup.UI.HUD.cs` — HUD widget construction (slider, currency text, phase label, speed buttons, map button)
- `Editor/RushTankSceneSetup.Environment.cs` — ground/horizon/mountain backdrop
- `Editor/RushTankSceneSetup.SubScene.TankVisual.cs` — multi-part tank silhouette
- `Runtime/UI/RushTankPhaseBanner.cs` — phase intro overlay watching ECS singleton

Other strong references (260520-R5 expanded census — 22 demos):
- **BackpackCrawler** — procedural pixel-art landscape (4 PNG layers with X+Y wrap tiling), partial-class HUD split
- **BattleDemo** — LOD + Impostor fallback, Synty integration path
- **BattleDemoIso** — billboard shader, isometric camera rig
- **ColorFitDemo / BlockBlastDemo / Match3Demo / MergeDemo / SlideDemo** — puzzle-grid perspectives; all use URP/Unlit quads on XZ plane; canonical for puzzle primitive layout

See `references/` for: `playability-checklist.md` (auditor's worksheet), `perspective-playbooks.md` (4 perspectives + shader enforcement), `demo-audit-2026-05-19.md` (22-demo consensus + divergence), `validation-script.md` (how to run the checklist via Unity MCP).

### E. Puzzle-specific (grid-based demos only)

- [ ] **P0-E1 PuzzleVisualConfig exists and is non-null.** Every grid-based demo must have a `PuzzleVisualConfig` ScriptableObject wired to its `GridSyncBase` subclass. NOT per-cell inline color constants.
- [ ] **P0-E2 Grid cell visuals use shared `PuzzleVisualConfig`.** All visual parameters (cell size, gap, colors, tile sprites) come from the config — zero hardcoded magic numbers inline in cell-creation code. Source: review-260520-round5-extraction-skills.md §C
