---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Project Override Pattern — Deep-Merge

The `.claude/asset-pipeline.json` in a consumer project is deep-merged over the kit defaults from `default-manifest-schema.md`. Project keys WIN at every leaf.

## Merge semantics

| Construct | Behavior |
|---|---|
| Object key in both | Recurse into the value |
| Primitive in both | Project replaces kit |
| Array in both | Project REPLACES (arrays do not concatenate) |
| Key only in project | Added to merged result |
| Key only in kit | Preserved |
| Project value is `null` | Treats as "disable" (kept as-null) — see `triage-rules.md` for prefix-disable semantics |

## Worked example — adding a `Loc_` prefix

A project ships localization data files with a custom prefix and a custom target folder.

### Project manifest

```json
{
  "incomingRoot": "Assets/_Game/StickManForge/_Incoming",
  "prefixes": {
    "Loc_": {
      "type": "data.localization",
      "regex": "^Loc_[a-z]{2}(-[A-Z]{2})?$",
      "targetFolder": "Localization/"
    }
  },
  "folderMap": {
    "Localization/": {
      "group": "data_localization",
      "labels": ["tier_base", "kind_data"]
    }
  }
}
```

### Merged result (effective manifest)

```json
{
  "incomingRoot": "Assets/_Game/StickManForge/_Incoming",
  "prefixes": {
    "T_":   { ... kit default ... },
    "S_":   { ... kit default ... },
    "...":  "(all other kit prefixes preserved)",
    "Loc_": {
      "type": "data.localization",
      "regex": "^Loc_[a-z]{2}(-[A-Z]{2})?$",
      "targetFolder": "Localization/"
    }
  },
  "folderMap": {
    "Art/2D/Characters/": { ... kit ... },
    "...": "(all kit folder entries preserved)",
    "Localization/":      { "group": "data_localization", "labels": ["tier_base", "kind_data"] }
  },
  "budgets":      "(kit defaults preserved — project didn't override)",
  "addressables": "(kit defaults preserved)",
  "triage":       "(kit defaults preserved)"
}
```

## Worked example — narrowing a budget

Project wants stricter SFX size limits.

### Project manifest

```json
{
  "incomingRoot": "Assets/_Game/StickManForge/_Incoming",
  "budgets": {
    "audio.sfx": { "maxBytes": 524288, "warnBytes": 524288, "errBytes": 2097152 }
  }
}
```

### Effect

The entire `audio.sfx` budget object is replaced (object → object deep-merges, but each KEY in the inner object is leaf-replaced). All three thresholds drop.

Kit's `audio.bgm`, `texture`, etc. budgets are untouched.

## Worked example — disabling a kit prefix

Project doesn't ship shaders and wants to reject any `Sh_*.shader` file.

### Project manifest

```json
{
  "incomingRoot": "Assets/_Game/StickManForge/_Incoming",
  "prefixes": {
    "Sh_": { "regex": null }
  }
}
```

### Effect

Deep-merge replaces `prefixes.Sh_.regex` with `null`. The `type` + `targetFolder` are preserved from kit defaults but the validator hits the `prefix-disabled` rule (per `triage-rules.md`) and REJECTS any `Sh_*` file.

## Non-mergeable arrays

Arrays in the manifest (e.g. `addressables.labelFamilies.realm`, `folderMap[X].labels`) are REPLACED on conflict. If the project wants to extend the realm list, it must restate the full list:

```json
{
  "addressables": {
    "labelFamilies": {
      "realm": [
        "realm_primal","realm_iron","realm_storm","realm_void",
        "realm_celestial","realm_chaos",
        "realm_dlc1","realm_dlc2"
      ]
    }
  }
}
```

This is intentional — list-merge semantics are ambiguous (prepend? append? dedupe?) and we'd rather be explicit.

## Implementation note

The merge is a standard recursive object-merge with primitive-replace + array-replace. Implementations of equivalent merges exist in lodash (`mergeWith` with custom array handler) and Node's `util` (no built-in deep merge — roll your own ~20 LOC).

`validate-naming.cjs` includes the merge utility inline so the skill has no external dependencies.
