using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Which one-shot outputs of a rolled-back simulation start and which stop (ADR-0039):
    /// presentation events and hits. Each key is handed out once, whether prediction or the
    /// GameHost produced it first. A predicted item is retracted when a re-simulation no longer
    /// produces it or when the GameHost's record of its tick does not contain it; a GameHost item
    /// the client never predicted starts late. A predicted item whose tick's record never arrives
    /// stays as predicted.
    /// </summary>
    public sealed class RollbackLedger<TKey, TItem> where TKey : struct, IEquatable<TKey>
    {
        /// <summary>Ticks of history kept for deduplication behind the newest authoritative tick.</summary>
        public const uint HorizonTicks = 180;

        private readonly Func<TItem, TKey> _keyOf;
        private readonly Func<TKey, uint> _tickOf;
        private readonly Dictionary<TKey, TItem> _started = new();
        private readonly HashSet<TKey> _predictedOnly = new();
        private readonly Dictionary<uint, HashSet<TKey>> _authoritative = new();
        private readonly Stack<HashSet<TKey>> _recordPool = new();
        private readonly HashSet<TKey> _pendingResimulation = new();
        private readonly List<TItem> _newlyStarted = new();
        private readonly List<TItem> _newlyRetracted = new();
        private readonly List<TKey> _scratchKeys = new();
        private readonly List<uint> _scratchTicks = new();
        private bool _resimulating;
        private uint _newestAuthoritativeTick;

        public RollbackLedger(Func<TItem, TKey> keyOf, Func<TKey, uint> tickOf)
        {
            _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));
            _tickOf = tickOf ?? throw new ArgumentNullException(nameof(tickOf));
        }

        /// <summary>Items handed out to start, and to stop, since the ledger was created.</summary>
        public int StartedTotal { get; private set; }
        public int RetractedTotal { get; private set; }

        /// <summary>The GameHost's complete record for <paramref name="tick"/>.</summary>
        public void RecordAuthoritative(uint tick, IReadOnlyList<TItem> items)
        {
            if (_newestAuthoritativeTick >= HorizonTicks && tick < _newestAuthoritativeTick - HorizonTicks)
                return; // older than the deduplication history: handing it out now could repeat it
            if (!_authoritative.TryGetValue(tick, out var record))
            {
                record = _recordPool.Count > 0 ? _recordPool.Pop() : new HashSet<TKey>();
                _authoritative[tick] = record;
            }
            record.Clear();
            foreach (var item in items)
            {
                var key = _keyOf(item);
                record.Add(key);
                if (_started.ContainsKey(key))
                    _predictedOnly.Remove(key);
                else
                    Start(key, item, predicted: false);
            }
            if (tick > _newestAuthoritativeTick)
                _newestAuthoritativeTick = tick;

            _scratchKeys.Clear();
            foreach (var key in _predictedOnly)
                if (_tickOf(key) == tick && !record.Contains(key))
                    _scratchKeys.Add(key);
            foreach (var key in _scratchKeys)
                Retract(key);
            Prune();
        }

        /// <summary>The prediction after <paramref name="afterTick"/> is about to be replaced.</summary>
        public void BeginResimulation(uint afterTick)
        {
            _pendingResimulation.Clear();
            foreach (var key in _predictedOnly)
                if (_tickOf(key) > afterTick)
                    _pendingResimulation.Add(key);
            _resimulating = true;
        }

        /// <summary>Items a predicted tick produced (ordinary or re-simulated).</summary>
        public void RecordPredicted(IReadOnlyList<TItem> items)
        {
            foreach (var item in items)
            {
                var key = _keyOf(item);
                if (_authoritative.TryGetValue(_tickOf(key), out var record) && !record.Contains(key))
                    continue; // the GameHost's record of that tick already rules it out
                if (_started.ContainsKey(key))
                {
                    _pendingResimulation.Remove(key);
                    continue;
                }
                Start(key, item, predicted: record == null);
            }
        }

        /// <summary>Retract the replaced prediction's items the re-simulation did not produce again.</summary>
        public void EndResimulation()
        {
            if (!_resimulating) return;
            _resimulating = false;
            _scratchKeys.Clear();
            _scratchKeys.AddRange(_pendingResimulation);
            _pendingResimulation.Clear();
            foreach (var key in _scratchKeys)
                Retract(key);
        }

        /// <summary>Items to start and to stop since the previous call. An item started and
        /// retracted in between appears in neither; one retracted and predicted again stays.</summary>
        public void Drain(List<TItem> started, List<TItem> retracted)
        {
            StartedTotal += _newlyStarted.Count;
            RetractedTotal += _newlyRetracted.Count;
            started.AddRange(_newlyStarted);
            retracted.AddRange(_newlyRetracted);
            _newlyStarted.Clear();
            _newlyRetracted.Clear();
        }

        private void Start(TKey key, TItem item, bool predicted)
        {
            _started[key] = item;
            if (predicted)
                _predictedOnly.Add(key);
            int retracted = IndexOf(_newlyRetracted, key);
            if (retracted >= 0)
                _newlyRetracted.RemoveAt(retracted); // still showing: neither stop nor restart it
            else
                _newlyStarted.Add(item);
        }

        private void Retract(TKey key)
        {
            if (!_started.TryGetValue(key, out var item))
                return;
            _started.Remove(key);
            _predictedOnly.Remove(key);
            int pending = IndexOf(_newlyStarted, key);
            if (pending >= 0)
                _newlyStarted.RemoveAt(pending); // never handed out: nothing to stop
            else
                _newlyRetracted.Add(item);
        }

        private int IndexOf(List<TItem> items, TKey key)
        {
            for (int i = 0; i < items.Count; i++)
                if (_keyOf(items[i]).Equals(key))
                    return i;
            return -1;
        }

        private void Prune()
        {
            if (_newestAuthoritativeTick < HorizonTicks)
                return;
            uint oldest = _newestAuthoritativeTick - HorizonTicks;
            _scratchKeys.Clear();
            foreach (var key in _started.Keys)
                if (_tickOf(key) < oldest)
                    _scratchKeys.Add(key);
            foreach (var key in _scratchKeys)
            {
                _started.Remove(key);
                _predictedOnly.Remove(key);
            }
            _scratchTicks.Clear();
            foreach (uint tick in _authoritative.Keys)
                if (tick < oldest)
                    _scratchTicks.Add(tick);
            foreach (uint tick in _scratchTicks)
            {
                _recordPool.Push(_authoritative[tick]);
                _authoritative.Remove(tick);
            }
        }
    }

    /// <summary>Identity of one hit across prediction, re-simulation and the GameHost's record.</summary>
    public readonly record struct HitKey(uint MatchTick, ulong OwnerEntityId, ulong TargetEntityId,
        byte AttackSequence, byte AttackSlot, bool Blocked)
    {
        public static HitKey Of(SpellResolver.HitResult hit)
            => new(hit.MatchTick, hit.OwnerEntityId, hit.TargetEntityId, hit.AttackSequence, hit.AttackSlot, hit.Blocked);
    }
}
