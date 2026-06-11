// Exposes internal WorldPainter.Editor types/members to the EditMode test assembly
// so pure editor helpers can be unit-tested without making them public.
// Consolidated from GpuTerrainEditorAssemblyInfo.cs + GrassInteract/Editor/EditorAssemblyInfo.cs.
// Test assemblies are still named GpuTerrain.EditorTests / GrassInteract.EditorTests in P4
// and merge into WorldPainter.Tests in P5 — all three are granted (extra grants are harmless).
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WorldPainter.Tests")]
