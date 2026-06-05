#!/usr/bin/env node
// t1k-origin: kit=theonekit-unity | repo=The1Studio/theonekit-unity | module=base | protected=false
/**
 * validate-naming.cjs — apply manifest rules to a scanned file list.
 *
 * Usage:
 *   node validate-naming.cjs --files-json <path> --manifest <path> [--triage-out <path>]
 *
 * --manifest may be a merged manifest JSON. If you want kit+project merge,
 * caller is responsible for producing the merged input. The skill body uses
 * the inline mergeManifests() helper (exported below) when invoked
 * programmatically.
 *
 * Emits per-file triage JSON:
 *   { triaged: [{ source, verdict, reason[], type, targetFolder, group, labels, suggestRename? }] }
 *
 * Exit codes:
 *   0 — validation ran (regardless of verdicts)
 *   1 — IO error (file/manifest missing or unparseable)
 *   2 — bad CLI args
 */

'use strict';

const fs = require('fs');
const path = require('path');

// ---- Deep merge (kit defaults <- project override) -----------------------

function isPlainObject(v) {
  return v !== null && typeof v === 'object' && !Array.isArray(v);
}

function mergeManifests(base, override) {
  if (!isPlainObject(base)) return override;
  if (!isPlainObject(override)) return override;
  const out = { ...base };
  for (const key of Object.keys(override)) {
    const bv = base[key];
    const ov = override[key];
    if (isPlainObject(bv) && isPlainObject(ov)) {
      out[key] = mergeManifests(bv, ov);
    } else {
      out[key] = ov;
    }
  }
  return out;
}

// ---- Triage logic --------------------------------------------------------

const EXT_EXPECTED = {
  texture:                 new Set(['.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr']),
  'texture.atlas':         new Set(['.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr']),
  sprite:                  new Set(['.png', '.psd']),
  material:                new Set(['.mat']),
  'audio.sfx':             new Set(['.wav', '.ogg', '.mp3']),
  'audio.bgm':             new Set(['.wav', '.ogg', '.mp3']),
  vfx:                     new Set(['.vfx', '.prefab']),
  scriptableobject:        new Set(['.asset']),
  'animation.clip':        new Set(['.anim', '.fbx']),
  'animation.controller':  new Set(['.controller']),
  shader:                  new Set(['.shader', '.shadergraph', '.hlsl']),
  prefab:                  new Set(['.prefab']),
  'prefab.camera':         new Set(['.prefab']),
};

function detectPrefix(name, prefixes) {
  // Longest-match-wins so "SFX_" beats "S_"
  const keys = Object.keys(prefixes).sort((a, b) => b.length - a.length);
  for (const k of keys) {
    if (name.startsWith(k)) return k;
  }
  return null;
}

function sizeVerdict(type, sizeBytes, budgets) {
  const b = budgets[type];
  if (!b) return { level: 'ok' };
  if (typeof b.errBytes === 'number' && sizeBytes > b.errBytes) {
    return { level: 'err', reason: 'size-err' };
  }
  if (typeof b.warnBytes === 'number' && sizeBytes > b.warnBytes) {
    return { level: 'warn', reason: 'size-warn' };
  }
  return { level: 'ok' };
}

function resolveRealm(name, addressables) {
  const cfg = addressables && addressables.realmAutoDetect;
  if (!cfg || !cfg.pattern || !cfg.map) return null;
  const re = new RegExp(cfg.pattern);
  const m = name.match(re);
  if (!m) return null;
  const tag = m[1];
  return cfg.map[tag] || null;
}

function resolveLabels(rawLabels, realmLabel) {
  if (!Array.isArray(rawLabels)) return [];
  const out = [];
  for (const l of rawLabels) {
    if (l === 'realm:auto') {
      if (realmLabel) out.push(realmLabel);
      // else drop the token
    } else {
      out.push(l);
    }
  }
  return out;
}

function triageOne(file, manifest) {
  const { name, ext, sizeBytes } = file;
  const prefixes = manifest.prefixes || {};
  const budgets = manifest.budgets || {};
  const folderMap = manifest.folderMap || {};
  const addressables = manifest.addressables || {};
  const triageCfg = manifest.triage || {};

  const result = {
    source: file.path,
    verdict: 'ACCEPTED-CLEAN',
    reason: [],
    type: null,
    targetFolder: null,
    group: null,
    labels: [],
  };

  // 1. Prefix detection
  const prefix = detectPrefix(name, prefixes);
  if (!prefix) {
    result.verdict = 'REJECTED-ERROR';
    result.reason.push('no-prefix');
    result.suggestRename = true;
    return result;
  }
  const cfg = prefixes[prefix];
  result.type = cfg.type;

  // 2. Regex
  if (cfg.regex === null) {
    result.verdict = 'REJECTED-ERROR';
    result.reason.push('prefix-disabled');
    return result;
  }
  if (cfg.regex) {
    const re = new RegExp(cfg.regex);
    if (!re.test(name)) {
      result.verdict = 'REJECTED-ERROR';
      result.reason.push('regex-mismatch');
      result.suggestRename = true;
      return result;
    }
  }

  // 3. Size budget
  const sv = sizeVerdict(cfg.type, sizeBytes, budgets);
  if (sv.level === 'err') {
    if (triageCfg.rejectOnErrCap) {
      result.verdict = 'REJECTED-ERROR';
      result.reason.push(sv.reason);
      return result;
    }
    result.verdict = 'ACCEPTED-WITH-WARNING';
    result.reason.push('size-err-downgraded');
  } else if (sv.level === 'warn') {
    result.verdict = 'ACCEPTED-WITH-WARNING';
    result.reason.push(sv.reason);
  }

  // 4. Target folder
  const tf = cfg.targetFolder;
  result.targetFolder = tf;
  const fmEntry = tf ? folderMap[tf] : null;
  if (!fmEntry) {
    if (result.verdict === 'ACCEPTED-CLEAN') result.verdict = 'ACCEPTED-WITH-WARNING';
    result.reason.push('no-folder-map');
  } else {
    result.group = fmEntry.group || null;

    // 5. Realm auto-detect
    const realmLabel = resolveRealm(name, addressables);
    result.labels = resolveLabels(fmEntry.labels, realmLabel);
    const askedAuto = (fmEntry.labels || []).includes('realm:auto');
    if (askedAuto && !realmLabel) {
      if (result.verdict === 'ACCEPTED-CLEAN') result.verdict = 'ACCEPTED-WITH-WARNING';
      result.reason.push('no-realm-tag');
    }
  }

  // 6. Extension sanity
  const expected = EXT_EXPECTED[cfg.type];
  if (expected && !expected.has(ext)) {
    if (triageCfg.warnOnUnknownExtension) {
      if (result.verdict === 'ACCEPTED-CLEAN') result.verdict = 'ACCEPTED-WITH-WARNING';
      result.reason.push('unknown-ext');
    } else {
      result.verdict = 'REJECTED-ERROR';
      result.reason.push('unknown-ext');
    }
  }

  return result;
}

// ---- CLI -----------------------------------------------------------------

function parseArgs(argv) {
  const args = { filesJson: null, manifest: null, triageOut: null };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--files-json') args.filesJson = argv[++i];
    else if (a === '--manifest') args.manifest = argv[++i];
    else if (a === '--triage-out') args.triageOut = argv[++i];
    else if (a === '--help' || a === '-h') {
      process.stdout.write('Usage: validate-naming.cjs --files-json <path> --manifest <path> [--triage-out <path>]\n');
      process.exit(0);
    } else {
      process.stderr.write(`Unknown arg: ${a}\n`);
      process.exit(2);
    }
  }
  if (!args.filesJson || !args.manifest) {
    process.stderr.write('ERROR: --files-json and --manifest are required\n');
    process.exit(2);
  }
  return args;
}

function main() {
  const args = parseArgs(process.argv);
  let filesPayload;
  let manifest;
  try {
    filesPayload = JSON.parse(fs.readFileSync(args.filesJson, 'utf8'));
  } catch (err) {
    process.stderr.write(`ERROR reading files-json: ${err.message}\n`);
    process.exit(1);
  }
  try {
    manifest = JSON.parse(fs.readFileSync(args.manifest, 'utf8'));
  } catch (err) {
    process.stderr.write(`ERROR reading manifest: ${err.message}\n`);
    process.exit(1);
  }
  const files = Array.isArray(filesPayload) ? filesPayload : filesPayload.files;
  if (!Array.isArray(files)) {
    process.stderr.write('ERROR: files-json must be an array or { files: [...] }\n');
    process.exit(1);
  }
  const triaged = files.map((f) => triageOne(f, manifest));
  const payload = {
    counts: {
      'ACCEPTED-CLEAN':         triaged.filter((t) => t.verdict === 'ACCEPTED-CLEAN').length,
      'ACCEPTED-WITH-WARNING':  triaged.filter((t) => t.verdict === 'ACCEPTED-WITH-WARNING').length,
      'REJECTED-ERROR':         triaged.filter((t) => t.verdict === 'REJECTED-ERROR').length,
    },
    triaged,
  };
  const json = JSON.stringify(payload, null, 2);
  if (args.triageOut) {
    fs.writeFileSync(args.triageOut, json + '\n', 'utf8');
    process.stdout.write(`Triaged ${triaged.length} file(s) -> ${args.triageOut}\n`);
  } else {
    process.stdout.write(json + '\n');
  }
}

if (require.main === module) main();

module.exports = { mergeManifests, triageOne, detectPrefix, resolveRealm, resolveLabels };
