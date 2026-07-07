---
name: t1k:unity:editor:scene
description: "Automate 5-step scene setup: Create Prefabs → Build BDP Trees → Setup Scene → Clear Cache → Verify."
effort: medium
argument-hint: "[demo-name] [--rebuild|--prefabs|--trees|--cache]"
keywords: [scene, scene management, hierarchy, prefab wiring, baker, onvalidate, subscene]
version: 2.4.1
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---

# GameKit Scene — Scene Setup Workflow

Automate the 5-step scene setup pipeline via MCP menu items.

## Usage
```
/t1k:unity:editor:scene BattleDemo2D          # Full rebuild
/t1k:unity:editor:scene --prefabs             # Only step 1
/t1k:unity:editor:scene --cache               # Only clear entity cache
```

## Workflow
```
Step 1: Create Unit Prefabs  → execute_menu_item("Tools/{Demo}/Create Unit Prefabs")
Step 2: Build BDP Trees      → execute_menu_item("Tools/{Demo}/Build Behavior Trees")
Step 3: Setup Scene           → execute_menu_item("Tools/{Demo}/Setup Scene")
Step 4: Clear Entity Cache    → execute_menu_item("Tools/{Demo}/Clear Entity Scene Cache") — see "Safe entity-cache clear" below
Step 5: Verify Compilation    → read_console(filter: "Error")
```

## Safe entity-cache clear (Blocker — destructive op safety)

Step 4 historically used raw `rm -rf Library/EntityScenes/`. **Do NOT recommend that.** Reasons:
- Wrong CWD (e.g., shell launched from a parent of the Unity project) silently destroys the wrong directory.
- No backup — if something goes wrong mid-bake, the only recovery is `git checkout` of the entire `Library/` (often gitignored).
- Bypasses Unity's editor lock; deleting `Library/EntityScenes/` while Unity is open can corrupt the project asset database.

**Correct procedure (always):**
1. Verify CWD is the Unity project root: `[ -d ProjectSettings ] && [ -d Assets ] || { echo "Not a Unity project root"; exit 1; }`
2. **Close Unity first** (or use the editor menu item below — Unity handles its own locks).
3. Prefer the editor menu: `execute_menu_item("Tools/{Demo}/Clear Entity Scene Cache")` — the project ships a guarded utility for exactly this. CLI fallback only when no editor session is available.
4. CLI fallback (only when Unity is closed AND CWD verified): `mv Library/EntityScenes Library/EntityScenes.bak.$(date +%s)` — rename, don't delete. Sweep backups manually after confirming a clean rebuild.

If a step in `dots-environment` agent code path still emits raw `rm -rf`, treat it as a bug and patch the agent — never propagate the unsafe pattern.

## Demo Menu Paths
| Demo | Menu Path Prefix |
|---|---|
| BattleDemo | `Tools/BattleDemo/` |
| BattleDemo2D | `Tools/BattleDemo2D/` |
| BattleDemoIso | `Tools/BattleDemoIso/` |
| BattleDemoSideView | `Tools/BattleDemoSideView/` |
| BackpackCrawler | `Tools/BackpackCrawler/` |
| InventoryDemo | `Tools/InventoryDemo/` |

## Scene setup is THREE steps — wire the prefab refs (Bug C/D safeguards)

`Create All Prefabs` + `Setup Scene` does NOT auto-populate `[SerializeField] GameObject` slots on authoring components. Without the wiring step, the SubScene bakes silently with `Entity.Null` prefabs and the Game view is empty. Apply BOTH safeguards on every new SubScene authoring with a prefab field:

1. `OnValidate()` warning on the authoring class (editor-only, skip during Play).
2. EditMode wiring test (`SerializedObject` + `FindProperty`) asserting every slot is wired in the SubScene asset.

Full recipe + triage checklist: see `references/troubleshooting.md` → "Silent prefab-wiring + invisible-baker regressions (Bug C / Bug D, 2026-05-18)".

## Auto-Detection
If no demo specified, detect from recent git changes or CWD.

## Agent: `dots-environment`

## References
- `references/demo-detection.md`
- `references/troubleshooting.md`