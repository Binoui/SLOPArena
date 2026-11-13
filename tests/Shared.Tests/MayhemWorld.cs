using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

    internal static readonly string[] Four = { "fightguy", "manki", "wibou", "bonk" };

    /// <summary>A begun show. Without a roster every contestant uses the plain engine fighter.</summary>
    internal static ServerSimulation NewShow(uint seed = 1337, FriendlyFireMode fire = FriendlyFireMode.Shoves, int count = 4,
        bool reverse = false, string[]? roster = null, bool begin = true)
    {
        var arena = Arena();
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule(fire));
        foreach (int index in reverse ? Enumerable.Range(0, count).Reverse() : Enumerable.Range(0, count))
        {
            var content = roster != null ? BuiltInContentResolver.ResolvePackage(roster[index % roster.Length]) : null;
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

    /// <summary>
    /// The first seed whose show airs <paramref name="channel"/> in <paramref name="act"/>, with its episode. PRIME TIME!
    /// means the classic remix on both children's first twists.
    /// </summary>
    internal static (uint Seed, int Episode) SeedWith(ChannelId channel, int act, TwistId? twist = null)
    {
        for (uint seed = 1; seed < 100_000; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            for (int i = 0; i < MayhemShowTuning.EpisodesPerAct; i++)
            {
                int episode = act * MayhemShowTuning.EpisodesPerAct + i;
                if (plan.ChannelAt(episode) != channel) continue;
                if (plan.AiringAt(episode) is SoloAiring solo ? solo.Draw.Twist == (twist ?? new TwistId(1))
                    : Matches((RemixAiring)plan.AiringAt(episode), PrimePairs.BreakOutDodge, new(1), new(1)))
                    return (seed, episode);
            }
        }
        throw new InvalidOperationException($"No seed airs {channel} in act {act}.");
    }

    private static bool Matches(RemixAiring remix, PrimePairId pair, TwistId goal, TwistId hazard)
        => remix.Pair == pair && remix.Goal.Twist == goal && remix.Hazard.Twist == hazard;

    /// <summary>The first <paramref name="count"/> (seed, boss episode) pairs that air this remix, skipping <paramref name="skip"/>.</summary>
    internal static IEnumerable<(uint Seed, int Episode)> SeedsWithRemix(PrimePairId pair, TwistId goal, TwistId hazard, int count, int skip = 0)
    {
        for (uint seed = 1; seed < 100_000 && count > 0; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            for (int act = 0; act < MayhemShowTuning.Acts && count > 0; act++)
            {
                int episode = act * MayhemShowTuning.EpisodesPerAct + MayhemShowTuning.RegularPerAct;
                if (plan.AiringAt(episode) is not RemixAiring remix || !Matches(remix, pair, goal, hazard)) continue;
                if (skip-- > 0) continue;
                count--;
                yield return (seed, episode);
            }
        }
        if (count > 0) throw new InvalidOperationException($"Too few seeds air pair {pair.Value} with twists {goal.Value}/{hazard.Value}.");
    }

    /// <summary>A show positioned so its next tick calls this remix.</summary>
    internal static (ServerSimulation Sim, int Episode) Remix(PrimePairId pair, TwistId goal, TwistId hazard, int count = 4,
        string[]? roster = null, bool reverse = false, int skip = 0)
    {
        var (seed, episode) = SeedsWithRemix(pair, goal, hazard, 1, skip).Single();
        var sim = NewShow(seed, count: count, reverse: reverse, roster: roster);
        SkipToCall(sim, episode);
        return (sim, episode);
    }

    /// <summary>Every allowed pair with every goal twist and every hazard twist.</summary>
    internal static IEnumerable<(PrimePairEntry Pair, TwistId Goal, TwistId Hazard)> RemixCombinations()
    {
        foreach (var pair in PrimePairs.All)
            foreach (var goal in MayhemChannels.Find(pair.Goal).Twists)
                foreach (var hazard in MayhemChannels.Find(pair.Hazard).Twists)
                    yield return (pair, goal.Id, hazard.Id);
    }

    /// <summary>
    /// Airs <paramref name="airing"/> in <paramref name="episode"/> of a begun show whose clock has not reached that call,
    /// retiming the episode and shifting every later one. It keeps a found case reproducible when the plan's draws change.
    /// </summary>
    internal static void Pin(ServerSimulation sim, int episode, MayhemAiring airing)
    {
        var plan = Plan(sim);
        if (sim.CurrentTick >= plan.CallStart(episode)) throw new InvalidOperationException("Pin before the call.");
        T Field<T>(string name) => (T)typeof(MayhemShowPlan).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan)!;
        var calls = Field<ushort[]>("_callTicks");
        var plays = Field<ushort[]>("_playTicks");
        var starts = Field<uint[]>("_callStart");
        var tier = MayhemShowPlan.TierOf(episode);
        int shift = MayhemSpeed.Call(airing.Timing, tier) + MayhemSpeed.Play(airing.Timing, tier) - calls[episode] - plays[episode];
        Field<MayhemAiring[]>("_airings")[episode] = airing;
        calls[episode] = MayhemSpeed.Call(airing.Timing, tier);
        plays[episode] = MayhemSpeed.Play(airing.Timing, tier);
        for (int e = episode + 1; e < starts.Length; e++) starts[e] = (uint)(starts[e] + shift);
        typeof(MayhemShowPlan).GetField("<EndTick>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plan, (uint)(plan.EndTick + shift));
    }

    /// <summary>The airing <paramref name="seed"/>'s plan would give this remix's children, with their own channel seeds.</summary>
    internal static RemixAiring RemixAt(uint seed, int episode, PrimePairId pair, TwistId goal, TwistId hazard)
    {
        var row = PrimePairs.Find(pair);
        return new RemixAiring(pair, new(row.Goal, goal, MayhemMath.ChannelSeed(seed, episode, 0)),
            new(row.Hazard, hazard, MayhemMath.ChannelSeed(seed, episode, 1)));
    }

    /// <summary>Moves the show to the tick before <paramref name="episode"/>'s call; the next tick starts it.</summary>
    internal static void SkipToCall(ServerSimulation sim, int episode) => sim.SetTick(Plan(sim).CallStart(episode) - 1);

    /// <summary>A show positioned so its next tick calls <paramref name="channel"/> at <paramref name="tier"/>.</summary>
    internal static (ServerSimulation Sim, int Episode) Airing(ChannelId channel, SpeedTier tier, int count = 4,
        string[]? roster = null, FriendlyFireMode fire = FriendlyFireMode.Shoves, bool reverse = false, TwistId? twist = null)
    {
        int act = channel == MayhemChannels.PrimeTime ? 0 : MayhemChannels.Find(channel).Role == ChannelRole.Boss ? MayhemShowTuning.Acts - 1 : (int)tier;
        var (seed, episode) = SeedWith(channel, act, twist);
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
