---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Code Conventions (Universal)

Applies to ALL languages and frameworks. Kit-specific rules extend this in `code-conventions-{kit}.md`.

## SOLID Principles
- **Single Responsibility:** one class/function = one reason to change
- **Open/Closed:** extend via composition, not modification
- **Liskov Substitution:** subtypes must be substitutable for base types
- **Interface Segregation:** prefer small, focused interfaces
- **Dependency Inversion:** depend on abstractions, not concretions

## Naming
- Names must be self-documenting — if a name needs a comment, rename it
- Booleans: use `is`, `has`, `can`, `should` prefixes
- Functions: use verbs — `getUser`, `calculateTotal`, `validateInput`
- Avoid abbreviations except widely known ones (`id`, `url`, `api`)

## Structure
- One class/component per file (small related types may share)
- Max 200 lines per file — split if larger
- Guard clauses over nested if/else — return early
- Prefer composition over inheritance; prefer immutability (`const`, `readonly`, `final`) by default

## Code Quality
- No magic numbers — extract to named constants or config
- No empty catch blocks — handle or rethrow with context
- No `TODO` in merged code — track in issues instead
- Import order: stdlib → external packages → internal modules

## Data-Driven Over Hardcoded
- **NEVER hardcode mappings** (command→skill / role / agent, keyword→module) in hooks or scripts. Always read from registry files at runtime so new skills/agents/modules auto-discover with no code change.
- **Test:** deleting a static map should break nothing because the data comes from files. If it breaks → you're hardcoding.

## No Duplicated Logic
- Before writing a utility function, search for existing ones in shared modules (`telemetry-utils.cjs`, `lib/`)
- If the same pattern appears in 2+ files, extract to a shared module immediately — not "later"
- Each `.claude/` path resolution must use `resolveClaudeDir()` — no inline `path.join(cwd, '.claude')` checks
- `null` / `undefined` guards must be applied where data flows between systems

## No Derived Fields — SSOT for Data
- **NEVER store a value that can be computed from other columns.** If `C = f(A, B)` and `A`/`B` are stored, compute `C` at the query/use site, do not store it.
- **Exception — materialized for performance only:** acceptable IF profiling proves it's a real bottleneck AND source columns rarely change AND it's kept in sync via trigger/constraint/CI AND the formula is documented inline.
- **Test:** if you can reconstruct every value from the remaining columns with one deterministic expression, the column is derived and should not exist.

## Testing
- Test public behavior, not implementation details
- Each test should be independent — no shared mutable state
- Name tests descriptively: `should_returnError_when_inputIsNull`

## Living Document
If unsure about a convention not covered here, ask the user for their preference and update this file.
