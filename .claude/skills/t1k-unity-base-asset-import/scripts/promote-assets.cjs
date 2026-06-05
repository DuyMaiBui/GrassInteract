#!/usr/bin/env node
// t1k-origin: kit=theonekit-unity | repo=The1Studio/theonekit-unity | module=base | protected=false
/**
 * promote-assets.cjs — plan / apply moves for triaged assets.
 *
 * Usage:
 *   node promote-assets.cjs --triage-json <path> --project-root <path> --game-name <name> [--apply] [--plan-out <path>]
 *
 * Reads the triage JSON from validate-naming.cjs. Computes destination paths
 * under <project-root>/Assets/_Game/<game-name>/<targetFolder>/<filename>.
 *
 * DRY-RUN (default): emit move-plan JSON with { source, destination, group, labels }.
 * APPLY (--apply): emit MCP invocation directives the calling skill body executes
 *                  (Node script does NOT directly invoke MCP — that's the AI
 *                  layer's responsibility per ai-driven-design.md).
 *
 * Idempotency: if destination exists, mark as TARGET-COLLISION and skip.
 *
 * Exit codes:
 *   0 — plan emitted (apply phase always 0; caller checks per-file results)
 *   1 — IO error
 *   2 — bad CLI args
 */

'use strict';

const fs = require('fs');
const path = require('path');

function parseArgs(argv) {
  const args = {
    triageJson: null,
    projectRoot: null,
    gameName: null,
    apply: false,
    planOut: null,
  };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--triage-json')      args.triageJson = argv[++i];
    else if (a === '--project-root') args.projectRoot = argv[++i];
    else if (a === '--game-name')    args.gameName = argv[++i];
    else if (a === '--apply')        args.apply = true;
    else if (a === '--plan-out')     args.planOut = argv[++i];
    else if (a === '--help' || a === '-h') {
      process.stdout.write('Usage: promote-assets.cjs --triage-json <path> --project-root <path> --game-name <name> [--apply] [--plan-out <path>]\n');
      process.exit(0);
    } else {
      process.stderr.write(`Unknown arg: ${a}\n`);
      process.exit(2);
    }
  }
  if (!args.triageJson || !args.projectRoot || !args.gameName) {
    process.stderr.write('ERROR: --triage-json, --project-root, --game-name are required\n');
    process.exit(2);
  }
  return args;
}

function buildDestination(projectRoot, gameName, targetFolder, source) {
  const filename = path.basename(source);
  const rel = path.join('Assets', '_Game', gameName, targetFolder, filename);
  return path.join(projectRoot, rel);
}

function main() {
  const args = parseArgs(process.argv);
  let triage;
  try {
    triage = JSON.parse(fs.readFileSync(args.triageJson, 'utf8'));
  } catch (err) {
    process.stderr.write(`ERROR reading triage-json: ${err.message}\n`);
    process.exit(1);
  }
  const entries = Array.isArray(triage) ? triage : triage.triaged;
  if (!Array.isArray(entries)) {
    process.stderr.write('ERROR: triage-json must contain a triaged array\n');
    process.exit(1);
  }

  const plan = [];
  for (const e of entries) {
    if (e.verdict === 'REJECTED-ERROR') {
      plan.push({
        source: e.source,
        action: 'leave-in-incoming',
        reason: e.reason,
      });
      continue;
    }
    if (!e.targetFolder) {
      plan.push({
        source: e.source,
        action: 'skip-no-folder',
        reason: ['no-folder-map'],
      });
      continue;
    }
    const destination = buildDestination(args.projectRoot, args.gameName, e.targetFolder, e.source);
    let action = 'move-and-label';
    let collision = false;
    if (fs.existsSync(destination)) {
      action = 'skip-collision';
      collision = true;
    }
    plan.push({
      source: e.source,
      destination,
      action,
      collision,
      group: e.group,
      labels: e.labels || [],
      mcp: collision ? null : [
        { tool: 'mcp__UnityMCP__manage_asset', params: { action: 'move', source: e.source, destination } },
        e.group
          ? { tool: 'mcp__UnityMCP__manage_addressables', params: { action: 'set_labels', path: destination, group: e.group, labels: e.labels || [] } }
          : null,
      ].filter(Boolean),
    });
  }

  const payload = {
    apply: args.apply,
    projectRoot: args.projectRoot,
    gameName: args.gameName,
    counts: {
      'move-and-label':       plan.filter((p) => p.action === 'move-and-label').length,
      'skip-collision':       plan.filter((p) => p.action === 'skip-collision').length,
      'skip-no-folder':       plan.filter((p) => p.action === 'skip-no-folder').length,
      'leave-in-incoming':    plan.filter((p) => p.action === 'leave-in-incoming').length,
    },
    plan,
  };
  const json = JSON.stringify(payload, null, 2);
  if (args.planOut) {
    fs.writeFileSync(args.planOut, json + '\n', 'utf8');
    process.stdout.write(`Plan emitted (${plan.length} entries) -> ${args.planOut}\n`);
  } else {
    process.stdout.write(json + '\n');
  }
  if (!args.apply) {
    process.stderr.write('NOTE: dry-run mode. Skill body must invoke MCP per plan.mcp[] when user confirms.\n');
  }
}

if (require.main === module) main();

module.exports = { buildDestination };
