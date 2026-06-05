#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
//
// check-install-type.cjs — SessionStart hook (TEMPORARY).
//
// Warns the user when Claude Code is installed via npm (`@anthropic-ai/claude-code`)
// instead of the recommended native install. The npm-based distribution is
// being deprecated by Anthropic; this hook nudges studio users to migrate.
//
// LIFETIME: temporary. Self-disables after EXPIRES_AT (no-op early-return).
// REMOVAL: tracked at https://github.com/The1Studio/theonekit-core/issues/142
// REGISTRY: docs/temporary-hooks.md
//
// Detection (combined):
//   1. `which claude` (or `where claude` on Windows) → resolve symlinks
//   2. Path heuristic: if resolved path contains node_modules / .nvm / .npm /
//      yarn / pnpm / .volta → classify as "npm"
//   3. If path heuristic inconclusive → fallback `npm list -g --depth=0
//      --parseable @anthropic-ai/claude-code` (2s timeout)
//   4. Cache result in os.tmpdir()/t1k-install-type.json for 24h to avoid
//      repeating which/npm spawn cost on every session
//
// Fail-open: any error → exit 0, never block SessionStart.
'use strict';

const EXPIRES_AT = '2026-06-08'; // self-disable after this UTC date
const CACHE_TTL_MS = 24 * 60 * 60 * 1000; // 24h
const NPM_LIST_TIMEOUT_MS = 2000;
const MIGRATION_URL = 'https://code.claude.com/docs/en/setup';

try {
  // ── Self-disable after expiry ─────────────────────────────────────────
  const today = new Date().toISOString().slice(0, 10);
  if (today >= EXPIRES_AT) process.exit(0);

  const fs = require('fs');
  const os = require('os');
  const path = require('path');
  const { execFileSync } = require('child_process');

  // ── Cache lookup ───────────────────────────────────────────────────────
  const cacheFile = path.join(os.tmpdir(), 't1k-install-type.json');
  let cached = null;
  try {
    const raw = fs.readFileSync(cacheFile, 'utf8');
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed.ts === 'number' && Date.now() - parsed.ts < CACHE_TTL_MS) {
      cached = parsed;
    }
  } catch { /* cache miss */ }

  let installType = cached?.installType;
  let claudePath = cached?.claudePath;

  if (!installType) {
    // ── Step 1: locate claude binary ─────────────────────────────────────
    const isWin = process.platform === 'win32';
    const lookup = isWin ? 'where' : 'which';
    let rawPath = '';
    try {
      rawPath = execFileSync(lookup, ['claude'], {
        encoding: 'utf8',
        timeout: 1500,
        stdio: ['pipe', 'pipe', 'ignore'],
      }).split(/\r?\n/)[0].trim();
    } catch { /* claude not on PATH */ }

    if (!rawPath) {
      // Can't locate the binary — give up silently.
      process.exit(0);
    }

    // Resolve symlinks so we inspect the real install location.
    let resolved = rawPath;
    try { resolved = fs.realpathSync(rawPath); } catch { /* keep raw */ }
    claudePath = resolved;

    // ── Step 2: path heuristic ───────────────────────────────────────────
    const NPM_MARKERS = [
      `${path.sep}node_modules${path.sep}`,
      `${path.sep}.nvm${path.sep}`,
      `${path.sep}.npm${path.sep}`,
      `${path.sep}.volta${path.sep}`,
      `${path.sep}yarn${path.sep}`,
      `${path.sep}pnpm${path.sep}`,
      `${path.sep}.fnm${path.sep}`,
      `${path.sep}.asdf${path.sep}installs${path.sep}nodejs${path.sep}`,
    ];
    const NATIVE_MARKERS = [
      `${path.sep}.claude${path.sep}local${path.sep}`, // `claude migrate-installer`
      `${path.sep}.local${path.sep}bin${path.sep}claude`,
    ];

    const lcPath = resolved.toLowerCase();
    if (NPM_MARKERS.some((m) => lcPath.includes(m.toLowerCase()))) {
      installType = 'npm';
    } else if (NATIVE_MARKERS.some((m) => lcPath.includes(m.toLowerCase()))) {
      installType = 'native';
    }

    // ── Step 3: npm list fallback if heuristic inconclusive ──────────────
    if (!installType) {
      try {
        const out = execFileSync('npm', ['list', '-g', '--depth=0', '--parseable', '@anthropic-ai/claude-code'], {
          encoding: 'utf8',
          timeout: NPM_LIST_TIMEOUT_MS,
          stdio: ['pipe', 'pipe', 'ignore'],
        }).trim();
        if (out && out.split(/\r?\n/).some((line) => line.includes('@anthropic-ai/claude-code'))) {
          installType = 'npm';
        }
      } catch { /* npm not available or package not globally installed */ }
    }

    if (!installType) installType = 'native'; // best guess after all checks

    // ── Persist cache (best-effort) ──────────────────────────────────────
    try {
      fs.writeFileSync(
        cacheFile,
        JSON.stringify({ ts: Date.now(), installType, claudePath }),
        { mode: 0o600 }
      );
    } catch { /* cache write optional */ }
  }

  // ── Emit warning when npm detected ────────────────────────────────────
  if (installType === 'npm') {
    const msg = `[t1k:install-type] WARNING: Claude Code appears to be installed via npm (path: ${claudePath || 'unknown'}). The npm distribution is being deprecated; please migrate to the native install. Migration guide: ${MIGRATION_URL} . This warning is temporary and will stop firing after ${EXPIRES_AT}.`;
    console.log(msg);
  }

  process.exit(0);
} catch {
  process.exit(0);
}
