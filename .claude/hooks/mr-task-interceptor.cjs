#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
//
// mr-task-interceptor.cjs — PreToolUse hook on the Task tool.
//
// Implements The1Studio/theonekit-model-router#42 + #45 (Phase 1 + 2):
// decouple agent identity from model choice, then pick the model that best
// fits the task using a rule-based selector over capability tags.
//
// Flow:
//   1. Find the resolved agent's .md file (priority chain: project → user).
//   2. Parse frontmatter for `model:` and optional `mrHints.requires`.
//   3. Detect required capabilities from prompt + hints:
//        - prompt > 50K chars                                    → long-context
//        - prompt contains image content blocks                  → vision
//        - prompt matches reasoning keywords (audit/security/…)  → reasoning
//        - agent mrHints.requires array merges in
//   4. Filter providers-config.json candidates: enabled, all required caps.
//   5. Sort by tier (budget < standard < premium) — cheapest wins.
//   6. If no candidates fit → fall back to v2 static modelRouter.modelMapping.
//   7. If matched AND agent not in excludeAgents → run mr-delegate.sh, deny
//      the original Task, return cheap delegation's output via systemMessage.
//
// Fail-open: ANY internal exception → exit 0 (Task proceeds). A buggy hook
// must never block legitimate work. mr-delegate.sh owns safety; this hook
// is just the dispatcher.

'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawnSync } = require('child_process');

// ─── Debug log ──────────────────────────────────────────────────────────
// Every Task spawn this hook sees writes one JSONL line to ~/.model-router/
// debug.jsonl. Always on (low overhead — ~200 bytes per Task). Disable with
// MR_DEBUG_DISABLE=1. Read with: bash .claude/scripts/mr-tail-debug.sh
const DEBUG_LOG = path.join(os.homedir(), '.model-router', 'debug.jsonl');

function logDebug(entry) {
  if (process.env.MR_DEBUG_DISABLE === '1') return;
  try {
    fs.mkdirSync(path.dirname(DEBUG_LOG), { recursive: true });
    fs.appendFileSync(DEBUG_LOG, JSON.stringify({
      ts: new Date().toISOString(),
      ...entry,
    }) + '\n');
  } catch { /* fail-open: a broken log MUST never break delegation */ }
}

// passthrough() exits the hook with no routing decision. When a reason is
// passed, log the decision so consumers can diagnose "why didn't it route?"
// via mr-tail-debug.sh.
function passthrough(reason, extra) {
  if (reason) {
    logDebug({ event: 'intercept', decision: 'pass-' + reason, ...(extra || {}) });
  }
  process.exit(0);
}

let input;
try {
  input = JSON.parse(fs.readFileSync(0, 'utf8'));
} catch {
  passthrough();  // No reason logged — we have nothing to identify
}

// Anthropic renamed the subagent-spawn tool from `Task` (legacy) to `Agent`
// (current Claude Code as of 2026-05). Accept both — strict on either-or so
// unrelated tool fires (e.g. Bash, Edit) still passthrough fast.
// Fixes: The1Studio/theonekit-model-router#70 (interceptor exited 'not-task'
// on every spawn → transparent routing silently dead in current CC versions).
const SUBAGENT_TOOL_NAMES = new Set(['Task', 'Agent']);
if (!input || !SUBAGENT_TOOL_NAMES.has(input.tool_name)) {
  passthrough('not-task', { tool: input && input.tool_name });
}

// Recursion guard — don't intercept inside an already-delegated session.
if (process.env.MR_SPAWNED === '1') passthrough('mr-spawned');

const ti = input.tool_input || {};
const agentName = ti.subagent_type;
const prompt = ti.prompt || ti.description;
if (!agentName || !prompt) passthrough('invalid-input', { agent: agentName || null });

// Defense-in-depth: agent names are basenames of files under .claude/agents/.
// Reject anything with path separators, dot-dot, or other shell-meta chars.
// Without this, a Task spawn with subagent_type="../../etc/passwd" would
// have path.join() escape the agents dir and look for ".md" files outside.
if (!/^[A-Za-z0-9._-]+$/.test(agentName) || agentName.includes('..')) {
  passthrough('invalid-agent-name', { agent: agentName });
}

const projectRoot = input.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();

const searchDirs = [
  path.join(projectRoot, '.claude', 'agents'),
  path.join(process.env.HOME || os.homedir(), '.claude', 'agents'),
];

function fileExists(p) {
  try { fs.accessSync(p, fs.constants.R_OK); return true; } catch { return false; }
}

function findAgentFile(name) {
  for (const dir of searchDirs) {
    const p = path.join(dir, `${name}.md`);
    if (fileExists(p)) return p;
  }
  return null;
}

function readFrontmatter(file) {
  let text;
  try { text = fs.readFileSync(file, 'utf8'); } catch { return {}; }
  const m = text.match(/^---\s*\n([\s\S]*?)\n---\s*\n/);
  if (!m) return {};
  const fm = {};
  for (const line of m[1].split('\n')) {
    const kv = line.match(/^([A-Za-z_][\w-]*):\s*(.*)$/);
    if (!kv) continue;
    let v = kv[2].trim();
    v = v.replace(/^["'](.*)["']$/, '$1');
    fm[kv[1]] = v;
  }
  return fm;
}

function readJsonFile(p) {
  if (!fileExists(p)) return null;
  try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return null; }
}

function readConfig() {
  return readJsonFile(path.join(projectRoot, '.claude', 't1k-config-mr.json')) ||
         readJsonFile(path.join(process.env.HOME || os.homedir(), '.claude', 't1k-config-mr.json'));
}

function readProvidersConfig() {
  return readJsonFile(path.join(projectRoot, '.claude', 'providers-config.json')) ||
         readJsonFile(path.join(process.env.HOME || os.homedir(), '.claude', 'providers-config.json'));
}

// ─── Rule-based capability detection ────────────────────────────────────
// Hot path: this runs on every Task call. Stay deterministic + cheap.
// Rules adapted from LiteLLM's Complexity Router pattern — see
// docs/research-multi-provider-multimodal.md § 5.
function detectRequiredCapabilities(promptValue, fm) {
  const caps = new Set();

  // 1. Image content blocks → vision. Claude Code's Task tool today passes
  // `prompt` as a string, but the schema may grow to support content arrays
  // (image input) — handle both shapes.
  if (Array.isArray(promptValue)) {
    if (promptValue.some(b => b && (b.type === 'image' || b.type === 'input_image'))) {
      caps.add('vision');
    }
  }

  const promptText = typeof promptValue === 'string'
    ? promptValue
    : JSON.stringify(promptValue);

  // 2. Long-context: rough heuristic — >50K chars in the prompt.
  // ~12.5K tokens at 4 chars/token. Real long-context jobs are typically
  // much larger (whole codebases). Catches the obvious cases without
  // requiring a tokenizer.
  if (promptText.length > 50000) caps.add('long-context');

  // 3. Reasoning keywords. Check only the leading 2K chars to keep
  // detection fast for very long prompts; the topic usually surfaces early.
  const KEYWORDS = /\b(audit|security|architecture|design\s+(decision|review)|threat\s+model|deep\s+review|root\s+cause|exploit|vulnerability)\b/i;
  if (KEYWORDS.test(promptText.slice(0, 2000))) caps.add('reasoning');

  // 4. Agent frontmatter override: `mrHints: { requires: ["vision", ...] }`
  // Authors can pin requirements that the prompt-based heuristics would miss.
  // Frontmatter parser flattens, so mrHints arrives as a string of JSON.
  if (fm.mrHints) {
    try {
      const hints = typeof fm.mrHints === 'string' ? JSON.parse(fm.mrHints) : fm.mrHints;
      if (Array.isArray(hints.requires)) {
        hints.requires.forEach(c => typeof c === 'string' && caps.add(c));
      }
    } catch { /* ignore malformed hints */ }
  }

  return Array.from(caps);
}

// ─── Candidate filtering + capability-aware sort ────────────────────────
const TIER_RANK = { budget: 0, standard: 1, premium: 2 };

// Quality-driven capabilities: when a task needs `reasoning` or `long-context`,
// pick the BEST model that satisfies the requirement, not the cheapest.
// (User feedback 2026-05-25: "even when we need cheap model routing, quality
// still must be guaranteed".) Other capabilities stay cost-driven.
//
// long-context is a special case: tier is a weak proxy — sort by context_window
// descending so the model with the biggest window wins, then tier desc as tiebreaker.
const QUALITY_DRIVEN = new Set(['reasoning', 'long-context']);

function pickFromCandidates(requiredCaps, providersCfg) {
  if (!providersCfg || !providersCfg.providers) return null;
  const candidates = [];
  for (const [pname, p] of Object.entries(providersCfg.providers)) {
    if (p.enabled !== true) continue;
    for (const [mname, m] of Object.entries(p.models || {})) {
      if (m.enabled !== true) continue;
      const caps = Array.isArray(m.capabilities) ? m.capabilities : [];
      if (requiredCaps.every(r => caps.includes(r))) {
        candidates.push({
          provider: pname,
          model: mname,
          tier: m.tier || 'standard',
          tier_rank: TIER_RANK[m.tier || 'standard'] ?? 1,
          context_window: typeof m.context_window === 'number' ? m.context_window : 0,
        });
      }
    }
  }
  if (candidates.length === 0) return null;

  const wantsLongContext = requiredCaps.includes('long-context');
  const wantsQuality = requiredCaps.some(c => QUALITY_DRIVEN.has(c));

  candidates.sort((a, b) => {
    if (wantsLongContext) {
      // Larger context first, then premium-tier first as tiebreaker.
      if (a.context_window !== b.context_window) return b.context_window - a.context_window;
      return b.tier_rank - a.tier_rank;
    }
    if (wantsQuality) {
      // Premium-tier first for reasoning.
      return b.tier_rank - a.tier_rank;
    }
    // Default: cheapest tier first.
    return a.tier_rank - b.tier_rank;
  });
  return candidates[0];
}

// ─── Canonical model alias tables ───────────────────────────────────────
// modelMapping lookups try BOTH the raw frontmatter value AND its canonical
// alias, so config can be authored with either shorthand OR full-ID keys.
// Aliases per https://code.claude.com/docs/en/sub-agents.
// Fixes: The1Studio/theonekit-model-router#61 (Sonnet/Haiku silent passthrough)
const SHORT_TO_FULL = Object.freeze({
  opus:   'claude-opus-4-7',
  sonnet: 'claude-sonnet-4-6',
  haiku:  'claude-haiku-4-5-20251001',
});
const FULL_TO_SHORT = Object.freeze(
  Object.fromEntries(Object.entries(SHORT_TO_FULL).map(([s, f]) => [f, s]))
);

// ─── Kit-enforced passthrough policy ────────────────────────────────────
// Kit-author dictate: agents declaring an Opus-family model — OR `inherit`
// (Claude Code's default when `model:` is omitted) — MUST stay on Opus.
// Consumers cannot soften this via t1k-config-mr.json; the policy is the floor.
// Forms covered (per https://code.claude.com/docs/en/sub-agents):
//   - shorthand alias: `opus`
//   - full ID:         `claude-opus-4-7`
//   - 1M variant:      `claude-opus-4-7[1m]`
//   - inherit:         explicit `model: inherit` AND omitted-`model:` default
// excludeAgents (per-agent, consumer-editable) remains additive — consumers
// can ESCALATE more agents to passthrough but never DEMOTE one to cheap.
const KIT_PASSTHROUGH_MODELS = new Set([
  'opus',
  'claude-opus-4-7',
  'claude-opus-4-7[1m]',
  'inherit',
]);

// ─── Main flow ──────────────────────────────────────────────────────────
const cfg = readConfig();
const mr = cfg && cfg.modelRouter;
if (!mr) passthrough('no-config', { agent: agentName });
if (mr.enabled !== true) passthrough('disabled', { agent: agentName });
if (mr.mode !== 'transparent') passthrough('mode-' + (mr.mode || 'unset'), { agent: agentName });

const agentFile = findAgentFile(agentName);
if (!agentFile) {
  passthrough('unknown-agent', { agent: agentName, searched: searchDirs });
}

const fm = readFrontmatter(agentFile);
const modelKey = fm.model || 'inherit';

if (KIT_PASSTHROUGH_MODELS.has(modelKey)) {
  process.stderr.write(`[t1k:model-router] passthrough: agent=${agentName} model=${modelKey} (kit policy: Opus stays Opus)\n`);
  passthrough('kit-policy', { agent: agentName, modelKey });
}

const excluded = Array.isArray(mr.excludeAgents) && mr.excludeAgents.includes(agentName);
if (excluded) passthrough('excluded', { agent: agentName, modelKey });

const requiredCaps = detectRequiredCapabilities(prompt, fm);
const providersCfg = readProvidersConfig();
const ruleBased = pickFromCandidates(requiredCaps, providersCfg);

let pick = null;
let selectionSource = null;

if (ruleBased) {
  pick = ruleBased;
  selectionSource = requiredCaps.length > 0
    ? `rules:${requiredCaps.join(',')}`
    : `tier:${ruleBased.tier}`;
} else {
  // Fallback: v2 static modelMapping (preserves backward compat when no
  // candidate matches the required capability set).
  // Try raw key first, then cross-form alias (shorthand→full or full→shorthand)
  // so config authored with either form works. Fixes #61.
  const lookupCandidates = [modelKey, SHORT_TO_FULL[modelKey], FULL_TO_SHORT[modelKey]].filter(Boolean);
  let mapping = null;
  let matchedKey = null;
  if (mr.modelMapping) {
    for (const k of lookupCandidates) {
      if (mr.modelMapping[k]) { mapping = mr.modelMapping[k]; matchedKey = k; break; }
    }
  }
  if (mapping && mapping.provider && mapping.model) {
    pick = { provider: mapping.provider, model: mapping.model };
    selectionSource = `modelMapping:${matchedKey}`;
  }
}

if (!pick) {
  passthrough('no-candidate', {
    agent: agentName, modelKey, requiredCaps,
    providersAvailable: providersCfg && providersCfg.providers
      ? Object.keys(providersCfg.providers).length : 0,
  });
}

// Mirror readConfig/readProvidersConfig: try project-local first, then $HOME.
// The global install ships mr-delegate.sh to $HOME/.claude/scripts/, so
// consumers without a project-local copy would otherwise silently passthrough.
// Fixes #65.
const scriptPath = [
  path.join(projectRoot, '.claude', 'scripts', 'mr-delegate.sh'),
  path.join(process.env.HOME || os.homedir(), '.claude', 'scripts', 'mr-delegate.sh'),
].find(fileExists);
if (!scriptPath) passthrough('script-missing', { agent: agentName, pick });

let result;
try {
  // Pass --original-model so mr-delegate.sh can record the true model swap
  // in its savings telemetry event (requestModel=modelKey, routedModel=pick.model).
  // Without this, the script defaults requestModel=routedModel which makes the
  // worker's by-original-model breakdown meaningless. See #76 / contracts 35-36.
  // Outer spawnSync budget must cover len(pipe) × perHopTimeoutSec + buffer.
  // Pre-#88 this was fixed at 320s, which is < 2 × 300s primary timeout,
  // so the failover hop got strangled mid-call (stacked-timeout bug,
  // confirmed via DOTS-AI 2026-05-29 telemetry — see issue #88).
  // We now derive it from t1k-config-mr.json: per-hop × pipe.length + 30s.
  // Default budget of 270s = 2 hops × 120s + 30s buffer.
  const perHopSec = Number(mr.failover && mr.failover.perHopTimeoutSec) || 120;
  const pipeLen = Array.isArray(mr.failover && mr.failover.pipe) && mr.failover.pipe.length > 0
    ? mr.failover.pipe.length
    : 2;
  const outerBudgetMs = ((perHopSec * pipeLen) + 30) * 1000;

  result = spawnSync('bash', [
    scriptPath,
    agentName,
    typeof prompt === 'string' ? prompt : JSON.stringify(prompt),
    '--provider', pick.provider,
    '--model', pick.model,
    '--original-model', modelKey,
    '--selection-source', selectionSource,
  ], {
    encoding: 'utf8',
    timeout: outerBudgetMs,
    maxBuffer: 20 * 1024 * 1024,
    env: { ...process.env, CLAUDE_PROJECT_DIR: projectRoot },
  });
} catch (e) {
  passthrough('spawn-exception', { agent: agentName, pick, err: String(e && e.message || e) });
}

if (!result || result.error) {
  passthrough('spawn-error', { agent: agentName, pick, err: result && String(result.error || '') });
}

// EXIT_ALL_PROVIDERS_FAILED: 42 — mr-delegate.sh signals all cheap providers
// exhausted and the user opted into Anthropic fallback (MR_FALLBACK_TO_ANTHROPIC=1
// or modelRouter.failover.fallbackToAnthropic: true in t1k-config-mr.json).
// Passthrough to let the original Task spawn on Anthropic instead of returning
// a useless partial-output error banner.
if (result.status === 42) {
  process.stderr.write(`[t1k:model-router] All cheap providers failed for agent=${agentName}; falling back to Anthropic\n`);
  passthrough('all-providers-failed', { agent: agentName, pick });
}

const stdout = (result.stdout || '').trim();
const stderr = (result.stderr || '').trim();
const ok = result.status === 0;

// Banner is the user-visible signal that routing fired. The `agent[model]`
// format gives at-a-glance visibility into the swap that core's
// task-description-model-badge.cjs (#349) was designed for but never
// delivers in the routed case (deny path drops its modified description).
// Putting the badge here means transparent-routing consumers see the
// model on every spawn, in the same field where the cheap output appears.
const reasonLine = `[t1k:mr] ✓ ${agentName}[${pick.model}] — ${pick.provider} (${selectionSource})`;

logDebug({
  event: 'intercept',
  decision: 'route',
  agent: agentName,
  modelKey,
  requiredCaps,
  pick,
  selectionSource,
  promptChars: typeof prompt === 'string' ? prompt.length : JSON.stringify(prompt).length,
  delegateExit: result.status,
  delegateOk: ok,
});

// Per https://code.claude.com/docs/en/hooks: `systemMessage` is shown to the
// USER only, NOT injected into Claude's context. Only `permissionDecisionReason`
// reaches the calling LLM. The kit's prior design put the cheap-model output
// in systemMessage — which meant the parent session received only the banner
// (the reason line) and the actual delegated answer was invisible to Claude.
// Symptom: agent spawn returns `[t1k:model-router] Delegated ...` with no body.
//
// Fix: put the FULL body into permissionDecisionReason so the LLM sees the
// cheap-model output. Keep systemMessage too so the user UI also shows it.
const body =
  `${reasonLine}\n\n` +
  (ok ? '' : `(mr-delegate.sh exited ${result.status} — output may be partial)\n\n`) +
  `--- Delegated agent output ---\n${stdout || '(empty)'}` +
  (stderr ? `\n\n--- stderr ---\n${stderr}` : '');

const payload = {
  hookSpecificOutput: {
    hookEventName: 'PreToolUse',
    permissionDecision: 'deny',
    permissionDecisionReason: body,
  },
  systemMessage: body,
};

process.stdout.write(JSON.stringify(payload));
process.exit(0);
