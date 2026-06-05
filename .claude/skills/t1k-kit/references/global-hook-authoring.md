---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Global Hook Authoring — `$HOME`-resilient command pattern

When a kit ships hooks that install into **global** `~/.claude/settings.json` (NOT project `.claude/settings.json`), the hook-`command` strings MUST use the `node -e` wrapper pattern below — NOT bare `$HOME/.claude/hooks/X.cjs`.

## The bug this prevents (model-router#49 / 2026-05-25)

The T1K CLI's settings merger has a function `fixSingleCommandPath(cmd, isGlobal=false)` in `theonekit-cli/src/domains/installation/merger/settings-path-transformer.ts`. Its `isGlobal` parameter defaults to `false`, which means callers that forget to pass `true` cause `$HOME/...` paths to be rewritten to `$CLAUDE_PROJECT_DIR/...` at install time — silently breaking every hook with `MODULE_NOT_FOUND` (Node `loader:1424`).

`theonekit-model-router` shipped with bare `$HOME/.claude/hooks/mr-*.cjs` commands. During install, one caller of `fixHookCommandPaths()` omitted `isGlobal=true`, so the merger rewrote all 7 commands to use `$CLAUDE_PROJECT_DIR`. A single 90-minute session produced ~363 banner errors (`mr-metrics: 218`, `mr-spawn-guard: 133`, `mr-telemetry: 12`, 3× SessionStart hooks).

The CLI bug needs an upstream fix. The wrapper pattern below is **defense-in-depth** — it works correctly even when the CLI rewrites `$HOME` → `$CLAUDE_PROJECT_DIR`, because it ignores both env vars entirely and reads the user's real home from `/etc/passwd` via `os.userInfo().homedir`.

## Canonical wrapper pattern (cross-OS)

```json
{
  "type": "command",
  "command": "node -e \"const home = require('os').userInfo().homedir; process.env.HOME = home; process.env.USERPROFILE = home; require(require('path').join(home, '.claude', 'hooks', '<your-hook>.cjs'))\"",
  "timeout": 10
}
```

Replace `<your-hook>` with the actual filename. Apply to ALL hooks in your kit's source `.claude/settings.json` that will install to global scope.

### Why both `HOME` and `USERPROFILE`?

Hook code may inadvertently read either env var directly (`process.env.HOME` on Unix, `process.env.USERPROFILE` on Windows). Setting both inside the wrapper guarantees correct behavior regardless of platform AND of how the hook reads home. The local `home` const is captured once and reused for the `path.join` — avoids re-reading an env var that was just overwritten and keeps the source of truth in one place.

## Why this is resilient

| Mechanism | Why it works | OS coverage |
|---|---|---|
| `os.userInfo().homedir` | POSIX: reads from `getpwuid(geteuid())` (i.e. `/etc/passwd`). Windows: calls `GetUserProfileDirectoryW` (Win32 API). Unaffected by `HOME` or `USERPROFILE` env vars being wrong. | Linux + macOS + Windows |
| `process.env.HOME = home` BEFORE require | Restores `HOME` for the hook subprocess so any code that reads `process.env.HOME` (or shells out to a Unix tool) sees the corrected home. | Linux + macOS (Unix convention) |
| `process.env.USERPROFILE = home` BEFORE require | Same defense for hook code that reads the Windows-native env var. | Windows |
| `require(path.join(...))` | Pure Node module loading — `path.join` is OS-aware (forward vs backward slashes). No shell expansion, no env-var substitution at command-string level. Bypasses any T1K CLI transformer that operates on the string before subprocess spawn. | All |

## Other forms to AVOID

| Form | Why it breaks |
|---|---|
| `node "$HOME/.claude/hooks/X.cjs"` | CLI's `fixSingleCommandPath(isGlobal=false)` rewrites `$HOME` → `$CLAUDE_PROJECT_DIR` |
| `node "~/.claude/hooks/X.cjs"` | Tilde doesn't expand in node argv (shell-only feature); resolves literally to `~/.claude/...` which doesn't exist |
| `node "/home/<user>/.claude/hooks/X.cjs"` | Hard-coded path, not portable across machines/users |
| `node "%USERPROFILE%/.claude/hooks/X.cjs"` | Cross-shell unreliable (cmd.exe vs PowerShell vs bash); CLI transformer also rewrites |

## When does this apply

Only to hooks declared in your kit's `.claude/settings.json` at kit-root level (the file that gets merged into `~/.claude/settings.json` by `t1k install`). Hooks declared in **project-scope** `.claude/settings.json` correctly use `$CLAUDE_PROJECT_DIR` and don't need the wrapper.

## Validation

The release-action CI gate `validate-global-hook-home-resilience.cjs` (PR pending 2026-05-25) auto-fails any kit shipping bare `$HOME/.claude/...` or `~/.claude/...` paths in global-scope settings.json. Run locally:

```bash
node theonekit-release-action/scripts/validate-global-hook-home-resilience.cjs --root <your-kit-root>
```

## Reference incident

- model-router#49 — the bug report
- model-router#50 — the kit-side fix (wrapping all 7 mr-* commands)
- The CLI-side root-cause fix (changing `isGlobal` default to required) is a separate follow-up.
