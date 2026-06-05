#!/bin/bash
# t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
# mr-doctor.sh — health check for model-router prerequisites (v3+)
# Usage: bash .claude/scripts/mr-doctor.sh

set -euo pipefail

# Mirrors the runtime fallback chain in mr-task-interceptor.cjs:105-107.
# Prints the resolved path and returns 0, or returns 1 if neither exists.
resolve_providers_config() {
  local p="${REPO_ROOT:-$PWD}/.claude/providers-config.json"
  if [[ -f "$p" ]]; then echo "$p"; return 0; fi
  local g="$HOME/.claude/providers-config.json"
  if [[ -f "$g" ]]; then echo "$g"; return 0; fi
  return 1
}

# Detect repo root (script may be at .claude/scripts/ or scripts/)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ "$SCRIPT_DIR" == */.claude/scripts ]]; then
  REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
elif [[ "$SCRIPT_DIR" == */scripts ]]; then
  REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
else
  REPO_ROOT="$(pwd)"
fi

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

PASS=0
WARN=0
FAIL=0

check_pass() { echo -e "${GREEN}[PASS]${NC} $1"; PASS=$((PASS + 1)); }
check_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; WARN=$((WARN + 1)); }
check_fail() { echo -e "${RED}[FAIL]${NC} $1"; FAIL=$((FAIL + 1)); }

echo "=== model-router doctor ==="
echo ""

# 1. Claude Code
if command -v claude &>/dev/null; then
  check_pass "Claude Code installed ($(claude --version 2>/dev/null | head -1))"
else
  check_fail "Claude Code not found"
fi

# 2. Hosted oc-go-cc reachable. v1.0+ removed the local-daemon mode —
# opencode-go is a hosted service at oc-go-cc.the1studio.org, gated by
# gh-token + The1Studio org membership.
OC_ENDPOINT="${OPENCODE_GO_ENDPOINT:-https://oc-go-cc.the1studio.org}"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "${OC_ENDPOINT}/health" 2>/dev/null || echo "000")
case "$HTTP_CODE" in
  200)           check_pass "${OC_ENDPOINT} reachable (200)" ;;
  401|403)       check_pass "${OC_ENDPOINT} auth-proxy alive (HTTP $HTTP_CODE — needs gh token for real calls)" ;;
  000)           check_fail "${OC_ENDPOINT} unreachable (network down or proxy offline)" ;;
  *)             check_warn "${OC_ENDPOINT} returned HTTP $HTTP_CODE" ;;
esac

# 3. CCS endpoint (for kimi/codex direct routes)
if curl -s --max-time 5 "https://ccs.the1studio.org/health" > /dev/null 2>&1; then
  check_pass "ccs.the1studio.org reachable (kimi/codex providers available)"
else
  check_warn "ccs.the1studio.org unreachable (kimi/codex direct routes down — opencode-go still works)"
fi

# 4. GitHub auth (single credential for all providers in v1.0+)
if command -v gh &>/dev/null; then
  if gh auth token &>/dev/null; then
    check_pass "gh auth token available"
  else
    check_fail "gh CLI installed but not authenticated. Run: gh auth login --scopes read:packages,read:org,repo"
  fi
else
  check_fail "gh CLI not installed — required for ALL providers"
fi

# 5. Kit's runtime files (v2+ — no agents, just hooks/scripts/skills)
for f in \
  "$REPO_ROOT/.claude/hooks/mr-task-interceptor.cjs:Task interceptor (PreToolUse:Task)" \
  "$REPO_ROOT/.claude/scripts/mr-delegate.sh:mr-delegate.sh (delegation entry point)" \
  "$REPO_ROOT/.claude/scripts/mr-probe-providers.sh:mr-probe-providers.sh (aliveness probe)" \
  "$REPO_ROOT/.claude/skills/t1k-model-router-delegate/SKILL.md:t1k-model-router-delegate skill" \
  "$REPO_ROOT/.claude/t1k-config-mr.json:t1k-config-mr.json"
do
  path="${f%%:*}"
  label="${f#*:}"
  if [[ -e "$path" ]]; then
    check_pass "$label present"
  else
    check_fail "$label missing ($path)"
  fi
done

# providers-config.json — check project-local first, then global (mirrors runtime)
if CFG_PATH=$(resolve_providers_config); then
  if [[ "$CFG_PATH" == "$HOME/.claude/providers-config.json" ]]; then
    check_pass "providers-config.json present (global: $CFG_PATH)"
  else
    check_pass "providers-config.json present"
  fi
else
  check_warn "providers-config.json not found in project or global (~/.claude/) — create one to enable capability-based routing"
fi

# 6. mr-delegate.sh + mr-probe-providers.sh executable
for s in mr-delegate.sh mr-probe-providers.sh; do
  if [[ -x "$REPO_ROOT/.claude/scripts/$s" ]]; then
    : # already counted under 5
  else
    check_warn "$s exists but not executable — chmod +x $REPO_ROOT/.claude/scripts/$s"
  fi
done

# 7. Capability tags (v2.1+ rule-based selector reads these)
if CFG_PATH=$(resolve_providers_config) && command -v jq &>/dev/null; then
  CFG_SCOPE="project"
  [[ "$CFG_PATH" == "$HOME/.claude/providers-config.json" ]] && CFG_SCOPE="global"
  UNTAGGED=$(jq -r '
    .providers | to_entries[] | select(.value.enabled == true)
    | .key as $p | .value.models | to_entries[]
    | select(.value.enabled == true)
    | select((.value.capabilities | type) != "array" or (.value.capabilities | length) == 0)
    | "\($p)/\(.key)"
  ' "$CFG_PATH" 2>/dev/null)
  if [[ -z "$UNTAGGED" ]]; then
    MODEL_COUNT=$(jq '[.providers | to_entries[] | select(.value.enabled == true) | .value.models | to_entries[] | select(.value.enabled == true)] | length' "$CFG_PATH")
    check_pass "Capability tags ($CFG_SCOPE): $MODEL_COUNT enabled models all tagged"
  else
    check_warn "Capability tags ($CFG_SCOPE) missing on: $(echo "$UNTAGGED" | tr '\n' ',' | sed 's/,$//')"
  fi
fi

# 8. Provider aliveness probe cache (v2.1+)
PROBE_CACHE="${HOME}/.model-router/provider-probe-cache.json"
if [[ -f "$PROBE_CACHE" ]] && command -v jq &>/dev/null; then
  FRESH=$(jq -r 'if .valid_until then (now < (.valid_until | fromdateiso8601)) else false end' "$PROBE_CACHE" 2>/dev/null)
  DEAD=$(jq -r '[.providers[] | select(.status == "fail") | .name] | join(",")' "$PROBE_CACHE" 2>/dev/null)
  if [[ "$FRESH" == "true" ]]; then
    if [[ -z "$DEAD" ]]; then
      check_pass "Probe cache fresh — all providers alive"
    else
      check_warn "Probe cache marks DEAD: $DEAD (refresh: bash $REPO_ROOT/.claude/scripts/mr-probe-providers.sh)"
    fi
  else
    check_warn "Probe cache stale — refresh: bash $REPO_ROOT/.claude/scripts/mr-probe-providers.sh"
  fi
else
  check_warn "No probe cache — run: bash $REPO_ROOT/.claude/scripts/mr-probe-providers.sh"
fi

# 9. Consumer's installed agent roster (v2+ ships zero agents; needs other kits)
SEEN_AGENTS=0
for dir in "$REPO_ROOT/.claude/agents" "${HOME}/.claude/agents"; do
  [[ -d "$dir" ]] && SEEN_AGENTS=$((SEEN_AGENTS + $(find "$dir" -name 't1k-*.md' 2>/dev/null | wc -l)))
done
if [[ "$SEEN_AGENTS" -gt 0 ]]; then
  check_pass "Consumer agent roster: $SEEN_AGENTS t1k-* agent(s) discoverable"
else
  check_warn "No t1k-* agents discoverable — install theonekit-core or another kit to give model-router something to delegate to"
fi

# 10. Local state directory
if [[ -d "${HOME}/.model-router" ]]; then
  if [[ -f "${HOME}/.model-router/calls.jsonl" ]]; then
    CALL_COUNT=$(wc -l < "${HOME}/.model-router/calls.jsonl" 2>/dev/null || echo "0")
    check_pass "~/.model-router/ exists ($CALL_COUNT delegation log entries)"
  else
    check_pass "~/.model-router/ exists (no delegations yet)"
  fi
else
  check_warn "~/.model-router/ missing — will be created on first delegation"
fi

# Summary
echo ""
echo "=== Results: ${PASS} pass, ${WARN} warn, ${FAIL} fail ==="

if [[ $FAIL -gt 0 ]]; then
  exit 1
fi
