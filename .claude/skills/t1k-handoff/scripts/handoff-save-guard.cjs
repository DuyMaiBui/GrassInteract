#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=t1k-extended | protected=true
// handoff-save-guard.cjs — Deterministic mkdir-p + post-write existence guard for `t1k:handoff save`.
//
// The save workflow's parent-dir creation (SKILL.md step 5) and post-write verification
// (step 7) were prose-only and got skipped in practice (#536, #527 — false "saved" claims
// with no file on disk). This script makes both deterministic so the AI cannot skip them.
//
// Usage:
//   node handoff-save-guard.cjs prepare <save-path> [<fallback-path>]
//       Creates the parent dir of <save-path> (mkdir -p). On success prints the path that
//       is now writable. If the parent cannot be created AND a <fallback-path> is given,
//       it creates the fallback's parent instead and prints the fallback path (so the
//       caller writes there). Exit 0 = a writable path was printed to stdout; exit 2 = no
//       writable location (caller must surface the failure, NOT report "saved").
//
//   node handoff-save-guard.cjs verify <save-path>
//       Asserts <save-path> exists and is a non-empty regular file AFTER the Write.
//       Exit 0 = file present (safe to report "saved to <save-path>").
//       Exit 2 = file missing/empty — caller MUST NOT report success; retry on fallback.
//
// Output is single-line, machine-parseable: `OK <path>` / `FAIL <reason>`.
'use strict';

const fs = require('fs');
const path = require('path');

function fail(reason) {
  process.stdout.write(`FAIL ${reason}\n`);
  process.exit(2);
}

function ok(payload) {
  process.stdout.write(`OK ${payload}\n`);
  process.exit(0);
}

function mkdirpFor(filePath) {
  const dir = path.dirname(path.resolve(filePath));
  fs.mkdirSync(dir, { recursive: true });
  // Confirm the dir is real + writable (mkdirSync is silent on pre-existing).
  fs.accessSync(dir, fs.constants.W_OK);
  return dir;
}

const [, , action, target, fallback] = process.argv;

if (!action || !target) {
  fail('usage: handoff-save-guard.cjs <prepare|verify> <save-path> [<fallback-path>]');
}

if (action === 'prepare') {
  try {
    mkdirpFor(target);
    ok(path.resolve(target));
  } catch (primaryErr) {
    if (!fallback) {
      fail(`cannot-create-parent-dir-of "${target}": ${primaryErr.message} (no fallback given)`);
    }
    try {
      mkdirpFor(fallback);
      // Signal the caller to write to the fallback instead of the primary.
      ok(path.resolve(fallback));
    } catch (fallbackErr) {
      fail(`cannot-create-parent-dir for primary "${target}" (${primaryErr.message}) NOR fallback "${fallback}" (${fallbackErr.message})`);
    }
  }
} else if (action === 'verify') {
  const resolved = path.resolve(target);
  let st;
  try {
    st = fs.statSync(resolved);
  } catch {
    fail(`file-not-found "${resolved}" — Write claimed success but no file on disk; do NOT report "saved"`);
  }
  if (!st.isFile()) {
    fail(`not-a-regular-file "${resolved}"`);
  }
  if (st.size === 0) {
    fail(`empty-file "${resolved}" — 0 bytes written`);
  }
  ok(resolved);
} else {
  fail(`unknown-action "${action}" (expected prepare|verify)`);
}
