#nullable enable
using GPUGrass.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GPUGrass.Demo.Editor
{
    /// <summary>
    /// One-click demo builder: generates a self-contained GPUGrass demo scene from scratch — a procedural
    /// Terrain with a painted grass detail layer, the GPUGrass controller auto-set-up on it, and an
    /// orbiting interactor (+ trail) so the interactive bend is immediately visible. Saves the scene +
    /// generated assets under <c>Assets/GPUGrass/Demo/</c>. No hand-painting required.
    /// </summary>
    public static class GpuGrassDemoBuilder
    {
        private const string DEMO_FOLDER     = "Assets/GPUGrass/Demo";
        private const string GENERATED_FOLDER = "Assets/GPUGrass/Demo/Generated";
        private const string SCENE_PATH      = "Assets/GPUGrass/Demo/GpuGrassDemo.unity";

        private const int   HEIGHTMAP_RES   = 257;   // 2^n + 1
        private const int   DETAIL_RES      = 128;
        private const int   DETAIL_PER_PATCH = 16;
        private const float FIELD_SIZE      = 100f;  // metres (X & Z)
        private const float FIELD_HEIGHT    = 12f;   // metres (Y)

        [MenuItem("Tools/GPUGrass/Build Demo Scene", false, 20)]
        public static void BuildDemoScene()
        {
            EnsureFolders();

            // 1) Fresh scene with the default light + camera, then we add our own framing camera.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 2) Procedural terrain data (gentle rolling height) → saved as an asset (scenes can't embed it).
            TerrainData data = CreateTerrainData();
            AssetDatabase.CreateAsset(data, $"{GENERATED_FOLDER}/GpuGrassDemoTerrain.asset");

            // 3) Grass detail prototype from a generated blade texture, painted across the field.
            Texture2D detailTex = CreateBladeTexture();
            AssetDatabase.CreateAsset(detailTex, $"{GENERATED_FOLDER}/GpuGrassDemoBlade.asset");
            PaintGrass(data, detailTex);

            // 4) Terrain GameObject.
            GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "GpuGrassDemoTerrain";
            terrainGo.transform.position = Vector3.zero;
            var terrain = terrainGo.GetComponent<Terrain>();

            // 5) Frame the camera on the field.
            FrameCamera();

            // 6) Auto-setup GPUGrass on the terrain (wires assets, bakes the painted detail, rebuilds).
            //    Ensure (or load) the scene-shared config, then wire it to the terrain.
            GpuGrassConfig sharedConfig = GpuGrassSceneSetup.EnsureSharedConfig();
            int blades = GpuGrassAutoSetup.SetupOnTerrain(terrain, sharedConfig);

            // 7) Orbiting interactor + trail so the bend is visible immediately.
            Vector3 fieldCenter = new(FIELD_SIZE * 0.5f, 0f, FIELD_SIZE * 0.5f);
            CreateMover(fieldCenter, terrain);

            // 8) Save.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GPUGrass] Demo scene built at '{SCENE_PATH}' — {blades} grass blades baked. " +
                      "Press Play (or watch the Scene view) to see the orbiting interactor bend the grass.");
            EditorUtility.DisplayDialog("GPUGrass",
                $"Demo scene built:\n{SCENE_PATH}\n\n{blades} blades baked. Enter Play mode to see it.", "OK");
        }

        private static TerrainData CreateTerrainData()
        {
            var data = new TerrainData { heightmapResolution = HEIGHTMAP_RES };
            data.size = new Vector3(FIELD_SIZE, FIELD_HEIGHT, FIELD_SIZE);

            int res = data.heightmapResolution;
            var heights = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1);
                float v = (float)z / (res - 1);
                // Gentle rolling hills (low amplitude so the grass field stays mostly walkable).
                heights[z, x] = 0.12f * Mathf.PerlinNoise(u * 3f, v * 3f)
                              + 0.04f * Mathf.PerlinNoise(u * 9f, v * 9f);
            }
            data.SetHeights(0, 0, heights);
            return data;
        }

        private static Texture2D CreateBladeTexture()
        {
            const int W = 32, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "GpuGrassDemoBlade" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float t = (float)y / (H - 1);                  // 0 base → 1 tip
                float halfBlade = Mathf.Lerp(0.42f, 0.04f, t); // taper toward the tip
                Color32 col = Color32.Lerp(new Color32(40, 110, 30, 255), new Color32(120, 200, 70, 255), t);
                for (int x = 0; x < W; x++)
                {
                    float cx = (x / (float)(W - 1)) - 0.5f;     // -0.5..0.5
                    bool inside = Mathf.Abs(cx) <= halfBlade;
                    px[y * W + x] = inside ? col : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        private static void PaintGrass(TerrainData data, Texture2D detailTex)
        {
            data.SetDetailResolution(DETAIL_RES, DETAIL_PER_PATCH);

            var proto = new DetailPrototype
            {
                prototypeTexture = detailTex,
                usePrototypeMesh = false,
                renderMode       = DetailRenderMode.GrassBillboard,
                healthyColor     = new Color(0.45f, 0.8f, 0.3f),
                dryColor         = new Color(0.6f, 0.7f, 0.3f),
                minWidth         = 0.4f, maxWidth = 0.7f,
                minHeight        = 0.4f, maxHeight = 0.7f,
                noiseSpread      = 0.3f,
            };
            data.detailPrototypes = new[] { proto };

            int dw = data.detailWidth, dh = data.detailHeight;
            var layer = new int[dh, dw];
            // Paint a soft-edged disc of grass so the field has a clear vegetated patch + bare margins
            // (exercises the baker's coverage weighting + slope/edge handling).
            float cx = (dw - 1) * 0.5f, cz = (dh - 1) * 0.5f;
            float maxR = Mathf.Min(cx, cz);
            for (int z = 0; z < dh; z++)
            for (int x = 0; x < dw; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / maxR; // 0 centre → 1 edge
                float coverage = Mathf.Clamp01(1f - d);                                 // dense centre, bare rim
                layer[z, x] = Mathf.RoundToInt(coverage * 12f);
            }
            data.SetDetailLayer(0, 0, 0, layer);
        }

        private static void FrameCamera()
        {
            Camera? cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera));
                go.tag = "MainCamera";
                cam = go.GetComponent<Camera>();
            }
            cam.transform.position = new Vector3(FIELD_SIZE * 0.5f, 18f, -8f);
            cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
        }

        private static void CreateMover(Vector3 fieldCenter, Terrain terrain)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GrassMover";
            go.transform.localScale = Vector3.one * 2f;

            var interactor = go.AddComponent<GrassInteractor>();
            go.AddComponent<GrassTrailInteractor>();
            var mover = go.AddComponent<GpuGrassDemoMover>();
            mover.Configure(new Vector3(fieldCenter.x, 0f, fieldCenter.z), FIELD_SIZE * 0.28f, terrain);

            // Place it on the orbit start so the first frame is already over the field.
            float startX = fieldCenter.x + FIELD_SIZE * 0.28f;
            float startZ = fieldCenter.z;
            float startY = terrain.SampleHeight(new Vector3(startX, 0f, startZ)) + terrain.transform.position.y + 0.5f;
            go.transform.position = new Vector3(startX, startY, startZ);
            _ = interactor;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(DEMO_FOLDER))
                AssetDatabase.CreateFolder("Assets/GPUGrass", "Demo");
            if (!AssetDatabase.IsValidFolder(GENERATED_FOLDER))
                AssetDatabase.CreateFolder(DEMO_FOLDER, "Generated");
        }
    }
}
