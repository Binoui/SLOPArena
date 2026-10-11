using System;
using SlopArena.Shared;

namespace SlopArena.Client.World;

/// <summary>Cosmetic lifetime shared by match playback and recorded Lab previews.</summary>
public struct PresentationLifetime
{
    public int RemainingTicks { get; private set; }
    private readonly bool _attackBound;
    private readonly byte _sequence;
    private int _lastAttackTick;

    public PresentationLifetime(int durationTicks, bool attackBound, byte sequence, int attackTick)
    {
        RemainingTicks = Math.Max(1, durationTicks);
        _attackBound = attackBound;
        _sequence = sequence;
        _lastAttackTick = attackTick;
    }

    public static bool IsCurrentAttack(ActionState state, byte sequence, byte eventSequence)
        => state == ActionState.Attacking && sequence == eventSequence;

    public bool Advance(bool ownerAvailable, ActionState state, byte sequence, int attackTick)
    {
        if (RemainingTicks <= 0) return false;
        if (!_attackBound)
            RemainingTicks--;
        else if (!ownerAvailable || !IsCurrentAttack(state, sequence, _sequence))
            RemainingTicks = 0;
        else
        {
            RemainingTicks -= Math.Max(0, attackTick - _lastAttackTick);
            _lastAttackTick = attackTick;
        }
        return RemainingTicks > 0;
    }
}
