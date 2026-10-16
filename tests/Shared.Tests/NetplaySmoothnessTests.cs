using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Regression coverage for online presentation smoothness: self-track event identity,
/// opponent prediction seeding and input holding, per-tick opponent advance and raw
/// playout pacing.
/// </summary>
public sealed class NetplaySmoothnessTests
{
    private static ServerEntityPacket Packet(ServerSimulation server, ulong id, uint tick, InputState input) => new()
    {
        EntityId = id,
        Tick = tick,
        State = CharacterStatePacket.FromState(server.GetState(id), tick),
        HasInput = true,
        Input = input,
    };

    private static CharacterDefinition PresentationDef()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[6] = new CookedSlotDefinition(6, "ground.R", false, "Presentation fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(20, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedEmitPresentationOperation(2, AuthoringUnit.Ticks, "presentation.test.self", 0),
                    }),
            }));
        def.CookedSlots = slots;
        return def;
    }

    [Fact]
    public void SelfPredictedEventKeepsTheServerTickAfterMovementCorrectionsAndDedupesItsConfirmation()
    {
        var def = PresentationDef();
        var arena = TestHelpers.TestArena();
        float ground = TestHelpers.GroundPY(def);
        var self = TestHelpers.PlayerState(x: 40f, z: 100f) with { PY = ground };
        var opponent = TestHelpers.PlayerState(x: 150f, z: 150f) with { EntityId = 2, PY = ground };
        var server = new ServerSimulation(arena);
        server.RegisterEntity(1, def, self);
        server.RegisterEntity(2, def, opponent);
        var client = new RollbackSimulator(arena, 1);
        client.RegisterEntity(1, def, self);
        client.RegisterEntity(2, def, opponent);
        server.SetTick(100);
        Assert.True(client.SetTimeline(100));

        var inFlight = new Queue<(ServerEntityPacket[] Packets, List<TimelinePresentationEvent> Events)>();
        var serverEvents = new List<TimelinePresentationEvent>();
        var accepted = new List<TimelinePresentationEvent>();
        uint tick = 100;
        void Step(InputState input)
        {
            tick++;
            server.Tick(new() { [1] = input, [2] = default });
            var events = server.GetPresentationEvents(clear: true).ToList();
            serverEvents.AddRange(events);
            client.Tick(new() { [1] = input });
            inFlight.Enqueue((new[] { Packet(server, 1, tick, input), Packet(server, 2, tick, default) }, events));
            if (inFlight.Count > 4)
            {
                var (packets, delivered) = inFlight.Dequeue();
                client.IngestAuthoritativeBatch(packets);
                client.IngestPresentationEvents(delivered);
            }
            accepted.AddRange(client.DrainPresentationEvents());
        }

        for (int i = 0; i < 40; i++) Step(TestHelpers.Input(moveX: 1f));   // corrections replay movement
        for (int i = 0; i < 5; i++) Step(default);
        Step(new InputState { ActiveSlot = AbilitySlots.R });
        for (int i = 0; i < 30; i++) Step(default);

        var authoritative = Assert.Single(serverEvents, e => e.EntityId == 1);
        var presented = Assert.Single(accepted, e => e.EntityId == 1);
        Assert.Equal(authoritative.MatchTick, presented.MatchTick);
    }

    [Fact]
    public void OpponentMissingFromABatchIsReplayedFromItsOwnSnapshot()
    {
        var def = TestHelpers.EngineDef;
        var arena = TestHelpers.TestArena();
        float ground = TestHelpers.GroundPY(def);
        var states = new Dictionary<ulong, CharacterState>
        {
            [1] = TestHelpers.PlayerState(x: 40f, z: 100f) with { PY = ground },
            [2] = TestHelpers.PlayerState(x: 60f, z: 130f) with { EntityId = 2, PY = ground },
            [3] = TestHelpers.PlayerState(x: 60f, z: 70f) with { EntityId = 3, PY = ground },
        };
        var server = new ServerSimulation(arena);
        var client = new RollbackSimulator(arena, 1);
        foreach (var (id, state) in states)
        {
            server.RegisterEntity(id, def, state);
            client.RegisterEntity(id, def, state);
        }
        server.SetTick(100);
        Assert.True(client.SetTimeline(100));

        var run = TestHelpers.Input(moveX: 1f);
        var inFlight = new Queue<ServerEntityPacket[]>();
        uint tick = 100;
        float worst = 0f;
        for (int step = 0; step < 90; step++)
        {
            tick++;
            server.Tick(new() { [1] = default, [2] = run, [3] = run });
            client.Tick(new() { [1] = default });
            inFlight.Enqueue(new[] { Packet(server, 1, tick, default), Packet(server, 2, tick, run), Packet(server, 3, tick, run) });
            if (inFlight.Count <= 8) continue;
            var batch = inFlight.Dequeue();
            client.IngestAuthoritativeBatch(step % 7 == 0 ? batch.Take(2).ToArray() : batch);
            if (step > 30)
                worst = MathF.Max(worst, MathF.Abs(client.GetState(3).PX - server.GetState(3).PX));
        }
        Assert.True(worst < 0.01f, $"opponent 3 diverged by {worst:F3} m after a batch without its snapshot");
    }

    [Fact]
    public void PredictedOpponentDoesNotRepeatARelayedJumpPress()
    {
        var def = TestHelpers.EngineDef;
        var arena = TestHelpers.TestArena();
        float ground = TestHelpers.GroundPY(def);
        var self = TestHelpers.PlayerState(x: 40f, z: 100f) with { PY = ground };
        var opponent = TestHelpers.PlayerState(x: 80f, z: 100f) with
        {
            EntityId = 2, PY = ground, JumpsLeft = def.Movement.MaxJumps,
        };
        var server = new ServerSimulation(arena);
        server.RegisterEntity(1, def, self);
        server.RegisterEntity(2, def, opponent);
        var client = new RollbackSimulator(arena, 1);
        client.RegisterEntity(1, def, self);
        client.RegisterEntity(2, def, opponent);
        server.SetTick(100);
        Assert.True(client.SetTimeline(100));

        const int delay = 10;
        var inFlight = new Queue<ServerEntityPacket[]>();
        uint tick = 100;
        for (int step = 0; step < 60; step++)
        {
            tick++;
            var press = step == 20
                ? new InputState { Jump = true, JumpHeld = true }
                : new InputState { JumpHeld = step is > 20 and < 26 };
            server.Tick(new() { [1] = default, [2] = press });
            client.Tick(new() { [1] = default });
            inFlight.Enqueue(new[] { Packet(server, 1, tick, default), Packet(server, 2, tick, press) });
            if (inFlight.Count <= delay) continue;
            client.IngestAuthoritativeBatch(inFlight.Dequeue());
            if (step >= 20 + delay)
                Assert.Equal(server.GetState(2).JumpsLeft, client.GetState(2).JumpsLeft);
        }
    }

    [Fact]
    public void PredictedOpponentKeepsMovingEveryLocalTickBetweenBatches()
    {
        var def = TestHelpers.EngineDef;
        var arena = TestHelpers.TestArena();
        float ground = TestHelpers.GroundPY(def);
        var client = new RollbackSimulator(arena, 1);
        client.RegisterEntity(1, def, TestHelpers.PlayerState(x: 40f, z: 100f) with { PY = ground });
        var start = TestHelpers.PlayerState(x: 60f, z: 100f) with { EntityId = 2, PY = ground };
        client.RegisterEntity(2, def, start);
        Assert.True(client.SetTimeline(200));

        var run = TestHelpers.Input(moveX: 1f);
        client.IngestAuthoritativeBatch(new[]
        {
            new ServerEntityPacket { EntityId = 2, Tick = 200, State = CharacterStatePacket.FromState(start, 200), HasInput = true, Input = run },
        });

        var reference = new ServerSimulation(arena);
        reference.RegisterEntity(2, def, start);
        float previous = client.GetState(2).PX;
        for (int i = 0; i < 12; i++)
        {
            client.Tick(new() { [1] = default });
            reference.Tick(new() { [2] = run });
            float now = client.GetState(2).PX;
            Assert.Equal(reference.GetState(2).PX, now);
            if (i > 0) Assert.True(now > previous, $"opponent froze at local tick {i}");
            previous = now;
        }
    }

    [Fact]
    public void RawPlayoutAdvancesOneTickPerLocalTickDespiteBunchedArrival()
    {
        var def = TestHelpers.EngineDef;
        var arena = TestHelpers.TestArena();
        var client = new RollbackSimulator(arena, 1);
        client.RegisterEntity(1, def, TestHelpers.PlayerState(x: 40f, z: 100f) with { PY = TestHelpers.GroundPY(def) });
        client.RegisterEntity(2, def, TestHelpers.PlayerState(x: 60f, z: 100f) with { EntityId = 2 });
        Assert.True(client.SetTimeline(0));

        ServerEntityPacket Raw(uint tick)
        {
            var attacking = TestHelpers.PlayerState(x: tick * 0.1f, z: 100f) with
            {
                EntityId = 2, State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1,
                AttackElapsedTicks = (ushort)(tick % 40),
            };
            return new ServerEntityPacket { EntityId = 2, Tick = tick, State = CharacterStatePacket.FromState(attacking, tick) };
        }

        int previous = -1, irregular = 0;
        for (uint local = 1; local <= 400; local++)
        {
            // Two snapshots land together every other update; nothing arrives in between.
            if (local % 2 == 0)
                client.IngestAuthoritativeBatch(new[] { Raw(local - 1), Raw(local) });
            client.Tick(new() { [1] = default });
            int shown = (int)MathF.Round(client.GetState(2).PX / 0.1f);
            if (local > 20 && previous >= 0 && shown - previous != 1)
                irregular++;
            previous = shown;
            if (local < 2) continue;   // no snapshot has arrived yet
            Assert.Equal(ActionState.Attacking, client.GetState(2).State);
            Assert.Equal((ushort)(shown % 40), client.GetState(2).AttackElapsedTicks);
        }
        Assert.Equal(0, irregular);
        Assert.InRange(client.RawPlayoutDelay, RollbackSimulator.MinimumRawPlayoutDelay, RollbackSimulator.MaximumRawPlayoutDelay);
    }

    [Fact]
    public void RawPlayoutWidensItsDelayWhenSnapshotsStarve()
    {
        var def = TestHelpers.EngineDef;
        var client = new RollbackSimulator(TestHelpers.TestArena(), 1);
        client.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        client.RegisterEntity(2, def, TestHelpers.PlayerState(x: 10f) with { EntityId = 2 });
        Assert.True(client.SetTimeline(0));
        uint before = client.RawPlayoutDelay;

        for (uint local = 1; local <= 60; local++)
        {
            // Snapshots arrive in bursts of four every fourth update.
            if (local % 4 == 0)
            {
                var burst = new List<ServerEntityPacket>();
                for (uint t = local - 3; t <= local; t++)
                    burst.Add(new ServerEntityPacket
                    {
                        EntityId = 2, Tick = t,
                        State = CharacterStatePacket.FromState(TestHelpers.PlayerState(x: t) with
                        { EntityId = 2, State = ActionState.Hitstun, HitstunTicks = 30 }, t),
                    });
                client.IngestAuthoritativeBatch(burst);
            }
            client.Tick(new() { [1] = default });
        }
        Assert.True(client.RawPlayoutStarvations > 0);
        Assert.True(client.RawPlayoutDelay > before);
    }
}
