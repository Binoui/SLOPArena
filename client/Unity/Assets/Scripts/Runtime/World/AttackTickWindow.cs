using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Plays an inactive child of a cue on its owner's attack clock: <see cref="Content"/> turns on
    /// <see cref="StartTick"/> attack ticks after the cue spawned. Hitstop pauses that clock as it
    /// pauses the fighter's clip, so a moment stays on its pose. If the attack ends before
    /// <see cref="CancelableUntilTick"/> (interruption, a new move), Content is cleared; after that
    /// tick it plays out. For moments inside capability cues, which are not attack-bound: Bonk's
    /// landing plays its holy light while he is stuck and the POP when the plank comes out.
    /// Cosmetic only.
    /// </summary>
    public sealed class AttackTickWindow : MonoBehaviour, IPresentationOwnerReceiver
    {
        [Tooltip("Attack ticks after the cue spawned before Content turns on.")]
        public int StartTick;
        [Tooltip("Until this many attack ticks after the spawn, the attack ending clears Content.")]
        public int CancelableUntilTick;
        [Tooltip("Inactive child; its particles and sounds start when it turns on.")]
        public GameObject? Content;

        private PlayerRenderer? _owner;
        private byte _sequence;
        private int _spawnTick, _elapsed;
        private bool _started, _done;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            if (owner == null || !PresentationLifetime.IsCurrentAttack(owner.CurrentActionState,
                    owner.CurrentAttackSequence, owner.CurrentAttackSequence))
            {
                Finish(clear: true);
                return;
            }
            _sequence = owner.CurrentAttackSequence;
            _spawnTick = owner.CurrentAttackElapsedTicks;
            Step();
        }

        private void Update() => Step();

        private void Step()
        {
            if (_done || _owner == null) return;
            bool current = _owner.isActiveAndEnabled
                && PresentationLifetime.IsCurrentAttack(_owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence);
            if (!current)
            {
                Finish(clear: _elapsed < CancelableUntilTick);
                return;
            }
            _elapsed = Mathf.Max(_elapsed, _owner.CurrentAttackElapsedTicks - _spawnTick);
            if (!_started && _elapsed >= StartTick)
            {
                _started = true;
                if (Content != null) Content.SetActive(true);
            }
            if (_started && _elapsed >= CancelableUntilTick) _done = true;
        }

        private void Finish(bool clear)
        {
            _done = true;
            if (clear && Content != null) Content.SetActive(false);
        }
    }
}
