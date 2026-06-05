#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
// t1k-hook-dry-run-timeout: 30000
// check-mcp-health.cjs — SessionStart hook: validate required/recommended MCP servers
// Reads t1k-config-*.json → mcp section, checks against `claude mcp list` output.
'use strict';
try {
  const fs = require('fs');
  const path = require('path');
  const { execSync } = require('child_process');
  const cwd = process.cwd();
  const { T1K, resolveClaudeDir } = require('./telemetry-utils.cjs');
  const { logHook, createHookTimer, logHookCrash } = require('./hook-logger.cjs');
  const resolved = resolveClaudeDir();
  if (!resolved) process.exit(0);
  const { claudeDir, home } = resolved;
  const timer = createHookTimer('check-mcp-health');

  // ── Collect MCP requirements from all config fragments ──
  const required = [];
  const recommended = [];
  const optional = [];
  try {
    for (const f of fs.readdirSync(claudeDir).filter(f => f.startsWith(T1K.CONFIG_PREFIX) && f.endsWith('.json'))) {
      try {
        const config = JSON.parse(fs.readFileSync(path.join(claudeDir, f), 'utf8'));
        if (!config.mcp) continue;
        if (Array.isArray(config.mcp.required)) {
          for (const entry of config.mcp.required) required.push(entry);
        }
        if (Array.isArray(config.mcp.recommended)) {
          for (const entry of config.mcp.recommended) recommended.push(entry);
        }
        if (Array.isArray(config.mcp.optional)) {
          for (const entry of config.mcp.optional) optional.push(entry);
        }
      } catch { /* skip malformed */ }
    }
  } catch { /* ok */ }

  if (required.length === 0 && recommended.length === 0 && optional.length === 0) process.exit(0);

  // ── Deduplicate by name ──
  const dedup = (arr) => {
    const seen = new Set();
    return arr.filter(e => { if (seen.has(e.name)) return false; seen.add(e.name); return true; });
  };
  const reqList = dedup(required);
  const recList = dedup(recommended);
  const optList = dedup(optional);

  // ── Get connected MCP servers ──
  let connectedServers = new Set();
  // Map name(lower) → full raw line so fork checks can inspect the command/URL
  let serverLines = new Map();
  try {
    const output = execSync('claude mcp list', {
      encoding: 'utf8',
      timeout: 10000,
      stdio: ['pipe', 'pipe', 'ignore'],
    });
    // Parse output lines: "name: command/url - status"
    for (const line of output.split('\n')) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      // Match pattern: "server-name: ..." or just extract first word/token before ":"
      const colonIdx = trimmed.indexOf(':');
      if (colonIdx > 0) {
        const name = trimmed.substring(0, colonIdx).trim();
        // Check if connected (contains "Connected" or does NOT contain "Needs authentication" or "Error")
        const isConnected = trimmed.includes('Connected') || (!trimmed.includes('Needs authentication') && !trimmed.includes('Error'));
        if (isConnected) connectedServers.add(name.toLowerCase());
        serverLines.set(name.toLowerCase(), trimmed);
      }
    }
  } catch {
    // If claude mcp list fails, skip MCP health check silently
    process.exit(0);
  }

  // ── Fork-required check (reads consumer's Packages/manifest.json) ──
  // For entries that declare `fork.required: true`, verify both halves of the
  // install: (1) the registered MCP server command line contains the expected
  // fork marker, and (2) the Unity package URL in Packages/manifest.json
  // resolves to the fork. Pure data-driven — no kit-specific strings here.
  function readManifestDeps() {
    try {
      const manifestPath = path.join(cwd, 'Packages', 'manifest.json');
      if (!fs.existsSync(manifestPath)) return null;
      const m = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
      return (m && typeof m === 'object' && m.dependencies && typeof m.dependencies === 'object') ? m.dependencies : null;
    } catch { return null; }
  }
  function checkForkInstall(entry) {
    const fk = entry && entry.fork;
    if (!fk || fk.required !== true) return null; // not fork-gated
    const checks = (fk.checks && typeof fk.checks === 'object') ? fk.checks : {};
    const result = { manifestOk: null, serverCmdOk: null, missingHints: [] };

    // Half 1 — Unity-side UPM package
    if (fk.unityPackageId && checks.manifestUrlContains) {
      const deps = readManifestDeps();
      if (deps === null) {
        // Not a Unity project (no Packages/manifest.json) — skip silently; the
        // [t1k:mcp] line for the server below is the actionable signal.
        result.manifestOk = 'not-unity-project';
      } else {
        const url = typeof deps[fk.unityPackageId] === 'string' ? deps[fk.unityPackageId] : '';
        result.manifestOk = url.includes(checks.manifestUrlContains);
        if (!result.manifestOk) result.missingHints.push('upm');
      }
    }

    // Half 2 — registered MCP server command line
    if (checks.mcpServerCmdContains) {
      const line = serverLines.get(entry.name.toLowerCase()) || '';
      result.serverCmdOk = line.includes(checks.mcpServerCmdContains);
      if (!result.serverCmdOk) result.missingHints.push('server');
    }
    return result;
  }

  // ── Also check global MCP config files as fallback ──
  const mcpConfigPaths = [
    path.join(home || '', '.claude', 'mcp.json'),
    path.join(cwd, '.mcp.json'),
  ];
  for (const mcpPath of mcpConfigPaths) {
    try {
      const mcpConfig = JSON.parse(fs.readFileSync(mcpPath, 'utf8'));
      if (mcpConfig.mcpServers) {
        for (const name of Object.keys(mcpConfig.mcpServers)) {
          connectedServers.add(name.toLowerCase());
        }
      }
    } catch { /* ok */ }
  }

  // ── Emit unified [t1k:mcp] tags ──
  // Escape user-config-supplied strings before interpolation into a
  // newline-delimited tag stream. Tags are parsed by AI on attribute
  // boundaries; a stray `"` in installCmd or purpose would corrupt the
  // stream. This is fail-open: never throw on a bad fragment.
  function safeAttr(v) {
    if (v == null) return '';
    return String(v).replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\r?\n/g, ' ');
  }
  const lines = [];
  for (const entry of reqList) {
    const fork = checkForkInstall(entry);
    const serverConnected = connectedServers.has(entry.name.toLowerCase());
    if (!serverConnected) {
      lines.push(`[t1k:mcp] action=install tier=required name="${safeAttr(entry.name)}" purpose="${safeAttr(entry.purpose)}" cmd="${safeAttr(entry.installCmd)}"`);
      continue;
    }
    // Server is registered. If a fork is required, verify both halves.
    if (fork && (fork.manifestOk === false || fork.serverCmdOk === false)) {
      const help = Array.isArray(entry.fork && entry.fork.installHelp) ? entry.fork.installHelp.join(' || ') : '';
      lines.push(
        `[t1k:mcp] action=install-fork tier=required name="${safeAttr(entry.name)}" ` +
        `purpose="${safeAttr(entry.purpose)}" ` +
        `repo="${safeAttr(entry.fork.repo)}" branch="${safeAttr(entry.fork.branch)}" ` +
        `missing="${safeAttr(fork.missingHints.join(','))}" ` +
        `help="${safeAttr(help)}"`
      );
    } else {
      lines.push(`[t1k:mcp] action=ok tier=required name="${safeAttr(entry.name)}"`);
    }
  }
  for (const entry of recList) {
    if (connectedServers.has(entry.name.toLowerCase())) {
      lines.push(`[t1k:mcp] action=ok tier=recommended name="${safeAttr(entry.name)}"`);
    } else {
      lines.push(`[t1k:mcp] action=install tier=recommended name="${safeAttr(entry.name)}" purpose="${safeAttr(entry.purpose)}" cmd="${safeAttr(entry.installCmd)}"`);
    }
  }
  for (const entry of optList) {
    if (connectedServers.has(entry.name.toLowerCase())) {
      lines.push(`[t1k:mcp] action=ok tier=optional name="${safeAttr(entry.name)}"`);
    } else {
      lines.push(`[t1k:mcp] action=install tier=optional name="${safeAttr(entry.name)}" purpose="${safeAttr(entry.purpose)}" cmd="${safeAttr(entry.installCmd)}"`);
    }
  }

  // Count suggestions for telemetry. install-fork is a stricter variant of
  // install — counted separately so dashboards can track fork-migration drift.
  const suggested = lines.filter(l => l.includes('action=install') || l.includes('action=install-fork')).length;
  for (const line of lines) {
    const m = line.match(/action=(install(?:-fork)?)/);
    if (!m) continue;
    const nameMatch = line.match(/name="([^"]+)"/);
    const tierMatch = line.match(/tier=(\w+)/);
    if (nameMatch) {
      logHook('check-mcp-health', { suggest: nameMatch[1], tier: tierMatch ? tierMatch[1] : 'unknown', action: m[1] });
    }
  }

  if (lines.length > 0) {
    console.log(lines.join('\n'));
  }

  timer.end({ outcome: 'ok', suggested: suggested });
  process.exit(0);
} catch (e) {
  // fail-open: never block session start
  try {
    const { logHookCrash: _lhc } = require('./hook-logger.cjs');
    _lhc('check-mcp-health', e);
  } catch { /* truly fail-open */ }
  process.exit(0);
}
