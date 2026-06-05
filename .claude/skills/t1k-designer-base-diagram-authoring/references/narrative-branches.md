---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Narrative Branches — Mermaid Patterns

**Template:** `templates/narrative-branch.mmd` (Mermaid `classDiagram`)

## When to use this template
- Documenting branching narrative where player choice changes character relationships
- Showing character arcs alongside relationships in one view
- Mapping consequence flow: which ending triggers from which choices
- Multi-Act stories where each character has beats per Act

## Why `classDiagram` instead of `flowchart`
A `flowchart` shows ONE thing flowing. A `classDiagram`:
- Lets each character be a "class" with internal beats (Act1/Act2/Act3 as members)
- Shows relationships (mentor/ally/enemy) with cardinality
- Distinguishes certain (`-->`) from conditional (`..>`) paths

For pure decision trees use `flowchart` instead.

## Core Mermaid syntax you need

### Classes as characters
```
class Hero {
  +Act1 : leaves village
  +Act2 : confronts Elder
  -motivation : revenge OR truth
}
```
- `+member` = public beat (visible to player)
- `-member` = private motivation (GM/writer notes)
- Each line = one Act beat. Keep to 3-5 per character.

### Relationships
| Syntax | Meaning |
|---|---|
| `A --> B` | Directed hard relationship (Hero mentors Elder) |
| `A --|> B` | Inheritance / "is-a" (Rival is-a Hero archetype) |
| `A ..> B` | Conditional / dashed (happens IF a choice) |
| `A "1" --> "1" B` | Cardinality labels (one-to-one) |
| `A --> B : label` | Labeled relationship |

### Direction
```
direction LR
```
Left-right reads most naturally when Acts progress horizontally. Use `TB` for hierarchy-first diagrams.

## Patterns

### 1. Binary branch by single choice
```
Hero ..> Ending_A : if trust Elder
Hero ..> Ending_B : if refuse Elder
```
The `..>` (dashed) makes it clear these are alternatives, not sequential.

### 2. Character arc per Act
Each character class lists Act1/Act2/Act3 beats as members. Reading a column = one Act's state across all characters.

### 3. Consequence layering
```
Rival --> Ending_Aligned : enemy
Outcasts --> Ending_Rebel : ally
```
Show how secondary characters' states depend on the protagonist's choice.

### 4. Hidden motivation
Use `-` (private) for writer-only notes like `-motivation : revenge`. These don't appear to the player but guide the design.

## Render
```
/t1k:preview --from-file .claude/modules/design-base/skills/design-diagram-authoring/templates/narrative-branch.mmd
```

## Common mistakes
- **Using `flowchart` for character arcs** — `flowchart` can't show both arc AND relationships cleanly. Pick `classDiagram` when relationships matter.
- **Too many Acts per class** — 3-5 beats per character max. Split into multiple diagrams if you have more.
- **Mixing narrative and mechanics** — this diagram is about story. Item pickups, combat, and XP go in other diagrams.
- **Undocumented conditionals** — every `..>` must have a trigger label (`: if trust Elder`). Unlabeled dashes are confusing.
