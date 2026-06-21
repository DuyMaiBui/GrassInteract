#nullable enable
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Single-entry-point Editor window for GPUGrass scene setup (UIToolkit, modelled on the WorldPainter
    /// editor). Author the shared grass config once (embedded inspector), inspect per-terrain bake status
    /// (blade counts only — bake arrays stay hidden), bake all terrains, and tune performance in the
    /// Optimize section. Opens via Tools ▸ GPUGrass ▸ Scene Grass Setup.
    /// </summary>
    public sealed class GpuGrassSceneWindow : EditorWindow
    {
        private const string PREFS_CONFIG_PATH = "GPUGrass.SceneWindow.SharedConfigPath";

        private GpuGrassConfig? sharedConfig;

        // ── Dynamic UI references ─────────────────────────────────────────────
        private ObjectField? configField;
        private VisualElement? inspectorContainer;
        private VisualElement? optimizeContainer;
        private VisualElement? terrainListContainer;
        private Label? terrainHeader;
        private HelpBox? meshWarning;
        private Button? bakeButton;

        private string lastTerrainSig = string.Empty;

        // ── Menu ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/GPUGrass/Scene Grass Setup", false, 0)]
        private static void OpenWindow() => GetWindow<GpuGrassSceneWindow>("GPUGrass");

        // ── UI construction (UIToolkit) ───────────────────────────────────────

        private void CreateGUI()
        {
            VisualElement root = this.rootVisualElement;

            // Scroll the whole window so an expanded foldout (esp. the embedded InspectorElement, which
            // contains IMGUI-backed array drawers that mis-measure height in a plain flex column) flows and
            // scrolls instead of overlapping the elements below it.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            VisualElement body = scroll.contentContainer;
            body.style.paddingLeft = body.style.paddingRight = 8;
            body.style.paddingTop  = body.style.paddingBottom = 8;

            var title = new Label("GPUGrass — Scene Setup");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.marginBottom = 6;
            body.Add(title);

            body.Add(this.BuildConfigPicker());

            this.meshWarning = new HelpBox(
                "Assign a LOD blade mesh on the config (LOD / Render ▸ Lod Meshes). Grass will not render " +
                "until a mesh is assigned — every terrain shares this one mesh.", HelpBoxMessageType.Warning);
            this.meshWarning.style.display = DisplayStyle.None;
            body.Add(this.meshWarning);

            var propsFoldout = new Foldout { text = "Grass Properties (shared)", value = true };
            this.inspectorContainer = new VisualElement();
            propsFoldout.Add(this.inspectorContainer);
            body.Add(propsFoldout);

            this.terrainHeader = new Label("Terrains in scene (0)");
            this.terrainHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            this.terrainHeader.style.marginTop = 8;
            body.Add(this.terrainHeader);
            this.terrainListContainer = new VisualElement();
            body.Add(this.terrainListContainer);

            this.bakeButton = new Button(this.OnBakeAll) { text = "Setup & Bake All Terrains" };
            this.bakeButton.style.marginTop = 6;
            this.bakeButton.style.height = 26;
            body.Add(this.bakeButton);

            var optFoldout = new Foldout { text = "Optimize (Performance)", value = false };
            optFoldout.style.marginTop = 8;
            this.optimizeContainer = new VisualElement();
            optFoldout.Add(this.optimizeContainer);
            body.Add(optFoldout);

            // Restore the shared config across domain reloads.
            string savedPath = EditorPrefs.GetString(PREFS_CONFIG_PATH, string.Empty);
            if (!string.IsNullOrEmpty(savedPath))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(savedPath);
                if (loaded != null)
                {
                    this.SetConfig(loaded);
                    this.configField?.SetValueWithoutNotify(loaded);
                }
            }

            this.RebuildConfigSections();
            this.lastTerrainSig = string.Empty;
            this.RefreshTerrainRows();

            // Poll for terrain changes + mesh-warning state (rebuilds only when something actually changed).
            root.schedule.Execute(this.PollRefresh).Every(500);
        }

        private VisualElement BuildConfigPicker()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            this.configField = new ObjectField("Shared Config")
            {
                objectType = typeof(GpuGrassConfig),
                allowSceneObjects = false,
                value = this.sharedConfig,
            };
            this.configField.style.flexGrow = 1;
            this.configField.RegisterValueChangedCallback(evt =>
            {
                this.SetConfig(evt.newValue as GpuGrassConfig);
                this.RebuildConfigSections();
            });
            row.Add(this.configField);

            var createBtn = new Button(() =>
            {
                GpuGrassConfig cfg = GpuGrassSceneSetup.EnsureSharedConfig();
                this.SetConfig(cfg);
                this.configField?.SetValueWithoutNotify(cfg);
                this.RebuildConfigSections();
            })
            { text = "Create / Find" };
            createBtn.style.width = 100;
            row.Add(createBtn);

            return row;
        }

        // ── Config state ──────────────────────────────────────────────────────

        private void SetConfig(GpuGrassConfig? config)
        {
            this.sharedConfig = config;
            if (config != null)
                EditorPrefs.SetString(PREFS_CONFIG_PATH, AssetDatabase.GetAssetPath(config));
            else
                EditorPrefs.DeleteKey(PREFS_CONFIG_PATH);
        }

        private void RebuildConfigSections()
        {
            this.inspectorContainer?.Clear();
            this.optimizeContainer?.Clear();

            if (this.sharedConfig == null)
            {
                this.inspectorContainer?.Add(new HelpBox(
                    "Select or create a shared GpuGrassConfig to edit grass properties.",
                    HelpBoxMessageType.Info));
                this.bakeButton?.SetEnabled(false);
                this.UpdateMeshWarning();
                return;
            }

            this.bakeButton?.SetEnabled(true);

            // Embedded inspector — native UIToolkit, renders ALL serialized fields (fixes the blank
            // "Grass Properties" of the old IMGUI window). Edits write straight to the shared config asset.
            this.inspectorContainer?.Add(new InspectorElement(this.sharedConfig));

            this.optimizeContainer?.Add(this.BuildOptimizeSection(this.sharedConfig));
            this.UpdateMeshWarning();
        }

        // ── Optimize section (curated subset, same config = SSOT) ──────────────

        private VisualElement BuildOptimizeSection(GpuGrassConfig config)
        {
            var box = new VisualElement();
            var so = new SerializedObject(config);

            string[] fields =
            {
                "enableOcclusionCulling", "enableAdaptiveDensity", "adaptiveTargetFps", "minDensity",
                "lodMaxDistances", "renderCullDistance", "tierMode", "enableTerrainFallback",
                "lowEndMemoryThresholdMB",
            };
            foreach (string f in fields)
            {
                SerializedProperty? prop = so.FindProperty(f);
                if (prop != null)
                    box.Add(new PropertyField(prop));
            }
            box.Bind(so); // bound fields write back to the same config asset (no duplicated state)

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 6;

            var reapply = new Button(this.RebuildAllControllers) { text = "Re-apply & Rebuild" };
            reapply.style.flexGrow = 1;
            var preset = new Button(() => this.ApplyMobilePreset(config)) { text = "Apply Mobile Preset" };
            preset.style.flexGrow = 1;

            btnRow.Add(reapply);
            btnRow.Add(preset);
            box.Add(btnRow);
            return box;
        }

        // ── Mesh warning ──────────────────────────────────────────────────────

        private void UpdateMeshWarning()
        {
            if (this.meshWarning == null)
                return;

            bool hasMesh = this.sharedConfig != null
                && this.sharedConfig.LodMeshes != null
                && this.sharedConfig.LodMeshes.Length > 0
                && this.sharedConfig.LodMeshes[0] != null;

            this.meshWarning.style.display =
                (this.sharedConfig != null && !hasMesh) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Terrain status list ───────────────────────────────────────────────

        private void PollRefresh()
        {
            this.UpdateMeshWarning();
            this.RefreshTerrainRows();
        }

        private void RefreshTerrainRows()
        {
            if (this.terrainListContainer == null || this.terrainHeader == null)
                return;

            Terrain[] terrains = Terrain.activeTerrains;

            // Signature so we only rebuild the list when something actually changed (avoids per-poll flicker).
            var sb = new StringBuilder();
            foreach (Terrain t in terrains)
            {
                if (t == null) continue;
                var c = t.GetComponent<GpuGrassController>();
                int blades = c != null && c.Bake != null ? c.Bake.InstanceCount : -1;
                int tier   = c != null ? (int)c.ResolvedTier : -1;
                sb.Append(t.name).Append(':').Append(blades).Append(':').Append(tier).Append('|');
            }
            string sig = sb.ToString();
            if (sig == this.lastTerrainSig)
                return;
            this.lastTerrainSig = sig;

            this.terrainListContainer.Clear();
            int n = 0;
            foreach (Terrain t in terrains)
            {
                if (t == null) continue;
                n++;
                var c = t.GetComponent<GpuGrassController>();
                string blades = c == null
                    ? "not set up"
                    : (c.Bake != null ? c.Bake.InstanceCount.ToString("N0") : "—");
                string tier = c != null ? c.ResolvedTier.ToString() : "—";
                this.terrainListContainer.Add(BuildTerrainRow(t.name, blades, tier));
            }

            this.terrainHeader.text = $"Terrains in scene ({n})";
            if (n == 0)
                this.terrainListContainer.Add(new HelpBox(
                    "No active Terrains found in the open scene(s).", HelpBoxMessageType.Info));
        }

        private static VisualElement BuildTerrainRow(string name, string blades, string tier)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft = 8;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;

            var nameL = new Label(name) { tooltip = name };
            nameL.style.flexGrow = 1;
            nameL.style.overflow = Overflow.Hidden;

            var bladeL = new Label($"Blades: {blades}");
            bladeL.style.width = 130;
            var tierL = new Label($"Tier: {tier}");
            tierL.style.width = 110;

            row.Add(nameL);
            row.Add(bladeL);
            row.Add(tierL);
            return row;
        }

        // ── Actions ───────────────────────────────────────────────────────────

        private void OnBakeAll()
        {
            if (this.sharedConfig == null)
                return;

            var results = GpuGrassSceneSetup.SetupScene(this.sharedConfig);
            int total = 0;
            foreach (var r in results) total += r.BladeCount;

            Debug.Log($"[GPUGrass] Scene bake complete: {results.Count} terrain(s), {total:N0} total blades.");
            this.lastTerrainSig = string.Empty; // force a list rebuild
            this.RefreshTerrainRows();
            this.UpdateMeshWarning();
        }

        private void RebuildAllControllers()
        {
            int rebuilt = 0;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                var c = t.GetComponent<GpuGrassController>();
                if (c == null) continue;
                c.Rebuild();
                rebuilt++;
            }
            Debug.Log($"[GPUGrass] Re-apply & Rebuild: {rebuilt} controller(s) rebuilt.");
            this.lastTerrainSig = string.Empty;
            this.RefreshTerrainRows();
        }

        private void ApplyMobilePreset(GpuGrassConfig config)
        {
            var so = new SerializedObject(config);
            SetBool(so, "enableOcclusionCulling", true);
            SetBool(so, "enableAdaptiveDensity",  true);
            SetFloat(so, "renderCullDistance",    70f);
            SetInt(so, "tierMode",                (int)GrassTierMode.Auto);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            this.RebuildAllControllers();

            Debug.Log("[GPUGrass] Mobile preset applied: occlusion on, renderCullDistance=70, " +
                      "adaptive density on, tier=Auto.");
        }

        private static void SetBool(SerializedObject so, string field, bool v)
        {
            var p = so.FindProperty(field);
            if (p != null) p.boolValue = v;
        }

        private static void SetFloat(SerializedObject so, string field, float v)
        {
            var p = so.FindProperty(field);
            if (p != null) p.floatValue = v;
        }

        private static void SetInt(SerializedObject so, string field, int v)
        {
            var p = so.FindProperty(field);
            if (p != null) p.intValue = v;
        }
    }
}
