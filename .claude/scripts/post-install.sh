#!/bin/bash
# t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
# post-install.sh — runs after `t1k modules add model-router`
#
# v1.0+ contract: opencode-go is HOSTED at oc-go-cc.the1studio.org. No local
# binary install. No API key to manage client-side. Single credential is
# `gh auth token` (verified for The1Studio org membership server-side).
#
# This script just verifies prerequisites + emits a quick-start hint.
# Idempotent — safe to re-run.

set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

ok()   { echo -e "${GREEN}[OK]${NC} $1"; }
warn() { echo -e "${YELLOW}[!]${NC} $1"; }
fail() { echo -e "${RED}[X]${NC} $1"; }

echo ""
echo "=== model-router post-install ==="
echo ""

# ─── 1. GitHub CLI + auth (the ONE credential model-router needs) ───
if ! command -v gh &>/dev/null; then
  fail "gh CLI not installed — required for all providers"
  echo "  Install: https://cli.github.com/"
  exit 1
fi

if ! gh auth token &>/dev/null; then
  fail "gh not authenticated"
  echo "  Run: gh auth login --scopes read:packages,read:org,repo"
  exit 1
fi
ok "gh CLI authenticated"

# ─── 2. Hosted oc-go-cc reachable ───
OC_ENDPOINT="${OPENCODE_GO_ENDPOINT:-https://oc-go-cc.the1studio.org}"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "${OC_ENDPOINT}/health" 2>/dev/null || echo "000")
case "$HTTP_CODE" in
  200)      ok "${OC_ENDPOINT} reachable (auth-proxy alive)" ;;
  401|403)  ok "${OC_ENDPOINT} reachable (HTTP $HTTP_CODE — auth-proxy alive, real calls need The1Studio org membership)" ;;
  000)      warn "${OC_ENDPOINT} unreachable — opencode-go provider unavailable until the server is back" ;;
  *)        warn "${OC_ENDPOINT} returned HTTP $HTTP_CODE — opencode-go provider may not work" ;;
esac

# ─── 3. CCS auth-proxy reachable (kimi/codex direct routes) ───
if curl -s --max-time 5 "https://ccs.the1studio.org/health" > /dev/null 2>&1; then
  ok "ccs.the1studio.org reachable (kimi/codex direct routes available)"
else
  warn "ccs.the1studio.org unreachable — kimi/codex providers unavailable; opencode-go still works"
fi

# ─── 4. Sync mr-* hooks to $HOME/.claude/hooks/ (closes #52) ───
# settings.json registers each hook via a `node -e` wrapper that resolves
# to $HOME/.claude/hooks/mr-*.cjs (PR #50's HOME-override workaround
# requires the hooks to live there). But `t1k modules update` copies the
# kit into the consumer's project-local .claude/, not into $HOME. Without
# this sync step, every hook fires Cannot find module 'mr-*.cjs' — the
# Stop hook surfaces it loudly; SessionStart / PostToolUse fail silently.
GLOBAL_HOOKS_DIR="${HOME}/.claude/hooks"
PROJECT_HOOKS_DIR="${PWD}/.claude/hooks"
if [[ -d "$PROJECT_HOOKS_DIR" ]]; then
  mkdir -p "$GLOBAL_HOOKS_DIR"
  HOOKS_COPIED=0
  for h in "$PROJECT_HOOKS_DIR"/mr-*.cjs; do
    [[ -f "$h" ]] || continue
    cp "$h" "$GLOBAL_HOOKS_DIR/$(basename "$h")"
    HOOKS_COPIED=$((HOOKS_COPIED + 1))
  done
  if (( HOOKS_COPIED > 0 )); then
    ok "Synced $HOOKS_COPIED mr-*.cjs hook(s) → \$HOME/.claude/hooks/"
  fi
fi

# ─── 5. Merge hook ENTRIES into $HOME/.claude/settings.json (closes #68) ───
# Step 4 syncs the hook FILES, but Claude Code won't invoke them unless
# settings.json has matching `hooks` entries. Other kits routinely
# overwrite settings.json on their own installs without preserving model-
# router; without an idempotent merge here, transparent routing silently
# stops firing (hook files on disk, no registration, no error message).
GLOBAL_SETTINGS="${HOME}/.claude/settings.json"
KIT_SETTINGS="${PWD}/.claude/settings.json"
if [[ -f "$KIT_SETTINGS" ]] && command -v jq &>/dev/null; then
  if [[ ! -f "$GLOBAL_SETTINGS" ]]; then
    mkdir -p "$(dirname "$GLOBAL_SETTINGS")"
    echo '{"hooks":{}}' > "$GLOBAL_SETTINGS"
  fi
  if grep -qE 'mr-task-interceptor\.cjs' "$GLOBAL_SETTINGS"; then
    ok "settings.json already registers model-router hooks — skip merge"
  else
    BACKUP="${GLOBAL_SETTINGS}.backup-$(date +%Y%m%d-%H%M%S)"
    cp "$GLOBAL_SETTINGS" "$BACKUP"
    if jq -s '
      .[0] as $g | .[1] as $k |
      $g
      | .hooks.PreToolUse       = (($g.hooks.PreToolUse       // []) + ($k.hooks.PreToolUse       // []))
      | .hooks.PostToolUse      = (($g.hooks.PostToolUse      // []) + ($k.hooks.PostToolUse      // []))
      | .hooks.SessionStart     = (($g.hooks.SessionStart     // []) + ($k.hooks.SessionStart     // []))
      | .hooks.UserPromptSubmit = (($g.hooks.UserPromptSubmit // []) + ($k.hooks.UserPromptSubmit // []))
      | .hooks.Stop             = (($g.hooks.Stop             // []) + ($k.hooks.Stop             // []))
    ' "$GLOBAL_SETTINGS" "$KIT_SETTINGS" > "${GLOBAL_SETTINGS}.tmp" 2>/dev/null; then
      if [[ -s "${GLOBAL_SETTINGS}.tmp" ]] \
         && jq -e 'type == "object" and (.hooks | type == "object")' "${GLOBAL_SETTINGS}.tmp" >/dev/null 2>&1; then
        mv "${GLOBAL_SETTINGS}.tmp" "$GLOBAL_SETTINGS"
        ok "Registered model-router hooks in settings.json (backup: $BACKUP)"
      else
        warn "Merged settings.json failed validation — restoring from $BACKUP"
        rm -f "${GLOBAL_SETTINGS}.tmp"
        cp "$BACKUP" "$GLOBAL_SETTINGS"
      fi
    else
      warn "jq merge failed — original settings.json preserved (backup: $BACKUP)"
      rm -f "${GLOBAL_SETTINGS}.tmp"
    fi
  fi
elif [[ ! -f "$KIT_SETTINGS" ]]; then
  warn "Kit settings.json not at $KIT_SETTINGS — skipping hook-registry merge"
else
  warn "jq not installed — model-router hooks WILL NOT FIRE without manual registration"
  echo "  Install jq, then re-run: bash .claude/scripts/post-install.sh"
fi

# ─── 6. Deploy default config files (idempotent — don't clobber existing) ───
# Without these the interceptor exits 'pass-no-config' on every Task spawn —
# debug.jsonl shows the symptom but routing never fires.
for cf in t1k-config-mr.json providers-config.json; do
  src="${PWD}/.claude/${cf}"
  dst="${HOME}/.claude/${cf}"
  if [[ -f "$src" ]] && [[ ! -f "$dst" ]]; then
    cp "$src" "$dst"
    ok "Deployed default ${cf} → \$HOME/.claude/"
  elif [[ -f "$dst" ]]; then
    ok "${cf} already at \$HOME/.claude/ — keeping consumer's existing config"
  fi
done

# ─── 7. Local state directory ───
mkdir -p "${HOME}/.model-router"
ok "Log directory: ~/.model-router/"

# ─── 8. Provider aliveness probe (warm the cache for the first session) ───
PROBE_SH="${PWD}/.claude/scripts/mr-probe-providers.sh"
if [[ -x "$PROBE_SH" ]]; then
  if bash "$PROBE_SH" >/dev/null 2>&1; then
    ok "Provider probe cache populated"
  else
    warn "Provider probe failed — run later: bash .claude/scripts/mr-probe-providers.sh"
  fi
fi

# ─── 9. Quick start hint ───
echo ""
echo "=== model-router ready ==="
echo ""
echo "Transparent routing is ON by default — Task spawns auto-route to cheap"
echo "models when the agent's model: frontmatter maps via modelMapping in"
echo ".claude/t1k-config-mr.json. No further action needed."
echo ""
echo "Manual delegation:"
echo "  bash .claude/scripts/mr-delegate.sh <agent-name> \"<task>\" --provider opencode-go --model kimi-k2.6"
echo ""
echo "Disable telemetry:    export MR_TELEMETRY_DISABLE=1"
echo "Enable failover:      export MR_FAILOVER_ENABLED=1"
echo "List live providers:  bash .claude/scripts/mr-probe-providers.sh"
echo "Doctor:               bash .claude/scripts/mr-doctor.sh"
echo ""
