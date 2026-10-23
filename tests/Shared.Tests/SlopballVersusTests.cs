using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using static SlopArena.Shared.Tests.SlopballTests;

namespace SlopArena.Shared.Tests;

public class SlopballVersusTests
{
    internal static Dictionary<ulong, SlopballCamp> Camps(int count = 2) => Enumerable.Range(1, count)
        .ToDictionary(i => (ulong)i, i => i % 2 == 1 ? SlopballCamp.Teal : SlopballCamp.Clay);

    internal static ServerSimulation Match(int count = 2, bool ballOnly = false)
    {
        var arena = Court();
        var sim = new ServerSimulation(arena, new SlopballVersusMatchRule(Camps(count), ballOnly));
        for (ulong id = 1; id <= (ulong)count; id++)
            sim.RegisterEntity(id, TestHelpers.EngineDef, TestHelpers.PlayerState());
        sim.BeginVersusSlopball();
        sim.Tick(new());
        return sim;
    }

    [Fact]
    public void CampsRequireTwoOrFourPlayersAndBalancedMembership()
    {
        Assert.Throws<ArgumentException>(() => new SlopballVersusMatchRule(Camps(3)));
        Assert.Throws<ArgumentException>(() => new SlopballVersusMatchRule(new Dictionary<ulong, SlopballCamp>
            { [1] = SlopballCamp.Teal, [2] = SlopballCamp.Teal }));
        Assert.Throws<ArgumentException>(() => new ServerSimulation(TestHelpers.TestArena(), new SlopballVersusMatchRule(Camps())));
        var input = Camps(); var rule = new SlopballVersusMatchRule(input);
        input[1] = SlopballCamp.Clay;
        Assert.Equal(SlopballCamp.Teal, rule.CampOf(1));
    }

    [Fact]
    public void StartsMirrorTheExistingCourtWithoutChangingTheSoloStart()
    {
        var arena = Court();
        var original = arena.SpawnPoints[0];
        var camps = Camps(4).Values.ToArray();
        var starts = Enumerable.Range(0, 4).Select(i => MatchFormat.SlopballVersus.StartFor(arena, i, 4, camps)).ToArray();
        Assert.Equal(starts[0].X, starts[1].X);
        Assert.Equal(starts[2].X, starts[3].X);
        Assert.Equal(-starts[0].Z, starts[1].Z);
        Assert.Equal(MathF.PI, starts[1].Yaw - starts[0].Yaw);
        Assert.True(starts[2].X > starts[0].X);
        Assert.Equal(original, arena.SpawnPoints[0]);
    }

    [Fact]
    public void TheBallWaitsForItsServerAndDoesNotFallOrAcceptAnOpponentHit()
    {
        var sim = Match();
        var ball = sim.ArcadeBall!.Value;
        for (int i = 0; i < 180; i++) sim.Tick(new());
        Assert.Equal(ball, sim.ArcadeBall.Value);
        Assert.Equal(SlopballVersusPhase.HeldServe, sim.SlopballVersus!.Value.Phase);
        Strike(sim, 2);
        sim.Tick(new());
        Assert.Equal(SlopballVersusPhase.HeldServe, sim.SlopballVersus.Value.Phase);
        Assert.DoesNotContain(sim.LastTickHits, h => h.TargetEntityId == ball.EntityId);
        Strike(sim, 1);
        sim.Tick(new());
        Assert.Equal(SlopballVersusPhase.Rally, sim.SlopballVersus.Value.Phase);
        Assert.Contains(sim.LastTickSlopballEvents, e => e.Kind == SlopballEventKind.Served && e.Camp == SlopballCamp.Teal);
        Assert.True(sim.ArcadeBall.Value.DamagePercent > 0);
    }

    [Theory]
    [InlineData(-3f, SlopballCamp.Clay)]
    [InlineData(3f, SlopballCamp.Teal)]
    public void FloorContactAwardsExactlyOnePointAndTheConcedingCampServes(float floorZ, SlopballCamp scorer)
    {
        var arena = Court();
        var ball = new ArcadeBallSimulation(arena.Arcade!.Ball!, arena);
        var rules = new SlopballVersusSimulation(arena.Arcade.Slopball!, new SlopballVersusMatchRule(Camps()), ball);
        rules.Begin(); rules.BeginTick(ball, _ => new(0, 2, -3));
        Assert.True(rules.BeforeAttack(ball, 0f, 1));
        rules.BeginTick(ball, _ => default);
        ball.Reset(new(0, .31f, floorZ), new(0, -8, 0));
        ball.Step(arena, new Dictionary<ulong, CharacterState>(), new Dictionary<ulong, CharacterDefinition>());
        rules.PlanMotion(ball); rules.FinishTick(ball); rules.FinishTick(ball);
        Assert.Single(rules.Events, e => e.Kind == SlopballEventKind.PointScored && e.Camp == scorer);
        Assert.Equal(scorer == SlopballCamp.Teal ? 1 : 0, rules.State.TealScore);
        Assert.Equal(scorer == SlopballCamp.Clay ? 1 : 0, rules.State.ClayScore);
        Assert.Equal(scorer.Opponent(), rules.State.ServingCamp);
        Assert.False(ball.State.Active);
    }

    [Fact]
    public void APointResetsDamageResourcesPositionsAndProjectilesThenRotatesTheServer()
    {
        var sim = Match(4);
        var state = sim.GetState(3);
        state.DamagePercent = 150; state.Cooldown3 = 50; state.PX = -4;
        sim.SetState(3, state);
        Strike(sim, 1);
        for (int i = 0; i < 120 && sim.SlopballVersus!.Value.ClayScore == 0; i++) sim.Tick(new());
        Assert.Equal(1, sim.SlopballVersus!.Value.ClayScore);
        Assert.Equal(3UL, sim.SlopballVersus.Value.ServerEntityId);
        Assert.Equal(0, sim.GetState(3).DamagePercent);
        Assert.Equal(0, sim.GetState(3).Cooldown3);
        Assert.NotEqual(-4, sim.GetState(3).PX);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        ulong previousBall = sim.ArcadeBall!.Value.EntityId;
        while (sim.SlopballVersus.Value.Phase == SlopballVersusPhase.WaitingServe) sim.Tick(new());
        Assert.NotEqual(previousBall, sim.ArcadeBall.Value.EntityId);
        Assert.Equal(0f, sim.ArcadeBall.Value.DamagePercent);
    }

    [Theory]
    [InlineData(false, 3UL, false)]
    [InlineData(true, 2UL, false)]
    [InlineData(false, 2UL, true)]
    public void TeamAndBallOnlyRulesFilterContactsBeforeTheyConsumeProjectiles(bool ballOnly, ulong target, bool hits)
    {
        var sim = Match(4, ballOnly);
        var victim = sim.GetState(target);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = victim.PX, Y = victim.PY, Z = victim.PZ,
            Radius = .5f, Shape = HitboxShape.Sphere, Damage = 10, DurationTicks = 10, BaseKnockback = 5 });
        sim.Tick(new());
        Assert.Equal(hits, sim.LastTickHits.Any(h => h.TargetEntityId == target));
        Assert.Equal(hits, sim.GetState(target).DamagePercent > 0);
    }

    [Fact]
    public void BothCampsStayOnTheirOwnSideOfTheNet()
    {
        var sim = Match();
        var teal = sim.GetState(1); teal.PZ = 1; teal.VZ = 8; teal.KVZ = 8; sim.SetState(1, teal);
        var clay = sim.GetState(2); clay.PZ = -1; clay.VZ = -8; clay.KVZ = -8; sim.SetState(2, clay);
        sim.Tick(new());
        Assert.True(sim.GetState(1).PZ < 0); Assert.True(sim.GetState(2).PZ > 0);
        Assert.True(sim.GetState(1).VZ <= 0); Assert.True(sim.GetState(2).VZ >= 0);
    }

    [Fact]
    public void PointResetKeepsDifferentSizedFightersGroundedThroughoutTheGap()
    {
        var sim = new ServerSimulation(Court(), new SlopballVersusMatchRule(Camps()));
        var tall = TestHelpers.EngineDef; tall = tall with { CapsuleHeight = 2.4f };
        var shortFighter = TestHelpers.EngineDef; shortFighter = shortFighter with { CapsuleHeight = .8f };
        sim.RegisterEntity(1, tall, TestHelpers.PlayerState());
        sim.RegisterEntity(2, shortFighter, TestHelpers.PlayerState());
        sim.BeginVersusSlopball(); sim.Tick(new());
        Strike(sim, 1);
        for (int i = 0; i < 150 && sim.SlopballVersus!.Value.ClayScore == 0; i++) sim.Tick(new());
        Assert.Equal(1, sim.SlopballVersus!.Value.ClayScore);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(tall.CapsuleHeight * .5f, sim.GetState(1).PY);
            Assert.Equal(shortFighter.CapsuleHeight * .5f, sim.GetState(2).PY);
            sim.Tick(new());
        }
    }

    [Fact]
    public void FirstToTwentyOneEndsTheMatchAndSnapshotsRestoreEveryPointTransition()
    {
        var sim = Match();
        var copy = Match();
        for (int point = 1; point <= 21; point++)
        {
            while (sim.SlopballVersus!.Value.Phase == SlopballVersusPhase.WaitingServe)
            { sim.Tick(new()); copy.RestoreSnapshot(sim.CaptureSnapshot()); }
            Strike(sim, 1);
            copy.RestoreSnapshot(sim.CaptureSnapshot());
            for (int i = 0; i < 120 && sim.SlopballVersus.Value.ClayScore < point; i++)
            {
                sim.Tick(new()); copy.Tick(new());
                Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()), $"Point {point}, tick {i}");
                if (i % 3 == 0) copy.RestoreSnapshot(sim.CaptureSnapshot());
            }
            Assert.Equal(point, sim.SlopballVersus.Value.ClayScore);
            Assert.Equal(point == 21, sim.EvaluateMatch().IsEnded);
        }
        Assert.Equal(SlopballCamp.Clay, sim.SlopballVersus!.Value.Winner);
        Assert.Equal(2UL, sim.EvaluateMatch().WinnerEntityId);
        copy.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()));
    }

    [Fact]
    public void SetupAndResultsCarryCampsAndRejectUnbalancedRosters()
    {
        const string body = """
            {"matchId":"volley","arenaName":"slopball","format":"slopball-versus","ballOnly":true,
             "players":[{"steamId":1,"characterClass":"Manki","entityId":1,"camp":"teal"},
                        {"steamId":2,"characterClass":"Bonk","entityId":2,"camp":"clay"}]}
            """;
        using var doc = JsonDocument.Parse(body);
        var setup = MatchStartRequestCodec.TryParse(doc.RootElement);
        Assert.NotNull(setup); Assert.True(setup!.BallOnly);
        Assert.Equal(SlopballCamp.Clay, setup.Players[1].Camp);
        using var invalid = JsonDocument.Parse(body.Replace("\"clay\"", "\"teal\""));
        Assert.Null(MatchStartRequestCodec.TryParse(invalid.RootElement));
        var result = new MatchResultPacket(500, false, new[] { new MatchResultEntry(1, 1, 0, 0) },
            versus: new(21, 19, SlopballCamp.Teal));
        var bytes = new byte[result.WireSize]; result.Serialize(bytes);
        Assert.True(MatchResultPacket.TryDeserialize(bytes, out var restored));
        Assert.Equal(result.Versus, restored!.Versus);
        Assert.False(MatchResultPacket.TryDeserialize(bytes[..^1], out _));
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    [InlineData(CharacterClass.Bashie)]
    public void AServerCanReachTheHeldBallWithAGroundNormal(CharacterClass fighter)
    {
        var arena = WorldSnapshotTests.LoadArena("slopball");
        var content = BuiltInContentResolver.Resolve(fighter);
        var sim = new ServerSimulation(arena, new SlopballVersusMatchRule(Camps()));
        for (ulong id = 1; id <= 2; id++)
            sim.RegisterEntity(id, content.Definition, TestHelpers.PlayerState(), content.BakedAnimation);
        sim.BeginVersusSlopball(); sim.Tick(new());
        var ball = sim.ArcadeBall!.Value;
        var checks = new List<string>();
        for (int i = 0; i < 150 && sim.SlopballVersus!.Value.Phase == SlopballVersusPhase.HeldServe; i++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1, TargetEntityId = ServerSimulation.ArcadeBallTargetInputId } });
            checks.AddRange(sim.Resolver.LastTickCheckedHitboxes.Select(h => $"{h.X:0.00},{h.Y:0.00},{h.Z:0.00} r{h.Radius:0.00}"));
        }
        Assert.True(sim.SlopballVersus!.Value.Phase != SlopballVersusPhase.HeldServe,
            $"{fighter} could not serve at {ball.Position}; checked hitboxes: {string.Join("; ", checks.Distinct().Take(12))}");
    }

    [Fact]
    public void MalformedWinnerStateCannotPartiallyRestoreTheWorld()
    {
        var sim = Match();
        var before = sim.CaptureSnapshot();
        var data = before.Data.ToArray();
        // The versus footer precedes the input-release flag and two course-presence flags.
        data[^8] = (byte)SlopballCamp.Clay;
        Assert.Throws<System.IO.InvalidDataException>(() => sim.RestoreSnapshot(new WorldSnapshot(data)));
        Assert.True(before.ContentEquals(sim.CaptureSnapshot()));
    }

    [Fact]
    public void VersusEventsCarryTheirCampThroughWorldFrames()
    {
        var sim = Match(); sim.SetTick(10);
        WorldFramePacket Frame(SlopballCamp camp) => new(MatchState.Playing, sim.CaptureSnapshot(),
            Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>(),
            new[] { new SlopballTickEvent(10, new SlopballEvent(SlopballEventKind.PointScored, 1000, 0, .5f, camp)) });
        var bytes = Frame(SlopballCamp.Clay).Serialize();
        Assert.True(WorldFramePacket.TryDeserialize(bytes, out var copy));
        Assert.Equal(SlopballCamp.Clay, Assert.Single(copy!.SlopballEvents).Event.Camp);
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame(SlopballCamp.None).Serialize());
    }

    [Fact]
    public void MatchPushRequiresBalancedCampsWithDistinctUsableEntityIds()
    {
        var map = new MatchContentHandleMap(1, new[] { new MatchContentHandleRecord(new ContentHandle(1), CharacterClass.Manki,
            new MatchContentIdentity("manki", "0.0.0-dev", new string('a', 64), new string('b', 64), new string('c', 64)), "Manki") });
        map = map.WithArena(new MatchArenaRecord("slopball", new string('d', 64)));
        using var content = JsonDocument.Parse(MatchContentHandleMapCodec.Serialize(map));
        string json = JsonSerializer.Serialize(new
        {
            serverId = Guid.NewGuid(), matchId = Guid.NewGuid(), matchPort = 15000, arenaName = "slopball",
            format = "slopball-versus", ballOnly = true,
            content = content.RootElement,
            players = new[]
            {
                new { steamId = 101L, name = "A", isHost = true, entityId = 1, camp = "teal", characterSelection = "Manki" },
                new { steamId = 102L, name = "B", isHost = false, entityId = 2, camp = "clay", characterSelection = "Bonk" },
            },
        });
        using var doc = JsonDocument.Parse(json);
        var setup = LobbyPayloadCodec.TryParseMatchStarted(doc.RootElement);
        Assert.NotNull(setup); Assert.True(setup!.BallOnly);
        Assert.Equal(SlopballCamp.Clay, setup.Players[1].Camp);
        foreach (var invalidJson in new[] { json.Replace("\"clay\"", "\"teal\""), json.Replace("\"entityId\":2", "\"entityId\":1"),
                     json.Replace("\"entityId\":2", "\"entityId\":255") })
        {
            using var invalid = JsonDocument.Parse(invalidJson);
            Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(invalid.RootElement));
        }
    }

    internal static void Strike(ServerSimulation sim, ulong owner)
    {
        var ball = sim.ArcadeBall!.Value.Position;
        sim.Resolver.Spawn(new Hitbox { OwnerId = owner, X = ball.X, Y = ball.Y, Z = ball.Z,
            Radius = .1f, Shape = HitboxShape.Sphere, Damage = 10, BaseKnockback = 5,
            KnockbackAngle = -90, DurationTicks = 1 });
    }
}
