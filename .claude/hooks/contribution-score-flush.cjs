#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
/**
 * contribution-score-flush.cjs — Stop hook: enforced contribution-score telemetry flush.
 *
 * Problem this solves
 * -------------------
 * Contribution-score telemetry (the t1k:contribution-score SSOT skill) is a
 * SKILL-BODY step in t1k:triage Step 8. In a real `--ecosystem --yolo` run the
 * model SKIPPED Step 8 entirely → zero telemetry recorded. Two structural facts:
 *
 *   1. No deterministic backstop — if the model skips the step, nothing records.
 *   2. The worker REJECTS triage credit for OPEN artifacts. POSTing
 *      `type:"triage-backfill"` for an open PR returns HTTP 403
 *      {"error":"author_attribution_failed","reason":"triage_requires_closed_artifact"}.
 *      It only accepts closed/merged artifacts. `/t1k:triage --yolo` arms many PRs
 *      for ASYNC auto-merge — they aren't closed at Stop time, so their credit must
 *      be POSTed LATER, once they merge, by a future session.
 *
 * This hook delivers a model-independent, cross-session-retrying backstop:
 *   - flushes a durable queue (pending-contribution-scores.jsonl) the skill appends to,
 *   - keeps 403 `triage_requires_closed_artifact` entries for retry next session,
 *   - independently scans the transcript for `gh pr merge` / `gh issue close` etc.
 *     against T1K repos and backstops a default-score POST for any ref not already recorded.
 *
 * Fail-open: ANY missing prerequisite → one stderr line, exit 0. Never blocks Stop.
 * Security: never logs the token, full POST body, or rationale contents.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFileSync } = require('child_process');

const {
  isTelemetryEnabled,
  findProjectRoot,
  resolveClaudeDir,
  readTelemetryEndpoint,
  parseHookStdin,
  readFeatureFlag,
} = require('./telemetry-utils.cjs');
const { safeResolve, SafePathError } = require('./lib/safe-paths.cjs');

// ── Tunable constants ──────────────────────────────────────────────────────
const MAX_ATTEMPTS = 20;            // drop a 403-retry entry after this many tries
const MAX_AGE_DAYS = 14;            // drop a 403-retry entry older than this
const RECORDED_PRUNE_DAYS = 30;     // prune recorded-ledger entries older than this
const POST_TIMEOUT_MS = 8000;
const QUEUE_FILE = 'pending-contribution-scores.jsonl';
const RECORDED_FILE = 'contribution-scores-recorded.jsonl';
const BACKSTOP_SCORE = 3;
const BACKSTOP_RATIONALE = 'auto-backstop: triaged this session (default score)';
const T1K_REPO_RE = /^The1Studio\/(theonekit-[A-Za-z0-9._-]+|t1k-[A-Za-z0-9._-]+)$/;

// ── Endpoint resolution ─────────────────────────────────────────────────────
/**
 * Resolve the telemetry base endpoint (trailing `/ingest` stripped — the
 * contributions API lives at `<EP>/api/contributions`). Order:
 *   1. env T1K_TELEMETRY_ENDPOINT
 *   2. project .claude/t1k-config-core.json telemetry.cloud.endpoint
 *   3. global ~/.claude/t1k-config-core.json (same field)
 * Returns null when none configured.
 */
function resolveContributionsBase(projectRoot, home) {
  let raw = readTelemetryEndpoint(projectRoot); // handles env + project config
  if (!raw && home) {
    try {
      const globalCfg = path.join(home, '.claude', 't1k-config-core.json');
      if (fs.existsSync(globalCfg)) {
        const c = JSON.parse(fs.readFileSync(globalCfg, 'utf8'));
        raw = c.telemetry?.cloud?.endpoint || null;
      }
    } catch { /* fail-open */ }
  }
  if (!raw) return null;
  return raw.replace(/\/ingest\/?$/, '').replace(/\/$/, '');
}

/** Check telemetry.cloud.enabled across project + global config (default true). */
function isCloudTelemetryEnabled(projectRoot, home) {
  const read = (p) => {
    try {
      if (!fs.existsSync(p)) return undefined;
      const c = JSON.parse(fs.readFileSync(p, 'utf8'));
      return c.telemetry?.cloud?.enabled;
    } catch { return undefined; }
  };
  const proj = read(path.join(projectRoot, '.claude', 't1k-config-core.json'));
  if (proj === false) return false;
  if (proj === true) return true;
  const glob = home ? read(path.join(home, '.claude', 't1k-config-core.json')) : undefined;
  if (glob === false) return false;
  return true; // default-on / fail-open
}

// ── gh resolution ───────────────────────────────────────────────────────────
function resolveGh() {
  let token = null;
  let user = null;
  try {
    token = execFileSync('gh', ['auth', 'token'], {
      timeout: 5000, encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    }).trim() || null;
  } catch { return { token: null, user: null }; }
  if (!token) return { token: null, user: null };
  try {
    user = execFileSync('gh', ['api', 'user', '--jq', '.login'], {
      timeout: 5000, encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    }).trim() || null;
  } catch { return { token, user: null }; }
  return { token, user };
}

/**
 * #365 — resolve the current repo + current branch's PR number so the transcript
 * backstop can score the documented yolo admin-merge form
 * (`gh pr merge --admin --squash --delete-branch`), which carries neither a
 * positional number nor a `--repo` flag. Best-effort: any failure → null fields
 * (the backstop simply can't fill the gap, same as before — never throws).
 */
function resolveGhContext() {
  const out = { currentRepo: null, currentPr: null };
  try {
    out.currentRepo = execFileSync('gh', ['repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner'], {
      timeout: 5000, encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    }).trim() || null;
  } catch { /* not a gh repo / no auth — leave null */ }
  try {
    const pr = execFileSync('gh', ['pr', 'view', '--json', 'number', '--jq', '.number'], {
      timeout: 5000, encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    }).trim();
    if (pr && /^\d+$/.test(pr)) out.currentPr = pr;
  } catch { /* no PR for current branch — leave null */ }
  return out;
}

// ── JSONL helpers ────────────────────────────────────────────────────────────
function readJsonl(filePath) {
  try {
    if (!fs.existsSync(filePath)) return [];
    return fs.readFileSync(filePath, 'utf8')
      .split('\n')
      .map(l => l.trim())
      .filter(Boolean)
      .map(l => { try { return JSON.parse(l); } catch { return null; } })
      .filter(Boolean);
  } catch { return []; }
}

function writeJsonl(filePath, rows) {
  try {
    if (!rows.length) {
      if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
      return;
    }
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, rows.map(r => JSON.stringify(r)).join('\n') + '\n');
  } catch { /* fail-open */ }
}

// ── Transcript backstop extraction ───────────────────────────────────────────
/**
 * Resolve the transcript path safely (symlink-checked, under a known harness
 * root). Returns a readable path or null.
 */
function resolveTranscriptPath(rawTranscriptPath) {
  if (!rawTranscriptPath) return null;
  try {
    const p = safeResolve(rawTranscriptPath, [os.tmpdir(), os.homedir()]);
    return p && fs.existsSync(p) ? p : null;
  } catch (e) {
    if (e instanceof SafePathError) return null;
    return null;
  }
}

/**
 * Parse a transcript JSONL string and return canonical ref_urls for every
 * triage-style terminal `gh` action against a T1K repo.
 *
 * Scans assistant `tool_use` Bash commands for:
 *   gh pr merge <n> --repo The1Studio/<t1k-repo>
 *   gh pr merge --admin --squash --delete-branch          (#365 yolo form)
 *   gh pr close <n> --repo The1Studio/<t1k-repo>
 *   gh issue close <n> --repo The1Studio/<t1k-repo>
 *   gh pr create ... --repo The1Studio/<t1k-repo>      (optional)
 *   gh issue create ... --repo The1Studio/<t1k-repo>   (optional)
 *
 * For pr → `/pull/<n>`, for issue → `/issues/<n>`. Non-T1K repos are skipped.
 * Returns a de-duplicated array of { ref_url, repo, type } (type=triage-backfill).
 *
 * #365 — the documented org-admin yolo override (`gh pr merge --admin --squash
 * --delete-branch`) has NEITHER a positional PR number NOR a `--repo` flag: it
 * operates on the current branch's PR in the current repo. The previous extractor
 * required both, so EVERY admin-merge escaped the backstop and scored zero. The
 * caller now supplies an optional `ctx` ({ currentRepo, currentPr }) resolved from
 * git/gh, used as a fallback when the command omits the repo and/or the number.
 *
 * Pure / no I/O beyond the passed string + ctx — directly unit-testable.
 */
function extractRefsFromTranscript(transcriptText, ctx) {
  const refs = new Map(); // ref_url → { ref_url, repo, type }
  if (!transcriptText || typeof transcriptText !== 'string') return [];

  const commands = [];
  for (const line of transcriptText.split('\n')) {
    const t = line.trim();
    if (!t) continue;
    let entry;
    try { entry = JSON.parse(t); } catch { continue; }
    const content = entry?.message?.content;
    if (!Array.isArray(content)) continue;
    for (const block of content) {
      if (block && block.type === 'tool_use' && block.name === 'Bash' &&
          block.input && typeof block.input.command === 'string') {
        commands.push(block.input.command);
      }
    }
  }

  for (const cmd of commands) {
    collectRefsFromCommand(cmd, refs, ctx);
  }
  return [...refs.values()];
}

/**
 * Extract refs from a single shell command string (a command may chain several
 * gh calls with && / ; / newlines). Mutates the `refs` Map.
 *
 * @param {string} cmd shell command line
 * @param {Map} refs accumulator (ref_url → entry)
 * @param {{currentRepo?:string, currentPr?:string|number}} [ctx]
 *   Fallbacks for the #365 numberless/repoless admin-merge form, resolved by the
 *   hook from `gh repo view` / `gh pr view` at flush time.
 */
function collectRefsFromCommand(cmd, refs, ctx) {
  if (typeof cmd !== 'string' || cmd.indexOf('gh ') === -1) return;
  const ctxRepo = ctx && typeof ctx.currentRepo === 'string' ? ctx.currentRepo : null;
  const ctxPr = ctx && (typeof ctx.currentPr === 'string' || typeof ctx.currentPr === 'number')
    ? String(ctx.currentPr).replace(/^#/, '')
    : null;
  // A command line may contain multiple gh invocations; split on common separators.
  const segments = cmd.split(/&&|\|\||;|\n/);
  for (const seg of segments) {
    const m = seg.match(/\bgh\s+(pr|issue)\s+(merge|close|create)\b/);
    if (!m) continue;
    const kind = m[1];   // pr | issue
    const action = m[2]; // merge | close | create

    // create: no number yet (artifact being created) — number lookup belongs to
    // the skill, skip the deterministic backstop here.
    if (action === 'create') continue;

    // Repo: prefer an explicit --repo; fall back to the resolved current repo.
    const repoMatch = seg.match(/--repo[=\s]+(\S+)/);
    const repo = repoMatch ? repoMatch[1].replace(/^["']|["']$/g, '') : ctxRepo;
    if (!repo || !T1K_REPO_RE.test(repo)) continue;

    // Number: a positional PR/issue number anywhere after the action verb (the
    // `#?\d+` token that is NOT the value of a --flag and NOT part of the repo
    // slug). When the command carries no positional number — the documented
    // `gh pr merge --admin --squash --delete-branch` yolo form (#365) — fall back
    // to the resolved current PR number.
    const n = extractPositionalNumber(seg, action) || ctxPr;
    if (!n) continue;

    const segment = kind === 'pr' ? 'pull' : 'issues';
    const ref_url = `https://github.com/${repo}/${segment}/${n}`;
    if (!refs.has(ref_url)) {
      refs.set(ref_url, { ref_url, repo, type: 'triage-backfill' });
    }
  }
}

/**
 * Extract a positional PR/issue number from a `gh pr|issue merge|close` segment.
 *
 * Handles the number appearing BEFORE flags (`gh pr merge 54 --admin ...`) and
 * AFTER flags (`gh pr merge --admin --squash 54`). Tokens consumed by a flag
 * value (e.g. `--repo The1Studio/x`) are skipped, so the repo slug's digits are
 * never mistaken for the PR number. Returns the number string, or null.
 */
function extractPositionalNumber(seg, action) {
  const verbRe = new RegExp(`\\bgh\\s+(?:pr|issue)\\s+${action}\\b`);
  const verbMatch = verbRe.exec(seg);
  if (!verbMatch) return null;
  const tail = seg.slice(verbMatch.index + verbMatch[0].length);
  // Tokenize the remaining args, skipping flag values for known value-flags.
  const tokens = tail.trim().split(/\s+/).filter(Boolean);
  const VALUE_FLAGS = new Set(['--repo', '-R', '--body', '-b', '--subject', '-t', '--match-head-commit']);
  for (let i = 0; i < tokens.length; i++) {
    const tok = tokens[i];
    if (tok.startsWith('-')) {
      // `--flag=value` carries its own value; skip just this token.
      if (tok.includes('=')) continue;
      // `--repo VALUE` consumes the next token as its value.
      if (VALUE_FLAGS.has(tok)) { i++; continue; }
      continue; // valueless flag (e.g. --admin, --squash, --delete-branch)
    }
    const numMatch = tok.match(/^#?(\d+)$/);
    if (numMatch) return numMatch[1];
  }
  return null;
}

// ── POST + disposition ───────────────────────────────────────────────────────
/**
 * Default POST implementation using Node built-in fetch (Node ≥18). Returns
 * { status, reason } where status is the HTTP code (or 0 on network error) and
 * reason is the parsed worker `reason` field if present.
 */
async function defaultPost({ base, token, body }) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), POST_TIMEOUT_MS);
  try {
    const res = await fetch(`${base}/api/contributions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
      },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    let reason = null;
    try {
      const json = await res.json();
      reason = json && (json.reason || null);
    } catch { /* non-JSON body */ }
    return { status: res.status, reason };
  } catch {
    return { status: 0, reason: null }; // network / timeout / abort
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Decide what to do with an entry given a POST result. Pure — unit-testable.
 *
 * @param {{status:number, reason:string|null}} result
 * @param {object} entry queue entry (with `attempts`, `first_seen_ts`)
 * @param {number} nowMs
 * @returns {{ disposition: 'drop'|'keep'|'drop-recorded', logReason: string }}
 *   - 'drop-recorded' → entry recorded (201/200): drop from queue + add to recorded ledger
 *   - 'drop'          → terminal failure (out-of-scope / bad): drop, do NOT record
 *   - 'keep'          → retry next session (transient 5xx/network OR not-yet-closed 403)
 */
function disposeResult(result, entry, nowMs) {
  const { status, reason } = result || {};
  // Recorded — newly (201) or idempotent (200).
  if (status === 201 || status === 200) {
    return { disposition: 'drop-recorded', logReason: status === 201 ? 'recorded' : 'already_recorded' };
  }
  // Artifact not yet merged/closed — keep and retry next session, bounded.
  if (status === 403 && reason === 'triage_requires_closed_artifact') {
    const attempts = (entry.attempts || 0) + 1;
    const firstSeen = entry.first_seen_ts ? new Date(entry.first_seen_ts).getTime() : nowMs;
    const ageDays = (nowMs - firstSeen) / (24 * 60 * 60 * 1000);
    if (attempts > MAX_ATTEMPTS) {
      return { disposition: 'drop', logReason: `dropped: max-attempts(${MAX_ATTEMPTS}) exceeded` };
    }
    if (ageDays > MAX_AGE_DAYS) {
      return { disposition: 'drop', logReason: `dropped: age>${MAX_AGE_DAYS}d` };
    }
    return { disposition: 'keep', logReason: `keep: not-yet-closed attempts=${attempts}` };
  }
  // Transient — keep, do NOT increment attempts (not the artifact's fault).
  if (status === 0 || status >= 500) {
    return { disposition: 'keep', logReason: `keep: transient(${status})` };
  }
  // 400 / other 403 (out-of-scope repo, bad url, never succeeds) → terminal drop.
  return { disposition: 'drop', logReason: `drop: terminal(${status}${reason ? '/' + reason : ''})` };
}

// ── Main flush orchestration ──────────────────────────────────────────────────
/**
 * Core flush logic. Pure-ish: all side effects go through `deps` so tests inject
 * an in-memory POST, queue, recorded ledger, and transcript text.
 *
 * @param {object} deps
 * @param {string} deps.user authed gh login
 * @param {string} deps.token gh token
 * @param {string} deps.base endpoint base (no /ingest)
 * @param {string} deps.kit kit short name (e.g. theonekit-core)
 * @param {object[]} deps.queue current queue entries
 * @param {object[]} deps.recorded current recorded-ledger entries
 * @param {string} deps.transcriptText raw transcript JSONL (may be '')
 * @param {(args:{base,token,body})=>Promise<{status,reason}>} deps.post
 * @param {number} [deps.nowMs]
 * @returns {Promise<{ queue: object[], recorded: object[], stats: object }>}
 */
async function runFlush(deps) {
  const { user, token, base, kit, post } = deps;
  const nowMs = deps.nowMs || Date.now();
  const stats = { queueRecorded: 0, queueKept: 0, queueDropped: 0, backstopRecorded: 0, backstopKept: 0, backstopDropped: 0 };

  // Build a set of ref_urls already recorded (cross-session) so we never re-POST.
  const recordedSet = new Set((deps.recorded || []).map(r => r.ref_url).filter(Boolean));
  // Prune stale recorded-ledger entries.
  let recorded = (deps.recorded || []).filter(r => {
    if (!r.ts) return true;
    const age = (nowMs - new Date(r.ts).getTime()) / (24 * 60 * 60 * 1000);
    return age <= RECORDED_PRUNE_DAYS;
  });

  const addRecorded = (ref_url) => {
    if (!ref_url || recordedSet.has(ref_url)) return;
    recordedSet.add(ref_url);
    recorded.push({ ref_url, ts: new Date(nowMs).toISOString() });
  };

  // ── 1. Flush the durable queue (skill-appended entries) ──
  const survivors = [];
  for (const entry of (deps.queue || [])) {
    // Only flush entries belonging to the authed user.
    if (entry.user && entry.user !== user) { survivors.push(entry); continue; }
    if (entry.ref_url && recordedSet.has(entry.ref_url)) { stats.queueDropped++; continue; }

    let effectiveEntry = entry;
    const body = buildPostBody(entry, { user, kit });
    let result = await post({ base, token, body });

    // Author-mismatch auto-retype: the worker rejects sync-back-pr / issue credit
    // when the artifact's GitHub author != submitter (403 author_mismatch). The
    // submitter didn't author it, but did triage/merge it — re-type ONCE as
    // triage-backfill and re-POST. Never retype an already-triage-backfill entry
    // (loop guard) and only the first attempt is retried.
    if (result.status === 403 && result.reason === 'author_mismatch' &&
        (entry.type === 'sync-back-pr' || entry.type === 'issue')) {
      logStatus('queue', entry.ref_url, `retype: author_mismatch ${entry.type}→triage-backfill`);
      effectiveEntry = { ...entry, type: 'triage-backfill' };
      const retryBody = buildPostBody(effectiveEntry, { user, kit });
      result = await post({ base, token, body: retryBody });
    }

    const { disposition, logReason } = disposeResult(result, effectiveEntry, nowMs);
    logStatus('queue', entry.ref_url, logReason);

    if (disposition === 'drop-recorded') {
      addRecorded(entry.ref_url);
      stats.queueRecorded++;
    } else if (disposition === 'keep') {
      // Persist the retyped form so the next session retries as triage-backfill.
      const next = { ...effectiveEntry };
      if (result.status === 403 && result.reason === 'triage_requires_closed_artifact') {
        next.attempts = (entry.attempts || 0) + 1;
      }
      if (!next.first_seen_ts) next.first_seen_ts = new Date(nowMs).toISOString();
      survivors.push(next);
      stats.queueKept++;
    } else {
      stats.queueDropped++;
    }
  }

  // ── 2. Model-independent transcript backstop ──
  // #365 — pass the resolved git/gh context so the numberless/repoless admin-merge
  // form (`gh pr merge --admin --squash --delete-branch`) is captured too.
  const backstopRefs = extractRefsFromTranscript(deps.transcriptText || '', deps.ghContext);
  // Skip refs already recorded this run or already present in the surviving queue.
  const queuedRefs = new Set(survivors.map(s => s.ref_url).filter(Boolean));
  for (const ref of backstopRefs) {
    if (recordedSet.has(ref.ref_url) || queuedRefs.has(ref.ref_url)) continue;
    const entry = {
      user,
      kit,
      repo: ref.repo,
      type: 'triage-backfill',
      ref_url: ref.ref_url,
      ai_score: BACKSTOP_SCORE,
      ai_rationale: BACKSTOP_RATIONALE,
      attempts: 0,
      first_seen_ts: new Date(nowMs).toISOString(),
    };
    const body = buildPostBody(entry, { user, kit });
    const result = await post({ base, token, body });
    const { disposition, logReason } = disposeResult(result, entry, nowMs);
    logStatus('backstop', ref.ref_url, logReason);

    if (disposition === 'drop-recorded') {
      addRecorded(ref.ref_url);
      stats.backstopRecorded++;
    } else if (disposition === 'keep') {
      if (result.status === 403 && result.reason === 'triage_requires_closed_artifact') {
        entry.attempts = 1;
      }
      survivors.push(entry);
      queuedRefs.add(ref.ref_url);
      stats.backstopKept++;
    } else {
      stats.backstopDropped++;
    }
  }

  return { queue: survivors, recorded, stats };
}

/** Build the worker POST body from a queue entry. SSOT for the body shape. */
function buildPostBody(entry, { user, kit }) {
  const body = {
    user,
    kit: entry.kit || kit,
    repo: entry.repo,
    type: entry.type || 'triage-backfill',
    ref_url: entry.ref_url,
    ai_score: entry.ai_score,
    ai_rationale: entry.ai_rationale,
  };
  if (entry.evidence_excerpt) body.evidence_excerpt = entry.evidence_excerpt;
  return body;
}

/** Short, secret-safe status line. Never logs token / body / rationale. */
function logStatus(source, refUrl, reason) {
  try {
    const shortRef = String(refUrl || '?').replace(/^https?:\/\/github\.com\//, '');
    process.stderr.write(`[t1k:contrib-flush] ${source} ${shortRef} ${reason}\n`);
  } catch { /* non-critical */ }
}

// ── Hook entry point ──────────────────────────────────────────────────────────
async function main() {
  if (!isTelemetryEnabled()) process.exit(0);

  const hookData = parseHookStdin() || {};
  const projectRoot = findProjectRoot();
  const resolved = resolveClaudeDir();
  if (!resolved || !resolved.claudeDir) {
    process.stderr.write('[t1k:contrib-flush] skipped: no .claude dir\n');
    process.exit(0);
  }
  const { claudeDir, home } = resolved;

  // Respect the kit-wide telemetry feature flag AND telemetry.cloud.enabled.
  if (!readFeatureFlag(claudeDir, 'telemetry', true)) {
    process.stderr.write('[t1k:contrib-flush] skipped: telemetry feature off\n');
    process.exit(0);
  }
  if (!isCloudTelemetryEnabled(projectRoot, home)) {
    process.stderr.write('[t1k:contrib-flush] skipped: cloud telemetry disabled\n');
    process.exit(0);
  }

  const base = resolveContributionsBase(projectRoot, home);
  if (!base) {
    process.stderr.write('[t1k:contrib-flush] skipped: no endpoint\n');
    process.exit(0);
  }

  const queuePath = path.join(claudeDir, QUEUE_FILE);
  const recordedPath = path.join(claudeDir, RECORDED_FILE);
  const queue = readJsonl(queuePath);
  const transcriptPath = resolveTranscriptPath(hookData.transcript_path);

  // Fast exit: nothing to flush and no transcript to scan.
  if (!queue.length && !transcriptPath) {
    process.exit(0);
  }

  const { token, user } = resolveGh();
  if (!token || !user) {
    process.stderr.write('[t1k:contrib-flush] skipped: gh auth unavailable\n');
    process.exit(0);
  }

  let transcriptText = '';
  if (transcriptPath) {
    try { transcriptText = fs.readFileSync(transcriptPath, 'utf8'); } catch { /* ok */ }
  }

  const recorded = readJsonl(recordedPath);
  // Derive kit short name from config/metadata; default theonekit-core.
  const kit = deriveKitName(claudeDir);

  // #365 — resolve current repo + PR so the transcript backstop can score the
  // numberless/repoless admin-merge yolo form. Best-effort; null on any failure.
  const ghContext = resolveGhContext();

  const result = await runFlush({
    user, token, base, kit, queue, recorded, transcriptText, ghContext, post: defaultPost,
  });

  writeJsonl(queuePath, result.queue);
  writeJsonl(recordedPath, result.recorded);

  const s = result.stats;
  process.stderr.write(
    `[t1k:contrib-flush] done queue(rec=${s.queueRecorded},keep=${s.queueKept},drop=${s.queueDropped}) ` +
    `backstop(rec=${s.backstopRecorded},keep=${s.backstopKept},drop=${s.backstopDropped})\n`
  );
  process.exit(0);
}

/** Resolve the kit short name (e.g. theonekit-core) for the POST body. */
function deriveKitName(claudeDir) {
  try {
    const metaPath = path.join(claudeDir, 'metadata.json');
    if (fs.existsSync(metaPath)) {
      const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
      if (typeof meta.name === 'string' && meta.name.startsWith('theonekit-')) return meta.name;
      if (typeof meta.kitName === 'string' && meta.kitName.startsWith('theonekit-')) return meta.kitName;
    }
  } catch { /* fall through */ }
  return 'theonekit-core';
}

// Exports for unit tests (pure functions + injectable orchestrator).
module.exports = {
  resolveContributionsBase,
  isCloudTelemetryEnabled,
  extractRefsFromTranscript,
  collectRefsFromCommand,
  extractPositionalNumber,
  disposeResult,
  runFlush,
  buildPostBody,
  readJsonl,
  writeJsonl,
  T1K_REPO_RE,
  MAX_ATTEMPTS,
  MAX_AGE_DAYS,
  BACKSTOP_SCORE,
  BACKSTOP_RATIONALE,
};

// Only run side effects when invoked directly (not when required by tests).
if (require.main === module) {
  // Hard fail-open wrapper: any unexpected throw still exits 0.
  main().catch(() => process.exit(0));
  // Safety net: never hang the Stop event.
  setTimeout(() => process.exit(0), POST_TIMEOUT_MS + 4000).unref();
}
