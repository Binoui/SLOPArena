using UnityEngine;

namespace SlopArena.Client.UI
{
    internal static class UiModalStateBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAtPlayStart()
        {
            UiModalState.ResetForPlaySession();
            GamepadUIBridge.ResetForPlaySession();
        }
    }
}
