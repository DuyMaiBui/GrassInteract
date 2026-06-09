#!/usr/bin/env node
// t1k-origin: kit=theonekit-designer | repo=The1Studio/theonekit-designer | module=base | protected=false
// List templates shipped by the design-diagram-authoring skill.
// Output: JSON array of { name, path, description } — one entry per .mmd template.
// Used by /t1k:find-skill and doctor integrity checks.

'use strict';

const path = require('path');
const fs = require('fs');

const TEMPLATE_DIR = path.join(__dirname, '..', 'templates');

const DESCRIPTIONS = {
  'quest-tree.mmd':       'Branching quest progression with prerequisites (flowchart TD)',
  'economy-flow.mmd':     'Currency faucets, conversions, and sinks (flowchart LR)',
  'narrative-branch.mmd': 'Character relationships + branching narrative lanes (classDiagram)',
  'level-spatial.mmd':    'Level zones connected by two-way/one-way/gated edges (flowchart LR)',
};

function listTemplates() {
  if (!fs.existsSync(TEMPLATE_DIR)) {
    return [];
  }
  const files = fs.readdirSync(TEMPLATE_DIR).filter((f) => f.endsWith('.mmd')).sort();
  return files.map((name) => ({
    name,
    path: path.join('templates', name),
    description: DESCRIPTIONS[name] || '(undocumented template)',
  }));
}

if (require.main === module) {
  const templates = listTemplates();
  process.stdout.write(JSON.stringify(templates, null, 2) + '\n');
}

module.exports = { listTemplates };
