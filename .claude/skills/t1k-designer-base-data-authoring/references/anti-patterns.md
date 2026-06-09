---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Anti-Patterns — Design Data Authoring

Patterns to actively avoid when authoring CSV/JSON game-design data. Each one looks reasonable in the moment and causes downstream pain.

## Tables in design docs as the source of truth

When designers edit the table directly in `Demo-X.md`, the data drifts from any code consumer because the table isn't loadable. Keep the canonical data in `*.csv` / `*.json` and **reference** it from the doc with a one-line pointer + row count.

```markdown
> Data: see [items.csv](../data/backpack-crawler/items.csv) (84 rows).
```

If a published wiki needs a human-readable table excerpt, generate it from the data file via a build step — never hand-maintain a duplicate.

## Nesting in CSV

If a column would hold a list of objects (e.g., `prereq_nodes` as `;`-separated `id:level` pairs), the format has tapped out. Switch the whole table to JSON, or split: keep the flat scalar fields in CSV and move the nested portion into a sibling JSON file with `id` cross-references.

A cell like `{a:1;b:2}` or `[obj1]|[obj2]` is a smell — the parser is custom, the validator is missing, and designers will eventually break the encoding.

## Free-form strings as IDs

IDs are kebab-case lowercase, hyphen-separated, matching `^[a-z0-9_-]+$`. `letter-opener-2`, not `Letter Opener +2` or `letterOpener2` or `Letter_Opener_(2)`. IDs are URLs, file paths, dictionary keys, and diff anchors — they need to survive every system that handles them.

## Magic-number columns

Don't ship columns named `column_a`, `column_b`, `column_c` or `value_1`, `value_2`. Name what they mean: `base_damage`, `crit_chance`, `cooldown_seconds`. The schema is the documentation; opaque names guarantee future archaeology.

## Mixing units in the same column

`100ms` vs `0.1s` vs `100` (where 100 means milliseconds-but-you-have-to-know) all in one column is a runtime bug waiting to happen. Pick one unit per column, encode it in the column name (`cooldown_seconds`, `delay_ms`), and document the choice in `MANIFEST.md`.

## Inventing data not in source reports

If the design doc doesn't specify a value, mark it `TODO_FROM_DESIGN` rather than guess. A guessed value lands in spreadsheets, gets reviewed for balance against itself, and quietly becomes "what shipped" — even though no designer ever signed off. Better: explicit gap markers force the conversation.

## Schema drift between CSV and JSON

If a project uses CSV for items and JSON for gacha banner pity rules, the cross-reference convention (e.g., `items.csv:letter-opener-2` vs `"items.letter-opener-2"`) must match the rest of the project. Mixing two conventions across files turns every downstream loader into a special case. Pick once per project, document in MANIFEST.

## Top-level JSON arrays

Always wrap the array in an object: `{ "items": [...] }`, not `[...]`. Top-level arrays cannot grow new sibling fields (`_meta`, `version`, `schema_url`) without a breaking format change.

## Mixing scaling units (multiplier vs absolute) in the same column

A `damage_modifier` column where some rows hold `1.5` (meaning ×1.5 multiplier) and others hold `50` (meaning flat +50) is unparseable: the loader can't disambiguate, and two designers will write contradicting values that look type-correct to the validator. Same value, different math.

Split the column into two: `damage_multiplier` (numeric, default `1.0`) and `damage_flat_bonus` (numeric, default `0`). Both encode their semantics in the name. The runtime applies them in a documented order (e.g., `final = (base + flat) * multiplier`). If only one of the two ever fires per row, that's still cheaper than overloading one column.

Same trap appears in `cooldown_modifier` (multiplier vs seconds-delta), `drop_rate` (probability vs weight), and `xp_gain` (flat vs %).

## Type churn between drafts

A `tier` column starts as `int` (1/2/3), then a designer adds "S" tier and the column silently becomes `string` ("1"/"2"/"3"/"S"). Downstream loaders that parsed it as int still parse "1" correctly — until they hit "S" and crash in production. Or `cost` starts as `int`, becomes `float` to support fractional, then becomes `string` to support `"free"` / `"variable"` — each step compiles, the validator passes, the runtime breaks at row N.

Lock column types in the schema from day 1. If a column needs to admit multiple value shapes, declare it explicitly (`tier: enum["1","2","3","S"]` from the start, or `cost: { oneOf: [number, "free", "variable"] }`). Add a CI gate that diffs the inferred column types across PRs and **warns on type widening** — this is the only signal that catches the drift before the runtime does.
