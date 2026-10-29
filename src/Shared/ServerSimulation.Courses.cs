using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public partial class ServerSimulation
{
    private readonly OnlineCourseSimulation? _onlineCourse;
    private readonly Dictionary<ulong, ArcadePoint> _coursePrevious = new();
    private readonly Dictionary<ulong, int> _courseCheckpoints = new();
    public OnlineCourseState? OnlineCourse => _onlineCourse?.State;
    public CourseScrambleState? Scramble => _onlineCourse?.Scramble;
    public ulong CourseTargetBreaker(int index) => _onlineCourse?.Breaker(index) ?? 0;
    public IReadOnlyList<CoursePlayerProgress> CoursePlayers => _onlineCourse?.Players.ToArray() ?? Array.Empty<CoursePlayerProgress>();
    public IReadOnlyList<CourseTickEvent> LastTickOnlineCourseEvents => _onlineCourse?.Events ?? Array.Empty<CourseTickEvent>();
    public CharacterCourseState? CourseFor(ulong id) => _onlineCourse?.For(id)?.State ?? _course?.State;
    public bool IsCourseTargetBroken(ulong id, int index) => CourseSimulationFor(id)?.IsBroken(index) ?? false;
    public int CourseTargetHitsLeft(ulong id, int index) => CourseSimulationFor(id)?.HitsLeft(index) ?? 0;
    public ulong CourseTargetEntityId(ulong id, int index) => CourseSimulationFor(id)?.EntityId(index) ?? 0;
    private CharacterCourseSimulation? CourseSimulationFor(ulong id) => _onlineCourse?.For(id) ?? _course;
    public bool IsEntityInactive(ulong id) => _states.TryGetValue(id, out var state) && IsEliminated(state);
    private bool IsEliminated(CharacterState state) => _rule.IsEliminated(state) || (_onlineCourse?.Inactive(state.EntityId) ?? false)
        || (_mayhem?.Inactive(state.EntityId) ?? false);

    public void BeginOnlineCourse()
    {
        if (_onlineCourse == null) throw new InvalidOperationException("This Match has no online course.");
        _onlineCourse.Begin();
    }
    public void WithdrawCoursePlayer(ulong id)
    {
        if (_onlineCourse == null || _onlineCourse.Inactive(id) || !_states.ContainsKey(id)) return;
        _onlineCourse.Withdraw(id);
        FreezeCourseFighter(id);
    }

    private void FreezeCourseFighter(ulong id)
    {
        var state = _states[id];
        if (state.InteractionId != 0) InterruptInteraction(ref state);
        CancelAttackRuntime(id, ref state, removeProjectiles: true);
        state.VX = state.VY = state.VZ = state.KVX = state.KVY = state.KVZ = 0;
        state.HitstopTicks = 0; state.TargetEntityId = 0; state.LockOn = false; state.State = ActionState.Idle;
        _states[id] = state;
    }

    private void CaptureCoursePositions()
    {
        _coursePrevious.Clear(); _courseCheckpoints.Clear();
        foreach (var pair in _states)
        {
            _coursePrevious[pair.Key] = new(pair.Value.PX, pair.Value.PY, pair.Value.PZ);
            _courseCheckpoints[pair.Key] = _onlineCourse!.For(pair.Key)!.State.SavedCheckpoint;
        }
    }
    private void FinishOnlineCourseTick()
    {
        if (_onlineCourse == null) return;
        _onlineCourse.FinishTick(_tick, _coursePrevious, _states, LastTickDeaths.Select(x => x.EntityId).ToArray(), RespawnCourseRacer);
        foreach (var progress in _onlineCourse.Players)
            if (progress.Course.SavedCheckpoint != _courseCheckpoints[progress.EntityId])
            {
                var checkpoint = _arena.Arcade!.Course!.Checkpoints[progress.Course.SavedCheckpoint];
                SetRespawnPosition(progress.EntityId, checkpoint.Respawn.X, checkpoint.Respawn.Y, checkpoint.Respawn.Z, checkpoint.RespawnYaw);
            }
        foreach (var item in _onlineCourse.Events)
            if (item.Event.Kind == CharacterCourseEventKind.Completed)
                FreezeCourseFighter(item.EntityId);
    }
    private void RespawnCourseRacer(ulong id, CourseSafeGround ground)
    {
        FreezeCourseFighter(id);
        var old = _states[id];
        _lastHitCredits.Remove(id); _lastHitContexts.Remove(id);
        _spellResolver.StopHomingAt(id);
        var definition = Def(id);
        _states[id] = new CharacterState
        {
            EntityId = id, PX = ground.Position.X, PY = ground.Position.Y, PZ = ground.Position.Z,
            FacingYaw = ground.Yaw, AimYaw = ground.Yaw, IsGrounded = true, State = ActionState.Idle,
            JumpsLeft = definition.Movement.MaxJumps, AirDodgesLeft = 1, Deaths = old.Deaths,
            InvincibilityTicks = RespawnInvincibilityTicks,
            LastTerminalInteractionId = old.LastTerminalInteractionId, InteractionTerminalTick = old.InteractionTerminalTick,
            InteractionPhase = (byte)(old.LastTerminalInteractionId != 0 ? DefenseInteractionPhase.Terminal : DefenseInteractionPhase.None),
            Form = new FormState { Active = old.Form.Active },
        };
    }

}
