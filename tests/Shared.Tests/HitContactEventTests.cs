using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class HitContactEventTests
{
    private const ulong AttackerId = 1;
    private const ulong VictimId = 2;
    private const string GroundPresentationId = "presentation.test.hit.ground";
    private const string AirPresentationId = "presentation.test.hit.air";
    private static CharacterDefinition ConfiguredDefinition()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = WithPresentation(slots[0], GroundPresentationId);
        slots[7] = WithPresentation(slots[7], AirPresentationId);
        def = def with { CookedSlots = slots };
        def = TestHelpers.WithMove(def, AbilitySlots.Slot1, false, "Ground contact", 20, hitPresentationId: GroundPresentationId);
        def = TestHelpers.WithMove(def, AbilitySlots.Slot1, true, "Air contact", 20, hitPresentationId: AirPresentationId);
        return def;
    }

    private static CookedSlotDefinition WithPresentation(CookedSlotDefinition slot, string id)
        => new(slot.Ordinal, slot.Id, slot.IsAir, slot.Name, slot.Description, slot.IconId,
            slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove, slot.PreserveMomentumOnStart,
            slot.Timeline, slot.ChargePool, slot.AimMovement, slot.AimAnimationId, slot.AllowSlideCarry, id);

    private static ServerSimulation CreateSimulation(CharacterDefinition def, params (ulong id, float x)[] entities)
    {
        var sim = new ServerSimulation(TestHelpers.TestArena());
        foreach (var (id, x) in entities)
        {
            var state = TestHelpers.PlayerState(x) with { EntityId = id, PY = TestHelpers.GroundPY(def) };
            sim.RegisterEntity(id, def, state);
        }
        return sim;
    }

    private static Hitbox Contact(in CharacterState victim, bool airborne = false, bool multiple = false) => new()
    {
        X = victim.PX,
        Y = victim.PY,
        Z = victim.PZ,
        EndX = victim.PX,
        EndY = victim.PY,
        EndZ = victim.PZ,
        Radius = 0.45f,
        Shape = HitboxShape.Sphere,
        Damage = 5f,
        BaseKnockback = 2f,
        KnockbackGrowth = 3f,
        StunTicks = 20,
        DurationTicks = 5,
        OwnerId = AttackerId,
        ActivationId = 9,
        AttackSequence = 17,
        AttackSlot = AbilitySlots.Slot1,
        ActivationAirborne = airborne,
        Airborne = airborne,
        HitsMultipleOpponents = multiple,
    };

    [Theory]
    [InlineData(false, GroundPresentationId)]
    [InlineData(true, AirPresentationId)]
    public void AcceptedConfiguredHitEmitsCapturedSlotPresentationAtContact(bool airborne, string presentationId)
    {
        var def = ConfiguredDefinition();
        var sim = CreateSimulation(def, (AttackerId, -2f), (VictimId, 0f));
        var victim = sim.GetState(VictimId);
        sim.Resolver.Spawn(Contact(in victim, airborne));

        sim.Tick(new Dictionary<ulong, InputState>());

        var hit = Assert.Single(sim.LastTickHits);
        var evt = Assert.Single(sim.GetPresentationEvents());
        Assert.Equal(PresentationEventSource.HitContact, evt.Source);
        Assert.Equal(presentationId, evt.PresentationId);
        Assert.Equal(AttackerId, evt.EntityId);
        Assert.Equal(hit.AttackSequence, evt.AttackSequence);
        Assert.Equal(hit.HitX, evt.WorldX);
        Assert.Equal(hit.HitY, evt.WorldY);
        Assert.Equal(hit.HitZ, evt.WorldZ);
    }

    [Theory]
    [InlineData(2f, 0.6f)]
    [InlineData(6f, 1f)]
    [InlineData(13f, 1.4719601f)]
    [InlineData(100f, 1.6f)]
    public void ConfirmedImpactWeightTravelsWithContactAndSurvivesPacketReplay(float damage, float scale)
    {
        var sim = CreateSimulation(ConfiguredDefinition(), (AttackerId, -2f), (VictimId, 0f));
        var victim = sim.GetState(VictimId);
        var contact = Contact(in victim);
        contact.Damage = damage;
        sim.Resolver.Spawn(contact);
        sim.Tick(new Dictionary<ulong, InputState>());

        var evt = Assert.Single(sim.GetPresentationEvents());
        Assert.Equal(scale, evt.Placement.LocalScaleX, 5);
        Assert.Equal(scale, evt.Placement.LocalScaleY, 5);
        Assert.Equal(scale, evt.Placement.LocalScaleZ, 5);
        var packet = new PresentationEventPacket(evt.MatchTick, evt.EntityId, evt.OperationIndex,
            evt.PresentationId, evt.AttackSequence, evt.Source, evt.WorldX, evt.WorldY,
            evt.WorldZ, evt.WorldYaw, evt.Placement);
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);
        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var replay));
        Assert.Equal(evt, replay!.Value.ToEvent());
    }

    [Theory]
    [InlineData("shield")]
    [InlineData("invincible")]
    [InlineData("miss")]
    public void RejectedOrBlockedConfiguredContactEmitsNoHitContact(string outcome)
    {
        var def = ConfiguredDefinition();
        var sim = CreateSimulation(def, (AttackerId, -2f), (VictimId, 0f));
        var victim = sim.GetState(VictimId);
        if (outcome == "invincible")
        {
            victim.InvincibilityTicks = 3;
            sim.SetState(VictimId, victim);
        }
        if (outcome != "miss")
            sim.Resolver.Spawn(Contact(in victim));
        else
        {
            var miss = Contact(in victim);
            miss.X += 5f;
            miss.EndX += 5f;
            sim.Resolver.Spawn(miss);
        }

        var inputs = outcome == "shield"
            ? new Dictionary<ulong, InputState> { [VictimId] = new() { ShieldHeld = true } }
            : new Dictionary<ulong, InputState>();
        sim.Tick(inputs);

        Assert.DoesNotContain(sim.GetPresentationEvents(), evt => evt.Source == PresentationEventSource.HitContact);
    }

    [Fact]
    public void MultipleAcceptedVictimsReceiveDistinctHitContactKeys()
    {
        var def = ConfiguredDefinition();
        var sim = CreateSimulation(def, (AttackerId, -2f), (VictimId, 0f), (3, 0f));
        var victim = sim.GetState(VictimId);
        sim.Resolver.Spawn(Contact(in victim, multiple: true));

        sim.Tick(new Dictionary<ulong, InputState>());

        var events = sim.GetPresentationEvents().Where(evt => evt.Source == PresentationEventSource.HitContact).ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal(2, events.Select(evt => evt.Key).Distinct().Count());
        Assert.Equal(events.Length, events.Select(evt => evt.OperationIndex).Distinct().Count());
        Assert.All(events, evt => Assert.Equal(AttackerId, evt.EntityId));
    }

    [Fact]
    public void PredictedLocalHitContactIsPresentedOnceAcrossAuthoritativeConfirmation()
    {
        var def = ConfiguredDefinition();
        var server = CreateSimulation(def, (AttackerId, -2f), (VictimId, 0f));
        var predictor = new NetplayPredictor(TestHelpers.TestArena(), AttackerId);
        foreach (var (id, state) in server.GetAllStates())
            predictor.RegisterEntity(id, def, state);
        var victim = server.GetState(VictimId);
        server.Resolver.Spawn(Contact(in victim));
        predictor.Resolver.Spawn(Contact(in victim));

        server.Tick(new Dictionary<ulong, InputState>());
        Assert.True(predictor.Advance(default));
        var started = new List<TimelinePresentationEvent>();
        var retracted = new List<TimelinePresentationEvent>();
        predictor.DrainPresentation(started, retracted);
        var authoritative = Assert.Single(server.GetPresentationEvents());
        Assert.Equal(authoritative, Assert.Single(started));
        Assert.Empty(retracted);

        var frame = WorldFramePacket.Deserialize(new WorldFramePacket(MatchState.Playing,
            server.CaptureSnapshot(), Array.Empty<RelayedInput>(), new[] { authoritative }, server.LastTickHits).Serialize());
        predictor.IngestFrames(new[] { frame, frame });
        started.Clear();
        predictor.DrainPresentation(started, retracted);
        Assert.Empty(started);
        Assert.Empty(retracted);
        Assert.True(server.CaptureSnapshot().ContentEquals(predictor.Simulation.CaptureSnapshot()));
    }
}
