---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---

# UnityMCP — Always The1Studio Fork on the `beta` Branch

Always-loaded hard rule on which UnityMCP source to install. Kit-wide; inherited by every Unity project that installs `theonekit-unity`.

## Reconnect on disconnect — don't abandon the session

The UnityMCP bridge **drops on every domain reload / assembly recompile / package re-resolution** (you'll see `[Errno 32] Broken pipe`, `Timeout receiving Unity response`, `Command processing timed out`, or `Unity instance '<Name>@<hash>' not found`). This is NORMAL and transient — the editor's `OnBeforeAssemblyReload` stops the bridge, and it auto-reconnects once the reload finishes. **Disconnect ≠ dead editor.** (Confirmed 2026-06-04: a long Amplify/Feel decoupling session hit broken-pipe/timeout repeatedly during back-to-back resolves + Burst rebuilds — every one was a reload, never a dead editor.)

**Required behavior when an MCP call fails with a connection/timeout error:**

1. **Do NOT** ask the user to restart the editor, click `Start Session`, or `set_active_instance` to a stray helper. **Do NOT** kill/restart Unity (hard rule, see `unity-forbidden-operations.md`).
2. **Diagnose first** (filesystem, no MCP needed): editor process alive? (`pgrep -af "Unity.*<Project>"`), workers busy? (`pgrep -af "AssetImportWorker.*<Project>"`), last Editor.log lines mention `OnBeforeAssemblyReload` / `Reloading assemblies`? → it's a reload, wait.
3. **Wait + retry the same call.** Reloads can take 30–180s on Linux Unity 6 (Burst rebuilds longer). Re-issue the call; the bridge reconnects on its own.
4. **Multi-instance churn:** during package resolution Unity spins up a transient `UnityPackagesEnvironment@<hash>` and the main editor temporarily disappears from `mcpforunity://instances`. Wait for the main `<Project>@<hash>` to return; do not pin the packages-environment instance.
5. Only after the editor process is confirmed GONE (not just unresponsive) should you involve the user (ask them to reopen — never kill it yourself).

The reconnect loop is: fail → diagnose (reload?) → wait → retry, **never** fail → escalate-to-user.

## Rule

The Python MCP server registered as `UnityMCP` MUST come from the The1Studio fork on the `beta` branch:

```
git+https://github.com/The1Studio/unity-mcp.git@beta#subdirectory=Server
```

Canonical install (use `-s user` so it survives across projects):

```
claude mcp remove UnityMCP -s user || true   # idempotent — clears any prior bad config
claude mcp remove UnityMCP -s local || true  # the Editor "Configure" button writes local scope
claude mcp add UnityMCP -s user -- \
  uvx --from "git+https://github.com/The1Studio/unity-mcp.git@beta#subdirectory=Server" \
  mcp-for-unity
```

## ⛔ Forbidden alternatives

| Alternative | Why it's banned |
|---|---|
| The PyPI package `mcpforunityserver` (any version) | Upstream registry release; not pinned to the fork's beta branch. Version drift from the Unity-side `Packages/com.coplaydev.unity-mcp/` submodule (also pinned to `The1Studio/unity-mcp@beta`) is not validated by this project. |
| The MCP For Unity Editor panel buttons **"Configure All Detected Clients"** and **"Install Skills"** for the Claude Code client | These write `uvx --offline --prerelease explicit --from "mcpforunityserver>=0.0.0a0" mcp-for-unity` to `~/.claude.json` at **local scope**. The `--offline` flag forbids the network fetch of an uncached package → permanent connect failure. Documented failure mode (2026-05-18). |
| Any other branch of `The1Studio/unity-mcp` (`master`, feature branches) | The submodule pins `beta`; the MCP server must match. Using `master` risks a tool-schema mismatch with the in-editor bridge. |
| Anyone else's fork of the upstream `unity-mcp` repo | Only the The1Studio fork carries our tool patches. |

## Always verify after install

After every register/re-register, run BOTH probes in the same turn:

```bash
claude mcp get UnityMCP                # expect: ✓ Connected, Command: uvx, Args: --from git+https://github.com/The1Studio/unity-mcp.git@beta#subdirectory=Server mcp-for-unity
claude mcp list | grep -i unity         # expect: ✓ Connected
```

If `claude mcp get` shows `mcpforunityserver` in the args, `--offline` anywhere in the args, scope = `local config` instead of `user config`, OR a branch other than `beta` after `@` — the rule is violated. Remove and re-add per the canonical install above.

## When the Unity-side panel is wrong

If the Editor's MCP For Unity panel shows `Configured` with the Claude Code client but `claude mcp list` shows the broken PyPI command, the panel wrote local-scope config OVER your user-scope config. Click **`Unregister`** in the panel, then run the canonical install commands again. Do NOT click "Install Skills" or "Configure All Detected Clients" — those re-write the broken config.

## Why

- The Unity-side package `Packages/com.coplaydev.unity-mcp/` is a git submodule pinned to `The1Studio/unity-mcp` on `beta` (per project `CLAUDE.md` "Submodule Editability" table). Python server MUST match.
- 2026-05-18 session: every MCP call failed with `-32000` "Failed to connect" because the Editor's "Configure All Detected Clients" button had silently rewritten the working user-scope config with the broken local-scope `--offline + mcpforunityserver` command. Diagnosis took ~5 minutes; auto-rewrite is invisible until it breaks.
- The `--offline` flag plus an uncached registry package is **unrecoverable** — `uvx` cannot fetch the package without network, and the package was never cached, so retrying never succeeds.

## Related

- `rules/unity-forbidden-operations.md` — parallel rule banning kill/quit and `Assets/Reimport All`
- `modules/base/skills/t1k-unity-base-mcp-skill/SKILL.md` § "🔧 Install / Re-Install — Always The1Studio Fork on beta" — the operator-side install procedure with the same canonical commands and verification
- `CLAUDE.md` "Submodule Editability" — Unity-side package pinned to The1Studio/unity-mcp@beta
- SessionStart hook reminder `[t1k:mcp] action=install-fork tier=required name="UnityMCP" repo="The1Studio/unity-mcp" branch="beta"`
