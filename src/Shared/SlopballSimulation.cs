using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    public enum SlopballPhase : byte { WaitingServe, Live, Stopped, Countdown }
    public enum SlopballEventKind : byte { Served, Received, Redirected, Returned, ReServe, Missed, Stopped }
    public readonly struct SlopballEvent
    {
        public readonly SlopballEventKind Kind;
        public readonly ulong BallId, Returns;
        public readonly float ContactTime;
        public SlopballEvent(SlopballEventKind kind, ulong ballId, ulong returns, float time)
        { Kind = kind; BallId = ballId; Returns = returns; ContactTime = time; }
    }
    public readonly struct SlopballState
    {
        public readonly SlopballPhase Phase;
        public readonly ulong Returns, BestStreak;
        public readonly uint SessionSeed;
        public readonly ushort ServeInTicks;
        public readonly ushort CountdownTicks;
        public readonly bool Received, ReturnArmed, ClearedNet;
        public readonly SlopballShot Shot;
        public readonly float Difficulty;
        public bool Ended => Phase == SlopballPhase.Stopped;
        /// <summary>Rally heat (0-4): one step per five returns in the combo; it changes the serve mix.</summary>
        public int Heat => SlopballDefinition.HeatOf(Returns);
        public SlopballState(SlopballPhase phase, ulong returns, ushort serveInTicks, bool received,
            bool returnArmed, SlopballShot shot, float difficulty, ushort countdownTicks = 0, bool clearedNet = false,
            ulong bestStreak = 0, uint sessionSeed = 0)
        { Phase = phase; Returns = returns; ServeInTicks = serveInTicks; Received = received;
            ReturnArmed = returnArmed; Shot = shot; Difficulty = difficulty; CountdownTicks = countdownTicks; ClearedNet = clearedNet;
            BestStreak = bestStreak; SessionSeed = sessionSeed; }
    }

    /// <summary>Solo challenge rules and chronological goals; normal fighter simulation continues between shots.</summary>
    internal sealed class SlopballSimulation
    {
        private enum TraceKind : byte { Floor, Receive, Body, Clear }
        private readonly struct Trace
        {
            public readonly TraceKind Kind;
            public readonly float Time;
            public readonly ArcadePoint Position;
            public Trace(TraceKind kind, float time, ArcadePoint position) { Kind = kind; Time = time; Position = position; }
        }
        private readonly SlopballDefinition _rules;
        private readonly ArcadeBallDefinition _ballDefinition;
        private readonly float _ceiling;
        private readonly List<Trace> _trace = new();
        private readonly List<SlopballEvent> _events = new();
        private SlopballPhase _phase = SlopballPhase.WaitingServe;
        private ulong _returns, _bestStreak, _shotIndex;
        private uint _seed;
        private ushort _serveIn;
        private ushort _countdown;
        private bool _received, _armed, _cleared, _attacked;
        private int _cursor;
        private SlopballShot _shot;
        public IReadOnlyList<SlopballEvent> Events => _events;
        public SlopballState State => new(_phase, _returns, _serveIn, _received, _armed, _shot,
            (float)Math.Min(_returns, _rules.RampReturns) / _rules.RampReturns, _countdown, _cleared, _bestStreak, _seed);

        /// <param name="ceiling">The court's ceiling height, which keeps moonball serves under it.</param>
        public SlopballSimulation(SlopballDefinition rules, ArcadeBallDefinition definition, ArcadeBallSimulation ball,
            float ceiling = float.PositiveInfinity)
        {
            _rules = rules; _ballDefinition = definition; _ceiling = ceiling;
            _seed = rules.Seed;
            _shot = rules.CreateShot(definition, 0, 0, _seed, _ceiling); _serveIn = rules.ServeGapTicks;
            ball.Retire();
        }

        public void BeginCountdown(ArcadeBallSimulation ball, ushort ticks, uint? sessionSeed = null)
        {
            if (ticks == 0 || ticks > 600) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (_phase != SlopballPhase.WaitingServe || _returns != 0 || _shotIndex != 0)
                throw new InvalidOperationException("A countdown requires a clean Slopball attempt.");
            if (sessionSeed == 0) throw new ArgumentOutOfRangeException(nameof(sessionSeed));
            _seed = sessionSeed ?? _rules.Seed;
            _shot = _rules.CreateShot(_ballDefinition, 0, 0, _seed, _ceiling);
            _phase = SlopballPhase.Countdown; _countdown = ticks; _serveIn = 0;
            ball.Retire();
        }

        public bool BeginTick(ArcadeBallSimulation ball)
        {
            _events.Clear(); _trace.Clear(); _cursor = 0; _attacked = false;
            if (State.Ended) return false;
            if (_phase == SlopballPhase.Countdown)
            {
                if (--_countdown == 0) Serve(ball);
                // Go launches from the untouched spawn. Gameplay starts on the next
                // tick, after the input adapter has discarded pre-Go edges.
                return false;
            }
            if (_phase == SlopballPhase.WaitingServe && --_serveIn == 0)
                Serve(ball);
            return true;
        }

        private void Serve(ArcadeBallSimulation ball)
        {
            ball.Reset(_shot.Spawn, _shot.Velocity, _shot.Curve);
            _received = _armed = _cleared = false; _phase = SlopballPhase.Live;
            Emit(SlopballEventKind.Served, ball, 0f);
        }

        public void PlanMotion(ArcadeBallSimulation ball)
        {
            if (_phase != SlopballPhase.Live) return;
            float receiveZ = _rules.NetZ - _rules.NetHalfThickness - ball.State.Radius;
            float clearZ = _rules.NetZ + _rules.NetHalfThickness + ball.State.Radius;
            foreach (var segment in ball.Path)
            {
                if (!_received && segment.Start.Z <= receiveZ) _trace.Add(new(TraceKind.Receive, segment.StartTime, segment.Start));
                AddCrossing(segment, receiveZ, positive: false, TraceKind.Receive);
                AddCrossing(segment, clearZ, positive: true, TraceKind.Clear);
            }
            foreach (var contact in ball.Contacts)
            {
                if (contact.Body) _trace.Add(new(TraceKind.Body, contact.Time, contact.Position));
                else if (contact.NormalY > .5f && contact.Position.Y <= _rules.FloorY + ball.State.Radius + .001f)
                    _trace.Add(new(TraceKind.Floor, contact.Time, contact.Position));
            }
            _trace.Sort((a, b) => { int time = a.Time.CompareTo(b.Time); return time != 0 ? time : a.Kind.CompareTo(b.Kind); });
            foreach (var item in _trace)
            {
                if (item.Kind == TraceKind.Floor)
                {
                    // A terminal boundary wins ties; projectiles never consume a retired ball
                    // later in this tick. An earlier attack invalidates this provisional future.
                    ball.ClipCollisionPath(MathF.Max(0f, item.Time - .000001f));
                    break;
                }
            }
        }

        public bool BeforeAttack(ArcadeBallSimulation ball, float time, bool playerContact = true)
        {
            CommitThrough(ball, time);
            if (_phase != SlopballPhase.Live) return false;
            _attacked = true;
            if (_received && playerContact) { _armed = true; Emit(SlopballEventKind.Redirected, ball, time); }
            return true;
        }

        /// <summary>
        /// "Every touch pops": after a damaging hit's launch, a ball in the player half that would not clear the
        /// net on its own is relaunched as a lob that does (SlopballDefinition.ReturnAssist). The hit still reports
        /// its own launch, so a feeble tap reads as one. Returns whether the ball was relaunched.
        /// </summary>
        public bool AssistReturn(ArcadeBallSimulation ball, float launch)
        {
            if (_phase != SlopballPhase.Live) return false;
            var state = ball.State;
            if (_rules.ReturnAssist(_ballDefinition, state.Position, state.Velocity, launch) is not ArcadePoint velocity) return false;
            ball.Relaunch(velocity);
            return true;
        }

        public void FinishTick(ArcadeBallSimulation ball)
        { if (!_attacked) CommitThrough(ball, 1f); }

        public void Stop(ArcadeBallSimulation ball)
        {
            if (State.Ended) return;
            _phase = SlopballPhase.Stopped; ball.Retire(); Emit(SlopballEventKind.Stopped, ball, 1f);
        }

        private void CommitThrough(ArcadeBallSimulation ball, float time)
        {
            while (_phase == SlopballPhase.Live && _cursor < _trace.Count && _trace[_cursor].Time <= time)
            {
                var item = _trace[_cursor++];
                if (item.Kind == TraceKind.Receive && !_received)
                { _received = true; Emit(SlopballEventKind.Received, ball, item.Time); }
                else if (item.Kind == TraceKind.Body && _received)
                { _armed = true; Emit(SlopballEventKind.Redirected, ball, item.Time); }
                else if (item.Kind == TraceKind.Floor)
                {
                    ball.RetireAt(item.Position);
                    if (item.Position.Z <= _rules.NetZ)
                    {
                        Emit(SlopballEventKind.Missed, ball, item.Time);
                        _returns = 0; Schedule();
                    }
                    else if (_received && _armed && _cleared)
                    {
                        if (_returns == ulong.MaxValue) throw new InvalidOperationException("Slopball counter exhausted.");
                        _returns++;
                        _bestStreak = Math.Max(_bestStreak, _returns);
                        Schedule(); Emit(SlopballEventKind.Returned, ball, item.Time);
                    }
                    else { Schedule(); Emit(SlopballEventKind.ReServe, ball, item.Time); }
                }
                else if (item.Kind == TraceKind.Clear && _received && _armed
                    && item.Position.Y - ball.State.Radius >= _rules.NetTop)
                {
                    // Crossing is only eligibility. The ball remains hittable and can
                    // bounce back into the player's half until either floor ends the shot.
                    _cleared = true;
                }
            }
        }
        private void Schedule()
        {
            if (_shotIndex == ulong.MaxValue) throw new InvalidOperationException("Slopball shot sequence exhausted.");
            _shotIndex++;
            _shot = _rules.CreateShot(_ballDefinition, _shotIndex, _returns, _seed, _ceiling);
            _phase = SlopballPhase.WaitingServe; _serveIn = _rules.ServeGapTicks;
        }
        private void Emit(SlopballEventKind kind, ArcadeBallSimulation ball, float time)
            => _events.Add(new(kind, ball.State.EntityId, _returns, time));
        private void AddCrossing(SphereMotionSegment segment, float z, bool positive, TraceKind kind)
        {
            if (positive ? segment.Start.Z >= z || segment.End.Z < z : segment.Start.Z <= z || segment.End.Z > z) return;
            float time = segment.StartTime + (segment.EndTime - segment.StartTime) * (z - segment.Start.Z) / (segment.End.Z - segment.Start.Z);
            _trace.Add(new(kind, time, segment.Sample(time)));
        }
    }
}
