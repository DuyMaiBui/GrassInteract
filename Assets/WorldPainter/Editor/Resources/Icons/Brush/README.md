# BrushDock tool icons

Drop 16×16 (or 32×32 HiDPI) PNGs here with the filenames the `BrushIcons` helper looks
for. Missing icons are fine — the BrushDock tool buttons fall back to text-only.

## Expected filenames

Per tool id (the part after the dot), with a `_d` suffix variant for pro/dark skin:

| Tool id           | Light skin   | Dark skin     |
|-------------------|--------------|---------------|
| `density.paint`   | `paint.png`  | `paint_d.png` |
| `density.erase`   | `erase.png`  | `erase_d.png` |
| `density.smooth`  | `smooth.png` | `smooth_d.png`|
| `height.raise`    | `raise.png`  | `raise_d.png` |
| `height.lower`    | `lower.png`  | `lower_d.png` |
| `height.smooth`   | `smooth.png` | `smooth_d.png`|
| `height.flatten`  | `flatten.png`| `flatten_d.png`|
| `splat.paint`     | `paint.png`  | `paint_d.png` |
| `splat.erase`     | `erase.png`  | `erase_d.png` |
| `instance.place`  | `place.png`  | `place_d.png` |
| `instance.erase`  | `erase.png`  | `erase_d.png` |
| `instance.single` | `single.png` | `single_d.png`|

If only one variant is authored, both skins fall back to it.

## Importer settings

- Texture Type: Default (or Editor GUI)
- sRGB: ON
- Alpha is Transparency: ON
- Mip Maps: OFF
- Wrap Mode: Clamp
- Filter Mode: Bilinear
- Read/Write: not required

After authoring, call `BrushIcons.Invalidate()` once or restart the editor; the helper
caches lookups per session.
