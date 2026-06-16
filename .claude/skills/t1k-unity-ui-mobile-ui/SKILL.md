---
name: t1k:unity:ui:mobile-ui
description: Unity mobile UI — touch input, gestures (pinch/swipe/drag), responsive layouts, safe areas, uGUI optimization, UI Toolkit, accessibility for mobile game UI.
effort: medium
keywords: [mobile UI, responsive, unity, canvas]
version: 2.6.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: ui
protected: false
---

# Unity Mobile UI

## When This Skill Triggers

- Building mobile game UI (HUD, menus, popups)
- Handling touch input, gestures, multi-touch
- Implementing responsive layouts for different devices
- Optimizing UI performance (Canvas batching, overdraw)
- Setting up safe areas for notch/cutout devices
- Choosing between uGUI and UI Toolkit

## Quick Reference

| Task | Reference |
|------|-----------|
| Touch input, gestures, New Input System | [Touch & Input](references/touch-input.md) |
| Responsive layout, safe areas, anchoring | [Responsive Layout](references/responsive-layout.md) |
| Canvas optimization, batching, overdraw | [UI Optimization](references/ui-optimization.md) |

## Critical Rules

1. **New Input System for touch** — `EnhancedTouch` API for gestures; never use legacy `Input.GetTouch`
2. **Safe Area always** — Apply `Screen.safeArea` to root canvas panel
3. **Split canvases** — Separate static UI (HUD) from dynamic UI (damage numbers)
4. **Disable Raycast Target** — On all non-interactive elements (images, text)
5. **Avoid Layout Groups in runtime** — Pre-calculate positions; Layout Groups trigger constant rebuilds
6. **TextMeshPro always** — Never use Unity's legacy Text component
7. **Atlas sprites** — All UI sprites in sprite atlases to reduce draw calls
8. **Hide via CanvasGroup** — Use `alpha = 0` AND `blocksRaycasts = false`; never `SetActive(false)` for frequent toggles

## Key Patterns

### Safe Area Handler
```csharp
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaHandler : MonoBehaviour
{
    void Awake()
    {
        var safeArea = Screen.safeArea;
        var rt = GetComponent<RectTransform>();
        var anchorMin = safeArea.position / new Vector2(Screen.width, Screen.height);
        var anchorMax = (safeArea.position + safeArea.size) / new Vector2(Screen.width, Screen.height);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
    }
}
```

### Gesture Detection Service
```csharp
public sealed class GestureService : IInitializable, IDisposable, ITickable
{
    // Detect: Tap, DoubleTap, Swipe (4-dir), Pinch, LongPress
    // Fire signals: TapSignal, SwipeSignal, PinchSignal
    // See references/touch-input.md for full implementation
}
```

## Related Skills

- `unity-game-patterns` — UI state machines, input handling
- `unity-animation-vfx` — UI animations, tween transitions
- `unity-ugui` — uGUI Canvas, RectTransform, Image, Button API
- `unity-ui-toolkit` — UI Toolkit (UXML/USS) alternative
- `unity-hud-layout` — HUD zoning, FloatingLayer/OverlayLayer patterns, anti-pattern catalog
- `theone-studio-patterns` — VContainer for UI services

## HUD Overlap Pre-Commit Checklist (added 2026-05-15 from BackpackCrawler retrospective)

For every child RectTransform of the HUD canvas, before committing:

- [ ] Mark its anchor zone (TL / T / TR / L / C / R / BL / B / BR)
- [ ] Assert no two widgets share the same zone unless their shared parent has a LayoutGroup
- [ ] If a widget's anchor crosses zones (e.g., diagonal stretch), justify in a comment
- [ ] Runtime-built UI (tooltips, ghosts, popups) lives in a dedicated FloatingLayer — see `unity-ugui` Gotcha B and `unity-hud-layout`
- [ ] Full-screen overlays (flash panels, centered transient text) live in a dedicated OverlayLayer with documented sibling-index rules
- [ ] Stat / level / XP / shield rows use a VerticalLayoutGroup zone — never absolute offsets like `UIPadding + HPBarHeight + N`
- [ ] Mutex pairs (FightButton ⇄ RestartButton) are siblings under the same LayoutGroup; show/hide via `SetActive` (LayoutGroup auto-collapses inactive children)

Applies to portrait mobile HUDs with >3 widgets; smaller HUDs may skip.

## Gotchas

1. **Portrait CanvasScaler match factor**: Use `matchWidthOrHeight = 0.5f` for portrait games, NOT 1.0. Match=1.0 scales by height only, causing text to shrink on wide phones (18:9, 20:9). Match=0.5 balances width/height scaling.
2. **TMP font size is NOT the same as Android sp/iOS pt**: Unity TMP fontSize is in "reference pixels" scaled by CanvasScaler. At 1080×1920 reference with match=0.5, a fontSize of 24 ≈ 24sp on a reference device. On higher-DPI devices, CanvasScaler scales it up proportionally.
3. **Safe area only handles notch/status bar**: Safe area insets do NOT account for thumb reach zones. Bottom 40% of portrait screen is thumb-reachable; top 20% requires deliberate reach. Place primary actions in bottom half.
4. **HUD widget overlap from offset-stacking**: see HUD Overlap Pre-Commit Checklist above. Reference impl pattern lives in consumer projects (search for `*HudZones*.cs`); the canonical example is the BackpackCrawler portrait HUD in the DOTS-AI workspace.
