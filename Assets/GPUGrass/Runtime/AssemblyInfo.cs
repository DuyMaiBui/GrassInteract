using System.Runtime.CompilerServices;

// Exposes internal types (GpuGrassChunkedBuffer + its partition, the blittable GPU structs, and
// GpuGrassRenderer.LodThresholds) to the EditMode test assembly so the CPU-side logic can be
// unit-tested without a GPU/Play context. No effect on player builds.
[assembly: InternalsVisibleTo("GPUGrass.Tests")]
