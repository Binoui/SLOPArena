using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Client prediction for full-world rollback (ADR-0039). One <see cref="ServerSimulation"/>
    /// holds every fighter. The newest World Frame from the GameHost is the Anchor: the
    /// predictor restores its snapshot and re-simulates the Prediction Window up to the local
    /// tick with the player's recorded inputs and, for each opponent, the input the GameHost
    /// relayed at the anchor tick without its press edges. When the earlier prediction of the
    /// anchor tick matches the snapshot byte for byte and the opponents' assumed inputs did not
    /// change, there is nothing to redo. Between frames it advances one paced tick at a time.
    ///
    /// Presentation events and hits pass through a <see cref="RollbackLedger{TKey,TItem}"/> each:
    /// each starts once, predicted ones are retracted when the anchor contradicts them, and those
    /// the client never predicted start when the GameHost's record of their tick arrives.
    /// </summary>
    public sealed class NetplayPredictor
    {
        /// <summary>Longest Prediction Window; past it <see cref="Advance"/> waits for frames.</summary>
        public const uint MaxPredictionTicks = 60;
        /// <summary>Smallest anchor-tick difference counted as a misprediction rather than drift.</summary>
        public const float MispredictionMeters = 0.01f;
        private const int HistoryCapacity = (int)MaxPredictionTicks + 4;

        private readonly ServerSimulation _sim;
        private readonly IMatchRule _rule;
        private readonly ulong _selfId;
        private readonly List<ulong> _fighters = new();
        private readonly Dictionary<ulong, InputState> _tickInputs = new();
        private Dictionary<ulong, InputState> _assumed = new();
        private Dictionary<ulong, InputState> _nextAssumed = new();
        private readonly uint[] _inputTicks = new uint[HistoryCapacity];
        private readonly InputState[] _inputs = new InputState[HistoryCapacity];
        private readonly Prediction[] _predictions = new Prediction[HistoryCapacity];
        private readonly RollbackLedger<PresentationEventKey, TimelinePresentationEvent> _events =
            new(static e => e.Key, static k => k.MatchTick);
        private readonly RollbackLedger<HitKey, SpellResolver.HitResult> _hits =
            new(static h => HitKey.Of(h), static k => k.MatchTick);
        private readonly RollbackLedger<SlopballEventKey, SlopballTickEvent> _slopball =
            new(static e => e.Key, static k => k.MatchTick);
        private readonly RollbackLedger<CourseEventKey, CourseTickEvent> _courses =
            new(static e => e.Key, static k => k.MatchTick);
        private readonly RollbackLedger<MayhemEventKey, MankiMayhemEvent> _mayhem =
            new(static e => e.Key, static k => k.MatchTick);
        private readonly List<MankiMayhemEvent> _tickMayhem = new();
        private readonly List<CourseTickEvent> _tickCourses = new();
        private readonly List<SlopballTickEvent> _tickSlopball = new();
        private readonly List<TimelinePresentationEvent> _tickEvents = new();
        private readonly List<SpellResolver.HitResult> _tickHits = new();
        private CharacterState[] _presented = Array.Empty<CharacterState>();
        private uint _localTick;
        private uint _anchorTick;
        private bool _hasAnchor;
        private bool _started;
        private NetplayPredictorStats _window;
        private (int Started, int Retracted) _reportedEffects, _reportedHits;

        public NetplayPredictor(ArenaDefinition arena, ulong selfEntityId, IMatchRule? rule = null)
        {
            _rule = rule ?? new StockMatchRule(3);
            _sim = new ServerSimulation(arena, _rule);
            _selfId = selfEntityId;
            for (int i = 0; i < _predictions.Length; i++)
                _predictions[i] = new Prediction();
        }

        public ulong SelfEntityId => _selfId;
        /// <summary>Last simulated tick: presented states are the states after it.</summary>
        public uint LocalTick => _localTick;
        /// <summary>Tick of the newest restored or confirmed World Snapshot; 0 before the first frame.</summary>
        public uint AnchorTick => _anchorTick;
        public bool HasAnchor => _hasAnchor;
        /// <summary>Ticks predicted beyond the anchor.</summary>
        public uint PredictionTicks => _hasAnchor && _localTick > _anchorTick ? _localTick - _anchorTick : 0;
        /// <summary>Match state of the anchor frame.</summary>
        public MatchState AuthoritativeMatchState { get; private set; } = MatchState.Waiting;
        public SpellResolver Resolver => _sim.Resolver;
        /// <summary>The predicted world, for presentation reads (definitions, hitboxes); never mutate it.</summary>
        public ServerSimulation Simulation => _sim;

        /// <summary>Register every fighter of the match, as the GameHost does, before the first tick.</summary>
        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            if (_started)
                throw new InvalidOperationException("Fighters must be registered before prediction starts.");
            _sim.RegisterEntity(id, def, initialState, baked);
            if (!_fighters.Contains(id))
            {
                _fighters.Add(id);
                _fighters.Sort();
            }
            if (id != _selfId)
                _assumed[id] = default; // a connected opponent with no input yet consumes neutral
            _presented = new CharacterState[_fighters.Count];
            foreach (var prediction in _predictions)
                prediction.Resize(_fighters.Count);
        }

        /// <summary>Seed the absolute GameHost tick before prediction begins.</summary>
        public bool SetTimeline(uint lastCompletedTick)
        {
            if (_started || lastCompletedTick < _localTick)
                return false;
            _localTick = lastCompletedTick;
            _sim.SetTick(lastCompletedTick);
            return true;
        }

        /// <summary>
        /// Predict the next tick with the player's input, which is recorded for re-simulation.
        /// Returns false without simulating when the Prediction Window is full.
        /// </summary>
        public bool Advance(InputState selfInput)
        {
            if (AuthoritativeMatchState == MatchState.Ended)
                return false;
            if (_hasAnchor && _localTick - _anchorTick >= MaxPredictionTicks)
            {
                _window.StarvedTicks++;
                return false;
            }
            _started = true;
            uint tick = _localTick + 1;
            int slot = Slot(tick);
            _inputTicks[slot] = tick;
            _inputs[slot] = AsConsumed(selfInput);
            SimulateTick(tick);
            _localTick = tick;
            return true;
        }

        /// <summary>The input recorded for <paramref name="tick"/> as the GameHost decodes it.</summary>
        public bool TryGetRecordedInput(uint tick, out InputState input)
        {
            int slot = Slot(tick);
            input = _inputs[slot];
            return _inputTicks[slot] == tick && tick != 0;
        }

        public FrameIngestResult IngestFrame(WorldFramePacket frame) => IngestFrames(new[] { frame });

        /// <summary>
        /// Validate each frame before accepting its records. The newest valid frame anchors
        /// prediction; valid older frames can still recover missing authoritative history.
        /// </summary>
        public FrameIngestResult IngestFrames(IReadOnlyList<WorldFramePacket> frames)
        {
            var accepted = new List<(WorldFramePacket Frame, ServerSimulation.PreparedSnapshot Snapshot)>();
            WorldFramePacket? newest = null;
            ServerSimulation.PreparedSnapshot? prepared = null;
            uint? finish = AuthoritativeMatchState == MatchState.Ended ? _anchorTick : null;
            foreach (var frame in frames)
            {
                _window.Frames++;
                ServerSimulation.PreparedSnapshot snapshot;
                try
                {
                    ValidateRecords(frame);
                    snapshot = _sim.PrepareSnapshot(frame.Snapshot);
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
                {
                    _window.RejectedFrames++;
                    continue;
                }
                accepted.Add((frame, snapshot));
                if (_hasAnchor && frame.Tick <= _anchorTick)
                    _window.StaleFrames++;
                if (AuthoritativeMatchState == MatchState.Ended)
                    continue;
                bool advances = !_hasAnchor || frame.Tick > _anchorTick ||
                    frame.Tick == _anchorTick && frame.MatchState > AuthoritativeMatchState;
                if (!advances)
                    continue;
                if (frame.MatchState == MatchState.Ended && (!finish.HasValue || frame.Tick < finish.Value))
                    finish = frame.Tick;
                if (newest == null || frame.Tick > newest.Tick ||
                    frame.Tick == newest.Tick && frame.MatchState > newest.MatchState)
                {
                    newest = frame;
                    prepared = snapshot;
                }
            }

            bool acceptedAny = false;
            foreach (var item in accepted)
            {
                if (finish.HasValue && item.Frame.Tick > finish.Value)
                    continue;
                RecordAuthoritative(item.Frame);
                acceptedAny = true;
                if (AuthoritativeMatchState != MatchState.Ended && finish.HasValue &&
                    item.Frame.Tick == finish.Value && item.Frame.MatchState == MatchState.Ended)
                {
                    newest = item.Frame;
                    prepared = item.Snapshot;
                }
            }
            uint before = _localTick;
            bool ended = false;
            if (newest != null)
            {
                Anchor(newest, prepared!);
                AuthoritativeMatchState = newest.MatchState;
                ended = newest.MatchState == MatchState.Ended;
            }
            return new FrameIngestResult(acceptedAny, ended || _localTick != before);
        }

        public CharacterState GetState(ulong id) => _sim.GetState(id);
        public IReadOnlyDictionary<ulong, CharacterState> GetAllStates() => _sim.GetAllStates();

        /// <summary>Presentation events to start and to stop since the previous call.</summary>
        public void DrainPresentation(List<TimelinePresentationEvent> started, List<TimelinePresentationEvent> retracted)
            => _events.Drain(started, retracted);

        /// <summary>Hits to give feedback for, and predicted hits the GameHost ruled out, since the previous call.</summary>
        public void DrainHits(List<SpellResolver.HitResult> started, List<SpellResolver.HitResult> retracted)
            => _hits.Drain(started, retracted);

        /// <summary>Slopball events to present, and predicted ones the GameHost ruled out, since the previous call (ADR-0040).</summary>
        public void DrainMayhemEvents(List<MankiMayhemEvent> started, List<MankiMayhemEvent> retracted)
            => _mayhem.Drain(started, retracted);

        public void DrainCourseEvents(List<CourseTickEvent> started, List<CourseTickEvent> retracted)
            => _courses.Drain(started, retracted);

        public void DrainSlopballEvents(List<SlopballTickEvent> started, List<SlopballTickEvent> retracted)
            => _slopball.Drain(started, retracted);

        /// <summary>Prediction health since the previous call.</summary>
        public NetplayPredictorStats TakeStats()
        {
            var stats = _window;
            stats.PredictionTicks = PredictionTicks;
            stats.EffectsStarted = _events.StartedTotal - _reportedEffects.Started;
            stats.EffectsRetracted = _events.RetractedTotal - _reportedEffects.Retracted;
            stats.HitsStarted = _hits.StartedTotal - _reportedHits.Started;
            stats.HitsRetracted = _hits.RetractedTotal - _reportedHits.Retracted;
            _reportedEffects = (_events.StartedTotal, _events.RetractedTotal);
            _reportedHits = (_hits.StartedTotal, _hits.RetractedTotal);
            _window = default;
            return stats;
        }

        private void Anchor(WorldFramePacket frame, ServerSimulation.PreparedSnapshot prepared)
        {
            uint tick = frame.Tick;
            AssumeFrom(frame, _nextAssumed);
            CheckSelfInput(frame);

            if (frame.MatchState == MatchState.Ended)
            {
                bool finalExact = TryGetPrediction(tick, out var final) && final!.Snapshot!.ContentEquals(frame.Snapshot);
                CapturePresented();
                _sim.ApplySnapshot(prepared);
                if (finalExact) _window.ExactAnchors++;
                else CountCorrection(final);
                SetAnchor(tick);
                _localTick = tick;
                Array.Clear(_inputTicks, 0, _inputTicks.Length);
                Array.Clear(_inputs, 0, _inputs.Length);
                foreach (var future in _predictions)
                    future.Snapshot = null;
                StorePrediction(tick, frame.Snapshot);
                _assumed.Clear();
                _nextAssumed.Clear();
                BeginResimulation(tick);
                EndResimulation();
                MeasureSnap();
                return;
            }

            if (tick >= _localTick)
            {
                // The GameHost is at or past the local tick: take its state as it is.
                bool same = TryGetPrediction(tick, out var current) && current!.Snapshot!.ContentEquals(frame.Snapshot);
                if (same)
                    _window.ExactAnchors++;
                else
                {
                    CapturePresented();
                    _sim.ApplySnapshot(prepared);
                    MeasureSnap();
                    if (tick > _localTick)
                        _window.Jumps++;
                    else
                        CountCorrection(current);
                    StorePrediction(tick, frame.Snapshot);
                }
                SetAnchor(tick);
                _localTick = tick;
                SwapAssumed();
                return;
            }

            uint age = _localTick - tick;
            if (age > _window.MaxAnchorAgeTicks)
                _window.MaxAnchorAgeTicks = age;
            bool predicted = TryGetPrediction(tick, out var prediction);
            bool exact = predicted && prediction!.Snapshot!.ContentEquals(frame.Snapshot);
            if (exact && SameInputs(_assumed, _nextAssumed))
            {
                _window.ExactAnchors++;
                SetAnchor(tick);
                return;
            }

            long started = Stopwatch.GetTimestamp();
            CapturePresented();
            _sim.ApplySnapshot(prepared);
            if (exact)
                _window.ExactAnchors++;
            else
                CountCorrection(predicted ? prediction : null);
            SetAnchor(tick);
            StorePrediction(tick, frame.Snapshot);
            SwapAssumed();
            BeginResimulation(tick);
            for (uint t = tick + 1; t <= _localTick; t++)
                SimulateTick(t);
            EndResimulation();
            MeasureSnap();

            _window.ResimulatedTicks += (int)age;
            if (age > _window.MaxResimulatedTicks)
                _window.MaxResimulatedTicks = age;
            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _window.TotalResimulationMs += milliseconds;
            if (milliseconds > _window.MaxResimulationMs)
                _window.MaxResimulationMs = milliseconds;
        }

        private void BeginResimulation(uint tick)
        {
            _events.BeginResimulation(tick);
            _hits.BeginResimulation(tick);
            _slopball.BeginResimulation(tick);
            _courses.BeginResimulation(tick);
            _mayhem.BeginResimulation(tick);
        }

        private void EndResimulation()
        {
            _events.EndResimulation();
            _hits.EndResimulation();
            _slopball.EndResimulation();
            _courses.EndResimulation();
            _mayhem.EndResimulation();
        }

        private void ValidateRecords(WorldFramePacket frame)
        {
            if (frame.MatchState > MatchState.Ended)
                throw SnapshotReader.Malformed("match state");
            if (frame.Events.Count > WorldFramePacket.MaxPresentationEvents || frame.Hits.Count > WorldFramePacket.MaxHits ||
                frame.SlopballEvents.Count > WorldFramePacket.MaxSlopballEvents || frame.CourseEvents.Count > WorldFramePacket.MaxCourseEvents || frame.MayhemEvents.Count > WorldFramePacket.MaxMayhemEvents)
                throw SnapshotReader.Malformed("frame record count");
            ulong previous = 0;
            bool first = true;
            foreach (var input in frame.Inputs)
            {
                if (!_fighters.Contains(input.EntityId) || !first && input.EntityId <= previous)
                    throw SnapshotReader.Malformed("relayed input roster/order");
                if (input.HasInput && input.Input.LockMode > TargetLockMode.OnHit)
                    throw SnapshotReader.Malformed("relayed input lock mode");
                first = false;
                previous = input.EntityId;
            }
            foreach (var evt in frame.Events)
                if (!InEventRange(evt.MatchTick, frame) || !ValidPresentationOwner(evt.EntityId) || !WorldFramePacket.CanCarry(evt))
                    throw SnapshotReader.Malformed("presentation event");
            foreach (var hit in frame.Hits)
                if (!InEventRange(hit.MatchTick, frame) || !ValidPresentationOwner(hit.OwnerEntityId) ||
                    hit.KnockbackDirection > AuthoringKnockbackDirection.TowardOwner ||
                    !float.IsFinite(hit.Damage) || !float.IsFinite(hit.DirX) || !float.IsFinite(hit.DirZ) ||
                    !float.IsFinite(hit.BaseKnockback) || !float.IsFinite(hit.KnockbackGrowth) ||
                    !float.IsFinite(hit.HitX) || !float.IsFinite(hit.HitY) || !float.IsFinite(hit.HitZ) ||
                    !float.IsFinite(hit.ImpactForce) || !float.IsFinite(hit.ContactTime) || hit.ContactTime is < 0f or > 1f)
                    throw SnapshotReader.Malformed("hit record");
            if (frame.SlopballEvents.Count > 0 && !_sim.Slopball.HasValue && !_sim.SlopballVersus.HasValue)
                throw SnapshotReader.Malformed("Slopball event mode");
            foreach (var item in frame.SlopballEvents)
                if (!InEventRange(item.MatchTick, frame) || item.Event.Kind > SlopballEventKind.PointScored ||
                    item.Event.Camp > SlopballCamp.Clay || !float.IsFinite(item.Event.ContactTime) ||
                    item.Event.ContactTime is < 0f or > 1f ||
                    item.Event.Kind is SlopballEventKind.ServeReady or SlopballEventKind.PointScored && item.Event.Camp == SlopballCamp.None)
                    throw SnapshotReader.Malformed("Slopball event");
            if (frame.CourseEvents.Count > 0 && !_sim.OnlineCourse.HasValue)
                throw SnapshotReader.Malformed("course event mode");
            if (frame.MayhemEvents.Count > 0 && !_sim.MankiMayhem.HasValue)
                throw SnapshotReader.Malformed("Mayhem event mode");
            foreach (var item in frame.MayhemEvents)
                if (!WorldFramePacket.ValidMayhemEvent(item, frame.Tick)
                    || item.EntityId != ServerSimulation.MayhemHostEntityId && !RuntimeSolidSet.IsWallId(item.EntityId) && !_fighters.Contains(item.EntityId))
                    throw SnapshotReader.Malformed("Mayhem event");
            foreach (var item in frame.CourseEvents)
            {
                bool target = item.Event.Kind is CharacterCourseEventKind.TargetHit or CharacterCourseEventKind.TargetBroken;
                bool validIndex = target ? item.Event.Index is >= 0 and < CharacterCourseDefinition.MaxTargets
                    : item.Event.Kind is CharacterCourseEventKind.Go or CharacterCourseEventKind.Completed
                        ? item.Event.Index == -1 : item.Event.Index is >= -1 and < 32;
                if (!InEventRange(item.MatchTick, frame) || item.Event.Kind > CharacterCourseEventKind.TargetHit ||
                    item.EntityId >= CharacterCourseSimulation.FirstInputAlias ||
                    !(item.EntityId == 0 && target || _fighters.Contains(item.EntityId)) ||
                    item.Sequence >= WorldFramePacket.MaxCourseEvents || !validIndex)
                    throw SnapshotReader.Malformed("course event");
            }
        }

        private bool ValidPresentationOwner(ulong id) => _fighters.Contains(id)
            || _sim.MankiMayhem.HasValue && id == ServerSimulation.MayhemHostEntityId;

        private static bool InEventRange(uint tick, WorldFramePacket frame)
            => tick >= frame.EventsFromTick && tick <= frame.Tick;

        private void SetAnchor(uint tick)
        {
            _anchorTick = tick;
            _hasAnchor = true;
            _started = true;
            _window.Anchors++;
        }

        private void SimulateTick(uint tick)
        {
            _tickInputs.Clear();
            foreach (ulong id in _fighters)
            {
                // As the GameHost: eliminated and disconnected fighters consume no input.
                if (_sim.IsEntityInactive(id))
                    continue;
                if (id == _selfId)
                    _tickInputs[id] = TryGetRecordedInput(tick, out var own) ? own : default;
                else if (_assumed.TryGetValue(id, out var assumed))
                    _tickInputs[id] = assumed;
            }
            _sim.Tick(_tickInputs);
            _events.RecordPredicted(_sim.GetPresentationEvents(clear: true));
            _hits.RecordPredicted(_sim.LastTickHits);
            if (_sim.Slopball.HasValue || _sim.SlopballVersus.HasValue)
            {
                _tickSlopball.Clear();
                foreach (var item in _sim.LastTickSlopballEvents)
                    _tickSlopball.Add(new SlopballTickEvent(_sim.SimulationTick, item));
                _slopball.RecordPredicted(_tickSlopball);
            }
            if (_sim.OnlineCourse.HasValue) _courses.RecordPredicted(_sim.LastTickOnlineCourseEvents);
            if (_sim.MankiMayhem.HasValue) _mayhem.RecordPredicted(_sim.LastTickMayhemEvents);
            StorePrediction(tick, _sim.CaptureSnapshot());
        }

        private void RecordAuthoritative(WorldFramePacket frame)
        {
            for (uint tick = frame.EventsFromTick; ; tick++)
            {
                _tickEvents.Clear();
                foreach (var evt in frame.Events)
                    if (evt.MatchTick == tick)
                        _tickEvents.Add(evt);
                _events.RecordAuthoritative(tick, _tickEvents);
                if (_sim.MankiMayhem.HasValue)
                {
                    _tickMayhem.Clear();
                    foreach (var item in frame.MayhemEvents)
                        if (item.MatchTick == tick) _tickMayhem.Add(item);
                    _mayhem.RecordAuthoritative(tick, _tickMayhem);
                }
                if (_sim.OnlineCourse.HasValue)
                {
                    _tickCourses.Clear();
                    foreach (var item in frame.CourseEvents)
                        if (item.MatchTick == tick) _tickCourses.Add(item);
                    _courses.RecordAuthoritative(tick, _tickCourses);
                }
                _tickHits.Clear();
                foreach (var hit in frame.Hits)
                    if (hit.MatchTick == tick)
                        _tickHits.Add(hit);
                _hits.RecordAuthoritative(tick, _tickHits);
                if (_sim.Slopball.HasValue || _sim.SlopballVersus.HasValue)
                {
                    _tickSlopball.Clear();
                    foreach (var item in frame.SlopballEvents)
                        if (item.MatchTick == tick)
                            _tickSlopball.Add(item);
                    _slopball.RecordAuthoritative(tick, _tickSlopball);
                }
                if (tick == frame.Tick) break;
            }
        }

        /// <summary>Opponents repeat what the GameHost relayed at the anchor tick, without presses.</summary>
        private void AssumeFrom(WorldFramePacket frame, Dictionary<ulong, InputState> assumed)
        {
            assumed.Clear();
            foreach (ulong id in _fighters)
                if (id != _selfId)
                    assumed[id] = default;
            foreach (var relayed in frame.Inputs)
            {
                if (relayed.EntityId == _selfId || !assumed.ContainsKey(relayed.EntityId))
                    continue;
                if (relayed.HasInput)
                    assumed[relayed.EntityId] = relayed.Input.WithoutEdges();
                else
                    assumed.Remove(relayed.EntityId); // disconnected or eliminated: no input
            }
        }

        private void CheckSelfInput(WorldFramePacket frame)
        {
            if (!TryGetRecordedInput(frame.Tick, out var own))
                return;
            foreach (var relayed in frame.Inputs)
                if (relayed.EntityId == _selfId && (!relayed.HasInput || !SameInput(relayed.Input, own)))
                    _window.SelfInputMismatches++;
        }

        private void SwapAssumed() => (_assumed, _nextAssumed) = (_nextAssumed, _assumed);

        private void CountCorrection(Prediction? prediction)
        {
            _window.Corrections++;
            if (prediction == null)
            {
                _window.Mispredictions++;
                return;
            }
            bool mispredicted = false;
            for (int i = 0; i < _fighters.Count; i++)
            {
                var before = prediction.States[i];
                var after = _sim.GetState(_fighters[i]);
                float distance = Distance(before, after);
                if (_fighters[i] == _selfId)
                    _window.MaxSelfCorrectionMeters = Math.Max(_window.MaxSelfCorrectionMeters, distance);
                else
                    _window.MaxOpponentCorrectionMeters = Math.Max(_window.MaxOpponentCorrectionMeters, distance);
                if (distance >= MispredictionMeters || before.State != after.State ||
                    before.DamagePercent != after.DamagePercent || before.Deaths != after.Deaths ||
                    before.HitReactionSequence != after.HitReactionSequence)
                    mispredicted = true;
            }
            if (mispredicted)
                _window.Mispredictions++;
        }

        private void CapturePresented()
        {
            for (int i = 0; i < _fighters.Count; i++)
                _presented[i] = _sim.GetState(_fighters[i]);
        }

        /// <summary>How far the re-simulation moved what the player sees at the local tick.</summary>
        private void MeasureSnap()
        {
            for (int i = 0; i < _fighters.Count; i++)
            {
                float distance = Distance(_presented[i], _sim.GetState(_fighters[i]));
                if (_fighters[i] == _selfId)
                    _window.MaxSelfSnapMeters = Math.Max(_window.MaxSelfSnapMeters, distance);
                else
                    _window.MaxOpponentSnapMeters = Math.Max(_window.MaxOpponentSnapMeters, distance);
            }
        }

        private void StorePrediction(uint tick, WorldSnapshot snapshot)
        {
            var prediction = _predictions[Slot(tick)];
            prediction.Tick = tick;
            prediction.Snapshot = snapshot;
            for (int i = 0; i < _fighters.Count; i++)
                prediction.States[i] = _sim.GetState(_fighters[i]);
        }

        private bool TryGetPrediction(uint tick, out Prediction? prediction)
        {
            prediction = _predictions[Slot(tick)];
            if (prediction.Snapshot != null && prediction.Tick == tick)
                return true;
            prediction = null;
            return false;
        }

        private static int Slot(uint tick) => (int)(tick % HistoryCapacity);

        private static float Distance(in CharacterState a, in CharacterState b)
        {
            float dx = a.PX - b.PX, dy = a.PY - b.PY, dz = a.PZ - b.PZ;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The input as the GameHost decodes it from the wire.</summary>
        private static InputState AsConsumed(InputState input)
        {
            Span<byte> wire = stackalloc byte[InputState.Size];
            input.Write(wire);
            return InputState.Deserialize(wire);
        }

        private static bool SameInput(InputState a, InputState b)
        {
            Span<byte> x = stackalloc byte[InputState.Size];
            Span<byte> y = stackalloc byte[InputState.Size];
            a.Write(x);
            b.Write(y);
            return x.SequenceEqual(y);
        }

        private static bool SameInputs(Dictionary<ulong, InputState> a, Dictionary<ulong, InputState> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var pair in a)
                if (!b.TryGetValue(pair.Key, out var other) || !SameInput(pair.Value, other))
                    return false;
            return true;
        }

        private sealed class Prediction
        {
            public uint Tick;
            public WorldSnapshot? Snapshot;
            public CharacterState[] States = Array.Empty<CharacterState>();

            public void Resize(int fighters)
            {
                States = new CharacterState[fighters];
                Snapshot = null;
            }
        }
    }

    /// <summary>Whether a drain supplied accepted history and reset the local timeline.</summary>
    public readonly record struct FrameIngestResult(bool AcceptedAny, bool TimelineReset);

    /// <summary>One window of <see cref="NetplayPredictor"/> health (for the periodic netplay log).</summary>
    public struct NetplayPredictorStats
    {
        /// <summary>Frames received, frames no newer than the anchor, and frames whose snapshot or records were rejected.</summary>
        public int Frames, StaleFrames, RejectedFrames;
        /// <summary>Frames that became the anchor; of those, predictions that matched byte for byte.</summary>
        public int Anchors, ExactAnchors;
        /// <summary>Anchors whose prediction differed; of those, by at least
        /// <see cref="NetplayPredictor.MispredictionMeters"/> or a state, damage, KO or reaction change.</summary>
        public int Corrections, Mispredictions;
        /// <summary>Anchors past the local tick (the client was behind the GameHost).</summary>
        public int Jumps;
        /// <summary>Ticks not predicted because the Prediction Window was full.</summary>
        public int StarvedTicks;
        /// <summary>Anchor ticks where the GameHost consumed a different input than the player's.</summary>
        public int SelfInputMismatches;
        /// <summary>Presentation events and hits handed out to start and to stop (as of the last drain).</summary>
        public int EffectsStarted, EffectsRetracted, HitsStarted, HitsRetracted;
        /// <summary>Prediction Window at the end of the window, and the oldest anchor seen.</summary>
        public uint PredictionTicks, MaxAnchorAgeTicks;
        public int ResimulatedTicks;
        public uint MaxResimulatedTicks;
        public double TotalResimulationMs, MaxResimulationMs;
        /// <summary>Largest difference between predicted and anchored positions at the anchor tick.</summary>
        public float MaxSelfCorrectionMeters, MaxOpponentCorrectionMeters;
        /// <summary>Largest jump a re-simulation made in what the player sees.</summary>
        public float MaxSelfSnapMeters, MaxOpponentSnapMeters;
    }
}
