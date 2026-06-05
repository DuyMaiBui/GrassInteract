---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Unity Project Folder Structure — `_Game/<GameName>/` Convention

Org-wide convention for The1Studio Unity projects. Verified across 6 of 8 sibling
projects (Arrow3D, FoodieSizzle, Hear_N_Hand, PuppyGo, TheOneFeatureProject,
DOTSTemplate). ColorFit uses `0.Game/` and Screw3D uses a project-named root — both
are deviations that should migrate to this pattern.

## Rule

`Assets/_Game/<GameName>/` is the project-owned root. Everything game-specific —
Scripts, Prefabs, Audio, Sprites, Scenes, Blueprints, ScriptableObjects, Animations,
VFX, Shaders — nests inside. Unity-required folders and third-party SDKs stay at
`Assets/` root.

## Canonical layout

```text
Assets/
├── _Game/
│   ├── <GameName>/                     ← project-owned module
│   │   ├── _Incoming/                  ← gitignored staging — artists drop raw files
│   │   ├── Animations/{Clips,Controllers}/
│   │   ├── Art/{2D,3D}/                ← omit 3D for 2D-only games
│   │   ├── Audio/{BGM,SFX,Ambient,VO}/
│   │   ├── Blueprints/                 ← Soft.Generic.Blueprints SO data
│   │   ├── Prefabs/{UI,World,Cameras,VFX}/
│   │   ├── Scenes/                     ← Bootstrap.unity + per-area subfolders
│   │   ├── ScriptableObjects/          ← SO_* assets
│   │   ├── Scripts/                    ← C# source (asmdef-rooted)
│   │   ├── Settings/                   ← Input actions, PhysicsLayers.cs
│   │   ├── Shaders/                    ← project-owned HLSL / Shader Graph
│   │   └── VFX/                        ← .vfx assets + Shuriken prefabs
│   ├── PuzzleGame/                     ← shared template module (if applicable)
│   ├── TheOne.Advertisement/           ← shared kit module
│   └── TheOne.RemoteConfig/            ← shared kit module
├── AddressableAssetsData/              ← Unity-required at Assets/ root
├── Editor/                             ← Unity-required at root
├── Plugins/                            ← Unity-required at root
├── Resources/                          ← Unity-required at root
├── Settings/                           ← URP asset + render-pipeline configs
└── PlayFabSDK/, GoogleMobileAds/, ByteBrewSDK/   ← third-party SDKs at root
```

## Minimal core vs rich-art tiers

The minimal core — Audio, Blueprints, Prefabs, Scenes, Scripts, Sprites — is present
in 100% of surveyed `_Game/<GameName>/` modules. Rich-art / DOTS games extend with
Animations, Materials, Shaders, Textures, FBX, VFX.

For a new game, scaffold the minimal core plus only the folders needed on day one. Add
Animations/, Shaders/, VFX/ when the first asset of that type actually lands. Do not
pre-create empty folders.

## What NOT to put under `_Game/<GameName>/`

| Folder | Correct location | Reason |
|--------|-----------------|--------|
| `AddressableAssetsData/` | `Assets/` root | Unity importer expectation |
| `Resources/` | `Assets/` root | Unity runtime loading requirement |
| `Editor/` | `Assets/` root | Unity editor assembly requirement |
| `Plugins/` | `Assets/` root | Native/managed plugin resolution |
| `Settings/` (URP, HDRP) | `Assets/` root | Render pipeline pipeline config |
| `PlayFabSDK/`, `GoogleMobileAds/` | `Assets/` root | SDK relative-path expectations |
| `google-services.json` | `Assets/` root | Build pipeline expectation |

## Naming the root prefix

`_Game/` (underscore prefix) sorts to top in Unity's Project window — this is the
preferred pattern. Known deviations in the org:

- `0.Game/` (ColorFit) — achieves the same sort-to-top effect but deviates from org
  pattern; migrate when refactoring.
- Project root directly under `Assets/` without a `_Game/` wrapper (Screw3D) —
  deviates; migrate to `_Game/<GameName>/`.

## Related

- Asset naming prefixes: `T_/S_/M_/SFX_/BGM_/VO_/VFX_/Anim_/Ctrl_/SO_/Prefab_/Vcam_/Sh_`
  table — see `naming-prefix-conventions.md` in the designer wiki-core skill.
- Per-game asset pipeline elaboration — see consumer wiki (e.g. `StickManForge-Asset-Pipeline.md`).
- Addressable group registration — see `t1k-unity-base-addressables` skill.
