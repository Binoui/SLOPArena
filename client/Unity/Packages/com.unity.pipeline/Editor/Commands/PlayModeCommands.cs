using System.IO;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Commands for controlling Unity Editor play mode state.
    /// Enables automation workflows to control Editor play/stop/pause from CLI tools.
    /// </summary>
    public static class PlayModeCommands
    {
        // TODO Pipeline: should all commands have name with namespace: editor.play build.generate instead of using _

        [CliCommand("editor_play", "Enter Unity Editor play mode. Skips the scripting-domain reload unless reload=true; the project's Enter Play Mode Options return when Play Mode ends.", Tags = new[] { "editor/playmode" })]
        public static string EnterPlayMode(
            [CliArg("reload", "Reload the scripting domain on entry, as the project's Enter Play Mode Options would. Off by default: the reload costs as long as a recompile and resets nothing a scene reload does not, while static state set before Play Mode survives without it.")] bool reload = false)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Editor is already in play mode");
                return "Already in play mode";
            }

            bool skipped = !reload && PlayModeReloadPolicy.SkipReloadForNextPlay();
            EditorApplication.isPlaying = true;
            return skipped ? "Entered play mode (domain reload skipped)" : "Entered play mode";
        }

        [CliCommand("editor_stop", "Exit Unity Editor play mode", Tags = new[] { "editor/playmode" })]
        public static string ExitPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Editor is already in edit mode");
                return "Already in edit mode";
            }

            EditorApplication.isPlaying = false;
            return "Exited play mode";
        }

        [CliCommand("editor_pause", "Toggle pause state of Unity Editor play mode", Tags = new[] { "editor/playmode" })]
        public static string PausePlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Cannot pause - Editor is not in play mode");
                return "Cannot pause - not in play mode";
            }

            EditorApplication.isPaused = !EditorApplication.isPaused;
            string state = EditorApplication.isPaused ? "paused" : "unpaused";
            return $"Play mode {state}";
        }
    }

    /// <summary>
    /// Lets editor_play skip the domain reload for one Play Mode session and puts the project's
    /// Enter Play Mode Options back afterwards. The saved options live in Library, so a crash or
    /// quit during that session is repaired on the next Editor start.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeReloadPolicy
    {
        private const string SavedOptionsPath = "Library/Pipeline/play-mode-options.json";

        private sealed class SavedOptions
        {
            public bool enabled;
            public int options;
        }

        static PlayModeReloadPolicy()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Restore();
            };
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                Restore();
        }

        /// <summary>
        /// Disable the domain reload for the next Play Mode entry. False when it is already off
        /// (the project or another session such as stage views manages it).
        /// </summary>
        internal static bool SkipReloadForNextPlay()
        {
            if (EditorSettings.enterPlayModeOptionsEnabled
                && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(SavedOptionsPath));
            File.WriteAllText(SavedOptionsPath, JsonConvert.SerializeObject(new SavedOptions
            {
                enabled = EditorSettings.enterPlayModeOptionsEnabled,
                options = (int)EditorSettings.enterPlayModeOptions
            }));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions |= EnterPlayModeOptions.DisableDomainReload;
            return true;
        }

        private static void Restore()
        {
            if (!File.Exists(SavedOptionsPath))
                return;
            try
            {
                var saved = JsonConvert.DeserializeObject<SavedOptions>(File.ReadAllText(SavedOptionsPath));
                if (saved != null)
                {
                    EditorSettings.enterPlayModeOptionsEnabled = saved.enabled;
                    EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)saved.options;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not restore Enter Play Mode Options: {ex.Message}");
            }
            File.Delete(SavedOptionsPath);
        }
    }
}
