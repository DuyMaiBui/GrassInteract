---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Report Format

Markdown report emitted after the validate phase. Always shown to the user before `--apply`.

## Template

```markdown
# Asset Import Report — <timestamp>

**Incoming root**: `<incomingRoot>`
**Manifest**: kit-defaults + <project-manifest-path-or-"none">
**Scanned**: <N> files

## Summary

| Verdict | Count |
|---|---|
| ACCEPTED-CLEAN          | <n> |
| ACCEPTED-WITH-WARNING   | <n> |
| REJECTED-ERROR          | <n> |

## Per-file triage

| File | Status | Target | Group | Labels | Reason |
|---|---|---|---|---|---|
| `T_BoneSkull_D.png` | ACCEPTED-CLEAN | `Art/2D/Items/` | `art_2d_items` | `realm_primal, tier_base, kind_art` | — |
| `bonewarden_diff.png` | REJECTED-ERROR | — | — | — | `no-prefix`. Suggested: `T_PrimalBoneSkullCap_D.png` (matched SO_Weapon_PrimalBoneSkullCap) |
| `SFX_Hit_Big_999.wav` | ACCEPTED-WITH-WARNING | `Audio/SFX/` | `audio_sfx` | `tier_base, kind_audio` | `size-warn` (1.4 MB > 1 MB) |
| `Anim_Player_Run.fbx` | ACCEPTED-CLEAN | `Animations/Clips/` | — | — | — |
| `M_Floor_Stone_x.mat` | REJECTED-ERROR | — | — | — | `regex-mismatch`. Suggested: `M_FloorStone_Tile.mat` |

## Rename suggestions (REJECTED)

For each rejected file with a rename suggestion, show source + suggested + confidence:

| Source | Suggested | Confidence | Basis |
|---|---|---|---|
| `bonewarden_diff.png` | `T_PrimalBoneSkullCap_D.png` | high | matched `SO_Weapon_PrimalBoneSkullCap.asset` |
| `M_Floor_Stone_x.mat` | `M_FloorStone_Tile.mat` | low | `[ai-guess]` from tokens |

## Next steps

- **ACCEPTED**: run with `--apply` to move + label.
- **REJECTED**: rename source files per suggestions, then re-run scan.
- **WARNINGS**: review and decide whether to accept or fix at source.
```

## Sections always present

1. Header — timestamp + manifest sources + scanned count
2. Summary — triage counts
3. Per-file triage — full table
4. Rename suggestions — only if REJECTED count > 0
5. Next steps — actionable

## Confidence levels for rename suggestions

| Level | Criterion |
|---|---|
| `high` | Exact match against `contentKeys` source (e.g. ScriptableObject name) |
| `medium` | Token overlap >= 2 with a `contentKeys` candidate |
| `low` | `[ai-guess]` — derived from tokenizing the source filename only |

## Output channels

- Markdown to stdout (or `--out <path>`)
- Structured JSON triage to `--triage-out <path>` (consumed by `promote-assets.cjs`)

Both channels reflect the same data; markdown is for humans, JSON for the next phase.
