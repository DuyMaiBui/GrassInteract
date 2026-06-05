#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
/**
 * update-check-cache.cjs — 1-hour cooldown + exponential-backoff for SessionStart
 * GitHub release checks.
 *
 * Defends against GitHub's 60/h anonymous rate limit when 50 concurrent users
 * fire 2 update checks per session. Without this layer, the rate limit blows
 * in seconds and auto-update silently fails for ~30 min until the limit resets.
 *
 * Cache file: ~/.claude/.update-check-cache.json (mode 0o600)
 * Shape: { kits: { lastSuccess, consecutiveFailures }, cli: { ... } }
 *
 * Backoff curve:
 *   consecutiveFailures=0  → 1h after lastSuccess
 *   consecutiveFailures=1  → 10m after last failure
 *   consecutiveFailures=2  → 1h
 *   consecutiveFailures=3  → 6h
 *   consecutiveFailures>=4 → 6h cap
 *
 * Write protocol: temp-file + atomic rename (POSIX safe under concurrent
 * SessionStart hooks across the 50-user fleet).
 *
 * Fail-open: any read/parse/write error falls through to "always run" so a
 * corrupt cache file never blocks updates for users.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');

const CACHE_FILENAME = '.update-check-cache.json';
const ONE_HOUR_MS = 60 * 60 * 1000;
const TEN_MIN_MS = 10 * 60 * 1000;
const SIX_HOURS_MS = 6 * 60 * 60 * 1000;

function cachePath() {
  return path.join(os.homedir(), '.claude', CACHE_FILENAME);
}

function readCache() {
  const p = cachePath();
  if (!fs.existsSync(p)) return { kits: null, cli: null };
  try {
    const raw = fs.readFileSync(p, 'utf8');
    const parsed = JSON.parse(raw);
    return {
      kits: parsed.kits || null,
      cli: parsed.cli || null,
    };
  } catch {
    return { kits: null, cli: null };
  }
}

function writeCache(data) {
  const p = cachePath();
  const dir = path.dirname(p);
  try {
    fs.mkdirSync(dir, { recursive: true });
    const tmp = `${p}.${process.pid}.${Date.now()}.tmp`;
    fs.writeFileSync(tmp, JSON.stringify(data, null, 2) + '\n', { mode: 0o600 });
    fs.renameSync(tmp, p);
    try { fs.chmodSync(p, 0o600); } catch { /* best-effort */ }
  } catch { /* fail-open */ }
}

function backoffMs(consecutiveFailures) {
  if (consecutiveFailures <= 0) return ONE_HOUR_MS;
  if (consecutiveFailures === 1) return TEN_MIN_MS;
  if (consecutiveFailures === 2) return ONE_HOUR_MS;
  return SIX_HOURS_MS;
}

/**
 * Should the SessionStart hook run the network check now?
 * @param {'kits'|'cli'} scope
 * @returns {boolean} true = run; false = skip (cache hit or in backoff)
 */
function shouldRun(scope) {
  if (scope !== 'kits' && scope !== 'cli') return true;
  const cache = readCache();
  const entry = cache[scope];
  if (!entry || typeof entry.lastSuccess !== 'number') return true;

  const consecutiveFailures = Number(entry.consecutiveFailures) || 0;
  const wait = backoffMs(consecutiveFailures);
  const reference = consecutiveFailures > 0
    ? (entry.lastFailure || entry.lastSuccess)
    : entry.lastSuccess;
  const nextAllowed = reference + wait;
  return Date.now() >= nextAllowed;
}

function recordSuccess(scope) {
  if (scope !== 'kits' && scope !== 'cli') return;
  const cache = readCache();
  cache[scope] = {
    lastSuccess: Date.now(),
    consecutiveFailures: 0,
  };
  writeCache(cache);
}

function recordFailure(scope) {
  if (scope !== 'kits' && scope !== 'cli') return;
  const cache = readCache();
  const prev = cache[scope] || { consecutiveFailures: 0 };
  cache[scope] = {
    lastSuccess: prev.lastSuccess || 0,
    lastFailure: Date.now(),
    consecutiveFailures: (Number(prev.consecutiveFailures) || 0) + 1,
    // Preserve any version metadata recorded by recordCliVersions on prior runs.
    // SSOT lives in `cli` slot; we copy through unchanged so a failed network
    // poll doesn't erase the last-known versions used by require-current-cli.
    latestVersion: prev.latestVersion || null,
    currentVersion: prev.currentVersion || null,
    lastVersionCheckTimestamp: prev.lastVersionCheckTimestamp || null,
  };
  writeCache(cache);
}

/**
 * Record the detected current + latest CLI versions in the `cli` slot. Called
 * from check-cli-updates.cjs after a successful version detection round.
 * Bumps lastSuccess + clears consecutiveFailures (mirrors recordSuccess) so
 * the cooldown logic stays consistent.
 *
 * @param {{ current: string, latest: string }} versions  raw semver strings
 */
function recordCliVersions(versions) {
  if (!versions || typeof versions !== 'object') return;
  const current = typeof versions.current === 'string' ? versions.current : null;
  const latest = typeof versions.latest === 'string' ? versions.latest : null;
  if (!current || !latest) return;
  const cache = readCache();
  cache.cli = {
    lastSuccess: Date.now(),
    consecutiveFailures: 0,
    currentVersion: current,
    latestVersion: latest,
    lastVersionCheckTimestamp: Date.now(),
  };
  writeCache(cache);
}

/**
 * Refresh just the `currentVersion` field eagerly, independent of network cooldown.
 *
 * Reason: `recordCliVersions` only runs after a successful GitHub poll. When the
 * cooldown (shouldRun=false) blocks the poll, the cache keeps a stale `currentVersion`
 * — fatal after a self-update, because `require-current-cli` then compares the
 * stale cached current against the cached latest and false-blocks the user
 * (verified 2026-05-25: cache said 4.17.0 / latest 4.19.12, but local binary was
 * actually 4.19.12 → every state-mutating t1k command blocked).
 *
 * `current` here is read from the live installed binary by the caller (no network).
 * Safe to call on every SessionStart.
 *
 * @param {string} current  raw semver string from `readCliVersion(binary)`
 */
function recordCurrentVersion(current) {
  if (typeof current !== 'string' || !current) return;
  const cache = readCache();
  const prev = cache.cli || {};
  if (prev.currentVersion === current) return; // no-op if unchanged
  cache.cli = {
    ...prev,
    currentVersion: current,
  };
  writeCache(cache);
}

/**
 * Read the last-known CLI versions written by recordCliVersions.
 * @returns {{ current: string, latest: string, ageMs: number } | null}
 *          null when cache missing, corrupt, or version fields absent.
 */
function getCliVersions() {
  const cache = readCache();
  const cli = cache.cli;
  if (!cli || typeof cli !== 'object') return null;
  if (typeof cli.currentVersion !== 'string' || typeof cli.latestVersion !== 'string') return null;
  const ts = Number(cli.lastVersionCheckTimestamp);
  const ageMs = Number.isFinite(ts) && ts > 0 ? Date.now() - ts : Infinity;
  return {
    current: cli.currentVersion,
    latest: cli.latestVersion,
    ageMs,
  };
}

module.exports = {
  shouldRun,
  recordSuccess,
  recordFailure,
  recordCliVersions,
  recordCurrentVersion,
  getCliVersions,
  // exported for tests:
  _internal: { backoffMs, cachePath, readCache, writeCache },
};
