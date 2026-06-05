#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
// project-detector.cjs — SessionStart hook: auto-detect project type and framework
'use strict';

if (process.env.T1K_DEBUG_DETECTOR === '1') {
  console.error('[t1k:detector-debug] starting');
}

/**
 * Pick the most relevant kit name from metadata, skipping phantom entries.
 * A phantom entry is one where `files` is undefined or an empty array —
 * written by `t1k init` when it was interrupted before file extraction.
 * @param {object} meta - Parsed .claude/metadata.json
 * @returns {string} kit name or 'core' as fallback
 */
function pickFramework(meta) {
  if (meta.kitName) {
    if (process.env.T1K_DEBUG_DETECTOR === '1') {
      console.error('[t1k:detector-debug] using meta.kitName:', meta.kitName);
    }
    return meta.kitName;
  }
  const kits = meta.kits || {};
  // Filter out phantom entries: files must be a non-empty array
  const populated = Object.entries(kits)
    .filter(([, k]) => k && Array.isArray(k.files) && k.files.length > 0)
    .sort((a, b) => (b[1].installedAt || '1970-01-01').localeCompare(a[1].installedAt || '1970-01-01'));
  if (process.env.T1K_DEBUG_DETECTOR === '1') {
    const allKeys = Object.keys(kits);
    const populatedKeys = populated.map(([n]) => n);
    const phantomKeys = allKeys.filter(k => !populatedKeys.includes(k));
    console.error('[t1k:detector-debug] all kits:', allKeys, '| populated:', populatedKeys, '| phantoms:', phantomKeys);
  }
  if (populated.length > 0) {
    if (process.env.T1K_DEBUG_DETECTOR === '1') {
      console.error('[t1k:detector-debug] selected populated kit:', populated[0][0]);
    }
    return populated[0][0];
  }
  const fallback = meta.name || 'core';
  if (process.env.T1K_DEBUG_DETECTOR === '1') {
    console.error('[t1k:detector-debug] no populated kits, fallback to:', fallback);
  }
  return fallback;
}

try {
  const fs = require('fs');
  const path = require('path');
  const cwd = process.cwd();

  function exists(p) { return fs.existsSync(path.join(cwd, p)); }
  function readJson(p) { try { return JSON.parse(fs.readFileSync(path.join(cwd, p), 'utf8')); } catch { return null; } }

  let projectType = 'unknown', framework = '', packageManager = '';

  // TheOneKit project
  if (exists('.claude/metadata.json')) {
    const meta = readJson('.claude/metadata.json');
    if (meta) {
      projectType = 'theonekit';
      // Detect framework from multiple metadata formats, skipping phantom kit entries
      framework = pickFramework(meta);
    }
  }
  // Unity
  else if (exists('Assets') && exists('ProjectSettings')) { projectType = 'unity'; framework = 'Unity'; }
  // Cocos
  else if (exists('assets') && (exists('project.json') || exists('settings/project.json'))) { projectType = 'cocos'; framework = 'Cocos Creator'; }
  // React Native
  else if (exists('app.json') && (exists('metro.config.js') || exists('metro.config.cjs'))) { projectType = 'react-native'; framework = exists('.expo') ? 'Expo' : 'React Native CLI'; }
  // Node.js
  else if (exists('package.json')) {
    projectType = 'node';
    const pkg = readJson('package.json');
    if (pkg) {
      const deps = { ...pkg.dependencies, ...pkg.devDependencies };
      if (deps['next']) framework = 'Next.js';
      else if (deps['nuxt']) framework = 'Nuxt';
      else if (deps['@nestjs/core']) framework = 'NestJS';
      else if (deps['express']) framework = 'Express';
      else if (deps['react']) framework = 'React';
      else if (deps['vue']) framework = 'Vue';
    }
    packageManager = exists('pnpm-lock.yaml') ? 'pnpm' : exists('yarn.lock') ? 'yarn' : 'npm';
  }
  // Python
  else if (exists('pyproject.toml') || exists('requirements.txt')) {
    projectType = 'python';
    if (exists('manage.py')) framework = 'Django';
  }
  // Go
  else if (exists('go.mod')) { projectType = 'go'; }
  // Rust
  else if (exists('Cargo.toml')) { projectType = 'rust'; }
  // .NET
  else if (fs.readdirSync(cwd).some(f => f.endsWith('.csproj') || f.endsWith('.sln'))) { projectType = 'dotnet'; }
  // Docker
  else if (exists('Dockerfile') || exists('docker-compose.yml')) { projectType = 'containerized'; }
  // Configuration-only (like theonekit-core without metadata)
  else if (exists('.claude') && !exists('src') && !exists('package.json')) { projectType = 'configuration'; }

  const parts = [`[project-type] ${projectType}`];
  if (framework) parts.push(`[framework] ${framework}`);
  if (packageManager) parts.push(`[package-manager] ${packageManager}`);
  console.log(parts.join(' | '));

  process.exit(0);
} catch (e) {
  process.exit(0); // fail-open
}
