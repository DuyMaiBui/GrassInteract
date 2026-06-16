---
name: t1k:unity:ui:hud-layout
description: Unity HUD layout discipline — zone-based design, LayoutGroup recipes per zone, modal vs HUD canvas separation, FloatingLayer/OverlayLayer patterns, anti-pattern catalog. Use when designing or refactoring runtime game HUDs (>3 widgets in a single RectTransform).
effort: medium
keywords: [HUD layout, screen overlap, widget anchor, RectTransform stack, zone, LayoutGroup, FloatingLayer, OverlayLayer, HUD zoning, widget overlap]
version: 2.6.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: ui
protected: false
---

# Unity HUD Layout Discipline

A zone-based pattern for runtime game HUDs. Replaces absolute-pixel-offset stacking with declarative zones each owning a LayoutGroup. Built from the BackpackCrawler portrait HUD retrospective (2026-05-15) + Unity 6 uGUI research.

## When this skill triggers

Activate when the user asks to:
- Build a HUD with >3 widgets sharing one RectTransform
- Fix HUD widgets visibly overlapping or covering each other
- Refactor a HUD that uses `UIPadding + HPBarHeight + N` style absolute offsets
- Create a portrait or landscape mobile HUD with zone separation
- Add tooltips / drag ghosts / popups that should always render on top
- Add full-screen flash overlays (damage flash, level-up text)

Skip for: pure modal panels (use `unity-ugui` modal pattern); decorative-only layouts; UI Toolkit (use `unity-ui-toolkit`).

## Core decision tree

```
HUD contains >3 widgets in same RectTransform?
├── YES → split into named zones (this skill)
│   └── Each zone gets LayoutGroup OR explicit anchors
└── NO → anchors only, no LayoutGroup needed
```

```
Widget is built at runtime (tooltip / drag ghost / popup)?
├── YES → parent into FloatingLayer + SetAsLastSibling on Show
└── NO → parent into the appropriate zone
```

```
Widget is a full-screen flash or centered transient text?
├── YES → parent into OverlayLayer
│   ├── Always-bottom (e.g. DamageFlash) → SetSiblingIndex(0)
│   └── Always-top (e.g. LevelUpFlash) → SetAsLastSibling on Show
└── NO → goes in a zone or FloatingLayer
```

## Zone-based HUD template (portrait reference: 1080×1920)

```
Canvas (ScreenSpaceOverlay, sortingOrder=10)
├── BottomPanel (35% bottom, full width)
│   ├── PhaseRow         anchor=top, full width, h=PhaseHeight    [HorizontalLayoutGroup]
│   ├── StatsLeftZone    anchor=BL, w=StatsWidth, h=zoneH         [VerticalLayoutGroup, UpperLeft]
│   ├── GridZone         anchor=center, fixed w×h                 [no LayoutGroup; child has GridLayoutGroup]
│   ├── ActionRightZone  anchor=BR, w=StatsWidth, h=zoneH         [VerticalLayoutGroup, UpperRight]
│   └── ActionCenterZone anchor=B, w=ActionWidth, auto-h          [VerticalLayoutGroup, LowerCenter]
├── FloatingLayer       full-canvas stretch, no Image, no LayoutGroup
└── OverlayLayer        full-canvas stretch, no Image, no LayoutGroup (top-most sibling)
```

Reference impl: `Assets/Demos/RPG/BackpackCrawler/Editor/BackpackCrawlerSceneSetup.Canvas.HudZones.cs`.

## LayoutGroup recipes

| Zone purpose | LayoutGroup | childAlignment | childControl{Width,Height} | childForceExpand |
|---|---|---|---|---|
| Stat stack (HP, level, XP) | VerticalLayoutGroup | UpperLeft | true, true | false, false |
| Phase row (single label) | HorizontalLayoutGroup | MiddleCenter | true, true | false, false |
| Action column (mutex buttons, each needs `LayoutElement`) | VerticalLayoutGroup | LowerCenter | true, true | false, false |
| Inventory grid | GridLayoutGroup | n/a | n/a (uses cellSize) | n/a |
| Float-above zone | None | n/a | n/a | n/a |

## Modal vs HUD canvas separation (sortingOrder constants)

```csharp
public const int CanvasOrderHud           = 10;   // Main HUD canvas
public const int CanvasOrderFloatingLayer = 20;   // Tooltips/ghosts (sibling of HUD root, above HUD widgets)
public const int CanvasOrderOverlayLayer  = 30;   // Flash panels (above floating)
public const int CanvasOrderModal         = 50;   // Modal panels with own Canvas + CanvasGroup
public const int CanvasOrderModalAbove    = 60;   // Modal-above-modal (e.g. confirm dialog atop shop)
```

Modals MUST get their own Canvas + CanvasGroup + GraphicRaycaster + a backdrop dim Image (full-screen, alpha 0.7). Without the backdrop the HUD beneath bleeds through visibly.

## FloatingLayer + OverlayLayer pattern

**FloatingLayer** is for runtime-built widgets that need explicit z-order management.

```csharp
// In SceneSetup, after canvas + BottomPanel exist:
var floatingLayer = new GameObject("FloatingLayer", typeof(RectTransform));
floatingLayer.transform.SetParent(canvas.transform, false);
var fl = floatingLayer.GetComponent<RectTransform>();
fl.anchorMin = Vector2.zero; fl.anchorMax = Vector2.one;
fl.offsetMin = Vector2.zero; fl.offsetMax = Vector2.zero;
floatingLayer.transform.SetAsLastSibling();
// No Image, no LayoutGroup → never blocks raycasts when empty.

// Runtime tooltip creation:
tooltipPanel = new GameObject("TooltipPanel", typeof(Image));
tooltipPanel.transform.SetParent(floatingLayer.transform, false);

// Show:
tooltipPanel.transform.SetAsLastSibling();
tooltipPanel.SetActive(true);
```

**OverlayLayer** is identical structure but sits above FloatingLayer (further `SetAsLastSibling`). Use sibling-index discipline:
- `SetSiblingIndex(0)` — always-bottom (e.g., DamageFlash that should stay below LevelUpText)
- `SetAsLastSibling()` — most-recent-shown (e.g., LevelUpText that should always read)

## Anti-pattern catalog

1. **Stacking widgets via absolute offsets in same RectTransform**
   - `widget.anchoredPosition = new Vector2(UIPadding, UIPadding + HPBarHeight + 4f);` repeated for every new widget
   - Fix: split into a zone with a VerticalLayoutGroup; widgets become siblings in layout order

2. **Runtime widgets parented to canvas root with creation-order z**
   - `tooltip.transform.SetParent(canvas.transform, false);` — last-built wins z
   - Fix: parent into FloatingLayer + `SetAsLastSibling()` on Show

3. **Modal panel with no backdrop dim**
   - Modal opens, HUD beneath remains visible → chaotic visual
   - Fix: add full-screen Image child at sortingOrder=ModalOrder, color=BackdropDim (RGBA 0,0,0,0.7), raycastTarget=true

4. **ContentSizeFitter inside ContentSizeFitter** (Unity 6 known bug)
   - Causes infinite recursion warnings
   - Fix: only one ContentSizeFitter per chain; use LayoutElement.preferredHeight on inner

5. **Per-frame layout rebuild from text changes**
   - Fast-changing text in a LayoutGroup → layout recomputes every frame
   - Fix: text in a fixed-size container (no ContentSizeFitter); only HP/XP fill amount changes

6. **`raycastTarget = true` on decorative graphics**
   - Every Image/Text raycasts on every pointer event → CPU cost
   - Fix: `raycastTarget = false` on decorative widgets; keep true only on Buttons/interactive

7. **Full-screen flash overlays without explicit sibling-order management**
   - DamageFlash and LevelUpFlash z-fight unpredictably
   - Fix: both in OverlayLayer; DamageFlash uses `SetSiblingIndex(0)`, LevelUpFlash uses `SetAsLastSibling()` on Show

8. **Empty zone container with content living at sibling level** (zone-refactor regression)
   - `GridZone` exists as empty placeholder while `GridPanel` (the actual grid with cells) sits at the SAME hierarchy level. The zone label is dead architecture; future zone-based layout logic won't apply.
   - Fix: reparent content INTO its zone (`transform.SetParent(gridZone, worldPositionStays=false)`). Then the zone owns the rect and any layout responsiveness applies.
   - Symptom: zone hierarchy looks correct in inspector but `childCount == 0`. Verify EVERY zone has its content children; an empty zone is almost always wrong.
   - Discovered: BackpackCrawler 2026-05-15 (commit `a2b20439`) — zone scaffold was built but GridPanel/SynergyInfoBtn were never reparented in.

9. **Peripheral widgets anchored to canvas-root with arbitrary anchor%** (e.g. anchor.y=0.4)
   - LeftHand/RightHand hand-display sprites were anchored to canvas at `anchorMin/Max=(0, 0.4)`. At 40% of canvas height in a portrait HUD, they land DIRECTLY on top of StatsLeftZone content (which fills top-down from the BottomPanel's top edge). Visible result: hand sprite covers stat text.
   - Fix: reparent into the OWNING zone (e.g. inside `StatsLeftZone` as last child with `LayoutElement.ignoreLayout=true` if absolute positioning is needed), OR anchor to a position that's geometrically clear of all zones (typically `anchor.y < zone_bottom_fraction` or `anchor.y > zone_top_fraction`), OR — most commonly — use the "flank-a-zone" recipe below.
   - Rule of thumb: if a widget needs to "follow" zone content, parent it INTO the zone — don't anchor it at canvas-root with a guessed percentage.
   - Discovered: BackpackCrawler 2026-05-15 — handoff noted "HandDisplay anchor lifted" as a fix; the lift overshot and landed in stat territory.

   #### Recipe: Flank a centered zone (left/right widgets bracketing a grid)

   Use this when you have a CENTERED zone (like a grid) and want two satellite widgets (like hand-display sprites) sitting right beside it on the left and right. The widgets MUST move with the zone if the zone's screen position changes.

   ```
   Parent of all three: the same RectTransform (e.g. BottomPanel)

   CenteredZone (the grid)
   ├── anchor:        (0.5, 0.5)  →  parent-center
   ├── pivot:         (0.5, 0.5)
   ├── sizeDelta:     (Zw, Zh)
   └── anchoredPos:   (0, Zy)     →  Zy is the vertical offset within the parent

   LeftSatellite (e.g. LeftHand)
   ├── anchor:        (0.5, 0.5)  →  parent-center (same as zone)
   ├── pivot:         (1, 0.5)    →  RIGHT edge of widget aligns to anchor
   ├── sizeDelta:     (Sw, Sh)
   └── anchoredPos:   (-(Zw/2 + gap), Zy)
                       │
                       └─ widget's right edge sits `gap` px left of zone's left edge

   RightSatellite (e.g. RightHand)
   ├── anchor:        (0.5, 0.5)
   ├── pivot:         (0, 0.5)    →  LEFT edge of widget aligns to anchor
   ├── sizeDelta:     (Sw, Sh)
   └── anchoredPos:   (Zw/2 + gap, Zy)
                       │
                       └─ widget's left edge sits `gap` px right of zone's right edge
   ```

   **Why it works:** all three share the same anchor (parent-center), so they translate together if the parent moves. Pivot-at-edge means anchoredPos.x equals the widget's inner edge in the zone's coordinate system — clean math, no magic numbers.

   **Concrete example from BackpackCrawler:** GridZone has `sizeDelta=(512, 457), anchoredPos=(0, -10)`. With `gap=4`:
   - `LeftHand`: pivot=(1, 0.5), anchoredPos=(-260, -10), sizeDelta=(60, 80) → right edge at x=-260, sits 4px left of grid's left edge at x=-256
   - `RightHand`: pivot=(0, 0.5), anchoredPos=(260, -10), sizeDelta=(60, 80) → left edge at x=260, sits 4px right of grid's right edge at x=256
   - Both vertically centered on the grid (y=-10 matches grid's anchoredPos.y)

   **Pitfall:** if the zone uses a LayoutGroup or its `sizeDelta` is driven by a ContentSizeFitter, the half-width `Zw/2` you hardcode here goes stale. Either pin the zone size or recompute the satellites' anchoredPos.x at runtime from `zone.rect.width / 2 + gap`.

10. **Zone container with `sizeDelta.y=0` and a VerticalLayoutGroup that has childForceExpandHeight=false**
    - `ActionCenterZone` had `sizeDelta=(320, 0)` and 3 children (FightButton, RestartButton, RewardTray) controlled by VerticalLayoutGroup with `childControlHeight=true, childForceExpandHeight=false, childAlignment=LowerCenter`. Combined preferred height of children was ~280px but zone height was 0. With `LowerCenter` alignment, content overflowed BELOW the zone bottom — i.e., off the screen.
    - Fix: either set `sizeDelta.y` to the expected content height (e.g. 280), OR set `childForceExpandHeight=true` and anchor the zone to stretch vertically, OR add a `ContentSizeFitter (preferredSize)` on the zone.
    - Diagnostic: in editor inspector, if a zone's RectTransform shows `Height: 0` but the zone has active layout-controlled children, it WILL clip on the layout group's alignment direction.

11. **Modal Canvas with `CanvasScaler.matchWidthOrHeight=1.0` while main HUD uses 0.5**
    - Result: on aspect ratios that diverge from the reference resolution (e.g. iPhone 15 Pro Max 9:19.5 vs reference 9:16), the modal scales by HEIGHT only and the content WIDTH overflows the screen. Hero card row gets clipped at left and right edges.
    - Fix: every Canvas in the project should use the SAME `matchWidthOrHeight` (typically `0.5` for balanced portrait). If a modal genuinely needs a different match value, document why inline.
    - Pre-commit check: every Canvas in the scene should have identical `referenceResolution` and `matchWidthOrHeight` unless explicitly justified.

12. **LayoutGroup with `childControlWidth/Height=true` and children without a LayoutElement**
    - Symptom: **button renders only its text child but the colored background is invisible**. Inspector shows the button at `rect: 0×0` with `drivenByObject: <parent VerticalLayoutGroup>`. The TMP text child still draws because TMP computes its own bounds from font metrics.
    - Root cause: `VerticalLayoutGroup` / `HorizontalLayoutGroup` with `childControlHeight=true, childForceExpandHeight=false` SETS each child's size to the child's preferred-size. An Image has no preferred-size signal, so the layout group sizes it to 0×0. The button is geometrically present and interactable but invisible.
    - Fix (preferred): add a `LayoutElement` component to each managed child with explicit `preferredWidth` and `preferredHeight` (and ideally `minWidth`/`minHeight` for safe shrinking). The layout group then has a concrete size signal to honor.
    - Alternative fixes (situational):
      - Set `childForceExpand{Width,Height}=true` on the parent → children stretch to fill remaining space (correct for "one big button per row" layouts, wrong when you have multiple stacked rows that should each take preferred size)
      - Set `childControl{Width,Height}=false` on the parent + give each child an explicit `sizeDelta` (skip the layout system entirely for size; layout only handles position)
    - Diagnostic: in the inspector, RectTransform shows `drivenByObject: <LayoutGroup>` AND `width/height = 0` AND the parent's `childControl*=true` → this anti-pattern.
    - Discovered: BackpackCrawler 2026-05-15 — fixed by adding LayoutElement (preferredWidth=280, preferredHeight=80) to FightButton and RestartButton after the zone refactor's VerticalLayoutGroup zeroed them out.

13. **HUD currency view registered with the WRONG match-key vs. the ECS wallet's `CurrencyId`** (silently frozen at "0")
    - Symptom: a top-bar currency widget renders its icon and label but the VALUE stays frozen (e.g. always "0") even though the ECS wallet buffer is updating correctly. No error, no warning — purely a display freeze.
    - Root cause: the library `HUDController` polls the wallet (`DOTSEconomy.WalletEntry`, post-W7 wallet-v2) and notifies views keyed by `(int)WalletEntry.CurrencyId`. If the SceneSetup registered each `HUDCurrencyView` with the RAW row index (0/1/2) while the wallet stores game-specific currencies at `CurrencyId.Custom0..Custom7` (= `100 + rawIndex`), then `HUDController.NotifyViewsForCurrency()` compares `0/1/2` against `100/101/102`, never matches, and never pushes the new value to the view.
    - **Rule: the HUD view's match-key MUST equal the ECS wallet's `CurrencyId` int — NOT the raw row/config index.** Register via the currency-id mapping helper (e.g. `ChaosForgeCurrencyConstants.ToCurrencyIdValue(rawKey)`), not the raw key.
    - Subtlety: the ICON lookup may legitimately stay on the raw index (e.g. `CurrencyTypeToSpriteName` switches on row index for the sprite path) while the VALUE match-key uses the `CurrencyId` int. Two different keys for two different purposes on the same widget — don't unify them by accident.
    - Diagnostic: wallet buffer shows correct amounts in the entity inspector but the TMP text never changes → check that `view`'s registered key matches `(int)WalletEntry.CurrencyId`, not the authoring/config index.
    - Discovered: ChaosForge 2026-05-30 (commit `0976633d`) — top bar froze at "0" after realm select because views were registered with raw 0/1/2 instead of `Custom0..2` = 100/101/102. Applies to ANY HUD that bridges a custom-currency ECS wallet to uGUI views.

## Reference Files

| File | Content |
|---|---|
| `references/zone-template.md` | Copy-pasteable C# zone scaffold |
| `references/anti-patterns.md` | Detailed before/after for each anti-pattern |

## Related skills

- `unity-ugui` — Canvas, RectTransform, Image, Button, drag-and-drop API (foundation)
- `unity-mobile-ui` — Touch ergonomics, safe area, HUD overlap pre-commit checklist
- `unity-ui-toolkit` — UXML/USS alternative; this skill is uGUI-specific

## Sources

- Unity 6 uGUI research report: `plans/reports/researcher-260515-1338-ugui-hud-best-practices.md`
- BackpackCrawler retrospective: `plans/reports/brainstorm-260515-1337-backpackcrawler-ui-visual-overlap.md`
- BackpackCrawler post-refactor audit (2026-05-15): anti-patterns 8-11 added from a second-pass MCP-driven RectTransform audit that revealed zone-refactor regression bugs
- Unity Learn — UI optimization tips (multi-canvas batching)
- Unity Manual — uGUI Auto Layout (LayoutGroup performance characteristics)
