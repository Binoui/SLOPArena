using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Tucks its owner into her shell while the attack shows one of <see cref="HoldAnimationIds"/>, through the
    /// model's <see cref="ShellTuckRig"/>: Bashie's Shell Slide folds in as she dives and stays in while she slides,
    /// then pops out when the slide ends (a hit, a miss or the look-down shows another clip) or the attack does. The
    /// tuck eases in over <see cref="InSeconds"/> and out over <see cref="OutSeconds"/>, overshooting a little each
    /// way. One tuck per cue: once out, it stays out. Emit it on the attack at the tick she dives. Cosmetic only.
    /// </summary>
    [DefaultExecutionOrder(700)] // asks before ShellTuckRig (800) applies
    public sealed class ShellTuck : MonoBehaviour, IPresentationOwnerReceiver
    {
        private static readonly bool[] Variants = { false, true };   // the slot's ground and air timelines

        public ShellTuckPose Pose = ShellTuckPose.Bashie;
        [Tooltip("Clips of the attack that keep her tucked: the dive and the slide loop.")]
        public string[] HoldAnimationIds = System.Array.Empty<string>();
        public float InSeconds = 0.1f;
        public float OutSeconds = 0.12f;
        [Tooltip("Overshoot of the ease (ease-out-back): 1.7 goes about 10% past and settles.")]
        public float Overshoot = 1.7f;

        private PlayerRenderer? _owner;
        private ShellTuckRig? _rig;
        private byte _sequence;
        private float _in, _out, _outFrom;
        private bool _leaving, _done;

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
            _rig = ShellTuckRig.For(owner, Pose);
            _done = _rig == null;
        }

        private void LateUpdate()
        {
            if (_done || _owner == null || _rig == null) return;
            if (!_owner.isActiveAndEnabled)
            {
                _done = true;                               // a knockout: the rig puts her back at once
                return;
            }
            if (!_leaving && Holding())
            {
                _in += Time.deltaTime;
                _rig.Request(Pose, Ease(_in / InSeconds));
                return;
            }
            if (!_leaving)
            {
                _leaving = true;
                _outFrom = Mathf.Min(1f, Ease(_in / InSeconds));
            }
            _out += Time.deltaTime;
            if (_out >= OutSeconds)
            {
                _done = true;
                return;
            }
            _rig.Request(Pose, _outFrom * (1f - Ease(_out / OutSeconds)));
        }

        private bool Holding() => ShowsClip(_owner!, _sequence, HoldAnimationIds);

        /// <summary>
        /// Whether <paramref name="owner"/>'s attack <paramref name="sequence"/> is still current and shows one of
        /// <paramref name="animationIds"/>: its stage or phase clip, read from the slot's clip names at
        /// <see cref="PlayerRenderer.CurrentComboStage"/>, as the renderer picks it.
        /// </summary>
        public static bool ShowsClip(PlayerRenderer owner, byte sequence, string[] animationIds)
        {
            if (!PresentationLifetime.IsCurrentAttack(owner.CurrentActionState, owner.CurrentAttackSequence, sequence)
                || owner.CharacterDef == null || owner.CurrentAttackSlot == 0)
                return false;
            int stage = owner.CurrentComboStage;
            foreach (bool airborne in Variants)
            {
                var names = owner.CharacterDef.GetSlotAbility(owner.CurrentAttackSlot - 1, airborne)?.AnimationNames;
                if (names != null && stage < names.Length && System.Array.IndexOf(animationIds, names[stage]) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Ease-out-back: 0 to 1, passing 1 by about a tenth on the way.</summary>
        private float Ease(float x)
        {
            x = Mathf.Clamp01(x) - 1f;
            return 1f + (Overshoot + 1f) * x * x * x + Overshoot * x * x;
        }
    }
}
