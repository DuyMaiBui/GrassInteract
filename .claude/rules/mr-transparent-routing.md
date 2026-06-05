---
origin: theonekit-model-router
repository: The1Studio/theonekit-model-router
module: null
protected: false
---
# Model Router — Transparent Routing

When transparent routing is enabled, swap the **model** that runs each subagent — keep the agent's identity, prompt, and tools intact. This is what preserves engine-kit specialization (Unity DOTS, Cocos, etc.): the resolved Unity agent still runs, just on a cheap LLM instead of Opus.

## Activation Check

Read `.claude/t1k-config-mr.json`. The hook + rule both only fire when:
- File exists AND `modelRouter.enabled` is `true` AND `modelRouter.mode` is `"transparent"`

If any condition is false, this rule is inert.

## How It Works

Two mechanisms cooperate:

### 1. `mr-task-interceptor.cjs` (PreToolUse on `Task`)

When the main session spawns a subagent via the `Task` tool, this hook:

1. Reads the resolved agent's `.md` from the priority chain (project `.claude/agents/` → `~/.claude/agents/`).
2. Parses its `model:` frontmatter field (e.g. `claude-opus-4-7`, `inherit`).
3. Looks up `modelRouter.modelMapping[<model>]` in `t1k-config-mr.json`.
4. If matched AND the agent is NOT in `modelRouter.excludeAgents`:
   - Synchronously runs `mr-delegate.sh <agent> <prompt> --provider X --model Y`.
   - Blocks the original Task (`permissionDecision: deny`) and returns the cheap delegation's stdout as a `systemMessage`. The parent session sees the result as if Task had completed.
5. Otherwise: silent passthrough — Task runs normally on Anthropic.

This is the primary mechanism. It is mechanical and deterministic — runs whether or not you read this rule.

### 2. Delegation Bias (this rule, behavioral)

For **main-session work** that doesn't go through `Task` (inline `Edit` / `Write` / `Bash`), you decide whether to delegate. The interceptor only catches Task spawns; mainline edits stay on Opus unless you redirect them yourself.

## Step 0 — MANDATORY pre-tool consultation (mainline only)

**Before ANY inline Edit / Write / Bash that mutates user files, evaluate Delegation Bias.** The Task interceptor handles delegated work for you. This step is for the rest.

The check is one sentence: *"Is this task mechanical, boilerplate, or single-purpose?"*

- **Yes** → spawn it via `Task` with the appropriate `subagent_type` (e.g. `t1k-fullstack-developer` for implementation, `t1k-code-reviewer` for review). The interceptor will route to a cheap model automatically. If no `subagent_type` fits, call `mr-delegate.sh` directly with a sensible agent name.
- **No** → proceed with inline Edit/Write/Bash. State briefly why (e.g. "multi-file refactor needing cross-file context, inlining").

Skipping this step and going straight to inline Read+Edit burns Opus tokens for work a cheap model could do. The session-start hook `mr-transparent-routing-reminder.cjs` reinforces this — if you see `[t1k:transparent-routing] ACTIVE`, Step 0 is required.

## Step 0.5 — Parallel sub-agent dispatch (Agent tool, TeamCreate)

When fanning out parallel sub-agents for mechanical code work, pick the narrowest specialized T1K agent that fits the task. The transparent-routing interceptor will route the agent to a cheap provider automatically via `modelMapping` — no need to name a v1 cheap-coder shim.

| Task pattern | Preferred subagent_type |
|---|---|
| Mass rename, mechanical refactor, edit-per-plan | `t1k-fullstack-developer` |
| Read-only exploration ("find/list/grep") | `Explore` (built-in) |
| Run tests + report results | `t1k-tester` |
| Code review (read-only with Bash for lint/grep) | `t1k-code-reviewer` |
| Doc audit (read-only) or doc writes per spec | `t1k-docs-manager` |
| Multi-server MCP tool invocation | `t1k-mcp-manager` |

`general-purpose` is the FALLBACK when no specialized T1K agent matches. Default bias: pick the narrowest specialist that fits, not the broadest generalist.

## Delegation Bias — Prefer delegation for mechanical work

The primary motivation is **Opus token preservation**. Cheap subagents cost roughly 1-5% of Opus per token.

| Task pattern | Default |
|---|---|
| Single-file rename, format, lint-fix, add boilerplate | **Delegate** (Task → implementer-type agent) |
| Run a test suite + report results | **Delegate** (Task → tester) |
| Update README / docstring / comment | **Delegate** (Task → docs-writer) |
| Code review of changed lines (single PR / small scope) | **Delegate** (Task → reviewer) |
| Find files matching a pattern, list usages, search refs | **Delegate** (Task → explorer) |
| Audit existing docs for gaps | **Delegate** (Task → docs-scout / reader) |
| Multi-file refactor with cross-file reasoning | **Inline** (Opus owns this) |
| Design decision, architecture, planning | **Inline** (judgment calls) |
| Task that needs 3+ different tool types or chained context | **Inline** (orchestration overhead > delegation cost) |
| Reading one file to gather context (no edit follows) | **Inline** (single Read is free) |

**Heuristic — apply BEFORE picking a tool:** ask *"is this task mechanical, boilerplate, or single-purpose?"* If yes → spawn via Task. If it needs design judgement, cross-file reasoning, or 3+ distinct tools → inline. When in doubt for write/mutate tasks → delegate.

**Anti-pattern:** "the task is too trivial to spawn a subagent for." That phrase is wrong when transparent routing is on. The Task interceptor does the heavy lifting — your job is just to USE `Task` for mechanical work instead of inlining.

## When NOT to Delegate

1. **Parallel/multi-agent mode**: skill invoked with `--parallel` flag or multi-agent pipeline.
2. **Orchestration tasks**: planner, git-manager, brainstormer, project-manager — usually need Opus reasoning; mark them in `excludeAgents` if you want the interceptor to skip them.
3. **MR_SPAWNED=1**: already inside a delegated session (interceptor self-skips, but inline edits should also skip).
4. **User explicitly requested Claude**: user said "use Claude" or "don't delegate".

## `modelMapping` — the configuration knob

Keyed by **model name** (the value of `model:` in any agent's frontmatter), maps to `{ provider, model }`:

```json
{
  "modelRouter": {
    "enabled": true,
    "mode": "transparent",
    "modelMapping": {
      "claude-opus-4-7":           { "provider": "opencode-go", "model": "glm-5.1" },
      "claude-opus-4-7[1m]":       { "provider": "opencode-go", "model": "minimax-m2.7" },
      "claude-sonnet-4-6":         { "provider": "kimi",        "model": "kimi-k2.6" },
      "claude-haiku-4-5-20251001": { "provider": "kimi",        "model": "kimi-k2.5" },
      "inherit":                   { "provider": "kimi",        "model": "kimi-k2.6" }
    },
    "excludeAgents": [
      "t1k-architect",
      "t1k-planner"
    ]
  }
}
```

- An agent with `model: claude-opus-4-7` → routes to `opencode-go/glm-5.1`.
- An agent with `model: inherit` (the common case) → routes to `kimi/kimi-k2.6` by default.
- An agent listed in `excludeAgents` → never intercepted; stays on Opus.

To change all Sonnet calls to GPT-5.1: one line in the mapping. Applies to every agent in every kit whose frontmatter declares `model: claude-sonnet-4-6`.

## `failover.pipe` — what runs after the primary fails

The primary (selected by `modelMapping` above) is the **head** of an ordered failover pipe. When the primary returns a provider-failure signal (HTTP 429, 5xx, ECONNREFUSED, timeout, `rate limit` text), the bash delegate advances to the next hop without escalating to Anthropic. Anthropic is only used as the terminal when **all** configured hops fail.

```json
{
  "modelRouter": {
    "failover": {
      "enabled": true,
      "perHopTimeoutSec": 120,
      "pipe": [
        { "provider": "kimi",        "model": "kimi-k2.6" },
        { "provider": "opencode-go", "model": "glm-5.1" }
      ],
      "fallbackToAnthropic": true
    }
  }
}
```

- `perHopTimeoutSec` (default `120`) — per-attempt budget. Tuned for kimi at 138K input tokens (~67s observed). The interceptor's outer `spawnSync` budget is automatically sized to `pipe.length × perHopTimeoutSec + 30s`.
- `pipe` (ordered) — hops to try after the interceptor-selected primary. The CLI primary always runs as hop 0; matching entries in the pipe are skipped to avoid a wasted retry. When `pipe` is absent, the legacy circular `failover.chain` map is consulted for backward compat.
- `fallbackToAnthropic: true` — after the whole pipe fails, bash exits 42 and the interceptor passes the original Task through to Anthropic native.

A non-provider failure (real model error, not infrastructure) **stops** the pipe — we propagate the error rather than mask it by burning another 120s on the next hop.

## Delegation Output

The Task interceptor returns the cheap model's text output via `systemMessage`. The parent session sees it as the Task's result.

If `mr-delegate.sh` exits non-zero (timeout, provider down), the interceptor surfaces what it has + the exit code. Don't let the Task fall through to Anthropic on error — that would burn Opus tokens AND the cheap call's tokens. If the delegation failed, decide whether to retry or inline.
