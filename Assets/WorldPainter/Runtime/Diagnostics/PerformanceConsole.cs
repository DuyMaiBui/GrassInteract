#nullable enable
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

            this.sb.Clear();
            this.sb.Append("best ").Append(this.bestMs.ToString("0.0"))
                   .Append("   1%low ").Append(this.lowMs.ToString("0.0")).Append(" ms\n");
            this.sb.Append("mem  mono ").Append(Mb(this.monoHeap));
            if (this.totalAlloc > 0) this.sb.Append("   total ").Append(Mb(this.totalAlloc));
            this.sb.Append('\n');
            this.sb.Append("GC   ").Append(this.gcPerFrame > 0 ? this.gcPerFrame + " coll/frame  ⚠" : "0 (stable)").Append('\n');
            this.sb.Append("rez  ").Append(rw).Append('x').Append(rh)
                   .Append("  (").Append(Screen.width).Append('x').Append(Screen.height).Append(" @").Append(scale.ToString("0.00")).Append(")\n");
            if (!string.IsNullOrEmpty(this.deviceText)) this.sb.Append(this.deviceText).Append('\n');
            foreach (var kv in metrics)
                this.sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            this.bodyText = this.sb.ToString();
        }

        private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("0") + " MB";

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
            this.bodyStyle ??= new GUIStyle(GUI.skin.label);
            this.tierStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            this.btnStyle  ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            this.headStyle.fontSize = fs + 3;
            this.bodyStyle.fontSize = fs;
            this.tierStyle.fontSize = fs + 1;
            this.btnStyle.fontSize  = fs;
            this.btnStyle.padding   = new RectOffset(10, 10, 8, 8);
        }

        private void OnGUI()
        {
            this.EnsureStyles();

            Color fpsCol = this.fps >= 50f ? new Color(0.40f, 1f, 0.45f)
                         : this.fps >= 30f ? new Color(1f, 0.82f, 0.30f)
                                           : new Color(1f, 0.40f, 0.40f);

            float pad = Mathf.Max(8f, Screen.height * 0.012f);
            float w   = Mathf.Clamp(Screen.width * 0.46f, 280f, 520f);
            float h   = this.expanded ? Mathf.Min(Screen.height * 0.7f, 440f) : (this.headStyle!.fontSize + 24f);

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
