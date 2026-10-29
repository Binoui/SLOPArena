using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    [DefaultExecutionOrder(700)]
    public sealed class WhistlePuff : MonoBehaviour, IPresentationOwnerReceiver
    {
        public enum Phase { Idle, Inhaling, Blown, Deflating, Done }

        // The accepted puff was timed on a whistle blown at tick 48; a move that blows sooner plays it faster.
        private const float AcceptedBlowTick = 48f;

        public AudioSource? Inhale;
        public AudioSource? Deflate;
        [Tooltip("The move's blow cue: its tick is where the puff empties into the whistle.")]
        public string BlowPresentationId = "presentation.bashie.whistle.blow";
        public Phase State { get; private set; }

        private PlayerRenderer? _owner;
        private WhistlePuffRig? _rig;
        private byte _sequence;
        private float _puff, _whistle, _deflateFrom, _whistleFrom, _deflateTime, _pace = 1f;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            _sequence = owner.CurrentAttackSequence;
            _rig = WhistlePuffRig.For(owner);
            _pace = BlowTick(owner.CurrentMove) / AcceptedBlowTick;
            State = _rig != null && CurrentAttack() ? Phase.Inhaling : Phase.Done;
        }

        private float BlowTick(SlopArena.Shared.CookedSlotDefinition? move)
        {
            if (move != null)
                foreach (var stage in move.Timeline.Stages)
                    foreach (var operation in stage.Operations)
                        if (operation is SlopArena.Shared.CookedEmitPresentationOperation emit
                            && emit.PresentationId == BlowPresentationId && emit.Tick > 0)
                            return emit.Tick;
            return AcceptedBlowTick;
        }

        private void LateUpdate()
        {
            if (State is Phase.Idle or Phase.Done || _owner == null || _rig == null) return;
            if (!_owner.isActiveAndEnabled || _owner.CurrentAttackSequence != _sequence)
            {
                Finish();
                return;
            }
            if (!CurrentAttack() && State != Phase.Deflating)
            {
                if (State != Phase.Inhaling) { Finish(); return; }
                State = Phase.Deflating;
                _deflateFrom = _puff;
                _whistleFrom = _whistle;
                if (Inhale != null) Inhale.Stop();
                if (Deflate != null) Deflate.Play();
            }

            if (State == Phase.Deflating)
            {
                _deflateTime += Time.deltaTime;
                float remaining = 1f - Mathf.SmoothStep(0f, 1f, _deflateTime / 0.3f);
                _rig.Request(_deflateFrom * remaining, _whistleFrom * remaining);
                if (_deflateTime >= 0.3f) Finish();
                return;
            }

            float tick = _owner.CurrentAttackElapsedTicks / _pace;
            if (tick >= AcceptedBlowTick && State == Phase.Inhaling)
            {
                State = Phase.Blown;
                if (Inhale != null) Inhale.Stop();
            }
            _puff = State == Phase.Inhaling
                ? Mathf.SmoothStep(0f, 1f, tick / 40f) + Mathf.SmoothStep(0f, 0.025f, (tick - 34f) / 14f) * Mathf.Sin(tick * 0.9f)
                : 0f;
            _whistle = Mathf.SmoothStep(0f, 1f, (tick - 4f) / 4f)
                * (1f - Mathf.SmoothStep(0f, 1f, (tick - 56f) / 8f));
            _rig.Request(_puff, _whistle);
            if (tick >= 68) Finish();
        }

        private bool CurrentAttack() => _owner != null && PresentationLifetime.IsCurrentAttack(
            _owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence);

        private void Finish()
        {
            State = Phase.Done;
            if (Inhale != null) Inhale.Stop();
        }

        private void OnDisable()
        {
            Finish();
            if (Deflate != null) Deflate.Stop();
        }
    }
}
