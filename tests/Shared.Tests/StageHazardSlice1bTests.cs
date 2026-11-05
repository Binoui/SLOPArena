using SlopArena.Shared.AI;
using SlopArena.Shared.StageHazards;
using Xunit;

namespace SlopArena.Shared.Tests;

public class StageHazardSlice1bTests
{
    private static ArenaDefinition Arena()
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(StageHazardOffParityProbe.Root, "data/arenas/splash_deck.arena"))!.Value;
        arena.HazardAnchors = StageHazardAuthoring.Parse(File.ReadAllText(Path.Combine(StageHazardOffParityProbe.Root,
            "tests/Shared.Tests/Fixtures/StageHazards/splash-deck-shell.json")), arena);
        return arena;
    }
    private static HazardAction First(in ArenaDefinition arena) { var f = new KrakenHazard(arena.HazardAnchors!); return f.Plan(123, 0, f.NextLock(123, 0, 1)); }
    private static ServerSimulation Sim(ArenaDefinition arena, IMatchRule? rule = null, uint seed = 123)
    {
        var sim = new ServerSimulation(arena, rule ?? NoWinMatchRule.ForPractice(StageHazardMode.On), stageHazardSeed: seed);
        for (ulong id = 1; id <= 2; id++) sim.RegisterEntity(id, TestHelpers.FightGuyDef,
            State(id, id == 1 ? 0 : 12, 1));
        return sim;
    }
    private static CharacterState State(ulong id, float x, float y, float z = 0) => new()
    { EntityId = id, PX = x, PY = y, PZ = z, State = ActionState.Idle, IsGrounded = true,
        MatchState = MatchState.Playing, JumpsLeft = TestHelpers.FightGuyDef.Movement.MaxJumps, AirDodgesLeft = 1 };
    private static readonly Dictionary<ulong, InputState> Empty = new();
    [Fact]
    public void LanesFollowLockPoseAndCuesCarryTheActionIdentityAndGeometry()
    {
        var arena = Arena(); var family = new KrakenHazard(arena.HazardAnchors!);
        var variants = new HashSet<string>();
        for (uint seed = 0; seed < 100; seed++)
        {
            var action = family.Plan(seed, 0, family.NextLock(seed, 0, 1));
            var lane = arena.HazardAnchors!.Entries.Where(e => e.Key.StartsWith("slam_"))
                .Select(e => (e.Key, Box: (HazardAnchor.Box)e.Value))
                .OrderBy(e => MathF.Abs(e.Box.Center.X - action.LockedPose.X)).First();
            string variant = (action.LockedPose.Z >= 0 ? "port." : "starboard.") + lane.Key[5..];
            Assert.Equal(variant, action.Variant); variants.Add(variant);
            var view = new StageHazardView(action.Window.WarningTick,
                new HazardAmbientPose(family.AmbientId, action.LockedPose), action);
            Assert.Equal("kraken", view.AmbientPose.Id);
            Assert.All(view.Cues, cue => { Assert.Equal("kraken.slam", cue.PresentationId); Assert.Equal(variant, cue.Variant); });
            Assert.Equal(action.Attacks.Select(a => a.Definition), view.Cues.Select(c => c.Geometry));
            Assert.Equal(8.5, action.Attacks.Max(a => a.Definition.X + a.Definition.Radius)
                - action.Attacks.Min(a => a.Definition.X - a.Definition.Radius), 4);
        }
        Assert.Equal(6, variants.Count);
    }
    [Fact]
    public void MemoryWithholdsVisibleWarningForNormalReactionDelay()
    {
        var arena = Arena(); var action = First(arena); var memory = new BotMemory();
        uint warning = action.Window.WarningTick;
        int delay = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks;
        for (uint tick = warning; tick <= warning + delay; tick++)
        {
            memory.ObserveStageHazards(new StageHazardView(tick, new HazardAmbientPose("kraken", default), action));
            Assert.Equal(tick == warning + delay, memory.VisibleHazards != null);
        }
        memory.Reset(); Assert.Null(memory.VisibleHazards);
    }
    [Fact]
    public void OffAnchorsNeitherChangeFightNorAddSnapshotBytes()
    {
        var arena = Arena(); var anchored = StageHazardOffParityProbe.Fight(arena);
        arena.HazardAnchors = null;
        Assert.Equal(anchored, StageHazardOffParityProbe.Fight(arena));
        var plain = Sim(arena, NoWinMatchRule.Instance);
        arena.HazardAnchors = Arena().HazardAnchors;
        var withAnchors = Sim(arena, NoWinMatchRule.Instance);
        Assert.Null(withAnchors.StageHazards);
        Assert.Equal(plain.CaptureSnapshot().Data, withAnchors.CaptureSnapshot().Data);
    }
    [Fact]
    public void RuleAdmissionAndMissingAnchorFailureAreExplicit()
    {
        foreach (var format in Enum.GetValues<MatchFormat>())
        {
            if (format != MatchFormat.Stock) Assert.Throws<ArgumentOutOfRangeException>(() => format.CreateRule(3, stageHazards: StageHazardMode.On));
            else Assert.Equal(StageHazardMode.On, Assert.IsType<StockMatchRule>(format.CreateRule(3, stageHazards: StageHazardMode.On)).StageHazards);
        }
        Assert.Equal(StageHazardMode.Off, new StockMatchRule(3).StageHazards);
        Assert.Equal(StageHazardMode.Off, NoWinMatchRule.Instance.StageHazards);
        Assert.NotNull(Sim(Arena()).StageHazards);
        var arena = Arena(); arena.HazardAnchors = null;
        Assert.Throws<ArgumentException>(() => Sim(arena));
        arena.Name = "unregistered"; Assert.Null(Sim(arena).StageHazards);
    }
    [Fact]
    public void FullWorldRestoreAcrossSlamMatchesContinuousIncludingHits()
    {
        var arena = Arena(); var first = First(arena); var h = first.Attacks[first.Attacks.Count / 2].Definition;
        var continuous = Sim(arena); var restored = Sim(arena, seed: 999);
        for (uint tick = 1; tick <= first.Window.RetireTick + 5; tick++)
        {
            if (tick == first.Window.ImpactTick)
            {
                continuous.SetState(1, State(1, h.X, h.Y));
                restored.SetState(1, State(1, h.X, h.Y));
            }
            restored.RestoreSnapshot(continuous.CaptureSnapshot());
            continuous.Tick(Empty); restored.Tick(Empty);
            Assert.Equal(continuous.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
        }
    }
    [Theory]
    [InlineData(false, false, false, 1)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 0)]
    public void SlamHitsOnceLaunchesAndHonorsDefenseAndSafeStrip(bool shield, bool invincible, bool safe, int hits)
    {
        var arena = Arena(); var action = First(arena); var sim = Sim(arena);
        for (uint t = 1; t < action.Window.ImpactTick; t++) sim.Tick(Empty);
        string laneId = "slam_" + action.Variant.Split('.')[1];
        var lane = (HazardAnchor.Box)arena.HazardAnchors!.Entries[laneId];
        float x = lane.Center.X;
        if (safe)
        {
            var lanes = arena.HazardAnchors.Entries.Values.OfType<HazardAnchor.Box>().Where(b => b.HalfExtents.X == 4.25f).OrderBy(b => b.Center.X).ToArray();
            int i = Array.FindIndex(lanes, b => b == lane);
            x = i < 2 ? (lane.Center.X + lanes[i + 1].Center.X) / 2 : (lane.Center.X + lanes[i - 1].Center.X) / 2;
        }
        var state = State(1, x, lane.Center.Y);
        if (invincible) state.InvincibilityTicks = 100;
        sim.SetState(1, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ShieldHeld = shield } };
        int contacts = 0; bool launched = false;
        for (uint t = action.Window.ImpactTick; t <= action.Attack.EndTick + 2; t++)
        {
            sim.Tick(inputs);
            contacts += sim.LastTickHits.Count(h => h.OwnerEntityId == StageHazardOwner.EntityId && h.TargetEntityId == 1 && !h.Blocked);
            var post = sim.GetState(1); launched |= post.KVY > 0 || post.HitstunTicks > 0;
        }
        Assert.Equal(hits, contacts);
        Assert.Equal(hits > 0, sim.GetState(1).DamagePercent > 0);
        if (hits > 0) Assert.True(launched);
    }
    [Theory]
    [InlineData(1, 1)]
    [InlineData(181, 0)]
    public void HazardKoPreservesOnlyRecentFighterCredit(int age, int expectedKos)
    {
        var arena = Arena(); var action = First(arena); var sim = Sim(arena);
        uint fighterHitTick = action.Window.ImpactTick - (uint)age;
        var lane = (HazardAnchor.Box)arena.HazardAnchors!.Entries["slam_" + action.Variant.Split('.')[1]];
        for (uint tick = 1; tick <= action.Window.ImpactTick; tick++)
        {
            if (tick == fighterHitTick)
            {
                sim.SetState(1, State(1, lane.Center.X, lane.Center.Y));
                sim.Resolver.Spawn(new Hitbox { OwnerId = 2, X = lane.Center.X, Y = lane.Center.Y,
                    Radius = 2, Damage = 1, DurationTicks = 1 });
            }
            if (tick == action.Window.ImpactTick) sim.SetState(1, State(1, lane.Center.X, lane.Center.Y));
            sim.Tick(Empty);
        }
        Assert.Contains(sim.LastTickHits, h => h.OwnerEntityId == StageHazardOwner.EntityId && h.TargetEntityId == 1);
        var victim = sim.GetState(1); victim.PY = arena.KillHeight - 10; sim.SetState(1, victim);
        sim.Tick(Empty);
        Assert.Equal(expectedKos, sim.GetKOs(2));
    }
    [Fact]
    public void NoActionOpensAfterOutcomeIncludingPendingWarnings()
    {
        var sim = Sim(Arena(), new StockMatchRule(1, StageHazardMode.On));
        var state = sim.GetState(2); state.Deaths = 1; sim.SetState(2, state);
        Assert.True(sim.EvaluateMatch().IsEnded);
        for (int t = 0; t < 1800; t++) { sim.Tick(Empty); Assert.Empty(sim.StageHazards!.Cues); }
        sim.RestoreSnapshot(sim.CaptureSnapshot());
    }
    [Fact]
    public void NormalCpuSeesDelayedWarningAndLeavesLaneBeforeImpact()
    {
        var arena = Arena(); var action = First(arena); var sim = Sim(arena);
        var lane = (HazardAnchor.Box)arena.HazardAnchors!.Entries["slam_" + action.Variant.Split('.')[1]];
        var memory = new BotMemory(); var policy = new HeuristicBotPolicy(); var rng = new Random(42);
        for (uint t = 1; t < action.Window.WarningTick; t++) sim.Tick(Empty);
        sim.SetState(1, State(1, lane.Center.X, TestHelpers.FightGuyDef.CapsuleHeight / 2, 7));
        for (uint t = action.Window.WarningTick; t <= action.Window.ImpactTick; t++)
        {
            memory.ObserveStageHazards(sim.StageHazards);
            policy.ObserveOpponent(memory, sim.GetState(2), TestHelpers.FightGuyDef);
            var self = sim.GetState(1); memory.ChooseOpponent(self);
            var input = policy.Decide(self, TestHelpers.FightGuyDef, rng, memory, arena);
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        }
        Assert.True(MathF.Abs(sim.GetState(1).PX - lane.Center.X) > lane.HalfExtents.X + TestHelpers.FightGuyDef.CapsuleRadius, $"position={sim.GetState(1).PX},{sim.GetState(1).PY},{sim.GetState(1).PZ}; lane={lane.Center.X}; damage={sim.GetState(1).DamagePercent}");
        Assert.Equal(0, sim.GetState(1).DamagePercent);
    }
}
