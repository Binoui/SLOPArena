#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared.StageHazards;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Visual evidence for the Splash Deck kraken presenter: steps a prepared Training or Local Brawl
    /// match (sloparena.stage.hazards.prepare, then editor_play) through real slams and captures the
    /// gameplay camera at fixed phases of each, for one port and one starboard variant, plus the lurk
    /// between slams. Writes PNGs and receipt.json below a fresh tmp/ directory.
    /// </summary>
    public static class SlopArenaKrakenCaptureCommands
    {
        private static readonly (string name, int fromImpact, bool fromEnd)[] Shots =
        {
            ("1-warning-start", -120, false), ("2-mid-warning", -60, false), ("3-before-impact", -3, false),
            ("4-impact", 0, false), ("5-lie", 5, false), ("6-withdraw", 22, true),
        };

        [CliCommand("sloparena.stage.hazards.kraken-capture",
            "Capture the kraken presenter from the gameplay camera at warning start, mid warning, before impact, impact, lie and withdraw for one port and one starboard slam, plus the lurk. Requires a prepared hazard match in Play Mode.",
            MainThreadRequired = true)]
        public static async Task<object> Capture(
            [CliArg("output", "Fresh directory below tmp/.")] string output = "tmp/stage-hazards/impl/kraken-unity",
            [CliArg("arrange", "Training: put both fighters beside each slam lane so the fight camera frames it.")] bool arrange = true,
            [CliArg("ab", "Also capture mid warning with the arm shadow and spray hidden, for a pixel comparison.")] bool ab = true,
            [CliArg("max-ticks", "Simulation ticks to search before giving up.")] int maxTicks = 9000,
            [CliArg("budget", "Seconds before giving up; pass a larger Pipeline --timeout.")] int budgetSeconds = 200)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string dir = Path.GetFullPath(Path.Combine(root, output));
            if (!output.StartsWith("tmp/") || output.Contains("..")) throw new ArgumentException("output must be below tmp/.");
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new ArgumentException("output must be a fresh directory.");
            Directory.CreateDirectory(dir);

            double deadline = EditorApplication.timeSinceStartup + 25;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No match.");
                await Task.Delay(20);
            }
            var sim = match.CaptureSimulation;
            if (sim.StageHazards == null) throw new InvalidOperationException("Hazards are Off; prepare with --on true.");
            var presenter = (KrakenHazardPresentation)typeof(TrainingMatch)
                .GetField("_stageHazards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match);
            if (presenter?.Kraken == null) throw new InvalidOperationException("The kraken presenter has no art.");

            // Run with Pipeline's --timeout (the CLI default is 30 s); this bounds the search itself.
            deadline = EditorApplication.timeSinceStartup + Math.Max(20, budgetSeconds);
            var owner = new object();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Capture stepping is busy.");
            if (!match.TryAcquireCaptureInputSequence(owner)) { match.ReleaseCaptureStepping(owner); throw new InvalidOperationException("Capture input is busy."); }
            var receipt = new List<object>();
            var sides = new HashSet<string>();
            uint ordinal = uint.MaxValue, impact = 0, end = 0;
            int shot = 0;
            uint lurkFrom = 0;
            bool lurkFight = false, lurkWide = false;
            string variant = null;
            try
            {
                match.SetCaptureSequenceInput(owner, default);
                if (arrange) match.SetNpcMode(NpcAiMode.Idle);
                for (int i = 0; i < maxTicks && (sides.Count < 2 || !lurkWide || (variant != null && shot < Shots.Length)); i++)
                {
                    if (i % 30 == 0) await Task.Delay(1);
                    var view = sim.StageHazards;
                    var lead = view.Cues.OrderBy(c => c.Operation).FirstOrDefault();
                    if (lead != null && lead.Ordinal != ordinal)
                    {
                        string side = lead.Variant.Split('.')[0];
                        ordinal = lead.Ordinal; impact = lead.ImpactTick; end = lead.EndTick; shot = 0;
                        variant = sides.Contains(side) ? null : lead.Variant;
                        if (variant != null)
                        {
                            sides.Add(side);
                            if (arrange) Arrange(match, lead);
                        }
                    }
                    if (variant != null && shot < Shots.Length)
                    {
                        var s = Shots[shot];
                        long at = (s.fromEnd ? end : impact) + s.fromImpact;
                        if (view.Tick >= at)
                        {
                            await Settle(arrange && shot == 0 ? 45 : 3);
                            string name = $"{variant}-{s.name}";
                            Render(match, $"{dir}/{name}.png");
                            receipt.Add(State(name, view.Tick, presenter, match));
                            if (ab && s.name == "2-mid-warning")
                            {
                                var hidden = Hide(presenter, true);
                                Render(match, $"{dir}/{name}-no-shadow-no-spray.png");
                                Hide(presenter, false, hidden);
                            }
                            shot++;
                            continue;
                        }
                    }
                    if (lead == null && ordinal != uint.MaxValue && lurkFrom == 0) lurkFrom = view.Tick;
                    // Mid-peek of the lurk loop (eyes and cap above the water), after the glide out.
                    if (lead == null && lurkFrom != 0 && !lurkWide && view.Tick >= lurkFrom + 240 && presenter.Phase == "lurk")
                    {
                        if (!lurkFight)
                        {
                            if (arrange) ArrangeToward(match, presenter.Kraken.position);
                            await Settle(45);
                            Render(match, $"{dir}/lurk-fight-camera.png");
                            receipt.Add(State("lurk-fight-camera", view.Tick, presenter, match));
                            lurkFight = true;
                        }
                        Wide(match, presenter.Kraken.position, $"{dir}/lurk-wide.png");
                        receipt.Add(State("lurk-wide", view.Tick, presenter, match));
                        lurkWide = true;
                    }
                    match.CaptureStep(owner);
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Kraken capture exceeded its bound.");
                }
            }
            finally
            {
                match.ReleaseCaptureInputSequence(owner);
                match.ReleaseCaptureStepping(owner);
            }
            var cost = new
            {
                presents = presenter.Presents,
                meanMs = Math.Round(presenter.MeanPresentMilliseconds, 4),
                maxMs = Math.Round(presenter.MaxPresentMilliseconds, 4),
                lastMs = Math.Round(presenter.LastPresentMilliseconds, 4),
            };
            var result = new { success = sides.Count == 2 && lurkWide, mode = MatchConfig.Mode.ToString(), sides = sides.ToArray(), cost, shots = receipt };
            File.WriteAllText(Path.Combine(dir, "receipt.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return result;
        }

        private static Camera Gameplay(TrainingMatch match) =>
            (typeof(MatchBase).GetField("_cameraMount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match) as SlopArena.Client.Camera.CameraMount)?.RenderCamera
            ?? Camera.main ?? Camera.allCameras.FirstOrDefault();

        private static object State(string name, uint tick, KrakenHazardPresentation presenter, TrainingMatch match)
        {
            var kraken = presenter.Kraken;
            var shadow = presenter.Root.Find("Arm shadow");
            var renderer = shadow != null ? shadow.GetComponent<MeshRenderer>() : null;
            var spray = presenter.Root.Find("Spray")?.GetComponent<MeshFilter>()?.sharedMesh;
            int drops = 0;
            if (spray != null)
            {
                var sizes = new List<Vector2>();
                spray.GetUVs(1, sizes);
                for (int i = 0; i < sizes.Count; i += 4) if (sizes[i].x > 0f) drops++;
            }
            return new
            {
                name, tick, phase = presenter.Phase, variant = presenter.ActiveVariant,
                kraken = new[] { kraken.position.x, kraken.position.y, kraken.position.z }, yaw = kraken.eulerAngles.y,
                shadowOn = shadow != null && shadow.gameObject.activeSelf,
                shadowStrength = renderer != null && shadow.gameObject.activeSelf ? renderer.sharedMaterial.GetFloat("_Strength") : 0f,
                shadowReach = renderer != null && shadow.gameObject.activeSelf ? renderer.sharedMaterial.GetFloat("_Reach") : 0f,
                drops,
                camera = CameraState(match),
            };
        }

        private static object CameraState(TrainingMatch match)
        {
            var camera = Gameplay(match);
            if (camera == null) return null;
            var sim = match.CaptureSimulation;
            var p = sim.GetState(MatchConfig.LocalEntityId);
            var fighter = new Vector3(p.PX, p.PY, p.PZ);
            var t = camera.transform;
            return new
            {
                name = camera.name,
                position = new[] { t.position.x, t.position.y, t.position.z },
                pitch = Vector3.Angle(t.forward, Vector3.ProjectOnPlane(t.forward, Vector3.up)),
                yaw = t.eulerAngles.y,
                toPlayer = Vector3.Distance(t.position, fighter),
                fov = camera.fieldOfView,
            };
        }

        private static (bool, bool) Hide(KrakenHazardPresentation presenter, bool hide, (bool, bool) restore = default)
        {
            var shadow = presenter.Root.Find("Arm shadow").gameObject;
            var spray = presenter.Root.Find("Spray").gameObject;
            var before = (shadow.activeSelf, spray.activeSelf);
            if (hide) { shadow.SetActive(false); spray.SetActive(false); }
            else { shadow.SetActive(restore.Item1); spray.SetActive(restore.Item2); }
            return before;
        }

        /// <summary>Both fighters standing on the deck beside the lane, as in a fight near it.</summary>
        private static void Arrange(TrainingMatch match, HazardCue lead)
        {
            var cues = match.CaptureSimulation.StageHazards.Cues;
            float centre = (cues.Min(c => c.Geometry.X) + cues.Max(c => c.Geometry.X)) / 2f;
            match.ResetTrainingPositions();
            Place(match, MatchConfig.LocalEntityId, new Vector3(centre - 1.5f, 0f, 0f));
            if (match.NpcCount > 0) Place(match, match.GetNpcIdAt(0), new Vector3(centre + 1.5f, 0f, 0f));
        }

        private static void ArrangeToward(TrainingMatch match, Vector3 kraken)
        {
            match.ResetTrainingPositions();
            float x = Mathf.Clamp(kraken.x, -10f, 10f), z = Mathf.Sign(kraken.z) * 7f;
            Place(match, MatchConfig.LocalEntityId, new Vector3(x - 1.5f, 0f, z));
            if (match.NpcCount > 0) Place(match, match.GetNpcIdAt(0), new Vector3(x + 1.5f, 0f, z));
        }

        private static void Place(TrainingMatch match, ulong id, Vector3 at)
        {
            var sim = match.CaptureSimulation;
            var state = sim.GetState(id);
            state.PX = at.x; state.PZ = at.z; state.PY = sim.GetDefinition(id).CapsuleHeight / 2f;
            state.VX = state.VY = state.VZ = 0; state.IsGrounded = true;
            sim.SetState(id, state);
        }

        /// <summary>Let rendered frames run (simulation held) so the follow camera settles.</summary>
        private static async Task Settle(int frames)
        {
            int target = Time.frameCount + frames;
            double until = EditorApplication.timeSinceStartup + 5;
            while (Time.frameCount < target && EditorApplication.timeSinceStartup < until) await Task.Delay(1);
        }

        /// <summary>A stage-wide view from the far side of the boat toward the kraken.</summary>
        private static void Render(TrainingMatch match, string path)
        {
            var camera = Gameplay(match) ?? throw new InvalidOperationException("No gameplay camera.");
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var target = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Wide(TrainingMatch match, Vector3 kraken, string path)
        {
            var main = Gameplay(match);
            var go = new GameObject("Kraken capture camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                if (main != null) camera.CopyFrom(main);
                camera.fieldOfView = 50f;
                var look = new Vector3(kraken.x * 0.6f, -6f, kraken.z * 0.6f);
                go.transform.position = new Vector3(kraken.x * 0.3f, 24f, -Mathf.Sign(kraken.z) * 18f);
                go.transform.LookAt(look);
                var rt = RenderTexture.GetTemporary(1920, 1080, 24);
                var active = RenderTexture.active;
                try
                {
                    camera.targetTexture = rt;
                    camera.Render();
                    RenderTexture.active = rt;
                    var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = active;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
#endif
