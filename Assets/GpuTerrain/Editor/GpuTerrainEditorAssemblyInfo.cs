using System.Runtime.CompilerServices;

// Expose editor-assembly internals to the EditMode test assembly so pure helpers
// (e.g. TerrainBrushPreview.CreateUnitDisc) can be unit-tested. Mirrors the runtime
// GpuTerrainAssemblyInfo pattern.
[assembly: InternalsVisibleTo("GpuTerrain.EditorTests")]
// Density kernel + encoder folded into GpuTerrain (P3b); DensityBrushMathTests still
// live in GrassInteract.EditorTests, so expose internals to that assembly too.
[assembly: InternalsVisibleTo("GrassInteract.EditorTests")]
