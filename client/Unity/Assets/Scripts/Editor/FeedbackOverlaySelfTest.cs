#if UNITY_EDITOR
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using SlopArena.Client.UI;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class FeedbackOverlaySelfTest
    {
        [CliCommand("sloparena.feedback.self-test", "Open Training and verify capture, pause restoration and retained retry against unreachable Master. Requires Play Mode; caller restores scene and stops Play.", MainThreadRequired = true, Tags = new[] { "feedback", "test" })]
        public static async Task<object> Run()
        {
            Require(Application.isPlaying, "Enter Play Mode through the gateway first.");
            var gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            UnityEditor.EditorWindow.GetWindow(gameViewType).Focus();
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.ArenaName = "training";
            var load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Arena_Offline.unity", new LoadSceneParameters(LoadSceneMode.Single));
            await Until(() => load.isDone && MatchPauseMenu.Active != null, "Training load");
            await Task.Delay(500);
            var pause = MatchPauseMenu.Active;
            var overlay = Object.FindFirstObjectByType<FeedbackOverlay>();
            Require(overlay != null, "Persistent feedback overlay missing.");
            var session = ChatSession.Instance;
            Require(session != null, "ChatSession missing.");
            var clientField = typeof(ChatSession).GetField("_masterClient", BindingFlags.NonPublic | BindingFlags.Instance);
            var previousClient = clientField.GetValue(session);
            using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromSeconds(1) };
            using var unreachable = new MasterServerClient(http);
            typeof(MasterServerClient).GetField("_token", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(unreachable, "feedback-self-test");
            bool originalPause = pause.IsPaused;
            try
            {
                pause.SetPaused(false);
                await Task.Delay(100);
                ChatInputGate.Tick();
                FeedbackOverlay.RequestOpen();
                await Until(() => overlay.State == FeedbackOverlay.DraftState.Composing, "capture composer");
                Require(overlay.CaptureHasColor, "Captured image is black.");
                Require(overlay.CapturedBeforeComposer && overlay.CaptureFrame >= 0 && overlay.ComposerFrame >= overlay.CaptureFrame, "Composer preceded capture.");
                Require(pause.IsPaused && Time.timeScale == 0, "Local feedback must pause Training.");
                overlay.Close();
                Require(!pause.IsPaused && Time.timeScale == 1, "Close did not restore unpaused state.");
                await Task.Delay(100); ChatInputGate.Tick();
                pause.SetPaused(true);
                FeedbackOverlay.RequestOpen();
                await Until(() => FeedbackOverlay.OwnsInput, "reopen");
                overlay.Close();
                Require(pause.IsPaused && Time.timeScale == 0, "Close did not retain existing pause.");
                await Task.Delay(100); ChatInputGate.Tick();
                FeedbackOverlay.RequestOpen();
                clientField.SetValue(session, unreachable);
                typeof(FeedbackOverlay).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overlay, null);
                await Until(() => overlay.State == FeedbackOverlay.DraftState.Retry, "unreachable send");
                Require(overlay.SealedSubmission != null && overlay.SealedSubmission.Image != null, "Retry lost draft or image.");
                var id = overlay.SealedSubmission.SubmissionId;
                Directory.CreateDirectory("/tmp/arena-feedback");
                ScreenCapture.CaptureScreenshot("/tmp/arena-feedback/composer.png");
                await Task.Delay(300);
                Require(File.Exists("/tmp/arena-feedback/composer.png"), "Composer evidence missing.");
                return new { success = true, captureFrame = overlay.CaptureFrame, composerFrame = overlay.ComposerFrame,
                    nonBlack = overlay.CaptureHasColor, submissionId = id, retryKept = true, screenshot = "/tmp/arena-feedback/composer.png" };
            }
            finally
            {
                clientField.SetValue(session, previousClient);
                overlay.Close();
                typeof(FeedbackOverlay).GetMethod("ClearDraft", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overlay, null);
                if (pause != null) pause.SetPaused(originalPause);
            }
        }

        private static async Task Until(Func<bool> predicate, string step)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (!predicate())
            {
                if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException("Feedback self-test timed out at " + step);
                await Task.Delay(10);
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
