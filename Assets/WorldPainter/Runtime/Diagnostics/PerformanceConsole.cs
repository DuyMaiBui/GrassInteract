#nullable enable
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace WorldPainter.Diagnostics
{
    /// <summary>
    /// On-screen runtime performance console + live diagnostic toggles for ON-DEVICE profiling —
    /// when the Unity Profiler isn't attached and you need to find a bottleneck on the handset
    /// WITHOUT rebuilding for every experiment.
    ///
    /// Zero setup: auto-spawns in EVERY build via <see cref="RuntimeInitializeOnLoadMethod"/>.
    /// IMGUI overlay (works under either input backend; handles its own touch/click events).
    ///
    /// Shows:
    ///   • smoothed FPS + frame-time (ms), colour-coded (green ≥50 / amber ≥30 / red &lt;30)
    ///   • rolling best + 1%-low frame time (spikes / stutter)
    ///   • managed-heap + total memory, GC collections/frame (the stutter source)
    ///   • GPU device name + the ACTUAL render resolution (after render-scale)
    ///   • per-frame draw calls (+ indirect draws), batches, SetPass calls, triangles, vertices
    ///   • the WorldPainter grass scatter tier — RED on CPU fallback
    ///   • any custom metric pushed via <see cref="Report"/>
    ///
    /// Live toggles (tap on device — no rebuild):
    ///   • <b>Scale</b> cycles URP render scale (1.00 → 0.85 → 0.70 → 0.55). If FPS jumps roughly
    ///     in proportion, the frame is FRAGMENT / fillrate-bound (e.g. grass overdraw); if it
    ///     barely moves, it is vertex- or CPU-bound. This is the fastest bottleneck classifier.
    ///   • <b>Cap</b> cycles the frame cap (60 → 45 → 30 → ∞).
    ///
    /// Tap the FPS header to expand/collapse. Disable via <see cref="AUTO_BOOT"/> false, the
    /// scripting define <c>WP_PERF_CONSOLE_OFF</c>, or delete the file. Strip before production.
    /// </summary>
    public sealed class PerformanceConsole : MonoBehaviour
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const bool  AUTO_BOOT   = true;
        private const float WINDOW_SEC  = 0.5f;
        private const int   MAX_SAMPLES = 256;

        private static readonly float[] SCALE_STEPS = { 1.00f, 0.85f, 0.70f, 0.55f };
        private static readonly int[]   CAP_STEPS   = { 60, 45, 30, 0 };

        // ── Bootstrap ─────────────────────────────────────────────────────────
        private static PerformanceConsole? instance;

        // ── Public signal for governor / Phase 3 density controller ──────────
        /// <summary>
        /// Smoothed average frame time in milliseconds over the last ~0.5 s window.
        /// SSOT for the adaptive-quality signal — consumed by RenderScaleGovernor and
        /// (Phase 3) the adaptive grass density controller. Returns 0 before the first
        /// full window accumulates.
        /// </summary>
        public static float SmoothedFrameMs => instance != null ? instance.avgMs : 0f;

#if !WP_PERF_CONSOLE_OFF
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!AUTO_BOOT || instance != null) return;
            var go = new GameObject("[PerformanceConsole]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerformanceConsole>();
        }
#endif

        // ── Custom-metrics API ────────────────────────────────────────────────
        private static readonly Dictionary<string, string> metrics = new();
        public static void Report(string key, string value) => metrics[key] = value;
        public static void ClearMetric(string key) => metrics.Remove(key);

        // ── Frame timing ──────────────────────────────────────────────────────
        private readonly float[] samples = new float[MAX_SAMPLES];
        private int   head, filled;
        private float avgMs, bestMs, lowMs, fps;

        // ── Memory / GC ───────────────────────────────────────────────────────
        private int  lastGc, gcPerFrame;
        private long monoHeap, totalAlloc;

        // ── GPU render stats (ProfilerRecorder — live in the editor + development builds) ──
        // NOTE: "Draw Calls Count" EXCLUDES Graphics.RenderMeshIndirect draws (grass/props use those),
        // so the indirect count is surfaced separately — otherwise the grass draws look invisible here.
        private ProfilerRecorder drawCallsRec, indirectDrawRec, setPassRec, batchesRec, vertsRec, trisRec;

        // ── Scatter tier (WorldPainter) ───────────────────────────────────────
        private global::WorldPainter.WorldPainter? worldPainter;
        private float  nextFindTime;
        private string tierText = "Grass tier: (searching…)";
        private bool   tierIsCpu;

        // ── Render-scale reflection (no hard URP C# dependency) ────────────────
        private static PropertyInfo? renderScaleProp;
        private int    scaleIdx;
        private string scaleLabel = "Scale 1.00";
        private string capLabel   = "Cap 60";
        private string deviceText = "";

        // ── Prebuilt display strings ──────────────────────────────────────────
        private readonly StringBuilder sb = new();
        private string headerText = "FPS —";
        private string bodyText   = "";

        // ── UI ────────────────────────────────────────────────────────────────
        private bool      expanded = true;
        private GUIStyle? boxStyle, headStyle, bodyStyle, tierStyle, btnStyle;
        private Texture2D? bgTex;
        // Reused for per-frame CalcHeight content-fit measuring (no per-frame GC on a GC-monitoring tool).
        private readonly GUIContent calcContent = new();

        private void OnEnable()
        {
            // Render-stat counters live under ProfilerCategory.Render. .Valid is false in non-development
            // player builds (stats stripped) → the readers fall back to 0, so nothing throws on device.
            this.drawCallsRec    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            this.indirectDrawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Indirect Draw Calls Count");
            this.setPassRec      = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            this.batchesRec      = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            this.vertsRec        = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            this.trisRec         = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDisable()
        {
            this.drawCallsRec.Dispose();
            this.indirectDrawRec.Dispose();
            this.setPassRec.Dispose();
            this.batchesRec.Dispose();
            this.vertsRec.Dispose();
            this.trisRec.Dispose();
        }

        private void Start()
        {
            this.deviceText = SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType;
            // Sync the scale index to whatever the active pipeline currently uses.
            float cur = GetRenderScale();
            for (int i = 0; i < SCALE_STEPS.Length; i++)
                if (Mathf.Abs(SCALE_STEPS[i] - cur) < 0.03f) { this.scaleIdx = i; break; }
        }

        private void Update()
        {
            this.samples[this.head] = Time.unscaledDeltaTime;
            this.head = (this.head + 1) % MAX_SAMPLES;
            if (this.filled < MAX_SAMPLES) this.filled++;

            this.ComputeStats();
            this.PollMemory();
            this.PollScatterTier();
            this.BuildText();
        }

        private void ComputeStats()
        {
            float sum = 0f, best = float.MaxValue, worst = 0f, acc = 0f;
            int n = 0;
            for (int i = 0; i < this.filled; i++)
            {
                int idx = (this.head - 1 - i + MAX_SAMPLES) % MAX_SAMPLES;
                float s = this.samples[idx];
                sum += s; n++;
                if (s < best)  best  = s;
                if (s > worst) worst = s;
                acc += s;
                if (acc >= WINDOW_SEC) break;
            }
            if (n == 0) return;
            this.avgMs  = sum / n * 1000f;
            this.bestMs = best  * 1000f;
            this.lowMs  = worst * 1000f;
            this.fps    = this.avgMs > 0.0001f ? 1000f / this.avgMs : 0f;
        }

        private void PollMemory()
        {
            int gc = System.GC.CollectionCount(0);
            this.gcPerFrame = gc - this.lastGc;
            this.lastGc     = gc;
            this.monoHeap   = System.GC.GetTotalMemory(false);
            this.totalAlloc = Profiler.supported ? Profiler.GetTotalAllocatedMemoryLong() : 0;
        }

        private void PollScatterTier()
        {
            if (this.worldPainter == null && Time.unscaledTime >= this.nextFindTime)
            {
                this.nextFindTime = Time.unscaledTime + 1f;
#if UNITY_2023_1_OR_NEWER
                this.worldPainter = Object.FindFirstObjectByType<global::WorldPainter.WorldPainter>();
#else
                this.worldPainter = Object.FindObjectOfType<global::WorldPainter.WorldPainter>();
#endif
            }
            if (this.worldPainter == null) return;
            string tier = this.worldPainter.ScatterActiveTierName;
            if (string.IsNullOrEmpty(tier)) return;
            this.tierIsCpu = tier.Equals("CPU", System.StringComparison.OrdinalIgnoreCase);
            this.tierText  = this.tierIsCpu ? "Grass tier: CPU  ⚠ GPU tier unavailable" : "Grass tier: " + tier;
        }

        private void BuildText()
        {
            this.headerText = $"FPS {this.fps,5:0.0}   {this.avgMs,4:0.0} ms";

            float scale = GetRenderScale();
            int rw = Mathf.RoundToInt(Screen.width * scale), rh = Mathf.RoundToInt(Screen.height * scale);
            this.scaleLabel = $"Scale {scale:0.00}";
            int cap = Application.targetFrameRate;
            this.capLabel = "Cap " + (cap <= 0 ? "∞" : cap.ToString());

            // One metric per line — a stable, fixed set of rows so each value is easy to track
            // frame-to-frame (no packed multi-value lines that wrap awkwardly in a narrow box).
            long draws = Stat(this.drawCallsRec), ind = Stat(this.indirectDrawRec), batches = Stat(this.batchesRec);
            long setpass = Stat(this.setPassRec), tris = Stat(this.trisRec), verts = Stat(this.vertsRec);

            this.sb.Clear();
            this.sb.Append("best     ").Append(this.bestMs.ToString("0.0")).Append(" ms\n");
            this.sb.Append("1% low   ").Append(this.lowMs.ToString("0.0")).Append(" ms\n");
            this.sb.Append("mem      ").Append(Mb(this.monoHeap)).Append('\n');
            if (this.totalAlloc > 0) this.sb.Append("mem tot  ").Append(Mb(this.totalAlloc)).Append('\n');
            this.sb.Append("GC       ").Append(this.gcPerFrame > 0 ? this.gcPerFrame + " coll/frame  ⚠" : "0 (stable)").Append('\n');
            this.sb.Append("rez      ").Append(rw).Append('x').Append(rh)
                   .Append("  (").Append(Screen.width).Append('x').Append(Screen.height).Append(" @").Append(scale.ToString("0.00")).Append(")\n");
            // GPU render stats — draw = standard draw calls; indirect = RenderMeshIndirect (grass/props),
            // which the standard "Draw Calls Count" excludes.
            this.sb.Append("draw     ").Append(draws).Append('\n');
            this.sb.Append("indirect ").Append(ind).Append('\n');
            this.sb.Append("batch    ").Append(batches).Append('\n');
            this.sb.Append("setpass  ").Append(setpass).Append('\n');
            this.sb.Append("tris     ").Append(FmtK(tris)).Append('\n');
            this.sb.Append("vert     ").Append(FmtK(verts)).Append('\n');

            if (!string.IsNullOrEmpty(this.deviceText)) this.sb.Append(this.deviceText).Append('\n');
            foreach (var kv in metrics)
                this.sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            this.bodyText = this.sb.ToString();
        }

        private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("0") + " MB";

        /// <summary>Last-frame value of a render counter, or 0 when the recorder is unavailable (release builds).</summary>
        private static long Stat(ProfilerRecorder r) => r.Valid && r.Count > 0 ? r.LastValue : 0;

        /// <summary>Compact count formatting: 9320 → "9.3k", 1_250_000 → "1.3M".</summary>
        private static string FmtK(long n) =>
            n >= 1_000_000 ? (n / 1_000_000f).ToString("0.0") + "M"
          : n >= 1_000     ? (n / 1_000f).ToString("0.0") + "k"
                           : n.ToString();

        // ── Live controls ─────────────────────────────────────────────────────
        private void CycleRenderScale()
        {
            this.scaleIdx = (this.scaleIdx + 1) % SCALE_STEPS.Length;
            SetRenderScale(SCALE_STEPS[this.scaleIdx]);
        }

        private void CycleCap()
        {
            int cur = Application.targetFrameRate;
            int i = 0;
            for (int k = 0; k < CAP_STEPS.Length; k++) if (CAP_STEPS[k] == cur) { i = k; break; }
            int next = CAP_STEPS[(i + 1) % CAP_STEPS.Length];
            QualitySettings.vSyncCount  = 0;
            Application.targetFrameRate = next;
        }

        private static float GetRenderScale()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return 1f;
            renderScaleProp ??= rp.GetType().GetProperty("renderScale");
            object? v = renderScaleProp?.GetValue(rp);
            return v is float f ? f : 1f;
        }

        private static void SetRenderScale(float s)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            renderScaleProp ??= rp.GetType().GetProperty("renderScale");
            renderScaleProp?.SetValue(rp, Mathf.Clamp(s, 0.2f, 2f));
        }

        // ── Render ────────────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            int fs = Mathf.Max(13, Mathf.RoundToInt(Screen.height * 0.020f));

            if (this.bgTex == null)
            {
                this.bgTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                this.bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.74f));
                this.bgTex.Apply();
            }
            if (this.boxStyle == null)
            {
                this.boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
                this.boxStyle.normal.background = this.bgTex;
            }
            this.headStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            // wordWrap: long lines (device name, rez, dens/gov metrics) wrap inside the box instead of
            // clipping off the right edge on a wide screen. \n line breaks are still honoured.
            this.bodyStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            this.tierStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            this.btnStyle  ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            this.headStyle.fontSize = fs + 3;
            this.bodyStyle.fontSize = fs;
            this.tierStyle.fontSize = fs + 1;
            this.btnStyle.fontSize  = fs;
            this.btnStyle.padding   = new RectOffset(10, 10, 8, 8);
        }

        /// <summary>Wrapped height of <paramref name="text"/> in <paramref name="style"/> at <paramref name="width"/>, GC-free.</summary>
        private float Measure(GUIStyle style, string text, float width)
        {
            this.calcContent.text = text;
            return style.CalcHeight(this.calcContent, width);
        }

        private void OnGUI()
        {
            this.EnsureStyles();

            Color fpsCol = this.fps >= 50f ? new Color(0.40f, 1f, 0.45f)
                         : this.fps >= 30f ? new Color(1f, 0.82f, 0.30f)
                                           : new Color(1f, 0.40f, 0.40f);

            float pad = Mathf.Max(8f, Screen.height * 0.012f);
            // Wider cap on big screens — at 1920 wide the old 520 px cap forced long metric lines to clip.
            float w   = Mathf.Clamp(Screen.width * 0.46f, 320f, 760f);

            // Size the box to its ACTUAL content (CalcHeight at the wrapped inner width) instead of the old
            // fixed 440 px cap. The font scales up with screen height (fs ≈ Screen.height·0.020 → 22 px at
            // 1080p), so a hardcoded height clipped the buttons / device / metric rows on tall screens.
            float innerW = w - this.boxStyle!.padding.horizontal;
            const float gap = 6f; // approx GUILayout vertical spacing between stacked controls
            float h = this.boxStyle.padding.vertical + this.Measure(this.headStyle!, this.headerText, innerW);
            if (this.expanded)
            {
                h += gap + this.Measure(this.tierStyle!, this.tierText, innerW);
                h += gap + this.Measure(this.bodyStyle!, this.bodyText, innerW);
                h += gap + this.Measure(this.btnStyle!,  this.scaleLabel, innerW); // single Scale/Cap button row
            }
            h = Mathf.Min(h + 4f, Screen.height - 2f * pad); // never taller than the screen

            GUILayout.BeginArea(new Rect(pad, pad, w, h), this.boxStyle);

            this.headStyle.normal.textColor = fpsCol;
            if (GUILayout.Button(this.headerText, this.headStyle))
                this.expanded = !this.expanded;

            if (this.expanded)
            {
                this.tierStyle!.normal.textColor = this.tierIsCpu ? new Color(1f, 0.35f, 0.35f) : new Color(0.45f, 1f, 0.55f);
                GUILayout.Label(this.tierText, this.tierStyle);

                this.bodyStyle!.normal.textColor = Color.white;
                GUILayout.Label(this.bodyText, this.bodyStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(this.scaleLabel, this.btnStyle!)) this.CycleRenderScale();
                if (GUILayout.Button(this.capLabel,   this.btnStyle!)) this.CycleCap();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }
    }
}
