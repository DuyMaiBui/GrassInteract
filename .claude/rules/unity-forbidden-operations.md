---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Unity — Forbidden Operations

Always-loaded hard bans on Unity operations that destroy productivity or have no legitimate automation use case. Kit-wide rule shipped by `theonekit-unity`.

## ⛔ NEVER terminate, quit, or restart the Unity Editor

The Unity Editor process is **user-owned**. You must NEVER terminate, quit, restart, or relaunch it on your own initiative — with ONE narrow exception: an explicit, in-session user request to do so (see **"✅ Narrow exception — explicit user-authorized termination"** below).

Forbidden commands and tool calls:

- `kill <pid>` where `<pid>` resolves to a Unity process (binary contains "Unity")
- `pkill -f Unity`, `pkill -f UnityHub`, `pkill -f UnityShaderCompiler`, `pkill -f AssetImportWorker`
- `killall Unity`, `killall UnityHub`, `killall AssetImportWorker`
- `mcp__UnityMCP__execute_menu_item(menu_path="File/Quit")` (or any quit/exit menu path)
- Any wrapper script that internally calls one of the above

If you think you need to terminate Unity, you are almost certainly wrong. The correct actions are:

| Symptom | What to actually do |
|---|---|
| MCP returns "No Unity Editor instances" / connection closed | **Diagnose first** per the "MCP timeout ≠ bridge disconnect" section below. If the bridge is genuinely dead but the editor is alive, ask the user to click `Window > MCP for Unity > Start Session`. **Do NOT terminate the editor.** |
| Unity feels unresponsive / mid-domain-reload | Wait. `pgrep -af "AssetImportWorker.*<your-project>"` to confirm workers are active. Workers go idle when reload finishes. |
| You think the editor is "stuck" | It almost certainly isn't — Unity 6 domain reloads on Linux can take 60+ seconds. Check `stat Library/ScriptAssemblies/*.dll` for recent mtime to confirm progress. |
| You need to restart Unity to apply a config / package change | **Ask the user** via `AskUserQuestion`. The user closes Unity, runs the change, reopens Unity. You never touch the process. |

### Why

- The user has open unsaved scenes, prefab edits, console history, Inspector pinned objects, and editor layouts that vanish on `kill -9`. There is no recovery.
- Terminating Unity stops any in-flight compile / bake / import — leaving Library/ in a corrupt half-state that takes longer to recover from than the original problem.
- The user is the one looking at Unity; they know whether it's actually broken or just busy. You only see the MCP socket, which is a downstream signal.
- Hard-terminating AssetImportWorkers orphans the parent editor's import queue → subsequent saves can corrupt assets.

### Enforcement

A `unity-process-guard.cjs` PreToolUse hook (shipped by `theonekit-core`) hard-blocks the forbidden commands above (exit 2). To remove the hook, edit `.claude/settings.json` — but doing so requires the user's explicit consent.

### ✅ Narrow exception — explicit user-authorized termination

The ban above is about the AI deciding to kill Unity (the 2026-05-15 failure mode). It does **not** apply when the **user directly and unambiguously requests** termination/restart **in the current session** — e.g. "kill Unity", "close the editor for me", "restart Unity". In that case the user owns the consequence (unsaved-state loss), so the AI may carry it out.

Conditions — **ALL** must hold:

1. **Direct, in-session request.** The instruction is the user's own, given this session ("kill/close/quit/restart Unity"). A stale preference, an inferred need, or the AI's own judgment that a restart "would help" does **not** qualify — that is still the banned case. When in doubt, ask via `AskUserQuestion`; do not assume.
2. **Right target.** Confirm the PID is the intended project's editor (`ps -p <pid> -o cmd=` shows `-projectpath …/<project>`), not another project's Unity.
3. **Graceful first.** Prefer `kill <pid>` (SIGTERM — lets Unity flush/save) over `kill -9 <pid>` (SIGKILL). Only escalate to `-9` if SIGTERM doesn't exit and the user still wants it gone.
4. **Audit marker.** Prepend the explicit authorization marker to that single command:
   ```bash
   T1K_ALLOW_UNITY_KILL=1 kill <pid>
   ```
   The `unity-process-guard.cjs` hook detects this marker and allows **only that one command** through. It is not a global disable — every other Unity-kill command stays blocked. The marker is the on-the-record signal that this termination was user-ordered, not an AI decision.

**Relaunch:** the AI can terminate but cannot bring up the GUI editor itself; after the kill, the **user reopens** Unity via Unity Hub (the AI may launch via CLI only if the user explicitly asks).

### History

Originating incident, 2026-05-15: AI lost the MCP bridge during a Unity 6 domain reload (Linux, ~60s reload time) and tried to terminate the Unity Editor PID to "restart" the connection. The editor was fine — only the MCP socket had dropped during reload. User response: "we will never ever kill or re-import-all the Unity Editor ourselves." Lesson: MCP visibility loss ≠ editor problem. Ask the user; never touch the process.

---

## ⛔ NEVER call `Assets/Reimport All`

Do **NOT** invoke any of these without an explicit, in-this-session user order or a referenced document that requires it for a specific recovery step:

- `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")`
- Any menu path containing "Reimport All"
- `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` from an Editor script
- Any tool wrapper that internally fans out to project-wide reimport

### Why

- **Massive cost:** triggers a from-scratch reimport of every asset (textures, models, shaders, scripts, audio, prefabs). On a large project this is **30+ minutes** of wall-clock time and spawns multiple `UnityShaderCompiler` workers.
- **Cannot be cancelled:** once issued via MCP, Unity processes the queue regardless of subsequent commands. The MCP bridge typically goes unresponsive while the reimport runs, blocking all further work.
- **Almost never the right answer:** the legitimate use case (rebuild stale Burst caches or recover from a corrupted asset DB) is satisfied by targeted alternatives at a fraction of the cost.

### What to use instead

| Goal | Correct command |
|---|---|
| Recompile scripts after edits | `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` |
| Re-bake DOTS SubScenes after entity-type changes | `rm -rf Library/EntityScenes/` then enter Play mode |
| Clear stale Burst cache after type changes | `rm -rf Library/BurstCache Library/Bee/artifacts Library/ScriptAssemblies` then `refresh_unity` |
| Reimport ONE asset (texture, model, prefab) | `manage_asset(action="reimport", path="Assets/...")` |
| Recover from "everything looks broken" | Restart the Unity Editor (faster than Reimport All) |

### Test

If you catch yourself thinking "let me just trigger Reimport All to be safe," STOP. Ask: which of the above targeted commands fits my actual goal? If none, the situation does not call for Reimport All — it calls for understanding what is actually broken.

### History

Caught 2026-05-09: AI issued `Assets/Reimport All` after a Burst cache nuke when `refresh_unity` returned `compile_requested: false`. The correct fix was to touch a `.cs` file (or call `refresh_unity` with the explicit `compile="request"` arg). The Reimport All ran for 10+ minutes and could not be cancelled. User issued the hard ban: "we will never ever need to reimport all, dont even call it, that is the wrong call, dont do it until you have the direct order or the requirement from the document."

### Enforcement

The same `unity-process-guard.cjs` hook (shipped by `theonekit-core`) blocks `mcp__UnityMCP__execute_menu_item` calls whose `menu_path` contains "Reimport All", "Quit", or "Exit". The hook's matcher is `Bash|mcp__UnityMCP__execute_menu_item`.

---

## ⏳ MCP timeout ≠ bridge disconnect — diagnose before escalating

When an MCP call fails ("Unity is reloading", "No Unity Editor instances", "Connection closed before reading expected bytes"), **do not jump to asking the user to click `Start Session` in `Window > MCP for Unity`**. Almost always Unity's main thread is busy (compiling, importing, baking, domain-reloading) and the bridge thread is blocked behind it. The bridge IS connected; Unity just isn't ready.

Run these probes BEFORE escalating to the user:

| Signal | Command | Meaning |
|---|---|---|
| Editor + workers | `pgrep -af "Unity.*<your-project>"` | Main PID + AssetImportWorker count. >2 workers = busy import. |
| Lock file | `ls Temp/UnityLockfile` | Present → editor running. |
| Worker log | `tail -10 Logs/AssetImportWorker0.log` | "Importing X"/"ReloadAssembly" = busy. "shutdown … idle timeout" = idle. |
| Compile recency | `stat Library/ScriptAssemblies/<your-runtime-asmdef>.dll` | mtime = last compile finish. |

**Linux note:** `Logs/Editor.log` does NOT exist on Linux Unity 6. Use `Logs/AssetImportWorker*.log` for compile/import status from the worker side.

### Decision tree

1. MCP fails → run probes.
2. Editor + workers active → BUSY. Wait. `Monitor` for DLL mtime to advance OR worker log "shutdown … idle".
3. Editor exists + 0 workers + recent DLLs + lock file → bridge socket genuinely dead → THEN ask user for `Start Session`.
4. Editor process gone → user closed Unity → ask them to reopen.

History: 2026-05-09 — AI asked the user to click `Start Session` while Unity was actually mid-recompile; user pointed out the failure mode is different. The bridge was fine; Unity just hadn't finished reloading the domain yet. Lesson encoded above.
