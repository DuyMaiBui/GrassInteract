---

origin: theonekit-model-router
repository: The1Studio/theonekit-model-router
module: null
protected: false
---
# Model Capabilities Guide

> This file is read by the primary Claude agent to decide which provider and model to use for each delegation. When delegating via `/t1k:model-router:delegate` or transparent routing, choose the model that best fits the task requirements.

## Available Models (OpenCode Go)

| Model | Quality | Context | Speed | Cost | Best for |
|-------|---------|---------|-------|------|----------|
| `glm-5.1` | Excellent | 200K | Medium | High (~880 req/5hr) | Complex architecture, security review, difficult reasoning |
| `glm-5` | Good | 200K | Medium | Medium (~1150 req/5hr) | High-quality coding, refactoring |
| `kimi-k2.6` | Excellent | 256K | Medium | Medium (~1850 req/5hr) | Best balance — general coding, writing, analysis |
| `kimi-k2.5` | Good | 256K | Medium | Medium (~1850 req/5hr) | Solid for writing/summarization. For file-discovery roles prefer k2.6 (see Known limitations). |
| `mimo-v2-pro` | Good | 128K | Fast | Medium (~1290 req/5hr) | Code completion, generation, fast tasks |
| `mimo-v2-omni` | Fair | 256K | Fast | Low (~2150 req/5hr) | Fast prototyping, simple tasks |
| `qwen3.6-plus` | Fair | 128K | Fast | Low (~3300 req/5hr) | Cost-effective general coding |
| `qwen3.5-plus` | Basic | 128K | Very Fast | Very Low (~10200 req/5hr) | Cheapest — file listing, grep, simple lookup |
| `minimax-m2.7` | Fair | **1M** | Medium | Low (~3400 req/5hr) | Long context specialist — large file analysis |
| `minimax-m2.5` | Basic | **1M** | Fast | Very Low (~6300 req/5hr) | Long context on a budget |

## Model Selection Guidelines

### By task complexity

| Task complexity | Recommended models | Why |
|----------------|-------------------|-----|
| **Simple** (list files, grep, lookup) | `qwen3.5-plus`, `mimo-v2-omni` | Cheapest, fast enough |
| **Medium** (code review, write docs, implement feature) | `kimi-k2.6`, `glm-5` | Good quality/cost balance |
| **Complex** (architecture analysis, security audit, deep reasoning) | `glm-5.1` | Best reasoning, worth the cost |
| **Long context** (analyze large codebase, read many files) | `minimax-m2.7` | 1M context window |

### By task domain (suggestions)

The kit no longer ships role-shaped agents. Once you've picked an agent from the consumer's `t1k-*` roster (per `rules/mr-transparent-routing.md`), use the **task complexity** table above to pick the model. These domain hints just narrow the starting point:

| Task domain | Default model | Upgrade when |
|---|---|---|
| File discovery / grep / list | `qwen3.5-plus` | Complex codebase, need synthesis → `kimi-k2.6` |
| Doc audit (read-only) | `kimi-k2.6` | Large docs set → `minimax-m2.7` |
| Doc write / README update | `kimi-k2.6` | Highly technical docs → `glm-5.1` |
| Implement / boilerplate | `kimi-k2.6` | Trivial single-file change → `qwen3.5-plus` |
| Code review / security audit | `glm-5.1` | Quick scan only → `kimi-k2.6` |
| Run tests / interpret failures | `qwen3.5-plus` | Complex multi-suite analysis → `kimi-k2.6` |

These are **suggestions**, not defaults. You choose the best model per task.

### Known limitations

| Model | Limitation |
|-------|-----------|
| `kimi-k2.5/k2.6` | Tool_calls with Write may fail (reasoning_content issue). Fallback auto-kicks in. |
| `kimi-k2.5` | More prone than k2.6 to picking bash-style brace patterns (`{*.js,*.py,...}`) which Claude Code's `Glob` does not expand → empty result. For exploration/discovery tasks prefer k2.6. |
| `qwen3.5-plus` | May hit Alibaba quota. Fallback to mimo-v2-pro. |
| `minimax-m2.5/m2.7` | Uses Anthropic-compatible endpoint (native, no translation needed). |
| `Glob` tool (any model) | Recursive on path by default, matches FILENAME only (not full path), and does NOT expand brace patterns. Broad patterns like `*` or `[!.]*` flood with subtree results and cannot exclude subdirs (e.g. `.claude/`). Verified end-to-end via ccs proxy logs 2026-05-08 — proxy/CLIProxy/Kimi all forward Glob args correctly; the surprise is in Glob semantics. |

## Providers

All three providers authenticate with `gh auth token` (The1Studio org membership required) — no per-provider keys to manage.

| Provider | Models | Endpoint |
|----------|--------|----------|
| **OpenCode Go** | GLM, Kimi, Qwen, MiMo, MiniMax | `https://oc-go-cc.the1studio.org` |
| **Kimi (direct)** | kimi-k2, kimi-k2.5, kimi-k2.6 | `https://ccs.the1studio.org/api/provider/kimi` |
| **Codex** | gpt-5.1, o3 | `https://ccs.the1studio.org/api/provider/codex` |

### Provider selection guidelines

| Scenario | Provider | Why |
|----------|----------|-----|
| Default (most tasks) | OpenCode Go | Largest model selection, native Anthropic-compatible endpoint |
| Kimi-specific tasks | Kimi direct | Better tool_calls support than going through OpenCode Go translation |
| OpenCode Go quota exhausted | Kimi direct | Fallback — independent quota |
| GPT/o3 tasks | Codex | OpenAI models for comparison or specific needs |

> Usage: `--provider kimi --model kimi-k2.6`
> Requires: `gh auth login` with The1Studio org membership.

## Cache behavior

All models auto-cache prompts ≥1024 tokens. Turn 2+ typically 98% cache hit. No action needed.
