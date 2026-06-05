#!/bin/bash
# t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
#
# mr-probe-providers.sh — Active provider-aliveness check.
#
# Hits each provider listed in .claude/providers-config.json with a minimal
# chat-completion request (4 tokens, single "ping" prompt) and records
# PASS/FAIL + latency + reason into a cache file. The cache is what
# mr-doctor-check.cjs reads at session start AND what mr-delegate.sh reads
# before delegating (so callers fail fast on dead providers instead of
# waiting 5 min for claude -p to give up).
#
# Why a real probe instead of just /health: /health only confirms the
# auth-proxy is up. The actual provider upstream (Kimi API, Codex/OpenAI
# pipeline, OpenCode Go subscription) can be down even when the proxy
# answers /health — verified 2026-05-25 with codex returning HTTP 502 on
# every chat request while /providers reported status=authenticated.
#
# Usage:
#   bash mr-probe-providers.sh             # probe all, write cache, print table
#   bash mr-probe-providers.sh --json      # machine-parseable output to stdout
#   bash mr-probe-providers.sh --refresh   # alias for the default (kept explicit)
#   bash mr-probe-providers.sh --cache-only # read existing cache, no network
#
# Cache file: ~/.model-router/provider-probe-cache.json
# TTL semantics: this script always writes a fresh cache. Consumers
# (mr-doctor-check.cjs, mr-delegate.sh) decide their own staleness window.

set -euo pipefail

JSON_OUT=0
CACHE_ONLY=0
for arg in "$@"; do
  case "$arg" in
    --json)       JSON_OUT=1 ;;
    --refresh)    : ;;  # default; no-op for clarity
    --cache-only) CACHE_ONLY=1 ;;
    -h|--help)
      sed -n '3,30p' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *)
      echo "[mr-probe] WARNING: unknown flag '$arg'" >&2 ;;
  esac
done

CACHE_DIR="${HOME}/.model-router"
CACHE_FILE="${CACHE_DIR}/provider-probe-cache.json"
mkdir -p "$CACHE_DIR"

# Single source of truth for the cache freshness window. Consumers
# (mr-doctor-check.cjs, mr-delegate.sh) read `valid_until` from the cache
# instead of redefining the TTL — no drift, one number to tune.
# 60 min = balances catching upstream recoveries quickly vs probe-call cost.
PROBE_TTL_S=3600

PROVIDERS_CFG=""
for candidate in \
  "${PWD}/.claude/providers-config.json" \
  "${CLAUDE_PROJECT_DIR:-}/.claude/providers-config.json" \
  "${HOME}/.claude/providers-config.json"
do
  [[ -n "$candidate" && -f "$candidate" ]] && { PROVIDERS_CFG="$candidate"; break; }
done

if [[ -z "$PROVIDERS_CFG" ]]; then
  echo "[mr-probe] ERROR: providers-config.json not found in cwd/CLAUDE_PROJECT_DIR/~/.claude" >&2
  exit 1
fi

# Cache-only mode: just emit what's on disk.
if [[ "$CACHE_ONLY" == "1" ]]; then
  if [[ ! -f "$CACHE_FILE" ]]; then
    echo "[mr-probe] no cache at $CACHE_FILE — run without --cache-only to populate" >&2
    exit 1
  fi
  cat "$CACHE_FILE"
  exit 0
fi

# Token for remote-proxy auth (kimi, codex, opencode-go all use gh token).
TOKEN=$(gh auth token 2>/dev/null || cat "${CACHE_DIR}/.t1k-gh-token-cache" 2>/dev/null || true)
if [[ -z "${TOKEN:-}" ]]; then
  echo "[mr-probe] ERROR: no gh auth token. Run: gh auth login --scopes read:org,repo" >&2
  exit 1
fi

# Build per-provider probe plan from providers-config.json.
# Pick the FIRST enabled model in each provider's `models` map (data-driven,
# no hardcoded mappings). Most providers list budget tier first; if not,
# the probe still works — we send max_tokens=4 so cost is negligible.
PROBE_PLAN=$(jq -rc '
  .providers
  | to_entries[]
  | select(.value.enabled == true)
  | {
      name: .key,
      endpoint: .value.endpoint,
      probe_model: (
        .value.models
        | to_entries
        | map(select(.value.enabled == true))
        | (.[0].key // null)
      )
    }
' "$PROVIDERS_CFG")

if [[ -z "$PROBE_PLAN" ]]; then
  echo "[mr-probe] ERROR: no enabled providers in $PROVIDERS_CFG" >&2
  exit 1
fi

# Run probes sequentially (3-4 providers, no parallelism needed).
TS=$(date -u +%FT%TZ)
RESULTS=()

while IFS= read -r entry; do
  name=$(echo "$entry" | jq -r '.name')
  endpoint=$(echo "$entry" | jq -r '.endpoint')
  model=$(echo "$entry" | jq -r '.probe_model // ""')

  if [[ -z "$model" || "$model" == "null" ]]; then
    RESULTS+=("$(jq -nc \
      --arg name "$name" --arg endpoint "$endpoint" \
      --arg status "skip" --arg reason "no enabled model in providers-config.json" \
      '{name:$name, endpoint:$endpoint, model:null, status:$status, http_code:null, latency_ms:null, reason:$reason}')")
    continue
  fi

  # Anthropic-format chat-completion against the provider's endpoint.
  # All three providers expose a v1/messages route under their base URL.
  url="${endpoint}/v1/messages"
  body=$(jq -nc --arg m "$model" \
    '{model:$m, max_tokens:4, messages:[{role:"user", content:"ping"}]}')

  start=$(date +%s%N)
  http_code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
    -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "$body" "$url" 2>/dev/null || echo "000")
  end=$(date +%s%N)
  latency=$(( (end - start) / 1000000 ))

  if [[ "$http_code" == "200" ]]; then
    status="pass"; reason="ok"
  elif [[ "$http_code" == "000" ]]; then
    status="fail"; reason="connection failed or timed out (>15s)"
  else
    status="fail"; reason="HTTP $http_code from $url"
  fi

  RESULTS+=("$(jq -nc \
    --arg name "$name" --arg endpoint "$endpoint" --arg model "$model" \
    --arg status "$status" --argjson http_code "$http_code" \
    --argjson latency_ms "$latency" --arg reason "$reason" \
    '{name:$name, endpoint:$endpoint, model:$model, status:$status, http_code:$http_code, latency_ms:$latency_ms, reason:$reason}')")
done <<< "$PROBE_PLAN"

# Assemble cache JSON. `valid_until` is the canonical freshness deadline
# read by consumers — they compare it to `now`, no TTL constant on their side.
VALID_UNTIL=$(date -u -d "+${PROBE_TTL_S} seconds" +%FT%TZ 2>/dev/null \
  || python3 -c "import datetime; print((datetime.datetime.utcnow() + datetime.timedelta(seconds=${PROBE_TTL_S})).strftime('%Y-%m-%dT%H:%M:%SZ'))")
CACHE_JSON=$(printf '%s\n' "${RESULTS[@]}" | jq -sc --arg ts "$TS" --arg until "$VALID_UNTIL" --argjson ttl "$PROBE_TTL_S" \
  '{generated_at:$ts, valid_until:$until, ttl_seconds:$ttl, providers:.}')
echo "$CACHE_JSON" > "$CACHE_FILE"

# Output.
if [[ "$JSON_OUT" == "1" ]]; then
  echo "$CACHE_JSON"
else
  echo "=== Provider aliveness probe — $TS ==="
  pass=0; fail=0; skip=0
  while IFS= read -r row; do
    name=$(echo "$row" | jq -r '.name')
    model=$(echo "$row" | jq -r '.model // "(no model)"')
    status=$(echo "$row" | jq -r '.status')
    latency=$(echo "$row" | jq -r '.latency_ms // 0')
    reason=$(echo "$row" | jq -r '.reason')
    case "$status" in
      pass) printf '  %-14s %-18s → PASS  %dms\n' "$name" "($model)" "$latency"; pass=$((pass+1)) ;;
      fail) printf '  %-14s %-18s → FAIL  %s\n'   "$name" "($model)" "$reason"; fail=$((fail+1)) ;;
      skip) printf '  %-14s %-18s → SKIP  %s\n'   "$name" "($model)" "$reason"; skip=$((skip+1)) ;;
    esac
  done < <(echo "$CACHE_JSON" | jq -c '.providers[]')
  echo
  total=$((pass+fail+skip))
  echo "$pass/$total alive, $fail dead, $skip skipped."
  echo "Cache: $CACHE_FILE"
fi

# Exit non-zero only if EVERY provider failed — partial failures are common
# (one upstream down) and shouldn't block a CI gate that runs this script.
# Consumers check the cache file for per-provider status.
if [[ ${#RESULTS[@]} -gt 0 ]]; then
  alive=$(echo "$CACHE_JSON" | jq '[.providers[] | select(.status == "pass")] | length')
  [[ "$alive" -gt 0 ]] && exit 0 || exit 1
fi
exit 0
