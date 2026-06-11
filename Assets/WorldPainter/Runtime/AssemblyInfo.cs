// Exposes internal WorldPainter runtime types/members to editor + test assemblies.
// Consolidated from GrassInteract/Runtime/AssemblyInfo.cs and GpuTerrainAssemblyInfo.cs.
// Both runtime assemblies are merged into WorldPainter; all internal grants live here.
// Editor/test assemblies are still named GpuTerrain.Editor / GrassInteract.Editor in P3
// and will be renamed to WorldPainter.Editor / WorldPainter.Tests in P4/P5.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GpuTerrain.Editor")]
[assembly: InternalsVisibleTo("GrassInteract.Editor")]
[assembly: InternalsVisibleTo("GpuTerrain.EditorTests")]
[assembly: InternalsVisibleTo("GrassInteract.EditorTests")]
