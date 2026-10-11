using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hold-to-aim behavior retained for Manki's Round Bomb.
/// </summary>
public class CookedAimHoldTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef!;
    private static readonly CharacterDefinition FightGuyDef = TestHelpers.FightGuyDef!;

    private static ServerSimulation MakeSim(CharacterDefinition def)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        TestHelpers.RegisterPlayer(sim, def, state);
        return sim;
    }
    private static InputState HoldInput(bool aiming, short aimYaw = 0, short aimPitch = 0, ushort aimDistance = 0)
        => new() { IsAiming = aiming, AimYaw = aimYaw, AimPitch = aimPitch, AimDistance = aimDistance };

    [Theory]
    [InlineData((ushort)3)]
    [InlineData((ushort)17)]
    public void Manki_Q_FiresAtCookedThrowTrigger(ushort triggerTick)
    {
        var def = TestHelpers.MankiDef;
        var slots = def.CookedSlots!.ToArray();
        var slot = slots[4]; // ground.A
        var stage = slot.Timeline.Stages[0];
        slots[4] = new CookedSlotDefinition(slot.Ordinal, slot.Id, slot.IsAir, slot.Name,
            slot.Description, slot.IconId, slot.Behavior, slot.AimMode, slot.CooldownTicks,
            slot.IsRecoveryMove, slot.PreserveMomentumOnStart, new CookedTimeline(new[]
            {
                new CookedStage(stage.DurationTicks, stage.IasaTicks, stage.LandingLagTicks,
                    stage.AutoCancelBeforeTicks, stage.AutoCancelAfterTicks, stage.AnimationIds,
                    stage.Operations.Select(op => op is CookedStartCapabilityOperation capability
                        ? new CookedStartCapabilityOperation(capability.Tick, capability.Unit,
                            capability.CapabilityId, capability.CapabilityVersion,
                            ((CookedMankiRoundBombCapabilityParameters)capability.Parameters)
                                with { ThrowTriggerTick = triggerTick })
                        : op).ToArray()),
            }), aimMovement: slot.AimMovement);
        def.CookedSlots = slots;
        var sim = MakeSim(def);
        TestHelpers.TickHold(sim, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true, AimDistance = 400 }, 10);
        sim.Tick(new() { [1] = default });
        Assert.Equal((ushort)0, sim.GetState(1).AttackElapsedTicks);

        for (int tick = 1; tick < triggerTick; tick++)
        {
            sim.Tick(new() { [1] = default });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        sim.Tick(new() { [1] = default });

        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(AbilitySlots.A, bomb.AttackSlot);
        Assert.True(bomb.Explosion.HasValue);
    }

    [Fact]
    public void Manki_Q_HoldsWhileAiming_NoProjectileDuringHold()
    {
        var sim = MakeSim(MankiDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });

        for (int i = 0; i < 30; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        // Release → throw phase spawns the bomb at throw_trigger_tick (10) after release.
        for (int i = 0; i < 15; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void Manki_Q_LongHold_DoesNotCancel_ReleaseStillFires()
    {
        var sim = MakeSim(MankiDef);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });

        // Hold past the authored stage duration (600 ticks) — the stage clock must
        // freeze during the aim hold (IAimHoldCapability), not cancel the ability.
        for (int i = 0; i < 700; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }

        for (int i = 0; i < 15; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }


    [Fact]
    public void Manki_Q_ThrowsAtCursorDistance_WhenReleaseInputZeroesAim()
    {
        var sim = MakeSim(MankiDef);

        // Press Q with the cursor 4 m ahead (AimDistance = 400 cm), hold, then
        // release with a zeroed aim input — mirrors the client's release context
        // (it forwards the last yaw but no distance). The throw must use the LAST
        // HELD distance, not the zeroed release tick.
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 0, AimDistance = 400 } },
        });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimDistance: 400) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        // 4 m lob at 30° (hSpeed ≈ 7.4) vs the 0.5 m clamp the bug produced (hSpeed ≈ 1.3).
        float hSpeed = MathF.Sqrt(bomb.VX * bomb.VX + bomb.VZ * bomb.VZ);
        Assert.True(hSpeed > 5f, $"expected a 4 m lob, got hSpeed={hSpeed}");
    }

    [Fact]
    public void Manki_Q_ThrowsAlongCursorYaw_NotPressOrZeroedYaw()
    {
        var sim = MakeSim(MankiDef);

        // Aim 90° to the side while held; the release input carries yaw = 0.
        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000, AimDistance = 400 } },
        });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: true, aimYaw: 9000, aimDistance: 400) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, HoldInput(aiming: false) } });

        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.True(bomb.VX > 5f, $"expected a sideways launch, got VX={bomb.VX} VZ={bomb.VZ}");
        Assert.True(MathF.Abs(bomb.VZ) < 1f, $"expected no forward velocity, got VX={bomb.VX} VZ={bomb.VZ}");
    }
}
