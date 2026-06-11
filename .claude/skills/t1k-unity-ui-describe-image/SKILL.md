---
name: t1k:unity:ui:describe-image
description: Turn a UI screenshot (popup, screen, HUD, panel) into a Unity-ready breakdown — an isolated GameObject hierarchy rooted at FullScreenLayout, plus a detail table of anchor/pivot/anchored-position/size for every element including parent groups. Use BEFORE authoring a prefab from a mockup, to plan the RectTransform layout. Hands off to t1k:unity:ui:create-prefab / t1k:unity:tof:ui-create-prefab.
effort: low
keywords: [describe ui, ui screenshot, mockup, layout breakdown, hierarchy, detail table, anchor pivot, rect transform, FullScreenLayout, popup layout, screen layout, isolate elements, ui plan, image to ui, anchored position, pivot, vertical layout group]
argument-hint: "[image-path] (or paste/attach the screenshot)"
version: 2.3.1
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: ui
protected: false
---

# Describe UI Image → Hierarchy + Detail Table

Convert a UI mockup/screenshot into a structured, Unity-authorable spec. Produces **two artifacts**: an isolated GameObject **hierarchy** and a RectTransform **detail table**. This is a planning step — no prefab is created here. The output is the input to `t1k:unity:ui:create-prefab` (generic) or `t1k:unity:tof:ui-create-prefab` (TheOneFeature-scoped).

## When This Skill Triggers

User provides a UI screenshot/mockup and asks to "describe the popup/screen", break it into a hierarchy, or plan its layout (anchors, pivots, positions, sizes) before building the prefab.

## Step 0 — Analyze the Image

Route image analysis through **human-mcp** (`mcp__human-mcp__eyes_analyze`) when registered; otherwise use native vision (graceful fallback). See `rules/image-analysis-routing.md`. Read carefully: every distinct icon, label, button, background, badge, and the overlap relationships between them.

## Output 1 — Hierarchy

A tree of every GameObject. **The root is always `FullScreenLayout`.** Annotate each node with a one-line role.

### Isolation rule (the core discipline)

Isolate aggressively — one responsibility per GameObject:

- **A background is its own element**, separate from the content it sits behind. A reward pill = `RewardBackground` (the gray rounded sprite) + the text/icons as siblings inside the group. A button = `XxxButtonBackground` + `XxxButtonLabel`. An arrow button = `ArrowButtonBackground` + `ArrowIcon`.
- **Every labelled row is its own group** (e.g. `DifficultyGroup`, `RewardGroup`, `KeyGroup`, `ButtonGroup`) so it can be driven by a layout group independently.
- **Icon + its value are separate elements** (`HammerIcon` + `HammerValueText`), never one combined sprite-text.
- **Title overlaid on artwork** lives inside the artwork group (it is positioned relative to the banner).

### Overlap / parenting rule

An element that **visually straddles the edge of a panel** (e.g. a close button half-outside the card) must be a **sibling of the panel, not a child** — a child would be clipped by the panel's rect/mask. State this in a "grouping rationale" note.

### After the tree

Add a short **Grouping rationale** list explaining the non-obvious parenting choices (siblings vs children, why each group exists, which backgrounds were isolated).

## Output 2 — Detail Table

One row per element **including every parent group** (not just leaves). Columns:

| Column | Meaning |
|---|---|
| Element | Name matching the hierarchy |
| Anchor (min→max) | Stretch `(0,0→1,1)`, or a point like `Top-center (0.5,1)`. Stretch the things that should track parent width/height. |
| Pivot | `(x,y)` — use compass shorthand (`T`,`B`,`L`,`R`,`C`, e.g. `ML (0,0.5)`) |
| Anchored Pos (x, y) | Position **relative to its own parent** |
| Size (W × H) | width × height (omit for stretched dimensions) |
| Notes | sprite color, text style, raycast, behavior |

**Reference resolution:** default to **1080 × 1920** (portrait mobile) unless the user states otherwise; say which you used. All numbers are approximate — label them as estimates.

### After the table

Add **Anchoring strategy notes**: which elements stretch to track parent, which are anchored to top/bottom so stacked groups never overlap, and which groups are candidates for a `VerticalLayoutGroup` / `HorizontalLayoutGroup`.

## Worked shape (abbreviated)

```
FullScreenLayout
└── PopupPanel                 (white rounded card, center)
    ├── BannerGroup            (rounded-top header)
    │   ├── BannerImage
    │   └── TitleText
    └── ContentGroup           (vertical stack)
        ├── RewardGroup
        │   ├── RewardBackground   ← isolated bg
        │   ├── RewardLabel
        │   ├── HammerIcon
        │   └── HammerValueText
        └── ButtonGroup            (HorizontalLayoutGroup)
            ├── EnterButton/EnterButtonBackground + EnterButtonLabel
└── CloseButton                (SIBLING of PopupPanel — overlaps its bottom edge)
    ├── CloseButtonBackground
    └── CloseIcon
```

## Handoff

When the user is ready to build, point them at:
- `t1k:unity:ui:create-prefab` — generic UI prefab (variant of `UIBaseScreen`/`UIBasePopup`).
- `t1k:unity:tof:ui-create-prefab` — if the target lives under `Packages/TheOneFeature*` or `Assets/Prefabs/UIs/`.

## Gotchas

- Don't merge a background into its content — the isolation rule is what makes the spec authorable as separate sprites/text.
- Don't parent an overlapping element to the panel it spills out of — it gets clipped.
- Always include parent groups as table rows; their RectTransform sizing drives the children.
- State the reference resolution and that the numbers are estimates, so the builder treats them as a starting point, not gospel.
