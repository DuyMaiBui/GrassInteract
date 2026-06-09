---
name: t1k:designer:base:document
description: GDD structure, templates, and living documentation patterns for game projects — wiki pages, design docs, section layout, sync triggers
effort: medium
keywords: [GDD, game design document, documentation, design]
version: 1.13.2
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Game Design Document

## When This Skill Triggers
- Creating or updating a GDD, wiki page, or design document
- New demo added to the project
- Major gameplay feature changed (combat, inventory, progression, UI)
- Design review requested; syncing docs to code
- Writing Demo-*.md wiki pages for DOTS-AI demos

## What a GDD Is
A GDD (Game Design Document) is the living specification of a game's design intent. In this project, it exists at two levels:
- **Demo Wiki Pages** (`docs/wiki/Demo-{DemoName}.md`) — per-demo, code-synced, <300 lines
- **Full GDD** — high-level design pillars, economy, balance, art direction

## GDD Section Overview
| Section | Purpose |
|---------|---------|
| Game Overview | Title, genre, platform, audience, core loop |
| Game Pillars | 3-5 non-negotiable design tenets |
| Gameplay Mechanics | Core loop, combat, inventory, progression |
| Game Flow | Phase state machine, win/lose conditions |
| UI/UX Design | Screen layout, HUD, menu flow |
| Economy & Monetization | Currency, shops, drop rates |
| Technical Requirements | FPS target, memory budget, platform constraints |
| Art & Audio Direction | Visual style, palette, sound design |
| Content Matrix | Items, enemies, levels with stats |
| Balance Parameters | Tuning knobs, difficulty curves |

→ See `references/gdd-template.md` for full section templates

## Demo Wiki Page Workflow
1. Read `Editor/` (SceneSetup, PrefabCreator) to extract unit counts, arena sizes, menu paths
2. Read `Runtime/` (Systems, Components) to extract class names, constants
3. Read existing wiki page — identify stale sections
4. Cross-reference with library/package code for accuracy
5. Write/update using mandatory 14-section structure from `game-designer` agent
6. Verify ALL numbers match code constants (grep don't guess)

→ See `references/demo-wiki-template.md` for mandatory section list and examples

## Trigger Conditions
| Event | Action |
|-------|--------|
| New demo implemented | Create `docs/wiki/Demo-{Name}.md` from template |
| Code changes (systems, components) | Update affected wiki sections |
| Stats/balance tuned | Update Content Matrix and Balance Parameters |
| UI redesigned | Update UI/UX Design section |
| New editor tool added | Update Editor Tools section |
| Library module added | Update Library Coverage section |

## Living Doc Principles
- **Code is source of truth** — grep constants, never guess numbers
- **Wiki mirrors code** — update docs immediately after code changes
- **Cross-reference, don't duplicate** — link to other wiki pages
- **Numbers must be verifiable** — comment which file/line each constant comes from

→ See `references/living-doc-patterns.md` for sync patterns and cross-reference conventions

## Common Mistakes
| # | Mistake | Fix |
|---|---------|-----|
| 1 | Hardcoded numbers in wiki that don't match code | Grep source constants before writing |
| 2 | Wiki over 300 lines | Extract deep-dives to Domain-*.md, link from wiki |
| 3 | Missing cross-reference updates | After any wiki change, search for pages that link to it |
| 4 | Stale system class names after rename | Run grep for old name, update all occurrences |

## Cross-References
- engine implementation skills — library component and system names for accurate documentation
- engine architecture skills (auto-activated via registry) — system design patterns
- engine code convention skills (auto-activated via registry) — naming for mapping code to docs

## Reskin-Enablement Cross-Link (MANDATORY for art-touching design docs)

When the design doc you're authoring describes a feature that ships art assets (UI screens, characters, VFX, audio, animations), the doc MUST include a `## Reskin Contract` subsection cross-linking to the matching dept-page §Reskin Self-Service.

Template:

```markdown
### Reskin Contract

Which parts of this feature can a 2D / VFX artist reskin without engineering help?

| Element | Reskinnable | Replacement path | Linked dept-page section |
|---|---|---|---|
| <feature element 1> | Yes / No (engineering-only — explain why) | `Assets/Art/...` or via SO field | [Art-2D §Reskin Self-Service](StickManForge-Art-2D#reskin-self-service--project--prefab-structure) |
| <feature element 2> | ... | ... | ... |

Workflow link: [Art-2D §Reskin Workflow](StickManForge-Art-2D#3-reskin-workflow-artist-driven-no-engineer) |
```

If the dept page doesn't yet cover this asset family, the design doc author ALSO opens a wiki-edit ticket against the dept page (or applies the edit themselves if scope is small).

Cross-link to t1k-designer-wiki-core §Dept-Page Reskin Self-Service Convention for the canonical template.

Rationale: design intent for reskin-enablement decays if not captured at design time. Catching it in the design doc → propagating to dept page → enforced in implementation = the chain stays sealed.

## Reference Files
| File | Coverage |
|------|----------|
| `references/gdd-template.md` | Full GDD with all standard sections and guidance |
| `references/living-doc-patterns.md` | Sync triggers, cross-reference conventions, wiki patterns |
| `references/demo-wiki-template.md` | Demo-*.md mandatory structure, how to gather info from code |

## Gotchas

- **GDD goes stale on day 2 of production** — without a sync trigger (post-feature, post-playtest, post-pivot), the GDD becomes fiction. Bake the trigger into your sprint review.
- **One-page GDDs work for prototypes; multi-page GDDs work for live games** — a 60-page GDD for a 2-week prototype kills the prototype. Right-size by horizon.
- **Wiki vs in-repo MD is a real fork** — wiki updates outside PR review drift from code reality. Prefer in-repo docs for truth, wiki for marketing-facing summaries.
