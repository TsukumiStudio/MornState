using UnityEngine;
namespace MornLib {
    internal static class MornStateUtil {
#if DISABLE_MORN_STATE_LOG
        private const bool ShowLog = false;
#else
        private const bool ShowLog = true;
#endif
        private const string Prefix = "[<color=green>MornState</color>] ";
        internal static void Log(string message) {
            if(ShowLog) Debug.Log(Prefix + message);
        }
        internal static void LogError(string message) {
            if(ShowLog) Debug.LogError(Prefix + message);
        }
        internal static void LogWarning(string message) {
            if(ShowLog) Debug.LogWarning(Prefix + message);
        }
    }
}
