#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrassInteract
{
    /// <summary>
    /// Owns the top-down trample RenderTexture for an interactive grass field. Each frame it fades the map
    /// toward zero (so flattened patches recover) and additively splats every registered
    /// <see cref="GrassInteractor"/>'s circular footprint, then publishes the result as the
    /// <c>_GrassTrampleMap</c> global the grass shader samples.
    ///
    /// The whole update is a single ping-pong <see cref="Graphics.Blit"/> through one fade+splat shader —
    /// pipeline-agnostic (independent of URP's per-camera loop) and allocation-free per frame. It reads the
    /// SAME <c>_GrassFieldRect</c> that <see cref="GrassInteractField"/> binds, so the trample map and the
    /// density map can never disagree on world→UV (see <see cref="GrassFieldSpace"/>).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassTrampleMap : MonoBehaviour
    {
        /// <summary>Matches the <c>_TrampleInteractors</c> array size in GrassInteract/TrampleUpdate.shader.</summary>
        public const int MAX_INTERACTORS = 16;

        [Min(64)]
        [Tooltip("Trample RT resolution (square). 256–512 is plenty for a typical field.")]
        [SerializeField] private int resolution = 512;

        [Range(0f, 10f)]
        [Tooltip("How fast a flattened spot recovers, per second (higher = blades stand back up sooner).")]
        [SerializeField] private float recoveryPerSecond = 1.5f;

        [Tooltip("Fade+splat blit shader. Leave empty to auto-find GrassInteract/TrampleUpdate.")]
        [SerializeField] private Shader? updateShader;

        [Tooltip("Show a 256×256 live trample-map overlay in the bottom-right corner of the Game view (debug only).")]
        [SerializeField] private bool debugDrawOverlay = false;

        [Tooltip("Log per-second trample diagnostics (interactor count, world XZ, mapped UV, field rect, " +
                 "whether the global is bound to the live RT) to the Console. Debug only.")]
        [SerializeField] private bool debugLog = false;

        private float lastLogTime = -999f;

        private static readonly List<GrassInteractor> interactors = new List<GrassInteractor>();

        private RenderTexture? rtA;
        private RenderTexture? rtB;
        private RenderTexture? current;
        private Material? updateMaterial;
        private readonly Vector4[] interactorData = new Vector4[MAX_INTERACTORS];

        private static readonly int MapId = Shader.PropertyToID("_GrassTrampleMap");
        private static readonly int FadeId = Shader.PropertyToID("_TrampleFade");
        private static readonly int FieldRectId = Shader.PropertyToID("_GrassFieldRect");
        private static readonly int InteractorsId = Shader.PropertyToID("_TrampleInteractors");
        private static readonly int InteractorCountId = Shader.PropertyToID("_TrampleInteractorCount");

        /// <summary>Registers an interactor so its footprint is splatted each frame. Idempotent.</summary>
        public static void Register(GrassInteractor interactor)
        {
            if (!interactors.Contains(interactor))
                interactors.Add(interactor);
        }

        public static void Unregister(GrassInteractor interactor)
        {
            interactors.Remove(interactor);
        }

        private static int activeInstances;

        /// <summary>
        /// True when at least one enabled <see cref="GrassTrampleMap"/> exists in the loaded scenes. A
        /// <see cref="GrassInteractor"/> can only flatten grass when one is present to consume it and publish
        /// the trample texture; <see cref="GrassInteractor"/> warns at runtime when this is false.
        /// </summary>
        public static bool HasActiveInstance => activeInstances > 0;

        private void OnEnable()
        {
            ++activeInstances;
            this.Allocate();
#if UNITY_EDITOR
            // Edit-mode driver: tick every editor update + repaint the scene views so moving an interactor
            // flattens grass live without entering Play. Play mode uses LateUpdate (see Tick's caller).
            UnityEditor.EditorApplication.update -= this.EditorTick;
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update += this.EditorTick;
#endif
        }

        private void OnDisable()
        {
            activeInstances = Mathf.Max(0, activeInstances - 1);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= this.EditorTick;
#endif
            this.Release();
        }

        /// <summary>
        /// Idempotent + self-healing allocation. An edit-mode domain reload destroys these transient
        /// UnityEngine.Objects (leaving "fake-null" references); this recreates ONLY the ones that went
        /// missing. Uses Unity's overloaded <c>== null</c> (NOT <c>??=</c>, which compares the raw managed
        /// reference and so skips recreating a destroyed-but-non-null object — the original bug that left
        /// <c>updateMaterial</c> null while the RTs were live, so LateUpdate silently early-returned and the
        /// trample global never published).
        /// </summary>
        private void Allocate()
        {
            if (this.updateShader == null)
                this.updateShader = Shader.Find("GrassInteract/TrampleUpdate");
            if (this.updateShader == null)
            {
                Debug.LogError($"[{nameof(GrassTrampleMap)}] Shader 'GrassInteract/TrampleUpdate' not found.", this);
                return;
            }

            if (this.updateMaterial == null)
                this.updateMaterial = new Material(this.updateShader) { name = "GrassTrampleUpdate" };
            if (this.rtA == null) { this.rtA = this.CreateRT(); this.ClearRT(this.rtA); }
            if (this.rtB == null) { this.rtB = this.CreateRT(); this.ClearRT(this.rtB); }
            if (this.current == null)
                this.current = this.rtA;
        }

        private RenderTexture CreateRT()
        {
            // ARGBHalf (4×16-bit float): the trample map stores a VECTOR per texel — rg = push direction
            // (world XZ), b = magnitude — so the grass deform reads lean direction DIRECTLY instead of
            // reconstructing it from a scalar gradient (the old model left the trampled core upright and
            // overshot the footprint). Format note: an R8 RenderTexture samples as 0 in the grass shader on
            // this URP/GPU path even though SystemInfo.SupportsRenderTextureFormat(R8) reports true (that flag
            // is render-TARGET support, not sample-correctness). RHalf (the prior single-channel mask) samples
            // correctly here, and ARGBHalf — same half precision, more channels — does too.
            var rt = new RenderTexture(this.resolution, this.resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "GrassTrampleRT",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
            };
            rt.Create();
            return rt;
        }

        private void ClearRT(RenderTexture rt)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }

        private void Release()
        {
            if (this.rtA != null) { this.rtA.Release(); DestroyImmediate(this.rtA); this.rtA = null; }
            if (this.rtB != null) { this.rtB.Release(); DestroyImmediate(this.rtB); this.rtB = null; }
            if (this.updateMaterial != null) { DestroyImmediate(this.updateMaterial); this.updateMaterial = null; }
            this.current = null;
        }

        // In Play mode Unity calls LateUpdate every frame. In edit mode [ExecuteAlways] LateUpdate fires only
        // on sporadic editor ticks, so the editor path is driven from EditorApplication.update (see the
        // UNITY_EDITOR region) — guard here so the two paths never both run in the same edit-mode frame.
        private void LateUpdate()
        {
            if (Application.isPlaying)
                this.Tick();
        }

#if UNITY_EDITOR
        private void EditorTick()
        {
            if (Application.isPlaying)
                return;
            this.Tick();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        /// <summary>Fades the map, splats every interactor, and publishes the result as the global.</summary>
        private void Tick()
        {
            // Self-heal: an edit-mode domain reload nulls these transient objects. Recreate before use so a
            // reload can't leave the trample global stuck on the default black texture (= no interaction).
            if (this.current == null || this.updateMaterial == null || this.rtA == null || this.rtB == null)
                this.Allocate();
            if (this.current == null || this.updateMaterial == null || this.rtA == null || this.rtB == null)
                return; // shader missing — Allocate already logged the error

            // Recovery: scale the previous map toward 0. In edit mode use a fixed step (no Time.deltaTime).
            float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;
            float fade = Mathf.Clamp01(1f - this.recoveryPerSecond * dt);

            // Drop destroyed/null entries (a domain reload nulls them without calling Unregister) so they
            // don't pad the front of the list and push real interactors past the MAX_INTERACTORS cap.
            interactors.RemoveAll(it => it == null);

            int valid = 0;
            for (int i = 0; i < interactors.Count && valid < MAX_INTERACTORS; ++i)
            {
                GrassInteractor it = interactors[i];
                if (it == null)
                    continue;
                Vector3 p = it.WorldPosition;
                this.interactorData[valid] = new Vector4(p.x, p.z, Mathf.Max(it.Radius, 1e-4f), it.Strength);
                ++valid;
            }

            // Read the SAME field rect GrassInteractField binds (SSOT) so trample UV == density UV.
            Vector4 fieldRect = Shader.GetGlobalVector(FieldRectId);
            this.updateMaterial.SetFloat(FadeId, fade);
            this.updateMaterial.SetVectorArray(InteractorsId, this.interactorData);
            this.updateMaterial.SetInt(InteractorCountId, valid);
            this.updateMaterial.SetVector(FieldRectId, fieldRect);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            this.WarnIfInteractorsOffField(valid, fieldRect);
#endif

            RenderTexture dst = this.current == this.rtA ? this.rtB : this.rtA;
            Graphics.Blit(this.current, dst, this.updateMaterial);
            this.current = dst;

            Shader.SetGlobalTexture(MapId, this.current);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Diagnostic: per-second dump of what the splat actually fed the GPU + whether the grass shader's
            // global is bound to THIS live RT. Throttled so edit-mode EditorTick (many ticks/sec) doesn't spam.
            if (this.debugLog)
            {
                float now = Time.realtimeSinceStartup;
                if (now - this.lastLogTime >= 1f)
                {
                    this.lastLogTime = now;
                    Vector4 rect = Shader.GetGlobalVector(FieldRectId);
                    bool globalBound = Shader.GetGlobalTexture(MapId) == this.current;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[GrassTrampleMap] playing=").Append(Application.isPlaying)
                      .Append(" valid=").Append(valid)
                      .Append(" fade=").Append(fade.ToString("0.###"))
                      .Append(" rect=(").Append(rect.x.ToString("0.#")).Append(',').Append(rect.y.ToString("0.#"))
                      .Append(',').Append(rect.z.ToString("0.#")).Append(',').Append(rect.w.ToString("0.#")).Append(')')
                      .Append(" globalBoundToLiveRT=").Append(globalBound);
                    for (int i = 0; i < valid; ++i)
                    {
                        Vector4 d = this.interactorData[i];
                        float u = rect.z > 1e-5f ? (d.x - rect.x) / rect.z : -1f;
                        float v = rect.w > 1e-5f ? (d.y - rect.y) / rect.w : -1f;
                        sb.Append(" | it").Append(i).Append(": worldXZ=(").Append(d.x.ToString("0.0")).Append(',')
                          .Append(d.y.ToString("0.0")).Append(") r=").Append(d.z.ToString("0.0"))
                          .Append(" s=").Append(d.w.ToString("0.00"))
                          .Append(" uv=(").Append(u.ToString("0.00")).Append(',').Append(v.ToString("0.00")).Append(')');
                    }
                    Debug.Log(sb.ToString(), this);
                }
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float lastOffFieldWarnTime = -999f;

        /// <summary>
        /// If EVERY registered interactor sits outside the field rect, its footprint lands off the trample map
        /// and no grass flattens under it — a silent misconfiguration (e.g. a player roaming beyond a small
        /// field). Warn (throttled) only when ALL interactors are off-field AND a real rect is bound (a zero
        /// rect means no GrassInteractField yet — a different misconfiguration). Editor/dev builds only.
        /// </summary>
        private void WarnIfInteractorsOffField(int valid, Vector4 fieldRect)
        {
            if (valid == 0 || fieldRect.z <= 1e-5f || fieldRect.w <= 1e-5f)
                return;

            int offField = 0;
            for (int i = 0; i < valid; ++i)
            {
                float u = (this.interactorData[i].x - fieldRect.x) / fieldRect.z;
                float v = (this.interactorData[i].y - fieldRect.y) / fieldRect.w;
                if (u < 0f || u > 1f || v < 0f || v > 1f)
                    ++offField;
            }

            if (offField == valid && Time.realtimeSinceStartup - this.lastOffFieldWarnTime > 5f)
            {
                this.lastOffFieldWarnTime = Time.realtimeSinceStartup;
                Debug.LogWarning($"[{nameof(GrassTrampleMap)}] all {valid} interactor(s) are OUTSIDE the grass " +
                    $"field (min=({fieldRect.x:0.#},{fieldRect.y:0.#}) size=({fieldRect.z:0.#}x{fieldRect.w:0.#})) " +
                    "- their footprints land off the trample map, so no grass flattens under them. Move them " +
                    "inside the field, or enlarge GrassLayer.fieldBounds / reposition the GrassInteractField.", this);
            }
        }
#endif

        // Lazily-cached overlay label style. GUI.skin is only valid inside OnGUI, so it is built on first
        // use rather than in a field initializer — and cached so the overlay doesn't allocate a GUIStyle +
        // RectOffset every OnGUI event. GUIStyle is a plain managed class (not a UnityEngine.Object), so
        // `??=` is safe here (no fake-null pitfall).
        private static GUIStyle? overlayBoxStyle;

        // Debug-only: the overlay is stripped from release player builds via the guard below, so a build
        // can never ship the HUD even if debugDrawOverlay was left enabled in a saved scene. It stays
        // available in the editor and in development builds (on-device debugging).
        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!this.debugDrawOverlay)
                return;

            Texture? map = Shader.GetGlobalTexture(MapId);

            string sceneName = SceneManager.GetActiveScene().name;
            Vector4 fieldRect = Shader.GetGlobalVector(FieldRectId);
            string fieldRectStr = $"({fieldRect.x:0.#},{fieldRect.y:0.#},{fieldRect.z:0.#},{fieldRect.w:0.#})";
            string mapInfo = this.current != null
                ? $"{this.current.name} {this.current.width}x{this.current.height}"
                : "null";

            string info = $"Scene: {sceneName}\n" +
                          $"FieldRect: {fieldRectStr}\n" +
                          $"Interactors: {interactors.Count}\n" +
                          $"Map: {mapInfo}";

            const float overlaySize = 256f;
            const float padding = 10f;
            float left = Screen.width - overlaySize - padding;
            float top = padding;

            overlayBoxStyle ??= new GUIStyle(GUI.skin.box)
            {
                normal = { textColor = Color.white },
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(6, 6, 4, 4),
            };

            if (map != null)
                GUI.DrawTexture(new Rect(left, top, overlaySize, overlaySize), map, ScaleMode.StretchToFill, false);
            else
                GUI.Box(new Rect(left, top, overlaySize, overlaySize), "No trample map");

            float labelTop = top + overlaySize + 4f;
            GUI.Box(new Rect(left, labelTop, overlaySize, 72f), info, overlayBoxStyle);
#endif
        }
    }
}
