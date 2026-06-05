#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
/**
 * t1k-update-runner.cjs — Intermediate wrapper that runs `t1k update --yes`
 * in the background and persists the outcome so the NEXT SessionStart can
 * surface "PREV RUN FAILED" banners (Chrome/VSCode auto-update pattern).
 *
 * Phase 02 of 260418-1942-t1k-ecosystem-fixes. Detached children lose their
 * exit code when the parent unrefs immediately — this wrapper plugs that hole.
 *
 * Invocation (from t1k-update-spawn.cjs):
 *   node /path/to/t1k-update-runner.cjs <binary> <...args>
 *
 * Outputs (all under $HOME/.claude/):
 *   .kit-update.log     — human-readable log with appended `[t1k-update] exit=N ts=ISO` footer
 *   .kit-update.status  — JSON { exitCode, ts, args, filesChanged[], kits[], stderrTail }
 *                         written atomically (tmp + rename). Consumed by Phase 03's
 *                         scope-safety gate (`expectedFiles`) and the PREV RUN FAILED banner.
 *
 * Cross-platform: no /dev/stdin, no `2>/dev/null`, no shell syntax. Uses spawnSync,
 * os.homedir, path.join, process.platform.
 */

'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync, execFileSync } = require('child_process');
const { T1K } = require('../telemetry-utils.cjs');

const DEBUG = process.env.T1K_DEBUG_UPDATE === '1';
const STDERR_TAIL_BYTES = 2 * 1024;

function dbg(msg) {
  if (!DEBUG) return;
  try { process.stderr.write(`[t1k-update-runner] ${msg}\n`); } catch { /* ok */ }
}

function claudeHomeDir() {
  const home = process.env.HOME || process.env.USERPROFILE || os.homedir();
  return path.join(home, '.claude');
}

function logPath()    { return path.join(claudeHomeDir(), T1K.PATHS.UPDATE_LOG); }
function statusPath() { return path.join(claudeHomeDir(), T1K.PATHS.UPDATE_STATUS); }

/**
 * Read installed kit names from $HOME/.claude/metadata.json (best effort).
 * Derives a PRE-UPDATE snapshot of kits so Phase 03 can build a
 * user-friendly commit message even if the update itself rewrites metadata.
 *
 * @returns {string[]} kit repo short names (e.g. ["theonekit-unity"])
 */
function readInstalledKits() {
  try {
    const metaPath = path.join(claudeHomeDir(), 'metadata.json');
    const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
    const kits = new Set();
    const mods = meta && meta.installedModules ? meta.installedModules : {};
    for (const entry of Object.values(mods)) {
      if (entry && typeof entry === 'object' && entry.repository) {
        kits.add(String(entry.repository).split('/').pop());
      }
    }
    return Array.from(kits);
  } catch { return []; }
}

/**
 * List tracked + untracked files in the repo at `cwd` (no --exclude-standard
 * so untracked-but-gitignored files are still caught). Returns [] on any error.
 * @param {string} cwd
 * @returns {string[]}
 */
function listGitFiles(cwd) {
  try {
    const raw = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard'], {
      cwd, encoding: 'utf8', timeout: 10000,
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    });
    return raw.split('\n').filter(Boolean);
  } catch { return []; }
}

/**
 * Derive the list of `.claude/` files the update actually changed.
 *
 * Strategy:
 *   1. Prefer `git diff --name-only HEAD` scoped to `.claude/` — captures both
 *      tracked edits and deletions. Untracked (new) files are added via
 *      `git ls-files --others --exclude-standard`.
 *   2. If CWD is not a git repo, return [].
 *
 * Paths are repo-root-relative so Phase 03's scope-safety gate can use them
 * directly as `expectedFiles` with `git diff --name-only` comparison.
 *
 * @param {string} cwd
 * @returns {string[]}
 */
function deriveFilesChanged(cwd) {
  let tracked = [];
  try {
    const raw = execFileSync('git', ['diff', '--name-only', 'HEAD', '--', '.claude/'], {
      cwd, encoding: 'utf8', timeout: 10000,
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    });
    tracked = raw.split('\n').filter(Boolean);
  } catch { /* not a git repo or no HEAD */ }

  let untracked = [];
  try {
    const raw = execFileSync('git', ['ls-files', '--others', '--exclude-standard', '--', '.claude/'], {
      cwd, encoding: 'utf8', timeout: 10000,
      stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true,
    });
    untracked = raw.split('\n').filter(Boolean);
  } catch { /* ok */ }

  const merged = new Set([...tracked, ...untracked]);
  return Array.from(merged);
}

/**
 * Truncate a buffer to the last `maxBytes` of UTF-8 text. Safe on non-UTF-8
 * inputs (returns what decodes). Used for `stderrTail` in the status file.
 * @param {Buffer|string|null|undefined} buf
 * @param {number} maxBytes
 * @returns {string}
 */
function tailText(buf, maxBytes) {
  if (!buf) return '';
  const s = Buffer.isBuffer(buf) ? buf.toString('utf8') : String(buf);
  if (s.length <= maxBytes) return s;
  return s.slice(-maxBytes);
}

/**
 * Write a JSON payload atomically: tmp file in the same directory, then
 * fs.renameSync to the final path. Cross-platform; rename within the same
 * filesystem is atomic on Linux, macOS, and Windows.
 */
function writeStatusAtomic(finalPath, payload) {
  const dir = path.dirname(finalPath);
  try { fs.mkdirSync(dir, { recursive: true }); } catch { /* ok */ }
  const tmp = path.join(dir, `.kit-update.status.${process.pid}.${Date.now()}.tmp`);
  fs.writeFileSync(tmp, JSON.stringify(payload, null, 2));
  fs.renameSync(tmp, finalPath);
}

function appendLogFooter(exitCode) {
  try {
    fs.appendFileSync(logPath(), `[t1k-update] exit=${exitCode} ts=${new Date().toISOString()}\n`);
  } catch { /* ok */ }
}

// ── main ────────────────────────────────────────────────────────────────────

(function main() {
  const [, , binary, ...args] = process.argv;

  if (!binary) {
    // Invocation error — still emit a status so the next session knows.
    const payload = {
      exitCode: 2,
      ts: new Date().toISOString(),
      args: [],
      filesChanged: [],
      kits: [],
      stderrTail: 'runner invoked without <binary> argument',
    };
    try { writeStatusAtomic(statusPath(), payload); } catch { /* ok */ }
    appendLogFooter(2);
    process.exit(2);
  }

  dbg(`binary=${binary} args=${JSON.stringify(args)} cwd=${process.cwd()}`);

  // Snapshot kits BEFORE the update — metadata may be rewritten by the CLI.
  const kits = readInstalledKits();

  // spawnSync to capture exit code + stderr in-process. Stdout goes to the log
  // file via appending from the parent's stdio inheritance (runner itself is
  // spawned detached with fd → log).
  const result = spawnSync(binary, args, {
    cwd: process.cwd(),
    env: process.env, // already set by caller (NO_COLOR, CI, etc.)
    stdio: ['ignore', 'inherit', 'pipe'], // stdout → log fd, stderr captured
    windowsHide: true,
  });

  // exitCode vs signal: mirror Node convention. Signals surface as 128 + signum.
  let exitCode;
  if (result.error) {
    exitCode = 1;
  } else if (typeof result.status === 'number') {
    exitCode = result.status;
  } else if (result.signal) {
    // SIGTERM → 143, SIGINT → 130, etc. (128 + signal number on POSIX)
    const sigNums = { SIGINT: 2, SIGTERM: 15, SIGKILL: 9, SIGHUP: 1, SIGQUIT: 3 };
    exitCode = 128 + (sigNums[result.signal] || 0);
  } else {
    exitCode = 1;
  }

  dbg(`spawn result: status=${result.status} signal=${result.signal} error=${result.error && result.error.message}`);

  // Mirror child stderr to the log so humans can inspect it.
  const stderrBuf = result.stderr || Buffer.alloc(0);
  try {
    if (stderrBuf && stderrBuf.length > 0) fs.appendFileSync(logPath(), stderrBuf);
  } catch { /* ok */ }

  const stderrTail = tailText(stderrBuf, STDERR_TAIL_BYTES);
  const filesChanged = deriveFilesChanged(process.cwd());

  const payload = {
    exitCode,
    ts: new Date().toISOString(),
    args,
    filesChanged,
    kits,
    stderrTail,
  };

  try { writeStatusAtomic(statusPath(), payload); }
  catch (err) { dbg(`status write failed: ${err && err.message}`); }

  appendLogFooter(exitCode);

  process.exit(exitCode);
})();

module.exports = {
  // Exported for unit tests only — the module also auto-runs via IIFE above.
  // Test harness sets env T1K_UPDATE_RUNNER_NO_IIFE=1 to suppress — not used
  // currently because tests spawn the runner as a child process.
  _internal: {
    claudeHomeDir,
    logPath,
    statusPath,
    readInstalledKits,
    deriveFilesChanged,
    tailText,
    writeStatusAtomic,
    listGitFiles,
  },
};
