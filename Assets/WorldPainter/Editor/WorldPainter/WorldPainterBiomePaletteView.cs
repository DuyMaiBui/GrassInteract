#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Big-thumbnail card palette for <see cref="BiomePreset"/> assets.
    ///
    /// Shows one card per biome in <see cref="WorldPainter.Biomes"/>.
    /// Each card displays:
    ///   - AssetPreview thumbnail (64×64)
    ///   - Biome name label
    ///   - On hover: channel contribution tooltip
    ///
    /// Clicking a card sets it as the active biome (updates
    /// <see cref="WorldPainterState.ActiveBiomeIndex"/>).
    ///
    /// A "+" button opens a picker to add a BiomePreset reference to the list.
    ///
    /// Design §4.3 / Phase 5 task 3.
    /// </summary>
    internal sealed class WorldPainterBiomePaletteView
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int THUMB_SIZE = 64;

        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter     painter;
        private SerializedProperty        biomesProp = null!;

        // ── UI root ───────────────────────────────────────────────────────────

        private VisualElement? cardsContainer;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterBiomePaletteView(SerializedObject so, WorldPainter painter)
        {
            this.serializedObject = so;
            this.painter          = painter;
            this.biomesProp       = so.FindProperty("biomes")!;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("wp-biome-palette-root");

            // Header row.
            var header = new VisualElement();
            header.AddToClassList("wp-stack-header");
            header.style.flexDirection = FlexDirection.Row;

            var title = new Label("BIOMES");
            title.AddToClassList("wp-stack-title");
            header.Add(title);

            var addBtn = new Button(this.ShowBiomeAdder) { text = "+" };
            addBtn.AddToClassList("wp-add-btn");
            header.Add(addBtn);

            root.Add(header);

            // Cards container.
            this.cardsContainer = new VisualElement();
            this.cardsContainer.AddToClassList("wp-biome-cards");
            this.cardsContainer.style.flexDirection = FlexDirection.Row;
            this.cardsContainer.style.flexWrap      = Wrap.Wrap;
            root.Add(this.cardsContainer);

            this.RefreshCards();
            return root;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        public void RefreshCards()
        {
            if (this.cardsContainer == null) return;
            this.serializedObject.Update();
            this.cardsContainer.Clear();

            int count = this.biomesProp?.arraySize ?? 0;
            for (int i = 0; i < count; ++i)
            {
                var elem    = this.biomesProp.GetArrayElementAtIndex(i);
                var preset  = elem.objectReferenceValue as BiomePreset;
                int captured = i;

                this.cardsContainer.Add(this.BuildCard(preset, captured));
            }
        }

        // ── Card builder ──────────────────────────────────────────────────────

        private VisualElement BuildCard(BiomePreset? preset, int index)
        {
            var card = new VisualElement();
            card.AddToClassList("wp-biome-card");
            if (index == WorldPainterState.ActiveBiomeIndex)
                card.AddToClassList("wp-biome-card--selected");

            // Thumbnail.
            var thumb = new VisualElement();
            thumb.AddToClassList("wp-biome-card-thumb");
            thumb.style.width  = THUMB_SIZE;
            thumb.style.height = THUMB_SIZE;

            if (preset != null)
            {
                Texture2D? preview = AssetPreview.GetAssetPreview(preset);
                if (preview == null) preview = AssetPreview.GetMiniThumbnail(preset);
                if (preview != null)
                    thumb.style.backgroundImage = new StyleBackground(preview);
                else
                    thumb.style.backgroundColor = new StyleColor(new Color(0.35f, 0.2f, 0.55f, 0.8f));
            }
            else
            {
                thumb.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.5f));
            }

            card.Add(thumb);

            // Name label.
            var nameLabel = new Label(preset != null ? preset.name : "(none)");
            nameLabel.AddToClassList("wp-layer-name");
            card.Add(nameLabel);

            // Hover tooltip: channel contribution summary.
            if (preset != null)
            {
                string tip = preset.ChannelSummary();
                card.tooltip = tip;
            }

            // Remove button (small ✕).
            var removeBtn = new Button(() => this.RemoveBiome(index)) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            card.Add(removeBtn);

            // Click to select.
            card.RegisterCallback<ClickEvent>(_ =>
            {
                WorldPainterState.ActiveBiomeIndex = index;
                this.RefreshCards();
            });

            return card;
        }

        // ── Add / Remove ──────────────────────────────────────────────────────

        // Picker control ID stored so we can match the result.
        private int pickerControlId = -1;

        private void ShowBiomeAdder()
        {
            // Open object picker filtered to BiomePreset type.
            this.pickerControlId = this.GetHashCode();
            EditorGUIUtility.ShowObjectPicker<BiomePreset>(null, allowSceneObjects: false,
                searchFilter: string.Empty, controlID: this.pickerControlId);
        }

        /// <summary>
        /// Must be called from an OnGUI block (e.g. the inspector's OnInspectorGUI)
        /// to receive the ObjectSelector result.
        /// </summary>
        public void HandlePickerGUI()
        {
            if (Event.current == null) return;
            if (this.pickerControlId < 0) return;
            string cmd = Event.current.commandName;
            if (cmd == "ObjectSelectorClosed")
            {
                if (EditorGUIUtility.GetObjectPickerControlID() == this.pickerControlId)
                {
                    var picked = EditorGUIUtility.GetObjectPickerObject() as BiomePreset;
                    if (picked != null)
                        this.AddBiome(picked);
                    this.pickerControlId = -1;
                }
            }
        }

        private void AddBiome(BiomePreset preset)
        {
            if (this.biomesProp == null) return;
            Undo.RecordObject(this.painter, "Add Biome Preset");

            int newIdx = this.biomesProp.arraySize;
            this.biomesProp.InsertArrayElementAtIndex(newIdx);
            this.biomesProp.GetArrayElementAtIndex(newIdx).objectReferenceValue = preset;
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveBiomeIndex = newIdx;
            this.RefreshCards();
        }

        private void RemoveBiome(int index)
        {
            if (this.biomesProp == null) return;
            if (index < 0 || index >= this.biomesProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Biome Preset");
            this.biomesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            this.biomesProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveBiomeIndex =
                Mathf.Max(0, WorldPainterState.ActiveBiomeIndex - 1);
            this.RefreshCards();
        }
    }
}
