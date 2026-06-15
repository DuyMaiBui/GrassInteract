#nullable enable
namespace WorldPainter.Editor
{
    /// <summary>
    /// Ambient flag that tells <see cref="GrassLayerEditor"/> / <see cref="PropLayerEditor"/>
    /// whether they are being drawn inside the WorldPainter component's detail card (editable)
    /// or as the standalone sub-asset inspector (read-only).
    ///
    /// The WorldPainter inspector sets this <c>true</c> around the embedded
    /// <c>OnInspectorGUI()</c> call and resets it afterwards, so grass/prop layer properties can
    /// only be edited through the WorldPainter UI — selecting the sub-asset directly shows the
    /// same fields greyed-out.
    /// </summary>
    internal static class WorldPainterLayerEditContext
    {
        /// <summary>True while a layer editor is rendered inside the WorldPainter detail card.</summary>
        public static bool EditingInWorldPainter;
    }
}
