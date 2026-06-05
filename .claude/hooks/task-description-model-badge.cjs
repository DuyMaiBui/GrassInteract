#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
/**
 * task-description-model-badge.cjs — PreToolUse:Task hook
 *
 * Appends the agent's resolved model to the Task description so it surfaces
 * in the Claude Code backgrounded-agent manage pane. Instead of:
 *
 *   t1k-git-manager(Process lesson queue)
 *
 * the pane shows:
 *
 *   t1k-git-manager(Process lesson queue [haiku])
 *
 * or, when transparent routing is active:
 *
 *   t1k-git-manager(Process lesson queue [kimi/kimi-k2.5])
 *
 * Algorithm (per task spec):
 *   1. Validate PreToolUse for Task tool — else passthrough (exit 0).
 *   2. Extract subagent_type + description.
 *   3. Skip cases: already-badged, no subagent_type, MR_SPAWNED=1.
 *   4. Resolve agent frontmatter `model:` via priority chain
 *      (project .claude/agents/ → ~/.claude/agents/).
 *   5. Apply transparent-routing override from t1k-config-mr.json.
 *   6. Rewrite description: `${description} [${resolvedModel}]`.
 *   7. Emit `{"tool_input": {...modified...}}` on stdout; exit 0.
 *
 * Fail-open: any internal exception → exit 0, original input unchanged.
 *
 * Kill switch: T1K_TASK_DESCRIPTION_MODEL_BADGE_DISABLED=1 env var.
 *
 * Composes with mr-task-interceptor.cjs:
 *   - This hook runs FIRST (listed before mr-task-interceptor in settings.json).
 *   - If mr-task-interceptor denies the Task, the modified description is dropped
 *     along with the Task — no harm done.
 *   - If mr-task-interceptor passes through, the badged description reaches the
 *     real Task spawn and appears in the manage pane.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');

// ── helpers ────────────────────────────────────────────────────────────────

function passthrough() { process.exit(0); }

function readJsonFile(p) {
  try {
    if (!fs.existsSync(p)) return null;
    return JSON.parse(fs.readFileSync(p, 'utf8'));
  } catch { return null; }
}

/**
 * Parse YAML frontmatter from a markdown file.
 * Borrows the same lightweight approach as mr-task-interceptor.cjs:
 * match the first ---…--- block, then parse key: value lines.
 * Does NOT use a full YAML parser — intentional per task constraints.
 *
 * @param {string} filePath
 * @returns {Object} flat map of frontmatter key→value (all strings)
 */
function readFrontmatter(filePath) {
  let text;
  try { text = fs.readFileSync(filePath, 'utf8'); } catch { return {}; }
  const m = text.match(/^---\s*\n([\s\S]*?)\n---\s*\n/);
  if (!m) return {};
  const fm = {};
  for (const line of m[1].split('\n')) {
    const kv = line.match(/^([A-Za-z_][\w-]*):\s*(.*)$/);
    if (!kv) continue;
    let v = kv[2].trim().replace(/^["'](.*)["']$/, '$1');
    fm[kv[1]] = v;
  }
  return fm;
}

/**
 * Find the agent's .md file using the priority chain:
 *   1. <projectRoot>/.claude/agents/<name>.md
 *   2. ~/.claude/agents/<name>.md
 *
 * @param {string} agentName — basename without .md, e.g. "t1k-git-manager"
 * @param {string} projectRoot
 * @returns {string|null} absolute path or null if not found
 */
function findAgentFile(agentName, projectRoot) {
  const dirs = [
    path.join(projectRoot, '.claude', 'agents'),
    path.join(process.env.HOME || os.homedir(), '.claude', 'agents'),
  ];
  for (const dir of dirs) {
    const p = path.join(dir, `${agentName}.md`);
    try { fs.accessSync(p, fs.constants.R_OK); return p; } catch { /* try next */ }
  }
  return null;
}

/**
 * Resolve the effective model for `agentName` after transparent-routing override.
 *
 * Steps:
 *   a. Read agent frontmatter → `model:` field (or "inherit" if missing).
 *   b. Read t1k-config-mr.json (project first, then global).
 *   c. If MR enabled + mode=transparent + agent not excluded:
 *        - Check modelMapping[frontmatterModel] → "provider/model" string.
 *   d. Return the resolved label (frontmatterModel or "provider/model").
 *
 * @param {string} agentName
 * @param {string} projectRoot
 * @returns {string} e.g. "haiku", "kimi/kimi-k2.5", "?", "inherit"
 */
function resolveModel(agentName, projectRoot) {
  // Step 1: agent file + frontmatter
  const agentFile = findAgentFile(agentName, projectRoot);
  if (!agentFile) return '?';

  const fm = readFrontmatter(agentFile);
  const frontmatterModel = fm.model && fm.model.trim() ? fm.model.trim() : 'inherit';

  // Step 2: transparent routing config
  const mrCfg = (
    readJsonFile(path.join(projectRoot, '.claude', 't1k-config-mr.json')) ||
    readJsonFile(path.join(process.env.HOME || os.homedir(), '.claude', 't1k-config-mr.json'))
  );

  const mr = mrCfg && mrCfg.modelRouter;
  if (!mr || mr.enabled !== true || mr.mode !== 'transparent') {
    return frontmatterModel;
  }

  // Step 3: excluded agents — no override
  if (Array.isArray(mr.excludeAgents) && mr.excludeAgents.includes(agentName)) {
    return frontmatterModel;
  }

  // Step 4: model mapping lookup
  const mapping = mr.modelMapping && mr.modelMapping[frontmatterModel];
  if (mapping && mapping.model) {
    // Include provider prefix so the badge distinguishes kimi/kimi-k2.5 from
    // a same-named model on a different provider. Keep it concise.
    return mapping.provider ? `${mapping.provider}/${mapping.model}` : mapping.model;
  }

  return frontmatterModel;
}

// ── main ───────────────────────────────────────────────────────────────────

function main() {
  // Kill switch
  if (process.env.T1K_TASK_DESCRIPTION_MODEL_BADGE_DISABLED === '1') {
    passthrough();
  }

  // Recursion guard: don't badge inside an already-delegated session
  if (process.env.MR_SPAWNED === '1') {
    passthrough();
  }

  // Read + validate stdin
  let hookData;
  try {
    const raw = fs.readFileSync(0, 'utf8').trim();
    if (!raw) passthrough();
    hookData = JSON.parse(raw);
  } catch {
    // Malformed JSON → fail-open, don't block
    passthrough();
  }

  if (!hookData || hookData.tool_name !== 'Task') passthrough();

  const ti = hookData.tool_input || {};
  const agentName = ti.subagent_type;
  const description = typeof ti.description === 'string' ? ti.description : '';

  // Skip: no subagent_type
  if (!agentName) passthrough();

  // Security: reject path-traversal attempts in agent name
  if (!/^[A-Za-z0-9._-]+$/.test(agentName) || agentName.includes('..')) passthrough();

  // Skip: description already ends with [...] — already badged (re-spawn case)
  // Match: ends with `[something]` optionally followed by whitespace
  if (/\[[^\]]+\]\s*$/.test(description)) passthrough();

  // Resolve project root using the same logic as mr-task-interceptor
  const projectRoot = hookData.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();

  // Resolve the effective model
  const resolvedModel = resolveModel(agentName, projectRoot);

  // Build the modified tool_input
  const modifiedInput = Object.assign({}, ti, {
    description: `${description} [${resolvedModel}]`,
  });

  // Emit modified tool_input per Claude Code PreToolUse protocol
  process.stdout.write(JSON.stringify({ tool_input: modifiedInput }));
  process.exit(0);
}

try {
  main();
} catch (err) {
  // Fail-open: any uncaught exception must never block the Task spawn
  try { process.stderr.write(`[t1k:task-description-model-badge] error: ${err && err.message || err}\n`); } catch {}
  process.exit(0);
}
