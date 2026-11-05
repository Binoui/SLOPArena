using SlopArena.Shared.AI;
using SlopArena.Shared.StageHazards;
using Xunit;

namespace SlopArena.Shared.Tests;

public class BassDropHazardTests
{
    internal static ArenaDefinition Arena()
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(StageHazardOffParityProbe.Root, "data/arenas/after_hours.arena"))!.Value;
        arena.HazardAnchors = StageHazardAuthoring.Parse(File.ReadAllText(Path.Combine(StageHazardOffParityProbe.Root,
            "tests/Shared.Tests/Fixtures/StageHazards/after-hours-shell.json")), arena);
        return arena;
    }
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly Dictionary<ulong, InputState> Empty = new();
    private static CharacterState State(ArcadePoint p, float feet = 0) => new()
    { EntityId = 1, PX = p.X, PY = p.Y + Def.CapsuleHeight / 2 + feet, PZ = p.Z,
        State = ActionState.Idle, IsGrounded = feet == 0, MatchState = MatchState.Playing,
        JumpsLeft = Def.Movement.MaxJumps, AirDodgesLeft = 1 };
    private static ServerSimulation Sim(ArenaDefinition arena, uint seed = 1, bool on = true)
    {
        var sim = new ServerSimulation(arena, NoWinMatchRule.ForPractice(on ? StageHazardMode.On : StageHazardMode.Off), stageHazardSeed: seed);
        sim.RegisterEntity(1, Def, State(new(0, 0, 8)));
        var opponent = State(new(0, 0, 9)); opponent.EntityId = 2; sim.RegisterEntity(2, Def, opponent);
        return sim;
    }
    private static HazardAction Action(ArenaDefinition arena, uint seed)
    { var f = new BassDropHazard(arena.HazardAnchors!); return f.Plan(seed, 0, f.NextLock(seed, 0, 1)); }
    public static IEnumerable<object[]> Patterns() => Enumerable.Range(0, 6).Select(i => new object[] { i });
    private static uint Seed(ArenaDefinition arena, int pattern)
    {
        for (uint seed = 0; seed < 10000; seed++) if (Action(arena, seed).Variant == BassDropHazard.PatternNames[pattern]) return seed;
        throw new Exception("Pattern unreachable");
    }
    [Theory, MemberData(nameof(Patterns))]
    public void EveryPadHonorsPatternHeightAndOneHit(int pattern)
    {
        var arena = Arena(); uint seed = Seed(arena, pattern); var action = Action(arena, seed);
        var cells = action.Cells!;
        var sim = Sim(arena, seed);
        for (uint t = 1; t < action.Window.ImpactTick; t++) sim.Tick(Empty);
        var before = sim.CaptureSnapshot();
        foreach (int i in Enumerable.Range(0, 20))
        foreach (float feet in new[] { 0f, .8f })
        {
            sim.RestoreSnapshot(before); sim.SetState(1, State(cells.Anchor.Points[i], feet));
            int hits = 0; bool up = false;
            for (uint t = action.Window.ImpactTick; t <= action.Attack.EndTick + 35; t++)
            {
                sim.Tick(Empty);
                hits += sim.LastTickHits.Count(h => h.OwnerEntityId == StageHazardOwner.EntityId && !h.Blocked);
                up |= sim.GetState(1).KVY > 0;
            }
            int expected = feet == 0 && cells.Indices.Contains(i) ? 1 : 0;
            Assert.Equal(expected, hits); if (expected > 0) Assert.True(up);
        }
        foreach (var p in new[] { new ArcadePoint(-5.8f, 0, 0), new ArcadePoint(5.8f, 0, 0), new ArcadePoint(0, 0, -4.8f), new ArcadePoint(0, 0, 4.8f) })
        {
            sim.RestoreSnapshot(before); sim.SetState(1, State(p)); sim.Tick(Empty);
            Assert.DoesNotContain(sim.LastTickHits, h => h.OwnerEntityId == StageHazardOwner.EntityId);
        }
    }
    [Theory, MemberData(nameof(Patterns))]
    public void WarningTimingGeometryAndEscapeDistance(int pattern)
    {
        var arena = Arena(); var a = Action(arena, Seed(arena, pattern)); var c = a.Cells!;
        Assert.Equal(120u, a.Window.ImpactTick - a.Window.WarningTick);
        Assert.InRange(c.Indices.Count, 8, 12);
        var view = new StageHazardView(a.Window.WarningTick, new("bass_drop", default), a);
        Assert.All(view.Cues, q => { Assert.Equal(a.Window.WarningTick, q.WarningTick); Assert.Same(c, q.Cells); });
        foreach (var h in a.Attacks.Select(a => a.Definition))
        { Assert.InRange(h.Y + h.Radius, 0, .5f); Assert.Equal((sbyte)90, h.KnockbackAngle); }
        for (float x = -5; x <= 5; x += .25f)
        for (float z = -4; z <= 4; z += .25f)
        {
            float distance = MathF.Min(5 - MathF.Abs(x), 4 - MathF.Abs(z));
            foreach (int i in Enumerable.Range(0, 20).Except(c.Indices))
            {
                var p = c.Anchor.Points[i];
                float dx = MathF.Max(0, MathF.Abs(p.X - x) - 1), dz = MathF.Max(0, MathF.Abs(p.Z - z) - 1);
                distance = MathF.Min(distance, MathF.Sqrt(dx * dx + dz * dz));
            }
            Assert.InRange(distance, 0, 2.01f);
        }
    }
    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public void ShieldAndInvincibilityProtect(bool shield, bool invincible)
    {
        var arena = Arena(); var a = Action(arena, 1); var sim = Sim(arena);
        for (uint t = 1; t < a.Window.ImpactTick; t++) sim.Tick(Empty);
        var state = State(a.Cells!.Anchor.Points[a.Cells.Indices[0]]); state.InvincibilityTicks = invincible ? (ushort)100 : (ushort)0; sim.SetState(1, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ShieldHeld = shield } });
        Assert.Equal(0, sim.GetState(1).DamagePercent);
        if (shield) Assert.Contains(sim.LastTickHits, h => h.Blocked);
        else Assert.Empty(sim.LastTickHits);
    }
    [Fact]
    public void SnapshotRestoreAcrossDropAndOffParity()
    {
        var arena = Arena(); var a = Action(arena, 1); var sim = Sim(arena); var restored = Sim(arena, 999);
        for (uint t = 1; t <= a.Window.RetireTick + 2; t++)
        {
            if (t == a.Window.ImpactTick) sim.SetState(1, State(a.Cells!.Anchor.Points[a.Cells.Indices[0]]));
            restored.RestoreSnapshot(sim.CaptureSnapshot()); sim.Tick(Empty); restored.Tick(Empty);
            Assert.Equal(sim.CaptureSnapshot().Data, restored.CaptureSnapshot().Data);
        }
        var anchored = StageHazardOffParityProbe.Fight(arena); var off = Sim(arena, on: false).CaptureSnapshot().Data;
        arena.HazardAnchors = null;
        Assert.Equal(anchored, StageHazardOffParityProbe.Fight(arena));
        Assert.Equal(off, Sim(arena, on: false).CaptureSnapshot().Data);
    }
    [Fact]
    public void FamilyQueryAndMalformedAnchors()
    {
        var arena = Arena(); Assert.True(StageHazardFamilies.IsAvailable(arena));
        var splash = ArenaBinaryFormat.LoadFromFile(Path.Combine(StageHazardOffParityProbe.Root, "data/arenas/splash_deck.arena"))!.Value;
        Assert.True(StageHazardFamilies.IsAvailable(splash));
        arena.HazardAnchors = null; Assert.False(StageHazardFamilies.IsAvailable(arena)); Assert.Throws<ArgumentException>(() => Sim(arena));
        arena.HazardAnchors = new(new[] { KeyValuePair.Create<string, HazardAnchor>("dance_pads", new HazardAnchor.Cells(new[] { new ArcadePoint(0, 0, 0) }, 1, arena)) }, arena);
        Assert.False(StageHazardFamilies.IsAvailable(arena)); Assert.Throws<ArgumentException>(() => Sim(arena));
        arena.Name = "last_stop"; Assert.False(StageHazardFamilies.IsAvailable(arena));
    }
    [Fact]
    public void FrozenActionRejectsChangedGeometry()
    {
        var arena = Arena(); var family = new BassDropHazard(arena.HazardAnchors!); var a = Action(arena, 1);
        var writer = new SnapshotWriter(); family.WriteAction(writer, a); var bytes = writer.ToArray();
        var roundtrip = family.ReadAction(new SnapshotReader(bytes), 1);
        Assert.Equal(a.Variant, roundtrip.Variant);
        var altered = (StageAttack[])a.Attacks.ToArray(); var hitbox = altered[0].Definition; hitbox.Damage++;
        altered[0] = new StageAttack(altered[0].Operation, altered[0].SpawnTick, altered[0].EndTick, hitbox);
        writer = new SnapshotWriter(); family.WriteAction(writer, new HazardAction(a.Ordinal, a.Window, altered, a.PresentationId, a.Variant, a.Cells));
        Assert.Throws<InvalidDataException>(() => family.ReadAction(new SnapshotReader(writer.ToArray()), 1));
    }
    [Fact]
    public void NormalCpuEscapesManySeededDrops()
    {
        var arena = Arena(); int hits = 0;
        for (uint seed = 0; seed < 48; seed++)
        {
            var a = Action(arena, seed); var sim = Sim(arena, seed); var memory = new BotMemory(); var policy = new HeuristicBotPolicy(); var rng = new Random(42);
            for (uint t = 1; t < a.Window.WarningTick; t++) sim.Tick(Empty);
            sim.SetState(1, State(a.Cells!.Anchor.Points[a.Cells.Indices[(int)seed % a.Cells.Indices.Count]]));
            for (uint t = a.Window.WarningTick; t <= a.Attack.EndTick; t++)
            {
                memory.ObserveStageHazards(sim.StageHazards); var self = sim.GetState(1);
                policy.ObserveOpponent(memory, sim.GetState(2), Def); memory.ChooseOpponent(self);
                var input = policy.Decide(self, Def, rng, memory, arena);
                sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
                hits += sim.LastTickHits.Count(h => h.OwnerEntityId == StageHazardOwner.EntityId && !h.Blocked);
            }
            Assert.Equal(0, sim.GetState(1).DamagePercent);
        }
        Assert.Equal(0, hits);
    }
}
