#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
/**
 * mr-telemetry.cjs — PostToolUse + Stop hook: send model-router delegation events
 *
 * Reliability model (rewritten 2026-05-25): durable queue + async/await flush.
 *
 * The previous version raced `fetch(...).finally(process.exit(0))` against a
 * 4s setTimeout sentinel. In `claude -p` print-mode sessions the parent
 * process tears down quickly after the last tool use, killing the hook's
 * SIGTERM-able child process before fetch could settle — empirically ~83%
 * of events were lost (verified 2026-05-25 against model_router_events table).
 *
 * New design:
 *   1. Hook ALWAYS writes the event to a local queue file first (durable,
 *      instant — never depends on network or parent process lifecycle).
 *   2. Hook then attempts to flush the queue to the worker via
 *      async fetch+await. If flush completes, lines are removed from queue.
 *      If hook is killed mid-flush, queue retains unsent events for next call.
 *   3. Stop event also triggers a flush, draining anything left over from
 *      the session's last PostToolUse.
 *
 * Net effect: at-least-once delivery semantics. Hook may exit before flush
 * completes, but events are never lost — next firing drains them.
 *
 * Fires when MR_SPAWNED=1 (inside delegated sessions only).
 * Auth: GitHub token via `gh auth token`.
 * Fail-open: never blocks dev workflow.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFileSync } = require('child_process');

const MR_DIR = path.join(os.homedir(), '.model-router');
const QUEUE_FILE = path.join(MR_DIR, 'telemetry-queue.jsonl');
const MIRROR_FILE = path.join(MR_DIR, 'telemetry-out.jsonl');

// Resolve kit version once per hook invocation. Stamp on every event so the
// worker can correlate behavior with kit version (parallel to mr-delegate.sh).
function readMrVersion() {
  // Probe order mirrors mr-delegate.sh: project-local module.json, then
  // global module.json (rare), then global .t1k-manifest.json (what
  // `t1k init -g` actually ships). Without the manifest fallback every
  // global install reports 'unknown'.
  const home = os.homedir();
  const proj = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  for (const candidate of [
    path.join(proj, '.claude', 'modules', 'model-router', 'module.json'),
    path.join(home, '.claude', 'modules', 'model-router', 'module.json'),
    path.join(home, '.claude', 'modules', 'model-router', '.t1k-manifest.json'),
  ]) {
    try {
      const j = JSON.parse(fs.readFileSync(candidate, 'utf8'));
      if (j && j.version && typeof j.version === 'string') return j.version;
    } catch { /* try next */ }
  }
  return 'unknown';
}

function mirrorTelemetryEvent(payload) {
  if (process.env.MR_MIRROR_TELEMETRY !== '1') return;
  try {
    fs.mkdirSync(MR_DIR, { recursive: true });
    fs.appendFileSync(MIRROR_FILE, JSON.stringify(payload) + '\n');
  } catch { /* fail-open */ }
}
// `.gh-token-cache` (no prefix) is OWNED by the oc-go-cc daemon — it writes
// the file with `<timestamp>\n<token>` format. We MUST NOT touch that path
// or our cache reads collide with theirs (verified 2026-05-25 via `strings
// $(which oc-go-cc)` containing the literal path). Use a t1k-prefixed path
// to namespace-separate.
const TOKEN_CACHE = path.join(MR_DIR, '.t1k-gh-token-cache');
const TOKEN_MAX_AGE_MS = 30 * 60 * 1000;
const FLUSH_BUDGET_MS = 3500;

(async () => {
  try {
    if (process.env.MR_SPAWNED !== '1') return;

    let hookData = {};
    try {
      const raw = fs.readFileSync(0, 'utf8');
      hookData = JSON.parse(raw);
    } catch { /* no input = Stop hook with empty stdin is fine */ }

    let readTelemetryEndpoint;
    try {
      ({ readTelemetryEndpoint } = require(path.join(__dirname, 'telemetry-utils.cjs')));
    } catch {
      return;
    }

    const projectRoot = hookData.cwd || process.cwd();
    const endpoint = readTelemetryEndpoint(projectRoot);
    if (!endpoint) return;

    const isStopHook = hookData.hook_event_name === 'Stop' || !hookData.tool_name;

    if (!isStopHook) {
      const event = {
        type: 'model-router:tool-use',
        kit: 'theonekit-model-router',
        mrVersion: readMrVersion(),
        role: process.env.MR_DELEGATE_AGENT || 'unknown',
        parentPid: process.env.MR_DELEGATE_PARENT_PID || null,
        tool: hookData.tool_name || null,
        durationMs: hookData.duration_ms || null,
        ts: new Date().toISOString(),
        hostname: os.hostname(),
        platform: os.platform(),
        arch: os.arch(),
      };

      fs.mkdirSync(MR_DIR, { recursive: true });
      fs.appendFileSync(QUEUE_FILE, JSON.stringify(event) + '\n');
      mirrorTelemetryEvent(event);
    }

    await flushQueue(endpoint);
  } catch { /* fail-open */ }
})().finally(() => process.exit(0));

async function flushQueue(endpoint) {
  if (!fs.existsSync(QUEUE_FILE)) return;
  let lines;
  try {
    lines = fs.readFileSync(QUEUE_FILE, 'utf8').split('\n').filter(Boolean);
  } catch {
    return;
  }
  if (lines.length === 0) return;

  const ghToken = getGhToken();
  if (!ghToken) return;

  // Worker accepts arrays for model-router events (src/index.js handler:
  // `const bodies = Array.isArray(rawBody) ? rawBody : [rawBody]`).
  // Send the whole queue in one batch — fewer requests, faster flush,
  // friendlier to the per-request timeout budget.
  const events = [];
  for (const line of lines) {
    try { events.push(JSON.parse(line)); } catch { /* skip malformed */ }
  }
  if (events.length === 0) {
    try { fs.unlinkSync(QUEUE_FILE); } catch { /* ok */ }
    return;
  }

  const ok = await postBatch(endpoint, ghToken, events);

  try {
    if (ok) {
      fs.unlinkSync(QUEUE_FILE);
    }
    // On failure: leave queue intact; next firing retries the same batch.
  } catch { /* best effort */ }
}

async function postBatch(endpoint, ghToken, events) {
  const controller = new AbortController();
  // Use the full flush budget for the single batch request.
  const t = setTimeout(() => controller.abort(), FLUSH_BUDGET_MS);
  try {
    const res = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${ghToken}`,
      },
      body: JSON.stringify(events),
      signal: controller.signal,
    });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(t);
  }
}

// Reject any cached blob that isn't a plausible GitHub PAT (`ghp_`, `gho_`,
// `ghu_`, `ghs_`, `ghr_`). Observed in the wild (2026-05-25): the cache file
// somehow accreted a `<timestamp>\n<token>` shape, blowing up fetch with
// "invalid header value." Validate before trust.
function looksLikeToken(s) {
  return typeof s === 'string' && /^gh[opusr]_[A-Za-z0-9_]+$/.test(s);
}

function getGhToken() {
  try {
    if (fs.existsSync(TOKEN_CACHE)) {
      const stat = fs.statSync(TOKEN_CACHE);
      if (Date.now() - stat.mtimeMs < TOKEN_MAX_AGE_MS) {
        const cached = fs.readFileSync(TOKEN_CACHE, 'utf8').trim();
        if (looksLikeToken(cached)) return cached;
        // Cache is corrupt — fall through to re-fetch and overwrite.
      }
    }
    const tok = execFileSync('gh', ['auth', 'token'], {
      timeout: 5000, encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    }).trim();
    if (looksLikeToken(tok)) {
      fs.mkdirSync(path.dirname(TOKEN_CACHE), { recursive: true });
      fs.writeFileSync(TOKEN_CACHE, tok, { mode: 0o600 });
      return tok;
    }
    return null;
  } catch {
    return null;
  }
}
