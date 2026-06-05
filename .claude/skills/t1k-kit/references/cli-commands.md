---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit CLI — Maintainer-Facing Commands

Reference for the `t1k` CLI commands that maintainers run during scaffolding,
release work, and recovery. Behavior here is grounded in the cli source
(`theonekit-cli@v4.14.0`); flags are read directly from
`src/cli/command-registry.ts` and the per-command source files.

End-user-facing commands like `t1k init`, `t1k update`, `t1k uninstall`,
`t1k modules add/remove/list/preset` are documented elsewhere — only the
commands that matter for **kit maintenance** are listed here.

---

## `t1k new` — Bootstrap a fresh project

Source: `src/commands/new/new-command.ts` + `src/commands/new/phases/`

Creates a new TheOneKit project: directory setup → version selection →
download/extract release → install → optional post-setup (skills, packages).

```
t1k new
t1k new --kit unity
t1k new --dir ./my-project --kit unity --release v2.1.0 --yes
t1k new --kit-path /local/path/to/kit-checkout
```

### Flags

| Flag | Purpose |
|---|---|
| `--dir <dir>` | Target directory (default: `.`) |
| `--kit <kit>` | Kit to install (`unity`, `cocos`, `web`, `rn`, `nakama`, `designer`, `core`); supports comma-separated values for multi-kit installs |
| `-r, --release <version>` | Skip version-selection prompt; use a specific tag (`latest`, `v1.0.0`) |
| `--force` | Overwrite existing files without confirmation |
| `--exclude <pattern>` | Exclude files matching glob (repeatable) |
| `--opencode` | Install OpenCode CLI package (non-interactive) |
| `--gemini` | Install Google Gemini CLI package (non-interactive) |
| `--install-skills` | Install skill dependencies (non-interactive) |
| `--with-sudo` | Include system packages requiring sudo (Linux: ffmpeg, imagemagick) |
| `--prefix` | Move all slash commands into `commands/t1k/` so they get the `/t1k:` prefix |
| `--beta` | Show beta versions in the selector |
| `--refresh` | Bypass release cache; re-fetch latest from GitHub |
| `--docs-dir <name>` | Custom docs folder name (default: `docs`) |
| `--plans-dir <name>` | Custom plans folder name (default: `plans`) |
| `-y, --yes` | Non-interactive mode with sensible defaults |
| `--use-git` | Clone via git (uses local SSH/HTTPS creds) instead of GitHub API ZIP |
| `--archive <path>` | Use a local archive file (zip/tar.gz) instead of downloading |
| `--kit-path <path>` | Use a local checked-out kit directory instead of downloading |

### Gotchas

- **`--use-git`, `--archive`, and `--kit-path` are mutually exclusive.** Passing two at once throws "Options X, Y are mutually exclusive."
- **`--use-git` requires `--release <tag>`.** Git clone mode cannot list versions without GitHub API access. Error message points you at `t1k new --use-git --release v2.1.0`.
- **Modular kits auto-detect** via the release `manifest.json`. Modular path is taken only when no offline-flag overrides (`--use-git`, `--archive`, `--kit-path`) are present — otherwise falls back to the flat-kit downloader.
- **Module selection UX** — interactive picker activates only when `--yes`, `--preset`, `--modules`, and TTY are all consistent with prompting. With `--yes` and no selection flag, the install resolves required-only modules (per `docs/module-selection-ux.md`).
- **`--prefix` is rerunnable** but operates by moving directories; calling it twice with `--force` cleans `commands/` first.

---

## `t1k modules create` — Scaffold a new module.json

Source: `src/commands/modules/modules-create.ts`

Writes a fresh `.claude/modules/<name>/module.json` for a new module within an
existing modular kit. The audit's reference to "module new" maps to this
subcommand.

```
t1k modules create                                      # interactive
t1k modules create dots-ecs-core --kit theonekit-unity  # positional name
t1k modules create dots-ecs-core --kit theonekit-unity --yes
t1k modules create dots-ecs-core --kit theonekit-unity --json
```

### Flags

| Flag | Purpose |
|---|---|
| `[name]` (positional) | Module name (kebab-case). When omitted, prompted for. |
| `--dir <dir>` | Target project directory (default: `.`) — module dropped under `<dir>/.claude/modules/<name>/` |
| `--kit <kit>` | Owning kit (e.g. `theonekit-unity`, `theonekit-core`). REQUIRED in non-interactive (`--yes` or `--json`) mode. |
| `-y, --yes` | Skip prompts; emit a minimal module.json with no `detect:` block |
| `--json` | Machine-parseable summary on stdout: `{ action, module, path, hasDetect, warnings }` |

### Behavior

- Creates `<projectDir>/.claude/modules/<name>/module.json` (mkdirs as needed).
- Refuses to overwrite an existing `module.json` — aborts with the path it
  found. Move/delete the old file deliberately if you want a fresh scaffold.
- **Interactive only:** prompts for description, kit, `required` flag, and an
  optional `detect:` block (files glob + anyOf pattern + minHits).
- **Non-interactive (`--yes`/`--json`):** writes a stub with no `detect:`
  block. Module is opt-out of auto-scan until you hand-edit one in.
- `skills: []` is always empty on first write — the maintainer populates it
  before committing.

### Gotchas

- **`--kit` is REQUIRED with `--yes` or `--json`.** No default; the command
  aborts with "--kit is required in non-interactive mode (--yes or --json)".
- **Detect block + empty skills emits a warning** ("Module has detect: but
  skills: []. Populate skills[] before committing — otherwise P6d CI will
  reject it."). The warning is printed to stderr in interactive mode and
  surfaced in the JSON `warnings[]` array in `--json` mode.
- **Glob validation is strict** — every glob in `detect.files` must start
  with `**/` or `/`. Bare patterns like `*.cs` are rejected.
- **Module name MUST be kebab-case** (`a-z`, `0-9`, hyphens). PascalCase or
  snake_case names are rejected at prompt + positional validation.
- **Pair this with the `_disabled: true` detect-stub gotcha** in the parent
  `SKILL.md` — the recommended pattern for modules whose detect rules aren't
  ready yet is to ship `"detect": { "_disabled": true, ... }` so doctor #41
  surfaces it as "needs activation" instead of a hard CI failure.
- After running this command, **regenerate `.claude/t1k-modules.json`** via
  `generate-modules-registry.cjs` (see `rules/module-registry-sync.md`).

---

## `t1k migrate` — Port content to other AI providers

Source: `src/commands/migrate/migrate-command.ts`

One-shot migration of agents, commands, skills, config (`CLAUDE.md`), and
rules (`.claude/rules/`) to other coding-agent providers (cursor, codex,
opencode, gemini-cli, etc.). **NOT** the same as the registry-schema
migration documented in `references/migrate.md` and `/t1k:kit migrate`.

```
t1k migrate                                  # interactive provider picker
t1k migrate --agent cursor --agent codex     # explicit providers
t1k migrate --all                            # all supported providers
t1k migrate --global                         # install at user-level instead of project
t1k migrate --config                         # CLAUDE.md only
t1k migrate --rules                          # .claude/rules/ only
t1k migrate --skip-config --skip-rules       # everything except config + rules
t1k migrate --dry-run                        # preview targets, write nothing
t1k migrate --force                          # reinstall items deleted/edited by the user
```

### Flags

| Flag | Purpose |
|---|---|
| `-a, --agent <agents...>` | Target providers (repeatable). Examples: `cursor`, `codex`, `opencode`, `gemini-cli` |
| `-g, --global` | Install at user-level (`~/.cursor/`, `~/.codex/`, etc.) instead of project-level |
| `--all` | Migrate to every supported provider |
| `-y, --yes` | Skip confirmation prompts |
| `--config` | Migrate `CLAUDE.md` ONLY (suppresses agents/commands/skills/rules) |
| `--rules` | Migrate `.claude/rules/` ONLY (suppresses agents/commands/skills/config) |
| `--skip-config` | Skip config migration (alias: `--no-config`) |
| `--skip-rules` | Skip rules migration (alias: `--no-rules`) |
| `--source <path>` | Custom `CLAUDE.md` source path (only affects `--config`) |
| `--dry-run` | Compute and display the reconcile plan; write zero files |
| `-f, --force` | Force reinstall items the user has deleted or edited |

### Scope-resolution truth table (from `migrate-scope-resolver.ts`)

| Flags passed | agents | commands | skills | config | rules |
|---|---|---|---|---|---|
| (none) | yes | yes | yes | yes | yes |
| `--config` | no | no | no | yes | no |
| `--rules` | no | no | no | no | yes |
| `--config --rules` | no | no | no | yes | yes |
| `--skip-config` | yes | yes | yes | no | yes |
| `--skip-rules` | yes | yes | yes | yes | no |

### Workflow

1. Discover portable items in `~/.claude/agents/`, `~/.claude/commands/`,
   `~/.claude/skills/`, `~/.claude/CLAUDE.md`, `.claude/rules/`.
2. Provider selection (interactive picker unless `--agent`/`--all` set).
3. Scope selection (project vs global; respects `--global`).
4. Reconcile — compute diff between source state, target state, and
   portable registry. Display plan.
5. If `--dry-run`: exit here with `Dry run complete — no files written`.
6. Conflict resolution (interactive when TTY + not `--yes`).
7. Install phase — run the planned writes; rollback on failure via
   `migrate-rollback.ts`.

### Gotchas

- **Discovery covers `~/.claude/` only**, not project-level `.claude/`. If
  your skills live only in a project, this command finds nothing — it'll
  exit with `Nothing to migrate.`
- **Some providers do not support `commands`** (per the provider registry).
  Selected providers that don't support commands have commands silently
  skipped with a `[i] Commands skipped for: <providers> (unsupported)` info
  message.
- **`--force` is non-destructive for pre-existing user content.** It only
  reinstalls items that the registry tracked as installed but are now
  missing/edited. Files the user manually authored outside the registry
  are not touched.
- **Conflict resolution is mandatory in TTY mode.** If `plan.hasConflicts`
  is true and stdout is a TTY (and `--yes` is not set), the prompt blocks
  until each conflict is decided. Run with `--yes` in CI to take the
  defaults.
- **`--source` only affects `CLAUDE.md`** — it does NOT change where
  agents/commands/skills are read from.

---

## `t1k migrate metadata` — Migrate metadata.json to v3

Source: `src/commands/migrate/migrate-metadata.ts`

Upgrades a project's `.claude/metadata.json` file from legacy (v1/v2)
shape to the current v3 `installedModules` schema. Atomic: writes via
temp file → rename, leaves a `.bak` next to the original.

```
t1k migrate metadata               # rewrite project .claude/metadata.json
t1k migrate metadata --dry-run     # print before/after JSON, write nothing
t1k migrate metadata --global      # target ~/.claude/metadata.json instead
```

### Flags

| Flag | Purpose |
|---|---|
| `--dry-run` | Print "before" and "after" JSON to stdout; no files written |
| `-g, --global` | Target `~/.claude/metadata.json` instead of `<cwd>/.claude/metadata.json` |

### Behavior

- Detects "legacy shape" via `isLegacyMetadata()`: returns true when
  `schemaVersion !== 3` OR when legacy keys (`kits`, `modules`) are
  present at the top level.
- If `metadata.json` is already v3: prints "metadata.json is already v3 —
  no migration needed" and exits 0.
- Atomic write path: write to `.t1k-metadata-<ts>.json.tmp` beside the
  destination → `rename(metadata.json → metadata.json.bak)` →
  `rename(tmp → metadata.json)`. Both renames happen on the same mount,
  avoiding `EXDEV: cross-device link` failures.
- Auto-invoke: `autoMigrateMetadata()` runs silently from the
  `t1k update` kit-update prompt. Honors `T1K_SKIP_MIGRATION=1`.

### Gotchas

- **`.bak` is ALWAYS overwritten** if it already exists. If you ran the
  migration once and want to keep the previous backup, copy
  `metadata.json.bak` aside before re-running.
- **The escape hatch is `T1K_SKIP_MIGRATION=1`** — set this env var to
  skip the auto-invoke during `t1k update`. The interactive
  `t1k migrate metadata` command itself does NOT honor this var (only
  the auto-invoke does); use `--dry-run` to peek without writing.
- **Corrupt JSON in metadata.json** is a hard error in interactive mode
  (`metadata.json is not valid JSON: <path>`) but a silent skip for the
  auto-invoke (logs verbose, returns false). Maintainers debugging an
  upgrade should run the explicit command to surface the error.

---

## `t1k rollback` — Restore a per-kit pre-update snapshot

Source: `src/commands/rollback.ts` + `src/domains/installation/snapshot-manager.ts`

H7 snapshot-restore command. Restores a namespaced snapshot from
`~/.claude/.t1k-snapshots/<kit>/pre-<version>/` over `~/.claude/`. Used
to recover from a bad update.

```
t1k rollback --kit unity --to-snapshot pre-2.4.1
t1k rollback --kit core --to-snapshot pre-1.71.0 -y
```

### Flags

| Flag | Purpose |
|---|---|
| `--kit <name>` | REQUIRED. Kit whose snapshot to restore (e.g. `unity`, `web`, `core`). |
| `--to-snapshot <id>` | REQUIRED. Snapshot id; MUST be of the form `pre-<version>`. |
| `-y, --yes` | Declared in the CLI registry but currently unused (no-op as of v4.14.0). |

### Behavior

- Validates both flags, then validates snapshot-id matches `^pre-(.+)$`.
  Strips `pre-` to get the version, then resolves
  `~/.claude/.t1k-snapshots/<kit>/pre-<version>/`.
- If the snapshot directory does not exist: error
  `rollback: snapshot '<kit>/pre-<version>' does not exist at <path>` and
  exit 1.
- If found: `cpSync` recursive copy with `preserveTimestamps: true` over
  `~/.claude/`. Pre-existing files are NOT deleted — restore is a copy,
  not a sync.
- On success: prints `Rolled back <kit>/pre-<version> → <restoredTo>`.

### Snapshot lifecycle (READ THIS FIRST)

- Snapshots live at `~/.claude/.t1k-snapshots/<kit>/pre-<version>/`.
- Retention is **5 most-recent per kit** (`RETENTION_LIMIT` in
  `snapshot-manager.ts`). When exceeded, older snapshots are
  soft-moved to `~/.claude/.t1k-trash/<kit>/pre-<version>-<ts>/` —
  NEVER hard-deleted with `rm -rf`. The trash is a recovery path of
  last resort.
- The reserved kit name `core` (`CORE_KIT`) holds shared core files.
- `t1k doctor --backup-everything` prunes the older
  `~/.claude-backup-*/` directories (separate system from
  `.t1k-snapshots/`); it does NOT touch snapshots.

### Gotchas

- **No production code path currently creates snapshots automatically.**
  As of v4.14.0, `snapshot-manager.ts` exposes `createSnapshot()` and
  `addToSnapshot()`, but the only caller in production source is
  `rollback.ts` (read path). Until the install/update pipeline is wired
  to create snapshots, `t1k rollback` will fail with
  `snapshot '<kit>/pre-<version>' does not exist` for any project that
  was installed/updated before snapshot creation lands. Status: H7
  read-side shipped; write-side pending. Track via the H7 phase in
  `plans/reports/260422-1248-self-assembling-kit-architecture.md`.
- **`--yes` is currently a no-op** — declared in the CLI registry,
  never consumed by `rollbackCommand`. The skill should not promise
  confirmation-bypass behavior. Future-proof only.
- **`--to-snapshot` MUST literally start with `pre-`.** Passing the bare
  version (`--to-snapshot 2.4.1`) errors with `--to-snapshot must be of
  the form 'pre-<version>'`. The skill should always render the `pre-`
  prefix in examples.
- **Restore is a non-destructive copy.** Files added by the bad update
  are NOT removed by rollback — `cpSync` only writes the snapshot's
  files over their counterparts. If the bad update added wholly new
  files (new skills, new agents), those remain after rollback. Run
  `t1k doctor` afterwards to spot stragglers.
- **Per-kit isolation.** Rolling back `--kit unity` only touches files
  the unity kit owns. Files owned by other kits or by `core` are not
  reverted. To revert a multi-kit project, run rollback per kit in
  reverse install order.
- **Do not confuse with `t1k rollback-migration`.** That's a separate
  Phase 0.5 command that restores the most-recent
  `~/.claude/.t1k-settings-backup-<ts>` for the global `settings.json`
  only — different storage, different purpose, different command name.

### Recovery-of-last-resort

If `t1k rollback` fails because the snapshot is missing AND the project
is broken:

1. Check `~/.claude/.t1k-trash/<kit>/` for evicted snapshots; the
   directory name carries `pre-<version>-<ts>`. A maintainer can
   manually `mv` one back to `~/.claude/.t1k-snapshots/<kit>/pre-<v>`
   and retry the command.
2. If trash is also empty, fall back to `t1k install --reset` (the
   sanctioned destructive path; takes its own
   `~/.claude-backup-{ISO-ts}/` first).
3. **NEVER `rm -rf ~/.claude/`** — the <!-- gate:allow-rm-claude (rule statement) -->
   `validate-no-raw-rm-claude.cjs` gate forbids it in any shipped
   doc/script, and it destroys `.t1k-snapshots/`, `.t1k-trash/`, and
   any `ownership: user` files the user customized.

---

## Cross-cutting gotchas

- **Audit terminology drift** — issue #106 §15 references "module new"
  and "kit new" as if they were CLI subcommands. They're not. The
  shipped commands are `t1k new` (project bootstrap) and `t1k modules
  create` (module scaffold). When updating audits, use the actual CLI
  surface; the legacy names are an in-flight design doc, not real
  commands.
- **Maintainer vs end-user split** — `t1k migrate` (provider porting),
  `t1k migrate metadata` (schema upgrade), and `t1k rollback` are
  end-user commands. Maintainers run them too, but they're not gated
  on the `t1k-maintainer` module. `/t1k:kit migrate` and the rest of
  this skill ARE maintainer-only.
- **CLI version pin** — this reference targets `theonekit-cli@v4.14.0`.
  When the CLI ships a new release that changes any flag here, regenerate
  this file from source rather than patching individual lines.
