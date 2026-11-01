using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// A two-form engine fixture (ADR-0051), independent of Nyx's package. Form 0 is slow and floaty with three jumps,
/// form 1 fast and heavy with one. F swaps at <see cref="SwapTick"/> with a wide hitbox: form 0's pulls (Going Live),
/// form 1's pushes (Logging Off). Form 0's ground 3 swaps too, so its own cooldown lands after the swap.
/// </summary>
internal static class FormsFixture
{
    public const ulong Nyx = 1, Dummy = 100;
    public const ushort SwapTick = 6, FDuration = 30, SwapCooldown = 300, ECooldown = 600, JabCooldown = 120;
    public const float RealGravity = 20f, FakeGravity = 80f, RealFastFall = 12f, FakeFastFall = 30f;
    public const float RealRun = 8f, FakeRun = 16f;
    public const string GoingLiveHit = "presentation.fixture.going-live.hit";
    public const string LoggingOffHit = "presentation.fixture.logging-off.hit";
    public const float X = 100f, Z = 100f;

    public static CharacterDefinition Def()
    {
        var body = TestHelpers.EngineDef;
        var real = body with { Movement = Movement(body.Movement, RealRun, RealGravity, RealFastFall, 3), CookedSlots = Grid(0) };
        var fake = body with { Movement = Movement(body.Movement, FakeRun, FakeGravity, FakeFastFall, 1), CookedSlots = Grid(1) };
        return CharacterDefinition.WithForms(real, fake);
    }

    private static MovementStats Movement(MovementStats m, float run, float gravity, float fastFall, byte jumps)
    {
        m.RunSpeed = run;
        m.Gravity = gravity;
        m.FastFallSpeed = fastFall;
        m.MaxFallSpeed = 40f;
        m.MaxJumps = jumps;
        return m;
    }

    private static CookedSlotDefinition[] Grid(byte form)
    {
        var slots = CanonicalSlotProjection.All.Select(a => Slot(a, form, 30, 0, null)).ToArray();
        var hitbox = new CookedHitbox(AuthoringHitboxShape.Sphere, 3f, 0f, 0f, 0f, 0f, 0f, 0f, null, null,
            2f, 10f, 30f, 0f, 20, 4, true, 0,
            form == 0 ? AuthoringKnockbackDirection.TowardOwner : AuthoringKnockbackDirection.AwayFromOwner);
        foreach (bool air in new[] { false, true })
            Set(slots, AbilitySlots.F, air, form, FDuration, SwapCooldown, form == 0 ? GoingLiveHit : LoggingOffHit,
                new CookedSwapFormOperation(SwapTick, AuthoringUnit.Ticks),
                new CookedSpawnHitboxOperation(SwapTick, AuthoringUnit.Meters, hitbox));
        Set(slots, AbilitySlots.E, false, form, 10, ECooldown, null);
        if (form == 0)
            Set(slots, AbilitySlots.Normal2, false, form, 10, JabCooldown, null, new CookedSwapFormOperation(2, AuthoringUnit.Ticks));
        return slots;
    }

    private static void Set(CookedSlotDefinition[] slots, byte wire, bool air, byte form, ushort duration, ushort cooldown,
        string? hit, params CookedTimelineOperation[] operations)
    {
        Assert.True(CanonicalSlotProjection.TryGet(wire, air, out var address));
        slots[address.Ordinal] = Slot(address, form, duration, cooldown, hit, operations);
    }

    private static CookedSlotDefinition Slot(SlotAddress address, byte form, ushort duration, ushort cooldown,
        string? hit, params CookedTimelineOperation[] operations)
        => new(address.Ordinal, address.Id, address.IsAirborne, address.Id, "", "", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, cooldown, false, false,
            new CookedTimeline(new[] { new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), operations) }),
            hitPresentationId: hit, form: form);

    public static ServerSimulation Sim(CharacterDefinition def, byte startForm = 0, bool dummy = true)
    {
        var arena = TestHelpers.TestArena();
        arena.KillTop = 40f;
        var sim = TestHelpers.MakeSim(arena);
        var me = TestHelpers.PlayerState(X, Z);
        me.PY = TestHelpers.GroundPY(def);
        me.JumpsLeft = def.ForForm(startForm).Movement.MaxJumps;
        me.Form.Active = startForm;
        sim.RegisterEntity(Nyx, def, me);
        if (dummy)
        {
            var target = TestHelpers.NpcState(X, Z + 2f);
            target.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
            target.FacingYaw = MathF.PI;
            sim.RegisterEntity(Dummy, TestHelpers.EngineDef, target);
        }
        return sim;
    }

    public static void Step(ServerSimulation sim, InputState input = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [Nyx] = input });

    public static InputState Press(byte slot) => new() { ActiveSlot = slot };

    /// <summary>Steps until her form changes and returns her states on the ticks before and after the swap.</summary>
    public static (CharacterState Before, CharacterState After) StepThroughSwap(ServerSimulation sim, InputState held = default)
    {
        for (int i = 0; i < 60; i++)
        {
            var before = sim.GetState(Nyx);
            Step(sim, held);
            var after = sim.GetState(Nyx);
            if (after.Form.Active != before.Form.Active) return (before, after);
        }
        throw new InvalidOperationException("The swap never happened.");
    }
}

public class FormsTests
{
    private const ulong Nyx = FormsFixture.Nyx, Dummy = FormsFixture.Dummy;
    private const float Dt = 1f / 60f;

    [Fact]
    public void SwapInTheAirKeepsHerFallAndClampsHerJumpsToTheNewForm()
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, dummy: false);
        var s = sim.GetState(Nyx);
        s.PY = 20f; s.VY = 3f; s.IsGrounded = false; s.State = ActionState.Idle; s.JumpsLeft = 2; s.AirTimeTicks = 60;
        sim.SetState(Nyx, s);

        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        var (before, after) = FormsFixture.StepThroughSwap(sim);
        FormsFixture.Step(sim);
        var next = sim.GetState(Nyx);

        Assert.Equal(1, after.Form.Active);
        Assert.Equal(1, after.JumpsLeft);
        Assert.Equal(before.AirTimeTicks + 1, after.AirTimeTicks);
        TestHelpers.AssertNear(before.VY - FormsFixture.RealGravity * Dt, after.VY);
        TestHelpers.AssertNear(after.VY - FormsFixture.FakeGravity * Dt, next.VY);
        Assert.Same(def.ForForm(1), sim.GetDefinition(Nyx));
    }

    [Fact]
    public void LatchedFastFallContinuesAtTheNewFormsSpeed()
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, dummy: false);
        var s = sim.GetState(Nyx);
        s.PY = 30f; s.IsGrounded = false; s.State = ActionState.Idle; s.AirTimeTicks = 60;
        s.IsFastFalling = true; s.VY = -FormsFixture.RealFastFall;
        sim.SetState(Nyx, s);

        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        var (_, after) = FormsFixture.StepThroughSwap(sim);
        FormsFixture.Step(sim);

        Assert.True(after.IsFastFalling);
        TestHelpers.AssertNear(-FormsFixture.RealFastFall, after.VY);
        TestHelpers.AssertNear(-FormsFixture.FakeFastFall, sim.GetState(Nyx).VY);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void FsHitboxResolvesAgainstTheFormThatThrewItAfterTheSwap(byte startForm)
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, startForm);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        SpellResolver.HitResult hit = default;
        var events = new List<TimelinePresentationEvent>();
        for (int i = 0; i < 30 && hit.OwnerEntityId == 0; i++)
        {
            FormsFixture.Step(sim);
            events.AddRange(sim.GetPresentationEvents(clear: true));
            hit = sim.LastTickHits.FirstOrDefault(h => h.OwnerEntityId == Nyx && h.TargetEntityId == Dummy);
        }

        Assert.Equal(Nyx, hit.OwnerEntityId);
        Assert.Equal(startForm ^ 1, sim.GetState(Nyx).Form.Active);
        Assert.Equal(startForm, hit.Form);
        var hitEffects = events.Where(e => e.Source == PresentationEventSource.HitContact).Select(e => e.PresentationId).ToList();
        Assert.Equal(new[] { startForm == 0 ? FormsFixture.GoingLiveHit : FormsFixture.LoggingOffHit }, hitEffects);

        var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
            Array.Empty<TimelinePresentationEvent>(), new[] { hit });
        Assert.Equal(startForm, WorldFramePacket.Deserialize(frame.Serialize()).Hits.Single().Form);

        for (int i = 0; i < 30; i++) FormsFixture.Step(sim);
        float awayZ = sim.GetState(Dummy).PZ - (FormsFixture.Z + 2f);
        if (startForm == 0) Assert.True(awayZ < -.1f, $"Going Live pulls the dummy in (moved {awayZ:0.00} m).");
        else Assert.True(awayZ > .1f, $"Logging Off pushes the dummy away (moved {awayZ:0.00} m).");
    }

    [Fact]
    public void RestoreDuringFKeepsTheMoveItStartedInAndItsHitbox()
    {
        var def = FormsFixture.Def();
        var continuous = FormsFixture.Sim(def);
        FormsFixture.Step(continuous, FormsFixture.Press(AbilitySlots.F));
        FormsFixture.StepThroughSwap(continuous);
        FormsFixture.Step(continuous);
        Assert.Contains(continuous.Resolver.GetActiveHitboxes(), h => h.OwnerId == Nyx && h.Form == 0);

        var restored = FormsFixture.Sim(def);
        restored.RestoreSnapshot(continuous.CaptureSnapshot());
        Assert.Equal(1, restored.GetState(Nyx).Form.Active);
        Assert.True(CanonicalSlotProjection.TryGet(AbilitySlots.F, false, out var f));
        Assert.Same(def.CookedSlots![f.Ordinal], restored.GetActiveMove(Nyx)!.Value.Slot);
        var hitbox = restored.Resolver.GetActiveHitboxes().Single(h => h.OwnerId == Nyx);
        Assert.Equal(0, hitbox.Form);
        Assert.Same(def, hitbox.Def);

        for (int tick = 0; tick < FormsFixture.FDuration + 10; tick++)
        {
            FormsFixture.Step(continuous);
            FormsFixture.Step(restored);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"diverged {tick + 1} ticks after the restore");
        }
        Assert.Null(restored.GetActiveMove(Nyx));
        Assert.Equal(1, restored.GetState(Nyx).Form.Active);
    }

    [Fact]
    public void EachFormKeepsItsOwnClocksAndFHasOne()
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, dummy: false);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.E));
        for (int i = 0; i < 15; i++) FormsFixture.Step(sim);
        Assert.InRange(sim.GetState(Nyx).GetCooldown(AbilitySlots.E), (ushort)1, FormsFixture.ECooldown);

        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        var (before, after) = FormsFixture.StepThroughSwap(sim);
        Assert.Equal(0, after.GetCooldown(AbilitySlots.E));
        Assert.Equal(before.GetCooldown(AbilitySlots.E) - 1, after.Form.BankE);
        for (int i = 0; i < 5; i++) FormsFixture.Step(sim);
        Assert.Equal(after.Form.BankE - 5, sim.GetState(Nyx).Form.BankE);

        for (int i = 0; i < FormsFixture.FDuration; i++) FormsFixture.Step(sim);
        Assert.InRange(sim.GetState(Nyx).GetCooldown(AbilitySlots.F), (ushort)(FormsFixture.SwapCooldown - 20), FormsFixture.SwapCooldown);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.NotEqual(ActionState.Attacking, sim.GetState(Nyx).State);

        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.E));
        Assert.Equal(ActionState.Attacking, sim.GetState(Nyx).State);
        Assert.Equal(AbilitySlots.E, sim.GetState(Nyx).AttackSlot);

        while (sim.GetState(Nyx).GetCooldown(AbilitySlots.F) > 0) FormsFixture.Step(sim);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        var (beforeBack, back) = FormsFixture.StepThroughSwap(sim);
        Assert.Equal(0, back.Form.Active);
        Assert.True(beforeBack.Form.BankE > 0, "form 0's E is still cooling when she swaps back");
        Assert.Equal(beforeBack.Form.BankE - 1, back.GetCooldown(AbilitySlots.E));
        Assert.Equal(beforeBack.GetCooldown(AbilitySlots.E) - 1, back.Form.BankE);
    }

    [Fact]
    public void AMovesCooldownGoesToTheFormItStartedIn()
    {
        var sim = FormsFixture.Sim(FormsFixture.Def(), dummy: false);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.Normal2));
        for (int i = 0; i < 12; i++) FormsFixture.Step(sim);

        var s = sim.GetState(Nyx);
        Assert.Equal(1, s.Form.Active);
        Assert.Null(sim.GetActiveMove(Nyx));
        Assert.Equal(0, s.GetCooldown(AbilitySlots.Normal2));
        Assert.InRange(s.Form.Bank3, (ushort)(FormsFixture.JabCooldown - 3), FormsFixture.JabCooldown);
    }

    [Fact]
    public void RespawnKeepsTheFormSheWasKodIn()
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, dummy: false);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        FormsFixture.StepThroughSwap(sim);
        var s = sim.GetState(Nyx);
        s.PY = 200f; s.IsGrounded = false;
        sim.SetState(Nyx, s);
        FormsFixture.Step(sim);

        var respawned = sim.GetState(Nyx);
        Assert.Equal(1, respawned.Deaths);
        Assert.Equal(1, respawned.Form.Active);
        Assert.Equal(def.ForForm(1).Movement.MaxJumps, respawned.JumpsLeft);
        Assert.Same(def.ForForm(1), sim.GetDefinition(Nyx));
    }

    [Theory]
    [InlineData((byte)0, FormsFixture.RealRun)]
    [InlineData((byte)1, FormsFixture.FakeRun)]
    public void AMatchStartsInThePickedFormWithItsMovement(byte form, float runSpeed)
    {
        var def = FormsFixture.Def();
        var sim = FormsFixture.Sim(def, form, dummy: false);
        Assert.Equal(form, sim.GetState(Nyx).Form.Active);
        Assert.Same(def.ForForm(form), sim.GetDefinition(Nyx));
        for (int i = 0; i < 90; i++) FormsFixture.Step(sim, new InputState { MoveX = 1f });
        var s = sim.GetState(Nyx);
        TestHelpers.AssertNear(runSpeed, MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ), .05f);
    }

    [Fact]
    public void FightersWithoutFormsIgnoreAStartingForm()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(FormsFixture.X, FormsFixture.Z);
        state.Form.Active = 1;
        sim.RegisterEntity(Nyx, TestHelpers.EngineDef, state);
        Assert.Equal(0, sim.GetState(Nyx).Form.Active);
        Assert.Null(sim.GetDefinition(Nyx).AlternateForm);
    }

    [Theory]
    [InlineData("", (byte)0)]
    [InlineData(",\"form\":0", (byte)0)]
    [InlineData(",\"form\":1", (byte)1)]
    [InlineData(",\"form\":2", null)]
    [InlineData(",\"form\":\"fake\"", null)]
    public void MatchPlayerCarriesAnOptionalStartingForm(string field, byte? expected)
    {
        string body = "{\"matchId\":\"m\",\"players\":[{\"steamId\":1,\"characterClass\":\"Manki\",\"entityId\":1" + field
            + "},{\"steamId\":2,\"characterClass\":\"FightGuy\",\"entityId\":2}]}";
        var request = MatchStartRequestCodec.TryParse(JsonDocument.Parse(body).RootElement);
        Assert.Equal(expected, request?.Players[0].Form);
        if (expected != null) Assert.Equal(0, request!.Players[1].Form);
    }

    [Fact]
    public void WorldSnapshotsRoundTripBothFormsThroughAFight()
    {
        var def = FormsFixture.Def();
        ServerSimulation Make() => FormsFixture.Sim(def);
        var continuous = Make();
        var restored = Make();
        var rng = new Random(51);
        var inputs = new Dictionary<ulong, InputState>();
        byte[] slots = { AbilitySlots.F, AbilitySlots.E, AbilitySlots.Normal2, AbilitySlots.Normal1 };
        int swaps = 0, restores = 0;
        for (int tick = 1; tick <= 1500; tick++)
        {
            inputs[Nyx] = new InputState
            {
                MoveX = (float)(rng.NextDouble() * 2 - 1),
                MoveY = (float)(rng.NextDouble() * 2 - 1),
                Jump = rng.Next(40) == 0,
                JumpHeld = rng.Next(2) == 0,
                ActiveSlot = rng.Next(12) == 0 ? slots[rng.Next(slots.Length)] : (byte)0,
            };
            byte form = continuous.GetState(Nyx).Form.Active;
            continuous.Tick(new Dictionary<ulong, InputState>(inputs));
            restored.Tick(new Dictionary<ulong, InputState>(inputs));
            if (continuous.GetState(Nyx).Form.Active != form) swaps++;
            var expected = continuous.CaptureSnapshot();
            Assert.True(expected.ContentEquals(restored.CaptureSnapshot()), $"diverged at tick {tick}");
            if (rng.Next(25) == 0)
            {
                restored = Make();
                restored.RestoreSnapshot(expected);
                Assert.True(expected.ContentEquals(restored.CaptureSnapshot()), $"round trip changed tick {tick}");
                restores++;
            }
        }
        Assert.True(swaps >= 4, $"only {swaps} swaps");
        Assert.True(restores >= 20, $"only {restores} restores");
    }
}
