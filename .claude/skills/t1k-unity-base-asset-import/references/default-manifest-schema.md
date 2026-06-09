---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Default Manifest Schema — `.claude/asset-pipeline.json`

## Top-level keys

| Key | Type | Required | Purpose |
|---|---|---|---|
| `$schema` | string | no | URL to schema (informational) |
| `engine` | string | no | Engine + version tag (e.g. `unity-6.3`) |
| `incomingRoot` | string | yes | Path to `_Incoming` folder (project-relative). Use `<GameName>` placeholder which expands per `t1k-modules.json` consumer name. |
| `prefixes` | object | yes | Map prefix → `{ type, regex, targetFolder }` |
| `budgets` | object | yes | Map asset-type → size thresholds |
| `folderMap` | object | yes | Map target folder → `{ group, labels }` |
| `addressables` | object | yes | Label families + auto-detect rules |
| `triage` | object | yes | Triage gate toggles |
| `contentKeys` | object | no | Optional path for rename lookups (e.g. ScriptableObject folder) |
| `naming` | object | no | Override per-prefix regex (alternative to inline `prefixes.X.regex`) |
| `engineDefaults` | object | no | Per-asset-type import-setting defaults |

## Inline example (kit defaults)

```json
{
  "$schema": "https://the1studio.org/schemas/asset-pipeline.v1.json",
  "engine": "unity-6.3",
  "incomingRoot": "Assets/_Game/<GameName>/_Incoming",
  "prefixes": {
    "T_":      { "type": "texture",              "regex": "^T_[A-Z][a-zA-Z]*(_[A-Z][a-zA-Z]+)+_[ANRMOEH]+$", "targetFolder": "Art/2D/" },
    "S_":      { "type": "sprite",               "regex": "^S_[A-Z][a-zA-Z]*(_[A-Z][a-zA-Z]+)+$",            "targetFolder": "Art/2D/" },
    "M_":      { "type": "material",             "regex": "^M_[A-Z][a-zA-Z]+(_[A-Z][a-zA-Z]+)+$",            "targetFolder": "Materials/" },
    "SFX_":    { "type": "audio.sfx",            "regex": "^SFX_[A-Z][a-zA-Z]+_[A-Z][a-zA-Z]+(_[0-9]{2})?$", "targetFolder": "Audio/SFX/" },
    "BGM_":    { "type": "audio.bgm",            "regex": "^BGM_[A-Z][a-zA-Z]+$",                            "targetFolder": "Audio/BGM/" },
    "VFX_":    { "type": "vfx",                  "regex": "^VFX_[A-Z][a-zA-Z]+(_[A-Z][a-zA-Z]+)+$",          "targetFolder": "VFX/" },
    "SO_":     { "type": "scriptableobject",     "regex": "^SO_[A-Z][a-zA-Z]+_[A-Z][a-zA-Z]+$",              "targetFolder": "ScriptableObjects/" },
    "Anim_":   { "type": "animation.clip",       "regex": "^Anim_[A-Z][a-zA-Z]+_[A-Z][a-zA-Z]+$",            "targetFolder": "Animations/Clips/" },
    "Ctrl_":   { "type": "animation.controller", "regex": "^Ctrl_[A-Z][a-zA-Z]+$",                           "targetFolder": "Animations/Controllers/" },
    "Sh_":     { "type": "shader",               "regex": "^Sh_[A-Z][a-zA-Z]+(_[A-Z][a-zA-Z]+)*$",           "targetFolder": "Shaders/" },
    "Vcam_":   { "type": "prefab.camera",        "regex": "^Vcam_[A-Z][a-zA-Z]+$",                           "targetFolder": "Prefabs/Cameras/" },
    "Prefab_": { "type": "prefab",               "regex": "^Prefab_[A-Z][a-zA-Z_]+$",                        "targetFolder": "Prefabs/" }
  },
  "budgets": {
    "texture":        { "maxSize": 1024, "warnAbove": 1024, "errAbove": 2048 },
    "texture.atlas":  { "maxSize": 2048, "warnAbove": 2048, "errAbove": 4096 },
    "audio.sfx":      { "maxBytes": 1048576,  "warnBytes": 1048576, "errBytes": 5242880 },
    "audio.bgm":      { "maxBytes": 5242880,  "warnBytes": 5242880, "errBytes": 12582912 },
    "animation.clip": { "maxDurationSec": 30 },
    "sprite.maxDim":  { "maxSize": 1024, "warnAbove": 1024, "errAbove": 2048 }
  },
  "folderMap": {
    "Art/2D/Characters/":   { "group": "art_2d_characters", "labels": ["realm:auto", "tier_base", "kind_art"] },
    "Art/2D/Items/":        { "group": "art_2d_items",      "labels": ["realm:auto", "tier_base", "kind_art"] },
    "Audio/SFX/":           { "group": "audio_sfx",         "labels": ["tier_base", "kind_audio"] },
    "Audio/BGM/":           { "group": "audio_bgm",         "labels": ["realm:auto", "tier_base", "kind_audio"] },
    "VFX/Combat/":          { "group": "vfx_combat",        "labels": ["realm:auto", "tier_base", "kind_vfx"] },
    "Scenes/Realms/":       { "group": "scenes_realms",     "labels": ["realm:auto", "tier_base", "kind_scene"] }
  },
  "addressables": {
    "labelFamilies": {
      "realm": ["realm_primal","realm_iron","realm_storm","realm_void","realm_celestial","realm_chaos"],
      "tier":  ["tier_base","tier_expansion_1","tier_liveops"],
      "kind":  ["kind_art","kind_audio","kind_vfx","kind_scene","kind_data"]
    },
    "realmAutoDetect": {
      "pattern": "_(Primal|Iron|Storm|Void|Celestial|Chaos)$",
      "map": {
        "Primal":    "realm_primal",
        "Iron":      "realm_iron",
        "Storm":     "realm_storm",
        "Void":      "realm_void",
        "Celestial": "realm_celestial",
        "Chaos":     "realm_chaos"
      }
    }
  },
  "triage": {
    "rejectOnRegexMismatch":    true,
    "rejectOnErrCap":           true,
    "warnOnUnknownExtension":   true
  }
}
```

## Key notes

- **`prefixes.<P>.regex`** — anchored full-name match (no extension). Strip extension before matching.
- **`folderMap`** keys end with `/` to disambiguate folder vs file path.
- **`labels: ["realm:auto", ...]`** — the literal `realm:auto` token instructs the validator to substitute the auto-detected realm label (or omit if no match).
- **`budgets.<type>.maxBytes`** — file size; `maxSize` — pixel dimension; `maxDurationSec` — clip length.
- **`triage.rejectOnErrCap`** — if `true`, files exceeding `errAbove`/`errBytes` are REJECTED. If `false`, they downgrade to WARNING.

## Validation rules

A project `.claude/asset-pipeline.json`:

- MUST set `incomingRoot`
- SHOULD extend `prefixes` rather than redefine all
- MUST NOT remove kit-default prefixes (deletions are not honored on deep-merge by design)

If a prefix needs disabling, override its regex with `null` — the validator treats `null` as "skip type detection for this prefix" (rejected as unknown).
