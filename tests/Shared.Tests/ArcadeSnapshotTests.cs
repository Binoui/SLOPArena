using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using static SlopArena.Shared.Tests.SlopballTests;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Arcade objects in the World Snapshot (ADR-0039/0040). Every mutable field of the ball and the
/// Slopball rules must round-trip, and a co-op run restored mid-rally must keep playing exactly
/// like the run that produced the snapshot.
/// </summary>
public class ArcadeSnapshotTests
{
    private readonly ITestOutputHelper _output;
    public ArcadeSnapshotTests(ITestOutputHelper output) => _output = output;

    /// <summary>Per-tick scratch the rules rebuild in BeginTick; never part of a snapshot.</summary>
    private static readonly HashSet<string> SlopballScratch = new() { "_attacked", "_cursor" };

    [Fact]
    public void BallSnapshotCoversEveryMutableField()
    {
        var arena = Court();
        var original = new ArcadeBallSimulation(arena.Arcade!.Ball!, arena);
        var rng = new Random(41);
        foreach (var field in MutableFields(typeof(ArcadeBallSimulation)))
            field.SetValue(original, Fill(field.FieldType, rng));
        var copy = new ArcadeBallSimulation(arena.Arcade.Ball!, arena);
        var w = new SnapshotWriter();
        original.Capture().Write(w);
        var r = new SnapshotReader(w.ToArray());
        copy.Apply(ArcadeBallSimulation.Snapshot.Read(r));
        Assert.True(r.AtEnd);
        foreach (var field in MutableFields(typeof(ArcadeBallSimulation)))
            Assert.Equal(field.GetValue(original), field.GetValue(copy));
    }

    [Fact]
    public void SlopballSnapshotCoversEveryMutableFieldExceptTickScratch()
    {
        var arena = Court();
        var ball = new ArcadeBallSimulation(arena.Arcade!.Ball!, arena);
        var original = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball!, ball);
        var rng = new Random(42);
        foreach (var field in MutableFields(typeof(SlopballSimulation)))
            if (!SlopballScratch.Contains(field.Name))
                field.SetValue(original, Fill(field.FieldType, rng));
        var copy = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball!, ball);
        var w = new SnapshotWriter();
        original.Capture().Write(w);
        var r = new SnapshotReader(w.ToArray());
        copy.Apply(SlopballSimulation.Snapshot.Read(r));
        Assert.True(r.AtEnd);
        foreach (var field in MutableFields(typeof(SlopballSimulation)))
            if (!SlopballScratch.Contains(field.Name))
                Assert.Equal(field.GetValue(original), field.GetValue(copy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoopRunRestoredMidRallyMatchesTheContinuousOneEveryTick(bool friendlyFire)
    {
        var arena = WorldSnapshotTests.LoadArena("slopball");
        var roster = new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Bonk };
        var ids = Enumerable.Range(1, roster.Length).Select(i => (ulong)i).ToArray();
        var continuous = NewRun(arena, roster, reverseRegistration: false, friendlyFire);
        var restored = NewRun(arena, roster, reverseRegistration: true, friendlyFire);
        restored.RestoreSnapshot(continuous.CaptureSnapshot());

        var rng = new Random(2026);
        var inputs = new Dictionary<ulong, InputState>();
        int nextRestore = rng.Next(1, 41), restores = 0, serves = 0, ballHits = 0, ballTicks = 0, misses = 0;
        for (int tick = 1; tick <= 3600; tick++)
        {
            ChaseTheBall(continuous, ids, rng, inputs);
            continuous.Tick(new Dictionary<ulong, InputState>(inputs));
            restored.Tick(new Dictionary<ulong, InputState>(inputs));
            serves += continuous.LastTickSlopballEvents.Count(e => e.Kind == SlopballEventKind.Served);
            misses += continuous.LastTickSlopballEvents.Count(e => e.Kind == SlopballEventKind.Missed);
            var ball = continuous.ArcadeBall!.Value;
            if (ball.Active) ballTicks++;
            ballHits += continuous.LastTickHits.Count(h => h.TargetEntityId == ball.EntityId);

            var expected = continuous.CaptureSnapshot();
            var actual = restored.CaptureSnapshot();
            Assert.True(expected.ContentEquals(actual), $"Restored co-op run diverged at tick {tick}.");
            if (--nextRestore == 0)
            {
                restored = NewRun(arena, roster, reverseRegistration: true, friendlyFire);
                restored.RestoreSnapshot(expected);
                restores++;
                nextRestore = rng.Next(1, 41);
            }
        }
        _output.WriteLine($"restores {restores}, serves {serves}, ball ticks {ballTicks}, ball hits {ballHits}, misses {misses}");
        Assert.True(restores >= 100, $"restores {restores}");
        Assert.True(serves >= 8, $"serves {serves}");
        Assert.True(ballTicks >= 600, $"ball ticks {ballTicks}");
        Assert.True(ballHits >= 3, $"ball hits {ballHits}");
        Assert.True(misses >= 1, $"misses {misses}");
    }

    [Fact]
    public void SnapshotFromAnotherKindOfSimulationIsRejected()
    {
        var court = WorldSnapshotTests.LoadArena("slopball");
        var roster = new[] { CharacterClass.FightGuy, CharacterClass.Manki };
        var run = NewRun(court, roster, reverseRegistration: false);
        var fight = WorldSnapshotTests.NewMatch(WorldSnapshotTests.LoadArena("slop_pit"), roster, reverseRegistration: false);
        var before = run.CaptureSnapshot();
        Assert.Throws<InvalidDataException>(() => run.RestoreSnapshot(fight.CaptureSnapshot()));
        Assert.Throws<InvalidDataException>(() => fight.RestoreSnapshot(run.CaptureSnapshot()));
        Assert.True(run.CaptureSnapshot().ContentEquals(before));
    }

    /// <summary>A co-op run as the GameHost sets it up: fighters 1..N on the co-op starts.</summary>
    internal static ServerSimulation NewRun(ArenaDefinition arena, CharacterClass[] roster, bool reverseRegistration,
        bool friendlyFire = false)
    {
        var sim = new ServerSimulation(arena, new SlopballCoopMatchRule(friendlyFire: friendlyFire ? FriendlyFireMode.On : FriendlyFireMode.Off));
        var order = Enumerable.Range(0, roster.Length).ToList();
        if (reverseRegistration) order.Reverse();
        foreach (int slot in order)
        {
            var content = BuiltInContentResolver.Resolve(roster[slot]);
            var start = MatchFormat.SlopballCoop.StartFor(arena, slot, roster.Length);
            sim.RegisterEntity((ulong)(slot + 1), content.Definition, new CharacterState
            {
                PX = start.X, PY = start.Y, PZ = start.Z, FacingYaw = start.Yaw,
                State = ActionState.Idle, IsGrounded = true,
                JumpsLeft = content.Definition.Movement.MaxJumps, AirDodgesLeft = 1,
            }, content.BakedAnimation);
        }
        sim.BeginSlopballRun(0x51A7);
        return sim;
    }

    private static readonly byte[] Slots =
    {
        AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4,
        AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
    };

    /// <summary>Every fighter runs under the ball and swings at it, sometimes jumping or locking on.</summary>
    private static void ChaseTheBall(ServerSimulation sim, ulong[] ids, Random rng, Dictionary<ulong, InputState> inputs)
    {
        inputs.Clear();
        var ball = sim.ArcadeBall!.Value;
        foreach (ulong id in ids)
        {
            var me = sim.GetState(id);
            float tx = ball.Active ? ball.Position.X : (id - 2f) * 2.5f;
            float tz = ball.Active ? MathF.Min(ball.Position.Z, -1f) : -6f;
            float dx = tx - me.PX, dz = tz - me.PZ, distance = MathF.Sqrt(dx * dx + dz * dz);
            var input = new InputState();
            if (distance > .5f) { input.MoveX = dx / distance; input.MoveY = dz / distance; }
            short yaw = (short)Math.Clamp(MathF.Atan2(dx, dz) * 18000f / MathF.PI, -18000f, 18000f);
            input.FacingYaw = yaw; input.AimYaw = yaw;
            if (ball.Active && rng.Next(3) == 0) input.TargetEntityId = ServerSimulation.ArcadeBallTargetInputId;
            if (ball.Active && distance < 3f && rng.Next(4) == 0) input.ActiveSlot = Slots[rng.Next(Slots.Length)];
            if (ball.Active && ball.Position.Y > 1.8f && distance < 2.5f && rng.Next(5) == 0)
            { input.Jump = true; input.JumpHeld = true; }
            inputs[id] = input;
        }
    }

    [Theory]
    [InlineData(41u)]
    [InlineData(1337u)]
    public void MayhemRestoreBeforeEveryTickMatchesContinuousRoundsAndEvents(uint seed)
    {
        var continuous = MankiMayhemTests.NewMatch(seed);
        var restored = MankiMayhemTests.NewMatch(seed, reverse: true);
        var phases = new HashSet<MankiMayhemPhase>();
        var cards = new HashSet<MankiMayhemCard>();
        int restores = 0, attachedTicks = 0, benchTicks = 0;
        while (!continuous.EvaluateMatch().IsEnded && continuous.CurrentTick < 20000)
        {
            if (continuous.CurrentTick == 10)
            {
                var carrier = continuous.GetState(1);
                continuous.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId,
                    X = carrier.PX, Y = carrier.PY, Z = carrier.PZ, Radius = .4f, DurationTicks = 2,
                    StickyFuseTicks = 30, Explosion = new ProjectileExplosion { Radius = 2, Damage = 12, DurationTicks = 2 } });
            }
            if (continuous.CurrentTick == 22) MankiMayhemTests.Ko(continuous, 2);
            if (continuous.CurrentTick == 30) continuous.WithdrawMayhemPlayer(3);
            if (continuous.MayhemWalls.FirstOrDefault(w => w.Blocks) is { EntityId: > 0 } wall
                && continuous.CurrentTick % 17 == 0)
                continuous.Resolver.Spawn(new Hitbox { OwnerId = 1, X = wall.Center.X, Y = 1, Z = wall.Center.Z,
                    Radius = .4f, Damage = 1, DurationTicks = 1 });
            continuous.ClearPresentationEvents();
            restored.RestoreSnapshot(continuous.CaptureSnapshot()); restores++;
            var inputs = new Dictionary<ulong, InputState>
            {
                [1] = new() { MoveX = continuous.CurrentTick % 80 < 40 ? .1f : -.1f },
                [2] = new() { Jump = continuous.CurrentTick % 101 == 0 },
            };
            continuous.Tick(new(inputs)); restored.Tick(new(inputs));
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()),
                $"Mayhem seed {seed} diverged at tick {continuous.CurrentTick}.");
            Assert.Equal(continuous.LastTickMayhemEvents, restored.LastTickMayhemEvents);
            Assert.Equal(continuous.GetPresentationEvents(), restored.GetPresentationEvents());
            phases.Add(continuous.MankiMayhem!.Value.Phase);
            cards.Add(continuous.MankiMayhem!.Value.Card);
            if (continuous.Resolver.GetActiveHitboxes().Any(h => h.AttachedEntityId != 0)) attachedTicks++;
            if (continuous.IsEntityInactive(2)) benchTicks++;
        }
        Assert.True(continuous.EvaluateMatch().IsEnded);
        Assert.True(restores > 500); Assert.True(attachedTicks >= 30); Assert.True(benchTicks > 20);
        Assert.Equal(6, phases.Count);
        Assert.Equal(Enum.GetValues<MankiMayhemCard>().Order(), cards.Order());
        _output.WriteLine($"seed {seed}: restores {restores}, attached ticks {attachedTicks}, bench ticks {benchTicks}");
    }

    private static IEnumerable<FieldInfo> MutableFields(Type type)
        => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsInitOnly);

    private static object Fill(Type type, Random rng)
    {
        if (type == typeof(bool)) return true;
        if (type == typeof(byte)) return (byte)rng.Next(1, 256);
        if (type == typeof(ushort)) return (ushort)rng.Next(1, ushort.MaxValue);
        if (type == typeof(uint)) return (uint)rng.Next(1, int.MaxValue);
        if (type == typeof(ulong)) return ArcadeBallSimulation.FirstEntityId + (ulong)rng.Next(1, int.MaxValue);
        if (type == typeof(float)) return (float)(rng.NextDouble() * 200 - 100);
        if (type == typeof(SlopballPhase)) return SlopballPhase.Live;
        if (type == typeof(SlopballShot))
            return new SlopballShot((ulong)rng.Next(1, 999), new(1, 2, 3), new(4, 5, 6), (ushort)rng.Next(1, 300),
                SlopballServe.Curveball, (float)rng.NextDouble());
        throw new InvalidOperationException($"No snapshot test value for {type.Name}: add it to the snapshot.");
    }
}
