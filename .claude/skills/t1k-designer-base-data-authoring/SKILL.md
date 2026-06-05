---
name: t1k:designer:base:data-authoring
description: Author game-design data as CSV (flat tables) and JSON (nested/complex). Item lists, achievements, skill trees, enemy stats, balance curves, calendars. Spreadsheet-editable, version-controllable, code-loadable. NOT a runtime loader — authoring + schema + conventions only.
effort: low
keywords: [csv, json, data file, data authoring, schema, table, items, achievements, skill tree, enemies, balance, calendar, ScriptableObject, content matrix, content pipeline]
version: 1.13.0
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Design Data Authoring

Game design data lives in **files**, not buried in design docs. This skill ships templates, schemas, and conventions for authoring game data as CSV (flat) and JSON (nested), so designers can edit in spreadsheets, devs can load directly into engines, and version control diffs cleanly.

## When This Skill Triggers
- New game project with content matrices to design (items, enemies, achievements, skill tree, calendar)
- Converting design tables from Markdown design docs into editable data files
- Standardizing data file conventions across a project
- Designer asks "where should the items list live?" or "CSV or JSON for this?"
- Setting up a `docs/.../data/{project}/` directory for a new game

## Quick Reference (Intent → Section)

| If you... | Go to |
|---|---|
| Are setting up a new project's data directory | [Standard Project Layout](#standard-project-layout) + [Workflow](#workflow) |
| Need to choose between CSV and JSON for a specific dataset | [CSV vs JSON — Decision Rule](#csv-vs-json--decision-rule) |
| Are converting Markdown tables in a design doc into data files | [Workflow → Converting an existing design doc to data files](#workflow) |
| Are editing an existing CSV or JSON and need the rules | [CSV Conventions](#csv-conventions--canonical--see-referencescsv-conventionsmd) / [JSON Conventions](#json-conventions--canonical--see-referencesjson-conventionsmd) |
| Need to coordinate with engine-side loading or related skills | [Coordination With Other Skills](#coordination-with-other-skills) + [Reference Material](#reference-material) |

## What This Skill Is NOT
- NOT a runtime loader. Engine-side loading (Unity ScriptableObject baking, Addressables, Resources.Load) is engine-kit territory; this skill points to references for those patterns but does not implement them.
- NOT an ETL tool. No CSV→database, no JSON→Protobuf transforms.
- NOT a balance solver. Use `game-balance-tools` for formulas; this skill captures the *output* of balance work.
- NOT prescriptive on names. Templates show schema; designers set the actual values.

## CSV vs JSON — Decision Rule

**Use CSV when:**
- Each row is independent (no nesting)
- Designers will edit in Google Sheets / Excel / Numbers
- Schema is flat: ID + columns of scalars (numbers, short strings, enum tags)
- Localization-friendly (translators handle CSV easily)
- Examples: items, achievements, enemies, currencies, IAP catalog, daily modifiers, lore fragments, hero arcs, live-ops calendar, battle pass tier rewards

**Use JSON when:**
- Data is nested (objects within objects, arrays of objects)
- Conditional or polymorphic structure
- Schema includes computed fields, ranges, or complex types
- Examples: gacha banner config + pity rules, fusion combo conditions, cohort × week × currency matrices, NG+ scaling rules, encounter card decks, success-metric KPIs with floors

See [references/csv-vs-json.md](./references/csv-vs-json.md) for the long version.

## Standard Project Layout

```
docs/wiki/data/{project-name}/
├── MANIFEST.md          # index: every file + row/key count + one-line description
├── *.csv                # flat tables
├── *.json               # nested configs
└── schemas/             # OPTIONAL: $schema files for JSON validation
```

Reference path on this project: `docs/wiki/data/backpack-crawler/` (32 files, MANIFEST.md as index).

## Templates Shipped

Copy from `.claude/modules/design-base/skills/design-data-authoring/templates/` into your project's data directory and edit.

| Template | Format | Use For | Required Columns / Keys |
|---|---|---|---|
| `items.csv` | CSV | Item lists with stats, rarity, set tags | id, name, rarity, set_id, size_w, size_h, type, base_value, cooldown_seconds, flavor_text |
| `achievements.csv` | CSV | Achievement / quest definitions | id, name, category, condition, reward_type, reward_amount, in_world_flavor |
| `skill-tree.csv` | CSV | Skill tree nodes with prereqs | node_id, branch, tier, name, prereq_node_ids, cost, effect, capstone_flag |
| `enemies.csv` | CSV | Enemy bestiary | id, name, archetype, hp, atk, def, special_ability, drop_table_ref |
| `calendar.csv` | CSV | Live-ops calendar mega-table | week, content_focus, weekly_modifier, monthly_event, battle_pass_cycle, gacha_banner |
| `currency-curves.json` | JSON | 4-currency × N-cohort × W-week sink/faucet matrix | cohorts.{name}.currencies.{id}.weekly_net[w] + cumulative_at_w_n |
| `encounter-cards.json` | JSON | Procedural encounter deck per Act | act_n.cards[].card_id + enemy_archetype + position_layout + terrain_modifier + loot_drop_table_ref |
| `gacha-config.json` | JSON | Banners, rarity weights, pity rules | banners[], rarity_weights, pity, pricing |
| `MANIFEST.md` | Markdown | Data directory index | per-file rows, count, one-line description |

## CSV Conventions (canonical — see [references/csv-conventions.md](./references/csv-conventions.md))

1. **UTF-8, no BOM**, comma separator, `\n` line endings (Unix).
2. **Header row mandatory.** Lower-snake-case column names.
3. **Primary key column** named `id` (string, hyphen-separated, kebab-case). Globally unique within the file. Match `^[a-z0-9_-]+$`.
4. **Quote any cell** containing `,`, `"`, or `\n`. Escape internal `"` as `""`.
5. **Boolean cells:** `true` / `false` (lowercase).
6. **Empty/null cells:** leave blank (no `null` literal).
7. **List columns:** semicolon-separated (`tag1;tag2;tag3`). Document in `MANIFEST.md`.
8. **Cross-references:** `<file>:<id>` format — e.g. `items.csv:letter-opener-2` in `set_id` column.
9. **Prefer expanded rows over wide tables.** If a row would have 20+ columns, consider JSON.
10. **`notes` column allowed** as the last column for designer commentary that's not consumed by code.

## JSON Conventions (canonical — see [references/json-conventions.md](./references/json-conventions.md))

1. **2-space indentation.** UTF-8.
2. **Top-level object** (never top-level array — wrap in `{ "items": [...] }`).
3. **camelCase keys** (Unity convention) OR snake_case (game design convention). Pick one per project, document in MANIFEST. Default for designer kit: **snake_case** (matches CSV columns).
4. **`$schema` link** at top of file when validation matters: `"$schema": "./schemas/items.schema.json"`.
5. **Enum values as strings**, not integers.
6. **Numeric cells preserve type** (don't quote numbers).
7. **`_meta` block** at top is allowed for version + source-of-truth tracking: `{ "_meta": { "version": "1.0.0", "source_doc": "Demo-X-Systems.md§4" } }`.
8. **Cross-references:** dotted path or full file path, e.g. `"items.letter-opener-2"` or `"items.json#/items/letter-opener-2"`.

## Workflow

### Starting a new project's data
1. Create `docs/wiki/data/{project}/` directory.
2. List all the data sets the design needs (item lists, achievements, etc.).
3. For each, decide CSV vs JSON via the rule above.
4. Copy the matching template from this skill's `templates/` folder.
5. Populate from the design doc / report.
6. Build `MANIFEST.md` listing every file with row/key count.

### Converting an existing design doc to data files
1. Identify all tabular data in the doc (look for Markdown tables and bulleted lists with regular structure).
2. For each table, evaluate CSV vs JSON.
3. Extract into the matching template.
4. **Replace the table in the design doc with a one-line reference** to the data file: `> Data: see [items.csv](../data/backpack-crawler/items.csv) (84 rows).`
5. Add a sync note: "Source of truth = data file. Doc reference is read-only."

### Sync data → design doc tables (reverse)
For published GDDs (e.g., wiki pages a non-technical audience reads), generate a Markdown excerpt from the CSV/JSON via a build step. Don't hand-maintain duplicate tables.

## Coordination With Other Skills

| Skill | How they relate |
|---|---|
| `game-design-document` | GDD references data files. Designer edits the data file; doc shows summary + link. |
| `game-balance-tools` | Outputs balance formulas → data tables → consumed by this skill's templates. |
| `game-economy-design` | Currency cohort curves authored as `currency-curves.json` per this skill. |
| `design-diagram-authoring` | Sister skill: handles **diagrams** (Mermaid). This skill handles **data**. Both feed into the GDD. |
| `t1k-cook` | When implementing, the engine layer reads CSV/JSON authored here. |

## Anti-Patterns

See [references/anti-patterns.md](./references/anti-patterns.md) for the full catalogue (8 patterns, with examples). Top three: don't keep tables as the source of truth in design docs, don't nest objects inside CSV cells, and don't ship magic-number columns (`column_a` instead of `base_damage`).

## Asset-Identifier Cross-Link (MANDATORY for data referencing art)

When authoring CSV / JSON / SO data that references art identifiers (sprite paths, prefab refs, addressable keys, material names), the data file or its companion doc MUST cross-link to the matching dept-page §Reskin Self-Service so anyone reading the data can find:

1. Where the asset lives on disk (per dept-page Folder Map)
2. Which prefab consumes it (per dept-page Prefab Hierarchy Reference)
3. How to swap it (per dept-page Reskin Workflow)

### Data column conventions

For columns referencing art:
- `iconSprite`: path or addressable key relative to `Assets/` — see Art-2D §Reskin Self-Service Folder Map for the canonical pattern
- `vfxPrefab`: addressable key — see VFX §Reskin Self-Service Folder Map
- `audioClip`: addressable key — see Audio §Reskin Self-Service Folder Map (when Audio page adds one)

### SO authoring patterns

When defining a ScriptableObject that owns an art reference:
- Add a header tooltip pointing at the matching dept-page §Reskin Self-Service section
- For tint-driving SOs (`SO_Realm_*`, `SO_RarityTint_*`, `SO_EraPalette_*`), document that they're artist-editable (no engineering escalation)
- Use the canonical name from Asset-Pipeline §SO naming table (no drift)

### Drift-prevention

Code identifiers (CSV column names, SO field names) are SHIPPED — they MUST NOT be renamed in wiki unless code is updated in lockstep. Flag this in the data file's authoring guide.

Cross-link to t1k-designer-wiki-core §Dept-Page Reskin Self-Service Convention for the canonical template.

## Gotchas

- **Progression/curriculum variants belong in git, not IndexedDB** — saving named curve or curriculum snapshots as IndexedDB blobs makes them invisible to version control, unshareable, and unauditable. Ship variants as `data/<module>-variants/<name>.json` files committed to the repo. The tool's "Save variant" action should write to disk (or prompt export), not to IndexedDB. See `game-design-tool-architecture` § 3.6 for the full pattern.

- **Excel saves CSVs with UTF-8 BOM by default.** This violates the canonical "UTF-8, no BOM" rule and any downstream loader that doesn't strip the BOM will see a phantom character prepended to the first column header. **Workaround:** use `Save As → CSV UTF-8 (Comma delimited)`, then strip the BOM. Note the `sed -i` syntax differs by platform — macOS/BSD requires an empty string after `-i`, GNU/Linux does not:
  ```bash
  # macOS / BSD
  sed -i '' '1s/^\xef\xbb\xbf//' file.csv
  # GNU / Linux
  sed -i    '1s/^\xef\xbb\xbf//' file.csv
  # Portable (any sed)
  perl -i -pe 's/^\xef\xbb\xbf//' file.csv
  ```
  Google Sheets and Numbers export BOM-free by default, making them safer choices for initial authoring. Verify with `file file.csv` — a clean file reads `ASCII text` or `UTF-8 Unicode text`, not `UTF-8 Unicode (with BOM) text`.

- **Google Sheets exports CSVs with CRLF line endings on macOS.** The `\r` characters violate the Unix `\n` convention, introduce diff noise, and break custom Node.js row parsers that split on `\n` alone (`pandas` handles this transparently, but the divergence is still a gotcha). **Workaround:** post-process each export with `tr -d '\r' < raw.csv > clean.csv`, or rely on the bundled `validate-csv.cjs` which flags `\r` presence before committing.

- **Strict downstream validators drop the optional `notes` column.** The convention allows a trailing `notes` column for designer commentary that's not consumed by code, but JSON-Schema validators configured with `additionalProperties: false` will reject CSVs that include it unless `notes` is explicitly declared in the schema. **Workaround:** either add `notes` as a typed `string` field in the schema (with `additionalProperties: false` and `notes` in the declared property list), or strip it from the runtime artifact: `csvcut -C notes input.csv > runtime.csv`. Don't silently discard designer context — pick one approach and document it in `MANIFEST.md`.

## Validation

Run before committing:

```bash
# CSV consistency check (column count, encoding, primary-key uniqueness)
node .claude/modules/design-base/skills/design-data-authoring/scripts/validate-csv.cjs docs/wiki/data/{project}/*.csv

# JSON validity check
for f in docs/wiki/data/{project}/*.json; do
  python3 -m json.tool "$f" > /dev/null && echo "OK: $f"
done
```

## Sync to Kit

If you discover a new template pattern or convention that should ship with the kit globally, run `/t1k:sync-back` to PR the addition into `theonekit-designer`.

## Reference Material

- [csv-vs-json.md](./references/csv-vs-json.md) — decision guide deep-dive (includes the expanded decision tree)
- [csv-conventions.md](./references/csv-conventions.md) — exhaustive CSV rules
- [json-conventions.md](./references/json-conventions.md) — exhaustive JSON rules
- [anti-patterns.md](./references/anti-patterns.md) — patterns to avoid (full catalogue)
- [unity-data-loaders.md](./references/unity-data-loaders.md) — engine-side consumption patterns (ScriptableObject, Addressables, runtime parse)
