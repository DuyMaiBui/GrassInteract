---
phase: R6
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: S (~0.5 day)
unity instance: GrassInteract@de203215 (port 6403)
depends on: R5 complete
---

# R6 - Final Verification + Screenshot Diff vs Baseline

## Goal

Run the full verification sweep one last time across the migrated codebase. Confirm zero regressions vs the baseline established before R1. This phase ships nothing new; it confirms the refactor is invisible at runtime.

R6 is a verification-only phase. The subagent role here is light: walk every success-metric row from the plan, record the result.

## Scope

**IN:**
- Final pass of all 8 success-metric rows from plan.md.
- Final screenshot diff vs baseline.
- Cleanup notes for backups/ directory (keep one cycle; do NOT delete in R6).
- Final phase-6-report.md doubling as the plan close-out report.

**OUT:**
- Any code edit. R6 is verification-only.
- Deleting backups (keep through next plan cycle).
- Filing follow-up issues (those happen out of this plan via /t1k:issue).

## File Ownership

| File | Action | Notes |
|---|---|---|
| plans/scatter-layer-placement-split/phase-6-report.md | CREATE | Final report. Covers every success metric. |
| plans/scatter-layer-placement-split/screenshots/phase-6-render.png | CREATE | Final visual-parity screenshot. |

## Step-by-Step Tasks

1. **First MCP call:** `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"`.
2. **Refresh + console:** `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)` -> `read_console(types=[Error, Warning], count=50)` (count=50 for the final sweep; 30 in earlier gates).
3. **Run parity harness:** `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"`. Read console for `[Parity]` lines.
4. **Run any other harness still on disk:** if ScatterInstanceCullHarness has been re-introduced or any other Tools/GrassInteract/Self-Test/* menu items exist, run each. Note results.
5. **Final screenshot:** game-view -> plans/scatter-layer-placement-split/screenshots/phase-6-render.png.
6. **Visual diff:** compare phase-6-render.png with plans/authored-instance-scatter-editor/screenshots/phase-5-before.png. Manual eyeball at minimum; if a pixel-diff harness exists, use it. Record result.
7. **Asset-presence final check:** read demo TerrainScatterConfig .asset YAML. Confirm sub-asset m_Script guid = DensityScatterLayer.cs MonoScript GUID.
8. **Grep gate sweep (write all results to report):**
   - `grep -rn "HasAuthoredInstances" Assets/ Packages/` -> 0 hits.
   - `grep -rn "\[Obsolete\].*targetInstances" Assets/` -> 0 hits.
   - `grep -rn "FormerlySerializedAs.*targetInstances" Assets/` -> 0 hits.
   - `grep -rn "pragma warning disable 0618" Assets/` -> 0 hits in ScatterLayer.cs (other files OK if pre-existing).
   - `grep -rn "BuildFromAuthored" Assets/` -> 0 hits.
9. **Success-metric matrix:** walk every row from plan.md Success Metrics and mark PASS / FAIL with evidence.

| Metric | Result | Evidence |
|---|---|---|
| Compile clean after each phase | ? | read_console output |
| Demo asset migrates without data loss | ? | R3 backup diff |
| Demo renders visually identical | ? | phase-6-render.png vs phase-5-before.png |
| ScatterFieldRebuildLayerHarness PASS | ? | console |
| HasAuthoredInstances references | ? | grep |
| [Obsolete] targetInstances references | ? | grep |
| #pragma warning disable 0618 blocks | ? | grep |
| Demo TerrainScatterConfig sub-asset type | ? | YAML m_Script GUID |

10. **Write phase-6-report.md** with the matrix filled in, plus a brief "next steps" section noting backups retention.

## Verification Gate (R6 verifies itself - all 6 standard gate items collapse into the tasks above)

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` - FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=50)` - 0 NEW project errors.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` - no `[Parity] ERROR`.
5. Screenshot -> `plans/scatter-layer-placement-split/screenshots/phase-6-render.png` - visual parity vs `plans/authored-instance-scatter-editor/screenshots/phase-5-before.png`.
6. **Asset-presence:** demo TerrainScatterConfig sub-asset = DensityScatterLayer.

## Exit Criteria

- All 8 success metrics PASS.
- phase-6-report.md written with the full matrix + final screenshot.
- All 5 grep gates clean.
- Visual diff vs baseline: identical (or within 1-pixel anti-alias noise).

## Rollback Plan

R6 makes no code edits. If a gate fails:
- Identify which earlier phase introduced the regression via the per-phase reports.
- Loop back to that phase (R3 / R4 / R5 most likely).
- R6 itself does not produce rollback artifacts.

## Anti-Stall Guard Reminders

- **First MCP call = set_active_instance unity_instance="GrassInteract@de203215".**
- **No edits.** R6 is verification-only. If you find yourself wanting to fix a small thing, STOP and document it in phase-6-report.md as a "follow-up needed" entry; do NOT silently fix.
- **No progress narration during gate runs.** Run the harness, read the console, record the result.
- **150K commit checkpoint.** Unlikely to hit in R6 (verification-only) but the discipline applies: if approaching ~150K, finalize the report with what is verified and exit.
- **No Unity restart, no Assets/Reimport All.** refresh_unity only.

## Plan Close-Out

After phase-6-report.md ships and all gates pass:

- The plan is COMPLETE.
- Backups under plans/scatter-layer-placement-split/backups/ are RETAINED for one cycle - delete only after the next major plan (or after one week of confirmed stable demo).
- The brainstorm note's "Future Extensibility" hook is now live: adding Poisson or runtime-stream placement is one new ScatterLayer subclass + one new IScatterPlacement implementation.

Handoff back to user: confirm R6 results and close.
