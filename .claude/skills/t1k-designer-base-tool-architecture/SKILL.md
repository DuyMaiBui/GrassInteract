---
name: t1k:designer:base:tool-architecture
description: Build replicable schema-driven design tools — module registry, shape-first content pipeline, curriculum decoupling, load-time validation, solver-driven difficulty, telemetry feedback. Includes 5-step replication recipe.
effort: medium
keywords: [design tool, level editor, level pool, curriculum, progression, schema-driven, GameDesignToolTemplate, design tool replication, content pipeline, solver, telemetry feedback, schema migration, polycube, voxel surface, shape pool, family classification]
version: 1.13.2
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Game Design Tool Architecture

Build a replicable schema-driven design tool for level/content-driven games (puzzle, RPG, tower defense, card, match-3, platformer). Captures the toolchain pattern proven on Arrow3D — schema-driven UI, shape-first content pipeline, content-blind curriculum picker, two-phase content injection, load-time validation, and the hardening playbook (solver-driven difficulty, telemetry feedback, schema migrations, stats dashboard).

## When This Skill Triggers

- Building a new designer-facing tool from `GameDesignToolTemplate` or scratch
- Replicating an existing tool's patterns for a new game
- Hardening an MVP design tool with feedback loops, validation, migrations
- Reviewing whether a design-tool architecture decision fits or fights the playbook
- Architecting curriculum / level-pool / progression / comparison modules

## Maturity Tiers

A design tool walks through three tiers. Skipping ahead causes pain; skipping behind causes throwaway work.

| Tier | Goal | Outcome when missing |
|------|------|---------------------|
| 1. Foundation | Tool runs; designers can edit data | Hand-rolled CRUD per module → maintenance hell |
| 2. Core pipeline | Content can be generated, picked, injected, validated | Manual content authoring fails past 50 levels |
| 3. Hardening | Closed feedback loops; data-driven balance | Curriculum drifts from intent; bugs reach players |

## Tier 1 — Foundation

### Patterns

| Pattern | Why |
|---------|-----|
| **Schema-driven UI** — Zod schemas auto-generate tables and forms via `extractColumns` / `extractFormFields`; `extractFormFields` also extracts Zod `.default()` values so fields pre-populate correctly, and strips label markers (`[color]`, `[textarea]`, `[emoji]`, `[moduleRef:…]`) before rendering | Add a module by declaring schema; UI is free |
| **Local-first storage** — IndexedDB scoped by `{projectId, moduleId}`; writes to each key are serialized through a per-key mutex (prevents read-modify-write races when rapid saves fire concurrently) | No backend; offline iteration; export on demand |
| **Self-registering modules** — `registerModule({...})` via side-effect import | Sidebar populates automatically; no central manifest to drift |
| **Single engine, two callsites** — generation/validation lib called from in-editor Web Worker AND batch CLI | One source of truth for compute logic |
| **Cross-tree boundary** — design tool emits JSON; engine consumes; tool never edits engine code | Two codebases ship independently; engine bugs become audit reports, not silent fixes |
| **DataTable controlled pagination + stable row IDs** — `getRowId` callback returns a domain key (not array index); pagination state is controlled so filter/sort changes don't silently deselect rows | Row selection survives search and column sort; required for multi-select bulk actions |

### Arrow3D worked snippets

| What | Where |
|---|---|
| Module registration | `src/modules/level-editor/index.ts` + side-effect import in `src/App.tsx` |
| Schema → UI + defaults + marker strip | `src/core/schema-utils.ts` (`extractColumns`, `extractFormFields`) |
| Storage + write serialization | `src/core/data-store.ts` (`useDataStore` per module; `serialize()` mutex per key) |
| Worker + CLI parity | `src/modules/level-editor/workers/generator.worker.ts` calls same `generateLevel()` as `scripts/level-generate-arrow.mjs` |
| Cross-tree boundary | tool writes to `../UnityArrow3D/Assets/Level Json/`, never edits Unity source |
| Controlled pagination + getRowId | `src/components/data-table/data-table.tsx` |

### Anti-patterns

- ❌ Hand-rolled CRUD per module — defeats schema-driven design
- ❌ Backend service for "easier sync" — kills local-first iteration speed
- ❌ Two parallel engines for "interactive vs batch" paths — silent divergence
- ❌ Tool that auto-fixes engine bugs — cross-tree silent regressions
- ❌ Storing derived fields (e.g., `Size` when `Cells.bbox` computes it) — drift guaranteed

## Tier 2 — Core pipeline

The five-stage pipeline that scales content production from 10 to 1000+ levels.

### Stage 1: Shape-first generation

Generate geometry only. **Empty content array.** Instant (~ms per shape).

```
data/<batch-name>/Shape_001.json:
  { Cells: [...], Tiles: [] }   ← Tiles intentionally empty
```

**Why:** content placement is slow (often seconds per shape). Curriculum tuning iterates many times — baking content during shape gen wastes hours regenerating shapes that never make the cut.

### Stage 2: Curriculum picker (content-blind)

Score abstract metrics per L-slot from a progression curve:
- Geometry size targets / voxel-count buckets
- Family/category variety rules (e.g., 1-2 RECT → 1 SPECIAL rotation)

Picker has **zero content awareness**. Output: 1 shape per L-slot. Just geometry. Picker can be re-run cheaply since shape pool already exists.

### Stage 3: Content injection (two-phase)

| Phase | Type | Example |
|---|---|---|
| 3a | Auto primitives | Arrows, paths, mandatory placements — produced by generator engine; heavy compute |
| 3b | Designer-hinted/manual | Special elements, modifiers — placed by designer using per-L target counts surfaced as banner hints |

**Why two-phase:** auto path is deterministic and fits batch generation; manual path keeps designer authorship for rule-bending mechanics.

### Stage 4: Load-time validation (hard gate)

Two validators run before any level can be used:
1. **Geometry validator** — every cell exists, every face is exposed, no occlusions
2. **Reachability/solvability validator** — every primitive can resolve its goal

Failure → **throw**, never silent fallback. Same validators in tool AND engine — same JSON, same rules, same outcome.

### Stage 5: Engine output

Write JSON to engine's asset folder. Optional encrypted mirror. Engine consumes; tool is done. Schema agreed in writing.

### Arrow3D worked example

| Stage | File |
|---|---|
| 1. Shape gen | `scripts/gen-symbol-shapes.mjs` → `data/prod-batch-v*/` |
| 2. Picker | `scripts/build-level-curriculum.mjs` reads `src/modules/progression/config/default-progression.ts` |
| 3a. Auto inject | `scripts/level-generate-arrow.mjs` (parallel CLI) + `workers/generator.worker.ts` (in-editor) — both call `generator/index.ts` |
| 3b. Designer inject | `scripts/inject-elements.mjs` + manual editor placement |
| 4. Validate | `LevelShapeValidator` + `ArrowReachabilityValidator` (run in tool AND in Unity `LevelParamsLoader`) |
| 5. Output | `Arrow3D/UnityArrow3D/Assets/Level Json/Level_*.json` |

### Anti-patterns

- ❌ Picker that knows about content → couples curriculum tuning to content algorithms
- ❌ Baking content during shape gen → kills iteration speed
- ❌ Validating only at play time → bugs surface in player sessions
- ❌ Tool validators ≠ engine validators → silent dev/prod divergence
- ❌ Single-phase injection (everything auto) → templates lack rule-bending mechanics

## Tier 3 — Hardening

Graduate from MVP. Closed feedback loops; data-driven balance; production-grade.

### 3.1 Solver / auto-difficulty estimator

Run a play-simulator over each generated level. Score:
- States explored (proxy for puzzle space size)
- Decision branches (proxy for choice complexity)
- Dead-end frequency (proxy for trap density)
- Min-moves-to-solve

Compare actual score vs progression-curve intent at the level's L-slot. Reject outliers; auto-grade newly generated content; validate that "L=50" actually plays at L=50 difficulty target.

**MVP scope:** count states + branches + dead-ends. Defer A* / IDA* gold-plating.

### 3.2 Telemetry feedback loop

Engine emits per-level playtest logs (completion %, retries, fail-locations). Tool ingests JSON → progression module overlays "intended vs actual" per L-slot.

Unlocks data-driven curve adjustments; identifies rage-quit levels.

**Cross-tree caveat:** needs engine-side log emission. Plan with engine team before tool work begins.

### 3.3 Schema versioning + migration framework

`migrations/NNN-*.mjs` registry. Version stamp on every level JSON (`schemaVersion: N`). Auto-apply on data load.

Levels evolve without breaking historical content. Fully portable; bake into the template.

### 3.4 Stats dashboard module

Read-only module. Bar charts of:
- L-slot coverage (gaps highlighted)
- Family/category histogram
- Element/primitive count distribution
- Difficulty score distribution per L-slot (consumes 3.1 output)

Surfaces holes designers can't see in raw JSON.

### 3.5 A/B variant generator

Output 2-3 variants per L-slot at the same target difficulty. Tag with variant ID. Engine reads variant per cohort. Feeds telemetry loop (3.2) to compare cohort outcomes.

### 3.6 On-disk JSON variants

Save named curve/curriculum snapshots as `data/<module>-variants/<name>.json` — git-tracked files, not IndexedDB blobs.
**Why:** IndexedDB blobs are invisible to version control; variants need to be diffable, shareable, and auditable by the whole team.
File pointer: `data/progression-variants/`, `data/curriculum-variants/` (one file per named variant).

### 3.7 Chart-metric chip toggles

Declarative `STAT_CHIPS` table maps each metric to its toggle chip, curve target, and actual-achieved value. Dashboard code iterates the table — adding a new metric is one table row, not a conditional block.
**Why:** without the table, each new metric requires four scattered code sites to stay in sync; the table collapses them to one.
File pointer: `src/modules/stats-dashboard/config/stat-chips.ts` (or equivalent module config).
Refs: GameDesignToolTemplate PR #8, Issue #7.

### Arrow3D status (worked example)

| Pattern | Status |
|---|---|
| 3.1 Solver | Planned (reuses `walker.ts` + escape-animation engine) |
| 3.2 Telemetry | Deferred — needs Unity-side log emission |
| 3.3 Schema migrations | Shipped — `migrations/NNN-*.mjs` registry with `schemaVersion` stamp; auto-apply on load |
| 3.4 Stats dashboard | Planned |
| 3.5 A/B variants | Foundation present in `matchmaker/` (selector, slot-curve, output-writer) |
| 3.6 On-disk JSON variants | Shipped — progression/curriculum variants saved as `data/<module>-variants/<name>.json`; git-tracked instead of IndexedDB blobs |
| 3.7 Chart-metric chip toggles | Shipped — declarative `STAT_CHIPS` table drives per-metric toggle chips on the stats dashboard; no hardcoded conditionals per metric (template PR #8) |

### Anti-patterns

- ❌ Solver that aims for "perfect" — gold-plate kills MVP timeline; ship cheap heuristic first
- ❌ Telemetry without dashboard — raw logs don't drive design decisions
- ❌ Schema changes without migrations — breaks all existing level data
- ❌ Stats dashboard that writes — must be read-only to avoid drift between dashboard and SSOT modules

## Replication Recipe (5 steps)

1. **Start from `GameDesignToolTemplate`** — schema-driven shell + IndexedDB
2. **Define your geometry primitive** — grid / voxel / hex / graph. SSOT data type with computed bounds (no derived fields)
3. **Define your content primitives** — auto-place vs designer-place per primitive
4. **Build the pipeline** — Stage 1 (shape gen) → Stage 2 (picker) → Stage 3a/3b (inject) → Stage 4 (validate) → Stage 5 (engine writer). Single shared engine, two callsites
5. **Wire the engine consumer** — JSON schema agreed; engine runs same validators on load; tool never edits engine source

→ See `references/replication-recipe.md` for the full walkthrough with Arrow3D as worked example, including what to copy vs adapt vs skip.

## Hardening graduation checklist

Move Tier 2 → Tier 3 when ANY of these triggers:
- Curriculum picker output feels "off" (gut says easier/harder than intended)
- Designers complain about unseen L-slot coverage gaps
- A schema change broke historical level data
- Engine playtests reveal levels that "should" be L50 but everyone fails at L20

Order to ship:
1. Schema migrations (3.3) — cheap, portable, ship first
2. Stats dashboard (3.4) — surfaces gaps; designers self-serve
3. Solver (3.1) — validates curriculum intent; closes loop on (1)
4. Telemetry (3.2) — closes loop on (3); needs engine-side work
5. A/B variants (3.5) — once telemetry exists, multiplies throughput

## Cross-References

- `puzzle-game-design` — puzzle mechanics theory (combine for puzzle-genre tools)
- `game-level-design` — spatial flow and encounter design
- `game-procedural-generation` — generation algorithms feeding Stage 1
- `game-balance-tools` — DPS/EHP/curve formulas feeding Tier 3.1 solver scoring
- `game-design-document` — GDD/wiki sync patterns for the docs that wrap the tool

## Common Mistakes

| # | Mistake | Fix |
|---|---|---|
| 1 | Skipping Tier 1, going straight to procgen | No tool to drive procgen with — designers can't edit knobs |
| 2 | Picker that injects content while picking | Decouple — picker scores geometry only; content injection is later stage |
| 3 | Validators only in engine, not tool | Mirror them; tool catches bugs at edit time, not play time |
| 4 | Storing `Size` when `Cells.bbox` computes it | No derived fields — compute at use site |
| 5 | Per-game tool repo from scratch | Start from `GameDesignToolTemplate`; only add game-specific bits |
| 6 | Telemetry ingest without dashboard | Logs without visualization don't drive decisions |
| 7 | Solver gold-plating before MVP | Ship state-count heuristic; iterate later |


## Reference Files

| File | Coverage |
|------|----------|
| `references/replication-recipe.md` | Full 5-step walkthrough with Arrow3D worked example, what to copy / adapt / skip |

## Gotchas

- **Solver-driven difficulty needs a determinism contract** — same seed + same content = same output. If your solver uses `Random.value` instead of a seeded RNG, replays diverge.
- **Schema-driven tools rot if validation runs only at runtime** — load-time validation catches 80% of regressions on save; trust nothing without it.
- **Telemetry feedback ≠ telemetry firehose** — pipe only the metrics that drive design changes back into the tool; everything else is dashboard noise.
- **IndexedDB write races corrupt module data** — `useDataStore` runs multiple concurrent `setAllData` calls when rapid edits fire (e.g., inline-cell save while an import is in flight). Without a per-key mutex, the second write reads stale state and overwrites the first write's changes. Serialize writes via `data-store.ts → serialize(key, fn)`. Bug class: silently lost data, no thrown error.
- **Zod `.default()` values are not honored unless `extractFormFields` reads them** — `z.string().default("normal")` on a schema field does nothing unless the form generator extracts the `.default()` from the Zod type and passes it as `defaultValues` to the form. Without this, new-record forms show blank fields even when defaults are declared.
- **Label markers survive into rendered UI if not stripped** — `extractFormFields` must strip `[color:red]`, `[textarea]`, `[emoji:🗡️]`, `[moduleRef:items]` from field label strings before passing to the form renderer. Unstripped markers show as literal text in field labels.
