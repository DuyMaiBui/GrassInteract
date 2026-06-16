---
name: t1k:unity:base:project-structure
description: TheOne Unity Assets/ folder layout — 3-layer structure, _Game/<Game>/ asset tree, placement table, zsh scaffold recipe. Activate when placing assets, creating folders, or scaffolding a new game.
effort: medium
context: fork
keywords: [project structure, folder structure, _Game, asset placement, where to put, scaffold, file layout, assets folder, scripts folder]
version: 2.5.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# TheOne Unity Project Structure

Where every folder, asset, and script belongs in a TheOne Unity mobile-game project. Any contributor (or agent) can place a new asset/script without guessing, and scaffold a new game's tree in one safe command.

## The three layers

```text
Assets/
├── <Unity-required + third-party + framework>   ← layer 1: leave at root, do NOT move
├── Scripts/                                       ← layer 2: app composition root (bootstrap/DI)
└── _Game/                                         ← layer 3: project-owned root
    ├── TheOne.<Feature>/                          ←   reusable feature CODE modules
    └── <GameName>/                                ←   game-specific ASSETS (the big tree)
```

### Layer 1 — Root folders (fixed, do not move)

| Group | Folders |
|---|---|
| Unity-required / engine | `AddressableAssetsData/`, `Editor/`, `Plugins/`, `Resources/`, `Settings/`, `URP/`, `TextMesh Pro/`, `Scenes/` |
| Third-party SDKs | `PlayFabSDK/`, `GoogleMobileAds/`, `ByteBrewSDK/`, `Importers/`, `LocalizationSetting/` |
| TheOne framework | `UITemplate/`, `TheOneFeature/`, `TheOne.FTUE/`, `VContainer/`, `Packages/` |
| LiveOps data | `Blueprints/` (Lives/Period/ProgressionReward CSV+JSON) |

### Layer 2 — `Assets/Scripts/` (composition root only)

Bootstrap and DI wiring only — not gameplay:
- `Game.asmdef`, `GameLifetimeScope.cs` (root VContainer scope)
- `Scenes/` — per-scene scopes; `Adapters/` — framework bridges; `Services/` — app services
- `link.xml` — IL2CPP strip preservation

### Layer 3 — `Assets/_Game/` (project-owned)

**a) Feature CODE modules** — `_Game/TheOne.<Feature>/Scripts/` + `Scripts/DI/` (one asmdef pair each)

**b) Game ASSET tree** — `_Game/<GameName>/` top-level skeleton (fixed org-wide convention):

`Animations/` `Art/2D/` `Audio/` `Blueprints/` `Prefabs/` `Scenes/` `ScriptableObjects/` `Scripts/` `Settings/` `Shaders/` `VFX/` `_Incoming/`

Leaf folders under `Art/2D/…`, `ScriptableObjects/…`, and `VFX/…` are game-specific — see the game's wiki Asset-Pipeline page.

## "Where does this go?" — placement table

| You have… | Put it in |
|---|---|
| 2D sprite / texture / atlas | `_Game/<Game>/Art/2D/<category>/` |
| Animation clip / controller | `_Game/<Game>/Animations/Clips/` or `Controllers/` |
| Audio file | `_Game/<Game>/Audio/{BGM,SFX,Ambient,VO}/` |
| Prefab | `_Game/<Game>/Prefabs/{UI,World,Cameras,VFX}/` |
| Scene | `_Game/<Game>/Scenes/` |
| ScriptableObject asset | `_Game/<Game>/ScriptableObjects/<family>/` |
| VFX / Shader | `_Game/<Game>/VFX/` or `Shaders/` |
| Gameplay script (Mono/DOTS) | `_Game/<Game>/Scripts/{Runtime,Editor}/` |
| Reusable feature module | `_Game/TheOne.<Feature>/Scripts/` (+ `DI/`) |
| App bootstrap / DI scope | `Assets/Scripts/` |
| Game data table (SO/CSV) | `_Game/<Game>/Blueprints/` |
| LiveOps data | `Assets/Blueprints/` (root) |
| Raw artist drop | `_Game/<Game>/_Incoming/` (gitignored) |
| DOTS gameplay engine code | `Packages/unity-dots-library/…` — NOT Assets/ |
| Third-party SDK | `Assets/` root — leave untouched |

## Scaffolding a new game's tree

Full zsh-safe recipe: `references/scaffold-recipe.md`

Quick note: set `GAME="MyGame"`, run from `Assets/_Game/`, the recipe creates `.gitkeep` placeholders. Unity generates `.meta` on next open.

## Gotchas

- **zsh does NOT word-split unquoted variables.** `for d in $DIRS` runs once — whole string as one token → garbage folder with embedded newlines. Use `while IFS= read -r d` (see scaffold-recipe.md). Real incident confirmed.
- **Don't hand-write folder `.meta` files.** Let Unity generate GUIDs. Commit `.gitkeep`; Unity meta-izes on next open.
- **The `Assets/` root tree is fixed.** Asset-import validators reject assets outside the canonical tree. Scaffold first, then drop assets.
- **DOTS code is NOT a `_Game/` asset.** ECS systems/components → `Packages/unity-dots-library`. Only game-specific adapters/authoring belong under `_Game/<Game>/Scripts/`.
- **`_Game/TheOne.<Feature>/` ≠ `_Game/<GameName>/`.** Do not nest feature modules under the game folder.

## Related

- `t1k-unity-base-asset-import` — AssetImportProcessor that promotes `_Incoming/` drops
- `t1k-unity-base-code-conventions` — C# naming/style for scripts placed here
- `t1k-unity-architecture-assembly-graph` — asmdef layering across modules
- Game wiki **Asset-Pipeline → Folder Structure Standard** — game-specific leaf folders
