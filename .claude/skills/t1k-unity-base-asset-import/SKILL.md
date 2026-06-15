---
name: t1k:unity:base:asset-import
description: Import, validate, rename, and promote Unity assets from a project's _Incoming/ folder to canonical locations. Manifest-driven — reads .claude/asset-pipeline.json for prefix/budget/Addressables-mapping rules and layers project overrides on top of kit defaults. Report-only by default; --apply moves files via Unity MCP. Triage levels — ACCEPTED-CLEAN, ACCEPTED-WITH-WARNING, REJECTED-ERROR.
effort: medium
context: fork
keywords: [import assets, promote assets, validate assets, asset import, incoming assets, asset naming check, asset pipeline]
version: 2.4.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity Asset Import — Manifest-Driven Triage & Promotion

## Triggers

- "import assets", "promote assets", "validate assets"
- "asset import", "incoming assets", "_Incoming"
- "asset naming check", "asset pipeline", "rename suggestion"

---

## Purpose

Process raw assets dropped into `Assets/_Game/<GameName>/_Incoming/` by artists or AI: detect type, validate naming + size + import-settings, triage into ACCEPTED-CLEAN / ACCEPTED-WITH-WARNING / REJECTED-ERROR, emit a report, and (with `--apply`) move accepted files to canonical folders + set Addressables groups + labels via Unity MCP.

Report-only by default. Mutations are gated behind explicit `--apply`.

## Two-Layer Manifest Model

| Layer | File | Owner | Purpose |
|---|---|---|---|
| Kit defaults | `references/default-manifest-schema.md` (inline JSON) | theonekit-unity | Prefix table, budgets, folder map, Addressable label families |
| Project override | `.claude/asset-pipeline.json` (project root) | consumer project | Overrides + additions (new prefixes, project-specific realms, custom folders) |

**Deep merge** with **project keys winning** — for any key path that exists in both, project value replaces kit value. New keys from the project are additive.

If `.claude/asset-pipeline.json` is missing, the skill uses kit defaults only. If present but malformed, ERROR (do not silently fall back).

See `references/project-override-pattern.md` for a worked example.

## Workflow

1. **Scan** — `scripts/scan-incoming.cjs` walks `<incomingRoot>` (default `Assets/_Game/<GameName>/_Incoming`). Emits JSON `{ files: [{path, name, ext, sizeBytes}] }`.
2. **Load manifest** — read kit defaults, deep-merge `.claude/asset-pipeline.json` over them.
3. **Validate per file** — `scripts/validate-naming.cjs` — for each file: detect prefix, attempt regex match, check size budget, resolve target folder via `folderMap`.
4. **Triage** — assign ACCEPTED-CLEAN, ACCEPTED-WITH-WARNING, or REJECTED-ERROR per `references/triage-rules.md`.
5. **Report** — emit markdown table per `references/report-format.md`. STOP here unless `--apply`.
6. **AskUserQuestion** — gate the apply phase (options: apply / fix-then-rescan / abort).
7. **Apply (gated)** — `scripts/promote-assets.cjs --apply`:
   - For ACCEPTED files: invoke `mcp__UnityMCP__manage_asset(action="move", source, destination)`.
   - Set Addressables group + labels via `mcp__UnityMCP__manage_addressables`.
   - For REJECTED files: leave in place. Re-emit AI rename suggestion if possible.

Follows `rules/preview-first-batch.md` — smoke 5 files first if total >10.

Full step-by-step: `references/workflow.md`.

## Triage Levels

| Level | Trigger | Action |
|---|---|---|
| ACCEPTED-CLEAN | Prefix detected, regex passes, size within budget, target folder resolved | Move + apply labels |
| ACCEPTED-WITH-WARNING | Regex passes but size > warn-threshold OR unknown extension AND `warnOnUnknownExtension: true` | Move + apply labels + flag in report |
| REJECTED-ERROR | Regex fails, size > err-threshold, OR no prefix detected | Leave in `_Incoming/`. Emit AI rename suggestion. |

See `references/triage-rules.md` for the full decision tree.

## Rename Suggestion Logic (AI)

When regex fails on a recognizable prefix:

1. Tokenize the source filename (e.g. `bonewarden_skull_diff_1024.png` → tokens `bonewarden`, `skull`, `diff`).
2. If manifest declares `contentKeys` (e.g. `ScriptableObjects/SO_Weapon_*.asset`), grep those SO names for fuzzy matches to the longest token.
3. Match found (`SO_Weapon_PrimalBoneSkullCap.asset` → `PrimalBoneSkullCap`) → propose `T_PrimalBoneSkullCap_Helmet_D.png` per the `T_` regex.
4. No match → propose a regex-conforming PascalCase name from the tokens; flag as `[ai-guess]`.

Always present rename suggestions in the report; never auto-rename on `--apply`.

## Addressables Group + Label Assignment

After resolving the target folder, look up `folderMap[targetFolder]` for `{ group, labels }`.

Labels follow three families (see manifest `addressables.labelFamilies`):

- `kind_*` — always assigned from folder type (`kind_art`, `kind_audio`, `kind_vfx`, `kind_scene`, `kind_data`).
- `realm_*` — assigned via `realmAutoDetect` regex against filename suffix (`_Primal` → `realm_primal`).
- `tier_*` — default `tier_base` unless filename matches `_expansion_N` / `_liveops` patterns.

## Per-Asset-Type Validators

| Type | Checks |
|---|---|
| `texture` | Max dimension (manifest `budgets.texture.maxSize`), sRGB ON for albedo (`_D` suffix), mip maps OFF for UI sprites |
| `texture.atlas` | Higher `maxSize` budget; warn if not in an Addressable sprite atlas group |
| `audio.sfx` | Mono, ADPCM/Vorbis, load-type `DecompressOnLoad`, < 1 MB |
| `audio.bgm` | Stereo, Vorbis, load-type `Streaming`, < 5 MB |
| `animation.clip` | Root motion OFF unless under `Cinematic/`; duration < 30s |
| `sprite` | PPU = 100, filter mode = Bilinear, no mip maps |

Import-setting checks are advisory in v1.0 (read from `.meta` if it exists); v1.1 will apply them via MCP.

## Constraints

- **NEVER mutate without `--apply`** — default mode is report-only.
- **NEVER auto-rename rejected files** — only suggest, leave for human/AI confirmation.
- **ALWAYS gate `--apply` via `AskUserQuestion`** — even on clean reports (per `preview-first-batch.md`).
- **NEVER skip the manifest** — missing kit defaults = bug; missing project manifest = use kit defaults; malformed project manifest = ERROR.
- **NEVER process files matching `*.meta`** — those are Unity's import sidecars; mirror them on move.
- **Idempotent moves only** — if target file exists at destination, REJECT with `target-collision`; do not overwrite.

## Gotchas

1. **Unity `.meta` files** — every asset move must move the `.meta` file in lockstep, or Unity loses the GUID. `manage_asset(action="move")` handles this; raw `mv` does NOT.
2. **Realm auto-detect is suffix-only** — files without a realm tag get no `realm_*` label. That is intentional; over-tagging is worse than under-tagging.
3. **Project override deep-merge is by-key, not by-element** — `prefixes` is merged key-by-key (project can add `Loc_` without touching `T_`); but `labelFamilies.realm` is replaced entirely if the project sets it (arrays don't merge).
4. **`_Incoming/` is gitignored** by convention — never commit raw incoming files. The skill assumes incoming is ephemeral.
5. **Addressables group must exist** — `manage_addressables` does NOT create groups on demand. Pre-create via `t1k-unity-base-addressables` skill.

## References

- `references/default-manifest-schema.md` — full schema + inline example
- `references/workflow.md` — step-by-step
- `references/triage-rules.md` — decision tree
- `references/report-format.md` — markdown report template
- `references/project-override-pattern.md` — deep-merge worked example

## Scripts

- `scripts/scan-incoming.cjs` — list files under `<incomingRoot>`
- `scripts/validate-naming.cjs` — apply regex + size checks per file
- `scripts/promote-assets.cjs` — move via Unity MCP (gated by `--apply`)

## Related Skills

- `t1k-unity-base-addressables` — group + label registration
- `t1k-unity-base-code-conventions` — naming conventions (see `project-folder-structure.md`)
- `t1k-unity-base-mcp-skill` — Unity MCP wrapper used by `promote-assets.cjs`
