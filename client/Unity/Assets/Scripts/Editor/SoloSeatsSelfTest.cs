#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using SlopArena.Client;
using SlopArena.Client.World;
using SlopArena.Client.Simulation;
using Unity.Pipeline.Commands;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    public static class SoloSeatsSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Solo Seats")]
        public static void RunAll()
        {
            var saved = (SoloSeat[])MatchConfig.SoloSeats.Clone();
            var fields = typeof(MatchConfig).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => !field.IsInitOnly).ToArray();
            var values = fields.Select(field => field.GetValue(null)).ToArray();
            var opponents = MatchConfig.Opponents.ToArray();
            var camps = new Dictionary<ulong, SlopballCamp>(MatchConfig.Camps);
            var colours = new Dictionary<ulong, byte>(MatchConfig.Colours);
            try
            {
                MatchConfig.Reset();
                Require(MatchConfig.Seat(2).Kind == SeatKind.Cpu && MatchConfig.Seat(2).Fighter == CharacterClass.FightGuy
                    && MatchConfig.Seat(2).Difficulty == CpuDifficulty.Normal && MatchConfig.SoloOpponentCount == 1,
                    "Default Solo must have one Normal FightGuy CPU.");
                Require(MatchConfig.Seat(3).Kind == SeatKind.Open && MatchConfig.Seat(4).Kind == SeatKind.Open,
                    "P3 and P4 must start Open.");
                Require(!MatchConfig.RemoveSoloCpu(2), "Removing the last opponent must fail.");
                Require(MatchConfig.JoinSoloSeat(10) == 2, "A pad must replace the lowest CPU before an Open seat.");
                Require(MatchConfig.JoinSoloSeat(10) == 2 && MatchConfig.SoloOpponentCount == 1,
                    "One device must never take two seats.");
                Require(MatchConfig.JoinSoloSeat(11) == 3 && MatchConfig.JoinSoloSeat(12) == 4,
                    "Further pads must take the lowest non-player seats.");
                Require(MatchConfig.JoinSoloSeat(13) == 0, "A fifth player must not replace a Local Player.");
                MatchConfig.LeaveSoloSeat(3);
                Require(MatchConfig.Seat(3).Kind == SeatKind.Open && MatchConfig.Seat(3).DeviceId == 0,
                    "Leaving with other opponents must open the seat and release the device.");
                Require(MatchConfig.JoinSoloSeat(13) == 3, "Rejoining must fill the lowest available seat.");
                MatchConfig.LeaveSoloSeat(2);
                MatchConfig.LeaveSoloSeat(3);
                MatchConfig.LeaveSoloSeat(4);
                Require(MatchConfig.Seat(4).Kind == SeatKind.Cpu && MatchConfig.Seat(4).Difficulty == CpuDifficulty.Normal
                    && MatchConfig.SoloOpponentCount == 1, "The last Local Player leaving must hand the seat to a Normal CPU.");
                MatchConfig.Seat(2).Kind = SeatKind.Cpu;
                MatchConfig.Seat(2).Difficulty = CpuDifficulty.Hard;
                Require(MatchConfig.RemoveSoloCpu(4) && !MatchConfig.RemoveSoloCpu(2),
                    "CPU removal must preserve one opponent, including when P2 was previously Open.");
                Require(MatchConfig.Seat(2).Difficulty == CpuDifficulty.Hard,
                    "Changing or removing another seat must preserve the CPU difficulty.");
                foreach (LocalPlayMode mode in Enum.GetValues(typeof(LocalPlayMode)))
                {
                    MatchConfig.LocalMode = mode;
                    MatchConfig.ClearActiveMatch();
                    Require(MatchConfig.LocalMode == mode, "Match cleanup must preserve the Local Play retry destination.");
                }
                MatchConfig.Reset();
                Require(MatchConfig.LocalMode == LocalPlayMode.Brawl, "A full menu reset must restore Brawl as the default.");
                Debug.Log("[SoloSeatsSelfTest] Seat admission, ownership, leave fallback, removal and Local Play return persistence passed.");
            }
            finally
            {
                Array.Copy(saved, MatchConfig.SoloSeats, saved.Length);
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, values[i]);
                MatchConfig.Opponents.Clear();
                MatchConfig.Opponents.AddRange(opponents);
                MatchConfig.Camps.Clear();
                foreach (var pair in camps) MatchConfig.Camps.Add(pair.Key, pair.Value);
                MatchConfig.Colours.Clear();
                foreach (var pair in colours) MatchConfig.Colours.Add(pair.Key, pair.Value);
            }
        }

        [CliCommand("sloparena.solo.test-match", "Observe a disposable four-seat Solo to results with idle P1 input and real seat controllers. Captures HUD, end card and results.", MainThreadRequired = true, Tags = new[] { "solo", "testing" })]
        public static async Task<object> RunMatch(
            [CliArg("output", "Repository-relative capture directory.", Required = true)] string output,
            [CliArg("scripted-falls", "After forty seconds of simulation, place fighters below the kill plane in a known order to verify stock resolution and results.")] bool scriptedFalls = false)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || MatchConfig.Mode != GameMode.Solo)
                throw new InvalidOperationException("Open a disposable four-seat Solo stage selection or match first.");
            string? launchDiagnostic = null;
            if (FrontendController.CurrentPage == FrontendPage.StageSelect)
            {
                try { await SlopArenaUiCommands.Click("btn-confirm"); }
                catch (InvalidOperationException exception)
                {
                    var started = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
                    if (started == null || !started.enabled || started.NpcCount != 3) throw;
                    launchDiagnostic = exception.Message;
                }
            }
            var ready = System.Diagnostics.Stopwatch.StartNew();
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            while ((match == null || !match.enabled || match.NpcCount != 3) && ready.Elapsed.TotalSeconds < 8)
            {
                await Task.Delay(15);
                match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            }
            if (match == null || !match.enabled || match.NpcCount != 3)
                throw new InvalidOperationException("The Solo must finish initialising four fighters.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var bridge = (LocalSimulationBridge)typeof(TrainingMatch).GetField("_bridge", flags)!.GetValue(match)!;
            float originalScale = Time.timeScale;
            bool ended = false;
            var priorResults = ClientSession.CurrentMatchResults;
            var eliminated = new Dictionary<ulong, uint>();
            var arena = (ArenaDefinition)typeof(TrainingMatch).GetField("_arenaDef", flags)!.GetValue(match)!;
            var fallOrder = new[] { match.GetNpcIdAt(1), MatchConfig.LocalEntityId, match.GetNpcIdAt(0) };
            int nextFall = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            void Tick(uint tick)
            {
                var states = bridge.GetAllStates();
                foreach (var pair in states)
                    if (pair.Value.Deaths >= MatchConfig.MaxStocks && !eliminated.ContainsKey(pair.Key))
                        eliminated.Add(pair.Key, tick);
                if (ClientSession.CurrentMatchResults != null && !ReferenceEquals(priorResults, ClientSession.CurrentMatchResults))
                {
                    ended = true;
                    Time.timeScale = 1f;
                    return;
                }
                if (scriptedFalls && nextFall < fallOrder.Length && tick >= 2400 + nextFall * 600)
                {
                    ulong id = fallOrder[nextFall++];
                    var falling = states[id];
                    if (falling.Deaths < MatchConfig.MaxStocks)
                    {
                        falling.PY = arena.KillHeight - 5f;
                        falling.IsGrounded = false;
                        falling.VY = -20f;
                        match.SetCaptureState(id, falling);
                        Debug.Log($"[SoloSeatsSelfTest] scripted fall entity={id} tick={tick}");
                    }
                }
                match.SetCaptureInput(default);
            }
            match.CaptureSimulationTickCompleted += Tick;
            try
            {
                Time.timeScale = 12f;
                while (!ended && match.CaptureTick < 90 && timer.Elapsed.TotalSeconds < 8) await Task.Delay(15);
                Time.timeScale = 1f;
                var hud = await SlopArenaCaptureCommands.Capture(output + "/hud.png");
                Require(hud.Success, "HUD capture failed.");
                Time.timeScale = 30f;
                while (!ended && timer.Elapsed.TotalSeconds < 22) await Task.Delay(15);
                Require(ended, "The four-fighter Solo did not finish within the bounded run.");
                await Task.Delay(400);
                var end = await SlopArenaCaptureCommands.Capture(output + "/end-card.png");
                Require(end.Success, "End-card capture failed.");
                EditorApplication.isPaused = false;
                Time.timeScale = 1f;
                var expected = ClientSession.CurrentMatchResults;
                if (expected == null || expected.Entries.Count != 4) throw new InvalidOperationException("Results must include four fighters.");
                foreach (var result in expected.Entries)
                {
                    int placement = eliminated.TryGetValue(result.EntityId, out uint tick)
                        ? 1 + expected.Entries.Count(other => other.EntityId != result.EntityId &&
                            (!eliminated.TryGetValue(other.EntityId, out uint otherTick) || otherTick > tick)) : 1;
                    Require(result.Placement == placement, "Results disagree with observed elimination order.");
                }
                while (FrontendController.CurrentPage != FrontendPage.Results && timer.Elapsed.TotalSeconds < 27) await Task.Delay(15);
                Require(FrontendController.CurrentPage == FrontendPage.Results, "The match did not reach the Results page.");
                await Task.Delay(400);
                var results = await SlopArenaCaptureCommands.Capture(output + "/results.png");
                Require(results.Success, "Results capture failed.");
                return new { Success = true, ScriptedFalls = scriptedFalls, LaunchDiagnostic = launchDiagnostic, expected.Entries, Eliminations = eliminated, Hud = hud, EndCard = end, Results = results };
            }
            finally
            {
                Time.timeScale = originalScale;
                match.CaptureSimulationTickCompleted -= Tick;
                if (match != null) match.ClearCaptureInput();
                EditorApplication.isPaused = false;
            }
        }

        [CliCommand("sloparena.solo.test-couch", "Check two-player Solo input isolation and second-pad pause/resume in an existing disposable match.", MainThreadRequired = true, Tags = new[] { "solo", "testing" })]
        public static async Task<object> RunCouch()
        {
            if (FrontendController.CurrentPage == FrontendPage.StageSelect)
            {
                try { await SlopArenaUiCommands.Click("btn-confirm"); }
                catch (InvalidOperationException) when (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Arena_Offline") { }
            }
            var ready = System.Diagnostics.Stopwatch.StartNew();
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            while ((match == null || !match.enabled || match.NpcCount != 1) && ready.Elapsed.TotalSeconds < 8)
            {
                await Task.Delay(15);
                match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            }
            if (match == null || !match.enabled || MatchConfig.Mode != GameMode.Solo || match.NpcCount != 1)
                throw new InvalidOperationException("Start a two-player Solo with P2 on a disposable virtual pad.");
            var pad = SlopArena.Client.Input.CouchPads.ForSeat(2);
            if (pad == null || !pad.name.StartsWith("SoloFourProbe")) throw new InvalidOperationException("P2 must use the fixture's virtual pad.");
            float scale = Time.timeScale;
            void Send(UnityEngine.InputSystem.LowLevel.GamepadState state) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, state);
            try
            {
                Time.timeScale = 12f;
                while (match.CaptureTick < 20 && ready.Elapsed.TotalSeconds < 10) await Task.Delay(15);
                Time.timeScale = 1f;
                var p1 = match.GetCaptureState(MatchConfig.LocalEntityId);
                var p2 = match.GetCaptureState(match.GetNpcIdAt(0));
                Send(new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = Vector2.right });
                await Task.Delay(450);
                var moved1 = match.GetCaptureState(MatchConfig.LocalEntityId);
                var moved2 = match.GetCaptureState(match.GetNpcIdAt(0));
                Require(Mathf.Abs(moved2.PX - p2.PX) + Mathf.Abs(moved2.PZ - p2.PZ) > 0.1f, "P2's own pad did not move P2.");
                Require(Mathf.Abs(moved1.PX - p1.PX) + Mathf.Abs(moved1.PZ - p1.PZ) < 0.02f, "P2's pad moved P1.");
                Send(default);
                await Task.Delay(80);
                Send(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Start));
                await Task.Delay(100);
                Require(MatchPauseMenu.Active != null && MatchPauseMenu.Active.IsPaused, "P2 Start did not pause.");
                Send(default);
                await Task.Delay(80);
                Send(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Start));
                await Task.Delay(100);
                Require(MatchPauseMenu.Active != null && !MatchPauseMenu.Active.IsPaused, "P2 Start did not resume.");
                return new { Success = true, P1Displacement = new[] { moved1.PX - p1.PX, moved1.PZ - p1.PZ }, P2Displacement = new[] { moved2.PX - p2.PX, moved2.PZ - p2.PZ }, PauseAndResume = true };
            }
            finally { Send(default); Time.timeScale = scale; }
        }

        [CliCommand("sloparena.training.test-roster", "Check adding, selecting and removing a practice CPU through the live paused Training panel.", MainThreadRequired = true, Tags = new[] { "training", "testing" })]
        public static async Task<object> RunTrainingRoster()
        {
            if (MatchConfig.Mode != GameMode.Training) throw new InvalidOperationException("Open a disposable Training stage selection first.");
            if (FrontendController.CurrentPage == FrontendPage.StageSelect)
            {
                try { await SlopArenaUiCommands.Click("btn-confirm"); }
                catch (InvalidOperationException) when (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Arena_Offline") { }
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            while ((match == null || !match.enabled || match.NpcCount != 1) && timer.Elapsed.TotalSeconds < 8)
            {
                await Task.Delay(15);
                match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            }
            if (match == null || !match.enabled || match.NpcCount != 1) throw new InvalidOperationException("Training must initialise one practice CPU.");
            var pause = MatchPauseMenu.Active ?? throw new InvalidOperationException("Pause menu is missing.");
            var document = UnityEngine.Object.FindFirstObjectByType<HUDManager>()?.Document
                ?? throw new InvalidOperationException("The Training HUD is missing.");
            var scroll = document.rootVisualElement.Q<ScrollView>("ts-scroll");
            var bridge = (LocalSimulationBridge)typeof(TrainingMatch).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match)!;
            try
            {
                pause.SetPaused(true);
                await Task.Delay(100);
                scroll.ScrollTo(scroll.Q<Button>("cpu-add"));
                await Task.Delay(100);
                await SlopArenaUiCommands.Click("cpu-add");
                Require(match.NpcCount == 2, "The live panel did not add a CPU.");
                var expected = MatchConfig.Seat(2).Fighter;
                for (int i = 0; i < match.NpcCount; i++)
                    Require(bridge.InternalSim.GetDefinition(match.GetNpcIdAt(i)).Class == expected, "Practice CPU content differs from P2's fighter.");
                await SlopArenaUiCommands.Click("cpu-next");
                Require(match.SelectedNpcIndex == 1, "The panel did not select the second CPU.");
                await SlopArenaUiCommands.Click("cpu-remove");
                Require(match.NpcCount == 1, "The live panel did not remove the selected CPU.");
                match.ResetTrainingPositions();
                return new { Success = true, Fighter = expected.ToString(), Added = true, Selected = true, Removed = true, FinalCpus = match.NpcCount };
            }
            finally { pause.SetPaused(false); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
