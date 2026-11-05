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
    /// Visual evidence for the After Hours Bass Drop presenter. Steps a prepared Local Brawl or
    /// Training match on After Hours (sloparena.stage.hazards.prepare --stage after_hours, then
    /// editor_play) through real drops. The player stands at Spawn_01 and the CPU waits on a pad;
    /// for the first two drops it captures the gameplay camera at the warning, beats 3, 2 and 1,
    /// the impact and 20 ticks after, one spawn view each and a countdown placement A/B. It then
    /// records the second drop as a JPG frame sequence with a sound timeline, and runs a third drop
    /// with the CPU fighting and the idle player on a lit pad as the smoke check.
    /// Writes below a fresh tmp/ directory.
    /// </summary>
    public static class SlopArenaBassDropCaptureCommands
    {
        private static readonly (string name, int edge, int after)[] Shots =
        {
            ("1-warning", 0, 0), ("2-beat3", 1, 0), ("3-beat2", 2, 0), ("4-beat1", 3, 0), ("5-impact", 4, 0), ("6-blast6", 4, 6), ("7-after20", 4, 20),
        };
        // Spawn_02 and Spawn_03 (x, z, yaw) from tools/blender/after_hours/layout.py.
        private static readonly (float x, float z, float yaw)[] SpawnViews = { (6f, 5f, 225f), (-6f, 5f, 135f) };

        [CliCommand("sloparena.stage.hazards.bass-drop-capture",
            "Capture the Bass Drop presenter on After Hours: six phases for two drops from the gameplay camera, spawn views, a countdown placement A/B, a frame sequence of the second drop and a third-drop smoke with the CPU fighting. Requires a prepared After Hours hazard match in Play Mode.",
            MainThreadRequired = true)]
        public static async Task<object> Capture(
            [CliArg("output", "Fresh directory below tmp/.")] string output = "tmp/stage-hazards/impl/bass-drop-unity/captures",
            [CliArg("video", "Record the second drop as a 30 fps JPG sequence (10 s).")] bool video = true,
            [CliArg("placement", "Countdown placement used for the main shots: floor or booth.")] string placement = "floor",
            [CliArg("max-ticks", "Simulation ticks to search before giving up.")] int maxTicks = 4000,
            [CliArg("budget", "Seconds before giving up; pass a larger Pipeline --timeout.")] int budgetSeconds = 280)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            if (!output.StartsWith("tmp/") || output.Contains("..")) throw new ArgumentException("output must be below tmp/.");
            string dir = Path.GetFullPath(Path.Combine(root, output));
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new ArgumentException("output must be a fresh directory.");
            Directory.CreateDirectory(dir);
            var main = placement == "booth" ? BassDropPresentation.CountdownPlacement.Booth : BassDropPresentation.CountdownPlacement.Floor;
            var other = main == BassDropPresentation.CountdownPlacement.Floor ? BassDropPresentation.CountdownPlacement.Booth : BassDropPresentation.CountdownPlacement.Floor;

            double deadline = EditorApplication.timeSinceStartup + 25;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No match.");
                await Task.Delay(20);
            }
            var sim = match.CaptureSimulation;
            if (sim.StageHazards == null) throw new InvalidOperationException("Hazards are Off; prepare with --on true.");
            if (!(typeof(TrainingMatch).GetField("_stageHazards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match) is BassDropPresentation presenter))
                throw new InvalidOperationException("This match has no Bass Drop presenter; prepare --stage after_hours.");
            presenter.SetPlacement(main);
            if (match.NpcCount == 0) throw new InvalidOperationException("The capture needs a CPU.");
            ulong cpu = match.GetNpcIdAt(0);

            deadline = EditorApplication.timeSinceStartup + Math.Max(30, budgetSeconds);
            var owner = new object();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Capture stepping is busy.");
            if (!match.TryAcquireCaptureInputSequence(owner)) { match.ReleaseCaptureStepping(owner); throw new InvalidOperationException("Capture input is busy."); }
            var receipt = new List<object>();
            var drops = new List<object>();
            var sounds = new List<object>();
            var frames = new List<(uint tick, byte[] jpg)>();
            int pattern = -1, shot = 0, firedSeen = presenter.FiredEdges.Count;
            uint ordinal = uint.MaxValue;
            HazardCue lead = null;
            bool smoke = false, smokeDone = false, videoDone = !video;
            var smokeLog = new Dictionary<string, object>();
            var countdownSeen = new List<string>();
            int popped = 0; uint popTick = 0; float popKvy = 0; string phaseAtPop = null; bool blastAtPop = false, padsAtPop = false;
            uint videoFrom = 0, videoUntil = 0;
            try
            {
                match.SetCaptureSequenceInput(owner, default);
                match.SetNpcDifficulty(SlopArena.Shared.AI.CpuDifficulty.Normal);
                match.SetNpcMode(NpcAiMode.Idle);
                Arrange(match, cpu);
                await Settle(40);
                for (int i = 0; i < maxTicks && !smokeDone; i++)
                {
                    if (i % 30 == 0) await Task.Delay(1);
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Bass Drop capture exceeded its bound.");
                    var view = sim.StageHazards;
                    var cue = view.Cues.Where(c => c.PresentationId == BassDropPresentation.PresentationId).OrderBy(c => c.Operation).FirstOrDefault();
                    if (cue != null && cue.Ordinal != ordinal)
                    {
                        ordinal = cue.Ordinal; lead = cue; pattern++; shot = 0;
                        drops.Add(new { ordinal, variant = cue.Variant, warning = cue.WarningTick, impact = cue.ImpactTick, end = cue.EndTick, lit = cue.Cells.Indices.ToArray() });
                        if (pattern == 2)
                        {
                            // Smoke: the CPU fights for real; the idle player stands on a lit pad.
                            smoke = true;
                            match.SetNpcMode(NpcAiMode.Attack);
                            var p = cue.Cells.Anchor.Points[cue.Cells.Indices[cue.Cells.Indices.Count / 2]];
                            Place(match, MatchConfig.LocalEntityId, new Vector3(p.X, 0f, p.Z));
                        }
                        else if (pattern < 2 && !LitAt(cue, match, cpu)) Debug.LogWarning($"[BassDropCapture] the CPU is not on a lit pad for {cue.Variant}.");
                    }
                    if (lead != null && pattern < 2 && shot < Shots.Length)
                    {
                        var s = Shots[shot];
                        long at = (s.edge < 4 ? lead.WarningTick + s.edge * (((long)lead.ImpactTick - lead.WarningTick) / 4) : lead.ImpactTick) + s.after;
                        if (view.Tick >= at)
                        {
                            await Settle(shot == 0 ? 30 : 3);
                            string name = $"{pattern + 1}-{lead.Variant}-{s.name}";
                            Render(Gameplay(match), $"{dir}/{name}.png");
                            receipt.Add(State(name, view.Tick, presenter, match, cpu));
                            if (s.edge == 1 || s.edge == 3)
                            {
                                presenter.SetPlacement(other);
                                Render(Gameplay(match), $"{dir}/{name}-{other.ToString().ToLowerInvariant()}.png");
                                presenter.SetPlacement(main);
                            }
                            if (s.edge == 3)
                            {
                                var spawn = SpawnViews[pattern];
                                SpawnView(match, spawn, $"{dir}/{pattern + 1}-{lead.Variant}-spawn-view.png");
                                presenter.SetPlacement(other);
                                SpawnView(match, spawn, $"{dir}/{pattern + 1}-{lead.Variant}-spawn-view-{other.ToString().ToLowerInvariant()}.png");
                                presenter.SetPlacement(main);
                                receipt.Add(State($"{pattern + 1}-{lead.Variant}-spawn-view", view.Tick, presenter, match, cpu));
                            }
                            shot++;
                            continue;
                        }
                    }
                    // Between the first and second drop: put the CPU back on its pad and start the frame buffer.
                    if (pattern == 0 && cue == null && lead != null && view.Tick > lead.EndTick)
                    {
                        if (videoFrom == 0) { Arrange(match, cpu); await Settle(20); videoFrom = view.Tick; }
                    }
                    bool recording = !videoDone && videoFrom != 0 && pattern <= 1;
                    match.CaptureStep(owner);
                    view = sim.StageHazards;
                    // Hits on fighters by the stage owner, with the presenter's state on that very tick.
                    foreach (var hit in match.CaptureLastTickHits.Where(h => h.OwnerEntityId == StageHazardOwner.EntityId && !h.Blocked))
                    {
                        if (smoke && hit.TargetEntityId == MatchConfig.LocalEntityId) popped++;
                        if (smoke && hit.TargetEntityId == MatchConfig.LocalEntityId && popTick == 0)
                        {
                            popTick = view.Tick; phaseAtPop = presenter.Phase;
                            blastAtPop = presenter.Root.Find("Blast")?.gameObject.activeSelf == true;
                            padsAtPop = presenter.Root.Find("Lit pads")?.gameObject.activeSelf == true;
                        }
                        Debug.Log($"[BassDropCapture] hit tick={view.Tick} target={hit.TargetEntityId} phase={presenter.Phase}");
                    }
                    if (smoke && popTick != 0) popKvy = Mathf.Max(popKvy, sim.GetState(MatchConfig.LocalEntityId).KVY);
                    if (smoke && presenter.CountdownText.Length > 0 && (countdownSeen.Count == 0 || countdownSeen[^1] != presenter.CountdownText))
                        countdownSeen.Add(presenter.CountdownText);
                    for (int f = firedSeen; f < presenter.FiredEdges.Count; f++) sounds.Add(presenter.FiredEdges[f]);
                    firedSeen = presenter.FiredEdges.Count;
                    if (recording && view.Tick % 2 == 0)
                    {
                        await Settle(1);
                        frames.Add((view.Tick, Jpg(Gameplay(match))));
                        // A rolling 5 s lead-in until the second drop's cue appears, then 3 s after its impact.
                        if (pattern == 0 && frames.Count > 150) frames.RemoveAt(0);
                        if (pattern == 1 && view.Tick >= lead.ImpactTick + 180) { videoDone = true; videoUntil = view.Tick; }
                    }
                    if (smoke && lead != null && view.Tick > lead.ImpactTick + 30) smokeDone = true;
                }
            }
            finally
            {
                match.ReleaseCaptureInputSequence(owner);
                match.ReleaseCaptureStepping(owner);
            }
            if (frames.Count > 0)
            {
                string framesDir = Path.Combine(dir, "frames");
                Directory.CreateDirectory(framesDir);
                for (int f = 0; f < frames.Count; f++) File.WriteAllBytes(Path.Combine(framesDir, $"{f:0000}.jpg"), frames[f].jpg);
                File.WriteAllText(Path.Combine(dir, "frames.json"), JsonConvert.SerializeObject(new
                {
                    fps = 30, ticksPerFrame = 2, firstTick = frames[0].tick, lastTick = frames[^1].tick,
                    count = frames.Count, sounds,
                }, Formatting.Indented));
            }
            var cost = new { presents = presenter.Presents, meanMs = Math.Round(presenter.MeanPresentMilliseconds, 4), maxMs = Math.Round(presenter.MaxPresentMilliseconds, 4) };
            var smokeResult = new
            {
                ran = smokeDone, popped, popTick, impact = lead?.ImpactTick ?? 0, inSync = popTick != 0 && lead != null && popTick == lead.ImpactTick,
                phaseAtPop, blastAtPop, padsAtPop, popKvy, countdownSeen, cpuMode = "Attack", cpuDifficulty = "Normal",
            };
            bool ok = pattern >= 2 && smokeDone && smokeResult.inSync && blastAtPop && padsAtPop
                && countdownSeen.Take(5).SequenceEqual(new[] { "4", "3", "2", "1", "drOP" }) && (video ? frames.Count > 250 : true);
            var result = new
            {
                success = ok, mode = MatchConfig.Mode.ToString(), stage = MatchConfig.ArenaName, seed = MatchConfig.StageHazardSeed,
                placement = main.ToString(), matchTransform = new { position = V(match.transform.position), scale = V(match.transform.lossyScale), root = V(presenter.Root.position), rootScale = V(presenter.Root.lossyScale) }, drops, smoke = smokeResult, frames = frames.Count, videoFrom, videoUntil, cost, sounds, shots = receipt,
            };
            File.WriteAllText(Path.Combine(dir, "receipt.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return new { result.success, result.mode, result.stage, result.seed, result.matchTransform, result.drops, result.smoke, result.frames, result.cost };
        }

        [CliCommand("sloparena.stage.hazards.bass-drop-edges",
            "Feed real Bass Drop views to a fresh presenter in order, with skipped ticks and with rewinds across beat and drop edges, and count each (ordinal, edge) it fires. Requires a prepared After Hours hazard match in Play Mode.",
            MainThreadRequired = true)]
        public static async Task<object> Edges()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            double deadline = EditorApplication.timeSinceStartup + 25;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No match.");
                await Task.Delay(20);
            }
            var sim = match.CaptureSimulation;
            if (sim.StageHazards == null) throw new InvalidOperationException("Hazards are Off.");
            var views = new Dictionary<uint, StageHazardView>();
            var owner = new object();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Capture stepping is busy.");
            HazardCue cue = null;
            uint first = sim.StageHazards.Tick;
            try
            {
                for (int i = 0; i < 2000; i++)
                {
                    var view = sim.StageHazards;
                    views[view.Tick] = view;
                    // Skip a drop already under way: the runs below replay from 10 ticks before its warning.
                    cue ??= view.Cues.FirstOrDefault(c => c.PresentationId == BassDropPresentation.PresentationId && c.WarningTick >= first + 10);
                    if (cue != null && view.Tick > cue.ImpactTick + 50) break;
                    match.CaptureStep(owner);
                    if (i % 60 == 0) await Task.Delay(1);
                }
            }
            finally { match.ReleaseCaptureStepping(owner); }
            if (cue == null) throw new InvalidOperationException("No drop within 2000 ticks.");
            uint w = cue.WarningTick, imp = cue.ImpactTick, from = w - 10, to = imp + 50;
            object Run(string name, IEnumerable<uint> ticks)
            {
                var host = new GameObject("Bass Drop edge check");
                var presenter = new BassDropPresentation(host.transform);
                try
                {
                    int fed = 0;
                    foreach (uint t in ticks) { presenter.Present(views[t]); fed++; }
                    var fired = presenter.FiredEdges.Select(l => l.Split(' ').First(x => x.StartsWith("edge="))).ToList();
                    var counts = fired.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                    bool once = counts.Count == 5 && counts.Values.All(c => c == 1);
                    return new { name, fed, once, counts, late = presenter.FiredEdges.Count(l => l.EndsWith(" late")), log = presenter.FiredEdges.ToArray() };
                }
                finally { presenter.Dispose(); Object.DestroyImmediate(host); }
            }
            IEnumerable<uint> Range(uint a, uint b, uint step = 1) { for (uint t = a; t <= b; t += step) yield return t; }
            var runs = new List<object>
            {
                Run("in order", Range(from, to)),
                Run("every 7th tick (a client skipping ticks)", Range(from, to, 7).Append(to)),
                Run("rewind from beat 2 to before beat 3, then on", Range(from, w + 65).Concat(Range(w + 20, to))),
                Run("rewind from after the drop to before beat 4, then on", Range(from, imp + 4).Concat(Range(from, to))),
                Run("rollback window: every tick re-simulates the last 8", Range(from, to).SelectMany(t => Range(t > from + 8 ? t - 8 : from, t))),
            };
            bool ok = runs.All(r => (bool)r.GetType().GetProperty("once")!.GetValue(r));
            return new { success = ok, ordinal = cue.Ordinal, warning = w, impact = imp, runs };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        private static bool LitAt(HazardCue cue, TrainingMatch match, ulong id)
        {
            var s = match.CaptureSimulation.GetState(id);
            return cue.Cells.Indices.Any(i => Mathf.Abs(cue.Cells.Anchor.Points[i].X - s.PX) < 1f && Mathf.Abs(cue.Cells.Anchor.Points[i].Z - s.PZ) < 1f);
        }

        /// <summary>The player at Spawn_01 (the default fight view across the floor), the CPU on pad (2, -1), lit in checkerboard and east_half.</summary>
        private static void Arrange(TrainingMatch match, ulong cpu)
        {
            Place(match, MatchConfig.LocalEntityId, new Vector3(-6f, 0f, -5f));
            Place(match, cpu, new Vector3(2f, 0f, -1f));
        }

        private static void Place(TrainingMatch match, ulong id, Vector3 at)
        {
            var sim = match.CaptureSimulation;
            var state = sim.GetState(id);
            state.PX = at.x; state.PZ = at.z; state.PY = at.y + sim.GetDefinition(id).CapsuleHeight / 2f;
            state.VX = state.VY = state.VZ = 0; state.KVX = state.KVY = state.KVZ = 0; state.IsGrounded = true;
            sim.SetState(id, state);
        }

        private static SlopArena.Client.Camera.CameraMount Mount(TrainingMatch match) =>
            typeof(MatchBase).GetField("_cameraMount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match) as SlopArena.Client.Camera.CameraMount;

        private static Camera Gameplay(TrainingMatch match) => Mount(match)?.RenderCamera ?? Camera.main ?? Camera.allCameras.FirstOrDefault();

        private static object State(string name, uint tick, BassDropPresentation presenter, TrainingMatch match, ulong cpu)
        {
            var camera = Gameplay(match);
            var t = camera.transform;
            var c = match.CaptureSimulation.GetState(cpu);
            return new
            {
                name, tick, phase = presenter.Phase, variant = presenter.ActiveVariant, countdown = presenter.CountdownText,
                litPads = presenter.LitPads, placement = BassDropPresentation.Placement.ToString(),
                blast = presenter.Root.Find("Blast")?.gameObject.activeSelf == true,
                cpu = new[] { c.PX, c.PY, c.PZ }, cpuDamage = c.DamagePercent,
                camera = new { position = new[] { t.position.x, t.position.y, t.position.z }, yaw = t.eulerAngles.y, pitch = t.eulerAngles.x, fov = camera.fieldOfView },
            };
        }

        /// <summary>Let rendered frames run (simulation held) so the follow camera settles.</summary>
        private static async Task Settle(int frames)
        {
            int target = Time.frameCount + frames;
            double until = EditorApplication.timeSinceStartup + 5;
            while (Time.frameCount < target && EditorApplication.timeSinceStartup < until) await Task.Delay(1);
        }

        /// <summary>The gameplay camera's own lens behind a spawn, at its orbit radius and 35 degree pitch, looking at the spawn.</summary>
        private static void SpawnView(TrainingMatch match, (float x, float z, float yaw) spawn, string path)
        {
            var main = Gameplay(match);
            var mount = Mount(match);
            float radius = mount != null ? mount.GetOrbitRadius() : 9f;
            var go = new GameObject("Bass Drop spawn view camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(main);
                var focus = new Vector3(spawn.x, 1.2f, spawn.z);
                var rotation = Quaternion.Euler(35f, spawn.yaw, 0f);
                go.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * radius, rotation);
                Render(camera, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static Texture2D Read(Camera camera, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24);
            var target = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Render(Camera camera, string path)
        {
            var texture = Read(camera ?? throw new InvalidOperationException("No gameplay camera."), 1920, 1080);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static byte[] Jpg(Camera camera)
        {
            var texture = Read(camera, 1280, 720);
            var bytes = texture.EncodeToJPG(88);
            Object.DestroyImmediate(texture);
            return bytes;
        }
    }
}
#endif
