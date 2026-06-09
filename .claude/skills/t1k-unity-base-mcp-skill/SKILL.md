---
name: t1k:unity:base:mcp-skill
description: Orchestrate Unity Editor via MCP tools — GameObjects, scripts, scenes, assets, tests, cameras, graphics, packages. Best practices and workflow patterns for Unity-MCP integration.
effort: high
context: fork
keywords: [MCP, unity MCP, tool, bridge]
version: 2.2.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity-MCP Operator Guide

Use Unity Editor via MCP (Model Context Protocol) tools. Always read relevant resources before using tools.

## Template Notice

Examples in `references/` are reusable templates. Validate targets/components first; treat names, enum values, and property payloads as placeholders to adapt.

## ⚠️ Step 0 — Multi-Instance Targeting (MANDATORY when >1 editor may be open)

**Before ANY MCP tool/resource call, confirm which Unity project you are connected to.** When two or more editors are open, the bridge exposes ALL of them and a call with no pinned active instance is ambiguous — the server errors, OR (worse, in some builds) silently routes to the wrong project, producing baffling symptoms like an **empty scene hierarchy / empty entity query in edit mode** that look like a real bug but are just "you queried the other game."

Pre-flight every session (and re-run if you suspect drift):

```
1. List instances   → read resource  mcpforunity://instances
2. Pin the target   → set_active_instance(instance="<ProjectName>@<hash>")   # or hash prefix / port
3. VERIFY the pin    → read resource  mcpforunity://project/info
                       └─ assert data.projectRoot / projectName is the project you intend
```

Rules:
- **Never assume a single instance.** Even if you only opened one, a teammate/another session may have a second editor open. Always list first.
- **The pin is `set_active_instance` (session-global), or pass `unity_instance="<hash>"` per-call** to route just one call without changing the session default.
- **Re-verify after any long gap or after a reported "empty/null" result** — if a query that should return data comes back empty, your FIRST hypothesis is "wrong instance / lost pin", not "the data is gone". Re-read `mcpforunity://project/info` before debugging the supposed bug.
- **Symptom cheat-sheet:** empty `find_gameobjects`, empty entity queries, "scene has no objects", or edits landing in a project you didn't touch → check the active instance FIRST.
- Instances differ by `path`, `hash`, and `port` (e.g. DOTS-AI on 6400 vs another game on 6401). Match on `path` containing your project root, not on name alone.

## Resource-First Workflow

```
0. Pin + verify instance  → mcpforunity://instances → set_active_instance → mcpforunity://project/info   (see Step 0 above)
1. Check editor state     → mcpforunity://editor/state
2. Understand the scene   → mcpforunity://scene/gameobject-api
3. Find what you need     → find_gameobjects or resources
4. Take action            → tools (manage_gameobject, create_script, etc.)
5. Verify results         → read_console, capture_screenshot, resources
```

## Critical Best Practices

**After writing/editing scripts — always refresh and check console:**
```python
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
read_console(types=["error"], count=10, include_stacktrace=True)
```

## ⛔ Forbidden — `Assets/Reimport All`

**NEVER call `execute_menu_item(menu_path="Assets/Reimport All")` or any equivalent (`AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)`, etc.).**

Why it is forbidden:
- Triggers a from-scratch reimport of every asset in the project (textures, models, shaders, scripts, audio, prefabs). On a non-trivial project this is **30+ minutes** of CPU/GPU work and **multiple shader compiler workers** spawn.
- **Cannot be cancelled from MCP** once issued — Unity processes the queue regardless of subsequent commands. The MCP bridge typically goes unresponsive while the reimport runs.
- The legitimate reason to use it (rebuilding stale Burst caches or recovering from a corrupted asset DB) is satisfied by the **targeted** alternatives below at a fraction of the cost.

When you think you need Reimport All, you actually want one of these:

| Goal | Correct command |
|---|---|
| Recompile scripts after edits | `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` |
| Re-bake DOTS SubScenes after entity-type changes | `rm -rf Library/EntityScenes/` then enter Play mode |
| Clear stale Burst cache after type changes | `rm -rf Library/BurstCache Library/Bee/artifacts Library/ScriptAssemblies` then `refresh_unity` |
| Reimport ONE asset (texture, model, prefab) | `manage_asset(action="reimport", path="Assets/...")` |
| Recover from "everything looks broken" | Restart the Unity Editor (faster than Reimport All) |

**Hard rule:** The only path to legitimately calling Reimport All is (1) the user issues a direct order to do so, OR (2) a referenced document explicitly requires it for a specific recovery procedure. If neither applies, do not call it. If you are about to call it, stop and choose one of the targeted alternatives above.

**Use `batch_execute` for multiple operations (10–100x faster):**
```python
batch_execute(commands=[...], parallel=True)  # Max 25 per batch (configurable, max 100)
```

**Screenshots for visual verification:**
```python
manage_scene(action="screenshot", include_image=True, max_resolution=512)
manage_scene(action="screenshot", batch="surround", look_at="Player", max_resolution=256)
```

**Always check `editor_state` before complex operations** — wait if `is_compiling=true` or `is_domain_reload_pending=true`.

## 🔧 Install / Re-Install — Always The1Studio Fork on `beta`

UnityMCP must be registered from the The1Studio fork on the `beta` branch — never the upstream PyPI package `mcpforunityserver`, never any other branch. This matches the Unity-side UPM submodule (`Packages/com.coplaydev.unity-mcp/`), which is pinned to the same fork+branch.

**Canonical install (user scope so it survives across projects):**

```bash
# Idempotent — clear any prior bad config in either scope first.
claude mcp remove UnityMCP -s user  || true
claude mcp remove UnityMCP -s local || true

claude mcp add UnityMCP -s user -- \
  uvx --from "git+https://github.com/The1Studio/unity-mcp.git@beta#subdirectory=Server" \
  mcp-for-unity
```

**Always verify after install** (both commands, same turn):

```bash
claude mcp get UnityMCP   # expect: ✓ Connected, Scope: User config, Args: --from git+https://github.com/The1Studio/unity-mcp.git@beta#subdirectory=Server mcp-for-unity
claude mcp list | grep -i unity  # expect: ✓ Connected
```

If `claude mcp get` shows ANY of the following, the install is wrong — remove and re-add:

- `mcpforunityserver` anywhere in args → upstream PyPI package, banned.
- `--offline` flag → the Editor's "Configure All Detected Clients" button wrote this; permanent connect failure if the package isn't cached.
- `Scope: Local config` instead of `User config` → the Editor's Configure button writes local; user scope is required.
- Branch other than `beta` after `@` (e.g., `@master`) → the submodule pins `beta`.

### ⛔ Forbidden installation paths

| Path | Why banned |
|---|---|
| MCP For Unity Editor panel → **"Configure All Detected Clients"** button (Claude Code client) | Writes `uvx --offline --prerelease explicit --from "mcpforunityserver>=0.0.0a0" mcp-for-unity` to local scope. `--offline` + uncached package = unrecoverable connect failure (documented 2026-05-18). |
| MCP For Unity Editor panel → **"Install Skills"** button | Same broken config as above. |
| `uvx --from mcpforunityserver ...` (any version, any flag combination) | Upstream registry package — not validated against the The1Studio fork's tool patches. |
| Any branch of `The1Studio/unity-mcp` other than `beta` | The Unity-side submodule pins `beta`; the Python server must match. |
| Any third-party fork of `unity-mcp` | Only the The1Studio fork carries our tool patches. |

If the Editor panel shows `Configured` with Claude Code but `claude mcp get` shows the broken PyPI command, click the panel's **`Unregister`** button, then re-run the canonical install commands above. NEVER click "Install Skills" or "Configure All Detected Clients" to recover — those re-write the broken config every time.

## 🔍 When MCP doesn't respond — diagnose Unity status WITHOUT MCP

A failed MCP call (timeout, "Unity is reloading", "No Unity Editor instances") is almost never a bridge connectivity problem. **It usually means Unity's main thread is busy** (compiling, importing, baking, domain-reloading, lightmapping, etc.) and the bridge thread is blocked behind it. Asking the user to "Start Session" is the wrong escalation — the bridge IS connected, Unity just isn't yet ready to service requests.

Before asking the user for help, run these CLI probes to see what Unity is actually doing:

| Signal | Command | What it means |
|---|---|---|
| Editor process alive | `pgrep -af "Unity.*<projectname>"` | Editor PID + count of `AssetImportWorker*` children. >2 workers = active import. |
| Lock file present | `ls Temp/UnityLockfile` | File exists → editor is running on the project. Absent → editor closed. |
| Live import activity | `tail -10 Logs/AssetImportWorker0.log` | "Importing X" / "Reloading scripts" = busy. "shutdown with reason: Scaling down because of idle timeout" = idle. |
| Compile recency | `stat Library/ScriptAssemblies/<assembly>.dll` | mtime = last successful compile. If older than your most recent script edit → still compiling. |
| Bee build state | `ls Library/Bee/artifacts/` count | Growing count = active build. Stable count + recent mtime = build settling. |
| Domain reload | `tail Logs/AssetImportWorker0.log \| grep -i "reload"` | "Begin MonoManager ReloadAssembly" = reloading; "End MonoManager" = done. |

**On Linux, `Logs/Editor.log` does NOT exist at this path** — Unity 6 puts the equivalent main-editor log somewhere else (or only writes to stderr). Use `Logs/AssetImportWorker*.log` instead; they tail the same compile/import activity from the worker side.

### Recovery decision tree

1. MCP call fails → run probes above.
2. Editor process exists + workers active → Unity is BUSY. Wait. Retry `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` after Unity reports ready — do not orchestrate DLL-mtime polling loops in shell.
3. Editor process exists + no workers + DLLs are recent + lock file present → bridge socket is genuinely dropped. Ask user to click `Window > MCP for Unity > Start Session`.
4. Editor process gone + lock file gone → user closed Unity. Ask them to reopen.
5. Editor process gone + lock file present → Unity crashed. Ask user to relaunch.

**Never** ask the user to click "Start Session" before completing step 1. False escalation breaks user trust and wastes their time. Prove the bridge is dead by elimination, not by assumption.

## Core Tool Categories

| Category | Key Tools |
|----------|-----------|
| Scene | `manage_scene`, `find_gameobjects` |
| Objects | `manage_gameobject`, `manage_components` |
| Scripts | `create_script`, `script_apply_edits`, `manage_script`, `refresh_unity` |
| Assets | `manage_asset`, `manage_prefabs`, `manage_material`, `manage_texture` |
| Editor | `manage_editor`, `execute_menu_item`, `read_console` |
| Testing | `run_tests`, `get_test_job` |
| Batch | `batch_execute` |
| Camera | `manage_camera`, `manage_cinemachine` |
| Graphics | `manage_graphics`, `manage_render_pipeline`, `manage_shader` |
| Packages | `query_packages`, `manage_packages` |
| ProBuilder | `manage_probuilder` |
| UI | `manage_ui`, `manage_ui_toolkit` |
| DOTS | `manage_dots`, `manage_dots_graphics`, `manage_dots_physics`, `manage_dots_subscene` |
| Physics | `manage_physics`, `manage_physics2d` |
| Navigation | `manage_navigation` |
| Media | `manage_animation`, `manage_audio`, `manage_video`, `manage_vfx`, `manage_timeline` |
| World | `manage_terrain`, `manage_tilemap`, `manage_splines`, `manage_lighting`, `manage_mesh` |
| Systems | `manage_addressables`, `manage_build`, `manage_input_system`, `manage_localization`, `manage_netcode` |
| AI | `manage_behavior`, `manage_asset_hunter` |
| Performance | `manage_profiler`, `rendering_stats`, `validation_snapshot` |
| Code | `manage_scriptable_object`, `find_in_file` |

→ See reference files below for full parameter schemas and examples.

## Common Workflows

→ See `references/workflow-script-lifecycle.md`, `references/workflow-scene-objects.md`, `references/workflow-testing.md`, `references/workflow-assets-prefabs.md`, `references/workflow-batch-operations.md`, `references/workflow-camera-probuilder.md`, `references/workflow-ui-creation.md`, `references/workflow-ui-advanced.md` for extended patterns.

**Quick patterns:**
```python
# New script → attach:
create_script(path="Assets/Scripts/Foo.cs", contents="...")
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
manage_gameobject(action="modify", target="Player", components_to_add=["Foo"])

# Run tests (async):
result = run_tests(mode="EditMode")
get_test_job(job_id=result["job_id"], wait_timeout=60, include_failed_tests=True)
```

## Parameter Type Conventions

- Vectors: `position=[1,2,3]` or `"[1,2,3]"` (both accepted)
- Colors: `[255,0,0,255]` (0–255) or `[1.0,0,0,1.0]` (normalized, auto-converted)
- Paths: `"Assets/Scripts/Foo.cs"` (Assets-relative) or `"mcpforunity://path/..."` (URI)

→ See `references/error-recovery-guide.md` for error recovery table, auto-start setup, cache gotchas, and the 6-step asset refresh hierarchy.

## Reference Files
| File | Contents |
|------|----------|
| `references/tools-scene-objects.md` | manage_scene, find_gameobjects, manage_gameobject, manage_components |
| `references/tools-scripts-assets.md` | create_script, script_apply_edits, manage_asset, manage_prefabs, materials |
| `references/tools-editor-testing.md` | manage_editor, execute_menu_item, read_console, run_tests, find_in_file |
| `references/tools-camera-graphics.md` | manage_camera (all tiers), manage_graphics |
| `references/tools-batch-packages.md` | batch_execute, set_active_instance, query_packages, manage_packages, manage_ui |
| `references/tools-probuilder.md` | manage_probuilder (all actions, known bugs) |
| `references/workflow-script-lifecycle.md` | Create, edit, attach, validate C# scripts |
| `references/workflow-scene-objects.md` | Fresh builds, grids, clone/arrange, physics triggers |
| `references/workflow-testing.md` | Run tests, TDD, diagnose errors, domain reload recovery |
| `references/workflow-assets-prefabs.md` | Materials, textures, folder structure, prefab workflows |
| `references/workflow-camera-probuilder.md` | Camera setup, Cinemachine, ProBuilder scene building |
| `references/workflow-batch-operations.md` | Mass operations, multi-instance, input systems, pagination |
| `references/workflow-ui-creation.md` | UI Toolkit, uGUI Canvas, RectTransform, EventSystem |
| `references/workflow-ui-advanced.md` | Slider, Toggle, Input Field, Layout Group, TMP alignment |
| `references/tools-dots-physics-nav.md` | manage_dots, manage_dots_graphics/physics/subscene, manage_physics/2d, manage_navigation, manage_mesh |
| `references/tools-media-world.md` | manage_animation, manage_audio, manage_video, manage_vfx, manage_timeline, manage_terrain, manage_tilemap, manage_splines, manage_lighting |
| `references/tools-systems-code.md` | manage_addressables, manage_build, manage_input_system, manage_localization, manage_netcode, manage_script, manage_scriptable_object, manage_shader |
| `references/tools-perf-ai-misc.md` | manage_profiler, rendering_stats, manage_cinemachine, manage_render_pipeline, manage_behavior, manage_asset_hunter, validation_snapshot, manage_ui_toolkit |
| `references/error-recovery-guide.md` | Error diagnosis table, auto-start setup, cache gotchas, asset refresh hierarchy |
| `references/upm-gotchas.md` | UPM diagnostic recipes: duplicate-scope registry failure, embed-override pattern, submodule GUID conflict, `refresh_unity` timeout vs bridge disconnect |
| `references/workflow-scriptableobject-creation.md` | Custom ScriptableObject .asset creation — manage_asset limitation, execute_code Windows path-length failure, direct YAML write fallback |

## Gotchas

- **Prefer direct file edits over Unity APIs that may prompt the user.** When you have a choice — and you usually do — modify the underlying asset on disk yourself (YAML `.asset`, JSON, XML, CSV, TextAsset payloads) and then call `AssetDatabase.Refresh()` instead of going through Unity APIs that pop confirmation dialogs (`EditorUtility.DisplayDialog`, `EditorUtility.DisplayDialogComplex`, save / open file panels), modal progress bars, "Are you sure?" prompts on `PlayerSettings` mutations, or any importer that asks "Apply / Revert" on focus loss. Why: every interactive prompt freezes Unity's main thread, freezes the MCP bridge, and turns an autonomous agent run into a "please click this for me" round-trip with the user. Concrete pattern: to flip a value inside a ScriptableObject under version control, prefer `File.ReadAllText` → string-replace the YAML field → `File.WriteAllText` → `AssetDatabase.Refresh()` over `Load → SetDirty → SaveAssets` paths in YOUR populator/migration code that include `DisplayDialog` confirmations. Same principle for `ProjectSettings/*.asset`, `Packages/manifest.json`, addressables `.asset` group settings, and any `[ScriptedImporter]` output. When you DO write editor code that mutates data, gate any `DisplayDialog` behind an `Application.isBatchMode` check, or replace it with a `Debug.LogWarning` plus a `--force` style explicit confirm-flag in the menu item name (e.g., `Tools/X/Repopulate (force overwrite)`).
- **ANY open editor modal blocks ALL MCP calls — not just the one that opened it.** `EditorUtility.DisplayDialog`, `EditorUtility.DisplayDialogComplex`, `EditorUtility.SaveFilePanel`, `EditorUtility.OpenFilePanel`, and progress bars (`EditorUtility.DisplayProgressBar` left un-cleared) all run on Unity's main thread. While that thread is parked on the modal, EVERY subsequent MCP call — `refresh_unity`, `read_console`, `editor/state` resource, `execute_menu_item`, `run_tests`, `manage_scene`, etc. — times out with `MCP error -32001: Request timed out`. **The dialog does NOT have to be opened by the MCP call you're currently making** — a leftover modal from a prior `execute_menu_item`, OR a modal opened by editor code reacting to a file change (e.g., an `[InitializeOnLoad]` script, a `ScriptedImporter`, an `AssetPostprocessor`, or YOUR OWN populator that runs on first save) all stall the bridge identically. **Diagnosis pattern (3 signals together)**: (1) MCP calls return `Request timed out` on multiple consecutive different tools (not just the one that "caused" it); (2) AssetImportWorker processes are alive (`pgrep -af "AssetImportWorker.*<project>"`) but `Library/ScriptAssemblies/<assembly>.dll` mtime does NOT advance; (3) `Logs/AssetImportWorker0.log` shows no new work. **Recovery (you cannot dismiss the modal from MCP — the modal IS the blocker)**: ask the user to look at the Unity Editor window and click the dialog button (Cancel/Overwrite/OK). Then re-establish the MCP bridge via instances poll. **Prevention** in editor code you author: never call `EditorUtility.DisplayDialog` from a `MenuItem` that an agent is expected to invoke headlessly — gate dialogs behind an `Application.isBatchMode` check or use `Debug.LogWarning` + an explicit `--force` confirm-flag pattern instead.
- **Modal-blocked `execute_menu_item` is the narrow case of the above.** Same root cause; the menu DID fire, but downstream work is gated behind a UI prompt. **Recovery**: use `execute_code` to invoke the menu's underlying static method via reflection (most editor tools delegate to a `private static` worker that bypasses any UI gate), OR bootstrap whatever prerequisite the modal is complaining about and then re-run the menu item.
- **Addressables setup chicken-and-egg.** Any project-side `Tools/Addressables/Create*` menu item requires `AddressableAssetSettings.asset` to exist FIRST. If missing, the migration tool opens a modal ("Addressables not initialized — Open Window > Asset Management > Addressables > Groups and click 'Create Addressables Settings'") and the MCP call times out. Bootstrap canonically via `execute_code`:
  ```csharp
  var t = System.AppDomain.CurrentDomain.GetAssemblies()
      .SelectMany(a => a.GetTypes())
      .First(x => x.FullName == "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
  t.GetMethod("GetSettings", new[] { typeof(bool) }).Invoke(null, new object[] { true });
  UnityEditor.AssetDatabase.SaveAssets();
  ```
  Then re-run the migration menu item. **The `manage_addressables` MCP tool has actions `list_groups | get_group | list_entries | get_entry | list_labels | build | analyze` but no `init` / `create_settings` — file an MCP-gap issue if the team wants this.**
- **Editor MCP server runs in the same process as the editor** — a tool that hangs hangs the editor; never call long-running operations synchronously.
- **Scene mutations via MCP must be wrapped in `Undo.RecordObject` for Ctrl+Z support** — MCP edits without Undo are silently lost on Ctrl+Z.
- **MCP tool result size cap (`maxResultSizeChars`) is per-tool** — unbounded scene exports blow context window.
- **Tool schema can be newer than the active Unity package** — if a tool action from the client schema returns `Unknown action`, fall back to the matching resource or supported action list from the error instead of retrying the same action.
- **`run_tests` via MCP injects FALSE failures into log-asserting tests (bridge-disposal artifact).** A large-suite `run_tests` triggers a domain reload at the start of the run; during that reload the MCP bridge's socket briefly drops and logs `[Error] MCP-FOR-UNITY: Client handler error: Cannot access a disposed object. Object name: 'System.Net.Sockets.NetworkStream'` (sometimes surfacing as `InvalidOperationException: Collection was modified` from the bridge's handler). Unity's NUnit global log-assertion attributes that editor-emitted Error to **whatever test is in `SetUp`/`OneTimeSetUp` at that instant**, failing it with a message that is NOT one of the test's own assertions. **Signature (how to recognize it is NOT a code bug):** (1) the failure message contains `MCP-FOR-UNITY` / `NetworkStream` / `disposed` / `Collection was modified`, never a real `Assert.*` diff; (2) it lands on a **different random test each run** (e.g. CombatStripBackdrop one run, RespawnReadyCleanup the next); (3) it correlates with an `instance_count:0` or transient `UnityPackagesEnvironment@...` flicker in `mcpforunity://instances` mid-run. (4) Tests whose `SetUp` does `new GameObject` + `AddComponent` of a trivial POCO are common victims — they have no logic that can fail. **Verify-green WITHOUT closing the editor (preferred — batchmode requires the exclusive project lock so you'd have to close the editor; this avoids that):** run **per-assembly or in small batches** (`run_tests assembly_names=[...]`) instead of the whole suite in one shot. A tight per-assembly reload rarely coincides with a log-asserting `SetUp`, so isolated runs come back clean. To prove a full-suite run was green: re-run ONLY the assemblies that showed artifact-failures, in isolation — they pass, confirming every genuine test passed. A single full-suite run with `failures_capped:false` whose entire failure set is the bridge artifact + clean isolated re-runs of those assemblies = conclusive green. **Do NOT "fix" victim tests** with `LogAssert.Expect`/`ignoreFailingMessages` — the tests are fine; the bridge is the noise source, and it would never fire in CI/batchmode (no bridge).
- **Prompting / interaction-requiring actions to AVOID (no autonomous use — they freeze the bridge).** Use the alternatives in the right column unless the user explicitly says "I'll click it."

| ❌ Avoid (needs human click) | ✅ Use instead |
|---|---|
| `EditorUtility.DisplayDialog` / `DisplayDialogComplex` in agent-invoked code | `Debug.LogWarning` + a `--force` confirm-flag in the menu name (`Tools/X/Repopulate (force)`); or gate behind `if (Application.isBatchMode)` |
| `EditorUtility.DisplayCancelableProgressBar` left un-cleared | Always `EditorUtility.ClearProgressBar()` in `finally`; for headless flows, just `Debug.Log` progress |
| `EditorUtility.SaveFilePanel` / `OpenFilePanel` / `SaveFolderPanel` / `OpenFolderPanel` | Hard-code the path in the MenuItem (or accept it via a serialized config) — the populator pattern in `BalanceCsvImporter.cs` |
| `EditorUtility.SaveFilePanelInProject` | Direct `AssetDatabase.CreateAsset(obj, "Assets/path/...")` with the path baked into the populator |
| `AssetDatabase.ImportPackage(path, interactive: true)` | `AssetDatabase.ImportPackage(path, interactive: false)` — the `false` overload is silent |
| `File / Save As…` menu item | Direct file write + `AssetDatabase.Refresh()` |
| `File / Build And Run` / `File / Build Settings…` → "Build" button (Save Panel) | `BuildPipeline.BuildPlayer(...)` directly via `execute_code` with explicit `locationPathName` |
| `Edit / Preferences…` (modal window) | Edit `~/.config/unity3d/Editor.prefs` / `EditorPrefs.SetX(...)` via `execute_code` |
| `Assets / Reimport All` | **Banned** per `rules/unity-forbidden-operations.md`. Single-asset `manage_asset(action="reimport")` or targeted `Library/{BurstCache,ScriptAssemblies,Bee/artifacts}` rm |
| `Assets / Open C# Project` (IDE launch popup) | Don't invoke; the agent edits files directly anyway |
| `Window / Package Manager` → install via UI | Edit `Packages/manifest.json` directly + `AssetDatabase.Refresh()` |
| Entering Play mode with compile errors (modal "Cannot enter Play Mode") | Always `read_console types=["error"]` and assert empty BEFORE `manage_editor(action="play")` |
| `EditorApplication.Quit()` while scenes are dirty ("Save changes?") | **Banned** per `rules/unity-forbidden-operations.md` — never quit Unity from the agent |
| `PlayerSettings.colorSpace = Linear/Gamma` (prompts "Restart required") | Set via `execute_code` then warn user to restart manually if needed — do NOT call `EditorApplication.Exit` |
| `PlayerSettings.Android.keystorePass = ...` setters (some pop a password prompt) | Set via `execute_code` only when the value comes from `EditorPrefs` already (don't pop a fresh prompt) |
| `EditorSceneManager.SaveOpenScenes()` after a structural change (may prompt per-scene) | `EditorSceneManager.SaveScene(scene, path, saveAsCopy: false)` per scene with explicit path |
| Addressables UI buttons ("Configure All Detected Clients", "Install Skills") | Banned per `rules/unitymcp-the1studio-fork-only.md` for the MCP client itself; for Addressables groups use `AddressableAssetSettings.CreateGroup(...)` via `execute_code` |
| MK Toon Shader auto-configure popup on first import | Pre-create the config asset, or live with the noise (third-party — read-only) |
| `Unity Hub` update / sign-in dialogs | Never invoked from the agent; ignore if surfaced |

**Detection rule of thumb:** if the API name contains `Dialog`, `Panel`, `Picker`, `Wizard`, `Prompt`, `Confirm`, `Browse`, or `Show`, assume it pops a modal. If the API takes an `interactive: bool` overload, always pass `false` from agent code. If a third-party tool's `[MenuItem]` invokes any of the above, treat the whole menu item as unsafe for `execute_menu_item` and reach for `execute_code` against the underlying static method instead.
- **UPM "Registry configuration is invalid" / mass package-missing / GUID conflicts / `refresh_unity` timeout** → all four have surgical fixes documented in `references/upm-gotchas.md`. Always grep the Editor log for `"defined by more than one registry"` and `pgrep AssetImportWorker` BEFORE concluding "the bridge is broken" or "every package is missing."
- **Parallel teammates issuing MCP asset operations race on `Library/Artifacts/` moves.** Symptom: Unity opens a modal `"Moving file failed — Moving Library/TempArtifacts/Primary/<hash> to Library/Artifacts/<hash>/<hash>"` with [Try Again / Force Quit / Cancel] buttons. Root cause: Unity's `AssetImportWorker` processes hold filesystem locks while moving freshly-imported artifacts from `TempArtifacts/` to `Artifacts/`; when two MCP-triggered imports race on the same artifact (or its parent directory), the OS-level move fails with `EBUSY` / "file in use" and Unity falls back to the blocking dialog. Frequency rises sharply with teammate count: 1 active MCP teammate = safe; 2 = occasional; 3+ = frequent. **Recovery (user must act on the modal — agent cannot dismiss it via MCP)**: click **Try Again** first (usually clears within seconds as the other worker finishes); if Try Again fails 5+ times, click **Cancel** (aborts THIS one artifact move — Unity re-imports on next refresh, recoverable). **NEVER click Force Quit** — per `rules/unity-forbidden-operations.md` killing Unity loses unsaved scenes/prefabs/Inspector state. Reopening Unity is NOT needed; the dialog blocks the main thread but Unity itself is fine. **Prevention (agent-side)**: when running multiple teammates in parallel via `TeamCreate`, designate ONE teammate at a time as the MCP-actions owner. Other teammates can do file Reads, Grep, Bash, and analysis, but should NOT issue `refresh_unity` / `execute_menu_item` / `manage_asset` / `manage_prefabs` / asset-mutation MCP calls until the active MCP-owner finishes. Alternatively, serialize MCP-heavy phases in waves rather than concurrent fan-out. The race window aligns with Unity's domain-reload + asset-import cycles, which any MCP write triggers.
- **Stale Roslyn/Mono compile cache can lag behind source after many recompile cycles.** Symptom: edit a `.cs` file, source change is visible, `refresh_unity(mode="force", scope="scripts", compile="request")` completes, `read_console` reports zero CS errors, but the **DLL still executes the OLD code path** — runtime exception stack traces point to OLD line numbers, or decompilation of the loaded DLL shows old IL. Root cause: Unity's incremental compile cache (Roslyn + `Library/Bee/artifacts/`) occasionally fails to invalidate one compilation unit even though source mtime advanced. No error is logged — the cache is technically valid, just stale. **Diagnosis signals (all three together)**: (1) runtime exception cites the OLD line numbers / OLD code path, (2) source has the fix but `Library/ScriptAssemblies/<asmdef>.dll` shows recent mtime, (3) the fix IS in the latest git commit. **Recovery**: `rm -rf Library/Bee/artifacts Library/ScriptAssemblies` then `refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=true)` forces a full re-link. Alternatively, ask the user to restart the Unity Editor — cold-start domain reload always picks up fresh DLLs. **Anti-pattern**: do NOT call `Assets/Reimport All` — same recovery at 30+ min cost (banned per `rules/unity-forbidden-operations.md`). **When NOT to nuke the cache**: don't reach for this on every Burst error or test failure — only when the source/DLL-IL divergence is verified (e.g., the new fix is committed, the source line matches the expected behavior, but the runtime trace still cites the pre-fix line). Random cache-nukes cost 5-20 min per cycle.
- **`execute_menu_item` timeout ≠ menu did not fire — verify via filesystem mtime / scene-state before retrying.** When `execute_menu_item` returns `MCP error -32001: Request timed out`, the menu often DID fire but a downstream operation (long bake, focus-steal, lightmap rebuild) exceeded the MCP per-tool timeout. **Retrying re-fires the menu** — duplicate prefabs, doubled spawner registrations, two copies of a populated ScriptableObject. **Verify before retry**: (1) asset-creating menu → `stat -c '%Y %n' <output/path>` before and after, mtime advance = menu fired; (2) scene-mutating menu → `find_gameobjects(name="ExpectedName")` or `manage_scene(action="get_info")` for the new objects; (3) build/bake → check `Library/com.unity.addressables/*` or `*-LightingData.asset` mtime; (4) fallback → `read_console(types=["log"], count=20)` for the menu's typical success log. Naive retry cost is high — a single doubled menu fire can fail downstream gates like no-duplicate-addresses; mtime check costs zero.
- **`manage_asset(action="reimport")` on a freshly-written `.cs` file silently no-ops — AssetDatabase status stays `Unknown`, scripts never compile.** When an agent writes a brand-new `.cs` file via `Write` / `File.WriteAllText` and immediately calls `manage_asset(action="reimport", path="Assets/Foo.cs")`, the call returns success but the AssetDatabase doesn't import the file (`status: "Unknown", guid: null`). `read_console` shows no compile errors because Unity never noticed the file. Downstream: the new MonoBehaviour / IComponentData "doesn't exist", baker can't find it, tests fail with `TypeNotFound`. **Root cause**: `manage_asset(action="reimport")` calls `AssetDatabase.ImportAsset(path, ImportAssetOptions.Default)` which assumes the asset is already tracked. For a file the AssetDatabase has never seen, it does nothing. **Correct pattern for new `.cs` files**: `Write` → `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` → `read_console(types=["error"])`. `refresh_unity` calls `AssetDatabase.Refresh()` which scans disk and imports untracked files. For edits to *existing* `.cs` files, `manage_asset(action="reimport")` works correctly — only the new-file case is broken. File an MCP-gap issue against `The1Studio/unity-mcp` to either auto-detect untracked files in `manage_asset(action="reimport")` or surface a clear error.
- **Multi-teammate MCP race silently kills the Unity bridge — designate ONE MCP-owner when running 6+ parallel teammates.** Complementary to the parallel-teammate `Library/Artifacts/` gotcha above (the modal-blocking failure mode). This entry covers the silent-degradation mode that happens BEFORE the modal pops. When ≥6 parallel teammates each issue MCP calls within the same minute, the bridge degrades: (1) `set_active_instance` calls race — instance hash flips between the project Editor and ephemeral package-environment Editors every few seconds; (2) `refresh_unity` returns `{ "status": "recovered" }` but `Library/ScriptAssemblies/*.dll` mtime never advances — i.e. compile was never actually requested; (3) `read_console` returns empty or stale results for long windows even though `AssetImportWorker` is idle; (4) last known-good heartbeat appears in only ONE teammate's session log; others see only timeouts. **Root cause**: MCP bridge is single-threaded per-Editor-instance. Concurrent `set_active_instance` calls overwrite each other's session-level routing. Parallel `refresh_unity` calls queue; the Editor processes the first, the rest return cached "recovered" responses without re-executing. **Mitigation — `MCP-owner` designation**: when the team-lead spawns ≥6 parallel teammates that will need MCP, designate ONE as the MCP-owner. All MCP calls (`refresh_unity`, `read_console`, `run_tests`, `manage_*`) MUST go through that teammate via `SendMessage`. Other teammates work with filesystem + Bash only (`Write`, `Edit`, `git status`, `find`, `grep`). At the end of each phase, MCP-owner runs batched `refresh_unity` + `read_console` and reports compile-clean. For Gate 3 (compile-clean) and Gate 4 (test-pass), defer to a final dots-tester teammate that runs sequentially after all parallel work lands.
- **Linux/Wayland focus-steal during MCP tool calls is Unity Editor behavior, NOT MCP** — `AssetDatabase.Refresh()`, `CompilationPipeline.RequestScriptCompilation()`, and Test Runner activation all cause Unity to grab focus from whatever window the user is in, on every script edit / `refresh_unity` / `run_tests` call. The MCP package itself never calls `EditorWindow.Focus()` (verified via grep: only one read-only `focusedWindow` reference). **Fix is window-manager-level, not MCP-level.** For KDE Plasma 6 + KWin Wayland: write a window rule to `~/.config/kwinrulesrc` matching `wmclass=Unity` (the Editor's WM_CLASS is literally `Unity`, **not** `UnityEditor` — verify with `kdotool getwindowclassname <id>` before writing the rule), `wmclassmatch=1` (Exact), `fsplevel=4` (Extreme prevention), `fsplevelrule=2` (Force). Then `qdbus6 org.kde.KWin /KWin reconfigure`. Verify with the test job result's `editor_is_focused: false` field after a `run_tests` call. For i3/Sway: `for_window [class="Unity"] focus_on_window_activation none`. For GNOME: `gsettings set org.gnome.desktop.wm.preferences focus-mode 'click'` (partial).

- **`manage_asset(action="create")` does NOT support arbitrary ScriptableObject types — only Folder, Material, and PhysicsMaterial.** `execute_code` is the next approach but on Windows hits a Mono "filename too long" error (~260-char path limit) on both roslyn and codedom backends. **Reliable fallback on all platforms**: write the `.asset` YAML directly to disk using the script GUID from its `.cs.meta` file, then call `refresh_unity(mode="force", scope="assets")`. Full YAML template, field-name rules, and step-by-step guide: `references/workflow-scriptableobject-creation.md`.

Field reports for the gotchas in this section: see [references/incidents.md](references/incidents.md).

## MCP Gaps — FIX-FIRST in our fork, do NOT just file an issue

**`The1Studio/unity-mcp` is OUR editable fork (beta branch). When an MCP tool is actually BROKEN (a server-side code defect — crash, exception, wrong result, missing `await`), FIX IT DIRECTLY in the fork — do NOT settle for filing an `[t1k:mcp-gap]` issue.** Filing-only leaves the bug live in every session; we are the only ones who ship this fork, so "file and wait" never resolves. Fixing is almost always faster than the round-trip of issue → triage → someone-else-fixes.

> Established 2026-05-30: `rendering_stats` was 100% broken (`'coroutine' object has no attribute 'strip'`). The systemic root cause — a missing `await` on the async `get_unity_instance_from_context` — affected **22 tool files**, all patched in one PR (#12) once we fixed-first instead of only filing issue #7. User directive: *"don't file an MCP issue, fix it right away — we can only fix it in the project."*

### Fix-first procedure (server-side Python defect in the fork)

1. **Locate the source.** The Python server is the `Server/` subdir of `The1Studio/unity-mcp`. The in-project submodule `Packages/com.coplaydev.unity-mcp/` may contain it, but it can be on a feature branch with uncommitted work — safest is a fresh clone: `git clone --branch beta git@github.com:The1Studio/unity-mcp.git /tmp/unity-mcp-fix`.
2. **Diagnose the real root cause** (read the handler + the helper it calls; confirm types/async-ness). Then **sweep the whole tree for the same pattern** — these bugs are usually systemic (one missing-`await` pattern repeated across many tool files), not a single site.
3. **Patch + `python -m py_compile`** every changed file; re-grep to confirm zero remaining occurrences.
4. **Branch off `beta`, commit (conventional, reference the issue if one exists), push, open a PR to `beta`.** Do NOT merge — leave for review. Remove the temp clone after pushing.
5. Repo is editable, NOT a `theonekit-*` kit repo → the kit-PR-boundary rule does NOT apply; you may push the branch + open the PR from a consumer session.

### When to emit `[t1k:mcp-gap]` instead (the narrow file-only path)

Reserve the marker/issue path for cases you genuinely **cannot** fix-first:
- A **feature request** (the tool lacks an action/capability) requiring design/upstream coordination — not a defect.
- The fix needs **C# Unity-bridge** changes you can't validate without an Editor round-trip, or it's genuinely non-trivial.
- You're mid-task and can't context-switch — emit the marker AND tell the user a fix-first PR is the real follow-up.

**Marker syntax (all four attributes required):**

```
[t1k:mcp-gap kit="unity" tool="<tool-name>" gap="<one-line summary>" evidence="<verbatim error or repro>"]
```

**Examples:**

```
[t1k:mcp-gap kit="unity" tool="manage_dots" gap="action='set_chunk_capacity' returns Unknown action" evidence="manage_dots(action='set_chunk_capacity', archetype='Player') → {error: 'Unknown action: set_chunk_capacity'}"]

[t1k:mcp-gap kit="unity" tool="manage_terrain" gap="paint_layer rejects normalized splat weights >1.0 silently" evidence="manage_terrain(action='paint_layer', weight=1.5) returns ok but no pixels written"]
```

**When NOT to emit:**

- Transient errors (compilation pending, domain reload) — retry instead.
- Misuse on your part (wrong parameter type, missing required arg) — fix the call.
- Already-known gaps documented in `references/error-recovery-guide.md` — don't re-file.
- Anything that's a fork-feature request requiring upstream coordination — escalate to the user instead.

The marker is sanitized (paths/secrets stripped), fingerprinted, and 7-day deduped. Rate-limited to 5 markers per session shared with `[t1k:lesson]` and `[t1k:skill-bug]`.
