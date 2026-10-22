using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Plays an inactive child of a cue when its owner's attack ends, however it ends, where the
    /// cue then is, and hides parts of the cue: Sir Bonk's hobby horse leaves in the puff of straw
    /// it came in, and its pennant goes with it. Put it on a cue that is not attack-bound, so it
    /// outlives the attack, and give that cue a duration longer than the move. The played copy is
    /// detached and lives <see cref="ContentSeconds"/>, so it outlasts the cue too. Nothing plays
    /// when the owner goes away (a knockout disables it). Cosmetic only.
    /// </summary>
    public sealed class AttackEndCue : MonoBehaviour, IPresentationOwnerReceiver
    {
        [Tooltip("Inactive child; a detached copy of it plays when the attack ends.")]
        public GameObject? Content;
        public float ContentSeconds = 1f;
        [Tooltip("Parts of the cue switched off when the attack ends.")]
        public GameObject[] HideOnEnd = System.Array.Empty<GameObject>();

        private PlayerRenderer? _owner;
        private byte _sequence;
        private bool _armed;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            _armed = owner != null && PresentationLifetime.IsCurrentAttack(owner.CurrentActionState,
                owner.CurrentAttackSequence, owner.CurrentAttackSequence);
            if (_armed)
                _sequence = owner!.CurrentAttackSequence;
            else
                End(play: false);
        }

        private void LateUpdate()
        {
            if (!_armed) return;
            bool available = _owner != null && _owner.isActiveAndEnabled;
            if (available && PresentationLifetime.IsCurrentAttack(_owner!.CurrentActionState,
                    _owner.CurrentAttackSequence, _sequence))
                return;
            End(play: available);
        }

        private void End(bool play)
        {
            _armed = false;
            foreach (GameObject part in HideOnEnd)
                if (part != null) part.SetActive(false);
            if (!play || Content == null) return;
            Transform source = Content.transform;
            GameObject copy = Instantiate(Content, source.position, source.rotation);
            copy.transform.localScale = source.lossyScale;
            copy.SetActive(true);
            Destroy(copy, ContentSeconds);
        }
    }
}
