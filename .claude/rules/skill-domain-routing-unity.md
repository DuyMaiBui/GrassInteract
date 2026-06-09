---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
<!-- t1k-origin: kit=theonekit-unity | repo=The1Studio/theonekit-unity | module=null | protected=true -->

# Skill Domain Routing (theonekit-unity)

Intent-based discovery for Unity kit skills. For core T1K skills (cook, fix, plan, etc.), see `skills/t1k-help/references/skill-domain-routing.md`.

## DOTS — Entity Component System

User wants to...
- Write ISystem, IComponentData, archetype queries, and structural changes → `dots-ecs`
- Enable/disable components at runtime (IEnableableComponent) → `dots-enableable-components`
- Use EntityCommandBuffer for deferred structural changes → `dots-entity-command-buffer`
- Validate entity state and component data at runtime → `dots-runtime-validator`
- Apply proven DOTS architecture patterns → `dots-architecture`

## DOTS — Physics & Simulation

User wants to...
- Unity Physics 1.4.5 colliders, triggers, velocity, and raycasts → `dots-physics`
- Implement batch unit combat, projectiles, or hit detection → `dots-battlefield`

## DOTS — Performance

User wants to...
- Write IJobEntity, IJob, BurstCompile jobs, and NativeCollections → `dots-jobs-burst`
- Profile and optimize DOTS systems or ECS queries → `dots-performance`
- Use ZLinq zero-alloc LINQ for DOTS pipelines → `zlinq`
- Use ZString zero-alloc string formatting → `zstring`

## DOTS — Rendering & Graphics (module: `dots-rendering`)

User wants to...
- DOTS Graphics (Entities Graphics, BatchRendererGroup, GPU instancing) → `dots-rendering-graphics`
- 3D perspective camera, frustum, LOD → `dots-rendering-perspective-3d`
- 2D side-scrolling perspective camera and viewport → `dots-rendering-perspective-2d-sideview`
- 2D top-down perspective camera → `dots-rendering-perspective-2d-topdown`
- Isometric projection and perspective → `dots-rendering-perspective-isometric`
- Shared perspective framework utilities → `dots-rendering-perspective-framework`

## DOTS — Scene & Content

User wants to...
- Load, unload, or stream SubScenes with baking lifecycle → `dots-subscene`
- Manage scene state and transitions in DOTS → `t1k-scene`
- Build DOTS inventory grids or slot systems → `dots-inventory-grid`
- Implement DOTS puzzle mechanics → `dots-puzzle`
- DOTS RPG progression, stats, and inventory → `dots-rpg`

## DOTS — Testing (module: `dots-testing`)

User wants to...
- Write unit tests for DOTS systems using World test harness → `dots-testing-unit-testing`
- Validate entity/component state at runtime in DOTS → `dots-testing-runtime-validator`

## Rendering & Shaders

User wants to...
- URP pipeline setup, renderer features, and post-processing → `unity-urp`
- Write Shader Graph nodes or HLSL shaders → `unity-shader-graph`
- Light baking, lightmaps, and mixed lighting for DOTS → `unity-light-baking`
- Optimize shadow atlases and cascades → `unity-shadow-optimization`
- MK Toon Shader integration and customization → `mk-toon-shader`
- Amplify Impostors baking and runtime setup → `amplify-impostors`

## Visual Effects & Animation

User wants to...
- Build VFX Graph particle systems → `unity-vfx-graph`
- Animator, Animation Clips, and blend trees → `unity-animation`
- Combine VFX Graph with Animator workflows → `unity-animation-vfx`
- Timeline sequences and Cinemachine cameras → `unity-cinemachine`

## Gameplay Systems

User wants to...
- MonoBehaviour event patterns and lifecycle → `unity-monobehaviour`
- Object pooling, state machines, service locator patterns → `unity-game-patterns`
- New Input System bindings and action maps → `unity-input-system`
- Addressables loading, build groups, and memory management → `unity-addressables`
- Save/load system with local or cloud persistence → `unity-save-system`
- Scene management (loading, additive, async) → `unity-scene-management`

## AI & Navigation

User wants to...
- Behavior Designer Pro behavior trees → `behavior-designer-pro`
- Agent navigation, pathfinding, and obstacle avoidance → `agents-navigation`
- Tactical formations pack integration → `bdp-formations-pack`
- BDP movement pack locomotion → `bdp-movement-pack`
- BDP tactical decision pack → `bdp-tactical-pack`

## UI Systems

User wants to...
- UI Toolkit UXML/USS components and data binding → `unity-ui-toolkit`
- Legacy UGUI Canvas layouts and components → `unity-ugui`
- Mobile-optimized UI layout and touch → `unity-mobile-ui`
- Text localization and font configuration → `unity-text-config`
- Localization strings and language switching → `unity-localization`

## Mobile

User wants to...
- iOS/Android build settings, permissions, performance → `unity-mobile`

## Audio

User wants to...
- AudioSource, AudioMixer, spatial audio → `unity-audio`

## Terrain & Environment

User wants to...
- Unity Terrain sculpting, texturing, and vegetation → `unity-terrain`
- ProBuilder in-editor mesh modeling → `unity-probuilder`

## Asset Integrations

User wants to...
- Synty Polygon Generic pack setup and prefab guidelines → `synty-polygon-generic`
- Synty Polygon Fantasy Rivals integration → `synty-polygon-fantasy-rivals`
- Synty Polygon Knights asset setup → `unity-pivot-hierarchy` + `synty-polygon-knights`
- Asset Hunter Pro — dead asset detection and cleanup → `asset-hunter-pro`

## Serialization & Networking

User wants to...
- MemoryPack binary serialization → `memorypack`
- Google Protobuf schema and codegen → `google-protobuf`
- LitMotion zero-alloc tweening → `litmotion`
- Unity Netcode for GameObjects multiplayer → `unity-netcode`

## Testing & Quality

User wants to...
- Unity Test Runner, NUnit, and CI test automation → `testing-workflow`
- Code coverage reports and thresholds → `testing-code-coverage`
- Unity code conventions and code standards → `unity-code-conventions`
- Profiler, Memory Profiler, and frame debugger → `unity-profiling`

## Kit Maintenance (Unity)

User wants to...
- Balance stats, formulas, and DPS curves in-editor → `t1k-balance`
- Manage milestone deliverables and feature flags → `t1k-milestone`
- Playtest tracking and session reporting → `t1k-playtest`
- Profile CPU/GPU per-system in the Editor → `t1k-profile`
- Sync DOTS scene state across prefab variants → `t1k-sync`
- Wiki documentation for Unity modules → `t1k-wiki`

## MCP & Tooling

User wants to...
- Use the Unity MCP bridge to drive the Editor from Claude → `unity-mcp-skill`
- Create new Unity MCP tools → `unity-mcp-tool-creator`

## Notes

- All skills above are Unity-kit skills; invoke via the Skill tool
- For core T1K workflow skills (cook, fix, plan, test, review), see `skills/t1k-help/references/skill-domain-routing.md`
- DOTS-heavy features often combine multiple skills: dots-ecs + jobs-burst + physics
