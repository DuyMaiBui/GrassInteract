---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Library Quality Mandate — Unity Naming Charter (engine-specific additions)

Extends core `rules/library-quality-mandate.md` — Unity-specific additions only. Core supplies the universal mandate (zero-tech-debt budget, domain/data/test/charter/doc pass-test, decision authority); this file supplies the **Unity/DOTS concrete naming charter** that core's generic "charter-conformant" clause delegates to the owning kit.

## When this applies

A Unity project whose primary deliverable is a reusable library/submodule (typically under `Packages/<library-name>/`) consumed by other game projects — multiple demos exercising the library, `CLAUDE.md` naming the library as the deliverable, or the library having its own git remote. Single-shipped-game projects can use this as a north star; the decision-authority clause (in core) is optional there.

## Unity decision-authority rows (engine-specific)

These augment core's decision table with Unity-package-specific calls agents make autonomously under the mandate:

| Decision | Default action |
|---|---|
| Genre / perspective token in core-package code? | Block. Relocate to the correct package or namespace. |
| Demo introduces an `ISystem`/authoring pattern 2+ other demos already have inline? | Extract to the library package now. Do not "wait for the third copy." |
| Breaking import change when relocating namespaces? | Ship with `[Obsolete]`/`[MovedFrom]` aliases for one release cycle + migration guide. Never block on the breaking change itself. |
| Unity library package count grows? | Acceptable if each package serves a clear axis of reuse (genre add-on, perspective, infrastructure). Forbidden if it only buys "org clarity." |
| New `ISystem` shipped without tests? | Block. Any new ISystem ships with `Tests/EditMode/` coverage. |

## Naming charter (Unity surfaces — the load-bearing delta)

Core's pass-test requires "charter-conformant" names; for Unity that means these concrete surfaces:

| Surface | Rule | Bad → Good (example) |
|---|---|---|
| Type names | No demo / game / genre token. Use generic role or mechanic. | `MyDemoUnitPrefabCreator` → `UnitPrefabFactory`; `SurvivorWeapon` → `AutoFiringWeapon` |
| Namespaces | One root namespace per library package; no genre tokens except inside the game-modes package. | `LibCombat.AutoBattler.*` → `LibGamemodes.AutoBattler.*` |
| Generic roles | Use `Agent` / `Entity` / `Unit`. Never `Hero`/`Enemy`/`Monster` as type names. Specific classes (Mage, Knight) only as enum values. | `HeroController` → `AgentController` |
| Concrete instance vs role | A type/system/tag name must describe the generic ROLE, never one concrete instance — especially when a generic sibling already exists (e.g. a `ProjectileData`/`ProjectileCollisionSystem`). A concrete instance belongs in an enum VALUE or demo content, not a type name. | `ArrowRegistryTag`/`TeamArrowPrefab`/`ArrowPrefabAuthoring` → `ProjectileRegistryTag`/`TeamProjectilePrefab`/`ProjectilePrefabAuthoring`; `MageAttack*` → `ProjectileAttack*`/`CasterAttack*` (Mage = enum value) |
| Spatial | `Arena`, `Tile`, `Cell`, `Room`, `Region`. `Battlefield` is acceptable (concept, not name). | `BattleScene` → `ArenaScene` |
| Addressables groups | Prefix `<LibraryNamespace>.Gameplay.<Subsystem>`. | `MyDemo_Units` → `Lib.Gameplay.Agents` |
| Addressables keys | Archetype + optional team. | `RedMelee` → `Unit_Melee_Red` |
| SubScene names | `MainSubScene`, `<Concept>SubScene`. | `MyDemoSubScene` → `MainSubScene` |
| MenuItem paths | Library menus under `Tools/Library/<Subsystem>/`. Demo menus under `Tools/<DemoName>/`. | `Tools/MyDemo/BDP Build` → `Tools/Library/BDP/Build Standard Trees` |
| Prefab filenames | `Agent_<Archetype>_<Variant>.prefab`. Demo overrides `<DemoName>_Agent_Custom.prefab`. | `RedMelee.prefab` → `Agent_Melee_Swordsman.prefab` |
| Behavior-tree assets | Library trees `Library_<purpose>.asset`. Demo overrides `<DemoName>_<purpose>.asset`. | `MyDemo_StandardCombatTree.asset` → `Library_StandardCombat.asset` |
| Perspective tokens | Allowed inside the perspective-rendering subsystem (the file IS the perspective). Forbidden anywhere else in library. | `SideViewAuthoring.cs` in core/Runtime/ → rendering/Profiles/ |
| `.asmdef` layout | One assembly definition per library package; runtime/editor/tests split into separate `.asmdef`s. Package boundaries follow the namespace root. | flat `Scripts.asmdef` mixing runtime+editor → `Lib.Combat.Runtime.asmdef` + `Lib.Combat.Editor.asmdef` + `Lib.Combat.Tests.asmdef` |

**Project-specific naming charters** (concrete package names, namespace prefixes, addressable group names) live in the project's own `CLAUDE.md` or a project-local rule. This kit-wide rule supplies the **Unity patterns**; the project supplies the **concrete tokens**.

## Project adoption note (Unity)

1. Confirm the project's primary deliverable is a reusable Unity library/submodule.
2. Add a one-line bullet to the project's `CLAUDE.md` § "Key Principles" referencing core's mandate + this Unity charter.
3. Define the project's concrete naming charter (concrete package names, namespace roots, addressable prefixes) — inline in `CLAUDE.md` or a project-local `rules/<project>-naming-charter.md`.

## Related

- `rules/library-quality-mandate.md` (core) — the universal mandate this extends (zero-tech-debt budget, decision authority, pass-test).
- `rules/library-feature-discovery-protocol.md` — research 3× before implementing new; the mandate is the "why".
- `rules/manual-correction-implies-skill-gap-unity.md` — every manual correction is a skill gap; same philosophy applied to library architecture.
