---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Economy Flows — Mermaid Patterns

**Template:** `templates/economy-flow.mmd` (Mermaid `flowchart LR`)

## When to use this template
- Documenting currency sources and sinks in a GDD
- Pricing/conversion audits (is the gem → gold rate inflationary?)
- Spotting "leaks" (currency enters the economy but never leaves)
- Comparing hard-currency (IAP) vs soft-currency (earned) loops

## Core Mermaid syntax you need

### Direction
`flowchart LR` = Left-Right — matches the visual metaphor of "faucet flows into sink."

### Node shapes to distinguish roles
| Shape | Mermaid syntax | Role in economy |
|---|---|---|
| stadium | `F1([Quest Reward])` | Faucet (currency source) |
| cylinder | `Gold[(Gold)]` | Currency pool (held by player) |
| hexagon | `Shop{{Gem Shop}}` | Conversion / exchange |
| parallelogram | `S1[/Gacha Pull/]` | Sink (currency destroyed) |
| rectangle | `Item[Sword]` | Goods received (neutral) |

### Edges
- `F1 --> Gold` — faucet fills a pool
- `Gold --> S1` — sink drains a pool
- `Gems --> Shop --> Gold` — conversion chain (gems become gold)

### Styling
Color by role — a designer should see at-a-glance where money enters and leaves:
```
classDef faucet fill:#4caf50
classDef sink fill:#e53935
classDef conversion fill:#ffb300
classDef currency fill:#1976d2
```

## Patterns

### 1. Single-currency loop
```
Faucet --> Currency --> Sink
```
Early-stage prototype. One faucet, one sink. Easy to balance: sink rate > faucet rate = deflation.

### 2. Dual-currency with exchange
```
Quests --> Gold
IAP --> Gems --> Shop --> Gold
Gold --> Items
Gems --> Gacha
```
Standard F2P mobile model. Gems are hard currency (IAP + rare rewards); Gold is soft (earned). Exchange node enforces a sink on Gems.

### 3. Faucet with rate gate
```
Regen{{Energy Regen: 1/5min}} --> Energy
```
Use a hexagon to document the RATE, not just the fact of the faucet. Energy systems live or die by this number.

### 4. Leak detection
Every currency pool needs at least one outgoing edge to a sink. If `Gold` has 5 incoming faucets and 0 sinks, that's a leak — inflation is guaranteed.

## Render
```
/t1k:preview --from-file .claude/modules/design-base/skills/design-diagram-authoring/templates/economy-flow.mmd
```

## Common mistakes
- **Confusing goods with currency** — "Sword" is a good, not a currency. Goods don't enter the flow diagram; they're outputs of sinks.
- **Missing conversion nodes** — if Gems can buy Gold, draw the exchange explicitly. Implicit conversions hide balance bugs.
- **No rate labels** — "Ad Watch → Gems" is useless without the rate ("3 Gems per ad, 5 ads/day cap").
- **Forgetting offline faucets** — daily login, timer regen, passive generators are real faucets. Document them.
