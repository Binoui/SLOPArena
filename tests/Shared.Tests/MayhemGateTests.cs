using System.Linq;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The control gate at its four points: raw input, buffered slots, actual activation and movement admission.</summary>
public class MayhemGateTests
{
    private static readonly CharacterClass[] FightGuys = { CharacterClass.FightGuy };
    private static readonly byte[] Normals = { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3 };
    private static readonly byte[] Specials = { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F };
    private static readonly byte[] Retired = { AbilitySlots.Lmb, AbilitySlots.Rmb, AbilitySlots.RetiredNormal, AbilitySlots.RetiredFifthNormal };

    private static (ServerSimulation Sim, int Episode) Playing()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, roster: FightGuys);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        return (sim, episode);
    }

    [Fact]
    public void FrozenAdmitsNothingAndPlayingAdmitsMoveJumpAndTheThreeNormalsPlusOneExtraVerb()
    {
        var raw = new InputState
        {
            MoveX = .5f, MoveY = -.5f, Up = true, Left = true, Walk = true, Jump = true, JumpHeld = true, Down = true, DownPressed = true,
            ShieldHeld = true, ShieldPressed = true, GrabPressed = true, DashPressed = true, ToggleLock = true, RetargetPressed = true,
            LockMode = TargetLockMode.Always, FaceToCamera = true, IsAiming = true, AimYaw = 1200, AimPitch = 300, AimDistance = 500,
            FacingYaw = 900, TargetEntityId = 2, ActiveSlot = AbilitySlots.Normal2,
        };
        Assert.Equal(default, MayhemControls.Frozen.Filter(raw));
        Assert.All(Normals.Concat(Specials).Concat(Retired), slot => Assert.False(MayhemControls.Frozen.AdmitsSlot(slot)));
        var none = MayhemControls.Playing(ExtraVerb.None);
        var kept = none.Filter(raw);
        Assert.Equal((.5f, -.5f, true, true, true, true), (kept.MoveX, kept.MoveY, kept.Walk, kept.Jump, kept.JumpHeld, kept.FaceToCamera));
        Assert.Equal(((short)1200, (short)300, (ushort)500, (short)900), (kept.AimYaw, kept.AimPitch, kept.AimDistance, kept.FacingYaw));
        Assert.Equal(AbilitySlots.Normal2, kept.ActiveSlot);
        Assert.False(kept.Down || kept.DownPressed || kept.ShieldHeld || kept.ShieldPressed || kept.GrabPressed || kept.DashPressed
            || kept.ToggleLock || kept.RetargetPressed || kept.IsAiming);
        Assert.Equal(TargetLockMode.Never, kept.LockMode);
        Assert.Equal(0, kept.TargetEntityId);
        Assert.All(Normals, slot => Assert.True(none.AdmitsSlot(slot)));
        Assert.All(Specials.Concat(Retired), slot => Assert.False(none.AdmitsSlot(slot)));
        Assert.All(Specials, slot => Assert.True(MayhemControls.Playing(ExtraVerb.Specials).AdmitsSlot(slot)));
        Assert.All(Retired, slot => Assert.False(MayhemControls.Playing(ExtraVerb.Specials).AdmitsSlot(slot)));
        Assert.True(MayhemControls.Playing(ExtraVerb.Shield).Filter(raw).ShieldHeld);
        Assert.True(MayhemControls.Playing(ExtraVerb.Grab).Filter(raw).GrabPressed);
        Assert.True(MayhemControls.Playing(ExtraVerb.Beeline).Filter(raw).DashPressed);
        foreach (var verb in new[] { ExtraVerb.Shield, ExtraVerb.Grab, ExtraVerb.Specials, ExtraVerb.Beeline })
        {
            var filtered = MayhemControls.Playing(verb).Filter(raw);
            Assert.False(filtered.Down || filtered.DownPressed || filtered.ToggleLock || filtered.RetargetPressed);
        }
    }

    [Fact]
    public void RawForbiddenSlotsNeverStartAMoveWhileNormalsDo()
    {
        foreach (byte slot in Specials.Concat(Retired))
        {
            var (sim, _) = Playing();
            sim.Tick(new() { [1] = new InputState { ActiveSlot = slot } });
            Assert.Null(sim.GetActiveMove(1));
        }
        var (normal, _) = Playing();
        normal.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 } });
        Assert.NotNull(normal.GetActiveMove(1));
    }

    [Fact]
    public void BufferedForbiddenSlotsAreDroppedAndBufferedNormalsStillFire()
    {
        var (sim, _) = Playing();
        var state = sim.GetState(1); state.BufferedSlot = AbilitySlots.E; sim.SetState(1, state);
        sim.Tick(new());
        Assert.Equal(0, sim.GetState(1).BufferedSlot);
        Assert.Null(sim.GetActiveMove(1));
        state = sim.GetState(1); state.BufferedSlot = AbilitySlots.Normal1; sim.SetState(1, state);
        sim.Tick(new());
        Assert.NotNull(sim.GetActiveMove(1));
    }

    [Fact]
    public void TheActualActivationRefusesAForbiddenSlot()
    {
        var (sim, _) = Playing();
        var def = sim.GetDefinition(1);
        var special = def.GetCookedSlotAbility(AbilitySlots.E, airborne: false);
        Assert.NotNull(special);
        sim.ActivateAbility(1, new CookedTimelineAbility(special!), AbilitySlots.E - 1, def);
        Assert.Null(sim.GetActiveMove(1));
        var jab = def.GetCookedSlotAbility(AbilitySlots.Normal1, airborne: false)!;
        sim.ActivateAbility(1, new CookedTimelineAbility(jab), AbilitySlots.Normal1 - 1, def);
        Assert.NotNull(sim.GetActiveMove(1));
    }

    [Fact]
    public void MovementAdmissionAppliesTheGateBeforeDefenseGrabJumpDownAndBuffer()
    {
        var arena = MayhemWorld.Arena();
        var def = BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;
        CharacterState Fresh() => new()
        {
            EntityId = 1, PY = def.CapsuleHeight * .5f, IsGrounded = true, State = ActionState.Idle,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
        };
        var shield = new InputState { ShieldHeld = true, ShieldPressed = true };
        var grab = new InputState { GrabPressed = true };
        var down = new InputState { Down = true, DownPressed = true };
        var buffered = new InputState { ActiveSlot = AbilitySlots.E };
        CharacterState Run(InputState input, MayhemControls? gate, CharacterState? start = null)
        {
            var s = start ?? Fresh();
            Simulation.SimulateTick(ref s, def, input, arena, out _, out _, DownActionTuning.Default, false, null, 1f, null, gate);
            return s;
        }
        var none = MayhemControls.Playing(ExtraVerb.None);
        Assert.Equal(ActionState.Shielding, Run(shield, null).State);
        Assert.NotEqual(ActionState.Shielding, Run(shield, none).State);
        Assert.Equal(ActionState.Shielding, Run(shield, MayhemControls.Playing(ExtraVerb.Shield)).State);
        Assert.Equal(ActionState.GrabAttempt, Run(grab, null).State);
        Assert.NotEqual(ActionState.GrabAttempt, Run(grab, none).State);
        Assert.NotEqual(ActionState.Crouching, Run(down, none).State);
        Assert.Equal(ActionState.JumpSquat, Run(new InputState { Jump = true, JumpHeld = true }, none).State);
        Assert.NotEqual(ActionState.JumpSquat, Run(new InputState { Jump = true, JumpHeld = true }, MayhemControls.Frozen).State);
        var locked = Fresh(); locked.AnimLockTicks = 5; locked.State = ActionState.Attacking;
        Assert.Equal(0, Run(buffered, none, locked).BufferedSlot);
        var held = Fresh(); held.BufferedSlot = AbilitySlots.R;
        Assert.Equal(0, Run(default, none, held).BufferedSlot);
    }

    [Fact]
    public void HeldForbiddenControlsNeverTakeEffectAndLockAndRetargetStayOff()
    {
        var (sim, _) = Playing();
        for (int i = 0; i < 40; i++)
        {
            sim.Tick(new()
            {
                [1] = new InputState { ShieldHeld = true, ShieldPressed = i == 0, Down = true, DownPressed = i == 0, ToggleLock = true,
                    RetargetPressed = true, LockMode = TargetLockMode.Always, GrabPressed = i % 7 == 0, DashPressed = i % 5 == 0 },
            });
            var state = sim.GetState(1);
            Assert.NotEqual(ActionState.Shielding, state.State);
            Assert.NotEqual(ActionState.Crouching, state.State);
            Assert.NotEqual(ActionState.GrabAttempt, state.State);
            Assert.NotEqual(ActionState.HomingDash, state.State);
            Assert.NotEqual(ActionState.HomingDashFloat, state.State);
            Assert.False(state.LockOn);
            Assert.Null(sim.GetActiveMove(1));
        }
    }
}
