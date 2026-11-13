#if UNITY_EDITOR
using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Shared.StageHazards;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class SlopArenaStageHazardCommands
    {
        private const string Key = "SlopArena.StageHazardSmoke";
        private sealed class Saved
        {
            public StageHazardMode mode;
            public uint seed;
            public MatchFormat format;
            public string arena;
            public SoloSeatState[] seats;
        }

        [CliCommand("sloparena.stage.hazards.prepare", "Prepare a disposable Splash Deck Training or Local Brawl hazard smoke. Always stop and restore afterwards.", MainThreadRequired = true)]
        public static object Prepare([CliArg("on", "Enable hazards.")] bool on = true,
            [CliArg("solo", "Run the normal Local Brawl scene instead of Training.")] bool solo = false,
            [CliArg("stage", "Registered offline hazard stage.")] string stage = "splash_deck")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetString(Key, "") != "")
                throw new InvalidOperationException("Stop and restore any prior hazard smoke first.");
            SessionState.SetString(Key, JsonConvert.SerializeObject(new Saved { mode = MatchConfig.StageHazards,
                seed = MatchConfig.StageHazardSeed, format = MatchConfig.Format, arena = MatchConfig.ArenaName,
                seats = MatchConfig.SoloSeats.Select(SoloSeatState.Capture).ToArray() }));
            SlopArenaStageViewCommands.Prepare(stage);
            MatchConfig.StageHazards = on ? StageHazardMode.On : StageHazardMode.Off;
            MatchConfig.StageHazardSeed = 123;
            MatchConfig.Format = MatchFormat.Stock;
            MatchConfig.ArenaName = stage;
            MatchConfig.Seat(2) = new SoloSeat { Kind = SeatKind.Cpu, Fighter = "fightguy", Difficulty = CpuDifficulty.Normal };
            if (solo)
            {
                MatchConfig.Mode = GameMode.Solo;
                MatchConfig.LocalMode = LocalPlayMode.Brawl;
                MatchConfig.Seat(2) = new SoloSeat { Kind = SeatKind.Cpu, Fighter = "fightguy", Difficulty = CpuDifficulty.Normal };
                MatchConfig.Seat(3).Kind = SeatKind.Open; MatchConfig.Seat(4).Kind = SeatKind.Open;
                EditorSceneManager.OpenScene("Assets/Scenes/Arena_Offline.unity", OpenSceneMode.Single);
            }
            return new { success = true, on, solo, seed = MatchConfig.StageHazardSeed };
        }

        [CliCommand("sloparena.stage.hazards.smoke", "Observe a real Play Mode slam, hit and CPU escape with tick evidence and camera captures; Off verifies root absence.", MainThreadRequired = true)]
        public static async Task<object> Smoke([CliArg("output", "Fresh capture directory below tmp/.")] string output = "tmp/stage-hazards/impl/unity-smoke")
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            double deadline = EditorApplication.timeSinceStartup + 23;
            TrainingMatch match = null;
            while (match == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Match did not initialize.");
                await Task.Delay(20); match = Object.FindFirstObjectByType<TrainingMatch>();
            }
            bool on = MatchConfig.StageHazards == StageHazardMode.On;
            bool solo = MatchConfig.Mode == GameMode.Solo;
            if (!on)
            {
                bool absent = match.CaptureSimulation.StageHazards == null
                    && !match.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Stage Hazards (presentation only)");
                await SlopArenaCaptureCommands.Capture(output + "/training-off.png", "camera");
                Debug.Log($"[StageHazardSmoke] off rootAbsent={absent}");
                return new { success = absent, on, rootAbsent = absent };
            }
            if (solo)
            {
                uint first = match.CaptureTick;
                while (match.CaptureSimulation.StageHazards.Cues.Count == 0)
                {
                    if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No Brawl warning.");
                    await Task.Delay(20);
                }
                await SlopArenaCaptureCommands.Capture(output + "/local-brawl-on.png", "camera");
                Debug.Log($"[StageHazardSmoke] localBrawl on cpu={match.NpcCount} tick={match.CaptureTick}");
                return new { success = match.NpcCount > 0, solo, tick = match.CaptureTick, first, cpu = match.NpcCount };
            }
            object owner = new object();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Capture stepping is already owned.");
            if (!match.TryAcquireCaptureInputSequence(owner)) { match.ReleaseCaptureStepping(owner); throw new InvalidOperationException("Capture input is already owned."); }
            match.SetCaptureSequenceInput(owner, default);
            bool hit = false, escaped = false, positioned = false;
            uint impact = 0; float centerX = 0, halfWidth = 0;
            float launch = 0; uint hitTick = 0; int hazardHits = 0;
            try
            {
                match.SetNpcDifficulty(CpuDifficulty.Normal); match.SetNpcMode(NpcAiMode.Attack);
                for (int i = 0; i < 1100; i++)
                {
                    var sim = match.CaptureSimulation;
                    var cues = sim.StageHazards.Cues;
                    if (!positioned && cues.Count > 0)
                    {
                        match.ResetTrainingPositions();
                        var h = cues[cues.Count / 2].Geometry;
                        centerX = h.X; halfWidth = cues.Max(c => c.Geometry.X) - centerX + h.Radius;
                        impact = cues[0].ImpactTick;
                        foreach (ulong id in new[] { MatchConfig.LocalEntityId, match.GetNpcIdAt(0) })
                        {
                            var state = sim.GetState(id);
                            state.PX = centerX; state.PZ = id == MatchConfig.LocalEntityId ? -7 : 7;
                            state.PY = sim.GetDefinition(id).CapsuleHeight / 2; state.VX = state.VY = state.VZ = 0;
                            state.IsGrounded = true; state.DamagePercent = 0;
                            sim.SetState(id, state);
                        }
                        positioned = true;
                        await SlopArenaCaptureCommands.Capture(output + "/training-warning-reset.png", "camera");
                    }
                    match.CaptureStep(owner);
                    foreach (var contact in match.CaptureLastTickHits)
                        if (contact.OwnerEntityId == StageHazardOwner.EntityId)
                        {
                            hazardHits++;
                            Debug.Log($"[StageHazardSmoke] contact tick={sim.StageHazards.Tick} target={contact.TargetEntityId} damage={contact.Damage}");
                            if (contact.TargetEntityId == MatchConfig.LocalEntityId) { hit = true; hitTick = match.CaptureTick; }
                        }
                    if (positioned && sim.StageHazards.Tick >= impact)
                    {
                        var cpu = sim.GetState(match.GetNpcIdAt(0));
                        if (sim.StageHazards.Tick == impact) escaped = Math.Abs(cpu.PX - centerX) > halfWidth + sim.GetDefinition(cpu.EntityId).CapsuleRadius && cpu.DamagePercent == 0;
                        var player = sim.GetState(MatchConfig.LocalEntityId);
                        launch = Mathf.Max(launch, new Vector3(player.KVX, player.KVY, player.KVZ).magnitude);
                        if (sim.StageHazards.Tick == impact)
                            await SlopArenaCaptureCommands.Capture(output + "/training-impact-reset.png", "camera");
                        if (sim.StageHazards.Tick >= impact + 35) break;
                    }
                    if (i % 20 == 0) await Task.Delay(1);
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Smoke exceeded its bound.");
                }
            }
            finally { match.ReleaseCaptureInputSequence(owner); match.ReleaseCaptureStepping(owner); }
            await SlopArenaCaptureCommands.Capture(output + "/training-launched-reset.png", "camera");
            match.SetStageHazards(StageHazardMode.Off);
            await Task.Delay(40);
            bool offRebuilt = match.CaptureSimulation.StageHazards == null
                && !match.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Stage Hazards (presentation only)");
            match.SetStageHazards(StageHazardMode.On);
            bool onRebuilt = match.CaptureSimulation.StageHazards != null && match.CaptureSimulation.StageHazards.Tick == 0;
            Debug.Log($"[StageHazardSmoke] offRebuilt={offRebuilt} onRebuilt={onRebuilt}");
            Debug.Log($"[StageHazardSmoke] hit={hit} hitTick={hitTick} launch={launch} cpuEscaped={escaped} contacts={hazardHits}");
            return new { success = hit && launch > 0 && escaped && offRebuilt && onRebuilt, offRebuilt, onRebuilt, hit, hitTick, launch, cpuEscaped = escaped, hazardHits, impact };
        }

        [CliCommand("sloparena.stage.hazards.avoidance-smoke", "Run six arranged Local Brawl warnings with the real Normal CPU and idle spawn player.", MainThreadRequired = true)]
        public static async Task<object> AvoidanceSmoke(
            [CliArg("output", "Capture directory below tmp/.")] string output = "tmp/stage-hazards/impl/fix69")
        {
            double deadline = EditorApplication.timeSinceStartup + 24;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || match.CaptureSimulation == null)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("No match.");
                await Task.Delay(20);
            }
            if (MatchConfig.Mode != GameMode.Solo || match.CaptureSimulation.StageHazards == null)
                throw new InvalidOperationException("Prepare Local Brawl with hazards On first.");
            var owner = new object();
            if (!match.TryAcquireCaptureStepping(owner)) throw new InvalidOperationException("Stepping busy.");
            if (!match.TryAcquireCaptureInputSequence(owner)) { match.ReleaseCaptureStepping(owner); throw new InvalidOperationException("Input busy."); }
            int samples = 0, cpuHits = 0, spawnHits = 0;
            uint ordinal = uint.MaxValue, end = 0;
            var rows = new System.Collections.Generic.List<object>();
            var trace = new System.Collections.Generic.List<string>();
            try
            {
                match.SetCaptureSequenceInput(owner, default);
                match.SetNpcDifficulty(CpuDifficulty.Normal); match.SetNpcMode(NpcAiMode.Attack);
                for (int i = 0; i < 6500; i++)
                {
                    var sim = match.CaptureSimulation;
                    var cues = sim.StageHazards.Cues;
                    if (cues.Count > 0 && ordinal != cues[0].Ordinal && sim.CurrentTick < cues[0].ImpactTick)
                    {
                        match.ResetTrainingPositions();
                        ordinal = cues[0].Ordinal; end = cues[0].EndTick;
                        float left = cues.Min(c => c.Geometry.X - c.Geometry.Radius);
                        float right = cues.Max(c => c.Geometry.X + c.Geometry.Radius);
                        ulong cpuId = match.GetNpcIdAt(0);
                        var cpu = sim.GetState(cpuId); var def = sim.GetDefinition(cpuId);
                        cpu.PX = samples % 2 == 0 ? (left + right) / 2 : (left + right > 0 ? left - def.CapsuleRadius * .5f : right + def.CapsuleRadius * .5f);
                        cpu.PZ = 7; cpu.PY = def.CapsuleHeight / 2;
                        cpu.VX = cpu.VY = cpu.VZ = 0; cpu.IsGrounded = true;
                        sim.SetState(cpuId, cpu);
                        var arena = SlopArena.Shared.ArenaBinaryFormat.LoadFromFile(System.IO.Path.GetFullPath("../../data/arenas/splash_deck.arena")).Value;
                        var spawn = arena.SpawnPoints[samples % 2];
                        var player = sim.GetState(MatchConfig.LocalEntityId);
                        player.PX = spawn.X; player.PY = spawn.Y; player.PZ = spawn.Z;
                        player.VX = player.VY = player.VZ = 0;
                        sim.SetState(player.EntityId, player);
                        rows.Add(new { ordinal, tick = sim.CurrentTick, impact = cues[0].ImpactTick, end,
                            cpuX = cpu.PX, cpuZ = cpu.PZ, left, right, spawn = samples % 2 });
                        samples++;
                        if (samples == 1 || samples == 6)
                            await SlopArenaCaptureCommands.Capture(output + "/brawl-warning-" + samples + ".png", "camera");
                    }
                    match.CaptureStep(owner);
                    foreach (var hit in match.CaptureLastTickHits)
                        if (hit.OwnerEntityId == StageHazardOwner.EntityId && !hit.Blocked)
                        {
                            if (hit.TargetEntityId == match.GetNpcIdAt(0)) cpuHits++;
                            if (hit.TargetEntityId == MatchConfig.LocalEntityId) spawnHits++;
                        }
                    var state = sim.GetState(match.GetNpcIdAt(0));
                    trace.Add(JsonConvert.SerializeObject(new { tick = sim.CurrentTick, ordinal, state.PX, state.PY, state.PZ, cpuHits, spawnHits }));
                    if (samples > 0 && sim.CurrentTick == end - 4 && (samples == 1 || samples == 6))
                        await SlopArenaCaptureCommands.Capture(output + "/brawl-impact-" + samples + ".png", "camera");
                    if (samples >= 6 && sim.CurrentTick > end + 18) break;
                    if (i % 40 == 0) await Task.Delay(1);
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Avoidance smoke exceeded bound.");
                }
                System.IO.File.WriteAllLines(System.IO.Path.GetFullPath("../../" + output + "/brawl-trace.jsonl"), trace);
                return new { success = samples >= 6 && cpuHits == 0 && spawnHits == 0, samples, cpuHits, spawnHits, rows };
            }
            finally { match.ReleaseCaptureInputSequence(owner); match.ReleaseCaptureStepping(owner); }
        }

        [CliCommand("sloparena.stage.hazards.restore", "Restore hazard smoke configuration after stopped Play Mode.", MainThreadRequired = true)]
        public static object Restore()
        {
            SlopArenaStageViewCommands.Restore();
            string json = SessionState.GetString(Key, "");
            if (json == "") return new { success = true };
            var saved = JsonConvert.DeserializeObject<Saved>(json);
            MatchConfig.StageHazards = saved.mode; MatchConfig.StageHazardSeed = saved.seed;
            MatchConfig.Format = saved.format; MatchConfig.ArenaName = saved.arena;
            Array.Copy(saved.seats.Select(s => s.Restore()).ToArray(), MatchConfig.SoloSeats, saved.seats.Length);
            SessionState.EraseString(Key);
            return new { success = true };
        }
    }
}
#endif
