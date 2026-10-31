using System;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Nyx's generated particles and weapon trails on the attack clock, in simulation metres.</summary>
    public sealed class NyxAttackCue : MonoBehaviour, IPresentationOwnerReceiver
    {
        /// <summary>
        /// Content shown from <see cref="StartAfter"/> to <see cref="EndAfter"/> attack ticks after the
        /// cue spawned. A beat with a <see cref="Spawn"/> tick belongs to the emit nearest that tick, so
        /// one cue emitted at several ticks (Phone Slaps) shows a different beat for each.
        /// </summary>
        [Serializable]
        public sealed class Beat
        {
            public GameObject Content;
            public int StartAfter, EndAfter;
            public int Spawn = -1;
        }

        /// <summary>
        /// A weapon trail on the attack clock: the blade's hilt and tip, Steps samples per attack tick
        /// from StartAfter ticks after the cue's spawn, in this cue's frame. The first Lead ticks of
        /// samples come from before StartAfter and appear at once with their own times, so a hit on
        /// the first active tick, frozen by Hitstop, already shows the swing that brought the weapon
        /// there. The builder measures them from the move's clip and the hitbox it covers.
        /// </summary>
        [Serializable]
        public sealed class SweepTrail
        {
            public GameObject Style;
            public int StartAfter, Lead;
            public Vector3[] Hilts = Array.Empty<Vector3>(), Tips = Array.Empty<Vector3>();
            public int Steps = 1;
            public float Seconds = 0.15f;
        }

        public Beat[] Beats = Array.Empty<Beat>();
        public SweepTrail[] Trails = Array.Empty<SweepTrail>();
        public string OriginBoneId = "";
        private SwordSweepTrail[] _trails = Array.Empty<SwordSweepTrail>();
        private int[] _trailNext = Array.Empty<int>();
        private PlayerRenderer _owner;
        private byte _sequence;
        private int _spawnTick, _variant = -1, _lastAge = -1;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            if (owner == null) return;
            _sequence = owner.CurrentAttackSequence;
            _spawnTick = owner.CurrentAttackElapsedTicks;
            _variant = -1;
            foreach (var beat in Beats)
                if (beat.Spawn >= 0 && (_variant < 0 || Mathf.Abs(beat.Spawn - _spawnTick) < Mathf.Abs(_variant - _spawnTick)))
                    _variant = beat.Spawn;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            foreach (var beat in Beats)
            {
                beat.Content.SetActive(false);
                foreach (var system in beat.Content.GetComponentsInChildren<ParticleSystem>(true))
                {
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = system.main;
                    main.simulationSpeed = 0f;
                }
            }
            DisposeTrails();
            _trails = new SwordSweepTrail[Trails.Length];
            _trailNext = new int[Trails.Length];
            for (int i = 0; i < Trails.Length; i++)
            {
                if (Trails[i].Style == null || Trails[i].Tips.Length == 0) continue;
                _trails[i] = new SwordSweepTrail(owner.transform, Trails[i].Style, Trails[i].Seconds, 1f);
                _trails[i].SetActive(true);
            }
            Apply();
        }

        private void LateUpdate() => Apply();

        private void OnDestroy() => DisposeTrails();

        private void DisposeTrails()
        {
            foreach (var trail in _trails) trail?.Dispose();
            _trails = Array.Empty<SwordSweepTrail>();
        }

        private void Apply()
        {
            bool current = _owner != null && _owner.isActiveAndEnabled
                && PresentationLifetime.IsCurrentAttack(_owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence);
            int tick = current ? _owner.CurrentAttackElapsedTicks : -1;
            if (current)
            {
                var bone = string.IsNullOrEmpty(OriginBoneId) ? null : _owner.ResolvePresentationBone(OriginBoneId);
                transform.SetPositionAndRotation(bone != null ? bone.position : _owner.transform.position - Vector3.up * _owner.ModelYOffset,
                    Quaternion.Euler(0f, _owner.transform.eulerAngles.y, 0f));
            }
            int age = tick - _spawnTick;
            for (int i = 0; current && i < _trails.Length; i++)
            {
                var trail = _trails[i];
                if (trail == null) continue;
                // Attack ticks are the trail's clock: Hitstop freezes it and a rollback rewind clears it.
                var sweep = Trails[i];
                int steps = Mathf.Max(1, sweep.Steps);
                if (age < _lastAge) _trailNext[i] = 0;
                for (; _trailNext[i] < sweep.Tips.Length && _trailNext[i] <= (age - sweep.StartAfter + sweep.Lead) * steps; _trailNext[i]++)
                    trail.SampleAt((_spawnTick + sweep.StartAfter - sweep.Lead + _trailNext[i] / (float)steps) / 60f,
                        transform.TransformPoint(sweep.Hilts[_trailNext[i]]), transform.TransformPoint(sweep.Tips[_trailNext[i]]));
                trail.DrawAt(tick / 60f);
            }
            foreach (var beat in Beats)
            {
                bool show = current && age >= beat.StartAfter && age < beat.EndAfter && (beat.Spawn < 0 || beat.Spawn == _variant);
                beat.Content.SetActive(show);
                if (!show || age == _lastAge) continue;
                foreach (var system in beat.Content.GetComponentsInChildren<ParticleSystem>())
                {
                    // Rewind on rollback. Forward steps retain the world-space wake and ribbon history.
                    var main = system.main;
                    main.simulationSpeed = 1f;
                    bool restart = _lastAge < beat.StartAfter || age < _lastAge;
                    float step = (restart ? age - beat.StartAfter + 1 : age - _lastAge) / 60f;
                    system.Simulate(step, false, restart, false);
                    main.simulationSpeed = 0f;
                    system.Pause(false);
                }
            }
            _lastAge = age;
        }
    }
}
