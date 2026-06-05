---
name: t1k:unity:ui:ugui
description: Unity uGUI Canvas UI system — Canvas, RectTransform, Image, Button, TMP_Text, layouts, drag-and-drop, programmatic creation, DOTS ECS bridge patterns. Use when building runtime game UI with Canvas.
effort: high
context: fork
keywords: [uGUI, UI, canvas, unity]
version: 2.0.1
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: ui
protected: false
---

# Unity uGUI (Canvas UI)

Canvas-based UI for Unity 6. Use for gameplay HUD, menus, inventory grids, health bars.
For Editor UI or data-heavy panels, prefer UI Toolkit (`unity-ui-toolkit` skill).

## When to Use uGUI vs UI Toolkit

| Feature | uGUI (Canvas) | UI Toolkit |
|---------|--------------|------------|
| Runtime game UI | Preferred | Supported (Unity 6+) |
| World-space UI | Native support | Not supported |
| Drag-and-drop | Built-in interfaces | Manual |
| Editor extensions | Not supported | Preferred |
| Animation/tweening | DOTween/LitMotion | USS transitions |
| TextMeshPro | Native integration | Built-in text |

## Canvas Setup (Programmatic)

```csharp
// Screen-space overlay (HUD) — no camera needed
var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
var canvas = canvasGO.GetComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
canvas.sortingOrder = 10;

var scaler = canvasGO.GetComponent<CanvasScaler>();
scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = new Vector2(1920, 1080);
scaler.matchWidthOrHeight = 0.5f; // blend width/height matching

// EventSystem (required for clicks/drags)
if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
{
    var esGO = new GameObject("EventSystem",
        typeof(UnityEngine.EventSystems.EventSystem),
        typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
}
```

→ See `references/components-quick-ref.md` for Image, RawImage, TMP, Button, Toggle, Slider, Dropdown, InputField.

## RectTransform Anchoring

```csharp
// Stretch to fill parent
rt.anchorMin = Vector2.zero;     // bottom-left
rt.anchorMax = Vector2.one;      // top-right
rt.offsetMin = Vector2.zero;     // left/bottom padding
rt.offsetMax = Vector2.zero;     // right/top padding

// Fixed size, anchored top-left
rt.anchorMin = rt.anchorMax = new Vector2(0, 1); // top-left
rt.pivot = new Vector2(0, 1);
rt.anchoredPosition = new Vector2(10, -10);
rt.sizeDelta = new Vector2(200, 50);

// Fixed size, centered
rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
rt.pivot = new Vector2(0.5f, 0.5f);
rt.anchoredPosition = Vector2.zero;
rt.sizeDelta = new Vector2(300, 300);
```

## Layout Groups

```csharp
// Grid layout (inventory slots)
var grid = parent.gameObject.AddComponent<GridLayoutGroup>();
grid.cellSize = new Vector2(64, 64);
grid.spacing = new Vector2(4, 4);
grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
grid.constraintCount = 8; // 8 columns
grid.childAlignment = TextAnchor.UpperLeft;
```

→ See `references/layouts-and-drag-drop.md` for HorizontalLayout, ScrollRect, drag-and-drop.
For reusable grid cells implement `IInventoryGridCellHandler` (DOTSUI) — see `dots-inventory-grid` skill.

## DOTS ECS Bridge

```csharp
// MonoBehaviour reads ECS data each frame for UI updates
public class InventoryUI : MonoBehaviour {
    private EntityManager _em;
    private EntityQuery _playerQuery;

    void Start() {
        _em = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerQuery = _em.CreateEntityQuery(typeof(PlayerTag));
    }

    void LateUpdate() {
        if (_playerQuery.IsEmpty) return;
        var entity = _playerQuery.GetSingletonEntity();
        var grid = _em.GetBuffer<InventoryGridCell>(entity);
        // Update UI from grid buffer...
    }
}
```

→ See `references/dots-bridge-and-performance.md` for full patterns.

## Common Gotchas

1. **raycastTarget** — disable on decorative Text/Image (perf)
2. **Canvas rebuild** — separate dynamic from static canvases
3. **InputSystemUIInputModule required** — replaces StandaloneInputModule
4. **TMP only** — always TextMeshProUGUI, never UnityEngine.UI.Text
5. **World-space Canvas** — needs EventCamera set for raycasts
6. **Layout rebuilds** — `LayoutRebuilder.ForceRebuildLayoutImmediate()` if reading size same frame
7. **ScreenSpaceOverlay not captured by Camera.Render()** — MCP screenshots miss overlay Canvas; use ScreenSpaceCamera if you need camera captures to include UI
8. **Camera setup for ScreenSpaceCamera** — assign `canvas.worldCamera = Camera.main; canvas.planeDistance = 10f;` and set camera's culling mask to include UI layer
9. **GridLayoutGroup + SetActive(false)** — never use `SetActive(false)` on grid cell GameObjects; `GridLayoutGroup` collapses hidden children, breaking cell positions. Instead, hide visuals (disable `Image`/`CanvasGroup.alpha = 0`) while keeping the GameObject active
10. **NEVER use `??` with Unity objects** — `GetComponent<T>() ?? AddComponent<T>()` silently fails because C# `??` bypasses Unity's overridden `== null`. Always use: `var c = GetComponent<T>(); if (c == null) c = AddComponent<T>();`
11. **NEVER use `HasComponent<EnumType>()` in ECS** — Enums are NOT components. Access via `em.GetComponentData<Item>(e).Rarity` instead.
12. **CanvasScaler portrait setup**: For portrait-locked mobile games, use `referenceResolution = new Vector2(1080, 1920)` with `matchWidthOrHeight = 0.5f`. Do NOT use match=1.0 — it causes text to shrink on wider phones (18:9, 20:9). The 0.5 blend keeps text readable across all common mobile aspect ratios.
13. **Screen Space Overlay not captured by Camera.Capture**: MCP `manage_camera screenshot` won't show Overlay Canvas UI. Only Camera renders are captured. To verify Overlay UI, check component properties via MCP resource reads or use `capture_source='scene_view'`.
14. **`active=true` ≠ UI works** — see "UI Behavior Validation" section below. A panel can be on-screen, scale-resolved, AND completely empty/non-functional because the populate path was never called. State-only Play-mode checks lie.
15. **Template-spawn pattern is a hidden runtime contract** — if a screen has a child named `*Template`, `*Prefab`, or `*Slot` that's hidden on start, the screen expects an external caller to invoke a populate method (typically `SetEntries`, `Populate`, `Bind`, `Refresh`). The method *existing* in the screen class proves nothing — grep the codebase for *call sites*. Zero external call sites = the feature is unfinished.
16. **Deferred-work comments are red flags, not annotations** — `// Phase F wires the roster source`, `// for now Show(true) is sufficient`, `// TODO: populate from registry` mean the UI is half-built. Treat as "broken" until proven otherwise; never validate only the panel container.
17. **HorizontalLayoutGroup / VerticalLayoutGroup do NOT control child SIZE by default — children extrude past the parent and steal raycasts** (2026-05-26, ChaosForge bottom-nav bug): Unity's default `HorizontalLayoutGroup` / `VerticalLayoutGroup` config is `childControlWidth=false, childControlHeight=false, childForceExpandWidth=true, childForceExpandHeight=true`. With `childControlSize=false`, HLG/VLG only positions children — it does NOT resize them. A new `GameObject(..., typeof(RectTransform))` defaults to `anchorMin=anchorMax=(0,1)` (top-left), `pivot=(0.5,0.5)`, `sizeDelta=(100,100)`. So a 100px-tall button child placed in a 65px-tall HLG parent sticks out ~17px above the parent's top edge. Worse: because the child's screen rect extends above the parent, it STEALS raycasts from any sibling that draws BEFORE it in the parent chain (siblings drawn later are on top — and the bottom-nav strip typically has the highest sibling index). Symptom: tapping a big visible button does nothing — the click is captured by an invisible nav-tab rect that floats above its own strip. **Fix:** in the layout-group setup, ALWAYS explicitly set BOTH `childControlWidth=true` AND `childControlHeight=true` so HLG/VLG resizes children to fit; ALSO reset each child's `RectTransform.anchorMin/Max` to a stretch anchor (e.g. `(0,0)`/`(0,1)` for HLG children, `(0,0)`/`(1,0)` for VLG children) as belt-and-suspenders. Don't trust the default. Originating incident: `Assets/Demos/RPG/ChaosForgeDemo/Editor/ChaosForgeSceneSetup.cs` — `BuildForgeHome` BottomNav had `childControlSize` unset → 5 nav buttons stuck 17px above BottomNav's top → invisible nav-tab rects ate raycasts targeting the big orange FORGE button below them. Two-symptom bug (visible overlap + dead button) with one root cause (HLG didn't resize children).
18. **HLG height-fix alone doesn't satisfy "covers the buttons above" complaints — hide the whole panel when its content is non-functional** (2026-05-26, ChaosForge bottom-nav follow-up): After applying gotcha #17 (HLG `childControlHeight=true`) the BottomNav rects no longer geometrically overlap the big CTA buttons above them — but a user still reported "the bottom bar covers FORGE/UPGRADE even more." Reason: with HLG `childForceExpandWidth=true`, the nav tabs grew from 100 px to ~200 px wide (full panel width / 5 tabs), making them visually chunky enough to dominate the bottom strip even though they sit fully INSIDE BottomNav. When every tab in a nav strip is LOCKED (no functionality the player can use), the correct UX call is to **hide the panel entirely** rather than continue tuning layout. Set `panelGo.SetActive(false)` after construction — controller refs stay valid (Bind already captured them), no taps dispatch from a deactivated GO. Re-enable in code once any tab unlocks for the player. Fix: `Assets/Demos/RPG/ChaosForgeDemo/Editor/ChaosForgeSceneSetup.cs:1276` — `navGo.SetActive(false)` after `controller.Bind(..., bottomNav)`. **Rule of thumb:** when the only thing your panel ships is locked-state visuals, the visual itself is the problem, not the layout. Hide it. Originating commit: DOTS-AI `34de75fa`.

## UI Behavior Validation (Play-Mode Probes)

State-only validation (`active=true`, `scale != (0,0,0)`) misses the most common uGUI failure mode: **panel opens, panel is empty/dysfunctional**. Use these probes during Play-mode validation of any Canvas UI work.

### Required behavior probes per panel

| Probe | Tool | Pass condition |
|---|---|---|
| **List population** — count spawned children of the slot container | `mcpforunity://scene/gameobject/{containerId}/components` | `childCount > 0` (where N matches the expected entry count, e.g. roster size) |
| **Button wiring** — inspector-wired onClick listeners | Same resource URI, inspect `m_OnClick.m_PersistentCalls.m_Calls.Array.size` | `> 0` for primary buttons; if 0, verify a runtime `AddListener` exists in code (search `\.onClick\.AddListener`) |
| **Dynamic labels** — TMP_Text populated after interaction | Same resource URI, inspect TextMeshProUGUI `m_text` field | non-empty after the user action that should fill it |
| **Interactable state** — confirm/submit buttons gated on selection | Inspect `Button.interactable` | starts `false` AS DESIGNED, then flips to `true` after a probe-driven selection (forge a selection via the screen's public API in a test harness, OR validate the gating logic by reading the source) |

### Hidden failure mode signature

A panel can show on screen with all of:
- ✅ `active=true`, `activeInHierarchy=true`
- ✅ Scale resolved by `CanvasScaler` at runtime (e.g., `1.125, 1.125, 1.125`)
- ✅ The panel's `MonoBehaviour.Awake()` ran and configured its own buttons
- ❌ The `SetEntries(...)` method (or equivalent populator) **never called by any external caller**
- ❌ Confirm/submit button stays `interactable=false` forever because nothing was selected

Visual result: blank panel with greyed-out button. State-only checks pass; user sees a broken UI.

### Diagnostic recipe — "I see the panel but X doesn't work"

1. Find the panel GameObject via `find_gameobjects`. Confirm `active=true`.
2. Inspect its component list — find the screen's MonoBehaviour. Note the `[SerializeField]` references (`slotContainer`, `confirmButton`, etc.).
3. For the slot container, query `RectTransform.childCount` via the components resource. If `== 0`, the list was never populated → check #4.
4. **Grep the codebase for the populate method's external call sites** (e.g., `grep -rn "\.SetEntries(" Assets/`). Zero hits outside the screen file = nobody calls it = feature is unfinished. Read the screen's source for `// Phase X`, `// for now`, `// TODO` comments — confirms deferred work.
5. For the confirm/submit button: read `Button.interactable` + check the screen source for the gating condition (typically "a slot must be selected first").
6. Report findings as: "Panel opens correctly. List population gap: no external caller of `SetEntries`. Button correctly gated on selection but unreachable because step 3 failed."

### When NOT to add a probe

Skip Check #9-style probes for purely decorative UI (background images, static title bars). The probes target *interactive* and *data-driven* UI: lists, grids, drag-and-drop slots, anything that spawns runtime children, dynamic labels, buttons that gate game-flow transitions.

## Gotcha — HUD zoning + runtime sibling order (2026-05-15 BackpackCrawler)

Two recurring HUD bugs the `unity-hud-layout` skill exists to prevent. Both surfaced in the
BackpackCrawler portrait HUD and were fixed via a zone-based refactor (see `unity-hud-layout`).

### A. Never stack widgets via absolute pixel offsets in the same RectTransform

**Anti-pattern:**
```csharp
playerHPBar.anchoredPosition = new Vector2(UIPadding, UIPadding);
shieldText.anchoredPosition  = new Vector2(UIPadding, UIPadding + HPBarHeight + 4f);
levelText.anchoredPosition   = new Vector2(UIPadding, UIPadding + HPBarHeight + 26f);
xpBar.anchoredPosition       = new Vector2(UIPadding + 82f, UIPadding + HPBarHeight + 32f);
```
Each new widget invents a new offset; first text-size or font change cascades into visible overlap.

**Fix:** when >3 widgets share a RectTransform, split the parent into named **zones** with
LayoutGroups. Each new widget picks a zone, not a pixel offset. Full pattern:
[`unity-hud-layout`](../t1k-unity-ui-hud-layout/SKILL.md). Reference impl pattern lives in
consumer projects (search for `*HudZones*.cs`).

### B. Runtime-built UI MUST live in a dedicated FloatingLayer + SetAsLastSibling

**Anti-pattern:**
```csharp
tooltipPanel.transform.SetParent(canvas.transform, false); // sibling-order = creation order
```
Last-built widget wins z by accident. Add a popup later → it covers tooltips that should be on top.

**Fix:** create a `FloatingLayer` GameObject as a child of the canvas root. Parent every
runtime-built widget (tooltips, drag ghosts, popups) to it AND call
`transform.SetAsLastSibling()` on Show. Mutex behavior between widgets is enforced by
`SetActive`, NOT by sibling order. Full-screen flashes and centered transient text use a
separate `OverlayLayer` (above FloatingLayer) with `SetSiblingIndex(0)` for always-bottom
panels. Full pattern: [`unity-hud-layout`](../t1k-unity-ui-hud-layout/SKILL.md).

## Reference Files

| File | Content |
|------|---------|
| `references/components-quick-ref.md` | Image, RawImage, TMP, Button, Toggle, Slider, Dropdown, InputField |
| `references/layouts-and-drag-drop.md` | Layout groups, ScrollRect, IBeginDragHandler/IDragHandler/IDropHandler |
| `references/dots-bridge-and-performance.md` | ECS→UI data flow, Canvas batching, pooling, split canvases |
