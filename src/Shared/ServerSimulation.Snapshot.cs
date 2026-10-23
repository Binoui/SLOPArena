using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    public partial class ServerSimulation
    {
        /// <summary>World Snapshot encoding version; bump with any layout change.</summary>
        // 3: hitboxes carry returning projectiles (Sir Bonk's pot lid).
        // 4: Versus Slopball rally, score and serve rotation.
        // 5: solo and multiplayer Character Course progress, clocks, finish order and withdrawals.
        public const uint SnapshotFormatVersion = 6;

        private readonly List<ulong> _abilityOrder = new();

        /// <summary>The tick of the current state: the last simulated tick, or the tick last set.</summary>
        public uint CurrentTick => _tick;

        /// <summary>
        /// Capture the complete state after the last completed tick (ADR-0039). Restoring it into
        /// a simulation with the same arena, rule and registered fighters and simulating the same
        /// inputs reproduces the same snapshots on the same runtime. Per-tick scratch and
        /// presentation outputs are not state and are not captured.
        /// </summary>
        public WorldSnapshot CaptureSnapshot()
        {
            var w = new SnapshotWriter(2048);
            w.WriteUInt(SnapshotFormatVersion);
            w.WriteUInt(_tick);
            w.WriteULong(_nextActivationId);
            w.WriteULong(_nextInteractionId);
            var ids = SortedEntityIds();
            w.WriteInt(ids.Count);
            foreach (ulong id in ids)
            {
                w.WriteULong(id);
                SnapshotCodecs.Write(w, _states[id]);
                w.WriteBool(_kos.TryGetValue(id, out byte kos));
                if (_kos.ContainsKey(id)) w.WriteByte(kos);
                WriteHitRecord(w, _lastHitCredits, id);
                WriteHitRecord(w, _lastHitContexts, id);
                w.WriteBool(_lastActivationIds.TryGetValue(id, out ulong lastActivation));
                if (_lastActivationIds.ContainsKey(id)) w.WriteULong(lastActivation);
                w.WriteBool(_animFrames.TryGetValue(id, out int frame));
                if (_animFrames.ContainsKey(id)) w.WriteInt(frame);
                w.WriteBool(_prevAnimIndex.TryGetValue(id, out int previousAnim));
                if (_prevAnimIndex.ContainsKey(id)) w.WriteInt(previousAnim);
                w.WriteBool(_respawnPositions.TryGetValue(id, out var respawn));
                if (_respawnPositions.ContainsKey(id))
                {
                    w.WriteFloat(respawn.x);
                    w.WriteFloat(respawn.y);
                    w.WriteFloat(respawn.z);
                    w.WriteFloat(respawn.yaw);
                }
                w.WriteBool(_activeAbilities.TryGetValue(id, out var ability));
                if (ability != null)
                {
                    if (ability is not CookedTimelineAbility cooked)
                        throw new NotSupportedException(
                            $"Entity {id} runs {ability.GetType().Name}; snapshots cover cooked timeline activations.");
                    w.WriteInt(cooked.SlotOrdinal);
                    cooked.WriteSnapshot(w);
                }
            }
            _spellResolver.WriteSnapshot(w, OwnerContent);
            // Arcade objects (ADR-0040): the ball, then the Slopball rules that own it.
            w.WriteBool(_arcadeBall != null);
            if (_arcadeBall != null)
            {
                _arcadeBall.Capture().Write(w);
                w.WriteBool(_slopball != null);
                _slopball?.Capture().Write(w);
                w.WriteBool(_volley != null);
                _volley?.Capture().Write(w);
                w.WriteBool(_arcadeInputReleaseRequired);
            }
            w.WriteBool(_course != null);
            _course?.WriteSnapshot(w);
            w.WriteBool(_onlineCourse != null);
            _onlineCourse?.WriteSnapshot(w);
            if (_course != null) w.WriteBool(_arcadeInputReleaseRequired);
            return new WorldSnapshot(w.ToArray());
        }

        /// <summary>
        /// Replace this simulation's state with <paramref name="snapshot"/>. The live ability
        /// runtimes are retired without callbacks. The snapshot is decoded completely before
        /// anything changes, so a malformed snapshot leaves the simulation untouched.
        /// </summary>
        public void RestoreSnapshot(WorldSnapshot snapshot)
            => ApplySnapshot(PrepareSnapshot(snapshot));

        internal PreparedSnapshot PrepareSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var r = new SnapshotReader(snapshot.Data);
            if (r.ReadUInt() != SnapshotFormatVersion)
                throw SnapshotReader.Malformed("format version");
            uint tick = r.ReadUInt();
            if (tick != snapshot.Tick)
                throw SnapshotReader.Malformed("snapshot tick");
            ulong nextActivationId = r.ReadULong();
            ulong nextInteractionId = r.ReadULong();
            var ids = SortedEntityIds();
            int count = r.ReadCount(8);
            if (count != ids.Count)
                throw new InvalidOperationException("World snapshot fighters differ from this simulation's fighters.");

            var fighters = new FighterSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                ulong id = r.ReadULong();
                if (id != ids[i])
                    throw new InvalidOperationException($"World snapshot fighter {id} is not registered here.");
                var fighter = new FighterSnapshot { Id = id, State = SnapshotCodecs.ReadCharacterState(r) };
                if (fighter.State.EntityId != id)
                    throw SnapshotReader.Malformed("fighter identity");
                if (r.ReadBool()) fighter.Kos = r.ReadByte();
                fighter.Credit = ReadHitRecord(r);
                fighter.Context = ReadHitRecord(r);
                if (r.ReadBool()) fighter.LastActivationId = r.ReadULong();
                if (r.ReadBool()) fighter.AnimFrame = r.ReadInt();
                if (r.ReadBool()) fighter.PreviousAnim = r.ReadInt();
                if (r.ReadBool())
                    fighter.Respawn = (r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
                if (r.ReadBool())
                {
                    int ordinal = r.ReadInt();
                    var def = _defs[id];
                    var slot = def.CookedSlots != null && ordinal >= 0 && ordinal < def.CookedSlots.Count
                        ? def.CookedSlots[ordinal] : null;
                    if (slot == null || slot.Ordinal != ordinal)
                        throw SnapshotReader.Malformed($"cooked slot {ordinal} of fighter {id}");
                    var ability = new CookedTimelineAbility(slot);
                    WireAbility(id, ability, def);
                    ability.ReadSnapshot(r);
                    fighter.Ability = ability;
                }
                fighters[i] = fighter;
            }
            var resolver = SpellResolver.ReadSnapshot(r, OwnerContent);
            bool hasBall = r.ReadBool();
            if (hasBall != (_arcadeBall != null))
                throw SnapshotReader.Malformed("Arcade ball presence");
            ArcadeBallSimulation.Snapshot ball = default;
            SlopballSimulation.Snapshot rules = default;
            SlopballVersusSimulation.Snapshot volley = default;
            bool inputRelease = false;
            if (hasBall)
            {
                ball = ArcadeBallSimulation.Snapshot.Read(r);
                bool hasRules = r.ReadBool();
                if (hasRules != (_slopball != null))
                    throw SnapshotReader.Malformed("Slopball presence");
                if (hasRules) rules = SlopballSimulation.Snapshot.Read(r);
                bool hasVolley = r.ReadBool();
                if (hasVolley != (_volley != null)) throw SnapshotReader.Malformed("Versus Slopball presence");
                if (hasVolley)
                {
                    volley = SlopballVersusSimulation.Snapshot.Read(r);
                    _volley!.Validate(volley);
                }
                inputRelease = r.ReadBool();
            }
            bool hasCourse = r.ReadBool();
            if (hasCourse != (_course != null)) throw SnapshotReader.Malformed("solo course presence");
            CharacterCourseSimulation.Snapshot? course = hasCourse ? _course!.ReadSnapshot(r) : null;
            bool hasOnlineCourse = r.ReadBool();
            if (hasOnlineCourse != (_onlineCourse != null)) throw SnapshotReader.Malformed("online course presence");
            var onlineCourse = hasOnlineCourse ? _onlineCourse!.ReadSnapshot(r) : null;
            if (hasCourse) inputRelease = r.ReadBool();
            if (!r.AtEnd)
                throw SnapshotReader.Malformed("trailing data");

            return new PreparedSnapshot(tick, nextActivationId, nextInteractionId, fighters,
                resolver, ball, rules, volley, inputRelease, course, onlineCourse);
        }

        internal void ApplySnapshot(PreparedSnapshot prepared)
        {
            _tick = prepared.Tick;
            _nextActivationId = prepared.NextActivationId;
            _nextInteractionId = prepared.NextInteractionId;
            _activeAbilities.Clear();
            foreach (var fighter in prepared.Fighters)
            {
                ulong id = fighter.Id;
                _states[id] = fighter.State;
                SetOrRemove(_kos, id, fighter.Kos);
                SetOrRemove(_lastHitCredits, id, fighter.Credit);
                SetOrRemove(_lastHitContexts, id, fighter.Context);
                SetOrRemove(_lastActivationIds, id, fighter.LastActivationId);
                SetOrRemove(_animFrames, id, fighter.AnimFrame);
                SetOrRemove(_prevAnimIndex, id, fighter.PreviousAnim);
                SetOrRemove(_respawnPositions, id, fighter.Respawn);
                if (fighter.Ability != null)
                    _activeAbilities[id] = fighter.Ability;
            }
            _spellResolver.ApplySnapshot(prepared.Resolver);
            if (prepared.Course.HasValue) _course!.Apply(prepared.Course.Value);
            if (prepared.OnlineCourse != null) _onlineCourse!.Apply(prepared.OnlineCourse);
            if (_course != null) _arcadeInputReleaseRequired = prepared.InputRelease;
            if (_arcadeBall != null)
            {
                _arcadeBall.Apply(prepared.Ball);
                _slopball?.Apply(prepared.Rules);
                _volley?.Apply(prepared.Volley);
                _arcadeInputReleaseRequired = prepared.InputRelease;
            }

            // Outputs of the tick that produced the replaced state.
            LastTickHits.Clear();
            LastTickDeaths.Clear();
            LastTickAttackEntities = Array.Empty<SpellResolver.EntityData>();
            _presentationEvents.Clear();
            _lastTickAcceptedActions.Clear();
            _lastTickTouchdowns.Clear();
            _lastTickOrdinaryActionOpportunities.Clear();
            _lastTickDownAdmissions.Clear();
        }

        /// <summary>Fighters in ascending entity order, the canonical order of every snapshot.</summary>
        private List<ulong> SortedEntityIds()
        {
            var ids = new List<ulong>(_states.Keys);
            ids.Sort();
            return ids;
        }

        /// <summary>Owners of active abilities in ascending entity order: every simulation of a
        /// match, continuous or restored, ticks abilities identically.</summary>
        private List<ulong> SortedAbilityOwners()
        {
            _abilityOrder.Clear();
            _abilityOrder.AddRange(_activeAbilities.Keys);
            _abilityOrder.Sort();
            return _abilityOrder;
        }

        /// <summary>
        /// Keep fighter enumeration in ascending entity order regardless of registration order,
        /// so a client that registers itself first iterates fighters like the GameHost.
        /// </summary>
        private void SortStatesByEntity()
        {
            bool sorted = true;
            bool first = true;
            ulong previous = 0;
            foreach (ulong id in _states.Keys)
            {
                if (!first && id < previous)
                {
                    sorted = false;
                    break;
                }
                previous = id;
                first = false;
            }
            if (sorted)
                return;
            var entries = new List<KeyValuePair<ulong, CharacterState>>(_states);
            entries.Sort(static (a, b) => a.Key.CompareTo(b.Key));
            _states.Clear();
            foreach (var entry in entries)
                _states.Add(entry.Key, entry.Value);
        }

        private void WireAbility(ulong id, ServerAbility ability, CharacterDefinition def)
        {
            ability.Resolver = _spellResolver;
            ability.OwnerSimulation = this;
            ability.SimulationStates = _states;
            ability.BakedData = _bakedData.TryGetValue(id, out var baked) ? baked : null;
            ability.CharacterDef = def;
            ability.Arena = _arena;
            ability.PresentationSink = e => _presentationEvents.Add(e with { MatchTick = _tick });
        }

        private (CharacterDefinition? Def, BakedAnimationData? Baked) OwnerContent(ulong owner)
            => (_defs.TryGetValue(owner, out var def) ? def : null,
                _bakedData.TryGetValue(owner, out var baked) ? baked : null);

        private static void WriteHitRecord(SnapshotWriter w,
            Dictionary<ulong, (ulong attackerId, uint tick, byte slot)> records, ulong id)
        {
            bool present = records.TryGetValue(id, out var record);
            w.WriteBool(present);
            if (!present) return;
            w.WriteULong(record.attackerId);
            w.WriteUInt(record.tick);
            w.WriteByte(record.slot);
        }

        private static (ulong attackerId, uint tick, byte slot)? ReadHitRecord(SnapshotReader r)
            => r.ReadBool() ? (r.ReadULong(), r.ReadUInt(), r.ReadByte()) : null;

        private static void SetOrRemove<T>(Dictionary<ulong, T> map, ulong id, T? value) where T : struct
        {
            if (value.HasValue) map[id] = value.Value;
            else map.Remove(id);
        }

        internal sealed record PreparedSnapshot(uint Tick, ulong NextActivationId, ulong NextInteractionId,
            FighterSnapshot[] Fighters, SpellResolver.SnapshotData Resolver,
            ArcadeBallSimulation.Snapshot Ball, SlopballSimulation.Snapshot Rules,
            SlopballVersusSimulation.Snapshot Volley, bool InputRelease,
            CharacterCourseSimulation.Snapshot? Course, OnlineCourseSimulation.Snapshot? OnlineCourse);

        internal sealed class FighterSnapshot
        {
            public ulong Id;
            public CharacterState State;
            public byte? Kos;
            public (ulong attackerId, uint tick, byte slot)? Credit;
            public (ulong attackerId, uint tick, byte slot)? Context;
            public ulong? LastActivationId;
            public int? AnimFrame;
            public int? PreviousAnim;
            public (float x, float y, float z, float yaw)? Respawn;
            public CookedTimelineAbility? Ability;
        }
    }
}
