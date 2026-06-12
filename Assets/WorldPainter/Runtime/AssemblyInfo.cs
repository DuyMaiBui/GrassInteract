// Exposes internal WorldPainter runtime types/members to editor + test assemblies.
// Consolidated from WorldPainter/Runtime/AssemblyInfo.cs and GpuTerrainAssemblyInfo.cs.
// Both runtime assemblies are merged into WorldPainter; all internal grants live here.
// Editor/test assemblies are still named WorldPainter.Editor / WorldPainter.Editor in P3
// and will be renamed to WorldPainter.Editor / WorldPainter.Tests in P4/P5.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WorldPainter.Editor")]
[assembly: InternalsVisibleTo("WorldPainter.Tests")]
