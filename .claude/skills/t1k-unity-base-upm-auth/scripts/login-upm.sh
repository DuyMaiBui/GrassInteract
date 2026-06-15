#!/usr/bin/env bash
# t1k-origin: kit=theonekit-unity | repo=The1Studio/theonekit-unity | module=base | protected=false
#
# login-upm.sh — Non-interactive Unity UPM (Verdaccio) authentication.
#
# Single cross-platform replacement for the interactive setup-unity-upm-auth.ps1
# (Windows) and login-upm-macos.sh (macOS). One script, three platforms:
#   - Windows : run under Git Bash; installs Node.js via winget
#   - macOS   : installs Node.js via Homebrew
#   - Linux   : installs Node.js via pacman (Arch) or apt-get (Debian/Ubuntu)
#
# Auth is credentials-only and fully non-interactive — it calls the Verdaccio
# user endpoint directly instead of `npm login`:
#
#   PUT <registry>/-/user/org.couchdb.user/<username>
#   { "name": <username>, "password": <password>, ... }  ->  201 { "token": ... }
#
# The returned token is written to ~/.upmconfig.toml under [npmAuth."<registry>"],
# which is exactly where Unity reads UPM scoped-registry credentials (on Windows
# Git Bash, $HOME maps to %USERPROFILE%, so the same path applies).
#
# Required environment variables (NO interactive prompt — missing = exit 1):
#   UPM_USERNAME   registry username
#   UPM_PASSWORD   registry password
# Optional:
#   UPM_EMAIL      registry email (some Verdaccio configs require it)
#   UPM_REGISTRY   registry URL (default: https://upm.the1studio.org/)
#                  also overridable as positional arg $1
#
# Exit codes: 0 success | 1 missing creds / unsupported OS / install fail / auth fail
#
# NOTE (validated against upm.the1studio.org, 2026-06-04): this Verdaccio requires
# the HTTP Basic auth header to authenticate — the JSON body alone returns 409
# "user registration disabled" regardless of credentials. Credentials are sent via
# a 0600 curl config file (-K), never on the command line. The full npm field set
# in the body (_id, name, password, type, roles, date [, email]) is kept for
# compatibility with standard Verdaccio login.

set -euo pipefail

REGISTRY_URL="${1:-${UPM_REGISTRY:-https://upm.the1studio.org/}}"

# --- Step 1: require credentials (non-interactive, no prompt) ----------------
if [[ -z "${UPM_USERNAME:-}" || -z "${UPM_PASSWORD:-}" ]]; then
  cat >&2 <<'EOF'
Error: UPM_USERNAME and UPM_PASSWORD must be set (non-interactive auth).

  export UPM_USERNAME="your-user"
  export UPM_PASSWORD="your-pass"
  ./login-upm.sh [registry-url]

Optional: UPM_EMAIL, UPM_REGISTRY (default https://upm.the1studio.org/).
EOF
  exit 1
fi

# Reject usernames that would produce a malformed endpoint path (also blocks any
# path-injection via the username). Verdaccio usernames are [A-Za-z0-9._-].
if [[ ! "$UPM_USERNAME" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "Error: UPM_USERNAME contains unsupported characters (allowed: letters, digits, . _ -)." >&2
  exit 1
fi

# Normalise trailing slash.
[[ "$REGISTRY_URL" == */ ]] || REGISTRY_URL="${REGISTRY_URL}/"
UPM_EMAIL="${UPM_EMAIL:-}"

# --- Step 2: detect platform -------------------------------------------------
case "$(uname -s)" in
  Darwin)            PLATFORM="macos"   ;;
  Linux)             PLATFORM="linux"   ;;
  MINGW*|MSYS*|CYGWIN*) PLATFORM="windows" ;;
  *)
    echo "Error: unsupported OS '$(uname -s)'. Supported: macOS, Linux (Arch/Debian), Windows (Git Bash)." >&2
    exit 1
    ;;
esac

# --- Step 3: ensure node + npm -----------------------------------------------
install_node() {
  case "$PLATFORM" in
    macos)
      if ! command -v brew >/dev/null 2>&1; then
        echo "Error: Homebrew not found. Install from https://brew.sh then re-run." >&2
        exit 1
      fi
      echo "Installing Node.js via Homebrew..."
      brew install node
      ;;
    linux)
      if command -v pacman >/dev/null 2>&1; then
        echo "Installing Node.js via pacman (Arch)..."
        sudo pacman -S --needed --noconfirm nodejs npm
      elif command -v apt-get >/dev/null 2>&1; then
        echo "Installing Node.js via apt-get (Debian/Ubuntu)..."
        sudo apt-get update
        sudo apt-get install -y nodejs npm
      else
        echo "Error: no supported package manager (pacman/apt-get) found. Install Node.js manually, then re-run." >&2
        exit 1
      fi
      ;;
    windows)
      if ! command -v winget >/dev/null 2>&1; then
        echo "Error: winget not found. Install Node.js manually from https://nodejs.org then re-run." >&2
        exit 1
      fi
      echo "Installing Node.js via winget..."
      winget install --id OpenJS.NodeJS --source winget --accept-source-agreements --accept-package-agreements
      ;;
  esac
}

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  install_node
  if ! command -v node >/dev/null 2>&1; then
    if [[ "$PLATFORM" == "windows" ]]; then
      echo "Node.js installed but not on PATH in this shell. Close and reopen Git Bash, then re-run this script." >&2
    else
      echo "Error: Node.js installation failed." >&2
    fi
    exit 1
  fi
fi

# --- Step 4: acquire auth token from Verdaccio -------------------------------
# Build the JSON body with node so user/password are escaped safely. Credentials
# are passed via the environment (never argv, never echoed).
if ! REQUEST_BODY="$(
  UPM_USERNAME="$UPM_USERNAME" UPM_PASSWORD="$UPM_PASSWORD" UPM_EMAIL="$UPM_EMAIL" \
  node -e '
    const name = process.env.UPM_USERNAME;
    const password = process.env.UPM_PASSWORD;
    const email = process.env.UPM_EMAIL || "";
    const body = {
      _id: "org.couchdb.user:" + name,
      name,
      password,
      type: "user",
      roles: [],
      date: new Date().toISOString(),
    };
    if (email) body.email = email;
    process.stdout.write(JSON.stringify(body));
  '
)"; then
  echo "Error: failed to build the auth request (node)." >&2
  exit 1
fi

USER_ENDPOINT="${REGISTRY_URL}-/user/org.couchdb.user/${UPM_USERNAME}"

# This Verdaccio authenticates via the HTTP Basic auth header — the JSON body alone
# is NOT enough (it returns 409 "user registration disabled"). Pass the credentials
# to curl through a 0600 config file read with -K so they never appear in the
# process argv (/proc/<pid>/cmdline); the body still goes via stdin (--data-binary).
CURL_CFG="$(mktemp)"
chmod 600 "$CURL_CFG" 2>/dev/null || true
trap 'rm -f "$CURL_CFG"' EXIT
# Escape backslash and double-quote for curl config quoting. Username is validated
# to [A-Za-z0-9._-]; curl splits user:pass on the FIRST colon, so a password
# containing ':' is preserved intact.
esc_pw="${UPM_PASSWORD//\\/\\\\}"; esc_pw="${esc_pw//\"/\\\"}"
printf 'user = "%s:%s"\n' "$UPM_USERNAME" "$esc_pw" >"$CURL_CFG"
unset esc_pw

# `if !` keeps errexit from aborting on a network-level failure so we can emit a
# clear message. HTTP 4xx/5xx still return 0 here (no -f) and are handled below via
# the captured status code.
if ! HTTP_RESPONSE="$(
  printf '%s' "$REQUEST_BODY" | curl -sS -X PUT "$USER_ENDPOINT" \
    -K "$CURL_CFG" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json" \
    --data-binary @- \
    --max-time 30 --retry 2 --retry-connrefused \
    -w $'\n%{http_code}'
)"; then
  echo "Error: could not reach registry $REGISTRY_URL." >&2
  exit 1
fi
rm -f "$CURL_CFG"; trap - EXIT

HTTP_CODE="${HTTP_RESPONSE##*$'\n'}"
HTTP_BODY="${HTTP_RESPONSE%$'\n'*}"

parse_json_field() {
  # $1 = field name; reads JSON from stdin; prints field value or empty.
  FIELD="$1" node -e '
    let s=""; process.stdin.on("data",d=>s+=d).on("end",()=>{
      try { const j=JSON.parse(s); const v=j&&j[process.env.FIELD]; process.stdout.write(v?String(v):""); }
      catch(_) { process.stdout.write(""); }
    });'
}

if [[ "$HTTP_CODE" != "200" && "$HTTP_CODE" != "201" ]]; then
  echo "Error: registry login failed (HTTP $HTTP_CODE)." >&2
  ERR_MSG="$(printf '%s' "$HTTP_BODY" | parse_json_field error)"
  [[ -z "$ERR_MSG" ]] && ERR_MSG="$(printf '%s' "$HTTP_BODY" | parse_json_field message)"
  [[ -n "$ERR_MSG" ]] && echo "  registry: $ERR_MSG" >&2
  if [[ "$HTTP_CODE" == "401" || "$HTTP_CODE" == "409" ]]; then
    echo "  hint: check UPM_USERNAME / UPM_PASSWORD — this registry reports auth" >&2
    echo "        failures as 409 \"user registration disabled\"." >&2
  fi
  exit 1
fi

AUTH_TOKEN="$(printf '%s' "$HTTP_BODY" | parse_json_field token)"
if [[ -z "$AUTH_TOKEN" ]]; then
  echo "Error: registry returned no auth token." >&2
  exit 1
fi

# --- Step 5: write ~/.upmconfig.toml -----------------------------------------
UPM_CONFIG_PATH="$HOME/.upmconfig.toml"

# The token is a long-lived secret at rest — create newly with 0600 and tighten an
# existing file. (chmod is a partial no-op on Windows Git Bash; %USERPROFILE% is
# already per-user there.)
if [[ ! -f "$UPM_CONFIG_PATH" ]]; then
  ( umask 077; : >"$UPM_CONFIG_PATH" )
fi
chmod 600 "$UPM_CONFIG_PATH" 2>/dev/null || true

# Remove any existing section for this registry (exact-string match, ported from
# login-upm-macos.sh), then strip trailing blank lines so repeated runs don't
# accumulate them.
TMP_UPM="$(mktemp)"
awk -v target="[npmAuth.\"$REGISTRY_URL\"]" '
  BEGIN { skip = 0 }
  $0 == target { skip = 1; next }
  /^\[/ && skip == 1 { skip = 0 }
  skip == 0 { print }
' "$UPM_CONFIG_PATH" >"$TMP_UPM"
awk 'NF { last = NR } { line[NR] = $0 } END { for (i = 1; i <= last; i++) print line[i] }' \
  "$TMP_UPM" >"$UPM_CONFIG_PATH"
rm -f "$TMP_UPM"

{
  # Single blank-line separator, only when the file already has content.
  [[ -s "$UPM_CONFIG_PATH" ]] && printf '\n'
  printf '[npmAuth."%s"]\n' "$REGISTRY_URL"
  printf 'token = "%s"\n' "$AUTH_TOKEN"
  printf 'alwaysAuth = true\n'
} >>"$UPM_CONFIG_PATH"

# --- Step 6: verify + report (never echo token/password) ---------------------
if ! grep -qF "[npmAuth.\"$REGISTRY_URL\"]" "$UPM_CONFIG_PATH"; then
  echo "Error: failed to write auth section to $UPM_CONFIG_PATH." >&2
  exit 1
fi

echo "=== Setup Complete ==="
echo "Registry : $REGISTRY_URL"
echo "Config   : $UPM_CONFIG_PATH"
echo "Please restart Unity for changes to take effect."
