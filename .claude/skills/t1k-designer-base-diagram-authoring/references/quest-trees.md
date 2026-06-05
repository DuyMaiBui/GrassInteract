---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Quest Trees — Mermaid Patterns

**Template:** `templates/quest-tree.mmd` (Mermaid `flowchart TD`)

## When to use this template
- Quest progression with prerequisites (unlock chains)
- Branching main story with player-choice forks
- Optional side quests attached to the main spine
- Act-based pacing where each Act has its own subtree

## Core Mermaid syntax you need

### Direction
`flowchart TD` = Top-Down — the natural reading order for quest chains. Use `LR` only if your GDD is landscape.

### Node shapes (shape communicates type)
| Syntax | Shape | Use for |
|---|---|---|
| `Q1[Quest Name]` | rectangle | Standard quest / task |
| `Q1([Prologue])` | stadium | Terminal nodes (start/end) |
| `Q1{Trust Elder?}` | diamond | Player choice |
| `Q1[(Key Item)]` | cylinder | Required item gate |
| `Q1>Note]` | flag | Story note / reward |

### Edges
- `A --> B` — standard prerequisite edge (solid)
- `A -- Yes --> B` — labeled edge (choice outcome)
- `A -.optional.-> B` — dashed edge for optional / hidden paths
- `A ==> B` — thick edge to highlight a critical path

### Styling (classDef)
Group nodes by role — reader scans color before labels:
```
classDef choice fill:#ffd54f,stroke:#b28900,color:#000
class Q3,Q7 choice
```

## Patterns

### 1. Linear
```
Q1 --> Q2 --> Q3 --> End
```
Simplest. Use for tutorial acts.

### 2. Branch
```
Q1 --> Q2{Choice?}
Q2 -- A --> Q3A
Q2 -- B --> Q3B
Q3A --> Q4
Q3B --> Q4
```
Two paths reconverge at Q4 (convergent branch). Divergent branches never reconverge — use only when you mean it.

### 3. Hub-and-spoke
```
Hub((Town)) --> S1
Hub --> S2
Hub --> S3
```
All side quests flow off a central hub. Mirrors the game's actual UX (quest board).

### 4. Act-based
Use Mermaid subgraphs:
```
subgraph Act1
  A1 --> A2 --> A3
end
subgraph Act2
  B1 --> B2
end
A3 --> B1
```

## Render
```
/t1k:preview --from-file .claude/modules/design-base/skills/design-diagram-authoring/templates/quest-tree.mmd
```

## Common mistakes
- **Too many reconverging branches** — if A, B, C, D all funnel back to Q5, the choice was cosmetic. Either commit to divergent outcomes or drop the choice.
- **Mixing prerequisites with narrative flow** — a quest-tree shows unlocks, not story. Use `narrative-branch.mmd` for story.
- **Forgetting terminal nodes** — every graph needs a `Start([...])` and `End([...])` stadium so readers orient instantly.
