#!/bin/bash
# t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
# model-router delegate script (mr-delegate.sh)
# Usage: mr-delegate.sh <agent-name> "<task>" --provider <provider> --model <model>
#
# Spawns a Claude Code session routed to a cheaper model via ANTHROPIC_BASE_URL.
#
# v2 contract (2026-05-25): first arg is the AGENT NAME (e.g. t1k-fullstack-developer,
# t1k-code-reviewer, t1k-tester). The script discovers the agent file in the
# consumer's installed roster, parses its frontmatter for safety params
# (permissionMode, maxTurns, maxBudgetUsd), and runs `claude -p --agent <name>
# --model <chosen-cheap-model>`. The 6 mr-* wrapper agents are gone — any
# t1k-* agent the consumer has installed is delegatable.
#
# Supported providers:
#   - opencode-go: hosted at oc-go-cc.the1studio.org (GLM, Kimi, Qwen, MiMo, MiniMax)
#   - kimi:       via ccs.the1studio.org auth proxy (Kimi K2/K2.5/K2.6)
#   - codex:      via ccs.the1studio.org auth proxy (GPT-5.1, o3)
#
# Claude (main session) reads model-capabilities.md to choose the best model.
# This script is a dumb executor — it does NOT choose agents or models.
#
# Env opt-outs:
#   MR_TELEMETRY_DISABLE=1      — skip telemetry reporting
#   MR_FAILOVER_ENABLED=1       — opt-in: retry once with fallback provider on 5xx
#                                  (env var wins over JSON config when set explicitly)
#   MR_FALLBACK_TO_ANTHROPIC=1  — opt-in: exit sentinel 42 when all cheap providers fail,
#                                  telling mr-task-interceptor.cjs to passthrough to Anthropic
#   MR_FAILOVER_CHAIN_JSON      — JSON object overriding the hardcoded _fallback_for() map,
#                                  e.g. '{"opencode-go":"kimi","kimi":"opencode-go"}'
#                                  (loaded automatically from t1k-config-mr.json failover.chain)
#
# JSON config (takes effect when env vars above are not explicitly set):
#   ~/.claude/t1k-config-mr.json: modelRouter.failover.{enabled, chain, fallbackToAnthropic}

set -euo pipefail

# ─── Sentinel exit codes (interceptor uses these) ───
readonly EXIT_ALL_PROVIDERS_FAILED=42

# ─── Read failover config from t1k-config-mr.json (env vars take precedence) ─
# Schema (current):
#   modelRouter.failover.pipe         — ORDERED [{provider, model}] hops
#   modelRouter.failover.perHopTimeoutSec — per-attempt budget (default 120)
# Backward-compat:
#   modelRouter.failover.chain        — circular {primary: fallback} map (deprecated)
#                                       Derived into a 2-hop pipe at runtime.
T1K_CONFIG="${HOME}/.claude/t1k-config-mr.json"
if [[ -f "$T1K_CONFIG" ]] && command -v jq &>/dev/null; then
  if [[ -z "${MR_FAILOVER_ENABLED:-}" ]]; then
    CFG_FAILOVER=$(jq -r '.modelRouter.failover.enabled // empty' "$T1K_CONFIG" 2>/dev/null)
    [[ "$CFG_FAILOVER" == "true" ]] && MR_FAILOVER_ENABLED=1
  fi
  if [[ -z "${MR_FALLBACK_TO_ANTHROPIC:-}" ]]; then
    CFG_FB_ANT=$(jq -r '.modelRouter.failover.fallbackToAnthropic // empty' "$T1K_CONFIG" 2>/dev/null)
    [[ "$CFG_FB_ANT" == "true" ]] && MR_FALLBACK_TO_ANTHROPIC=1
  fi
  if [[ -z "${MR_FAILOVER_PIPE_JSON:-}" ]]; then
    CFG_PIPE=$(jq -c '.modelRouter.failover.pipe // empty' "$T1K_CONFIG" 2>/dev/null)
    [[ "$CFG_PIPE" != "null" && -n "$CFG_PIPE" && "$CFG_PIPE" != "[]" ]] && MR_FAILOVER_PIPE_JSON="$CFG_PIPE"
  fi
  if [[ -z "${MR_FAILOVER_CHAIN_JSON:-}" ]]; then
    CFG_CHAIN=$(jq -c '.modelRouter.failover.chain // empty' "$T1K_CONFIG" 2>/dev/null)
    [[ "$CFG_CHAIN" != "null" && -n "$CFG_CHAIN" ]] && MR_FAILOVER_CHAIN_JSON="$CFG_CHAIN"
  fi
  if [[ -z "${MR_PER_HOP_TIMEOUT_SEC:-}" ]]; then
    CFG_HOP=$(jq -r '.modelRouter.failover.perHopTimeoutSec // empty' "$T1K_CONFIG" 2>/dev/null)
    [[ "$CFG_HOP" =~ ^[0-9]+$ ]] && MR_PER_HOP_TIMEOUT_SEC="$CFG_HOP"
  fi
fi
# Per-hop default — covers kimi-k2.6 at 138K input tokens (~67s observed)
# with ~80% margin. See #88 for measurement.
: "${MR_PER_HOP_TIMEOUT_SEC:=120}"

# ─── P0: Recursive delegation guard ───
if [[ "${MR_SPAWNED:-}" == "1" ]]; then
  echo "ERROR: Recursive delegation detected. Spawned sessions cannot delegate." >&2
  exit 1
fi

AGENT="${1:?Usage: mr-delegate.sh <agent-name> \"<task>\" --provider <provider> --model <model>}"
TASK="${2:?Missing task description}"
shift 2

# Defense-in-depth: agent names are basenames of files under .claude/agents/.
# Reject path separators, parent-dir tokens, or shell-meta chars before doing
# any path.join — otherwise an agent name like "../../etc/passwd" would
# escape the agents dir during file lookup.
if [[ ! "$AGENT" =~ ^[A-Za-z0-9._-]+$ ]] || [[ "$AGENT" == *..* ]]; then
  echo "ERROR: agent name '$AGENT' contains invalid characters. Allowed: [A-Za-z0-9._-]" >&2
  exit 1
fi

# ─── Parse flags ───
PROVIDER=""
MODEL=""
ORIGINAL_MODEL=""     # The agent's declared model frontmatter value (sonnet, opus, etc.)
                      # Passed by mr-task-interceptor.cjs so the savings event records
                      # the actual model swap (requestModel != routedModel). Falls back
                      # to MODEL when invoked directly via /t1k:model-router:delegate
                      # (the explicit Bash path, where no swap is involved).
SELECTION_SOURCE=""   # The selector decision label from the interceptor
                      # (e.g., "rules:reasoning", "tier:budget", "modelMapping:sonnet").
while [[ $# -gt 0 ]]; do
  case "$1" in
    --provider)
      [[ $# -lt 2 ]] && { echo "ERROR: --provider requires a value" >&2; exit 1; }
      PROVIDER="$2"; shift 2 ;;
    --profile)
      # Backward compat alias (deprecated)
      [[ $# -lt 2 ]] && { echo "ERROR: --profile requires a value (deprecated, use --provider)" >&2; exit 1; }
      echo "[mr] WARNING: --profile is deprecated, use --provider" >&2
      PROVIDER="$2"; shift 2 ;;
    --model)
      [[ $# -lt 2 ]] && { echo "ERROR: --model requires a value" >&2; exit 1; }
      MODEL="$2"; shift 2 ;;
    --original-model)
      [[ $# -lt 2 ]] && { echo "ERROR: --original-model requires a value" >&2; exit 1; }
      ORIGINAL_MODEL="$2"; shift 2 ;;
    --selection-source)
      [[ $# -lt 2 ]] && { echo "ERROR: --selection-source requires a value" >&2; exit 1; }
      SELECTION_SOURCE="$2"; shift 2 ;;
    *) echo "[mr] WARNING: Unrecognized flag '$1', ignoring" >&2; shift ;;
  esac
done
# Backfill: if interceptor didn't provide --original-model (explicit Bash path),
# treat requestModel as routedModel (no swap happened).
[[ -z "$ORIGINAL_MODEL" ]] && ORIGINAL_MODEL="$MODEL"

# ─── Resolve kit version for telemetry correlation ──────────────────────────
# Every event POST stamps the kit's own release version (e.g. "3.6.1") so the
# worker can correlate event behavior with kit versions — useful for tracking
# fix rollouts and answering "which consumers are on the fixed version yet?"
# Search order: project-local first (a kit-source clone might differ from
# global), then global, then "unknown" if neither is reachable.
MR_VERSION="unknown"
# Probe order: project-local module.json (kit source repo or consumer with
# repo-local install), then global module.json (rare), then global manifest
# (.t1k-manifest.json — what `t1k init -g` actually ships). Stop at the first
# hit. Without the manifest fallback every global install reports 'unknown'.
for candidate in \
  "${PWD}/.claude/modules/model-router/module.json" \
  "${HOME}/.claude/modules/model-router/module.json" \
  "${HOME}/.claude/modules/model-router/.t1k-manifest.json"; do
  if [[ -r "$candidate" ]] && command -v jq &>/dev/null; then
    v=$(jq -r '.version // "unknown"' "$candidate" 2>/dev/null)
    if [[ -n "$v" && "$v" != "null" && "$v" != "unknown" ]]; then
      MR_VERSION="$v"
      break
    fi
  fi
done

# ─── Mirror telemetry mode (opt-in) ─────────────────────────────────────────
# When MR_MIRROR_TELEMETRY=1 OR --mirror-telemetry is set, every event POST
# is also appended to ~/.model-router/telemetry-out.jsonl. Lets consumers
# inspect exactly what was sent to the worker without needing admin access.
# Each line is the event payload + a "_mirror" header for source tracking.
MIRROR_TELEMETRY_FILE="${HOME}/.model-router/telemetry-out.jsonl"
should_mirror_telemetry() {
  [[ "${MR_MIRROR_TELEMETRY:-}" == "1" ]]
}
mirror_telemetry_event() {
  # $1 = payload JSON. Writes one line to MIRROR_TELEMETRY_FILE if enabled.
  should_mirror_telemetry || return 0
  mkdir -p "$(dirname "$MIRROR_TELEMETRY_FILE")" 2>/dev/null
  printf '%s\n' "$1" >> "$MIRROR_TELEMETRY_FILE" 2>/dev/null || true
}

# ─── Validate required args ───
if [[ -z "$PROVIDER" ]]; then
  echo "ERROR: --provider is required. Available: opencode-go, kimi, codex" >&2
  echo "Hint: Claude selects the provider based on .claude/model-capabilities.md" >&2
  exit 1
fi
if [[ -z "$MODEL" ]]; then
  echo "ERROR: --model is required. See .claude/model-capabilities.md for options." >&2
  exit 1
fi

# ─── Agent discovery + frontmatter-driven safety params ───
# Search order: cwd → CLAUDE_PROJECT_DIR → ~/.claude. First match wins.
# Conservative fallback when frontmatter fields are missing: plan / 25 / $5 —
# users who want richer behavior add explicit fields to the agent's .md.
_find_agent_file() {
  local name="$1" candidate
  for base in "${PWD}/.claude/agents" "${CLAUDE_PROJECT_DIR:-}/.claude/agents" "${HOME}/.claude/agents"; do
    [[ -z "$base" || ! -d "$base" ]] && continue
    candidate="${base}/${name}.md"
    if [[ -f "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  return 1
}

AGENT_FILE=$(_find_agent_file "$AGENT") || {
  echo "ERROR: agent '$AGENT' not found in any of:" >&2
  echo "  ${PWD}/.claude/agents/${AGENT}.md" >&2
  echo "  ${CLAUDE_PROJECT_DIR:-(unset)}/.claude/agents/${AGENT}.md" >&2
  echo "  ${HOME}/.claude/agents/${AGENT}.md" >&2
  echo "Hint: AI should pick an agent from the consumer's installed t1k-* roster." >&2
  echo "      List candidates with: ls .claude/agents/t1k-*.md ~/.claude/agents/t1k-*.md" >&2
  exit 1
}

# Extract a single frontmatter scalar field. Empty string if missing/blank.
# Frontmatter is the block between the first two `---` lines.
_fm_field() {
  local field="$1" file="$2"
  awk -v fld="$field" '
    BEGIN { in_fm=0; n=0 }
    /^---[[:space:]]*$/ { n++; in_fm = (n==1); next }
    in_fm {
      if (match($0, "^"fld":[[:space:]]*")) {
        v = substr($0, RLENGTH+1)
        gsub(/^[[:space:]]+|[[:space:]]+$/, "", v)
        gsub(/^["'"'"']|["'"'"']$/, "", v)
        print v
        exit
      }
    }
  ' "$file"
}

MODE=$(_fm_field permissionMode "$AGENT_FILE")
TURNS=$(_fm_field maxTurns "$AGENT_FILE")
BUDGET=$(_fm_field maxBudgetUsd "$AGENT_FILE")

# Conservative defaults — see header comment.
MODE="${MODE:-plan}"
TURNS="${TURNS:-25}"
BUDGET="${BUDGET:-5}"

# Validate mode against Claude Code's accepted set.
case "$MODE" in
  plan|acceptEdits|bypassPermissions) ;;
  *) echo "[mr] WARNING: agent declares permissionMode='$MODE' (unrecognized); falling back to 'plan'" >&2; MODE=plan ;;
esac

# ─── Helper: resolve GitHub token for remote providers ───
_resolve_gh_token() {
  # `.gh-token-cache` (without prefix) is owned by the oc-go-cc daemon
  # which writes `<timestamp>\n<token>` — unsafe to read as a token. Use our
  # namespaced .t1k-gh-token-cache (same path mr-telemetry.cjs writes).
  MR_GH_TOKEN=$(gh auth token 2>/dev/null || cat "${HOME}/.model-router/.t1k-gh-token-cache" 2>/dev/null || true)
  if [[ -z "${MR_GH_TOKEN:-}" ]]; then
    echo "ERROR: GitHub token required for '$PROVIDER' provider. Run: gh auth login" >&2
    exit 1
  fi
}

# DEPRECATED — kept as a no-op for any external script that sources us.
# /health is a false-positive: CCS proxies (and oc-go-cc) answer /health
# in <1s even when their upstream completion paths are fully hung
# (verified live 2026-05-29, see #88). Liveness is now checked via
# _completion_probe(), called from _setup_endpoint().
_check_remote_health() {
  return 0
}

# _completion_probe(base_url, model) — verify the provider can actually
# complete a request, not just answer /health. Sends a 4-token "ping"
# Anthropic-format POST with a 5s timeout. Returns 0 on 2xx, 1 otherwise.
# Result memoized in $MR_PROBE_RESULTS for the lifetime of this script
# run so pipe hops don't re-probe the same provider needlessly.
declare -A MR_PROBE_RESULTS=()
_completion_probe() {
  local base_url="$1"
  local model="$2"
  local cache_key="${base_url}|${model}"

  if [[ -n "${MR_PROBE_RESULTS[$cache_key]:-}" ]]; then
    [[ "${MR_PROBE_RESULTS[$cache_key]}" == "pass" ]] && return 0 || return 1
  fi

  if [[ -z "${MR_GH_TOKEN:-}" ]]; then
    return 1
  fi

  local body http_code
  body=$(jq -nc --arg m "$model" \
    '{model:$m, max_tokens:4, messages:[{role:"user", content:"ping"}]}' 2>/dev/null) \
    || body='{"model":"'"$model"'","max_tokens":4,"messages":[{"role":"user","content":"ping"}]}'

  http_code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 \
    -X POST \
    -H "Authorization: Bearer $MR_GH_TOKEN" \
    -H "x-api-key: $MR_GH_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$body" \
    "${base_url}/v1/messages" 2>/dev/null || echo "000")

  if [[ "$http_code" =~ ^2[0-9][0-9]$ ]]; then
    MR_PROBE_RESULTS[$cache_key]="pass"
    return 0
  fi
  MR_PROBE_RESULTS[$cache_key]="fail"
  echo "[mr] completion-probe FAIL: $base_url model=$model http=$http_code" >&2
  return 1
}

# ─── Provider → endpoint resolution (SSOT) ───
CCS_ENDPOINT="https://ccs.the1studio.org"
SUPPORTED_PROVIDERS=(opencode-go kimi codex)
PROVIDER_FAILURE_RE='HTTP[ /]*(5[0-9][0-9])|503|502|500|429|INTERNAL_SERVER_ERROR|ECONNREFUSED|Connection refused|upstream connect error|service unavailable|[Rr]ate.?[Ll]imit|[Tt]oo [Mm]any [Rr]equests|[Qq]uota.exceeded'

# _setup_endpoint(provider, model) — resolve the upstream base URL for the
# given provider and verify it can actually complete a request (not just
# answer /health). Returns 0 on success (env vars set), 1 if the probe
# fails so the pipe loop can advance to the next hop.
_setup_endpoint() {
  local p="$1"
  local m="${2:-}"
  local base_url

  case "$p" in
    kimi)
      _resolve_gh_token
      base_url="${CCS_ENDPOINT}/api/provider/kimi"
      ;;
    codex)
      _resolve_gh_token
      base_url="${CCS_ENDPOINT}/api/provider/codex"
      ;;
    opencode-go)
      _resolve_gh_token
      base_url="${OPENCODE_GO_ENDPOINT:-https://oc-go-cc.the1studio.org}"
      ;;
    *)
      return 1
      ;;
  esac

  # Skip the inline probe when no model is supplied (e.g. external callers
  # that just want endpoint resolution). The pipe loop always passes one.
  if [[ -n "$m" ]] && ! _completion_probe "$base_url" "$m"; then
    return 1
  fi

  export ANTHROPIC_BASE_URL="$base_url"
  export ANTHROPIC_API_KEY="$MR_GH_TOKEN"
  return 0
}

# ─── Multi-hop failover pipe (#88) ──────────────────────────────────────
# Liveness is checked inline per hop via _completion_probe() — the previous
# pre-flight /health gate gave false positives (CCS + oc-go-cc answer /health
# even while their completion paths hang). The pipe loop below calls
# _setup_endpoint(provider, model) per hop, which runs the probe and skips
# dead hops in ~5s instead of burning the full per-hop budget.
# _build_pipe_hops: emit ordered "provider model" lines for the failover pipe.
# First line is ALWAYS the CLI-supplied primary (so interceptor selection wins
# the head); subsequent lines come from `failover.pipe` (skipping any duplicate
# of the primary) or, when absent, a 2-hop derivation from the deprecated
# `failover.chain` map. Hardcoded final fallbacks preserve pre-#88 behavior.
_build_pipe_hops() {
  local primary_p="$1" primary_m="$2"
  echo "$primary_p $primary_m"

  if [[ -n "${MR_FAILOVER_PIPE_JSON:-}" ]] && command -v jq &>/dev/null; then
    echo "$MR_FAILOVER_PIPE_JSON" | jq -r --arg p "$primary_p" --arg m "$primary_m" \
      '.[] | select(.provider != $p or .model != $m) | "\(.provider) \(.model)"' 2>/dev/null
    return
  fi

  if [[ -n "${MR_FAILOVER_CHAIN_JSON:-}" ]] && command -v jq &>/dev/null; then
    local fb
    fb=$(echo "$MR_FAILOVER_CHAIN_JSON" | jq -r --arg p "$primary_p" '.[$p] // empty' 2>/dev/null)
    if [[ -n "$fb" ]]; then
      if [[ "$fb" == *" "* ]]; then
        echo "$fb"
      else
        case "$fb" in
          opencode-go) echo "opencode-go qwen3.5-plus" ;;
          kimi)        echo "kimi kimi-k2.6" ;;
          codex)       echo "codex codex-1" ;;
        esac
      fi
      return
    fi
  fi

  case "$primary_p" in
    opencode-go) echo "kimi kimi-k2.6" ;;
    kimi)        echo "opencode-go qwen3.5-plus" ;;
    codex)       echo "kimi kimi-k2.6" ;;
  esac
}

echo "[mr] Delegating: agent=$AGENT provider=$PROVIDER model=$MODEL mode=$MODE turns=$TURNS budget=\$$BUDGET" >&2

# ─── P0: Concurrent write lock (atomic mkdir) ───
LOCK_BASE="${HOME}/.model-router/locks"
mkdir -p "$LOCK_BASE"

if [[ "$MODE" == "acceptEdits" || "$MODE" == "bypassPermissions" ]]; then
  WRITE_LOCK="${LOCK_BASE}/write.lk"
  if ! mkdir "$WRITE_LOCK" 2>/dev/null; then
    LOCK_PID=$(cat "$WRITE_LOCK/pid" 2>/dev/null || true)
    if [[ -n "$LOCK_PID" ]] && kill -0 "$LOCK_PID" 2>/dev/null; then
      echo "ERROR: Another write-capable agent (PID $LOCK_PID) is running. Wait or kill it." >&2
      exit 1
    fi
    rm -rf "$WRITE_LOCK"
    mkdir "$WRITE_LOCK" 2>/dev/null || { echo "ERROR: Lock contention" >&2; exit 1; }
  fi
  echo $$ > "$WRITE_LOCK/pid"
  trap 'rm -rf "$WRITE_LOCK"' EXIT
fi

# ─── Logging ───
LOG_DIR="${HOME}/.model-router"
LOG_FILE="${LOG_DIR}/calls.jsonl"
mkdir -p "$LOG_DIR"

CALL_ID="$(date +%s)-$$"
START_TS=$(date -u +%FT%TZ)
START_SEC=$(date +%s)

TASK_LOG=$(echo "$TASK" | head -c 200)
[[ ${#TASK} -gt 200 ]] && TASK_LOG="${TASK_LOG}...[truncated]"

echo "{\"id\":\"${CALL_ID}\",\"ts\":\"${START_TS}\",\"agent\":\"${AGENT}\",\"provider\":\"${PROVIDER}\",\"model\":\"${MODEL}\",\"task\":$(echo "$TASK_LOG" | jq -Rs .),\"status\":\"start\"}" >> "$LOG_FILE"

# ─── Build command ───
CMD="claude"
CMD_ARGS=(
  "-p" "$TASK"
  "--agent" "$AGENT"
  "--model" "$MODEL"
  "--max-turns" "$TURNS"
  "--permission-mode" "$MODE"
  "--max-budget-usd" "$BUDGET"
  "--output-format" "json"
  "--disallowedTools" "Agent"
)

# ─── P0: Set spawn markers (consumed by mr-spawn-guard, mr-telemetry, mr-metrics) ───
export MR_SPAWNED=1
export MR_DELEGATE_AGENT="$AGENT"
export MR_DELEGATE_PARENT_PID=$$

# ─── Execute the pipe (#88) ──────────────────────────────────────────────
# Build the ordered hop list (head = interceptor-supplied primary, rest =
# failover.pipe / chain-derived). Iterate each hop with $MR_PER_HOP_TIMEOUT_SEC.
# On success: emit + stop. On provider failure: advance. On non-provider
# failure (real model error): emit + stop (don't mask bugs with failover).
PIPE_HOPS=()
while IFS= read -r line; do
  [[ -n "$line" ]] && PIPE_HOPS+=("$line")
done < <(_build_pipe_hops "$PROVIDER" "$MODEL")

# Failover disabled → only the primary runs.
if [[ "${MR_FAILOVER_ENABLED:-}" != "1" ]]; then
  PIPE_HOPS=("${PIPE_HOPS[0]}")
fi

# Iteration state preserved for the telemetry block below.
EXIT=99
STDOUT_LOG=""
STDERR_LOG=""
DURATION=0
FB_PROVIDER=""
FB_MODEL=""
HOP_IDX=0
PRIMARY_PROVIDER="$PROVIDER"

for hop in "${PIPE_HOPS[@]}"; do
  HOP_PROVIDER=$(echo "$hop" | awk '{print $1}')
  HOP_MODEL=$(echo "$hop" | awk '{print $2}')

  if ! _setup_endpoint "$HOP_PROVIDER" "$HOP_MODEL"; then
    echo "[mr] hop ${HOP_IDX}: ${HOP_PROVIDER} failed liveness probe, skipping" >&2
    HOP_IDX=$((HOP_IDX + 1))
    EXIT=1
    continue
  fi

  CMD_ARGS=(
    "-p" "$TASK"
    "--agent" "$AGENT"
    "--model" "$HOP_MODEL"
    "--max-turns" "$TURNS"
    "--permission-mode" "$MODE"
    "--max-budget-usd" "$BUDGET"
    "--output-format" "json"
    "--disallowedTools" "Agent"
  )

  HOP_CALL_ID="${CALL_ID}-h${HOP_IDX}"
  HOP_STDERR_LOG="${LOG_DIR}/${HOP_CALL_ID}.stderr"
  HOP_STDOUT_LOG="${LOG_DIR}/${HOP_CALL_ID}.stdout"
  HOP_START_SEC=$(date +%s)

  if [[ $HOP_IDX -eq 0 ]]; then
    echo "{\"id\":\"${HOP_CALL_ID}\",\"ts\":\"$(date -u +%FT%TZ)\",\"agent\":\"${AGENT}\",\"provider\":\"${HOP_PROVIDER}\",\"model\":\"${HOP_MODEL}\",\"hop\":${HOP_IDX},\"status\":\"start\"}" >> "$LOG_FILE"
  else
    echo "[mr] hop ${HOP_IDX}: failing over to ${HOP_PROVIDER}/${HOP_MODEL} after previous hop" >&2
    echo "{\"id\":\"${HOP_CALL_ID}\",\"ts\":\"$(date -u +%FT%TZ)\",\"agent\":\"${AGENT}\",\"provider\":\"${HOP_PROVIDER}\",\"model\":\"${HOP_MODEL}\",\"hop\":${HOP_IDX},\"status\":\"start\",\"failover_from\":\"${PRIMARY_PROVIDER}\"}" >> "$LOG_FILE"
  fi

  # Same stdin-redirect as before — see #72.
  set +e
  if command -v timeout &>/dev/null; then
    timeout "$MR_PER_HOP_TIMEOUT_SEC" "$CMD" "${CMD_ARGS[@]}" </dev/null >"$HOP_STDOUT_LOG" 2>"$HOP_STDERR_LOG"
    HOP_EXIT=$?
  elif command -v gtimeout &>/dev/null; then
    gtimeout "$MR_PER_HOP_TIMEOUT_SEC" "$CMD" "${CMD_ARGS[@]}" </dev/null >"$HOP_STDOUT_LOG" 2>"$HOP_STDERR_LOG"
    HOP_EXIT=$?
  else
    "$CMD" "${CMD_ARGS[@]}" </dev/null >"$HOP_STDOUT_LOG" 2>"$HOP_STDERR_LOG" &
    CHILD_PID=$!
    ( sleep "$MR_PER_HOP_TIMEOUT_SEC" && kill "$CHILD_PID" 2>/dev/null && echo "[mr] hop ${HOP_IDX} timed out after ${MR_PER_HOP_TIMEOUT_SEC}s" >&2 ) &
    TIMER_PID=$!
    wait "$CHILD_PID"
    HOP_EXIT=$?
    kill "$TIMER_PID" 2>/dev/null
    wait "$TIMER_PID" 2>/dev/null
  fi
  set -e

  HOP_END_SEC=$(date +%s)
  HOP_DURATION=$((HOP_END_SEC - HOP_START_SEC))
  DURATION=$((DURATION + HOP_DURATION))

  echo "{\"id\":\"${HOP_CALL_ID}\",\"ts\":\"$(date -u +%FT%TZ)\",\"agent\":\"${AGENT}\",\"provider\":\"${HOP_PROVIDER}\",\"model\":\"${HOP_MODEL}\",\"hop\":${HOP_IDX},\"exit\":${HOP_EXIT},\"duration\":${HOP_DURATION},\"status\":\"done\"}" >> "$LOG_FILE"

  # Last-hop-wins for the outer-scope telemetry references.
  EXIT=$HOP_EXIT
  PROVIDER="$HOP_PROVIDER"
  MODEL="$HOP_MODEL"
  STDOUT_LOG="$HOP_STDOUT_LOG"
  STDERR_LOG="$HOP_STDERR_LOG"
  if [[ $HOP_IDX -gt 0 ]]; then
    FB_PROVIDER="$HOP_PROVIDER"
    FB_MODEL="$HOP_MODEL"
  fi

  if [[ $HOP_EXIT -eq 0 ]]; then
    if [[ -s "$HOP_STDOUT_LOG" ]]; then
      RESULT_TEXT=$(jq -r 'if type == "object" then (.result // .) else . end' "$HOP_STDOUT_LOG" 2>/dev/null)
      if [[ -n "$RESULT_TEXT" && "$RESULT_TEXT" != "null" ]]; then
        printf '%s\n' "$RESULT_TEXT"
      else
        cat "$HOP_STDOUT_LOG"
      fi
    fi
    rm -f "$HOP_STDERR_LOG"
    break
  fi

  IS_PROVIDER_FAIL=0
  if [[ $HOP_EXIT -eq 124 ]]; then
    IS_PROVIDER_FAIL=1
  fi
  if [[ -s "$HOP_STDERR_LOG" ]] && grep -qE "$PROVIDER_FAILURE_RE" "$HOP_STDERR_LOG"; then
    IS_PROVIDER_FAIL=1
  fi
  if [[ -s "$HOP_STDOUT_LOG" ]] && grep -qE "$PROVIDER_FAILURE_RE" "$HOP_STDOUT_LOG"; then
    IS_PROVIDER_FAIL=1
  fi

  if [[ -s "$HOP_STDERR_LOG" ]]; then
    echo "[mr] hop ${HOP_IDX} (${HOP_PROVIDER}/${HOP_MODEL}) failed (exit ${HOP_EXIT}). Error output:" >&2
    tail -20 "$HOP_STDERR_LOG" >&2
  fi

  # Non-provider failure: real model error. Failing over here would mask the
  # bug and waste another 120s. Emit what we have and stop.
  if [[ $IS_PROVIDER_FAIL -ne 1 ]]; then
    if [[ -s "$HOP_STDOUT_LOG" ]]; then
      RESULT_TEXT=$(jq -r 'if type == "object" then (.result // .) else . end' "$HOP_STDOUT_LOG" 2>/dev/null)
      if [[ -n "$RESULT_TEXT" && "$RESULT_TEXT" != "null" ]]; then
        printf '%s\n' "$RESULT_TEXT"
      else
        cat "$HOP_STDOUT_LOG"
      fi
    fi
    break
  fi

  HOP_IDX=$((HOP_IDX + 1))
done

# ─── Final fallback to Anthropic (opt-in via MR_FALLBACK_TO_ANTHROPIC=1) ──
# If all configured cheap providers have been exhausted (primary failed + any
# failover attempt also failed), signal the interceptor to passthrough so the
# original Task runs on Anthropic native instead of returning partial output.
if [[ $EXIT -ne 0 && "${MR_FALLBACK_TO_ANTHROPIC:-}" == "1" ]]; then
  FB_LABEL="${FB_PROVIDER:-none}"
  echo "[mr] All cheap providers exhausted (primary=${PROVIDER}, fallback=${FB_LABEL}); signaling Anthropic fallback" >&2
  rm -f "$STDOUT_LOG" 2>/dev/null
  [[ -n "${FB_STDOUT_LOG:-}" ]] && rm -f "$FB_STDOUT_LOG" 2>/dev/null
  exit $EXIT_ALL_PROVIDERS_FAILED
fi

# ─── Telemetry (synchronous, fail-open) ───
if [[ "${MR_TELEMETRY_DISABLE:-}" == "1" ]]; then
  rm -f "$STDOUT_LOG" 2>/dev/null
  exit $EXIT
fi

TELEMETRY_ENDPOINT="${T1K_TELEMETRY_ENDPOINT:-https://t1k-telemetry.the1studio.org/ingest}"
HOST_HASH=$(echo "$(hostname 2>/dev/null || cat /etc/hostname 2>/dev/null || echo "${USER:-anon}")" | shasum -a 256 2>/dev/null | cut -c1-12 || echo "unknown")

# Telemetry POST: the worker reads (and stores into) a column named `role`.
# That field carries the agent name — single source of truth, no dual-spelling.
DELEGATION_PAYLOAD="{\"type\":\"model-router:delegation\",\"kit\":\"theonekit-model-router\",\"mrVersion\":\"${MR_VERSION}\",\"id\":\"${CALL_ID}\",\"role\":\"${AGENT}\",\"profile\":\"${PROVIDER}\",\"model\":\"${MODEL}\",\"exit\":${EXIT},\"duration\":${DURATION},\"ts\":\"$(date -u +%FT%TZ)\",\"hostname\":\"${HOST_HASH}\",\"platform\":\"$(uname -s)\"}"
mirror_telemetry_event "$DELEGATION_PAYLOAD"
curl -s -X POST "$TELEMETRY_ENDPOINT" \
  -H "Content-Type: application/json" \
  --max-time 3 \
  -d "$DELEGATION_PAYLOAD" \
  > /dev/null 2>&1 || true

# ─── Savings telemetry: model-router:request event with token usage ───
if [[ $EXIT -eq 0 && -s "$STDOUT_LOG" ]]; then
  INPUT_T=$(jq -r --arg m "$MODEL" '(.modelUsage[$m].inputTokens // .usage.input_tokens // 0)' "$STDOUT_LOG" 2>/dev/null)
  OUTPUT_T=$(jq -r --arg m "$MODEL" '(.modelUsage[$m].outputTokens // .usage.output_tokens // 0)' "$STDOUT_LOG" 2>/dev/null)
  CACHE_CREATE_T=$(jq -r --arg m "$MODEL" '(.modelUsage[$m].cacheCreationInputTokens // .usage.cache_creation_input_tokens // 0)' "$STDOUT_LOG" 2>/dev/null)
  CACHE_READ_T=$(jq -r --arg m "$MODEL" '(.modelUsage[$m].cacheReadInputTokens // .usage.cache_read_input_tokens // 0)' "$STDOUT_LOG" 2>/dev/null)
  # Extra structural counts the worker accepts but we never populated before.
  # claude -p --output-format json returns these at the top level of result.
  MESSAGE_COUNT=$(jq -r '(.num_turns // 0)' "$STDOUT_LOG" 2>/dev/null)
  # toolCallCount: rough count of tool_use blocks visible in the assistant content
  # (Anthropic format messages with content arrays). Falls back to 0 if shape differs.
  TOOL_CALL_COUNT=$(jq -r '[.. | objects | select(.type == "tool_use")] | length' "$STDOUT_LOG" 2>/dev/null)
  [[ -z "$TOOL_CALL_COUNT" || "$TOOL_CALL_COUNT" == "null" ]] && TOOL_CALL_COUNT=0
  # Failover tracking: if the failover branch ran above, FB_PROVIDER/FB_MODEL are set.
  FALLBACK_ATTEMPTS=0
  FALLBACK_MODEL=""
  if [[ -n "${FB_MODEL:-}" ]]; then
    FALLBACK_ATTEMPTS=1
    FALLBACK_MODEL="$FB_MODEL"
  fi

  TOTAL_INPUT=$((INPUT_T + CACHE_CREATE_T))

  if [[ "$TOTAL_INPUT" -gt 0 || "$OUTPUT_T" -gt 0 ]]; then
    GH_TOKEN_CACHE="${HOME}/.model-router/.t1k-gh-token-cache"
    SAVINGS_TOKEN=""
    if [[ -s "$GH_TOKEN_CACHE" ]]; then
      CACHED_VAL=$(cat "$GH_TOKEN_CACHE" 2>/dev/null | tr -d '\n')
      if [[ "$CACHED_VAL" =~ ^gh[opusr]_[A-Za-z0-9_]+$ ]]; then
        SAVINGS_TOKEN="$CACHED_VAL"
      fi
    fi
    if [[ -z "$SAVINGS_TOKEN" ]]; then
      SAVINGS_TOKEN=$(gh auth token 2>/dev/null || true)
    fi

    if [[ -n "$SAVINGS_TOKEN" ]]; then
      LATENCY_MS=$((DURATION * 1000))
      # The worker's full schema accepts 30 fields per event type. The savings
      # event was historically populating 12 of them. This payload now includes
      # role/id/profile/duration/messageCount/toolCallCount/fallbackAttempts/
      # fallbackModel so the worker stores the right thing on every column.
      # requestModel uses ORIGINAL_MODEL (from --original-model arg or fallback
      # to MODEL for explicit Bash path) so the actual model swap is recorded.
      REQUEST_PAYLOAD=$(jq -nc \
        --arg type "model-router:request" \
        --arg kit "theonekit-model-router" \
        --arg mrVersion "$MR_VERSION" \
        --arg id "$CALL_ID" \
        --arg role "$AGENT" \
        --arg profile "$PROVIDER" \
        --arg model "$MODEL" \
        --arg ts "$(date -u +%FT%TZ)" \
        --arg requestModel "$ORIGINAL_MODEL" \
        --arg routedModel "$MODEL" \
        --arg scenario "${SELECTION_SOURCE:-explicit}" \
        --argjson inputTokens "$TOTAL_INPUT" \
        --argjson outputTokens "$OUTPUT_T" \
        --argjson cachedTokens "$CACHE_READ_T" \
        --argjson duration "$DURATION" \
        --argjson latencyMs "$LATENCY_MS" \
        --argjson messageCount "$MESSAGE_COUNT" \
        --argjson toolCallCount "$TOOL_CALL_COUNT" \
        --argjson fallbackAttempts "$FALLBACK_ATTEMPTS" \
        --arg fallbackModel "$FALLBACK_MODEL" \
        --argjson exit "$EXIT" \
        --argjson success 1 \
        --arg hostname "$(hostname 2>/dev/null || echo unknown)" \
        --arg platform "$(uname -s)" \
        --arg arch "$(uname -m)" \
        '{type:$type, kit:$kit, mrVersion:$mrVersion, id:$id, role:$role, profile:$profile, model:$model, ts:$ts, requestModel:$requestModel, routedModel:$routedModel, scenario:$scenario, inputTokens:$inputTokens, outputTokens:$outputTokens, cachedTokens:$cachedTokens, duration:$duration, latencyMs:$latencyMs, messageCount:$messageCount, toolCallCount:$toolCallCount, fallbackAttempts:$fallbackAttempts, fallbackModel:$fallbackModel, exit:$exit, success:$success, hostname:$hostname, platform:$platform, arch:$arch}')

      mirror_telemetry_event "$REQUEST_PAYLOAD"
      curl -s -X POST "$TELEMETRY_ENDPOINT" \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $SAVINGS_TOKEN" \
        --max-time 3 \
        -d "$REQUEST_PAYLOAD" \
        > /dev/null 2>&1 || true
    fi
  fi
fi

rm -f "$STDOUT_LOG" 2>/dev/null
[[ -n "${FB_STDOUT_LOG:-}" ]] && rm -f "$FB_STDOUT_LOG" 2>/dev/null

exit $EXIT
