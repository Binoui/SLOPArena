using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Plays a footfall each time its owner's looping phase clip passes one of
    /// <see cref="FootfallTicks"/>: the clip-clop of Sir Bonk's hobby horse. A steered charge loops
    /// its gallop clip by setting the attack clock to the loop's own tick, so this reads that clock
    /// while <see cref="ComboStage"/> shows the loop's clip, until the attack ends. Each footfall
    /// copies, detached, the matching entry of <see cref="Steps"/> beside the lower foot (outward
    /// from his body) and, on the ground only, <see cref="Ground"/> at that foot: no dust on thin
    /// air. Put it on the charge's launch cue, which outlives the gallop. Cosmetic only.
    /// </summary>
    public sealed class LoopFootfalls : MonoBehaviour, IPresentationOwnerReceiver
    {
        [Tooltip("ComboStage while the looping clip shows (its index among the stage's clips).")]
        public int ComboStage = 1;
        public int LoopTicks = 24;
        [Tooltip("Loop ticks where a foot lands, in order.")]
        public int[] FootfallTicks = { 0, 12 };
        [Tooltip("Inactive children, one per footfall tick: letters and the knock.")]
        public GameObject[] Steps = System.Array.Empty<GameObject>();
        [Tooltip("Inactive child played at the foot when grounded: the dust kick.")]
        public GameObject? Ground;
        [Tooltip("Metres the step is placed outward from the foot, away from his body.")]
        public float SideOffset = 0.3f;
        public float CopySeconds = 0.8f;

        private PlayerRenderer? _owner;
        private Transform? _leftFoot, _rightFoot;
        private byte _sequence;
        private int _lastTick = -1;
        private bool _done;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            if (owner == null || !PresentationLifetime.IsCurrentAttack(owner.CurrentActionState,
                    owner.CurrentAttackSequence, owner.CurrentAttackSequence))
            {
                _done = true;
                return;
            }
            _sequence = owner.CurrentAttackSequence;
            _leftFoot = owner.ResolvePresentationBone("bone.left-foot");
            _rightFoot = owner.ResolvePresentationBone("bone.right-foot");
            Step();
        }

        private void LateUpdate() => Step();

        private void Step()
        {
            if (_done || _owner == null) return;
            if (!_owner.isActiveAndEnabled
                || !PresentationLifetime.IsCurrentAttack(_owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence))
            {
                _done = true;
                return;
            }
            if (_owner.CurrentComboStage != ComboStage || LoopTicks <= 0)
            {
                _lastTick = -1;
                return;
            }
            int tick = ((_owner.CurrentAttackElapsedTicks % LoopTicks) + LoopTicks) % LoopTicks;
            if (_lastTick < 0)
            {
                // Entering the loop: a footfall on this very tick plays (the gallop lands on its tick 0).
                _lastTick = (tick + LoopTicks - 1) % LoopTicks;
            }
            if (tick == _lastTick) return;
            for (int i = 0; i < FootfallTicks.Length; i++)
                if (Crossed(_lastTick, tick, FootfallTicks[i]))
                    Footfall(i);
            _lastTick = tick;
        }

        /// <summary>Whether <paramref name="mark"/> lies in (from, to], walking forward round the loop.</summary>
        private bool Crossed(int from, int to, int mark)
        {
            int span = (to - from + LoopTicks) % LoopTicks;
            int offset = (mark - from + LoopTicks) % LoopTicks;
            return offset > 0 && offset <= span;
        }

        private void Footfall(int index)
        {
            Transform? foot = LowerFoot();
            if (foot == null || _owner == null) return;
            Transform body = _owner.transform;
            Quaternion facing = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
            float side = Vector3.Dot(foot.position - body.position, body.right) < 0f ? -1f : 1f;
            if (index < Steps.Length && Steps[index] != null)
                Play(Steps[index], foot.position + body.right * (side * SideOffset), facing);
            if (Ground != null && _owner.CurrentIsGrounded)
                Play(Ground, foot.position, facing);
        }

        private Transform? LowerFoot()
        {
            if (_leftFoot == null) return _rightFoot;
            if (_rightFoot == null) return _leftFoot;
            return _leftFoot.position.y <= _rightFoot.position.y ? _leftFoot : _rightFoot;
        }

        private void Play(GameObject template, Vector3 position, Quaternion rotation)
        {
            GameObject copy = Instantiate(template, position, rotation);
            copy.SetActive(true);
            Destroy(copy, CopySeconds);
        }
    }
}
