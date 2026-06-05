#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
//
// mr-inline-tracker.cjs — PreToolUse hook on Edit / Write / Bash.
//
// Opt-in diagnostic hook. When `MR_DEBUG=1` is set in the environment OR
// `modelRouter.debug: true` is set in t1k-config-mr.json, this hook writes
// a JSONL line to ~/.model-router/debug.jsonl for every inline tool use
// from the MAIN session (where MR_SPAWNED is unset).
//
// Purpose: detect symptom "Claude is inlining Edit/Write/Bash instead of
// spawning Task for mechanical work" (Step 0 violation per
// rules/mr-transparent-routing.md). The Task interceptor cannot see this
// because no Task fires; this hook fills the visibility gap.
//
// Always exits 0 — never blocks the tool. Fail-open on any internal error.

'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');

// Recursion guard — don't log inline tools INSIDE a delegated session.
// Those are expected behavior (the cheap model runs Edit/Write/Bash in its
// own session); logging them would flood debug.jsonl.
if (process.env.MR_SPAWNED === '1') process.exit(0);

let input;
try {
  input = JSON.parse(fs.readFileSync(0, 'utf8'));
} catch {
  process.exit(0);
}

if (!input || !input.tool_name) process.exit(0);

// Check debug mode: env var wins; else config flag.
function readConfig(projectRoot) {
  for (const p of [
    path.join(projectRoot, '.claude', 't1k-config-mr.json'),
    path.join(os.homedir(), '.claude', 't1k-config-mr.json'),
  ]) {
    try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { /* try next */ }
  }
  return null;
}

const projectRoot = input.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();
const debugEnabled = process.env.MR_DEBUG === '1'
  || (readConfig(projectRoot) || {}).modelRouter?.debug === true;

if (!debugEnabled) process.exit(0);

// Build a short, privacy-conscious preview of the tool target. NEVER log
// the full prompt — Edit/Write file contents and Bash commands can contain
// secrets. Cap to 80 chars; logs go to a 0600-permissioned local file.
const ti = input.tool_input || {};
let target;
switch (input.tool_name) {
  case 'Edit':
  case 'Write':
  case 'NotebookEdit':
    target = ti.file_path || ti.notebook_path || '';
    break;
  case 'Bash':
    target = ti.command || '';
    break;
  default:
    target = '';
}

const DEBUG_LOG = path.join(os.homedir(), '.model-router', 'debug.jsonl');
try {
  fs.mkdirSync(path.dirname(DEBUG_LOG), { recursive: true });
  fs.appendFileSync(DEBUG_LOG, JSON.stringify({
    ts: new Date().toISOString(),
    event: 'inline',
    tool: input.tool_name,
    target_preview: typeof target === 'string' ? target.slice(0, 80) : '',
  }) + '\n');
} catch { /* fail-open */ }

process.exit(0);
