---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Library Quality Mandate — Great Lib, Zero Tech Debt

**Status:** kit-wide, always-loaded, decision-authority rule for Unity projects that build a shared **library / submodule** consumed by multiple games. Future sessions act on this mandate without re-asking the user for permission on debt-removal calls.

## When this rule applies

Activate this mandate when the project's primary deliverable is a **reusable library or submodule** (typically under `Packages/<library-name>/`) consumed by other game projects, rather than a single shipping game. Common signals: the project has multiple demos that exercise the library; the project's `CLAUDE.md` explicitly names the library as the primary deliverable; the library has its own git remote (submodule).

Projects whose primary deliverable is a single shipped game can use this rule as a north star for their own internal libraries, but the "decision authority" clause is optional in that case.

## Rule

The library is **on a zero-tech-debt budget**. Every extraction, refactor, naming, and packaging decision MUST optimize for two outcomes simultaneously:

1. **Maximum code reuse** — across current demos and any future game project consuming the library.
2. **New-game velocity** — a brand-new mobile game using the library can ship a vertical slice in ≤2 weeks (realistic: prototype 2 weeks, polish 4 weeks).

When these two outcomes conflict with "minimum effort" or "ship faster by deferring cleanup," **the mandate wins**. Deferring debt is forbidden; either ship the right answer or do not ship at all.

## Decision authority (the load-bearing clause)

Future agents (this session, subsequent sessions, all kit-shipped agents working in a library-first project) MUST make the following call types **autonomously** based on this mandate, without re-prompting the user:

| Decision | Default action under this mandate |
|---|---|
| Track / extraction included in a plan? | Ship it if it removes duplication or unblocks future reuse. Defer ONLY if a downstream dependency is genuinely unbuilt. |
| Wave-N / P2 work deferred to a follow-up plan? | **No.** All tracks ship in the same plan; sequencing within waves is allowed. |
| Naming-charter violation in proposed code? | Block. Rename before merge. No exceptions. |
| Genre / perspective token in core-package code? | Block. Relocate to the correct package or namespace. |
| Magic numbers / hardcoded paths in extracted code? | Block. Replace with ScriptableObject config or named constant. |
| Demo introduces a pattern that 2+ other demos already have inline? | Extract to library now. Do not "wait for the third copy." |
| Demo introduces a pattern unique to itself but plausibly reusable? | Either extract speculatively (preferred) OR file `[t1k:lesson]` marker so the next session catches the second occurrence. |
| Breaking import change when relocating namespaces? | Ship with `[Obsolete]` aliases for one release cycle + migration guide. Never block on the breaking change itself. |
| Library skill is wrong or stale? | Fix it as part of the current change. Do not leave for "later" — there is no later. |
| Library package count grows? | Acceptable if each new package serves a clear axis of reuse (genre add-on, perspective, infrastructure). Forbidden if it only buys "org clarity." |

The user does not need to be consulted on any of the above. If a decision falls outside these patterns, fall back to `rules/always-ask-on-unresolved.md`.

## What "great library" means (objective tests)

A change to the library passes the mandate iff ALL of the following hold:

1. **Genre-neutral:** the API would be acceptable in a puzzle game AND an RPG AND an idle game AND a deck-builder, without renaming.
2. **Perspective-neutral:** the API works in 2D top-down, 2D side-view, isometric, AND 3D, without `#if` blocks. (Perspective-specific code stays in a dedicated perspective subsystem.)
3. **Game-mode-neutral:** the API works in turn-based, real-time, auto-battler, idle, AND deck-builder game modes. Mode-specific code lives in a dedicated game-modes package.
4. **Data-driven:** every tunable number is a ScriptableObject field, an authoring component property, or a named constant — never an inline magic literal.
5. **Tested:** any new ISystem ships with `Tests/EditMode/` coverage.
6. **Charter-conformant:** type, namespace, addressable, prefab, SubScene, MenuItem, and behavior-tree names follow the project's naming charter (see § "Naming charter" below).
7. **Documented:** if the surface is non-trivial, the owning skill ships a `references/` doc update in the same commit. No "follow-up" docs.

## Naming charter (canonical guidance — project-specific values customize these patterns)

| Surface | Rule | Bad → Good (example) |
|---|---|---|
| Type names | No demo / game / genre token. Use generic role or mechanic. | `MyDemoUnitPrefabCreator` → `UnitPrefabFactory`; `SurvivorWeapon` → `AutoFiringWeapon` |
| Namespaces | One root namespace per library package; no genre tokens except inside the game-modes package. | `LibCombat.AutoBattler.*` → `LibGamemodes.AutoBattler.*` |
| Generic roles | Use `Agent` / `Entity` / `Unit`. Never `Hero`/`Enemy`/`Monster` as type names. Specific classes (Mage, Knight) only as enum values. | `HeroController` → `AgentController` |
| Spatial | `Arena`, `Tile`, `Cell`, `Room`, `Region`. `Battlefield` is acceptable (concept, not name). | `BattleScene` → `ArenaScene` |
| Addressables groups | Prefix `<LibraryNamespace>.Gameplay.<Subsystem>`. | `MyDemo_Units` → `Lib.Gameplay.Agents` |
| Addressables keys | Archetype + optional team. | `RedMelee` → `Unit_Melee_Red` |
| SubScene names | `MainSubScene`, `<Concept>SubScene`. | `MyDemoSubScene` → `MainSubScene` |
| MenuItem paths | Library menus under `Tools/Library/<Subsystem>/`. Demo menus under `Tools/<DemoName>/`. | `Tools/MyDemo/BDP Build` → `Tools/Library/BDP/Build Standard Trees` |
| Prefab filenames | `Agent_<Archetype>_<Variant>.prefab`. Demo overrides `<DemoName>_Agent_Custom.prefab`. | `RedMelee.prefab` → `Agent_Melee_Swordsman.prefab` |
| Behavior-tree assets | Library trees `Library_<purpose>.asset`. Demo overrides `<DemoName>_<purpose>.asset`. | `MyDemo_StandardCombatTree.asset` → `Library_StandardCombat.asset` |
| Perspective tokens | Allowed inside the perspective-rendering subsystem (the file IS the perspective). Forbidden anywhere else in library. | `SideViewAuthoring.cs` in core/Runtime/ → rendering/Profiles/ |

**Project-specific naming charters** (concrete package names, namespace prefixes, addressable group names) live in the project's own `CLAUDE.md` or a project-local rule. This kit-wide rule supplies the **patterns**; the project supplies the **concrete tokens**.

## How to apply

1. **Every PR / commit touching the library** — run a mental pass over the 7 objective tests + naming charter. Fail → fix before merging.
2. **Every brainstorm / plan** — extraction tracks default to "ship now"; only defer if a downstream dependency is genuinely missing.
3. **Every code review** — reviewers cite this rule by section number when blocking on naming, genre leakage, or premature deferral.
4. **Future sessions starting fresh** — read this rule first; act on it without asking the user to re-state preferences.

## Why this rule exists

Originated 2026-05-27 in a DOTS-AI library-extraction master-plan brainstorm. User directive:

> "we want to build a great lib, with no tech depth [debt]. note in claude.md about that, also note in skills so you can decide everything later yourself."

The library is the deliverable. Every demo, every extraction, every refactor is in service of "is this library good enough that other game projects can use it to ship products?" If the answer drifts toward "good enough for our current demos only," the project has failed its primary mission.

## Project adoption note

To adopt this mandate in a library-first Unity project:

1. Confirm the project's primary deliverable is a reusable library/submodule (see § "When this rule applies").
2. Add a one-line bullet to the project's `CLAUDE.md` § "Key Principles" referencing this rule.
3. Define the project's concrete naming charter (concrete package names, namespace roots, addressable prefixes) — either inline in `CLAUDE.md` or in a project-local `rules/<project>-naming-charter.md`.
4. From that point on, agents in the project act on the mandate without re-asking the user.

## Related

- `rules/development-principles.md` — SSOT, no silent fallbacks, no derived fields. The mandate strengthens these.
- `rules/code-conventions.md` — generic naming, no magic numbers. The mandate codifies the library-specific elaboration.
- `rules/library-feature-discovery-protocol.md` — research 3× before implementing new. The mandate is the "why" behind this protocol.
- `rules/manual-correction-implies-skill-gap.md` — every manual correction is a skill gap. Same philosophy applied to library architecture.
