---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Kit-Wide Fix Discipline — Fix at the Kit Source, Not Locally

## Rule

Every improvement, bug fix, doc correction, or behavior change to kit-owned content (anything under `.claude/` shipped by a `theonekit-*` kit) MUST land in the **kit source repo**, NOT just locally in the consumer project where the symptom was found.

A local-only patch is a regression in disguise: global (`$HOME/.claude/`) copies are overwritten on the next `t1k modules update` (fix disappears), and project-local (`<consumer>/.claude/`) copies survive locally but never reach other consumers (same bug, every other install). The kit source is the only place where one fix reaches everyone.

## How to apply

When a fix or improvement is needed:

1. **Identify the owning kit.** For any file under `.claude/` check `_origin.kit` JSON field, `origin:` frontmatter, or `t1k-origin: kit=...` comment. Or `grep -r` across `/mnt/Work/1M/8. OneAI/theonekit-*` clones.
2. **Edit in the kit source repo.** `cd` into the kit clone (`/mnt/Work/1M/8. OneAI/<owning-kit>`), branch, edit, test, commit, push, open PR.
3. **Release picks it up.** Merge → `theonekit-release-action` publishes → consumers get the fix on next `t1k modules update`.

### Acceptable stopgap pattern

A local patch is acceptable ONLY when:

- The fix is needed immediately to unblock current work, AND
- A `/t1k:sync-back` PR (or direct kit-source PR) is opened in the **same session**, AND
- The kit PR is tracked to merge before the work is declared done

The stopgap path becomes a violation when the session ends without a corresponding kit PR.

## Anti-patterns

- Editing a global `~/.claude/` file then closing the session → fix disappears on next update.
- Committing a fix only to the consumer repo → other consumers regress.
- Filing a skill-bug issue on the consumer repo → wrong tracker; kit maintainers don't see it.
- "Sync-back later" → "later" never happens; treat the sync-back PR as part of the same atomic unit of work.

## Related

- `prefer-local-over-global-edits.md` — within a project, prefer project-local over global (complementary)
- `kit-pr-workflow-boundary.md` — kit-PR babysitting belongs to the kit-maintainer session
- `update-kits-before-sync-back.md` — refresh the kit clone before pushing
- `orchestration-rules.md` — `/t1k:sync-back` and `/t1k:issue` are the canonical kit-targeting workflows
- `CLAUDE.md` Core Requirement #12 — origin of this rule
