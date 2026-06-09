---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# PlaceholderSpriteUtility — DOTSCore.Editor

**File:** `com.the1studio.dots-core/Editor/Utilities/PlaceholderSpriteUtility.cs`
**Namespace:** `DOTSCore.Editor`

Shared drawing primitives and texture I/O for generating placeholder sprite PNGs.
Demo sprite generators call these methods instead of duplicating them.

---

## Texture creation

```csharp
Texture2D PlaceholderSpriteUtility.CreateClear(int size)            // square
Texture2D PlaceholderSpriteUtility.CreateClear(int width, int height) // rect
```

Creates a transparent-white `RGBA32` texture. Pass to any Draw* method below.

---

## Drawing primitives (white on existing texture)

```csharp
// Rectangle
void FillRect(Texture2D tex, int size, float x0, float y0, float x1, float y1)
void FillRect(Texture2D tex, int width, int height, float x0, float y0, float x1, float y1)

// Circle
void FillCircle(Texture2D tex, int size, float cx, float cy, float radius)
void FillCircle(Texture2D tex, int width, int height, float cx, float cy, float radius)
```

Coordinates are in texels (pixels). All shapes are drawn in white.
Apply team tint at runtime (e.g., via `MaterialPropertyBlock._BaseColor`).

---

## Shape generators (return new texture)

| Method | Shape |
|--------|-------|
| `GenerateCircle(int size)` | Filled circle |
| `GenerateDiamond(int size)` | Filled diamond (45° square) |
| `GenerateStar(int size, int points=6, float innerRatio=0.45f)` | N-point star |
| `GenerateTriangle(int size)` | Filled upward triangle |
| `GeneratePentagon(int size)` | Filled regular pentagon |
| `GenerateRectangle(int size)` | Wide rectangle (halfH = halfW × 0.5) |

All generators return a new `Texture2D` allocated on the managed heap — call
`SaveTexture` or `Object.DestroyImmediate` when done.

---

## Canonical humanoid shape draws (library-level SSOT)

Extracted from `BattleDemoSideView`'s sprite generator. Coordinates scale
proportionally with `size` so any power-of-two size works.

```csharp
void DrawMeleeHumanoid(Texture2D tex, int size)   // stride legs, sword arm extended
void DrawRangerHumanoid(Texture2D tex, int size)  // bow drawn on front arm
void DrawMageHumanoid(Texture2D tex, int size)    // robe, pointy hat, staff
void DrawBossHumanoid(Texture2D tex, int size)    // wide armored body, horns, weapon
void DrawArrow(Texture2D tex, int size)           // horizontal arrow pointing right
```

**Canonical usage pattern (128×128 Stickman sprite):**

```csharp
[MenuItem("Tools/MyDemo/Generate Stickman Sprites")]
public static void Generate()
{
    string folder = "Assets/Demos/RPG/MyDemo/Sprites/";
    PlaceholderSpriteUtility.EnsureFolder(folder);

    var tex = PlaceholderSpriteUtility.CreateClear(128);
    PlaceholderSpriteUtility.DrawMeleeHumanoid(tex, 128);
    tex.Apply();
    PlaceholderSpriteUtility.SaveTexture(tex, folder, "Stickman_Melee");

    AssetDatabase.Refresh();
    PlaceholderSpriteUtility.ConfigureSpriteImportSettings(folder,
        new[] { "Stickman_Melee", "Stickman_Ranger", "Stickman_Mage", "Stickman_Boss" });
}
```

### Demo-override policy

- `BattleDemoSideView` — uses the library methods above (canonical source).
- `BattleDemoIso` — keeps its own foreshortened, front-facing overrides (different
  proportions, unit size=64). This is intentional per library charter (perspective
  demos may own shape variants when proportions genuinely differ).

---

## I/O helpers

```csharp
// Encode to PNG, write to disk, destroy the Texture2D
void SaveTexture(Texture2D tex, string folder, string name)
// folder must end with '/'; name has no extension

// Apply standard import settings (Sprite, point filter, no compression)
void ConfigureSpriteImportSettings(string folder, string[] spriteNames)

// Create a folder if it doesn't exist (AssetDatabase-aware)
void EnsureFolder(string folder)
```

---

## Geometry helpers (public — reusable in custom generators)

```csharp
bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
bool PointInConvexPolygon(Vector2 p, Vector2[] verts)
float CrossSign(Vector2 p1, Vector2 p2, Vector2 p3)
```

---

## Gotchas

- `SaveTexture` calls `Object.DestroyImmediate(tex)` — do NOT use `tex` after calling it.
- `ConfigureSpriteImportSettings` silently skips missing files; call AFTER `AssetDatabase.Refresh()`.
- All Draw* methods write white pixels — runtime tinting is the caller's responsibility.
- `DrawArrow` scales relative to `size/32f`; designed for 32px arrows (upscales cleanly to 64/128).
- `EnsureFolder` uses `Directory.CreateDirectory`, NOT `AssetDatabase.CreateFolder` — both work but produce slightly different meta GUIDs; prefer this one for consistency.
