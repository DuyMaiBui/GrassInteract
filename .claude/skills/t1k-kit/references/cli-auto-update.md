---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# CLI Auto-Update

TheOneKit CLI (`t1k`) auto-updates itself in the background at session start.

## How It Works

`check-cli-updates.cjs` fires on `SessionStart` (after `check-kit-updates.cjs`):

1. Reads `cli.repo` and `cli.npmPackage` from any `t1k-config-*.json` fragment
2. Locates the `t1k` binary on PATH (`which` / `where`)
3. Parses `t1k --version` → current semver
4. Queries `gh release view --repo <cli.repo>` → latest release tag
5. Compares versions:
   - **Equal or ahead** → silent exit, cache refreshed
   - **Major bump** (default behavior, `autoUpdateMajor: true`) → same auto-update path as minor/patch
   - **Major bump** (when `autoUpdateMajor: false`) → prints `[t1k:cli-major]` notice; user must run `t1k update` manually
   - **Minor / patch bump** → spawns a **detached** `t1k update --yes --cli-only` (or `t1k update --yes` on pre-2.5.0 CLIs) whose stdout/stderr stream to the rolling log with `NO_COLOR=1` / `FORCE_COLOR=0` / `TERM=dumb` for readable non-ANSI output; the current session keeps using the old binary, the new one activates on next session start

### --cli-only flag and version-gate

The `--cli-only` flag ships in theonekit-cli ≥ 2.5.0. It suppresses the post-update kit content cascade (`promptKitUpdate`), which would otherwise re-init the global `~/.claude/` kit under `--yes`. The hook version-gates the flag:

- **CLI ≥ 2.5.0**: spawns `t1k update --yes --cli-only` — CLI binary is upgraded, zero kit content side effects.
- **CLI < 2.5.0**: spawns `t1k update --yes` (legacy) — the upgrade happens but the cascade may still fire once. The next session, now on 2.5.0+, will use `--cli-only` going forward.

This graceful degradation keeps users on old CLIs unblocked while delivering the fix automatically once they upgrade.

## Config

Declared in `t1k-config-core.json`:

```json
{
  "cli": {
    "repo": "The1Studio/theonekit-cli",
    "npmPackage": "@the1studio/theonekit-cli"
  }
}
```

Kits do NOT need to declare this — core owns the CLI repo reference.

## Opt-Out

Shared with kit auto-update. Any `t1k-config-*.json` can disable both:

```json
{ "features": { "autoUpdate": false } }
```

### Major-Only Opt-Out

To keep minor/patch auto-updates but require manual action for major bumps (e.g., to review breaking changes), set:

```json
{ "features": { "autoUpdateMajor": false } }
```

Default: `true` (majors are auto-applied just like minor/patch). When `false`, majors fall back to the legacy notify-only behavior with the `[t1k:cli-major]` / `[t1k:major-update]` tags. Applies to both CLI binary and kit content (flat and modular).

## Cache

- File: `~/.claude/.cli-update-check-cache`
- TTL: 24 hours
- Global scope — one check per user, not per project

## Log

- File: `~/.claude/.cli-update.log`
- Rolling, capped at ~100KB (keeps the last half when it overflows)
- Each run appends a timestamped header + full `t1k update` output
- Inspect manually after a background update: `cat ~/.claude/.cli-update.log`

## Safeguards

| Guard | Behavior |
|---|---|
| **No `t1k` on PATH** | Silent exit — user is likely running from source |
| **CWD git remote matches `cli.repo`** | Silent exit — never self-update the CLI from its own source tree |
| **Cache hit (< 24h)** | Silent exit |
| **`gh` not authenticated / network error** | Fail-open, cache refreshed, retry next day |
| **Spawn fails (EACCES, PATH error, etc.)** | Logged to `.cli-update.log`, session continues |
| **Any uncaught error** | Fail-open, exit 0 |

## Dry-Run (for debugging)

```bash
rm -f ~/.claude/.cli-update-check-cache
T1K_CLI_UPDATE_NOOP=1 node .claude/hooks/check-cli-updates.cjs
```

Emits the `[t1k:cli-update]` tag and writes to the log, but does NOT spawn the real update. Useful for verifying version detection, gh lookup, and comparison logic without mutating the CLI install.

## What The AI Should Do When It Sees `[t1k:cli-update]`

- Note the version bump for the user
- Do NOT run `t1k update` — it is already running in the background
- Remind the user to restart their shell / session after the update log shows completion
- Suggest inspecting `~/.claude/.cli-update.log` if anything seems wrong on the next session

## What The AI Should Do When It Sees `[t1k:cli-major]`

This tag only appears when `features.autoUpdateMajor: false` is set.

- Surface the notice to the user
- Offer to run `t1k update` interactively so they can review release notes and any breaking changes
- Do NOT spawn a background update for major bumps when this tag is emitted — the opt-out is explicit

## What The AI Should Do When It Sees `[t1k:major-update]` (kit content)

Emitted by `check-kit-updates.cjs` when a kit or module has a major bump AND `features.autoUpdateMajor: false`.

- Surface the notice to the user with kit/module name and version range
- Offer to run the suggested `gh release download` command
- If migrating from an old schema (e.g., registry v1→v2), also recommend running `/t1k:doctor fix` after the update

## Banner Accuracy — `[t1k:update]` / `[t1k:update-failed]`

Phase 02 of 260418-1942-t1k-ecosystem-fixes split the generic auto-update banner into two distinct tags so log parsers can tell a real spawn from a failure by tag alone. **Never mix these tags.**

| Tag | When emitted | Meaning |
|---|---|---|
| `[t1k:update]` | Spawn succeeded (`spawnT1kUpdateDetached` returned `spawned: true`) | Background `t1k update --yes` is running; log at `~/.claude/.kit-update.log` |
| `[t1k:update-failed]` | Spawn itself failed (EACCES, PATH error, etc.) **OR** the previous detached run's recorded exit code is non-zero within the 24h window | Either spawning the child did not succeed this session, or the PREVIOUS session's background update exited non-zero |

### Status File — `~/.claude/.kit-update.status`

Because detached children lose their exit code when the parent unrefs, `.claude/hooks/lib/t1k-update-runner.cjs` wraps the real `t1k` invocation and persists the outcome to a JSON file:

```json
{
  "exitCode": 0,
  "ts": "2026-04-19T00:00:00Z",
  "args": ["update", "--yes"],
  "filesChanged": [".claude/skills/t1k-foo/SKILL.md"],
  "kits": ["theonekit-unity"],
  "stderrTail": "last 2KB of child stderr"
}
```

Written atomically (tmp file + `fs.renameSync`) so concurrent reads never observe a partial write.

- `filesChanged[]` — `.claude/`-relative paths changed by the update (derived from `git diff --name-only HEAD` + `git ls-files --others --exclude-standard` scoped to `.claude/`). Phase 03's scope-safety gate consumes this as `expectedFiles`.
- `kits[]` — pre-update snapshot of installed kit repo short names. Used for commit-message formatting.
- `stderrTail` — last ~2KB of child stderr, surfaced verbatim in the PREV RUN FAILED banner.

On the NEXT SessionStart, `check-kit-updates.cjs` reads this file and — if the previous run FAILED and the status is <24h old — prints a PREV RUN FAILED banner before it decides whether to re-spawn. Successful runs produce no banner; stale (>24h) failures are ignored.

## Auto-Commit Of Kit Sync — `features.autoCommitKitSync` (opt-in)

Phase 03 of 260418-1942-t1k-ecosystem-fixes added an opt-in flag that lets the session-start hook commit the `.claude/` changes produced by the auto-update pipeline for you. Default is **OFF** — behavior is unchanged unless the user flips the flag.

```json
{ "features": { "autoCommitKitSync": true } }
```

### Behavior when enabled

| Path | Trigger | Source of file list | Commit message |
|---|---|---|---|
| Manual fallback (CLI binary not on PATH) | Extraction lands in cwd; helper runs before the hook exits | `git status --porcelain -uall` (no expectedFiles) | `chore(t1k): sync <kit1>,<kit2> kit modules` (kits from `repoMap`) |
| CLI-spawned | Next SessionStart reads `~/.claude/.kit-update.status`; if `exitCode === 0` AND `filesChanged[]` non-empty, helper runs with `expectedFiles = status.filesChanged` | `.kit-update.status.filesChanged[]` | `chore(t1k): sync <kit1>,<kit2> kit modules` (kits from `status.kits`) |

The helper always aborts when:
- The working tree has **non-`.claude/`** changes (scope-safety skip + warn)
- Any staged `.claude/` file is NOT in `expectedFiles` (scope-safety abort when the update-runner's file list is available)
- The repo is mid-merge / mid-rebase
- No `.claude/` paths are dirty

The helper never pushes. It only creates a single local commit.

### `--no-verify --no-gpg-sign` exception (documented)

The auto-commit path runs inside a **TTY-less detached hook**, where Pinentry / GPG-SSH prompts would hang forever. For this ONE call site only we pass `--no-verify --no-gpg-sign` to `git commit`. This is the sole documented exception to the no-skip-hooks rule — every other commit path in TheOneKit retains hooks and signing.

### Debug

- `T1K_DEBUG_AUTOCOMMIT=1` — logs each gate + reason to stderr (e.g. `flag-off`, `no-changes`, `mid-merge`, `non-claude-dirty`, `unexpected-files`, `committed`).

## What The AI Should Do When It Sees `[t1k:update-failed]`

1. Note the failure visibly to the user with the reported reason.
2. Offer to inspect `~/.claude/.kit-update.log` for the detailed trace.
3. If the tail mentions a missing flag or preset, suggest running `t1k update` interactively so the user can pick the right module selection.
4. Do NOT silently re-spawn in the foreground — the next session will try again automatically, and running concurrent updates risks a lock held by the peer process.
