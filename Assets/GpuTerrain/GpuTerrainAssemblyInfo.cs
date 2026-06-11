// Assembly-level attributes for GpuTerrain.
// Grants test visibility to internal members so EditMode tests can use
// TerrainTileLoader.EnqueueDirect and GpuTerrainEngine.TileOriginWS
// without making them public.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GpuTerrain.EditorTests")]
