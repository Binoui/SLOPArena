using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Lets the person at the Editor press Play without breaking agent batches. Play claims the
    /// lease for <see cref="EditorCommandOwnership.HumanHandle"/> and agents queue behind it until
    /// Stop. Play pressed during an agent batch is cancelled, queued ahead of every agent, and
    /// started by itself when that batch settles; pressing Play again cancels the queued Play.
    /// While the owner plays, script reloads and auto-refresh wait, so other threads' saves cannot
    /// reload the session. <c>Window/Pipeline/Hold Editor for Me</c> holds the lease in Edit Mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class HumanEditorLease
    {
        internal const string Playtest = "playtest";
        internal const string Hold = "hold";
        private const string HoldMenu = "Window/Pipeline/Hold Editor for Me";
        // A playtest lease outlives Edit Mode only this long (a failed Play start, an unseen Stop).
        private const double EditModeGraceSeconds = 2;

        /// <summary>Set by editor_play so the Play Mode it requests stays the agent's.</summary>
        internal static bool AgentPlayRequested;

        private static bool s_ReloadsPaused;
        private static double s_EditModeSince;
        private static string s_TitleSuffix = string.Empty;

        static HumanEditorLease()
        {
            if (Application.isBatchMode || !PipelineServerStartup.IsMainProcess())
                return;
            s_EditModeSince = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Tick;
            EditorApplication.updateMainWindowTitle += title => title.title += s_TitleSuffix;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            var ownership = EditorCommandOwnershipSession.GetOrCreate();
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    OnPlayPressed(ownership);
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    if (ownership.HumanLease != null)
                        PauseReloads();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    ResumeReloads();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    ResumeReloads();
                    if (ownership.HumanLease == Playtest && ownership.ReleaseHuman())
                        Debug.Log("[Pipeline] Playtest over; agent batches resume.");
                    break;
            }
        }

        private static void OnPlayPressed(EditorCommandOwnership ownership)
        {
            var agentPlay = AgentPlayRequested;
            AgentPlayRequested = false;
            if (agentPlay || ownership.HasHostActivity("test") || ownership.HumanLease != null)
                return;

            if (ownership.HumanWaiting == Playtest)
            {
                ownership.CancelHumanWait();
                EditorApplication.isPlaying = false;
                Notify("Queued Play cancelled.");
                return;
            }

            var agent = ownership.OwnerHandle;
            if (ownership.TryClaimForHuman(Playtest, out var status))
                return;
            if (status.state == "blocked")
            {
                Notify("The Pipeline lease is blocked; playing without holding it.");
                return;
            }
            EditorApplication.isPlaying = false;
            Notify($"{agent} is running a batch. Play starts by itself when it ends; press Play again to cancel.");
        }

        private static void Tick()
        {
            var ownership = EditorCommandOwnershipSession.Load();
            var now = EditorApplication.timeSinceStartup;
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (!playing)
                AgentPlayRequested = false;

            var grant = ownership.TakeHumanGrant();
            if (grant == Playtest && !playing)
            {
                Notify("Your turn: starting Play. Agents wait until you press Stop.");
                EditorApplication.EnterPlaymode();
                playing = true;
            }
            else if (grant == Hold)
                Notify("The Editor is held for you; agents wait until you release it.");

            if (playing || EditorApplication.isCompiling || EditorApplication.isUpdating)
                s_EditModeSince = now;
            else if (ownership.HumanLease == Playtest && now - s_EditModeSince > EditModeGraceSeconds)
                ownership.ReleaseHuman();

            UpdateTitle(ownership);
        }

        [MenuItem(HoldMenu)]
        private static void ToggleHold()
        {
            if (Application.isBatchMode || EditorCommandOwnershipContext.Current != null)
            {
                Debug.LogWarning("Holding the Editor requires a human using the Pipeline menu.");
                return;
            }

            var ownership = EditorCommandOwnershipSession.GetOrCreate();
            if (ownership.HumanLease == Hold)
            {
                ownership.ReleaseHuman();
                Notify("Editor released; agent batches resume.");
            }
            else if (ownership.HumanWaiting == Hold)
            {
                ownership.CancelHumanWait();
                Notify("Hold request cancelled.");
            }
            else
            {
                var agent = ownership.OwnerHandle;
                if (ownership.TryClaimForHuman(Hold, out var status))
                    Notify("The Editor is held for you; agents wait until you release it.");
                else if (status.state == "blocked")
                    Notify("The Pipeline lease is blocked; nothing to hold.");
                else
                    Notify($"{agent} is running a batch; the hold starts when it ends.");
            }
        }

        [MenuItem(HoldMenu, true)]
        private static bool ToggleHoldValidate()
        {
            var ownership = EditorCommandOwnershipSession.Load();
            Menu.SetChecked(HoldMenu, ownership.HumanLease == Hold || ownership.HumanWaiting == Hold);
            return !Application.isBatchMode;
        }

        private static void PauseReloads()
        {
            if (s_ReloadsPaused)
                return;
            s_ReloadsPaused = true;
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
        }

        private static void ResumeReloads()
        {
            if (!s_ReloadsPaused)
                return;
            s_ReloadsPaused = false;
            AssetDatabase.AllowAutoRefresh();
            EditorApplication.UnlockReloadAssemblies();
        }

        private static void UpdateTitle(EditorCommandOwnership ownership)
        {
            var waiting = ownership.HumanWaiting;
            var lease = ownership.HumanLease;
            var suffix = waiting == Playtest ? $" - Play queued behind {ownership.OwnerHandle}"
                : waiting == Hold ? $" - Hold queued behind {ownership.OwnerHandle}"
                : lease == Playtest ? " - Playtest: agents wait"
                : lease == Hold ? " - Held for you: agents wait"
                : string.Empty;
            if (suffix == s_TitleSuffix)
                return;
            s_TitleSuffix = suffix;
            EditorApplication.UpdateMainWindowTitle();
        }

        private static void Notify(string message)
        {
            Debug.Log($"[Pipeline] {message}");
            var content = new GUIContent(message);
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()
                         .Where(window => window is SceneView || window.GetType().Name == "GameView"))
                window.ShowNotification(content, 5);
        }
    }
}
