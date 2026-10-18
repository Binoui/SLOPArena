using SlopArena.Client.Network;
using UnityEditor;

namespace SlopArena.Client.Editor
{
    internal static class SteamEditorSettings
    {
        private const string MenuPath = "Tools/SLOPArena/Steam/Enable in Editor";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool enabled = EditorPrefs.GetBool(ChatSession.EditorSteamEnabledPrefsKey, false);
            EditorPrefs.SetBool(ChatSession.EditorSteamEnabledPrefsKey, !enabled);
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(ChatSession.EditorSteamEnabledPrefsKey, false));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
