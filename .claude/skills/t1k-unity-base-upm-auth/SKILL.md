---
name: t1k:unity:base:upm-auth
description: "Set up Unity UPM (Verdaccio) scoped-registry auth non-interactively. Drives scripts/login-upm.sh to write ~/.upmconfig.toml from UPM_USERNAME/UPM_PASSWORD. Triggers: upm auth, unity registry login, upmconfig, the1studio registry, 401 from upm registry, package registry credentials."
effort: medium
context: fork
keywords: [upm auth, upmconfig, verdaccio, unity registry, the1studio registry, npm login, scoped registry]
version: 2.4.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity UPM Auth Setup

Configure Unity's UPM scoped-registry credentials non-interactively by driving
`scripts/login-upm.sh`. The script authenticates against the Verdaccio registry
directly (no interactive `npm login`) and writes the auth token to
`~/.upmconfig.toml`, where Unity reads UPM credentials.

This skill is the **human/orchestration layer**; `scripts/login-upm.sh` is the
deterministic primitive. The skill gathers credentials, exports them into the
script's environment, runs it, and verifies the result. Credentials are passed
via the environment only (never argv) and are never echoed.

## When to use

- First-time setup of `the1studio` (or any Verdaccio) UPM registry on a dev machine.
- Unity reports 401 / "no auth token" resolving scoped-registry packages.
- Rotating a registry credential into `~/.upmconfig.toml`.

## Inputs

| Variable | Required | Default |
|---|---|---|
| `UPM_USERNAME` | yes | — |
| `UPM_PASSWORD` | yes | — |
| `UPM_EMAIL` | no | — (some Verdaccio configs require it) |
| `UPM_REGISTRY` | no | `https://upm.the1studio.org/` |

## Workflow

1. **Locate the script.** `scripts/login-upm.sh` ships with this skill. Copy it to
   the consuming repo (root or `Tools/`) or run it in place.
2. **Gather credentials.** If `UPM_USERNAME`/`UPM_PASSWORD` are already exported,
   use them. Otherwise collect them with `AskUserQuestion` (or read from a secret
   manager). **Never** print the values back, and never write them to a file.
3. **Run non-interactively** — pass credentials through the environment, not argv:
   ```bash
   UPM_USERNAME="$UPM_USERNAME" UPM_PASSWORD="$UPM_PASSWORD" \
   UPM_REGISTRY="${UPM_REGISTRY:-https://upm.the1studio.org/}" \
     bash ./scripts/login-upm.sh
   ```
4. **Check the exit code.** `0` = success; `1` = missing creds / unsupported OS /
   Node install failure / registry unreachable / auth rejected. On non-zero,
   surface the script's stderr verbatim (it never contains the password/token).
5. **Verify** the config block exists:
   ```bash
   grep -F "[npmAuth.\"${UPM_REGISTRY:-https://upm.the1studio.org/}\"]" "$HOME/.upmconfig.toml"
   ```
6. **Tell the user to restart Unity** for changes to take effect.

## Platforms

- **Linux** — Node auto-installed via `pacman` (Arch) or `apt-get` (Debian/Ubuntu).
- **macOS** — Node auto-installed via Homebrew.
- **Windows** — run under **Git Bash**; Node auto-installed via `winget`.
  `$HOME` = `%USERPROFILE%`, exactly where Unity reads `.upmconfig.toml`.

## Gotchas

- **Credentials-only, non-interactive by design.** There is no token-paste path and
  no interactive prompt. If `UPM_USERNAME`/`UPM_PASSWORD` are unset the script exits
  1 immediately — the skill must supply them.
- **Basic auth is required (validated 2026-06-04 against upm.the1studio.org).** This
  Verdaccio authenticates via the HTTP Basic auth header, NOT the JSON body. A
  body-only `PUT /-/user/org.couchdb.user/<user>` returns `409 "user registration
  disabled"` for *every* credential (valid, invalid, or unknown user) — so 409 here
  means "auth failed," not "wrong body." The script sends credentials via a `0600`
  curl config file (`-K`) so they stay out of argv. The registry's web-login route
  (`POST /-/v1/login`) is not enabled (404), so legacy/Basic is the only path.
- **2FA hard-blocks.** Credentials-only login cannot satisfy 2FA. Use an automation
  account without 2FA for CI.
- **Windows winget PATH.** A fresh Node install via winget may not be on PATH in the
  current shell — the script exits 1 with a "reopen Git Bash" message. Pre-install
  Node to avoid it.
- **CI `sudo`.** On Linux CI, package install needs `sudo`/network. Prefer a runner
  image with Node preinstalled so the script only writes the config.

## Related

- `scripts/login-upm.sh` — the cross-platform primitive this skill drives.
- `references/ci-usage.md` — CI / GitHub Actions usage with secrets.
- `t1k:unity:base:addressables`, `t1k:unity:base:text-config` — adjacent Unity infra.
