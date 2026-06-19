using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Single funnel for all WorldPainter console logging. Use instead of
    /// <see cref="Debug"/> so logs are compiled out of production player builds.
    /// </summary>
    /// <remarks>
    /// Each method carries <c>[Conditional("UNITY_EDITOR")]</c> and
    /// <c>[Conditional("DEVELOPMENT_BUILD")]</c>. The C# compiler strips every call
    /// site — including evaluation of the arguments (so interpolated strings cost
    /// nothing) — in any build where neither symbol is defined. Net effect:
    /// <list type="bullet">
    /// <item>Editor: logs.</item>
    /// <item>Development build: logs.</item>
    /// <item>Production / release build: nothing — calls are removed.</item>
    /// </list>
    /// Because this strips warnings and errors too, do not route genuine
    /// release-time diagnostics (crash reporting, analytics) through here — call
    /// <see cref="Debug"/> directly for those.
    /// </remarks>
    public static class WpLog
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message) => Debug.Log(message);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message, Object context) => Debug.Log(message, context);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message) => Debug.LogWarning(message);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message, Object context) => Debug.LogWarning(message, context);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object message) => Debug.LogError(message);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object message, Object context) => Debug.LogError(message, context);
    }
}
