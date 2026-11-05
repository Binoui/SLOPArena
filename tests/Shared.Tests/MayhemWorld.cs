using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared.AI;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Builds Manki Mayhem shows on the real deck and drives them tick by tick.</summary>
internal static class MayhemWorld
{
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    internal static ArenaDefinition Arena()
    {
        var arena = WorldSnapshotTests.LoadArena("manki_mayhem");
        arena.Arcade = ArcadeStageDefinition.ForMayhem("manki_mayhem", 1, new(new(0, 6.5f, 41.5f), -12, 12, -10, 10, 0));
        return arena;
    }

    internal static readonly CharacterClass[] Four = { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };

    /// <summary>A begun show. Without a roster every contestant uses the plain engine fighter.</summary>
    internal static ServerSimulation NewShow(uint seed = 1337, FriendlyFireMode fire = FriendlyFireMode.Shoves, int count = 4,
        bool reverse = false, CharacterClass[]? roster = null, bool begin = true)
    {
        var arena = Arena();
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule(fire));
        foreach (int index in reverse ? Enumerable.Range(0, count).Reverse() : Enumerable.Range(0, count))
        {
            var content = roster != null ? BuiltInContentResolver.Resolve(roster[index % roster.Length]) : null;
            var def = content?.Definition ?? TestHelpers.EngineDef with
            {
                CaptureGeometry = new CookedCaptureGeometry(1.5f, .8f, 1.2f, .6f,
                    new CaptureAnchor(0, .6f, .2f), new CaptureAnchor(0, .6f, .65f)),
            };
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, index, count);
            var state = TestHelpers.PlayerState(spawn.X, spawn.Z);
            state.EntityId = (ulong)index + 1; state.PY = def.CapsuleHeight * .5f; state.FacingYaw = spawn.Yaw;
            sim.RegisterEntity(state.EntityId, def, state, content?.BakedAnimation);
        }
        if (begin) sim.BeginMankiMayhem(seed);
        return sim;
    }

    internal static MayhemShowPlan Plan(ServerSimulation sim) => sim.MayhemShow!.Plan!;
    internal static MayhemShowView View(ServerSimulation sim) => sim.MayhemView!;

    internal static void Step(ServerSimulation sim, int ticks = 1)
    { for (int t = 0; t < ticks; t++) sim.Tick(new()); }

    /// <summary>The first seed whose show airs <paramref name="channel"/> in <paramref name="act"/>, with its episode.</summary>
    internal static (uint Seed, int Episode) SeedWith(ChannelId channel, int act)
    {
        for (uint seed = 1; seed < 100_000; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            for (int i = 0; i < MayhemShowTuning.EpisodesPerAct; i++)
            {
                int episode = act * MayhemShowTuning.EpisodesPerAct + i;
                if (plan.ChannelAt(episode) == channel) return (seed, episode);
            }
        }
        throw new InvalidOperationException($"No seed airs {channel} in act {act}.");
    }

    /// <summary>Moves the show to the tick before <paramref name="episode"/>'s call; the next tick starts it.</summary>
    internal static void SkipToCall(ServerSimulation sim, int episode) => sim.SetTick(Plan(sim).CallStart(episode) - 1);

    /// <summary>A show positioned so its next tick calls <paramref name="channel"/> at <paramref name="tier"/>.</summary>
    internal static (ServerSimulation Sim, int Episode) Airing(ChannelId channel, SpeedTier tier, int count = 4,
        CharacterClass[]? roster = null, FriendlyFireMode fire = FriendlyFireMode.Shoves, bool reverse = false)
    {
        int act = MayhemChannels.Find(channel).Role == ChannelRole.Boss ? MayhemShowTuning.Acts - 1 : (int)tier;
        var (seed, episode) = SeedWith(channel, act);
        var sim = NewShow(seed, fire, count, reverse, roster);
        SkipToCall(sim, episode);
        return (sim, episode);
    }

    internal static Dictionary<ulong, MayhemCpu> Cpus(ServerSimulation sim, int reactionDelayTicks)
        => sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(reactionDelayTicks));

    internal static void StepCpus(ServerSimulation sim, Dictionary<ulong, MayhemCpu> cpus)
    {
        var inputs = new Dictionary<ulong, InputState>();
        foreach (var (id, cpu) in cpus) inputs[id] = cpu.Decide(sim, id);
        sim.Tick(inputs);
    }

    /// <summary>Runs from the call through the deadline commit; returns each contestant's pass for that episode.</summary>
    internal static bool[] RunEpisode(ServerSimulation sim, int episode, Action<ServerSimulation> step)
    {
        uint deadline = Plan(sim).Deadline(episode);
        while (sim.CurrentTick < deadline) step(sim);
        Assert.Equal(MayhemPhase.Verdict, View(sim).Cursor.Phase);
        return View(sim).Contestants.Select(c => (c.PassMask >> episode & 1) != 0).ToArray();
    }

    internal static CharacterState Ko(ServerSimulation sim, ulong id, float y = -100f)
    {
        var state = sim.GetState(id); state.PY = y; state.IsGrounded = false; sim.SetState(id, state);
        return state;
    }
}
