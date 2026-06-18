using UnityEngine;

namespace MeshAtlas.Editor.Combine
{
    /// <summary>
    /// One MeshRenderer captured for atlasing: the world transform to bake, its mesh, and
    /// the per-submesh materials. Shared by the bake and import pipelines so renderer
    /// collection lives in exactly one place.
    /// </summary>
    public sealed class RendererClip
    {
        public Matrix4x4 Transform { get; set; } = Matrix4x4.identity;
        public Mesh Mesh { get; set; }
        public Material[] Materials { get; set; }
    }
}
