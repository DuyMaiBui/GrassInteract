#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
/**
 * mr-transparent-routing-reminder.cjs — SessionStart hook
 *
 * Emits a multi-line dashboard when transparent routing is active.
 * Provides at-a-glance presence so consumers know the kit is operating
 * without having to grep logs (user feedback 2026-05-28).
 *
 * Includes:
 *   • Status line (kit version + mode + agent-count)
 *   • Last 24h delegation count + success rate + models used
 *   • Provider health (from probe cache)
 *   • Delegation Bias rule reminder for inline edits
 *   • Per-spawn banner format the AI should expect
 *
 * Exit codes:
 *   0 = always (no-op if transparent routing is off)
 *
 * Fail-open: any read error → falls back to minimal reminder.
 */

'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');

function tryReadJson(p) {
  try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return null; }
}

function tryReadJsonl(p, maxLines) {
  try {
    const text = fs.readFileSync(p, 'utf8');
    const lines = text.trim().split('\n').slice(-maxLines);
    return lines.map(l => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
  } catch { return []; }
}

function get24hCutoff() {
  return new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
}

function readKitVersion(projectDir) {
  // Try the module.json shipped with the kit.
  const candidates = [
    path.join(os.homedir(), '.claude', 'modules', 'model-router', 'module.json'),
    path.join(projectDir, '.claude', 'modules', 'model-router', 'module.json'),
  ];
  for (const p of candidates) {
    const j = tryReadJson(p);
    if (j && j.version) return j.version;
  }
  return null;
}

function loadDelegationStats(cutoff) {
  // calls.jsonl is the canonical lifecycle log. Read up to 5000 recent lines.
  const calls = tryReadJsonl(path.join(os.homedir(), '.model-router', 'calls.jsonl'), 5000);
  const recent = calls.filter(c => c && c.status === 'done' && c.ts && c.ts >= cutoff);
  const ok = recent.filter(c => c.exit === 0);
  const byModel = {};
  for (const c of recent) {
    const k = c.model || '?';
    byModel[k] = (byModel[k] || 0) + 1;
  }
  return { total: recent.length, ok: ok.length, byModel };
}

function loadProviderHealth() {
  const cache = tryReadJson(path.join(os.homedir(), '.model-router', 'provider-probe-cache.json'));
  if (!cache || !Array.isArray(cache.providers)) return null;
  const parts = cache.providers.map(p => `${p.name}${p.status === 'pass' ? '✓' : '✗'}`);
  return parts.join(' · ');
}

function countAgents(projectDir) {
  // Where does the interceptor look for agents? Same search dirs.
  const dirs = [
    path.join(projectDir, '.claude', 'agents'),
    path.join(os.homedir(), '.claude', 'agents'),
  ];
  let total = 0;
  const seen = new Set();
  for (const d of dirs) {
    try {
      for (const f of fs.readdirSync(d)) {
        if (!f.endsWith('.md')) continue;
        if (seen.has(f)) continue;
        seen.add(f);
        total += 1;
      }
    } catch { /* dir may not exist */ }
  }
  return total;
}

try {
  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();

  // Honor the project's config gate — same logic as before.
  const configPath = path.join(projectDir, '.claude', 't1k-config-mr.json');
  const globalConfigPath = path.join(os.homedir(), '.claude', 't1k-config-mr.json');
  const config = tryReadJson(configPath) || tryReadJson(globalConfigPath);
  const mr = config && config.modelRouter;
  if (!mr || mr.enabled !== true || mr.mode !== 'transparent') {
    process.exit(0);
  }

  const version = readKitVersion(projectDir);
  const stats = loadDelegationStats(get24hCutoff());
  const health = loadProviderHealth();
  const agentCount = countAgents(projectDir);

  const versionStr = version ? `v${version}` : 'enabled';
  const succRate = stats.total > 0 ? Math.round((stats.ok / stats.total) * 100) : null;
  const modelsStr = Object.entries(stats.byModel)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 4)
    .map(([m, n]) => `${m}×${n}`)
    .join(' ');

  // Banner format the interceptor now uses (the AI should look for this).
  const sampleBanner = '[t1k:mr] ✓ <agent>[<model>] — <provider> (<selector>)';

  const lines = [
    `[t1k:model-router] ${versionStr} · transparent routing ACTIVE · ${agentCount} agents in scope`,
    stats.total > 0
      ? `  ├─ Last 24h: ${stats.total} delegations · ${succRate}% success · models: ${modelsStr || '(none)'}`
      : `  ├─ Last 24h: 0 delegations (kit is idle but ready)`,
    health
      ? `  ├─ Providers: ${health}`
      : `  ├─ Providers: (no recent probe — run \`bash .claude/scripts/mr-probe-providers.sh\`)`,
    `  ├─ Per-Task banner: ${sampleBanner}`,
    `  └─ Delegation Bias (rules/mr-transparent-routing.md): mechanical/boilerplate work → Task tool; multi-file/judgment → inline.`,
    `     Diagnose any session: \`bash .claude/scripts/mr-tail-debug.sh\` — full playbook: wiki/Diagnostic-Playbook.`,
  ];

  process.stdout.write(lines.join('\n') + '\n');
  process.exit(0);
} catch {
  // Fail-open: emit the minimal reminder as a last resort.
  try {
    process.stdout.write('[t1k:transparent-routing] ACTIVE — Delegation Bias enforced. See rules/mr-transparent-routing.md.\n');
  } catch { /* ignore */ }
  process.exit(0);
}
