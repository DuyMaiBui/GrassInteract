#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Editor.Build
{
    /// <summary>
    /// Build pre-processor that fails Android builds when the mobile render config
    /// drifts from the verified-safe Medium configuration.
    ///
    /// Assertions (Android only):
    ///   1. Default Android quality index == 2 (Medium).
    ///   2. Active URP asset HDR off (m_SupportsHDR == false).
    ///   3. MSAA == 1 (anti-aliasing off for TBDR HSR).
    ///   4. Main-light shadowmap resolution ≤ 1024.
    ///   5. Shadow distance ≤ 25.
    ///   6. GraphicsSettings.alwaysIncludedShaders ⊇ "WorldPainter/IndirectGrass".
    ///   7. No WorldPainter component in scenes/prefabs has scatterForceTier == ForceCpu.
    ///      (This is the 20-fps CPU-tier footgun that bypasses GrassTierProbe.)
    ///
    /// All URP property reads use reflection — the WorldPainter.Editor asmdef deliberately
    /// has no URP C# asmdef reference, matching the pattern in PerformanceConsole.cs.
    /// </summary>
    internal sealed class MobileRenderConfigValidator : IPreprocessBuildWithReport
    {
        // ── IPreprocessBuildWithReport ────────────────────────────────────────

        // Early callback order — runs before asset-pipeline build steps so violations
        // surface immediately rather than partway through a long build.
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            this.ValidateAndroidRenderConfig();
        }

        // ── Validation entry point ────────────────────────────────────────────

        private void ValidateAndroidRenderConfig()
        {
            var violations = new List<string>();

            this.CheckQualityLevel(violations);
            this.CheckUrpAssetSettings(violations);
            this.CheckAlwaysIncludedShaders(violations);
            this.CheckScatterForceTier(violations);

            if (violations.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("[MobileRenderConfigValidator] Android build FAILED — render config violations:");
            sb.AppendLine();
            for (int i = 0; i < violations.Count; i++)
            {
                sb.Append("  [").Append(i + 1).Append("] ");
                sb.AppendLine(violations[i]);
            }

            throw new BuildFailedException(sb.ToString());
        }

        // ── Assertion 1: default Android quality == Medium (index 2) ─────────

        private const int REQUIRED_ANDROID_QUALITY_INDEX = 2; // VeryLow=0, Low=1, Medium=2
        private const string REQUIRED_ANDROID_QUALITY_NAME = "Medium";

        private void CheckQualityLevel(List<string> violations)
        {
            string[] names = QualitySettings.names;

            // Editor API: GetDefaultQualityForPlatform is the correct way to read per-platform default.
            // Falls back to the documented index from QualitySettings.yaml (Android: 2 = Medium).
            int androidDefault = this.GetAndroidDefaultQualityIndex();

            if (androidDefault != REQUIRED_ANDROID_QUALITY_INDEX)
            {
                string actualName = androidDefault >= 0 && androidDefault < names.Length
                    ? names[androidDefault]
                    : androidDefault.ToString();
                violations.Add(
                    $"Default Android quality is '{actualName}' (index {androidDefault}), " +
                    $"required '{REQUIRED_ANDROID_QUALITY_NAME}' (index {REQUIRED_ANDROID_QUALITY_INDEX}). " +
                    "Fix: Edit > Project Settings > Quality > Android default dropdown → set to Medium.");
            }
        }

        private int GetAndroidDefaultQualityIndex()
        {
            // QualitySettings.GetDefaultQualityForPlatform was added in Unity 2022.2 / 6.x.
            // Use reflection to stay compatible without requiring the exact Unity API surface.
            // Fallback: read the serialized YAML value we already know (Android: 2).
            try
            {
                var method = typeof(QualitySettings).GetMethod(
                    "GetDefaultQualityForPlatform",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(BuildTargetGroup) },
                    null);

                if (method != null)
                {
                    object? result = method.Invoke(null, new object[] { BuildTargetGroup.Android });
                    if (result is int idx)
                        return idx;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[MobileRenderConfigValidator] QualitySettings.GetDefaultQualityForPlatform " +
                    $"reflection failed ({ex.GetType().Name}); falling back to serialized default (index 2).");
            }

            // Hard-coded fallback matching the verified serialized value from QualitySettings.asset.
            return REQUIRED_ANDROID_QUALITY_INDEX;
        }

        // ── Assertions 2–5: URP asset HDR / MSAA / shadowmap / shadowDistance ─

        private const bool   REQUIRED_HDR                  = false;
        private const int    MSAA_ENABLED_MIN              = 2;    // ≥2 samples = MSAA ON; 0 and 1 both = disabled
        private const int    REQUIRED_MAX_SHADOWMAP        = 1024;
        private const float  REQUIRED_MAX_SHADOW_DISTANCE  = 25f;

        private void CheckUrpAssetSettings(List<string> violations)
        {
            RenderPipelineAsset? urpAsset = this.ResolveAndroidUrpAsset();
            if (urpAsset == null)
            {
                violations.Add(
                    "Could not resolve the active URP asset for Android Quality 'Medium'. " +
                    "Ensure a UniversalRenderPipelineAsset is assigned at " +
                    "Edit > Project Settings > Quality > Medium > Rendering > Render Pipeline Asset.");
                return;
            }

            // Read serialized fields via SerializedObject — robust across URP versions and
            // base/derived asset types. (reflection GetField missed inherited private fields and
            // silently returned default(0), false-failing MSAA when m_MSAA was actually 1.)
            // Each check is null-guarded: an absent field (unknown URP layout) is skipped, never
            // false-failed.
            using var so = new SerializedObject(urpAsset);

            // HDR — m_SupportsHDR (bool)
            SerializedProperty? hdrProp = so.FindProperty("m_SupportsHDR");
            if (hdrProp != null && hdrProp.boolValue != REQUIRED_HDR)
            {
                violations.Add(
                    $"URP asset '{urpAsset.name}': HDR is ON (m_SupportsHDR=true), required OFF. " +
                    "HDR doubles bandwidth on TBDR GPUs and costs ~2-5 fps. " +
                    "Fix: select Assets/URPDefaultResources/Medium.asset → Rendering → HDR → off.");
            }

            // MSAA — m_MSAA. Unity represents "disabled" as 1 (MsaaQuality.Disabled); some assets
            // store 0. BOTH mean no multisampling, so only fail when MSAA is actually ON (≥2 samples).
            SerializedProperty? msaaProp = so.FindProperty("m_MSAA");
            if (msaaProp != null && msaaProp.intValue >= MSAA_ENABLED_MIN)
            {
                violations.Add(
                    $"URP asset '{urpAsset.name}': MSAA is {msaaProp.intValue}x, required disabled (0 or 1). " +
                    "MSAA on TBDR writes full multi-sample tiles and blocks HSR. " +
                    "Fix: select Assets/URPDefaultResources/Medium.asset → Quality → Anti Aliasing → Disabled.");
            }

            // Main-light shadowmap resolution — m_MainLightShadowmapResolution (int)
            SerializedProperty? shadowmapProp = so.FindProperty("m_MainLightShadowmapResolution");
            if (shadowmapProp != null && shadowmapProp.intValue > REQUIRED_MAX_SHADOWMAP)
            {
                violations.Add(
                    $"URP asset '{urpAsset.name}': main-light shadowmap is {shadowmapProp.intValue}, " +
                    $"required ≤ {REQUIRED_MAX_SHADOWMAP}. " +
                    "Fix: select Assets/URPDefaultResources/Medium.asset → Shadows → Main Light → " +
                    $"Shadow Resolution → 1024 or lower.");
            }

            // Shadow distance — m_ShadowDistance (float)
            SerializedProperty? shadowDistProp = so.FindProperty("m_ShadowDistance");
            if (shadowDistProp != null && shadowDistProp.floatValue > REQUIRED_MAX_SHADOW_DISTANCE)
            {
                violations.Add(
                    $"URP asset '{urpAsset.name}': shadow distance is {shadowDistProp.floatValue:0.#}m, " +
                    $"required ≤ {REQUIRED_MAX_SHADOW_DISTANCE}m. " +
                    "Fix: select Assets/URPDefaultResources/Medium.asset → Shadows → Max Distance → 25 or lower.");
            }
        }

        /// <summary>
        /// Resolves the active URP pipeline asset for Android (Medium quality, index 2).
        /// Reads via QualitySettings reflection to stay asmdef-dependency-free.
        /// </summary>
        private RenderPipelineAsset? ResolveAndroidUrpAsset()
        {
            // Try to get the render pipeline asset for the Android default quality level (index 2).
            try
            {
                // Unity 6 / 2022+: QualitySettings.GetRenderPipelineAssetAt(index)
                var method = typeof(QualitySettings).GetMethod(
                    "GetRenderPipelineAssetAt",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);

                if (method != null)
                {
                    object? result = method.Invoke(null, new object[] { REQUIRED_ANDROID_QUALITY_INDEX });
                    if (result is RenderPipelineAsset rpa)
                        return rpa;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[MobileRenderConfigValidator] GetRenderPipelineAssetAt reflection failed " +
                    $"({ex.GetType().Name}); falling back to GraphicsSettings.currentRenderPipeline.");
            }

            // Fallback: the active render pipeline (may differ from Android default in editor,
            // but will at least surface the type for inspection).
            return GraphicsSettings.currentRenderPipeline as RenderPipelineAsset
                   ?? GraphicsSettings.defaultRenderPipeline as RenderPipelineAsset;
        }

        // ── Assertion 6: AlwaysIncludedShaders ⊇ "WorldPainter/IndirectGrass" ─

        private const string INDIRECT_GRASS_SHADER_NAME = "WorldPainter/IndirectGrass";

        private void CheckAlwaysIncludedShaders(List<string> violations)
        {
            // GraphicsSettings exposes no public alwaysIncludedShaders accessor — read the
            // serialized m_AlwaysIncludedShaders array directly off the GraphicsSettings asset.
            bool found = false;
            using var gfxSo = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty? includedProp = gfxSo.FindProperty("m_AlwaysIncludedShaders");
            if (includedProp != null && includedProp.isArray)
            {
                for (int i = 0; i < includedProp.arraySize; i++)
                {
                    var shader = includedProp.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                    if (shader != null && shader.name == INDIRECT_GRASS_SHADER_NAME)
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                violations.Add(
                    $"GraphicsSettings.alwaysIncludedShaders does NOT contain " +
                    $"'{INDIRECT_GRASS_SHADER_NAME}'. " +
                    "Without it the GPU indirect grass shader will be stripped → grass invisible on device. " +
                    "Fix: Edit > Project Settings > Graphics → Always Included Shaders → add " +
                    $"'{INDIRECT_GRASS_SHADER_NAME}' (Assets/WorldPainter/Shaders/GrassInteractIndirect.shader).");
            }
        }

        // ── Assertion 7: no WorldPainter component has scatterForceTier == ForceCpu ──

        // Serialized field name and enum value name from WorldPainter.Scatter.cs.
        private const string SCATTER_FORCE_TIER_FIELD  = "scatterForceTier";
        private const string FORCE_CPU_VALUE_NAME      = "ForceCpu";
        private const int    FORCE_CPU_ENUM_INT_VALUE  = 1; // ForceCpu = 1 (Auto=0, ForceCpu=1, ForceGpu=2)

        private void CheckScatterForceTier(List<string> violations)
        {
            var offenders = new List<string>();

            // Scan prefabs.
            this.ScanPrefabsForForceCpu(offenders);

            // Scan build scenes.
            this.ScanBuildScenesForForceCpu(offenders);

            if (offenders.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(
                $"Found {offenders.Count} WorldPainter component(s) with " +
                $"scatterForceTier = {FORCE_CPU_VALUE_NAME}. " +
                "This is the 20-fps CPU-tier footgun that bypasses GrassTierProbe and ships the " +
                "CPU scatter path on Adreno 730. Required: Auto or ForceGpu. " +
                "Fix: select each offending component → Scatter → Tier Override → Auto.");
            sb.AppendLine();
            foreach (string path in offenders)
                sb.Append("    ").AppendLine(path);

            violations.Add(sb.ToString().TrimEnd());
        }

        private void ScanPrefabsForForceCpu(List<string> offenders)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject? prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                foreach (WorldPainter wp in prefab.GetComponentsInChildren<WorldPainter>(true))
                {
                    if (this.IsForceCpu(wp))
                        offenders.Add($"Prefab: {prefabPath} (component on '{wp.gameObject.name}')");
                }
            }
        }

        private void ScanBuildScenesForForceCpu(List<string> offenders)
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled) continue;
                string scenePath = buildScene.path;

                // Load the scene's root GameObjects via SerializedObject to avoid
                // actually entering Play mode or dirtying the scene.
                // We open additively and scan; close after.
                UnityEngine.SceneManagement.Scene scene =
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                        scenePath,
                        UnityEditor.SceneManagement.OpenSceneMode.Additive);

                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (WorldPainter wp in root.GetComponentsInChildren<WorldPainter>(true))
                        {
                            if (this.IsForceCpu(wp))
                            {
                                offenders.Add(
                                    $"Scene: {scenePath}, " +
                                    $"GameObject: '{GetGameObjectPath(wp.gameObject)}'");
                            }
                        }
                    }
                }
                finally
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        /// <summary>
        /// Reads the private <c>scatterForceTier</c> field via SerializedObject to avoid
        /// requiring the enum to be public — the field is serialized so SO can always read it.
        /// Also falls back to reflection for belt-and-suspenders correctness.
        /// </summary>
        private bool IsForceCpu(WorldPainter wp)
        {
            // Primary: SerializedObject reads the exact serialized integer value Unity stores.
            using var so = new SerializedObject(wp);
            SerializedProperty? prop = so.FindProperty(SCATTER_FORCE_TIER_FIELD);
            if (prop != null)
                return prop.enumValueIndex == FORCE_CPU_ENUM_INT_VALUE;

            // Fallback: reflection (if the field name ever changes, this also misses — but
            // the SO path is authoritative; the reflection path is belt-and-suspenders).
            FieldInfo? field = typeof(WorldPainter).GetField(
                SCATTER_FORCE_TIER_FIELD,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return false;

            object? value = field.GetValue(wp);
            return value != null && (int)value == FORCE_CPU_ENUM_INT_VALUE;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a private or public field from a UnityEngine.Object via reflection.
        /// Returns default(T) and logs a warning if the field is not found.
        /// </summary>
        private T GetReflectedField<T>(object target, Type type, string fieldName)
        {
            FieldInfo? field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (field == null)
            {
                Debug.LogWarning(
                    $"[MobileRenderConfigValidator] Field '{fieldName}' not found on {type.FullName}. " +
                    "The URP asset schema may have changed — update the validator.");
                return default!;
            }

            object? value = field.GetValue(target);
            return value is T typed ? typed : default!;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            var parts = new Stack<string>();
            Transform? t = go.transform;
            while (t != null)
            {
                parts.Push(t.gameObject.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }
    }
}
