---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---

# UnityMCP — Always The1Studio Fork on the `beta` Branch

Always-loaded hard rule on which UnityMCP source to install. Kit-wide; inherited by every Unity project that installs `theonekit-unity`.

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
