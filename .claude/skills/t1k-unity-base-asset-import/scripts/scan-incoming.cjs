#!/usr/bin/env node
// t1k-origin: kit=theonekit-unity | repo=The1Studio/theonekit-unity | module=base | protected=false
/**
 * scan-incoming.cjs — list files under <incomingRoot> for the asset-import skill.
 *
 * Usage:
 *   node scan-incoming.cjs --incoming-root <path> [--json-out <path>]
 *
 * Emits to stdout (or --json-out file):
 *   { incomingRoot, fileCount, files: [{ path, name, ext, sizeBytes }] }
 *
 * Skips: *.meta, hidden dotfiles, directories.
 *
 * Exit codes:
 *   0 — scan ok (even if zero files)
 *   1 — incomingRoot missing or unreadable
 *   2 — bad CLI args
 */

'use strict';

const fs = require('fs');
const path = require('path');

function parseArgs(argv) {
  const args = { incomingRoot: null, jsonOut: null };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--incoming-root') args.incomingRoot = argv[++i];
    else if (a === '--json-out') args.jsonOut = argv[++i];
    else if (a === '--help' || a === '-h') {
      process.stdout.write('Usage: scan-incoming.cjs --incoming-root <path> [--json-out <path>]\n');
      process.exit(0);
    } else {
      process.stderr.write(`Unknown arg: ${a}\n`);
      process.exit(2);
    }
  }
  if (!args.incomingRoot) {
    process.stderr.write('ERROR: --incoming-root is required\n');
    process.exit(2);
  }
  return args;
}

function walk(root) {
  const out = [];
  const stack = [root];
  while (stack.length > 0) {
    const dir = stack.pop();
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (err) {
      throw new Error(`Cannot read directory ${dir}: ${err.message}`);
    }
    for (const ent of entries) {
      const full = path.join(dir, ent.name);
      if (ent.name.startsWith('.')) continue;
      if (ent.isDirectory()) {
        stack.push(full);
        continue;
      }
      if (!ent.isFile()) continue;
      if (ent.name.endsWith('.meta')) continue;
      let st;
      try {
        st = fs.statSync(full);
      } catch {
        continue;
      }
      const ext = path.extname(ent.name);
      const name = path.basename(ent.name, ext);
      out.push({
        path: full,
        name,
        ext: ext.toLowerCase(),
        sizeBytes: st.size,
      });
    }
  }
  return out;
}

function main() {
  const args = parseArgs(process.argv);
  if (!fs.existsSync(args.incomingRoot)) {
    process.stderr.write(`ERROR: incomingRoot does not exist: ${args.incomingRoot}\n`);
    process.exit(1);
  }
  const files = walk(args.incomingRoot);
  const payload = {
    incomingRoot: args.incomingRoot,
    fileCount: files.length,
    files,
  };
  const json = JSON.stringify(payload, null, 2);
  if (args.jsonOut) {
    fs.writeFileSync(args.jsonOut, json + '\n', 'utf8');
    process.stdout.write(`Scanned ${files.length} file(s) -> ${args.jsonOut}\n`);
  } else {
    process.stdout.write(json + '\n');
  }
}

main();
