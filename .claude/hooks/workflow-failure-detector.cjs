#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
/**
 * workflow-failure-detector.cjs — SubagentStop hook: auto-detect agent workflow failures.
 *
 * Closes the gap between hook-level tool errors (already covered by
 * telemetry-kit-error-collector.cjs) and workflow-level failures that only
 * surface when a background sub-agent completes. Without this hook, the
 * coordinator had to read the <task-notification> by eye and manually emit
 * a `[t1k:skill-bug]` marker.
 *
 * Pipeline:
 *   opt-out guard → feature flag → read transcript_path → extract last AI turn
 *   text + total tokens → apply 5 detection patterns → sanitize evidence →
 *   fingerprint + 7-day TTL dedup → rate-limit (3/session per pattern) →
 *   append `[t1k:skill-bug]` entries to pending-skill-updates.jsonl →
 *   emit `[t1k:workflow-failure-detected count=N]` to stdout.
 *
 * Detection patterns (each fires an independent skill-bug entry):
 *   P1  Mid-task stop   — incomplete-thought tail + token WINDOW (≥180K and ≤300K)
 *   P2  Skill fallback  — "cannot spawn" / "not actually available" / "<subj> fell back"
 *   P3  Tool unavailable— InputValidationError without ToolSearch recovery
 *   P4  Empty deliver.  — claimed Write at path but file missing or <100 bytes
 *   P5  Out-of-scope    — agent text admits modifying out-of-scope files
 *
 * False-positive guards (prose patterns only fire on a genuine sub-agent):
 *   - P1/P2/P5 are gated on a present, non-"unknown" agent_type. A missing
 *     agent_type on SubagentStop means we're likely reading the MAIN-session
 *     transcript, where this narration is normal — not a failure.
 *   - P1 uses a token WINDOW (floor 180K, ceiling 300K): above the ceiling the
 *     transcript is the main session (token bleed), so the signal is dropped.
 *   - P2 no longer matches the bare phrase "fall back to"/"falling back to" —
 *     those appear in ordinary prose describing fallback DESIGN behavior.
 *
 * Reuses (no duplicate utilities):
 *   - sanitize helpers from lib/kit-error-sanitizer.cjs (SSOT for redaction)
 *   - fingerprint()/checkAndRecord() from lib/kit-error-dedup.cjs
 *     scoped to its own cache via T1K_KIT_ERROR_CACHE_PATH env override
 *   - readFeatureFlag(), resolveClaudeDir(), ensureTelemetryDir(),
 *     findProjectRoot(), T1K constants from telemetry-utils.cjs
 *   - safeResolve from lib/safe-paths.cjs (validates transcript_path)
 *
 * Fail-open: any exception → process.exit(0). Crashes log to
 * ~/.claude/.workflow-failure-detector.log.
 *
 * Design note (AI-driven design): the hook emits FACTS (failure-pattern hits,
 * sanitized evidence snippets, agent type, token usage). It does NOT decide
 * whether to file an issue — that's the queue-processor + sub-agent's job.
 * No hardcoded skill→issue mappings.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');

const {
  parseHookStdin,
  isTelemetryEnabled,
  ensureTelemetryDir,
  findProjectRoot,
  resolveClaudeDir,
  readFeatureFlag,
  T1K,
} = require('./telemetry-utils.cjs');

const { safeResolve, SafePathError } = require('./lib/safe-paths.cjs');
const { logHook, createHookTimer } = require('./hook-logger.cjs');
const sanitizer = require('./lib/kit-error-sanitizer.cjs');

// ── Constants (data-driven config overrides) ──
const CACHE_FILENAME = '.workflow-failure-fingerprints.json';
const CRASH_LOG_FILENAME = '.workflow-failure-detector.log';
const QUEUE_FILENAME = 'pending-skill-updates.jsonl';
const RATE_DIR_NAME = 't1k-workflow-failure';
const FEATURE_FLAG = 'autoLessonSync';     // piggy-backs on the same flag as lesson-collector
const ENV_OPT_IN = 'T1K_AUTO_LESSON_SYNC'; // (same kill switch — sub-pipeline of auto-lesson)
const DRY_RUN_ENV = 'T1K_WORKFLOW_FAILURE_DRY_RUN';

// Token usage thresholds for P1 (mid-task stop). At/above this implies context
// exhaustion was the likely cause. Configurable via t1k-config-*.json.
const DEFAULT_MID_TASK_TOKEN_THRESHOLD = 180_000;
// Upper bound for P1. A genuine isolated sub-agent transcript rarely accumulates
// past this; when totalTokens exceeds it the SubagentStop hook is almost
// certainly reading the MAIN-session transcript (token bleed), not a fresh
// sub-agent. Observed false positive: tokens=682580 from a main-loop "let me
// read..." narration. Above the ceiling, the token signal is meaningless → skip.
const DEFAULT_MID_TASK_TOKEN_CEILING = 300_000;
// Empty-deliverable size threshold — files smaller than this when the agent
// claimed a Write are flagged as P4.
const DEFAULT_EMPTY_DELIVERABLE_BYTES = 100;
// Per-pattern rate limit (separate from lesson-collector's 5/session global).
const DEFAULT_MAX_PER_PATTERN_PER_SESSION = 3;

// ── Detection patterns (sanitized text only — never raw) ──
//
// P1 (mid-task stop) — incomplete-thought tail. We anchor on common
// trailing-fragment phrases that imply the agent intended to do another step.
// We do NOT use any single phrase as a smoking gun; the pattern only fires
// when paired with high token usage (see classifyMidTaskStop).
const MID_TASK_TAIL_RE = /\b(?:now let me|let me check|let me now|let me also|i['’]?ll just|i['’]?ll now|next,? i['’]?ll|next,? let me|going to|will continue|continuing with|let me look|let me read|let me see)\b[^.!?\n]{0,80}\.?\s*$/i;

// P2 (skill fallback) — skill body cannot fulfill stated purpose.
//
// IMPORTANT (#false-positive fix): bare "fall back to" / "falling back to" were
// REMOVED. They matched ordinary prose describing fallback *design behavior*
// (e.g. "the model-router would fail the whole pipe and fall back to Opus"),
// producing skill-bug false positives every turn. Every remaining alternative is
// self-diagnostic — it can ONLY appear when a skill/agent actually could not do
// its job. "fall back" is now matched ONLY when adjacent to a failure subject
// ("skill/agent/tool ... fell back" or "had to fall back ... because ... could
// not"), never as a bare verb phrase.
const SKILL_FALLBACK_RE = /\b(?:cannot spawn|can['’]?t spawn|hard error caught by pre-flight|not actually available in this context|not available in this fork|skill cannot|skill couldn['’]?t|(?:skill|agent|tool)(?:\s+\w+){0,4}\s+(?:fell back|had to fall back|forced to fall back))\b/i;

// P3 (tool unavailable) — deferred-tool schema not loaded.
const TOOL_VALIDATION_RE = /\b(?:InputValidationError|schema not loaded|tool schema is not loaded|deferred tool .*? not loaded)\b/i;
const TOOLSEARCH_RECOVERY_RE = /\bToolSearch\s*\(/i;

// P5 (out-of-scope) — self-admitted scope drift. Conservative match: the agent
// explicitly states it edited something outside its declared scope.
const OUT_OF_SCOPE_RE = /\b(?:out of scope|outside my scope|outside the declared scope|outside its declared scope|outside the agreed scope|went out of scope|edited files outside|modified files outside)\b/i;

// P4 (empty deliverable) — capture every claimed Write target the agent
// mentions and check on disk. Two complementary forms.
const WRITE_TARGET_RE_LIST = [
  // "Created file path/to/file.md"
  /(?:created|wrote|written|saved)(?:\s+(?:file|the file))?\s+(?:to|at)?\s*[`'"“”]?((?:[~.]?\/)?[\w\-./]+\.[A-Za-z][A-Za-z0-9]{0,7})[`'"“”]?/gi,
  // "Wrote N lines to path/to/file.md"
  /(?:wrote|written|saved)\s+(?:\d+\s+(?:lines?|bytes?)\s+)?to\s+[`'"“”]?((?:[~.]?\/)?[\w\-./]+\.[A-Za-z][A-Za-z0-9]{0,7})[`'"“”]?/gi,
];

/**
 * Read the workflow-failure-detector config block from any t1k-config-*.json
 * fragment in claudeDir. Piggy-backs on autoLessonSync feature flag (same kill
 * switch); per-detector limits live under workflowFailureDetector.{...}.
 */
function readConfig(claudeDir) {
  const defaults = {
    enabled: false,
    midTaskTokenThreshold: DEFAULT_MID_TASK_TOKEN_THRESHOLD,
    midTaskTokenCeiling: DEFAULT_MID_TASK_TOKEN_CEILING,
    emptyDeliverableBytes: DEFAULT_EMPTY_DELIVERABLE_BYTES,
    maxPerPatternPerSession: DEFAULT_MAX_PER_PATTERN_PER_SESSION,
    dedupeTTLDays: 7,
  };

  // Env kill switch (symmetric, mirrors lesson-collector)
  const envValue = process.env[ENV_OPT_IN];
  const envForceEnable = envValue === '1' || envValue === 'true';
  const envForceDisable = envValue === '0' || envValue === 'false' || envValue === '';
  let enabled;
  if (envForceDisable) {
    enabled = false;
  } else if (envForceEnable) {
    enabled = true;
  } else {
    enabled = readFeatureFlag(claudeDir, FEATURE_FLAG, defaults.enabled);
  }
  const result = { ...defaults, enabled };

  try {
    const files = fs.readdirSync(claudeDir)
      .filter(f => f.startsWith(T1K.CONFIG_PREFIX) && f.endsWith('.json'));
    for (const f of files) {
      try {
        const cfg = JSON.parse(fs.readFileSync(path.join(claudeDir, f), 'utf8'));
        const sub = cfg.workflowFailureDetector;
        if (sub && typeof sub === 'object') {
          if (typeof sub.midTaskTokenThreshold === 'number') result.midTaskTokenThreshold = sub.midTaskTokenThreshold;
          if (typeof sub.midTaskTokenCeiling === 'number') result.midTaskTokenCeiling = sub.midTaskTokenCeiling;
          if (typeof sub.emptyDeliverableBytes === 'number') result.emptyDeliverableBytes = sub.emptyDeliverableBytes;
          if (typeof sub.maxPerPatternPerSession === 'number') result.maxPerPatternPerSession = sub.maxPerPatternPerSession;
          if (typeof sub.dedupeTTLDays === 'number') result.dedupeTTLDays = sub.dedupeTTLDays;
        }
      } catch { /* skip malformed fragment */ }
    }
  } catch { /* no claudeDir */ }
  return result;
}

/** Stable session id — same shape as lesson-collector. */
function computeSessionId() {
  return process.env.CLAUDE_SESSION_ID ||
    crypto.createHash('md5')
      .update((process.env.CLAUDE_PROJECT_DIR || findProjectRoot()) + new Date().toISOString().slice(0, 10))
      .digest('hex').slice(0, 16);
}

/** $HOME/.claude — cross-platform global dir. */
function defaultGlobalClaudeDir() {
  const home = os.homedir() || process.env.HOME || process.env.USERPROFILE || os.tmpdir();
  return path.join(home, T1K.CLAUDE_DIR);
}

/**
 * Read the last assistant turn from the transcript JSONL.
 * Returns { text, totalTokens } — totalTokens is best-effort (transcript
 * entries often include usage.total_tokens or usage.input_tokens +
 * usage.output_tokens).
 */
function extractFinalTurn(transcriptPath) {
  const empty = { text: '', totalTokens: 0 };
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return empty;

  try {
    const raw = fs.readFileSync(transcriptPath, 'utf8');
    const lines = raw.trim().split('\n');
    let cumulativeInput = 0;
    let cumulativeOutput = 0;
    let lastTotalTokens = 0;
    let finalAssistantText = '';

    // Walk forward to accumulate token usage from every assistant turn,
    // and capture the last assistant turn's text.
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (!line) continue;
      let entry;
      try { entry = JSON.parse(line); } catch { continue; }
      if (!entry) continue;

      const isAssistant = entry.type === 'assistant' || entry.role === 'assistant'
        || (entry.message && entry.message.role === 'assistant');
      if (!isAssistant) continue;

      const usage = (entry.message && entry.message.usage) || entry.usage || null;
      if (usage) {
        if (typeof usage.total_tokens === 'number') {
          lastTotalTokens = usage.total_tokens;
        }
        if (typeof usage.input_tokens === 'number') cumulativeInput += usage.input_tokens;
        if (typeof usage.output_tokens === 'number') cumulativeOutput += usage.output_tokens;
      }

      const text = extractTextFromEntry(entry);
      if (text) finalAssistantText = text;
    }

    const totalTokens = lastTotalTokens || (cumulativeInput + cumulativeOutput);
    return { text: finalAssistantText, totalTokens };
  } catch {
    return empty;
  }
}

/** Concatenate text blocks from an assistant transcript entry. */
function extractTextFromEntry(entry) {
  const parts = [];
  const candidates = [entry.message, entry, entry.delta];
  for (const c of candidates) {
    if (!c) continue;
    const content = c.content;
    if (typeof content === 'string') parts.push(content);
    else if (Array.isArray(content)) {
      for (const block of content) {
        if (block && typeof block === 'object' && typeof block.text === 'string') {
          parts.push(block.text);
        }
      }
    }
  }
  return parts.join('\n').trim();
}

/** Sanitize evidence text via the shared kit-error-sanitizer helpers. */
function sanitizeEvidence(text, cwd, home) {
  let out = text;
  out = sanitizer._stripUserPaths(out, home, cwd);
  out = sanitizer._stripEnvVars(out);
  out = sanitizer._stripSecrets(out);
  out = sanitizer._stripSensitiveFilePaths(out);
  return out;
}

/**
 * Tail of the text — for trailing-thought matching we care about the final
 * 240 chars after trimming whitespace. (Anchoring on $ in the full text is
 * unreliable when agents append metadata.)
 */
function tailOf(text, n = 240) {
  if (!text) return '';
  return text.trim().slice(-n);
}

/**
 * P1 — mid-task stop. Fires only when ALL THREE hold:
 *   - trailing-thought regex matches the tail (last 240 chars), AND
 *   - totalTokens >= configured threshold (default 180K), AND
 *   - totalTokens <= configured ceiling (default 300K)
 * The token *window* (not just a floor) is critical: trailing thoughts alone
 * happen for legitimate reasons (the agent says "let me check" then runs a
 * tool). The floor restricts to near-exhausted context; the ceiling rejects the
 * case where the SubagentStop transcript is actually the MAIN-session transcript
 * (observed false positive: tokens=682580 — impossible for a fresh sub-agent —
 * paired with ordinary "let me read..." narration).
 */
function classifyMidTaskStop(text, totalTokens, threshold, ceiling) {
  if (totalTokens < threshold) return null;
  if (typeof ceiling === 'number' && ceiling > 0 && totalTokens > ceiling) return null;
  const tail = tailOf(text);
  const m = MID_TASK_TAIL_RE.exec(tail);
  if (!m) return null;
  return {
    pattern: 'mid-task-stop',
    bug: 'agent stopped mid-task without writing deliverable',
    evidence: `tokens=${totalTokens} tail="${m[0].slice(0, 100)}"`,
  };
}

/** P2 — skill body cannot fulfill stated purpose. */
function classifySkillFallback(text) {
  const m = SKILL_FALLBACK_RE.exec(text);
  if (!m) return null;
  // Pull a 120-char context window around the match
  const idx = m.index;
  const ctx = text.slice(Math.max(0, idx - 40), Math.min(text.length, idx + 120));
  return {
    pattern: 'skill-fallback',
    bug: 'skill body cannot fulfill stated purpose, fell back',
    evidence: `match="${m[0]}" ctx="${ctx.slice(0, 160).replace(/\s+/g, ' ')}"`,
  };
}

/** P3 — tool unavailable, no ToolSearch recovery. */
function classifyToolUnavailable(text) {
  const m = TOOL_VALIDATION_RE.exec(text);
  if (!m) return null;
  // If a ToolSearch call appears AFTER the error, treat as recovered.
  const errIdx = m.index;
  const afterErr = text.slice(errIdx);
  if (TOOLSEARCH_RECOVERY_RE.test(afterErr)) return null;
  return {
    pattern: 'tool-unavailable',
    bug: 'deferred tool used without ToolSearch schema load',
    evidence: `match="${m[0]}" no ToolSearch follow-up`,
  };
}

/** P5 — out-of-scope edits self-reported. */
function classifyOutOfScope(text) {
  const m = OUT_OF_SCOPE_RE.exec(text);
  if (!m) return null;
  const idx = m.index;
  const ctx = text.slice(Math.max(0, idx - 40), Math.min(text.length, idx + 120));
  return {
    pattern: 'out-of-scope',
    bug: 'agent reports modifications outside declared scope',
    evidence: `match="${m[0]}" ctx="${ctx.slice(0, 160).replace(/\s+/g, ' ')}"`,
  };
}

/**
 * P4 — empty deliverable. Walks every claimed Write target in the text and
 * fires for each path that does not exist OR is smaller than the threshold.
 * Returns an array (may be empty); other classifiers return single result.
 *
 * IMPORTANT: this classifier MUST be called with UNSANITIZED text. Sanitization
 * replaces real filesystem paths (e.g. /home/alice/proj/foo.md) with placeholder
 * tokens (e.g. <HOME>/proj/foo.md), which would then fail `fs.statSync()` and
 * either skip P4 entirely or produce false positives on placeholder strings.
 * Evidence strings emitted by this function are sanitized by the caller
 * (detectFailures) before being persisted to the queue.
 */
function classifyEmptyDeliverable(text, projectRoot, threshold) {
  const findings = [];
  const seen = new Set();
  for (const re of WRITE_TARGET_RE_LIST) {
    // Reset lastIndex (regex objects retain state across calls)
    re.lastIndex = 0;
    let m;
    let safety = 0;
    while ((m = re.exec(text)) !== null && safety++ < 20) {
      let candidate = (m[1] || '').trim();
      if (!candidate || seen.has(candidate)) continue;
      seen.add(candidate);

      // Resolve relative paths against projectRoot
      let resolved = candidate;
      if (!path.isAbsolute(resolved)) {
        if (resolved.startsWith('~/')) {
          resolved = path.join(os.homedir() || '', resolved.slice(2));
        } else {
          resolved = path.join(projectRoot, resolved);
        }
      }

      // Confine to project root or home for safety — never stat arbitrary disk
      let safe;
      try {
        safe = safeResolve(resolved, [projectRoot, os.homedir(), os.tmpdir()]);
      } catch (e) {
        if (e instanceof SafePathError) continue;
        throw e;
      }

      let exists = false;
      let size = 0;
      try {
        const stat = fs.statSync(safe);
        exists = true;
        size = stat.size;
      } catch { /* missing */ }

      if (!exists) {
        findings.push({
          pattern: 'empty-deliverable',
          bug: `claimed Write target does not exist: ${candidate}`,
          evidence: `path="${candidate}" missing`,
        });
      } else if (size < threshold) {
        findings.push({
          pattern: 'empty-deliverable',
          bug: `claimed Write target is empty or near-empty: ${candidate}`,
          evidence: `path="${candidate}" bytes=${size}`,
        });
      }
    }
  }
  return findings;
}

/**
 * Apply all 5 patterns. Returns flat array of findings.
 *   { pattern, bug, evidence }
 *
 * P1/P2/P3/P5 run on sanitized text (no path/secret sensitivity in their regex
 * triggers). P4 (empty-deliverable) MUST run on UNSANITIZED text because it
 * stat()s claimed Write targets from disk — sanitization would replace real
 * paths with placeholders before stat resolution. The caller is responsible
 * for passing the raw turn text alongside the sanitized text; if rawText is
 * omitted, P4 is skipped (fail-safe).
 *
 * Evidence strings emitted by P4 contain raw paths and must be sanitized by
 * the caller before being persisted to the queue. See callsite in main().
 */
function detectFailures(text, totalTokens, cfg, projectRoot, rawText, opts) {
  const findings = [];
  if (!text) return findings;

  // Prose-only patterns (P1 mid-task-stop, P2 skill-fallback, P5 out-of-scope)
  // are the ones prone to MAIN-loop false positives: they match narration the
  // main agent writes between tool calls. Gate them on a genuine sub-agent
  // termination — a present, non-"unknown" agent_type. When the SubagentStop
  // payload carries no real agent_type we are almost certainly reading the main
  // session transcript (observed: skill="agent:unknown" entries), so prose
  // patterns are suppressed. P3 (tool-unavailable, needs the literal
  // InputValidationError token) and P4 (empty-deliverable, disk-verified) are
  // self-grounding and run unconditionally.
  const genuineAgent = !!(opts && opts.genuineAgent);

  if (genuineAgent) {
    const r1 = classifyMidTaskStop(text, totalTokens, cfg.midTaskTokenThreshold, cfg.midTaskTokenCeiling);
    if (r1) findings.push(r1);

    const r2 = classifySkillFallback(text);
    if (r2) findings.push(r2);

    const r5 = classifyOutOfScope(text);
    if (r5) findings.push(r5);
  }

  const r3 = classifyToolUnavailable(text);
  if (r3) findings.push(r3);

  if (rawText) {
    const r4s = classifyEmptyDeliverable(rawText, projectRoot, cfg.emptyDeliverableBytes);
    for (const f of r4s) findings.push(f);
  }

  return findings;
}

/**
 * Write a crash log line. Never throws.
 */
function logCrash(err) {
  try {
    const logPath = path.join(defaultGlobalClaudeDir(), CRASH_LOG_FILENAME);
    const dir = path.dirname(logPath);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    const line = `[${new Date().toISOString()}] ${err && err.stack ? err.stack : String(err)}\n`;
    fs.appendFileSync(logPath, line);
  } catch { /* truly give up */ }
}

function main() {
  try {
    if (!isTelemetryEnabled()) return 0;

    const resolved = resolveClaudeDir();
    if (!resolved) return 0;
    const claudeDir = resolved.claudeDir;

    const cfg = readConfig(claudeDir);
    if (!cfg.enabled) return 0;

    const hookData = parseHookStdin() || {};
    const timer = createHookTimer('workflow-failure-detector');

    // Validate transcript_path BEFORE reading — same defense-in-depth as lesson-collector
    const rawTranscriptPath = hookData.transcript_path;
    let transcriptPath = null;
    if (rawTranscriptPath) {
      try {
        transcriptPath = safeResolve(rawTranscriptPath, [os.tmpdir(), os.homedir()]);
      } catch (e) {
        if (e instanceof SafePathError) {
          timer.end({ outcome: 'skip', note: 'unsafe-transcript-path' });
          return 0;
        }
        throw e;
      }
    }

    if (!transcriptPath) {
      // Fall back to inline text if testing supplies it
      if (typeof hookData.text === 'string' && hookData.text) {
        // Test harness path — proceed with inline text below
      } else {
        timer.end({ outcome: 'skip', note: 'no-transcript-path' });
        return 0;
      }
    }

    const turn = transcriptPath
      ? extractFinalTurn(transcriptPath)
      : { text: hookData.text || '', totalTokens: hookData.total_tokens || 0 };

    if (!turn.text) {
      timer.end({ outcome: 'skip', note: 'no-turn-text' });
      return 0;
    }

    const home = os.homedir() || '';
    const cwd = process.cwd();
    const projectRoot = findProjectRoot();
    const scrubbed = sanitizeEvidence(turn.text, cwd, home);

    // A genuine isolated sub-agent termination carries a present, non-"unknown"
    // agent_type. Prose-only patterns (P1/P2/P5) are suppressed without it to
    // avoid matching main-loop narration. The test-harness inline-text path
    // (hookData.text set, no transcript) sets agent_type explicitly when it
    // wants to exercise the prose patterns.
    const rawAgentType = typeof hookData.agent_type === 'string' ? hookData.agent_type.trim() : '';
    const genuineAgent = rawAgentType.length > 0 && rawAgentType !== 'unknown';

    // Pass BOTH sanitized (for P1/P2/P3/P5) and raw (for P4 disk-stat) text.
    // P4 emits evidence containing real paths; sanitize each finding's bug +
    // evidence below before persisting.
    const findings = detectFailures(scrubbed, turn.totalTokens, cfg, projectRoot, turn.text, { genuineAgent });
    if (findings.length === 0) {
      timer.end({ outcome: 'skip', note: 'no-findings' });
      return 0;
    }

    // P4 findings carry raw paths in bug + evidence — sanitize per-finding so
    // the queue never persists user filesystem paths or secrets.
    for (const f of findings) {
      if (f.pattern === 'empty-deliverable') {
        f.bug = sanitizeEvidence(f.bug, cwd, home);
        f.evidence = sanitizeEvidence(f.evidence, cwd, home);
      }
    }

    // Scope dedup cache to workflow-failure (mirror of lesson-collector pattern)
    const cachePath = path.join(defaultGlobalClaudeDir(), CACHE_FILENAME);
    const prevCacheEnv = process.env.T1K_KIT_ERROR_CACHE_PATH;
    process.env.T1K_KIT_ERROR_CACHE_PATH = cachePath;
    const { fingerprint, checkAndRecord } = require('./lib/kit-error-dedup.cjs');

    const telemetryDir = ensureTelemetryDir();
    const queuePath = path.join(telemetryDir, QUEUE_FILENAME);
    const sessionId = computeSessionId();

    // Per-pattern rate limiter (separate counter file per pattern this session)
    const rateDir = path.join(os.tmpdir(), RATE_DIR_NAME);
    if (!fs.existsSync(rateDir)) {
      try { fs.mkdirSync(rateDir, { recursive: true }); } catch { /* ok */ }
    }
    function patternCounterFile(pattern) {
      return path.join(rateDir, `${sessionId}.${pattern}.count`);
    }
    function readPatternCount(pattern) {
      try {
        const p = patternCounterFile(pattern);
        if (!fs.existsSync(p)) return 0;
        return parseInt(fs.readFileSync(p, 'utf8'), 10) || 0;
      } catch { return 0; }
    }
    function writePatternCount(pattern, n) {
      try { fs.writeFileSync(patternCounterFile(pattern), String(n)); } catch { /* ok */ }
    }

    const isDryRun = process.env[DRY_RUN_ENV] === '1';
    const nowIso = new Date().toISOString();
    const agentType = (typeof hookData.agent_type === 'string' && hookData.agent_type) || 'unknown';

    // Origin defaults — workflow failures by definition belong to the kit that
    // owns the failing agent/skill. The queue processor uses kit + skill to
    // resolve repo; we provide best-effort here.
    const originKit = 'theonekit-core';
    const skillSlug = `agent:${agentType}`;

    let queuedThisRun = 0;
    let droppedDuplicate = 0;
    let droppedRateLimit = 0;
    const enqueued = [];

    for (const finding of findings) {
      const ptn = finding.pattern;
      const sessionCount = readPatternCount(ptn);
      if (sessionCount >= cfg.maxPerPatternPerSession) {
        droppedRateLimit++;
        logHook('workflow-failure-detector', { drop: 'rate-limited', pattern: ptn });
        continue;
      }

      const fp = fingerprint(
        { tool: 'workflow-failure', cmd: ptn, stderrHead: finding.evidence },
        { reason: ptn, originKit }
      );
      const dedup = checkAndRecord(fp, {
        reason: ptn,
        originKit,
        maxAgeDays: cfg.dedupeTTLDays,
      });

      if (dedup.isDuplicate) {
        droppedDuplicate++;
        logHook('workflow-failure-detector', { drop: 'duplicate', pattern: ptn, fp });
        continue;
      }

      const entry = {
        ts: nowIso,
        type: 'skill-bug',
        fingerprint: fp,
        kit: originKit,
        skill: skillSlug,
        payload: {
          bug: finding.bug,
          evidence: finding.evidence,
          pattern: ptn,
          agentType,
          sessionId,
        },
        sessionId,
        dryRun: isDryRun,
        submitted: false,
        submittedAt: null,
        prUrl: null,
        issueUrl: null,
        source: 'workflow-failure-detector',
      };

      try { fs.appendFileSync(queuePath, JSON.stringify(entry) + '\n'); } catch { /* ok */ }

      if (isDryRun) {
        try {
          const logPath = path.join(defaultGlobalClaudeDir(), '.workflow-failure-dry-run.log');
          const logDir = path.dirname(logPath);
          if (!fs.existsSync(logDir)) fs.mkdirSync(logDir, { recursive: true });
          fs.appendFileSync(
            logPath,
            `[${nowIso}] DRY_RUN fp=${fp} pattern=${ptn} agent=${agentType}\n`
          );
        } catch { /* ok */ }
      }

      writePatternCount(ptn, sessionCount + 1);
      queuedThisRun++;
      enqueued.push({ pattern: ptn, fp });
    }

    // Restore the cache env so other hooks don't inherit our scoped path
    if (prevCacheEnv === undefined) delete process.env.T1K_KIT_ERROR_CACHE_PATH;
    else process.env.T1K_KIT_ERROR_CACHE_PATH = prevCacheEnv;

    if (queuedThisRun > 0) {
      const dryTag = isDryRun ? ' dryRun=1' : '';
      const patterns = enqueued.map(e => e.pattern).join(',');
      console.log(
        `[t1k:workflow-failure-detected] count=${queuedThisRun} patterns=${patterns} agent=${agentType}${dryTag}`
      );
    }

    logHook('workflow-failure-detector', {
      queued: queuedThisRun,
      duplicate: droppedDuplicate,
      rateLimited: droppedRateLimit,
      dryRun: isDryRun,
      agentType,
    });
    timer.end({ outcome: 'ok' });
    return 0;
  } catch (e) {
    logCrash(e);
    return 0; // fail-open
  }
}

// Spawned as a hook: run main and exit. Required as a module: just export.
if (require.main === module) {
  process.exit(main());
}

module.exports = {
  MID_TASK_TAIL_RE,
  SKILL_FALLBACK_RE,
  TOOL_VALIDATION_RE,
  TOOLSEARCH_RECOVERY_RE,
  OUT_OF_SCOPE_RE,
  WRITE_TARGET_RE_LIST,
  readConfig,
  extractFinalTurn,
  extractTextFromEntry,
  sanitizeEvidence,
  classifyMidTaskStop,
  classifySkillFallback,
  classifyToolUnavailable,
  classifyOutOfScope,
  classifyEmptyDeliverable,
  detectFailures,
  main,
};
