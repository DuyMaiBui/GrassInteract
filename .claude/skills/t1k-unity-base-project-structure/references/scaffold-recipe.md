---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Scaffold Recipe — zsh-safe new-game tree

Run from `Assets/_Game/`. Creates the full tree with `.gitkeep` so empty folders commit; Unity generates `.meta` on next open.

```bash
GAME="MyGame"     # <-- set this
cd Assets/_Game
while IFS= read -r d; do
  [ -z "$d" ] && continue
  mkdir -p "$GAME/$d"
  [ "$d" != "_Incoming" ] && touch "$GAME/$d/.gitkeep"
done <<'EOF'
_Incoming
Animations/Clips/Cinematic
Animations/Controllers
Art/2D/Characters/Hero
Art/2D/Characters/Enemies
Art/2D/Characters/Bosses
Art/2D/Environment
Art/2D/Items/Weapons
Art/2D/Items/Armor
Art/2D/Items/Accessories
Art/2D/Mounts
Art/2D/Pets
Art/2D/Realms
Art/2D/Skills
Art/2D/UI/Icons
Art/2D/UI/Sprites
Art/2D/UI/Atlases
Audio/BGM
Audio/SFX
Audio/Ambient
Audio/VO
Blueprints
Prefabs/UI
Prefabs/World
Prefabs/Cameras
Prefabs/VFX
Scenes/Realms
Scenes/Dungeons
ScriptableObjects/Realms
ScriptableObjects/CombatFeel
ScriptableObjects/Balance
Scripts/Runtime
Scripts/Editor/ImportPresets
Settings
Shaders
VFX/Combat
VFX/UI
VFX/Ambient
VFX/Forge
EOF
```

Then gitignore the staging contents (keep the folder):

```gitignore
Assets/_Game/<GameName>/_Incoming/*
!Assets/_Game/<GameName>/_Incoming/.gitkeep
```

## zsh gotcha

`for d in $DIRS` over a multi-line variable runs the loop **once** — the entire string is one token, `mkdir` creates a single folder with embedded newlines. **Always** use `while IFS= read -r d; do … done <<'EOF'` (shown above), or an array, or `${=DIRS}`.
