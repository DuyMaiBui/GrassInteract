---
name: t1k:unity:editor:wiki
description: "Wiki page management — create, update, audit game design wiki pages via game-designer."
effort: low
argument-hint: "[demo-name] [--create|--update|--audit]"
keywords: [wiki, documentation, knowledge base]
version: 2.0.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---

# GameKit Wiki — Wiki Page Management

Manage game design wiki pages in `docs/wiki/`.

## Operations
| Operation | Description |
|---|---|
| `--create` | Create new wiki page for a demo |
| `--update` (default) | Update wiki after code changes |
| `--audit` | Check all wiki pages against current code |

## Agent: `game-designer`

## Wiki Structure
```
docs/wiki/
├── Demo-BattleDemo.md
├── Demo-BattleDemo2D.md
├── Demo-BattleDemoIso.md
├── Demo-BattleDemoSideView.md
├── Demo-BackpackCrawler.md
└── Demo-InventoryDemo.md
```

## References
- `references/wiki-structure.md`

## Gotchas

- **`Demos.md` status column gap: "Implemented" ≠ "playable scene committed"** — "Implemented" should mean code was written; "Playable" means a committed scene + prefab set exists and the demo runs via `Tools/{Demo}/Setup Scene`. Conflating them led 6/23 demos to show "Implemented" while having empty `Scenes/` and `Prefabs/` folders (260520-R4). Add a separate "Playable" column or redefine "Implemented" to require scene assets. Source: review-260520-round4-design-placeholder.md §D6
