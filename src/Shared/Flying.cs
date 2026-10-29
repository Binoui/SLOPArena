using System;

namespace SlopArena.Shared;

public struct FlyingState
{
    public bool Active;
    public ushort FuelTicksLeft;
    public ushort BurstTicksLeft;
    public float LiftSpeed;
    public float HeadingYaw;
}

public static class Flying
{
    public static CookedMankiJetpackBoostCapabilityParameters? Parameters(CharacterDefinition def)
    {
        var slot = def.GetCookedSlotAbility(AbilitySlots.E, false);
        if (slot == null) return null;
        foreach (var stage in slot.Timeline.Stages)
            foreach (var operation in stage.Operations)
                if (operation is CookedStartCapabilityOperation { Parameters: CookedMankiJetpackBoostCapabilityParameters p })
                    return p;
        return null;
    }

    public static bool KeepsFlight(byte slot) => slot is AbilitySlots.A or AbilitySlots.R;

    internal static bool CanContinue(in CharacterState s)
        => s.State == ActionState.Flying
            || s.State is (ActionState.Attacking or ActionState.Aiming) && KeepsFlight(s.AttackSlot);

    internal static void Refuel(ref CharacterState s, CharacterDefinition def)
    {
        if (Parameters(def) is { } p) s.Flight.FuelTicksLeft = p.FuelTicks;
    }

    internal static void Enter(ref CharacterState s, CookedMankiJetpackBoostCapabilityParameters p, CharacterDefinition def)
    {
        s.Flight.Active = true;
        s.Flight.HeadingYaw = s.FacingYaw;
        s.Flight.BurstTicksLeft = p.JumpBurstTicks;
        s.Flight.LiftSpeed = p.VerticalSpeed;
        s.State = ActionState.Flying;
        s.AnimLockTicks = 0;
        s.IsGrounded = false;
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
    }

    internal static void End(ref CharacterState s, CharacterDefinition def, bool cooldown = true, bool beforeTimers = false)
    {
        if (!s.Flight.Active) return;
        s.Flight.Active = false;
        s.Flight.BurstTicksLeft = 0;
        s.VY = MathF.Min(s.VY, 0f);
        if (s.State == ActionState.Flying) s.State = ActionState.Idle;
        if (cooldown) s.SetCooldown(AbilitySlots.E, (ushort)(def.GetCookedSlotAbility(AbilitySlots.E, false)!.CooldownTicks + (beforeTimers ? 1 : 0)));
    }

    internal static void Tick(ref CharacterState s, in InputState input, CharacterDefinition def, bool cooldown)
    {
        if (!s.Flight.Active) return;
        if (!CanContinue(s) || s.Flight.FuelTicksLeft == 0)
        {
            End(ref s, def, cooldown, beforeTimers: true);
            return;
        }
        if (s.HitstopTicks > 0) return;
        var p = Parameters(def)!;
        if (input.Jump)
        {
            s.Flight.BurstTicksLeft = p.JumpBurstTicks;
            s.Flight.LiftSpeed = p.JumpBurstSpeed;
        }
        if (input.MoveX * input.MoveX + input.MoveY * input.MoveY > .01f)
            s.Flight.HeadingYaw = MathF.Atan2(input.MoveX, input.MoveY);
        ApplyMotion(ref s, p);
        s.Flight.FuelTicksLeft--;
        if (s.Flight.BurstTicksLeft > 0) s.Flight.BurstTicksLeft--;
    }

    internal static void ApplyMotion(ref CharacterState s, CookedMankiJetpackBoostCapabilityParameters p)
    {
        s.FacingYaw = s.Flight.HeadingYaw;
        s.VX = MathF.Sin(s.Flight.HeadingYaw) * p.HorizontalSpeed;
        s.VZ = MathF.Cos(s.Flight.HeadingYaw) * p.HorizontalSpeed;
        s.VY = s.Flight.BurstTicksLeft > 0 ? s.Flight.LiftSpeed : -p.SinkSpeed;
        s.IsFastFalling = false;
        s.IsGrounded = false;
    }
}
