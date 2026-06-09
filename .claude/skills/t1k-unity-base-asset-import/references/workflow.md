---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Asset Import Workflow

Step-by-step procedure for the `t1k-unity-base-asset-import` skill.

## Phase 0 — Pre-flight

1. Confirm `<incomingRoot>` exists (default `Assets/_Game/<GameName>/_Incoming/`).
2. Confirm Unity MCP is connected if `--apply` was requested (`mcp__UnityMCP__editor_state`).
3. Load kit defaults from `references/default-manifest-schema.md` (inline JSON).
4. If `.claude/asset-pipeline.json` exists, parse + deep-merge over kit defaults. On parse error → ERROR (do not silent-fall-back).

## Phase 1 — Scan

```bash
node .claude/modules/base/skills/t1k-unity-base-asset-import/scripts/scan-incoming.cjs \
  --incoming-root "Assets/_Game/StickManForge/_Incoming"
```

Emits JSON to stdout:

```json
{
  "incomingRoot": "Assets/_Game/StickManForge/_Incoming",
  "fileCount": 47,
  "files": [
    { "path": "Assets/_Game/StickManForge/_Incoming/T_BoneSkull_D.png", "name": "T_BoneSkull_D", "ext": ".png", "sizeBytes": 524288 }
  ]
}
```

Skips `*.meta`, hidden files, and directories.

## Phase 2 — Validate

```bash
node .claude/modules/base/skills/t1k-unity-base-asset-import/scripts/validate-naming.cjs \
  --files-json <scan-output> \
  --manifest <merged-manifest>
```

Per file, deterministic logic (no AI):

1. **Prefix detection** — match longest prefix in `manifest.prefixes` against filename. If none → REJECTED (`reason: no-prefix`).
2. **Regex check** — apply `prefixes[P].regex` to the stem (no extension). Mismatch → REJECTED (`reason: regex-mismatch`).
3. **Size budget** — read `sizeBytes` against `budgets[type].warnBytes`/`errBytes` (or `maxSize` for textures via dimension probe). Above `errBytes` → REJECTED (`reason: size-err`); above `warnBytes` → WARNING.
4. **Target folder** — resolve `prefixes[P].targetFolder` + look up `folderMap[targetFolder]` for `{ group, labels }`. Missing → WARNING (`reason: no-folder-map`).
5. **Realm auto-detect** — apply `addressables.realmAutoDetect.pattern` to stem; if matched, substitute `realm:auto` in labels with `realm_<lower>`. No match → drop the `realm:auto` token.

Emits per-file triage JSON.

## Phase 3 — Report

Emit markdown table per `report-format.md`. Include summary counts: ACCEPTED-CLEAN, ACCEPTED-WITH-WARNING, REJECTED-ERROR.

For REJECTED files, run AI rename suggestion (see SKILL.md § Rename Suggestion Logic).

**STOP here unless `--apply`.** Per `rules/preview-first-batch.md`:

- If total files > 10, smoke-test the first 5 in the report and confirm before showing the full list.
- Always `AskUserQuestion` before Phase 4.

## Phase 4 — Apply (gated)

```bash
node .claude/modules/base/skills/t1k-unity-base-asset-import/scripts/promote-assets.cjs \
  --triage-json <validate-output> \
  --apply
```

For each ACCEPTED file:

1. Resolve destination: `<projectRoot>/Assets/_Game/<GameName>/<targetFolder><filename>`.
2. Check target-collision: if exists → DOWNGRADE to REJECTED, log, skip.
3. Invoke `mcp__UnityMCP__manage_asset(action="move", source, destination)`.
4. Invoke `mcp__UnityMCP__manage_addressables(action="set_labels", path=destination, group, labels)`.
5. Verify with `manage_asset(action="get_info", path=destination)`.

For REJECTED files: log and leave in `_Incoming/`.

## Phase 5 — Summary

Emit final markdown:

```
## Apply Result
- Moved: <n>
- Labels set: <n>
- Skipped (collision): <n>
- Errors: <n>
```

If errors > 0, exit non-zero so calling skill can branch.

## Failure modes

| Symptom | Cause | Recovery |
|---|---|---|
| `manage_asset move` fails with "destination locked" | Unity is mid-import | Wait + retry (per `unity-forbidden-operations.md` — do NOT kill Unity) |
| `manage_addressables` "group not found" | Addressables group missing | Pre-create via `t1k-unity-base-addressables` skill; re-run |
| `.meta` orphaned at source | MCP move incomplete | `manage_asset(action="reimport", path=destination)` |
