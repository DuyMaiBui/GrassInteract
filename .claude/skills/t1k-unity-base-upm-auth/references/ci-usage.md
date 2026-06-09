---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Unity UPM Auth — `login-upm.sh`

Non-interactive setup of Unity UPM (Verdaccio) scoped-registry credentials. One
cross-platform bash script for Windows (Git Bash), macOS, and Linux. Replaces the
old interactive `setup-unity-upm-auth.ps1` and `login-upm-macos.sh`.

It calls the Verdaccio user endpoint directly (no interactive `npm login`) and
writes the returned token to `~/.upmconfig.toml`, where Unity reads UPM
credentials.

## Requirements

- **Credentials** supplied via environment variables (never prompted):
  - `UPM_USERNAME` — registry username **(required)**
  - `UPM_PASSWORD` — registry password **(required)**
  - `UPM_EMAIL` — registry email *(optional; some Verdaccio configs require it)*
  - `UPM_REGISTRY` — registry URL *(optional; default `https://upm.the1studio.org/`;
    also accepted as positional arg `$1`)*
- **Node.js + npm.** Auto-installed if missing: Homebrew (macOS), `pacman` (Arch),
  `apt-get` (Debian/Ubuntu), `winget` (Windows). In CI, prefer a runner image that
  already has Node so the script never needs `sudo`/`winget`.
- **Shell:** `bash`. On Windows run it under **Git Bash** (`$HOME` = `%USERPROFILE%`,
  exactly where Unity reads `.upmconfig.toml`).

## Local usage

```bash
export UPM_USERNAME="your-user"
export UPM_PASSWORD="your-pass"
# export UPM_REGISTRY="https://upm.the1studio.org/"   # optional override
./login-upm.sh
# → writes ~/.upmconfig.toml, then restart Unity
```

Override the registry inline:

```bash
UPM_USERNAME=u UPM_PASSWORD=p ./login-upm.sh https://upm.the1studio.org/
```

## Exit codes (CI gating)

| Code | Meaning |
|------|---------|
| `0`  | Success — token written to `~/.upmconfig.toml` |
| `1`  | Missing `UPM_USERNAME`/`UPM_PASSWORD`, unsupported OS, Node install failed, registry unreachable, auth rejected, or no token returned |

The script prints `=== Setup Complete ===` on success. It **never** echoes the
password or token; failures surface the registry's error message only.

## GitHub Actions

Store credentials as repository/organization secrets, then:

```yaml
jobs:
  unity-upm-auth:
    runs-on: ubuntu-latest      # node + apt-get preinstalled
    steps:
      - uses: actions/checkout@v4
      - name: Configure Unity UPM auth
        env:
          UPM_USERNAME: ${{ secrets.UPM_USERNAME }}
          UPM_PASSWORD: ${{ secrets.UPM_PASSWORD }}
          UPM_REGISTRY: https://upm.the1studio.org/
        run: |
          chmod +x ./login-upm.sh
          ./login-upm.sh        # non-zero exit fails the job automatically
```

`~/.upmconfig.toml` now exists on the runner; subsequent Unity license/build steps
resolve scoped-registry packages without prompting.

> **Self-hosted / Windows runners:** run under Git Bash. If Node was just installed
> via `winget`, the PATH may not refresh in the current shell — the script exits 1
> with a "reopen Git Bash" message. Pre-install Node in the runner image to avoid it.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `UPM_USERNAME and UPM_PASSWORD must be set` | Export both env vars before running. |
| `registry login failed (HTTP 401)` or `(HTTP 409) user registration disabled` | Auth failed — wrong `UPM_USERNAME`/`UPM_PASSWORD`. This Verdaccio reports *all* auth failures as 409, so 409 is a credentials problem, not a body-shape one. |
| `could not reach registry` | Network/DNS/firewall — runner cannot reach the registry host. |
| `Node.js installed but not on PATH` (Windows) | Reopen Git Bash, or pre-install Node. |
| 2FA enabled on the account | Credentials-only login cannot satisfy 2FA — use an account without it for automation. |

## Notes

- **Credentials-only, non-interactive by design.** No token-paste path and no
  interactive prompt — missing env vars fail fast (exit 1).
- Authentication uses the HTTP **Basic auth header** (validated against
  `upm.the1studio.org`), passed to curl via a `0600` config file so credentials
  never appear in the process command line. The JSON body alone does not
  authenticate on this registry (returns `409 "user registration disabled"`).
- Only `~/.upmconfig.toml` is written (what Unity needs). `.npmrc` is intentionally
  left untouched.
