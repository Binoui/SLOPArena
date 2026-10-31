namespace SlopArena.Shared.AI;

internal enum CpuAttackPhase : byte { None, Startup, Active, Recovery, Unknown }
internal readonly struct CpuTimingSnapshot
{
    public readonly CpuAttackPhase Phase;
    public readonly ushort LockTicks;
    public readonly uint AttackEpisode, RecoveryEpisode;
    public CpuTimingSnapshot(CpuAttackPhase phase, ushort lockTicks, uint attackEpisode = 0, uint recoveryEpisode = 0)
    { Phase = phase; LockTicks = lockTicks; AttackEpisode = attackEpisode; RecoveryEpisode = recoveryEpisode; }
}

internal sealed class CpuAttackModel
{
    private readonly int[] _starts, _actionEnds;
    private readonly int _first, _end;
    private readonly bool _known;

    public CpuAttackModel(CookedSlotDefinition slot)
    {
        _starts = new int[slot.Timeline.Stages.Count];
        _actionEnds = new int[_starts.Length];
        int offset = 0, first = int.MaxValue, end = 0;
        bool known = true;
        for (int i = 0; i < _starts.Length; i++)
        {
            var stage = slot.Timeline.Stages[i];
            _starts[i] = offset;
            _actionEnds[i] = offset + (stage.IasaTicks > 0 ? stage.IasaTicks : stage.DurationTicks);
            foreach (var operation in stage.Operations)
            {
                int tick = offset + operation.Tick;
                int start = tick, last = tick;
                switch (operation)
                {
                    case CookedSpawnHitboxOperation hit:
                        last += hit.Hitbox.DurationTicks; break;
                    case CookedSpawnProjectileOperation _:
                        last++; break;
                    case CookedStartCapabilityOperation cap:
                        switch (cap.Parameters)
                        {
                            case CookedDashStrikeCapabilityParameters dash: last += dash.TravelTicks + dash.TipHitbox.DurationTicks; break;
                            case CookedCycloneKickCapabilityParameters cyclone:
                                start += cyclone.WindupTicks;
                                last += System.Math.Max(cyclone.HitboxEndTick, cyclone.FinisherTick + 1); break;
                            case CookedRisingDragonCapabilityParameters rise: start += rise.RiseDelay; last += rise.RiseDelay + rise.RiseTicks; break;
                            case CookedWibouRisingSlashCapabilityParameters rise: last += rise.RiseTicks; break;
                            case CookedChargedProjectileCapabilityParameters shot: start += shot.FireTriggerTick; last = start + 1; break;
                            case CookedFingerGunsCapabilityParameters shot: start += shot.FireTriggerTick; last = start + 1; break;
                            case CookedMankiRoundBombCapabilityParameters mine: start += mine.ArmTicks + mine.WarningTicks; last = start + 1; break;
                            case CookedMankiBazookaCapabilityParameters shot: start += shot.ChargeTicks > 0 ? shot.ChargeTicks : shot.FireTriggerTick; last = start + 1; break;
                            default: known = false; continue;
                        }
                        break;
                    default: continue;
                }
                first = System.Math.Min(first, start);
                end = System.Math.Max(end, last);
            }
            offset += stage.DurationTicks;
        }
        _known = known && first != int.MaxValue;
        _first = first; _end = end;
    }

    public CpuTimingSnapshot Capture(in CharacterState state)
    {
        if (state.LandingLagTicks > 0) return new CpuTimingSnapshot(CpuAttackPhase.Recovery, state.LandingLagTicks);
        if (state.State == ActionState.HomingDashMiss) return new CpuTimingSnapshot(CpuAttackPhase.Recovery, state.StateTicks);
        if (state.State is ActionState.HomingDash or ActionState.HomingDashFloat)
            return new CpuTimingSnapshot(state.State == ActionState.HomingDashFloat ? CpuAttackPhase.Startup : CpuAttackPhase.Active, 0);
        if (state.State is not (ActionState.Attacking or ActionState.Aiming)) return default;
        if (!_known || state.State == ActionState.Aiming) return new CpuTimingSnapshot(CpuAttackPhase.Unknown, 0);
        int stage = System.Math.Min(state.ComboStage, _starts.Length - 1);
        int remaining = System.Math.Max(0, System.Math.Min(state.AnimLockTicks, _actionEnds[stage] - state.AttackElapsedTicks));
        var phase = state.AttackElapsedTicks < _first ? CpuAttackPhase.Startup
            : state.AttackElapsedTicks < _end ? CpuAttackPhase.Active : CpuAttackPhase.Recovery;
        return new CpuTimingSnapshot(phase, (ushort)remaining);
    }

    public static CpuTimingSnapshot WithoutActivation(in CharacterState state)
    {
        if (state.LandingLagTicks > 0) return new CpuTimingSnapshot(CpuAttackPhase.Recovery, state.LandingLagTicks);
        if (state.State == ActionState.HomingDashMiss) return new CpuTimingSnapshot(CpuAttackPhase.Recovery, state.StateTicks);
        if (state.State is ActionState.Attacking or ActionState.Aiming or ActionState.HomingDash or ActionState.HomingDashFloat)
            return new CpuTimingSnapshot(CpuAttackPhase.Unknown, 0);
        return default;
    }
}
