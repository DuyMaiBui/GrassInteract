# Phase 3 — Combine + asset emission

Effort: M · Blocks: P4 · Blocked by: P1 (UVRemapper), P2 (atlas textures)

## Goal

Implement `MeshCombiner` (remap UV0 per mesh into atlas sub-rects via P1's `UVRemapper`, then `Mesh.CombineMeshes` into one mesh / one submesh) and `AtlasAssetWriter` (write the combined mesh asset, the 4 atlas PNGs with correct importer settings, the single URP-Lit material wired to those atlases, and a ready-to-drop prefab).

## File ownership (exact paths)

- `Assets/MeshAtlas/Editor/Combine/MeshCombiner.cs`
- `Assets/MeshAtlas/Editor/Output/AtlasAssetWriter.cs`
- `Assets/MeshAtlas/Editor/Output/UrpLitMaterialFactory.cs` (builds + wires the URP-Lit material from the 4 atlases)

## Implementation notes

- **`MeshCombiner`:**
  - For each source mesh, rewrite UV0 with `UVRemapper.Remap(uv, subRectForItsMaterial)` BEFORE combine.
  - Set `combined.indexFormat = IndexFormat.UInt32` before `CombineMeshes` (avoids 16-bit overflow on large selections — see risk register).
  - `Mesh.CombineMeshes(instances, mergeSubMeshes: true, useMatrices: true)` → single submesh. Bake world transforms via each renderer's `localToWorldMatrix` (or relative to a chosen pivot) so combined geometry sits correctly.
  - Recalculate bounds; keep normals/tangents from sources (do NOT recompute normals — preserves shading).
- **`AtlasAssetWriter`:**
  - Write the 4 atlas `Texture2D`s as PNG (`EncodeToPNG`) to the user-chosen output folder, then `AssetDatabase.ImportAsset` and set importer settings per channel: normal map → `TextureImporterType.NormalMap`; albedo/emission → sRGB on; mask/normal → sRGB off. Disable `mipmapEnabled` only if requested (default keep mips — dilation from P2 makes mips safe).
  - Write `CombinedMesh.asset` via `AssetDatabase.CreateAsset`.
  - Build the URP-Lit material (`UrpLitMaterialFactory`): shader `Universal Render Pipeline/Lit`, assign `_BaseMap`, `_BumpMap` (+ enable `_NORMALMAP`), `_MetallicGlossMap` (+ `_METALLICSPECGLOSSMAP`), `_EmissionMap` (+ enable emission) — only for channels the user enabled. Because scalars were folded into pixels (P2), set material `_BaseColor=white`, `_Metallic`/`_Smoothness` neutral so the baked values aren't double-applied.
  - Build the prefab: a GameObject with `MeshFilter`(combined mesh) + `MeshRenderer`(the new material), saved via `PrefabUtility.SaveAsPrefabAsset` to the output folder.
- Generic names throughout — no project tokens. Constants for default output subfolder name.

## Success criteria

- Combined mesh = 1 submesh, UV0 remapped, `UInt32` index format, bounds correct.
- 4 PNGs written with correct importer settings (normal flagged `NormalMap`, sRGB correct per channel).
- URP-Lit material renders the combined mesh identically to the originals (tint/metal/smooth preserved because folded + neutral material values).
- Prefab exists in the output folder and instantiates with the combined mesh + material.

## Verification step

In-editor sample bake (GPU/asset I/O — not batch-unit-testable):
1. Run combine + write on the P2 sample output.
2. Inspect: combined mesh submesh count = 1; material has the 4 maps wired with correct keywords; prefab drops into a scene and renders.
3. `read_console` clean.
4. Pure-C# slice (UV0 rewrite correctness) is already covered by P1's `UVRemapperTests`; `MeshCombiner`'s index-format + submesh-merge is verified by inspecting the produced mesh in the sample bake.

## Rollback

Delete `Assets/MeshAtlas/Editor/Combine/` + `Assets/MeshAtlas/Editor/Output/`. P1/P2 unaffected; P4 not yet built.
