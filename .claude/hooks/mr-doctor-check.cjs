#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
/**
 * mr-doctor-check.cjs — Doctor hook for model-router (v2).
 *
 * Validates: oc-go-cc hosted endpoint, ccs auth proxy, delegate script,
 * interceptor hook, skill, gh auth, consumer agent roster.
 *
 * Exit codes:
 *   0 = all checks pass (or warnings only)
 *   1 = one or more checks failed (doctor reports issues)
 */

const { execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

/**
 * Mirrors the runtime fallback chain in mr-task-interceptor.cjs:105-107.
 * Returns { path, scope } or null if neither location has the file.
 */
function resolveProvidersConfigPath() {
  const projectPath = path.join(process.cwd(), '.claude', 'providers-config.json');
  if (fs.existsSync(projectPath)) return { path: projectPath, scope: 'project' };
  const globalPath = path.join(os.homedir(), '.claude', 'providers-config.json');
  if (fs.existsSync(globalPath)) return { path: globalPath, scope: 'global' };
  return null;
}

/**
 * Numeric semver compare. Returns negative if a<b, 0 if equal, positive if a>b.
 * Tolerates pre-release suffixes by splitting on '-' and comparing numeric parts only.
 * Used by the kit-freshness check below.
 */
function compareSemver(a, b) {
  const norm = s => String(s).split('-')[0].split('.').map(n => parseInt(n, 10) || 0);
  const A = norm(a);
  const B = norm(b);
  const len = Math.max(A.length, B.length);
  for (let i = 0; i < len; i++) {
    const x = A[i] || 0;
    const y = B[i] || 0;
    if (x !== y) return x - y;
  }
  return 0;
}

const results = [];
let hasFailure = false;

function check(name, fn) {
  try {
    const result = fn();
    results.push({ name, status: result.status, message: result.message });
    if (result.status === 'fail') hasFailure = true;
  } catch (err) {
    results.push({ name, status: 'fail', message: err.message });
    hasFailure = true;
  }
}

// oc-go-cc hosted endpoint (auth-proxy on DietPi, gated by gh token).
check('opencode-go-hosted-reachable', () => {
  const endpoint = process.env.OPENCODE_GO_ENDPOINT || 'https://oc-go-cc.the1studio.org';
  try {
    const code = execSync(`curl -s -o /dev/null -w "%{http_code}" --max-time 5 ${endpoint}/health`,
      { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'], timeout: 6000 }).trim();
    if (code === '200') return { status: 'pass', message: `${endpoint} reachable (200)` };
    if (code === '403' || code === '401') return { status: 'pass', message: `${endpoint} reachable (HTTP ${code} = auth-proxy alive, gh token needed for real calls)` };
    return { status: 'warn', message: `${endpoint} returned HTTP ${code}` };
  } catch (err) {
    return { status: 'warn', message: `${endpoint} unreachable: ${err.message.split('\n')[0]}` };
  }
});

// Consumer's installed agent roster — v2 ships zero agents; delegation
// requires consumer to have installed at least one other kit (or to have
// authored agents locally).
check('consumer-agent-roster', () => {
  const searchDirs = [
    path.join(process.cwd(), '.claude/agents'),
    path.join(process.env.CLAUDE_PROJECT_DIR || '', '.claude/agents'),
    path.join(process.env.HOME || '', '.claude/agents'),
  ].filter(Boolean).filter(d => { try { fs.accessSync(d); return true; } catch { return false; } });

  const seen = new Set();
  for (const dir of searchDirs) {
    for (const f of fs.readdirSync(dir)) {
      if (f.startsWith('t1k-') && f.endsWith('.md')) seen.add(f);
    }
  }
  if (seen.size === 0) {
    return {
      status: 'warn',
      message: 'No t1k-* agents discoverable. Install theonekit-core (or any kit) to give model-router something to delegate to.',
    };
  }
  return { status: 'pass', message: `${seen.size} t1k-* agent(s) discoverable across cwd + ~/.claude/agents` };
});

// Skill present.
check('skill-installed', () => {
  const skillPath = path.join(process.cwd(), '.claude/skills/t1k-model-router-delegate/SKILL.md');
  if (fs.existsSync(skillPath)) return { status: 'pass', message: 'Skill t1k-model-router-delegate installed' };
  return { status: 'fail', message: 'Skill t1k-model-router-delegate missing' };
});

// Delegate script.
check('delegate-script', () => {
  const scriptPath = path.join(process.cwd(), '.claude/scripts/mr-delegate.sh');
  if (!fs.existsSync(scriptPath)) return { status: 'fail', message: 'mr-delegate.sh missing' };
  try { fs.accessSync(scriptPath, fs.constants.X_OK); return { status: 'pass', message: 'mr-delegate.sh executable' }; }
  catch { return { status: 'warn', message: 'mr-delegate.sh exists but not executable' }; }
});

// Task interceptor hook (new in v2 — the primary routing mechanism).
check('task-interceptor', () => {
  const hookPath = path.join(process.cwd(), '.claude/hooks/mr-task-interceptor.cjs');
  if (!fs.existsSync(hookPath)) return { status: 'fail', message: 'mr-task-interceptor.cjs missing — PreToolUse:Task routing disabled' };
  return { status: 'pass', message: 'mr-task-interceptor.cjs present' };
});

// modelMapping config.
check('model-mapping-config', () => {
  const cfgPath = path.join(process.cwd(), '.claude/t1k-config-mr.json');
  if (!fs.existsSync(cfgPath)) return { status: 'warn', message: 't1k-config-mr.json not in project (using global default)' };
  try {
    const cfg = JSON.parse(fs.readFileSync(cfgPath, 'utf8'));
    const mm = cfg && cfg.modelRouter && cfg.modelRouter.modelMapping;
    if (!mm || typeof mm !== 'object' || Object.keys(mm).length === 0) {
      return { status: 'warn', message: 'modelRouter.modelMapping is empty — interceptor will passthrough everything' };
    }
    return { status: 'pass', message: `modelMapping has ${Object.keys(mm).length} entry(ies)` };
  } catch (err) {
    return { status: 'fail', message: `t1k-config-mr.json invalid: ${err.message}` };
  }
});

// Capability-tag coverage (v2.1) — every enabled model in providers-config.json
// needs a `capabilities` array, else the rule-based selector silently drops it
// from every candidate list (because `requiredCaps.every(r => caps.includes(r))`
// is vacuously true on the empty set, but agents requiring vision/long-context/
// reasoning would never match a tag-less model).
check('capability-tags', () => {
  const resolved = resolveProvidersConfigPath();
  if (!resolved) return { status: 'warn', message: 'providers-config.json not found in project or global (~/.claude/)' };
  const cfgPath = resolved.path;
  const scopeTag = resolved.scope === 'global' ? ' (global)' : '';
  try {
    const cfg = JSON.parse(fs.readFileSync(cfgPath, 'utf8'));
    const untagged = [];
    for (const [pname, p] of Object.entries(cfg.providers || {})) {
      if (p.enabled !== true) continue;
      for (const [mname, m] of Object.entries(p.models || {})) {
        if (m.enabled !== true) continue;
        if (!Array.isArray(m.capabilities) || m.capabilities.length === 0) {
          untagged.push(`${pname}/${mname}`);
        }
      }
    }
    if (untagged.length > 0) {
      return { status: 'warn', message: `${untagged.length} enabled model(s) have no capabilities array${scopeTag}: ${untagged.slice(0, 3).join(', ')}${untagged.length > 3 ? '…' : ''}` };
    }
    return { status: 'pass', message: `all enabled models carry capability tags${scopeTag}` };
  } catch (err) {
    return { status: 'fail', message: `providers-config.json invalid${scopeTag}: ${err.message}` };
  }
});

// gh auth (for remote providers).
check('gh-auth', () => {
  try {
    const token = execSync('gh auth token', { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'], timeout: 5000 }).trim();
    if (token) return { status: 'pass', message: 'gh auth token available' };
    return { status: 'warn', message: 'gh auth token empty' };
  } catch {
    return { status: 'warn', message: 'gh not authenticated. Run: gh auth login (needed for kimi/codex/opencode-go providers)' };
  }
});

// Remote endpoint (kimi/codex auth proxy).
check('remote-endpoint', () => {
  try {
    const resp = execSync('curl -s --max-time 5 https://ccs.the1studio.org/health',
      { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'], timeout: 8000 });
    const data = JSON.parse(resp);
    if (data.status === 'ok') return { status: 'pass', message: 'ccs.the1studio.org reachable (remote providers available)' };
    return { status: 'warn', message: 'Remote endpoint responded but status not ok' };
  } catch {
    return { status: 'warn', message: 'ccs.the1studio.org unreachable (kimi/codex providers unavailable, opencode-go still works)' };
  }
});

// Kit freshness — is the locally installed kit at or above the latest
// release on GitHub? Stale installs are a common source of "why is the
// routing not behaving like the docs say?" — old hook code shipped before
// some fix landed. This check makes the answer one banner-line away.
//
// Cached for 12h so we don't hammer the GH API on every session start;
// gh CLI uses the user's existing auth, so no extra credentials needed.
// Fail-open: no gh / no network / no manifest → warn, never fail.
check('kit-freshness', () => {
  // 1. Local: read installed version from manifest. Global install ships
  //    .t1k-manifest.json under ~/.claude/modules/model-router/.
  const manifestPath = path.join(
    process.env.HOME || os.homedir(),
    '.claude', 'modules', 'model-router', '.t1k-manifest.json'
  );
  if (!fs.existsSync(manifestPath)) {
    return { status: 'warn', message: 'manifest not found — kit may not be globally installed (skip check)' };
  }
  let installedVer;
  try {
    installedVer = JSON.parse(fs.readFileSync(manifestPath, 'utf8')).version;
  } catch {
    return { status: 'warn', message: 'manifest unreadable (skip check)' };
  }
  if (!installedVer || typeof installedVer !== 'string') {
    return { status: 'warn', message: 'manifest has no .version field (skip check)' };
  }

  // 2. Remote: latest GitHub release tag. 12h cache file lives next to
  //    the provider-probe cache so the file layout stays predictable.
  const CACHE_PATH = path.join(process.env.HOME || os.homedir(), '.model-router', 'freshness-cache.json');
  const TTL_MS = 12 * 60 * 60 * 1000;
  let latestVer = null;
  try {
    const cache = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
    if (cache && cache.latest_version && cache.checked_at && (Date.now() - cache.checked_at < TTL_MS)) {
      latestVer = cache.latest_version;
    }
  } catch { /* cache miss; refresh below */ }

  if (!latestVer) {
    try {
      // `gh` reuses the user's existing auth. If gh is missing or unauth'd,
      // execSync throws → we warn and skip rather than fail the check.
      // Use /git/refs/tags (NOT /releases or /tags): the kit ships TWO tags
      // per release — `modules-YYYYMMDD-HHMM` (date-stamped, useless for
      // version compare) and `model-router@X.Y.Z` (the semver we want).
      // /releases shows only the date-stamped form because it's the release
      // tag; /tags is sorted by commit date and the semver tags share commits
      // with the date tags so they may not appear in the top page. Refs are
      // returned in lexicographic order — semver tags sort to the end.
      const refsJson = execSync(
        "gh api 'repos/The1Studio/theonekit-model-router/git/refs/tags' --jq '[.[].ref | select(test(\"model-router@\"))]'",
        { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'], timeout: 5000 }
      );
      const refs = JSON.parse(refsJson);
      // Compare every semver tag and keep the highest, NOT just the last
      // refs in lexicographic order ("model-router@3.10.0" sorts before
      // "model-router@3.9.0" lexicographically, breaking last-wins).
      let highest = null;
      for (const r of refs) {
        const m = String(r).match(/model-router@(\d+\.\d+\.\d+)$/);
        if (!m) continue;
        if (highest === null || compareSemver(m[1], highest) > 0) {
          highest = m[1];
        }
      }
      latestVer = highest;
      if (latestVer) {
        try {
          fs.mkdirSync(path.dirname(CACHE_PATH), { recursive: true });
          fs.writeFileSync(CACHE_PATH, JSON.stringify({
            latest_version: latestVer,
            checked_at: Date.now(),
            valid_until: new Date(Date.now() + TTL_MS).toISOString(),
          }));
        } catch { /* cache write fail-open */ }
      }
    } catch (err) {
      return {
        status: 'warn',
        message: `installed=${installedVer}, latest=unknown (gh API unreachable: ${String(err.message || err).split('\n')[0]})`,
      };
    }
  }

  if (!latestVer) {
    return { status: 'warn', message: `installed=${installedVer}, latest=unknown (no semver tag found in last 10 releases)` };
  }

  const cmp = compareSemver(installedVer, latestVer);
  if (cmp >= 0) {
    return { status: 'pass', message: `installed=${installedVer} (up to date with latest=${latestVer})` };
  }
  return {
    status: 'warn',
    message: `OUTDATED: installed=${installedVer} < latest=${latestVer}. Run: t1k init -g --kit model-router --refresh`,
  };
});

// Per-provider aliveness probes. The probe runs a real chat-completion
// (max_tokens=4) against each provider in providers-config.json so we catch
// the "endpoint up but upstream broken" case (e.g. codex returning 502
// while /providers reports it as authenticated — observed 2026-05-25).
//
// The probe script writes a `valid_until` ISO timestamp into the cache —
// that's the single source of truth for freshness. This hook just reads it.
(function probeProviders() {
  const CACHE_PATH = path.join(process.env.HOME || '', '.model-router', 'provider-probe-cache.json');

  let cache = null;
  try {
    if (fs.existsSync(CACHE_PATH)) {
      cache = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
      if (cache.valid_until && Date.now() > new Date(cache.valid_until).getTime()) {
        cache = null;  // stale → reprobe
      }
    }
  } catch { cache = null; }

  if (!cache) {
    const probeSh = path.join(process.cwd(), '.claude/scripts/mr-probe-providers.sh');
    if (fs.existsSync(probeSh)) {
      try {
        execSync(`bash "${probeSh}" --json`, { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'], timeout: 30000 });
        if (fs.existsSync(CACHE_PATH)) cache = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
      } catch { /* fail-open — leave cache null */ }
    }
  }

  if (!cache || !Array.isArray(cache.providers)) {
    results.push({ name: 'provider-aliveness', status: 'warn', message: 'provider probe cache unavailable — run: bash .claude/scripts/mr-probe-providers.sh' });
    return;
  }

  for (const p of cache.providers) {
    const name = `provider:${p.name}`;
    if (p.status === 'pass') {
      results.push({ name, status: 'pass', message: `${p.model} → 200 in ${p.latency_ms}ms` });
    } else if (p.status === 'skip') {
      results.push({ name, status: 'warn', message: p.reason });
    } else {
      // Dead provider is a 'warn' not 'fail' — the kit is fine; the upstream is broken.
      results.push({ name, status: 'warn', message: `DEAD — ${p.reason}. mr-delegate.sh will fail fast on --provider ${p.name}` });
    }
  }
})();

console.log(JSON.stringify({ kit: 'theonekit-model-router', checks: results }, null, 2));
process.exit(hasFailure ? 1 : 0);
