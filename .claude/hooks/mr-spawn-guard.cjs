#!/usr/bin/env node
// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
// mr-spawn-guard.cjs — P0 safety hook
// Prevents delegated sessions from re-entering model-router delegation.
// Registered as PreToolUse hook matching "Bash" in spawned sessions.
//
// When MR_SPAWNED=1 (set by mr-delegate.sh), this hook blocks any
// Bash command that invokes mr-delegate.sh, preventing recursive delegation.

// Only block in spawned sessions (MR_SPAWNED=1 set by mr-delegate.sh)
if (process.env.MR_SPAWNED !== '1') {
  process.exit(0);
}

const input = JSON.parse(require('fs').readFileSync(0, 'utf8'));

// Only check Bash tool calls
if (input.tool_name !== 'Bash') {
  process.exit(0);
}

const command = input.tool_input?.command || '';

// Best-effort guard (can be bypassed via obfuscation).
// Authoritative guard is MR_SPAWNED=1 check in mr-delegate.sh itself.
if (command.includes('mr-delegate') || command.includes('/t1k:model-router-delegate') || command.includes('claude -p')) {
  process.stderr.write('Blocked: recursive delegation not allowed in spawned session\n');
  process.exit(2); // exit 2 = block operation
}

process.exit(0);
