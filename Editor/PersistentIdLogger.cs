using UnityEngine;

namespace Proselyte.Persistence
{
    public static class PersistentIdLogger 
    {
        public enum LogSeverity
        {
            Debug,
            Info,
            Warning,
            Error,
            Off,
        }

        public static LogSeverity MinimumSeverity = LogSeverity.Off;

        private static bool ShouldLog(LogSeverity severity) => severity >= MinimumSeverity;

        private static string FormatMessage(string message, LogSeverity severity)
        {
            return $"[{nameof(Proselyte)}.{nameof(Persistence)}][{severity}] {message}";
        }

        public static void LogDebug(string message)
        {
            if(!ShouldLog(LogSeverity.Debug)) return;
            Debug.Log(FormatMessage(message, LogSeverity.Debug));
        }

        public static void LogInfo(string message)
        {
            if(!ShouldLog(LogSeverity.Info)) return;
            Debug.Log(FormatMessage(message, LogSeverity.Info));
        }

        public static void LogWarning(string message)
        {
            if(!ShouldLog(LogSeverity.Warning)) return;
            Debug.LogWarning(FormatMessage(message, LogSeverity.Warning));
        }

        public static void LogError(string message)
        {
            if(!ShouldLog(LogSeverity.Error)) return;
            Debug.LogError(FormatMessage(message, LogSeverity.Error));
        }
    }
}
