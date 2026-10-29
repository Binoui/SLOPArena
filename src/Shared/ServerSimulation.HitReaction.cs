namespace SlopArena.Shared;

public partial class ServerSimulation
{
    private static void QueueHitReaction(in SpellResolver.HitResult hit, ref CharacterState victim,
        ref CharacterState attacker, bool attackerExists, ushort freeze, float dirX, float dirZ,
        float launchBase, float launchGrowth, float kbForce, float finalDamage,
        bool resolvedForce, bool zeroForce, bool crouchBrace)
    {
        victim.HitstopTicks = freeze;
        if (hit.ArmorProtected)
        {
            victim.QueuedArmorHitstop = true;
        }
        else
        {
            victim.QueuedArmorHitstop = false;
            victim.QueuedKBDirX = dirX; victim.QueuedKBDirZ = dirZ;
            victim.QueuedKBAngle = hit.KnockbackAngle;
            victim.QueuedKBBase = launchBase;
            victim.QueuedKBGrowth = launchGrowth;
            victim.QueuedKBForce = kbForce;
            victim.QueuedKBResolvedForce = resolvedForce;
            victim.QueuedKBZero = zeroForce;
            victim.QueuedKBDamage = finalDamage;
            victim.QueuedKBStun = hit.StunTicks;
            victim.QueuedKBFixedHitstunTicks = hit.FixedHitstunTicks;
            victim.QueuedKBStunGate = hit.StunTicks;
            victim.QueuedCrouchBrace = crouchBrace;
        }
        if (hit.FreezesOwner && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
            attacker.HitstopTicks = freeze;
    }
}
