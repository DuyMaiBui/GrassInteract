#nullable enable
using GPUGrass.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassBaker.Bake"/> against a synthetic flat Terrain with a painted
    /// detail layer. Verifies blade placement, in-bounds positions, slope masking, and the empty-detail case.
    /// </summary>
    public sealed class BakerTests
    {
        private const float SIZE = 30f, HEIGHT = 5f;

        private Terrain? terrain;
        private TerrainData? data;
        private Texture2D? tex;

        [SetUp]
        public void SetUp()
        {
            this.data = new TerrainData { heightmapResolution = 33 };
            this.data.size = new Vector3(SIZE, HEIGHT, SIZE); // flat (default zero heights → all-up normals)

            this.tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var prototype = new DetailPrototype
            {
                prototypeTexture = this.tex,
                usePrototypeMesh = false,
                renderMode       = DetailRenderMode.GrassBillboard,
            };
            this.data.detailPrototypes = new[] { prototype };
            this.data.SetDetailResolution(32, 16);

            var go = Terrain.CreateTerrainGameObject(this.data);
            go.transform.position = Vector3.zero;
            this.terrain = go.GetComponent<Terrain>();
        }

        [TearDown]
        public void TearDown()
        {
            if (this.terrain != null) Object.DestroyImmediate(this.terrain.gameObject);
            if (this.data != null) Object.DestroyImmediate(this.data);
            if (this.tex != null) Object.DestroyImmediate(this.tex);
        }

        private void PaintFull(int density)
        {
            int dw = this.data!.detailWidth, dh = this.data.detailHeight;
            var layer = new int[dh, dw];
            for (int z = 0; z < dh; z++)
            for (int x = 0; x < dw; x++)
                layer[z, x] = density;
            this.data.SetDetailLayer(0, 0, 0, layer);
        }

        private static GpuGrassConfig DefaultConfig()
        {
            // Defaults: targetDensityPerSqM 0.76, scaleRange (0.8,1.2), slopeRange (0,60), bladeHeight (0.3,0.6).
            return ScriptableObject.CreateInstance<GpuGrassConfig>();
        }

        [Test]
        public void Bake_PlacesBladesWithinTerrainBounds()
        {
            PaintFull(8);
            var config = DefaultConfig();
            var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int count = GpuGrassBaker.Bake(this.terrain!, config, bake);

                Assert.Greater(count, 0, "A fully-painted flat field within the slope band must produce blades.");
                Assert.AreEqual(count, bake.InstanceCount);

                foreach (Vector3 p in bake.Positions)
                {
                    Assert.GreaterOrEqual(p.x, -0.01f); Assert.LessOrEqual(p.x, SIZE + 0.01f);
                    Assert.GreaterOrEqual(p.z, -0.01f); Assert.LessOrEqual(p.z, SIZE + 0.01f);
                    Assert.GreaterOrEqual(p.y, -0.01f); Assert.LessOrEqual(p.y, HEIGHT + 0.01f);
                }
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(bake); }
        }

        [Test]
        public void Bake_SlopeMaskExcludesFlatGroundOutsideTheBand()
        {
            PaintFull(8);
            var config = DefaultConfig();
            // Restrict to steep slopes only — a flat terrain (slope ≈ 0) must then yield no blades.
            var so = new SerializedObject(config);
            so.FindProperty("slopeRange").vector2Value = new Vector2(80f, 90f);
            so.ApplyModifiedPropertiesWithoutUndo();

            var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int count = GpuGrassBaker.Bake(this.terrain!, config, bake);
                Assert.AreEqual(0, count, "Flat ground (slope ≈ 0) must be excluded by an 80–90° slope band.");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(bake); }
        }

        [Test]
        public void Bake_NoPaintedDetailProducesNoBlades()
        {
            PaintFull(0); // all-zero density
            var config = DefaultConfig();
            var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int count = GpuGrassBaker.Bake(this.terrain!, config, bake);
                Assert.AreEqual(0, count, "Unpainted detail must bake zero blades.");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(bake); }
        }

        [Test]
        public void Bake_TerrainSurface_ScattersWithoutPaintedDetail()
        {
            // No PaintFull() call → zero painted detail. Surface mode must still scatter from the mesh.
            var config = DefaultConfig();
            var so = new SerializedObject(config);
            so.FindProperty("placementSource").enumValueIndex = (int)GrassPlacementSource.TerrainSurface;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int count = GpuGrassBaker.Bake(this.terrain!, config, bake);
                Assert.Greater(count, 0, "TerrainSurface mode must scatter over the mesh with no painted detail.");
                Assert.AreEqual(count, bake.InstanceCount);
                foreach (Vector3 p in bake.Positions)
                {
                    Assert.GreaterOrEqual(p.x, -0.01f); Assert.LessOrEqual(p.x, SIZE + 0.01f);
                    Assert.GreaterOrEqual(p.z, -0.01f); Assert.LessOrEqual(p.z, SIZE + 0.01f);
                }
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(bake); }
        }

        [Test]
        public void Bake_TerrainSurface_AltitudeMaskExcludesOutsideBand()
        {
            // Flat terrain → every sample sits at normalized height 0; a (0.5,1.0) band excludes all.
            var config = DefaultConfig();
            var so = new SerializedObject(config);
            so.FindProperty("placementSource").enumValueIndex = (int)GrassPlacementSource.TerrainSurface;
            so.FindProperty("heightRange01").vector2Value = new Vector2(0.5f, 1.0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int count = GpuGrassBaker.Bake(this.terrain!, config, bake);
                Assert.AreEqual(0, count, "Flat ground (normalized height 0) must be excluded by a 0.5–1.0 altitude band.");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(bake); }
        }

        [Test]
        public void Bake_IsDeterministicForTheSameSeed()
        {
            PaintFull(8);
            var config = DefaultConfig();
            var a = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            var b = ScriptableObject.CreateInstance<GpuGrassBakeData>();
            try
            {
                int ca = GpuGrassBaker.Bake(this.terrain!, config, a);
                int cb = GpuGrassBaker.Bake(this.terrain!, config, b);
                Assert.AreEqual(ca, cb, "Same seed + same terrain → identical blade count.");
                Assert.AreEqual(a.InstanceCount, b.InstanceCount);
                if (a.InstanceCount > 0)
                    Assert.AreEqual(a.Positions[0], b.Positions[0], "Deterministic placement expected.");
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
