// Assembly-level attributes for GpuTerrain.
// Grants test visibility to internal members so EditMode tests can use
// TerrainTileLoader.EnqueueDirect and GpuTerrainEngine.TileOriginWS
// without making them public.
// Also grants Editor assembly access to the sculpt seam on GpuTerrainRenderer/Engine.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GpuTerrain.EditorTests")]
[assembly: InternalsVisibleTo("GpuTerrain.Editor")]
