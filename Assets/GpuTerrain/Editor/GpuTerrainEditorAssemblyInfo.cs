using System.Runtime.CompilerServices;

// Expose editor-assembly internals to the EditMode test assembly so pure helpers
// (e.g. TerrainBrushPreview.CreateUnitDisc) can be unit-tested. Mirrors the runtime
// GpuTerrainAssemblyInfo pattern.
[assembly: InternalsVisibleTo("GpuTerrain.EditorTests")]
