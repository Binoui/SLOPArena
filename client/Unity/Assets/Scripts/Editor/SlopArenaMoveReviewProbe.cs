#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.EditorTools
{
    public static class SlopArenaMoveReviewProbe
    {
        [CliCommand("sloparena.moves.probe", "Capture the initial real-g1 stepping proof.", MainThreadRequired = true)]
        public static async Task<object> Probe([CliArg("output", "Fresh evidence directory.", Required = true)] string output,
            [CliArg("facing-test", "Include isolated cosmetic facing witnesses.")] bool facingTest = false)
        {
            string directory = SlopArenaPhotoCommands.Resolve(output, "output", true);
            if (Directory.Exists(directory)) throw new ArgumentException("Use a fresh output directory.");
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (match == null || !match.CaptureSequenceReady)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("No ready Training match.");
                await Task.Delay(30);
                match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            }
            match.SetNpcMode(NpcAiMode.Idle);
            match.ResetTrainingPositions(); match.ResetTrainingDamage(); match.ClearCapturePresentation();
            var player = match.GetCaptureState(MatchConfig.LocalEntityId);
            var dummy = match.GetCaptureState(match.SelectedNpcId);
            player.FacingYaw = player.AimYaw = 0;
            player.Form = new FormState { Active = 0 };
            dummy.PX = player.PX; dummy.PY = player.PY; dummy.PZ = player.PZ + 1.2f;
            dummy.FacingYaw = Mathf.PI;
            match.SetCaptureState(MatchConfig.LocalEntityId, player);
            match.SetCaptureState(match.SelectedNpcId, dummy);
            var mount = UnityEngine.Object.FindFirstObjectByType<CameraMount>();
            var plan = new TakePlan
            {
                MoveId = "real-g1", Ticks = 90, Size = CaptureBudget.Fit(new FrameSize(960, 540), 10, 90),
                Views = new[] { ("behind",0f,35f),("rear-left",45f,35f),("left",90f,35f),("front-left",135f,35f),
                    ("front",180f,35f),("front-right",-135f,35f),("right",-90f,35f),("rear-right",-45f,35f),
                    ("low",0f,8f),("high",0f,45f) },
                FrameAhead = 0.6f, ViewDistance = 8.72f, OutputDirectory = directory,
                Player = new SemanticDrive { Input = tick => new SlopArena.Shared.InputState { ActiveSlot = tick == 0 ? SlopArenaTrainingCommands.MapSlot("1") : AbilitySlots.None } }
            };
            float capture = Time.captureDeltaTime, fixedDt = Time.fixedDeltaTime;
            int quality = QualitySettings.GetQualityLevel();
            MoveTakeRig rig = null;
            GameObject witness = null;
            int steps = 0, sparkObservations = 0, yawObservations = 0;
            float sparkError = 0, yawError = 0, sightCount = float.MaxValue;
            void SpawnWitness(uint tick)
            {
                if (!facingTest || ++steps != 35) return;
                var hit = new SpellResolver.HitResult { HitX = player.PX + 2, HitY = player.PY + 1, HitZ = player.PZ + 0.6f,
                    DirX = 1, DirZ = 0, KnockbackAngle = 30, HitstopTicks = 6 };
                GraphicHitEffect.Spawn(in hit, ImpactTier.Heavy);
                witness = GameObject.CreatePrimitive(PrimitiveType.Cube);
                witness.name = "Facing witness (temporary)";
                witness.transform.position = new Vector3(player.PX - 2, player.PY + 1, player.PZ + 0.6f);
                witness.transform.localScale = new Vector3(1, 0.5f, 0.05f);
                witness.AddComponent<YawBillboard>();
            }
            void ObserveFacing(ScriptableRenderContext context, UnityEngine.Camera camera)
            {
                if (!facingTest || !camera.name.StartsWith("Capture Camera ")) return;
                sightCount = Mathf.Min(sightCount, Shader.GetGlobalFloat("_StageSightCount"));
                foreach (var spark in UnityEngine.Object.FindObjectsByType<GraphicHitEffect>(FindObjectsSortMode.None))
                {
                    sparkError = Mathf.Max(sparkError, Vector3.Angle(spark.transform.forward, camera.transform.forward));
                    sparkObservations++;
                }
                foreach (var billboard in UnityEngine.Object.FindObjectsByType<YawBillboard>(FindObjectsSortMode.None))
                {
                    Vector3 up = billboard.transform.parent != null ? billboard.transform.parent.up : Vector3.up;
                    Vector3 forward = Vector3.ProjectOnPlane(-camera.transform.forward, up);
                    yawError = Mathf.Max(yawError, Vector3.Angle(billboard.transform.forward, forward));
                    yawObservations++;
                }
            }
            try
            {
                Time.captureFramerate = 60; Time.fixedDeltaTime = 1f / 60;
                int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
                if (ultra >= 0) QualitySettings.SetQualityLevel(ultra, true);
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().FirstOrDefault()?.Focus();
                match.CaptureSimulationTickCompleted += SpawnWitness;
                RenderPipelineManager.endCameraRendering += ObserveFacing;
                rig = MoveTakeRig.Begin(match, mount, mount.RenderCamera, plan);
                deadline = EditorApplication.timeSinceStartup + 180;
                while (!rig.Done)
                {
                    if (EditorApplication.isPaused) { EditorApplication.isPaused = false; rig.Result.EditorPausesResumed++; }
                    if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Move take stopped or timed out.");
                    await Task.Delay(30);
                }
                if (!rig.Result.Success) throw new InvalidOperationException(rig.Result.Failure);
                var receipt = new { format = 1, move = plan.MoveId, ticks = plan.Ticks, framesWritten = rig.Result.FramesWritten,
                    editorPausesResumed = rig.Result.EditorPausesResumed,
                    hits = rig.Result.Records.SelectMany(r => r.Hits.Select(h => new { frame = r.Frame, damage = h.Damage, blocked = h.Blocked })) };
                File.WriteAllText(Path.Combine(directory, "take.json"), JsonConvert.SerializeObject(receipt, Formatting.Indented));
                if (facingTest)
                {
                    var facingReceipt = new { sparkObservations, yawObservations, sparkError, yawError, sightCount };
                    File.WriteAllText(Path.Combine(directory, "facing.json"), JsonConvert.SerializeObject(facingReceipt, Formatting.Indented));
                    if (sparkObservations < 100 || yawObservations < 20 || sparkError > 0.1f || yawError > 0.1f || sightCount < 2)
                        throw new InvalidOperationException("Per-camera facing or sight-hole verification failed.");
                }
                return new { success = true, receipt };
            }
            finally
            {
                match.CaptureSimulationTickCompleted -= SpawnWitness;
                RenderPipelineManager.endCameraRendering -= ObserveFacing;
                if (witness != null) UnityEngine.Object.Destroy(witness);
                if (rig != null) UnityEngine.Object.DestroyImmediate(rig.gameObject);
                Time.captureDeltaTime = capture; Time.fixedDeltaTime = fixedDt; QualitySettings.SetQualityLevel(quality, true);
            }
        }
    }
}
#endif
