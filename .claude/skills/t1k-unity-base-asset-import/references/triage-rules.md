---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Triage Rules — Decision Tree

The validator emits one triage verdict per file. The decision tree below is applied in order; the FIRST matching condition decides.

## Decision tree

```
1. Detect prefix from filename
   └─ no prefix match → REJECTED-ERROR (reason: no-prefix)

2. Apply regex from manifest.prefixes[P].regex
   └─ regex returns null → REJECTED-ERROR (reason: prefix-disabled)
   └─ regex mismatch     → REJECTED-ERROR (reason: regex-mismatch, suggestRename=true)

3. Check size against manifest.budgets[type]
   └─ sizeBytes > errBytes (or dim > errAbove) AND triage.rejectOnErrCap
        → REJECTED-ERROR (reason: size-err)
   └─ sizeBytes > errBytes AND NOT triage.rejectOnErrCap
        → ACCEPTED-WITH-WARNING (reason: size-err-downgraded)
   └─ sizeBytes > warnBytes → ACCEPTED-WITH-WARNING (reason: size-warn)

4. Resolve target folder
   └─ folderMap miss → ACCEPTED-WITH-WARNING (reason: no-folder-map)

5. Resolve labels (realm:auto)
   └─ realm:auto present but no realm-suffix match
        → ACCEPTED-WITH-WARNING (reason: no-realm-tag)
        AND drop the realm:auto token

6. Check extension against expected (per type)
   └─ unknown extension AND triage.warnOnUnknownExtension
        → ACCEPTED-WITH-WARNING (reason: unknown-ext)
   └─ unknown extension AND NOT triage.warnOnUnknownExtension
        → REJECTED-ERROR (reason: unknown-ext)

7. All checks pass → ACCEPTED-CLEAN
```

## Type detection from prefix + extension

The prefix decides the **declared type**. The extension is a sanity check:

| Type | Expected extensions |
|---|---|
| `texture`, `texture.atlas` | `.png`, `.jpg`, `.tga`, `.psd`, `.exr` |
| `sprite` | `.png`, `.psd` |
| `material` | `.mat` |
| `audio.sfx`, `audio.bgm` | `.wav`, `.ogg`, `.mp3` |
| `vfx` | `.vfx`, `.prefab` |
| `scriptableobject` | `.asset` |
| `animation.clip` | `.anim`, `.fbx` |
| `animation.controller` | `.controller` |
| `shader` | `.shader`, `.shadergraph`, `.hlsl` |
| `prefab`, `prefab.camera` | `.prefab` |

Extension mismatch is a `unknown-ext` flag, not a hard reject — sometimes artists ship `.exr` where `.tga` is canonical.

## Reasons reference

| Reason code | Description | Triage |
|---|---|---|
| `no-prefix` | Filename starts with no recognized prefix | REJECTED-ERROR |
| `prefix-disabled` | Manifest declares `regex: null` for this prefix | REJECTED-ERROR |
| `regex-mismatch` | Prefix matches but name doesn't conform to regex | REJECTED-ERROR |
| `size-err` | Size exceeds `errBytes`/`errAbove` | REJECTED-ERROR |
| `size-err-downgraded` | Size exceeds err threshold but `rejectOnErrCap: false` | ACCEPTED-WITH-WARNING |
| `size-warn` | Size between `warnBytes` and `errBytes` | ACCEPTED-WITH-WARNING |
| `no-folder-map` | Target folder has no entry in `folderMap` | ACCEPTED-WITH-WARNING |
| `no-realm-tag` | `realm:auto` requested but no suffix match | ACCEPTED-WITH-WARNING |
| `unknown-ext` | Extension not in expected set | WARNING or REJECTED per manifest |
| `target-collision` | A file already exists at destination | REJECTED-ERROR (apply-phase only) |

## Multi-reason files

A file may accumulate multiple WARNING-level reasons (e.g. `size-warn` + `unknown-ext`). The verdict is ACCEPTED-WITH-WARNING with all reasons listed. ERROR is terminal — no further checks run after the first ERROR.
