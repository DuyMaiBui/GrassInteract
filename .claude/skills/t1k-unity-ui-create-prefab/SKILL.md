---
name: t1k:unity:ui:create-prefab
description: Author new generic Unity UI screen/popup prefabs as VARIANTS of UIBaseScreen.prefab / UIBasePopup.prefab from com.gdk.core, with correct Background/Content/Foreground layering, SafeArea scope, Timeline_Grp appear/disappear hookup, mandatory field-driven Content children, and Addressables registration. Use when creating UI prefabs OUTSIDE Packages/TheOneFeature* and outside Assets/Prefabs/UIs/. For TheOneFeature-scoped prefabs, use t1k:unity:tof:ui-create-prefab instead.
effort: low
keywords: [ui prefab, screen prefab, popup prefab, prefab variant, UIBaseScreen, UIBasePopup, Background, Content, Foreground, Timeline_Grp, SafeArea, anchor pivot, responsive ui, FlexibleLayoutGroupVer2, RootUI, gdk core, common ui, generic ui, unity mcp, content children, field-driven, layout image]
argument-hint: "[screen-name] [layout-image-path] (both optional)"
version: 2.6.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: ui
protected: false
---

# Create Generic Unity UI Prefab (Screen / Popup)

Prefab-only authoring guide for **generic Unity UI** screens / popups (NOT TheOneFeature-scoped). Scope is the `.prefab` asset: variant of the com.gdk.core base templates, hierarchy (Background / Content / Foreground / Timeline_Grp), SafeArea scope, anchor/pivot, layout groups, **field-driven Content children**, Addressables registration. Script side (View / Presenter / `[ScreenInfo]`) is OUT OF SCOPE.

> **Mirror counterpart:** `t1k:unity:tof:ui-create-prefab` (at `.claude/skills/t1k-unity-tof-ui-create-prefab/SKILL.md`). When editing either skill, mirror the change.

## Scope — When to Use This Skill vs. the TOF Skill

| Target prefab path | Use skill |
|---|---|
| `Packages/TheOneFeature*` (anything under any TheOneFeature.* package) | `t1k:unity:tof:ui-create-prefab` |
| `Assets/Prefabs/UIs/` (project-level TOF screen registry path) | `t1k:unity:tof:ui-create-prefab` |
| **Anything else** — `Packages/com.gdk.<x>/`, `Packages/<consumer>/`, `Assets/Prefabs/<Feature>/UI/`, project-side feature packages that do not use the TOF runtime | **this skill** |

If the target path matches a TOF row, STOP and switch to the TOF skill — TOF screens variant from a different base, register in a different Addressables group, and integrate with `ScreenManager` differently.

## Authoring Tool — Unity MCP (with documented limits)

Default to the Unity MCP bridge (`mcp__UnityMCP__manage_prefabs`, `mcp__UnityMCP__manage_gameobject`, `mcp__UnityMCP__manage_components`, `mcp__UnityMCP__manage_asset`). See §10 MCP Tool Capability Matrix below for which actions actually work today — **two operations require direct YAML edits as a fallback**: (a) creating a prefab variant (MCP `create_from_gameobject` has no `variant` flag), and (b) adding an asset to an Addressables group (`manage_asset` has no `addressables_add` action). When you do fall back to YAML, follow the exact patterns in §1 (variant) and §7 (Addressables) — they pattern off existing working files, not free-form.

See `t1k:unity:base:mcp-skill` for the MCP install/connect procedure and `rules/unitymcp-the1studio-fork-only.md` for the canonical install.

## Decision Tree

| Intent | Path |
|---|---|
| "New full screen" | Create variant of `UIBaseScreen.prefab` → [§1 Create as Variant](#1-create-as-variant) |
| "New popup / modal" | Create variant of `UIBasePopup.prefab` → [§1 Create as Variant](#1-create-as-variant) |
| "Spawn children for each [SerializeField] on the View" | [§9 Materialize Content Children](#9-materialize-content-children-from-view-fields-mandatory) |
| "User provided a layout image" | [§9.4 Layout image input](#94-layout-image-input-optional) |
| "Add full-bleed background image" | Place under `Background` → [§2 Layer scope](#2-layer-scope) |
| "Add main UI / popup body" | Place under `Content` → [§2 Layer scope](#2-layer-scope) |
| "Add HUD overlay / toast" | Place under `Foreground` → [§2 Layer scope](#2-layer-scope) |
| "Hook open/close animation" | Bind to `Timeline_Grp` (`Tl_Appears` / `Tl_Disappears`) → [§3 Timeline_Grp](#3-timeline_grp) |
| "Set anchors / pivot on a child" | [§5 Anchor / pivot rules](#5-anchor--pivot-rules) |
| "Grid that reflows by aspect ratio" | [§6 Layout groups](#6-layout-groups) |
| "Register prefab in Addressables" | [§7 Addressables registration](#7-addressables-registration) |
| "Which MCP actions actually work?" | [§10 MCP tool capability matrix](#10-mcp-tool-capability-matrix) |

## 1. Create as Variant

Pick exactly one base — never start from an empty Canvas, never duplicate the base (variant so future base fixes propagate).

| Need | Base prefab | GUID source |
|---|---|---|
| Screen (full-page view) | `Packages/com.gdk.core/Prefabs/CommonUIPrefab/UIBaseScreen.prefab` | read `<base>.meta` for `guid:` |
| Popup / modal | `Packages/com.gdk.core/Prefabs/CommonUIPrefab/UIBasePopup.prefab` (itself a variant of UIBaseScreen) | read `<base>.meta` for `guid:` |

### Track A — MCP-preferred path

1. Instantiate the base in a temp scene:
   `mcp__UnityMCP__manage_gameobject(action="create", name="<ScreenName>", prefab_path="<base-path>")`
2. Attach the View MonoBehaviour:
   `mcp__UnityMCP__manage_gameobject(action="modify", target="<ScreenName>", search_method="by_name", components_to_add=["<FullyQualified.ViewType>"])`
3. **Check capability** for variant save: does `mcp__UnityMCP__manage_prefabs.create_from_gameobject` accept a `variant` parameter? **As of 2026-05-28: NO** — the action's schema has only `unlink_if_instance`, which produces a STANDALONE prefab, not a variant. Until upstream `The1Studio/unity-mcp` adds the flag, skip step 4 and go to Track B.
4. **(Future, when MCP adds variant support)** `mcp__UnityMCP__manage_prefabs(action="create_from_gameobject", target="<ScreenName>", prefab_path="<target>", variant=true)`.

### Track B — Minimal-YAML variant (current reality)

Track B is the **mandatory** fallback today. It writes a variant `.prefab` file directly using a fixed template. It is NOT free-form YAML — copy from an existing working variant in the consumer project (e.g. any `*Variant.prefab` already loading at runtime) and swap three fields.

**Inputs to gather before writing:**

| Variable | How to derive |
|---|---|
| `<base-guid>` | `head -3 <base>.prefab.meta` → `guid:` line |
| `<base-root-go-fileID>` | See §1.5 lookup algorithm |
| `<screen-name>` | The View class name (e.g. `SettingsView`) |
| `<view-script-guid>` | `head -3 <ViewName>.cs.meta` → `guid:` line |
| `<random-int64-A>` | Positive int64 anchor ID, distinct per file |

**Template (write to `<target-path>.prefab`):**

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1001 &<random-int64-A>
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: <base-root-go-fileID>, guid: <base-guid>, type: 3}
      propertyPath: m_Name
      value: <screen-name>
      objectReference: {fileID: 0}
    # 20 RectTransform overrides identical to any existing minimal variant
    # (pivot.x/y, anchorMin.x/y, anchorMax.x/y, sizeDelta.x/y,
    #  localPosition.x/y/z, localRotation.w/x/y/z, anchoredPosition.x/y,
    #  localEulerAnglesHint.x/y/z) — all defaults
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: <base-guid>, type: 3}
```

After writing the file, attach the View script via Track A.1+A.2 against a re-instantiated copy, then write back into the variant via `mcp__UnityMCP__manage_prefabs.modify_contents`. Reason: embedding `m_AddedComponents` by hand requires synthetic fileIDs that are fragile; let MCP own it once the variant file exists.

**Wiring follows in §9.3 Track A (direct YAML edit) — MCP cannot wire in-prefab object references today.** Plan for that step when budgeting variant authoring time.

**Refresh:** call `mcp__UnityMCP__refresh_unity(scope="assets", mode="force")` so Unity imports the new asset.

**Validation:** after refresh, the new `.prefab` must start with `--- !u!1001 &<id>` and end with `m_SourcePrefab: ... guid: <base-guid>` matching the chosen base, and import without console errors.

## 1.5 Resolving the base root-GO fileID

Two cases:

**Case A — base is a standalone prefab:**

- Root GO fileID is the `&<id>` anchor on the **first** `!u!1 GameObject` block.
- Quick lookup: `grep -n -m1 '!u!1 &' <base>.prefab` → number after `&`.

**Case B — base is itself a variant** (e.g. `UIBasePopup` is a variant of `UIBaseScreen`):

- Root GO is NOT declared in the base file — it lives in the grandparent. The base references it via its own `m_Modifications`.
- Lookup:

  ```bash
  grep -B1 -A3 'propertyPath: m_Name' <base>.prefab | tail -5
  # Reads as: target: {fileID: <ROOT-GO-FILEID>, guid: <grandparent-guid>, type: 3}
  ```

- The `<ROOT-GO-FILEID>` and **grandparent** `guid` are what you write into the variant — NOT the base's own GUID.

If the base contains no `m_Name` override, find any `target:` in its `m_Modifications` — every root-property override targets the same root GO fileID.

## 2. Layer Scope — Background / Content / Foreground

The base prefab ships three sibling layer containers under the root. Place new GameObjects under the layer matching their visual role. SafeArea only wraps `Content` — the other two are full-bleed.

| Layer | SafeArea? | Render order | What goes here |
|---|---|---|---|
| `Background` | NO | Bottom | Full-screen images, gradients, video backdrops, particle backgrounds. |
| `Content` | YES | Middle | Main UI: popup body, panels, buttons, lists, HUD elements that must stay inside the safe area. |
| `Foreground` | NO | Top | Overlays that float over Content AND extend to screen edges: toasts, FX, tutorial pointers. |

Rules:

- **Never** place full-bleed art under `Content` — SafeArea will inset it.
- **Never** place tap-targets / readable UI under `Background` or `Foreground` — notch / gesture-bar risk.
- Do NOT add a second SafeArea anywhere.
- Do NOT add a CanvasScaler or AspectRatioFitter to the variant root — CanvasScaler lives ONLY on `RootUI`.

## 3. Timeline_Grp — Open / Close Animations

The base ships `Timeline_Grp` with `Tl_Appears` and `Tl_Disappears` PlayableDirectors. Customize per-screen by overriding the bound Timeline assets on the variant — never edit the base's Timeline assets directly. Never delete `Timeline_Grp`, `Tl_Appears`, or `Tl_Disappears` — open/close transitions silently no-op without them.

Author per-screen Timeline assets under `Packages/<consumer-package>/Timelines/<ScreenName>_Appears.playable` and `<ScreenName>_Disappears.playable` (or equivalent project path).

## 4. Reuse Prefabs — DRY

If the SAME widget appears **twice or more** anywhere in the project, it MUST be a prefab and instantiated as a prefab reference — never duplicated GameObjects.

Workflow:

1. Build the widget once as a standalone prefab under `Prefabs/UI/Widgets/`.
2. Drop prefab instances via `mcp__UnityMCP__manage_gameobject(action="create", prefab_path=…)`.
3. Override only per-instance fields.

If `com.gdk.core` or another generic UI package in the project ships reusable widgets, prefer those before building new ones. For TheOneFeature-scoped widgets at `Packages/TheOneFeature.PuzzleUI/Common/Prefabs/`, consult the TOF skill — they may carry TOF-runtime dependencies.

## 5. Anchor / Pivot Rules

**Core principle:** the pivot lives on the same edge as the anchor. Stretch ONLY the axis that must flex with device size; fixed-axis elements use coincident min=max anchors.

| Element role | Anchor min → max | Pivot |
|---|---|---|
| Layer container (`Background` / `Content` / `Foreground`) | (0,0) → (1,1) | (0.5, 0.5) |
| Header / title bar | (0,1) → (1,1) | (0.5, 1) |
| Footer / bottom nav | (0,0) → (1,0) | (0.5, 0) |
| Top-right HUD (coins, settings) | (1,1) → (1,1) | (1, 1) |
| Top-left HUD (back, menu) | (0,1) → (0,1) | (0, 1) |
| Bottom-right corner button | (1,0) → (1,0) | (1, 0) |
| Bottom-left corner button | (0,0) → (0,0) | (0, 0) |
| Centered popup body | (0.5,0.5) → (0.5,0.5) | (0.5, 0.5) |
| Horizontal stretched bar at y=Y | (0,Y) → (1,Y) | (0.5, Y) |
| Vertical stretched bar at x=X | (X,0) → (X,1) | (X, 0.5) |

## 6. Layout Groups

| Pattern | Component |
|---|---|
| Simple 1-axis horizontal list | Unity `HorizontalLayoutGroup` |
| Simple 1-axis vertical list | Unity `VerticalLayoutGroup` |
| Grid that must reflow rows×cols by aspect ratio | `FlexibleLayoutGroupVer2` (`Packages/TheOneFeature/UI/Utilities/UILayout/`) preset `BaseOnContent` |

Never use Unity `GridLayoutGroup` for responsive grids — it fixes cell count, breaking on different aspect ratios. `FlexibleLayoutGroupVer2` lives under the TheOneFeature path historically; it's a generic utility, NOT a runtime dependency on TOF.

## 7. Addressables Registration

Required so the screen-loading system (`ScreenManager.OpenScreen<T>()` or equivalent) can resolve the prefab.

### 7.1 Detect the correct group asset (project-specific)

Group names vary per project. **Do NOT hardcode**. Detect by reading an existing screen's GUID and grepping the group assets:

```bash
existing_guid=$(head -3 'Path/To/AnyExistingVariant.prefab.meta' | grep guid | awk '{print $2}')
grep -l "$existing_guid" Assets/AddressableAssetsData/AssetGroups/*.asset
```

Possible groups seen in the wild:

- `CommonUI.asset` / `BaseUI.asset` — dedicated generic UI group
- `UI.asset` — single group for all UI prefabs
- No group — system resolves by direct prefab reference (Addressables not used for UI)

If the consumer has not committed to a group, **ask the user via `AskUserQuestion`** before registering — silently picking a group locks in a hard-to-reverse convention.

### 7.2 Add entry via direct YAML edit

`mcp__UnityMCP__manage_asset` does NOT expose `addressables_add` (§10). Edit the group asset YAML directly:

1. Read the new prefab's GUID from its `.meta`.
2. Entries under `m_SerializeEntries` are sorted alphabetically by GUID. Grep for the first 1-2 hex chars to find the insertion point.
3. Insert:

   ```yaml
     - m_GUID: <new-prefab-guid>
       m_Address: <ScreenName>
       m_ReadOnly: 0
       m_SerializedLabels: []
       FlaggedDuringContentUpdateRestriction: 0
   ```

4. The `m_Address` MUST equal the prefab basename and match `[ScreenInfo(nameof(MyScreenView))]`.

### 7.3 Refresh

`mcp__UnityMCP__refresh_unity(scope="assets")`.

## 8. Final Checklist (before declaring done)

- [ ] Prefab is a **variant** of `UIBaseScreen.prefab` or `UIBasePopup.prefab` — file starts with `!u!1001 &` and ends with `m_SourcePrefab: {fileID: 100100000, guid: <base-guid>}`
- [ ] Target path is OUTSIDE `Packages/TheOneFeature*` and `Assets/Prefabs/UIs/` (use TOF skill otherwise)
- [ ] Prefab name `<ScreenName>View.prefab` matches the future `[ScreenInfo]` key
- [ ] **Every `[SerializeField]` GameObject/Component field on the View has a matching child** under `Content` (or other layer if explicitly tagged) per §9
- [ ] **Every UI child has explicit `UnityEngine.RectTransform` in `components_to_add`** — never plain `Transform` (would warn under Canvas)
- [ ] **Every child's RectTransform fields (anchor/pivot/sizeDelta/anchoredPos) were set via §9.2.5** post-create — verified by `get_hierarchy` readback or response `transform.position`
- [ ] **Every UI child was re-parented to `Content` / `Background` / `Foreground`** via `modify_contents(target, parent)` — NOT left at prefab root
- [ ] **Every such field is wired** on the View component via direct YAML fileID edit per §9.3 Track A — no null refs except documented `<Widget>Prefab` user-assigns. **Post-wire verification:** `manage_prefabs(action="get_info")` MUST return `prefabType: "Variant"`, `isVariant: true`, `parentPrefab: "<base>.prefab"`. If `prefabType: "Prefab"` the variant link is gone — restore from git and re-author.
- [ ] Full-bleed art placed under `Background` (no SafeArea inset)
- [ ] Main UI / popup body placed under `Content` (SafeArea-respecting)
- [ ] Overlays / toasts placed under `Foreground` (no SafeArea inset)
- [ ] `Timeline_Grp` intact (with `Tl_Appears` + `Tl_Disappears`); per-screen Timelines overridden if customized
- [ ] No second SafeArea added; no CanvasScaler on root; no AspectRatioFitter on root
- [ ] Every child's pivot sits on the same edge as its anchor (§5)
- [ ] Any widget used 2+ times is referenced as a prefab instance, not duplicated
- [ ] Grids use `FlexibleLayoutGroupVer2`, not `GridLayoutGroup`
- [ ] Prefab added to the consumer's chosen UI Addressables group with address = prefab name (or confirmed not required)
- [ ] `refresh_unity` succeeded with zero console errors
- [ ] If layout image was provided: every text-on-button region has a nested `<Button>/Label` TMP child with `m_text` set + RT placement applied (§9.4.B)
- [ ] If layout image was provided: every standalone text region has a `Txt<Name>` TMP child under `Content` (or `Foreground`) with `m_text` set + RT placement applied (§9.4.C)
- [ ] No TMP child was spawned for text that ALREADY has a `[SerializeField]` TMP field on the View — those are covered by the §9.4.A field-driven path

## 9. Materialize Content Children from View Fields (MANDATORY)

A skeleton variant with no children is incomplete — the Presenter's `OnViewReady` will null-ref on `this.View.<Field>.<Member>` because the field was never spawned. This section mandates field-driven child creation.

### 9.1 Inspect the View script

Parse the View class for `[SerializeField]` and `[field: SerializeField]` declarations. Capture for each field: name, type, default value.

**Field-driven inspection is the INTERACTIVE-content driver (Buttons, Transforms, runtime-mutated widgets), but it is NOT the only driver.** If a layout image is supplied, §9.4.B / §9.4.C ALSO spawn TMP decorations (text-on-button labels, standalone text badges) that have NO corresponding `[SerializeField]` field — purely visual content the Presenter never touches. Don't suppress them just because the View doesn't declare them.

### 9.2 Widget-type → child template mapping

For each field, create a child GameObject using this table. **ALWAYS include `UnityEngine.RectTransform` in `components_to_add` — never let MCP default to plain `Transform`**, which is invalid under a Canvas. (Verified 2026-05-28: `create_child` without explicit `components_to_add` ships a plain `Transform`.)

| Field type | `components_to_add` (mandatory order) | Default placement (applied via §9.2.5) |
|---|---|---|
| `UnityEngine.UI.Button` | `["UnityEngine.RectTransform", "UnityEngine.UI.Image", "UnityEngine.UI.Button"]` | Top-right HUD: anchor (1,1)-(1,1), pivot (1,1), size 120×120, anchoredPos (-40,-40) |
| `UnityEngine.RectTransform` / `UnityEngine.Transform` (container) | `["UnityEngine.RectTransform"]` — for Transform fields too. If name contains `Container`/`Group`/`Layout`/`Row`/`List`, also add `"UnityEngine.UI.HorizontalLayoutGroup"` | Bottom-stretched bar: anchor (0,0)-(1,0), pivot (0.5,0), sizeDelta (0,200), anchoredPos (0,200). For full-bleed parent containers: anchor (0,0)-(1,1), pivot (0.5,0.5), sizeDelta (0,0), anchoredPos (0,0). |
| `UnityEngine.UI.Image` | `["UnityEngine.RectTransform", "UnityEngine.UI.Image"]` | Centered: anchor (0.5,0.5)-(0.5,0.5), pivot (0.5,0.5), size 200×200 |
| `TMPro.TMP_Text` / `TextMeshProUGUI` (field-driven path only) | `["UnityEngine.RectTransform", "TMPro.TextMeshProUGUI"]` | Top: anchor (0,1)-(1,1), pivot (0.5,1), sizeDelta (0,80), anchoredPos (0,-40). For text-on-button OR standalone text NOT driven by a `[SerializeField]` field, see **§9.4.B** (nested `Label`) / **§9.4.C** (standalone `Txt<Name>`) — those are auto-spawned from the layout image without needing a field. |
| `UnityEngine.UI.Slider` | `["UnityEngine.RectTransform", "UnityEngine.UI.Slider"]` | Centered, size 300×40 |
| `UnityEngine.UI.ScrollRect` | `["UnityEngine.RectTransform", "UnityEngine.UI.ScrollRect"]` (build Viewport + Content as separate `create_child` calls) | Stretch: anchor (0,0)-(1,1), pivot (0.5,0.5), offsets 20 |
| `<CustomWidget>` where the type IS a `MonoBehaviour` AND a project prefab named `<CustomWidget>.prefab` exists | `create_child` with `source_prefab_path: "<path/to/<CustomWidget>.prefab>"` instead of `components_to_add` | Inherit from prefab |
| Field name ends in `Prefab` (e.g. `ItemPrefab`, `SlotPrefab`) — used by Presenter for runtime spawning | **Do NOT spawn a child.** Document as user-assigns-in-Inspector. | n/a |
| Primitive (`string`, `int`, `float`, `bool`, enum) | Skip — not a GameObject ref | n/a |

If a field has `[Header("Background")]` / `[Header("Foreground")]` attribute, override the parent from `Content` to that layer.

**`create_child`'s `parent_path` argument is IGNORED** (verified 2026-05-28) — every child lands at the prefab root regardless. Workaround: omit `parent_path`, then issue a separate `modify_contents(target: "<ChildName>", parent: "<LayerName>")` call to re-parent under the correct layer (`Content` / `Background` / `Foreground`). See §10 for the exact pattern.

### 9.2.5 Apply RectTransform AFTER create_child (MANDATORY)

`create_child` does NOT set anchor/pivot/sizeDelta/anchoredPosition — the new child is born with default-zeros and Unity's "centered, 100×100" implicit defaults. Without this step, every child stacks at the screen center regardless of the §9.2 default or §9.4 image-derived placement.

**For EACH child** spawned in §9.2, issue a second `modify_contents` call:

```
mcp__UnityMCP__manage_prefabs(
  action="modify_contents",
  prefab_path="<target>.prefab",
  target="<ChildName>",          # name lookup inside the prefab
  parent="<LayerName>",          # one of: Content / Background / Foreground (omit to keep at root)
  component_properties={
    "UnityEngine.RectTransform": {
      "m_AnchorMin.x": <num>, "m_AnchorMin.y": <num>,
      "m_AnchorMax.x": <num>, "m_AnchorMax.y": <num>,
      "m_Pivot.x": <num>,     "m_Pivot.y": <num>,
      "m_AnchoredPosition.x": <num>, "m_AnchoredPosition.y": <num>,
      "m_SizeDelta.x": <num>, "m_SizeDelta.y": <num>
    }
  }
)
```

**Property name form is mandatory flat-axis** — `m_AnchorMin.x` and `m_AnchorMin.y` as SEPARATE keys. The Vector2 object form `m_AnchorMin: {x:0, y:0}` returns `Unsupported SerializedPropertyType: Vector2` (verified 2026-05-28). Same flat-axis rule for `m_AnchorMax`, `m_Pivot`, `m_AnchoredPosition`, `m_SizeDelta`.

**Verification:** the response includes `transform.position` reflecting `anchoredPosition` — quick sanity check. For deeper verify, call `get_hierarchy` and confirm the child path is `<Root>/Content/<ChildName>`.

### 9.3 Wire references on the View component (direct YAML — MCP gap workaround)

**MCP `modify_contents(component_properties)` cannot wire in-prefab object references today** (verified 2026-05-28 against The1Studio/unity-mcp@beta). The resolver at `ComponentOps.cs:719` routes `{"name":...}` payloads through `ResolveSceneObjectByName` which hardcodes scene-only lookup (`ComponentOps.cs:875`); the loaded prefab root from `ManagePrefabs.ModifyContents:617` is never forwarded into the resolver. `GameObjectLookup` only falls back to `PrefabStageUtility.GetCurrentPrefabStage()` when a prefab is OPEN in Prefab Mode (`GameObjectLookup.cs:164,292`) — an in-memory `LoadPrefabContents` is invisible to that fallback. All three payload forms (`name` / `instanceID` / `path`) return scene-scoped errors on the prefab-asset path.

Use the **direct YAML edit** path below. It preserves the variant link (no Prefab-Mode-collapse risk) and works against any MCP version.

#### Procedure (Track A — direct YAML, MANDATORY today)

1. **Find each child component's fileID.** After §9.2 + §9.2.5 saved the prefab, parse the file with this awk script to map `m_Name` → component fileIDs:

   ```bash
   awk '
   /^--- !u!1 &/ { cur_name="" }
   /^  m_Name: / { if (!cur_name) cur_name=$2; print "GO " cur_name }
   /^--- !u!114 &/ { comp=$3; sub("&","",comp); print "  MB " comp " under " cur_name }
   /^--- !u!224 &/ { comp=$3; sub("&","",comp); print "  RT " comp " under " cur_name }
   ' <prefab.prefab>
   ```

   Component type discrimination (when a child has multiple MonoBehaviours, e.g. Image + Button on a button): read 8 lines past the `!u!114 &<fileID>` anchor and match `m_EditorClassIdentifier:` — Button = `UnityEngine.UI::UnityEngine.UI.Button`, Image = `UnityEngine.UI::UnityEngine.UI.Image`, TMP = `Unity.TextMeshPro::TMPro.TextMeshProUGUI`. For Transform-typed View fields, use the `!u!224` RectTransform fileID directly.

2. **Locate the View MonoBehaviour block.** After §9.2's `components_to_add: ["<View>"]` call, the View MB is appended near the end of the file as:

   ```yaml
   --- !u!114 &<view-mb-fileID>
   MonoBehaviour:
     ...
     m_Script: {fileID: 11500000, guid: <ViewScript.cs.meta guid>, type: 3}
     m_EditorClassIdentifier: <Namespace>::<FullName>
     <field1>: {fileID: 0}
     <field2>: {fileID: 0}
     ...
   ```

   Every `[SerializeField]` GameObject/Component field starts as `{fileID: 0}`.

3. **Replace each `{fileID: 0}` with the matching component fileID** via `Edit` tool. Map per the §9.2 widget-type column:

   | Field type | Wire to |
   |---|---|
   | `UnityEngine.UI.Button` | the MB whose `m_EditorClassIdentifier` ends in `UnityEngine.UI.Button` (typically the 2nd MB after Image, per step 1's identifier check) |
   | `UnityEngine.UI.Image` | the MB whose `m_EditorClassIdentifier` ends in `UnityEngine.UI.Image` (typically the 1st MB) |
   | `TMPro.TMP_Text` / `TextMeshProUGUI` | TMP MB fileID |
   | `UnityEngine.RectTransform` / `UnityEngine.Transform` | RectTransform (`!u!224`) fileID |
   | `<CustomWidget>` MonoBehaviour | the corresponding `!u!114` fileID on the child |

4. **Refresh + verify.** Call `mcp__UnityMCP__refresh_unity(scope="assets", mode="force")`, then `mcp__UnityMCP__manage_prefabs(action="get_info", prefab_path=...)`. Required readback: `prefabType: "Variant"` AND `isVariant: true` AND `parentPrefab: "<base>.prefab"`. If `prefabType: "Prefab"` (standalone), the variant link is gone — restore from git and re-author per §1.

5. **Read console for errors.** `mcp__UnityMCP__read_console(types=["error"])` — must be 0 errors mentioning the View type or `MissingReferenceException`.

#### Track B — Prefab Mode opt-in (OPT-IN, collapses Track-B variants)

If you ACCEPT losing the variant link (becomes a standalone prefab — same Prefab-Mode-save-collapse failure mode as the existing Gotcha), you can drive Prefab Mode through MCP:

- Open the prefab in Prefab Mode (currently no headless MCP action exists — `manage_asset` has no `open` action; `execute_menu_item("Assets/Open Prefab Asset")` requires Project-window selection which MCP cannot set). **Practical reality: user must double-click the prefab in the Unity Editor to enter Prefab Mode.**
- While the stage is open: `mcp__UnityMCP__manage_components(action="set_property", target="<RootName>", search_method="by_name", component_type="<ViewType>", properties={...})` — works because `GameObjectLookup.SearchGameObjects` falls back to `PrefabStageUtility.GetCurrentPrefabStage()` (verified `GameObjectLookup.cs:164`).
- `mcp__UnityMCP__manage_editor(action="close_prefab_stage")` to save + exit.
- **Verify the prefab is NOT a variant anymore:** `get_info` will return `prefabType: "Prefab"`. Document the collapse in the commit message.

Use Track A unless you have a deliberate reason to abandon variant inheritance.

#### Why this gap exists

Upstream root cause (file the gap via `/t1k:issue` against `The1Studio/unity-mcp@beta` if it doesn't already exist):

- `ComponentOps.SetObjectReference:719` → calls `ResolveSceneObjectByName(prop, name, ...)`
- `ResolveSceneObjectByName:861-887` → `GameObjectLookup.SearchGameObjects(ByName, ...)` → returns `"No GameObject named 'X' found in scene."` when no scene match (line 875)
- `ManagePrefabs.ModifyContents:617` → `PrefabUtility.LoadPrefabContents(path)` → does NOT enter Prefab Stage; the loaded root is never passed into `ComponentOps`
- **Proposed fix:** overload `SetObjectReference` / `ResolveSceneObjectByName` to accept optional `GameObject prefabRoot`. When set, search `prefabRoot.GetComponentsInChildren<Transform>(true)` instead of the active scene.

### 9.4 Layout image input (optional)

If the user provided a layout image (PNG/JPG/sketch) as a skill argument:

1. **Read** the image via Read tool — Claude vision handles PNG/JPG natively.
2. **Identify regions**:
   - **Interactive widgets** — buttons, sliders, scroll lists, HUD counters (drive §9.4.A field-driven mapping).
   - **Text-inside-button labels** — text rendered ON TOP of a button region (drive §9.4.B nested-label spawning).
   - **Standalone text decorations** — text NOT inside any button (titles, badges, subtitles) (drive §9.4.C standalone TMP spawning).

#### §9.4.A — Map field-driven regions (interactive widgets)

For each `[SerializeField]` field on the View, map its name to the closest visual element by name+type heuristic.

**Override** §9.2 default placements with image-derived anchors / pivots / sizes. Conversion formulas (image is Y-down, Unity anchor space is Y-up):

- `anchor.x = image_px_x / image_width`
- `anchor.y = 1 - (image_px_y / image_height)`
- `pivot.{x,y}` MUST match anchor edge per §5 (top-left corner → pivot (0,1); bottom-center → pivot (0.5,0); etc.)
- `sizeDelta` = approximate element width/height in pixels at the screen's reference resolution from `RootUI`'s CanvasScaler.
- `anchoredPosition` = offset from anchor point to pivot, in reference-resolution px.

Applied via §9.2.5 (these REPLACE the §9.2 defaults in the second `modify_contents` call). There is no separate "merge" step.

#### §9.4.B — Auto-spawn nested TMP labels for text-on-button

When the image shows text rendered ON TOP of a field-driven Button child (e.g. "Level 999" inside a `BtnPlay`):

1. **Skip if the View already declares a TMP field for the label** — the §9.4.A field-driven path covers it. Do NOT double-spawn.

2. **Create a NESTED child under the parent Button GO** via two-step pattern (create + re-parent, per §9.2.5 + §10):

   ```
   mcp__UnityMCP__manage_prefabs(
     action="modify_contents",
     prefab_path="<target>.prefab",
     create_child=[{"name": "Label", "components_to_add": ["UnityEngine.RectTransform", "TMPro.TextMeshProUGUI"]}]
   )
   # then re-parent:
   mcp__UnityMCP__manage_prefabs(
     action="modify_contents",
     prefab_path="<target>.prefab",
     target="Label",
     parent="BtnPlay"
   )
   ```

   Canonical name: `Label`. Multi-region: `LabelTitle`, `LabelValue`, etc.

3. **Apply RectTransform placement** via §9.2.5. Default for centered text inside a button: anchor (0,0)-(1,1), pivot (0.5,0.5), sizeDelta (0,0), anchoredPos (0,0) — stretches to fill. Image-derived footprint overrides this default.

4. **Apply text content** via `modify_contents`:

   ```
   mcp__UnityMCP__manage_prefabs(
     action="modify_contents",
     prefab_path="<target>.prefab",
     target="Label",
     component_properties={"TMPro.TextMeshProUGUI": {"m_text": "Level 999"}}
   )
   ```

   The serialized field name is **`m_text`** (verified against existing TMP prefabs). Do NOT use `text` (public property) or `m_Text` (capitalised — wrong).

5. **Naming collision guard:** if `<Button>/Label` already exists (variant re-author case), SKIP spawn — preserve Inspector hand-edits.

#### §9.4.C — Auto-spawn standalone TMP children for non-button text

When the image shows text NOT inside any field-driven Button region (titles, badges, captions):

1. **Skip if the View already declares a TMP field for it** (same dedup rule as §9.4.B).

2. **Create a child under `Content`** — same two-step create + re-parent pattern. Use `Foreground` if the image shows the text floats as an overlay.

   Naming: `Txt<CamelCaseFromText>` (e.g. text "Normal" → `TxtNormal`; "Level Complete!" → `TxtLevelComplete`). Truncate to a representative slug if text > 20 chars.

3. **Apply RectTransform placement** per the image-derived footprint via §9.2.5 (same rules as §9.4.A).

4. **Apply text content** via the same `m_text` modify_contents call as §9.4.B step 4.

5. **Default font size:** derive from image text height relative to reference resolution. Heuristic: `m_fontSize = round(image_text_pixel_height * (reference_width / image_width))` — typical mobile values 36-72. User can adjust in Inspector if needed.

6. **Naming collision guard:** same as §9.4.B step 5.

#### §9.4.D — Echo the full inferred layout (MANDATORY before any apply)

Before applying any of §9.4.A/B/C, echo the COMPLETE detected layout back to the user as a single confirmation block:

> "From the image I see:
> - **Field-driven (§9.4.A):** `<FieldA>` top-right ~80px from edges, `<FieldB>` 4-cell horizontal row at y≈200px from bottom, ...
> - **Text-on-button labels (§9.4.B):** `BtnPlay/Label` text "Level 999" centered, `BtnLevelSelect/Label` text "Levels" centered
> - **Standalone text (§9.4.C):** `TxtNormal` text "Normal" above BtnPlay (~y=180 from bottom)
>
> Apply? (yes / adjust)"

Wait for confirmation. Capture user adjustments and re-apply.

If no image is provided, only §9.4.A defaults apply via §9.2.5 with no echo step. §9.4.B/C are no-ops without an image source.

### 9.5 Checklist gates

Gates relevant to §9 (see §8 for the full list):

- Every `[SerializeField]` GameObject/Component field has a matching child
- Every UI child has explicit `UnityEngine.RectTransform` in `components_to_add` (never plain `Transform`)
- Every child's RectTransform (anchor/pivot/sizeDelta/anchoredPos) was set via §9.2.5 — verified via `get_hierarchy` readback or response `transform.position`
- Every UI child was re-parented to `Content` / `Background` / `Foreground` via §9.2.5's `parent:` arg (not at prefab root)
- Every such field is wired on the View component via direct YAML fileID edit per §9.3 Track A — no null refs except documented `<Widget>Prefab` user-assigns; post-wire `get_info` confirms `isVariant: true`

## 10. MCP Tool Capability Matrix

Quick-reference status as of **2026-05-28** against The1Studio/unity-mcp@beta. Re-test before assuming a row is still accurate.

| Operation | MCP call | Status | Notes |
|---|---|---|---|
| Instantiate prefab in scene | `manage_gameobject(action="create", prefab_path=…)` | ✅ Works | Use to bring base into temp scene |
| Add component to instance | `manage_gameobject(action="modify", components_to_add=[…])` | ✅ Works | Add View MonoBehaviour |
| Save GameObject as standalone prefab | `manage_prefabs(action="create_from_gameobject", unlink_if_instance=true)` | ✅ Works | **Loses variant link** — not what §1 wants |
| Save GameObject as prefab VARIANT | `manage_prefabs(action="create_from_gameobject", variant=true)` | ❌ Schema lacks `variant` param | Use §1 Track B minimal-YAML fallback |
| Read prefab hierarchy | `manage_prefabs(action="get_hierarchy", prefab_path=…)` | ⚠️ Assets/ only | Errors on `Packages/…` paths |
| Modify prefab contents — create child | `manage_prefabs(action="modify_contents", create_child=[…])` | ✅ Works | Default GO has plain `Transform`. ALWAYS pass `components_to_add: ["UnityEngine.RectTransform", …]` for UI children. |
| `create_child`'s `parent_path` arg | `create_child=[{name, parent_path, …}]` | ❌ IGNORED | Children land at prefab root regardless. Workaround: separate `modify_contents(target, parent)` call (next row). |
| Re-parent existing child | `modify_contents(target="<ChildName>", parent="<LayerName>")` | ✅ Works | Name lookup works; `parent` is a sibling/layer name. Use to move under `Content` / `Background` / `Foreground`. |
| Set RectTransform anchor/pivot/size | `modify_contents(component_properties={"UnityEngine.RectTransform": {"m_AnchorMin.x": …, …}})` | ✅ Works with flat-axis form | Vector2 form (`m_AnchorMin: {x,y}`) returns `Unsupported SerializedPropertyType`. Use `m_AnchorMin.x` + `m_AnchorMin.y` as separate keys. |
| Upgrade plain Transform → RectTransform | `modify_contents(target, components_to_add=["UnityEngine.RectTransform"])` | ✅ Works | Replaces the Transform in-place (RectTransform inherits). |
| Wire in-prefab object reference (headless) | `modify_contents(component_properties={ViewType: {field: {"name|instanceID|path": ..., "component": ...}}})` | ❌ ALL forms FAIL on prefab assets | Resolver hardcoded to scene lookup (`ComponentOps.cs:719,875`); prefab root from `ManagePrefabs:617` never forwarded. Use direct YAML fileID edit per §9.3 Track A. Gap tracked upstream as MCP issue against The1Studio/unity-mcp@beta. |
| Wire in-prefab object reference (Prefab Mode) | `manage_components(action="set_property", search_method="by_name", target="<Root>", component_type="<View>", properties={...})` while Prefab Mode is open | ⚠️ Works but COLLAPSES Track-B variants | `GameObjectLookup` falls back to `PrefabStageUtility.GetCurrentPrefabStage()` (`GameObjectLookup.cs:164`). Save-on-close rewrites the file as a standalone (`!u!1` not `!u!1001`). Use only if you accept losing the variant link. See §9.3 Track B. |
| Set component property (top-level tool) | `manage_components(action="set_property")` | ⚠️ Scene-only + tool deferred | Only resolves live scene objects, not prefab-asset internals. For prefab assets use `modify_contents`. |
| Add asset to Addressables group | `manage_asset(action="addressables_add")` | ❌ Not in schema | Edit `*.asset` YAML directly (§7.2) |
| Force re-import | `refresh_unity(scope="assets", mode="force")` | ✅ Works | Call after direct-YAML edits |
| Read console for errors | `read_console(types=["error"])` | ✅ Works | Filter via `filter_text` |
| Find GameObjects in scene | `find_gameobjects(search_term=…)` | ✅ Works | Use for cleanup-after-instantiate |
| Search assets by name | `manage_asset(action="search", search_pattern=…)` | ⚠️ Inconsistent | Sometimes returns 0 for existing assets; fall back to `Bash find` |

When a row marked ❌ blocks progress: file a `[t1k:mcp-gap]` marker per `rules/workflow-failure-auto-issue.md` against `The1Studio/unity-mcp`.

## Scope

Authoring of `.prefab` assets for generic Unity UI screens / popups OUTSIDE TheOneFeature paths. Does NOT cover: View/Presenter C# scripts, `[ScreenInfo]` attribute wiring, VContainer registration, runtime open/close calls, or TheOneFeature-runtime integration.

## Gotchas

- **Layout-image text decorations are auto-spawned even without a `[SerializeField]` field.** Text inside a field-driven button (e.g. "Level 999" inside `BtnPlay`) becomes a nested `<Button>/Label` TMP child (§9.4.B); standalone text (e.g. "Normal" caption above a pill) becomes a `Txt<Name>` child under `Content` (§9.4.C). Both get full RT placement per §9.2.5 + `m_text` content from the image. They are NOT wired to the View script — the Presenter never touches them, they are purely visual. User can later promote to `[SerializeField]` if interactivity is added. Without this, image-driven UIs ship with empty buttons and missing labels.
- **TMP serialized text property is `m_text`, NOT `text` or `m_Text`.** Verified against existing TMP prefabs (e.g. `titlePopup1.prefab`). The `text` form is the runtime C# property (`TMP_Text.text`); the `m_Text` form (capitalised M) is wrong. MCP `modify_contents(component_properties={"TMPro.TextMeshProUGUI": {"m_text": "<value>"}})` is the only working form.
- **`create_child` ignores `parent_path`.** Verified 2026-05-28: regardless of the value passed, the new child lands at the prefab root. Always follow with a separate `modify_contents(target: "<ChildName>", parent: "<LayerName>")` call to re-parent.
- **`create_child` defaults to plain `Transform`.** Without explicit `components_to_add`, the new GameObject ships with `UnityEngine.Transform` — invalid under a Canvas. ALWAYS pass `components_to_add: ["UnityEngine.RectTransform", …]` for UI children. Recovery: `modify_contents(target, components_to_add: ["UnityEngine.RectTransform"])` replaces the Transform in-place because RectTransform inherits.
- **RectTransform Vector2 properties need flat-axis form.** MCP rejects `m_AnchorMin: {x:0, y:1}` with `Unsupported SerializedPropertyType: Vector2`. Use separate keys `m_AnchorMin.x` and `m_AnchorMin.y`. Same for `m_AnchorMax`, `m_Pivot`, `m_AnchoredPosition`, `m_SizeDelta`.
- **Headless field-wiring via `modify_contents(component_properties)` does NOT work for in-prefab object references.** Verified 2026-05-28: ALL three payload forms — `{"name":...}`, `{"instanceID":...}`, `{"path":...}` — fail with scene-scoped errors when targeting children inside a prefab ASSET (not a Prefab Stage). Root cause: `ComponentOps.SetObjectReference:719` routes name lookups through `ResolveSceneObjectByName` which hardcodes scene search at `ComponentOps.cs:875`; the prefab root loaded by `ManagePrefabs.ModifyContents:617` via `PrefabUtility.LoadPrefabContents` is never forwarded into the resolver. `GameObjectLookup` only falls back to `PrefabStageUtility.GetCurrentPrefabStage()` when a prefab is OPEN in Prefab Mode (`GameObjectLookup.cs:164,292`) — an in-memory load is invisible to that fallback. **Workaround: direct YAML edit of `<field>: {fileID: <ComponentFileID>}` on the View's MonoBehaviour block** (§9.3 Track A). Preserves variant link, works against any MCP version. Track B (Prefab Mode + `manage_components.set_property`) does work but collapses Track-B variants to standalone — use only if you accept losing the variant link.
- **Children without §9.2.5 position step stack at center.** `create_child` does NOT set anchor/pivot/sizeDelta/anchoredPosition. The two-step pattern (`create_child` → `modify_contents(component_properties.RectTransform)`) is MANDATORY, not advisory.
- **`create_from_gameobject` produces a STANDALONE prefab, not a variant.** MCP schema has no `variant` flag (verified 2026-05-28). Use §1 Track B minimal-YAML. Silent-failure symptom: result starts with `--- !u!1 &<id>\nGameObject:` instead of `--- !u!1001 &<id>\nPrefabInstance:`.
- **Prefab-Mode save collapses a Track B variant to standalone.** If you instantiate the Track B variant in scene Prefab Mode, add structural changes (new children), then `close_prefab_stage`, Unity rewrites the file as a fully-fleshed `!u!1 GameObject` prefab — `m_SourcePrefab` is gone. Workaround: skip Prefab Mode for structural changes. Use `mcp__UnityMCP__manage_prefabs.modify_contents(create_child=[...], component_properties=...)` directly against the asset path — it preserves variant overrides.
- **Addressables group name is project-specific.** Never hardcode — detect via §7.1 grep, then `AskUserQuestion` if undetermined.
- **Skeleton prefab without children → runtime NullReferenceException.** Skipping §9 leaves `[SerializeField]` refs unwired. §9 is MANDATORY, not advisory.
- **Field-name `*Prefab` fields are user-assigns, not children.** Used by Presenter for runtime spawning — do NOT materialize as children.
- **Layout image hallucination.** §9.4 MUST echo interpretation before applying. Never silently spawn children from a guessed layout.
- **Variant-of-variant root-GO fileID lookup.** For chains like `UIBasePopup → UIBaseScreen`, root GO fileID points to the grandparent. See §1.5 Case B.
- **Used the wrong skill** — TOF-path prefab authored with this skill (or vice versa) variants from the wrong base and silently fails. Always check the path-based rule in Scope.
- **Duplicating the base instead of variant-ing it** — duplicated screens stop receiving base-prefab fixes. Always Track A→B.
- **Prefab name = Addressables address = `[ScreenInfo]` key.** Drift = silent load failure. Use `nameof(MyScreenView)`.
- **Full-bleed background placed under `Content`** — SafeArea insets it, revealing letterboxing.
- **Tap-targets placed under `Background` or `Foreground`** — notch / gesture-bar risk. Interactive UI MUST live under `Content`.
- **Deleting `Timeline_Grp`, `Tl_Appears`, or `Tl_Disappears`** — transitions silently no-op.
- **Editing the base's Timeline assets directly** — changes every screen. Override per-variant.
- **Adding a second SafeArea** — double inset. Base ships exactly one on `Content`.
- **CanvasScaler / AspectRatioFitter on the variant root** — fights `RootUI`'s scaler.
- **`GridLayoutGroup` for responsive grids** — fixed cell count breaks under aspect changes. Use `FlexibleLayoutGroupVer2`.
- **Pivot not matching anchor edge** — element drifts across resolutions. Apply §5 rigidly.
- **Silently picking an Addressables group** — locks in a hard-to-reverse convention. Ask the user when unclear.
- **Renaming a prefab after Addressables registration without updating the address** — address does NOT auto-rename.
- **Hand-editing `.prefab` YAML beyond the §1 Track B template** — corrupts variant overrides silently.
