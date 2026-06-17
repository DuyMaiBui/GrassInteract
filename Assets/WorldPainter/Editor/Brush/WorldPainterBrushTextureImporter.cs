#nullable enable
#if UNITY_EDITOR
using UnityEditor;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Configures brush-mask textures at IMPORT time so selecting a brush never reimports.
    ///
    /// The stamp compute kernel samples the mask with <c>_BrushMask.Load(...)</c>
    /// (<c>Assets/WorldPainter/Shaders/BrushMask.hlsl</c>), which requires an uncompressed,
    /// linear texture — block-compressed formats are not reliably <c>Load</c>-able across
    /// platforms and an sRGB curve would skew the <c>.r</c> mask weight. Previously the brush
    /// dock forced these settings via <c>SaveAndReimport</c> on every first selection of a brush,
    /// which synchronously re-entered <see cref="WorldPainterImportRebuildHook"/> and triggered a
    /// full terrain <c>TryBuild()</c> + scatter rebuild — the "rebuild on brush change" annoyance.
    ///
    /// Doing it here, in <see cref="OnPreprocessTexture"/>, means the settings are applied as part
    /// of the texture's normal import (when it is added to / changed in the Brushes folder). Brush
    /// selection then stays pure UI: no AssetDatabase write, no reimport, no rebuild.
    ///
    /// To apply to brushes that were imported before this postprocessor existed, reimport the
    /// Brushes folder once (right-click → Reimport).
    /// </summary>
    internal sealed class WorldPainterBrushTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            // assetPath uses forward slashes on every platform; only touch the Brushes folder.
            if (!this.assetPath.StartsWith(
                    WorldPainterBrushDock.BRUSH_FOLDER + "/", System.StringComparison.Ordinal))
                return;

            if (this.assetImporter is not TextureImporter importer) return;

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable         = true;
            importer.sRGBTexture        = false;
        }
    }
}
#endif
