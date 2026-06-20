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
    /// <para>
    /// <b>Log</b> and <b>Warning</b> are stripped from release players (verbose/dev only).
    /// <b>Error</b> is NOT stripped — genuine release-time failures must surface in shipping
    /// players (crash triage, field bug reports), so error calls compile into every build.
    /// </para>
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

        // NOT [Conditional]: errors must survive into release players for field diagnostics.
        public static void Error(object message) => Debug.LogError(message);

        public static void Error(object message, Object context) => Debug.LogError(message, context);
    }
}
