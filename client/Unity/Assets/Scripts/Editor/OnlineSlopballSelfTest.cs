using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    /// <summary>Observe a real online Versus client; never inject or decide gameplay.</summary>
    public static class OnlineSlopballSelfTest
    {
        [CliCommand("sloparena.arcade.slopball.test-online",
            "Wait for points in a disposable online Versus session, then verify camps, team markers and the score HUD.",
            MainThreadRequired = true, Tags = new[] { "arcade", "testing" })]
        public static async Task<object> Run(
            [CliArg("points", "Minimum combined score to observe.")] int points = 1,
            [CliArg("seconds", "Bounded wait, from 1 to 45 seconds.")] int seconds = 30)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused
                || MatchConfig.Format != MatchFormat.SlopballVersus || points < 1 || points > 40
                || seconds < 1 || seconds > 45)
                throw new InvalidOperationException("Use an unpaused disposable Versus match and a bounded point/timeout request.");
            var completed = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            double deadline = EditorApplication.timeSinceStartup + seconds;
            var field = typeof(PvPMatch).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic);

            void Observe()
            {
                try
                {
                    if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                        throw new InvalidOperationException("The online Versus client did not reach the requested score before the deadline.");
                    var match = UnityEngine.Object.FindFirstObjectByType<PvPMatch>();
                    var bridge = match != null ? field.GetValue(match) as RollbackSimulationBridge : null;
                    if (bridge?.HasWorldFrame != true || bridge.Simulation.SlopballVersus is not { } score
                        || score.TealScore + score.ClayScore < points) return;
                    var sim = bridge.Simulation;
                    var ids = sim.GetAllStates().Keys.ToArray();
                    var arena = (ArenaDefinition)typeof(ServerSimulation)
                        .GetField("_arena", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
                    float net = arena.Arcade!.Slopball!.NetZ;
                    if (!SlopballCamps.AreBalanced(ids.Select(sim.CampOf))
                        || ids.Any(id => sim.CampOf(id) == SlopballCamp.Teal
                            ? sim.GetState(id).PZ >= net : sim.GetState(id).PZ <= net))
                        throw new InvalidOperationException("The live fighters are not in balanced camps on their own halves.");
                    var labels = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                        .Where(d => d.rootVisualElement != null)
                        .SelectMany(d => d.rootVisualElement.Query<Label>().ToList()).ToArray();
                    var count = labels.FirstOrDefault(l => l.name == "slopball-count");
                    if (count == null || !count.text.Contains("TEAL") || !count.text.Contains("CLAY")
                        || !labels.Any(l => l.text?.Contains("FIRST TO 21") == true)
                        || labels.Any(l => l.name == "stock-count"))
                        throw new InvalidOperationException("The live Versus score and first-to-21 HUD are missing.");
                    int rings = 0;
                    var block = new MaterialPropertyBlock();
                    foreach (var mesh in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                                 .Where(m => m.name == "FighterGroundMarker"))
                    {
                        mesh.GetPropertyBlock(block);
                        if (block.GetColor("_CampColor").a > .5f) rings++;
                    }
                    if (rings != ids.Length)
                    {
                        var markers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                            .Where(m => m.name == "FighterGroundMarker").Select(m =>
                            {
                                var props = new MaterialPropertyBlock(); m.GetPropertyBlock(props);
                                return new { active = m.gameObject.activeInHierarchy, alpha = props.GetColor("_CampColor").a,
                                    position = m.transform.position.ToString() };
                            }).ToArray();
                        var fighters = UnityEngine.Object.FindObjectsByType<SlopArena.Client.Entities.PlayerRenderer>(FindObjectsSortMode.None)
                            .Select(r => new { r.EntityId, r.CapsuleHeight, position = r.transform.position.ToString(),
                                simY = sim.GetState(r.EntityId).PY }).ToArray();
                        completed.TrySetResult(new { success = false, reason = $"Expected {ids.Length} camp rings; found {rings}.",
                            markers, fighters, bridge.ClockSeeded, bridge.PredictionTicks });
                        return;
                    }
                    completed.TrySetResult(new
                    {
                        success = true, worldFrame = bridge.HasWorldFrame, localId = MatchConfig.LocalEntityId,
                        bridge.ClockSeeded, bridge.PredictionTicks,
                        localCamp = sim.CampOf(MatchConfig.LocalEntityId).ToString(),
                        teal = score.TealScore, clay = score.ClayScore, phase = score.Phase.ToString(),
                        score.ServerEntityId, rings, scoreText = count.text,
                        width = Screen.width, height = Screen.height,
                    });
                }
                catch (Exception error) { completed.TrySetException(error); }
            }

            void AfterRender(ScriptableRenderContext context, UnityEngine.Camera camera)
            { if (camera.cameraType == CameraType.Game && !completed.Task.IsCompleted) Observe(); }
            void Watchdog()
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    completed.TrySetException(new InvalidOperationException("No rendered Versus frame reached the requested score before the deadline."));
            }
            RenderPipelineManager.endCameraRendering += AfterRender;
            EditorApplication.update += Watchdog;
            try { return await completed.Task; }
            finally
            {
                RenderPipelineManager.endCameraRendering -= AfterRender;
                EditorApplication.update -= Watchdog;
            }
        }
    }
}
