---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Unity MCP Skill — Incident Log

Dated reproductions of MCP gotchas surfaced in real cooks. Body rules live in `SKILL.md`; this file holds the field reports that validated them.

## Parallel teammates race on `Library/Artifacts/` moves — modal-blocking dialog

- **Date:** 2026-05-24
- **Project / session:** DOTS-AI ChaosForge session
- **Concurrency:** 3 parallel teammates issuing MCP asset operations
- **Symptom:** Unity opened the modal `"Moving file failed — Moving Library/TempArtifacts/Primary/<hash> to Library/Artifacts/<hash>/<hash>"` mid-cook
- **Resolution:** designate ONE MCP-actions owner per phase; other teammates do filesystem-only work

## Stale Roslyn / Mono compile cache lags behind source — DLL runs old IL

- **Date:** 2026-05-24
- **Project / session:** downstream consumer project; populator phase
- **Trigger:** enum-cast fix after many recompile cycles
- **Symptom:** source had the fix, `refresh_unity` succeeded with zero CS errors, runtime exception still cited pre-fix line numbers
- **Resolution:** `rm -rf Library/Bee/artifacts Library/ScriptAssemblies` + force-refresh, OR restart Editor

## `execute_menu_item` timeout doubled work — naive retry without verification

- **Date:** 2026-05-23
- **Project / session:** ChaosForge sprite-import phase
- **Trigger:** `execute_menu_item("Tools/Sprites/Generate All Characters")` timed out at 30s
- **Failure mode:** agent retried; second run double-registered all 7 Addressables entries; CI's no-duplicate-addresses gate failed
- **Resolution:** verify via filesystem mtime / scene-state before retrying; mtime check costs zero

## `manage_asset(action="reimport")` no-op on freshly-written `.cs` file

- **Date:** 2026-05-23
- **Project / session:** ChaosForge boss-authoring phase
- **Artifact:** `BossStormcallerZephyrixAuthoring.cs` (newly written)
- **Symptom:** `manage_asset(action="reimport")` returned success with `status: "Unknown", guid: null`; baker couldn't find the type for 25 min
- **Resolution:** switched to `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)`
- **Upstream gap:** file an MCP-gap issue against `The1Studio/unity-mcp` to auto-detect untracked files in `manage_asset(action="reimport")` or surface a clear error

## Multi-teammate MCP race silently kills the bridge — 8 concurrent teammates

- **Date:** 2026-05-24
- **Project / session:** sleep-run DOTS-AI ChaosForge cook
- **Concurrency:** 8 concurrent teammates issuing MCP calls
- **Symptom:** `set_active_instance` flipping between project and ephemeral package environments; `refresh_unity` returned `{ "status": "recovered" }` but DLL mtime never advanced; `read_console` empty for 25-45 min windows; four teammates deferred Gate 3/4 verification
- **Resolution:** designate ONE MCP-owner; other teammates work filesystem + Bash only; final sequential `dots-tester` runs Gates 3/4 after parallel work lands

## Linux/Wayland focus-steal — KDE window rule

- **Date observed:** focus-steal flip from `editor_is_focused: true → false` captured in an earlier session
- **Source:** Unity Editor calls (`AssetDatabase.Refresh()`, `CompilationPipeline.RequestScriptCompilation()`, Test Runner activation) grab focus on every script edit / refresh / run-tests
- **Resolution:** window-manager-level rule (KDE `~/.config/kwinrulesrc`, i3/Sway `for_window`, GNOME focus-mode); MCP package itself never calls `EditorWindow.Focus()`
