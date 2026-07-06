using System.Runtime.CompilerServices;

// Lets GPUGrass.Tests call internal editor members directly (e.g. GpuGrassSceneWindow.ApplyMobilePreset)
// without a live editor window or reflection. Mirrors Runtime/AssemblyInfo.cs.
[assembly: InternalsVisibleTo("GPUGrass.Tests")]
