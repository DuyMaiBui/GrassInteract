---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Level Spatial Layouts — Mermaid Patterns

**Template:** `templates/level-spatial.mmd` (Mermaid `flowchart LR`)

## When to use this template
- Documenting zones in a level and how they connect
- Showing gate logic (which key opens which door)
- Distinguishing two-way passages from one-way drops
- Pre-greybox pass: sketch the topology before committing to 3D layout

## Core Mermaid syntax you need

### Direction
`flowchart LR` — spatial layouts read best left-to-right, matching most minimaps.

### Node shapes to distinguish zone roles
| Shape | Mermaid syntax | Use for |
|---|---|---|
| stadium | `Entry([Entry Plaza])` | Start / spawn zones |
| rectangle | `Market[Market Square]` | Standard zones |
| rhombus | `Gate{Locked Gate}` | Gates as nodes (rare) |

Prefer encoding gates as **edge labels** not nodes — see below.

### Edges (the important part for spatial diagrams)
| Syntax | Meaning |
|---|---|
| `A <--> B` | Two-way passage (walk in either direction) |
| `A --> B` | One-way drop (no return from B to A) |
| `A -- "Key" --> B` | Gated edge — requires item to traverse |
| `A -.hidden.- B` | Hidden / optional passage |
| `A -.zipline.-> B` | Labeled special traversal |

### Styling (classDef) for zones
Color by role:
```
classDef entry fill:#4caf50         %% start zones
classDef locked fill:#ffb300        %% gated zones
classDef danger fill:#e53935        %% hazard zones
classDef goal fill:#8e24aa          %% boss/objective
```

## Patterns

### 1. Hub-and-spoke
```
Hub([Town Square]) <--> Forest
Hub <--> Sewers
Hub <--> Temple
```
Player always returns to hub. Classic Metroidvania / Zelda structure.

### 2. Linear with one-way gates
```
A <--> B
B -- drop --> C
C <--> D
```
One-way drop from B to C creates tension: once you drop, you must progress forward to find an alternate route back.

### 3. Locked zones
```
Hub -- "Gate: Bronze Key" --> Dungeon1
Hub -- "Gate: Silver Key" --> Dungeon2
```
Label the key name on the edge — don't use a node. Keeps the topology clean.

### 4. Hidden shortcuts
```
Rooftop -.hidden.- Entry
```
Dashed edges = discoverable shortcuts. Designer intent: reward exploration.

## Render
```
/t1k:preview --from-file .claude/modules/design-base/skills/design-diagram-authoring/templates/level-spatial.mmd
```

## Common mistakes
- **Using `-->` for walkable passages** — `-->` is one-way; use `<-->` for normal walkable edges.
- **Gates as nodes, not edge labels** — a node called "LockedDoor" clutters the diagram. Put the key requirement on the edge.
- **Mixing spatial with logical flow** — a spatial diagram shows geography. Progression (quest order, unlocks over time) belongs in a quest tree.
- **Forgetting the start zone** — always mark the entry with a stadium `([...])` and green styling. Readers shouldn't have to guess where the player spawns.
- **Too many zones** — if a level has 40 zones, it's not a level anymore, it's a region. Split into multiple diagrams.
