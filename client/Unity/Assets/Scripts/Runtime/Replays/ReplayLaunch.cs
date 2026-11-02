using SlopArena.Client.UI;
using SlopArena.Shared.Replays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.Replays
{
    public sealed class ReplayRequest
    {
        public ReplayRequest(string path, FrontendPage returnTo, ReplayTrackChoice track, uint? startTick, ulong? focus)
        {
            Path = path;
            ReturnTo = returnTo;
            Track = track;
            StartTick = startTick;
            Focus = focus;
        }

        public string Path { get; }
        public FrontendPage ReturnTo { get; }
        public ReplayTrackChoice Track { get; }
        public uint? StartTick { get; }
        public ulong? Focus { get; }
    }

    /// <summary>
    /// The one way into the Replay scene: list rows, the Editor command and, later, Results' Watch Replay.
    /// The request crosses the scene load as a static; Play Mode runs without a domain reload, so a
    /// SubsystemRegistration hook clears it.
    /// </summary>
    public static class ReplayLaunch
    {
        public const string SceneName = "Replay";
        private static ReplayRequest? pending;

        /// <summary>Why the last Replay closed early, for the page it returned to; null when it did not.</summary>
        public static string? LastFailure { get; private set; }

        public static void Open(string path, FrontendPage returnTo, ReplayTrackChoice track = ReplayTrackChoice.Best,
            uint? startTick = null, ulong? focus = null)
        {
            pending = new ReplayRequest(path, returnTo, track, startTick, focus);
            LastFailure = null;
            SceneManager.LoadScene(SceneName);
        }

        public static bool TryTake(out ReplayRequest? request)
        {
            request = pending;
            pending = null;
            return request != null;
        }

        public static void Return(FrontendPage page, string? failure = null)
        {
            LastFailure = failure;
            if (failure != null) Debug.LogWarning($"[Replay] {failure}");
            FrontendController.Show(page);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            pending = null;
            LastFailure = null;
        }
    }
}
