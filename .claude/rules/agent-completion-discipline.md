---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Agent Completion Discipline — Commit Before Summary

**Resolves:** unity#74 (tail-of-thought stop — agent exits without committing pending work at >150K tokens)

## Rule

**COMMIT BEFORE YOU SUMMARIZE.** Any agent that has made edits, written files, or completed implementation work MUST commit + push those changes BEFORE composing any narrative summary, wrap-up, or report.

Order of operations (mandatory):

1. Dispatch all pending `Write` operations
2. `git add` + `git commit` + `git push`
3. THEN compose the summary / report / wrap-up message

**150K token checkpoint (MANDATORY for sub-agents):** At ~150K context tokens, STOP all investigation and immediately:

1. Check `git status` — if any staged or unstaged changes exist, commit them NOW
2. Dispatch any pending Write operations before reading more files
3. Only resume investigation or compose a summary AFTER the commit lands

## Anti-pattern to detect in yourself

> "Let me check one more thing before committing…" at >150K tokens.

That sentence is the symptom. Interrupt it. Commit first. Investigate after.

## Why

7/8 sub-agent stops in the 2026-05-14 BackpackCrawler session (unity#74) occurred because agents reached 168–212K tokens mid-investigation and exited as `completed` without committing already-made edits. Recovery required separate commit-only finisher agents, doubling spawn cost per phase.

## Validation (would this rule have prevented unity#74?)

Yes — all 7 failures shared the same pattern: investigation continued past 150K tokens with uncommitted edits. The 150K checkpoint with mandatory `git status` check would have forced commits at the right moment in all 7 cases.
