#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.StageHazards;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class SlopArenaBassDropCommands
    {
        [CliCommand("sloparena.stage.bass-drop.anchors", "Apply only After Hours hazard markers from layout.py's generated shell JSON.", MainThreadRequired = true)]
        public static object Anchors([CliArg("from", "Repository-relative generated shell JSON.", Required = true)] string from)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var shell = JObject.Parse(File.ReadAllText(Path.Combine(repo, from)));
            var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(repo, "data/arenas/after_hours.arena")).Value;
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Stages/after_hours/after_hours.unity", OpenSceneMode.Single);
                var aids = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "AuthoringAids");
                StageHazardMarkerAuthoring.Apply(aids, (JArray)shell["hazardAnchors"], arena);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                return new { success = true, cells = StageHazardMarkerAuthoring.Read(aids, arena).Entries.Count };
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        [CliCommand("sloparena.stage.bass-drop.smoke", "Step one real Local Brawl Bass Drop with an idle player on a lit pad and a Normal CPU.", MainThreadRequired = true)]
        public static async Task<object> Smoke()
        {
            double deadline = EditorApplication.timeSinceStartup + 22;
            TrainingMatch match = null;
            while (match == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No match.");
                await Task.Delay(20); match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            }
            object owner = new();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Stepping busy.");
            if (!match.TryAcquireCaptureInputSequence(owner)) { match.ReleaseCaptureStepping(owner); throw new InvalidOperationException("Input busy."); }
            bool positioned = false; uint impact = 0; int hits = 0; float upward = 0;
            try
            {
                match.SetCaptureSequenceInput(owner, default);
                for (int i = 0; i < 1200; i++)
                {
                    var sim = match.CaptureSimulation; var view = sim.StageHazards;
                    if (!positioned && view.Cues.Count > 0)
                    {
                        match.ResetTrainingPositions(); var cue = view.Cues[0]; var p = cue.Cells.Anchor.Points[cue.Cells.Indices[0]];
                        var s = sim.GetState(MatchConfig.LocalEntityId); s.PX = p.X; s.PZ = p.Z;
                        s.PY = p.Y + sim.GetDefinition(s.EntityId).CapsuleHeight / 2; s.IsGrounded = true;
                        s.VX = s.VY = s.VZ = 0; sim.SetState(s.EntityId, s);
                        positioned = true; impact = cue.ImpactTick;
                    }
                    match.CaptureStep(owner);
                    hits += match.CaptureLastTickHits.Count(h => h.OwnerEntityId == StageHazardOwner.EntityId && h.TargetEntityId == MatchConfig.LocalEntityId && !h.Blocked);
                    upward = Mathf.Max(upward, sim.GetState(MatchConfig.LocalEntityId).KVY);
                    if (positioned && match.CaptureTick > impact + 12) break;
                    if (i % 20 == 0) await Task.Delay(1);
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Smoke bound.");
                }
                return new { success = hits == 1 && upward > 0 && match.NpcCount > 0, impact, hits, upward, cpu = match.NpcCount, stage = MatchConfig.ArenaName };
            }
            finally { match.ReleaseCaptureInputSequence(owner); match.ReleaseCaptureStepping(owner); }
        }
    }
}
#endif
