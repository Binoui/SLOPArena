using System;

namespace SlopArena.Shared;

public enum GrabThrowRole : byte { Thrower, Victim }

public readonly struct GrabThrowPose
{
    public GrabThrowRole Role { get; }
    public float Seconds { get; }
    internal GrabThrowPose(GrabThrowRole role, float seconds) { Role = role; Seconds = seconds; }
}

public static class GrabThrow
{
    internal enum Kind : byte { Fling, UpToss }

    internal readonly struct Spec
    {
        internal readonly byte WindupPhase, ReleasedPhase;
        internal readonly float Damage, BaseKnockback, GrowthKnockback;
        internal readonly sbyte AngleDegrees;
        internal readonly ushort RecoveryTicks;
        internal Spec(DefenseInteractionPhase windup, DefenseInteractionPhase released, float damage,
            sbyte angle, float baseKnockback, float growth, ushort recovery)
        {
            WindupPhase = (byte)windup; ReleasedPhase = (byte)released; Damage = damage;
            AngleDegrees = angle; BaseKnockback = baseKnockback; GrowthKnockback = growth; RecoveryTicks = recovery;
        }
    }

    internal static Spec Specification(Kind kind) => kind == Kind.UpToss
        ? new(DefenseInteractionPhase.UpTossing, DefenseInteractionPhase.UpTossReleased, 4f, 90, 34f, 10f, 3)
        : new(DefenseInteractionPhase.Throwing, DefenseInteractionPhase.FlingReleased, 8f, 35, 10f, 32f, 12);

    internal static bool TryDecode(byte phase, out Kind kind, out bool released)
    {
        kind = phase is (byte)DefenseInteractionPhase.UpTossing or (byte)DefenseInteractionPhase.UpTossReleased
            ? Kind.UpToss : Kind.Fling;
        released = phase is (byte)DefenseInteractionPhase.FlingReleased or (byte)DefenseInteractionPhase.UpTossReleased;
        return released || phase is (byte)DefenseInteractionPhase.Throwing or (byte)DefenseInteractionPhase.UpTossing;
    }

    internal static bool IsWindup(byte phase) => TryDecode(phase, out _, out bool released) && !released;
    internal static bool IsRelease(byte phase) => TryDecode(phase, out _, out bool released) && released;

    public static bool WasReleased(in CharacterState holderBefore, in CharacterState victimBefore,
        in CharacterState holderAfter, in CharacterState victimAfter)
        => holderBefore.State == ActionState.Throwing && victimBefore.State == ActionState.Grabbed
            && holderBefore.InteractionId != 0 && victimBefore.InteractionId == holderBefore.InteractionId
            && holderBefore.InteractionPartnerId == victimBefore.EntityId
            && victimBefore.InteractionPartnerId == holderBefore.EntityId
            && IsWindup(holderBefore.InteractionPhase) && victimBefore.InteractionPhase == holderBefore.InteractionPhase
            && holderAfter.EntityId == holderBefore.EntityId && victimAfter.EntityId == victimBefore.EntityId
            && holderAfter.InteractionId == 0 && victimAfter.InteractionId == 0
            && holderAfter.LastTerminalInteractionId == holderBefore.InteractionId
            && victimAfter.LastTerminalInteractionId == holderBefore.InteractionId
            && holderAfter.InteractionTerminalTick != holderBefore.InteractionTerminalTick
            && holderAfter.InteractionPhase == Specification(holderBefore.InteractionPhase == (byte)DefenseInteractionPhase.UpTossing
                ? Kind.UpToss : Kind.Fling).ReleasedPhase;

    public static bool TrySamplePose(in CharacterState state, out GrabThrowPose pose)
    {
        pose = default;
        if (!TryDecode(state.InteractionPhase, out var kind, out bool released)) return false;
        bool thrower = state.State == ActionState.Throwing;
        if (!released && state.InteractionId != 0 && (thrower || state.State == ActionState.Grabbed))
        {
            pose = new(thrower ? GrabThrowRole.Thrower : GrabThrowRole.Victim,
                Math.Max(0, DefenseConfig.ThrowReleaseTicks - state.StateTicks) * Simulation.TickDt);
            return true;
        }
        if (released && thrower && state.InteractionId == 0)
        {
            pose = new(GrabThrowRole.Thrower,
                Math.Max(0, DefenseConfig.ThrowReleaseTicks + Specification(kind).RecoveryTicks - state.StateTicks) * Simulation.TickDt);
            return true;
        }
        return false;
    }
}
