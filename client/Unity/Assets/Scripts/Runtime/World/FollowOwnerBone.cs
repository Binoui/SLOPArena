using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Keeps part of a cue on one of its owner's bones for <see cref="Seconds"/> after it spawns,
    /// so world-space particles under it trail that bone's path: Sir Bonk's heels gouging the deck
    /// in the Joust's skid. It follows across the floor only and keeps its own height, which a
    /// <see cref="SnapToFloor"/> parent puts on the floor; it stops early where the stage floor
    /// drops away (he skidded off a ledge), so no marks float in the air. Cosmetic only.
    /// </summary>
    public sealed class FollowOwnerBone : MonoBehaviour, IPresentationOwnerReceiver
    {
        public string BoneId = "bone.left-foot";
        public float Seconds = 0.4f;
        [Tooltip("Metres the floor under the bone may differ from this part's height before it stops.")]
        public float MaxStep = 0.2f;

        private Transform? _bone;
        private float _until;

        public void BindOwner(PlayerRenderer owner)
        {
            _bone = owner != null ? owner.ResolvePresentationBone(BoneId) : null;
            _until = Time.time + Seconds;
            Follow();
        }

        private void LateUpdate()
        {
            if (_bone == null) return;
            if (Time.time > _until)
            {
                _bone = null;
                return;
            }
            Follow();
        }

        private void Follow()
        {
            if (_bone == null) return;
            Vector3 target = _bone.position;
            target.y = transform.position.y;
            if (PresentationFloor.Available && (!PresentationFloor.TrySample(target.x, target.z, out float floor)
                    || Mathf.Abs(floor - target.y) > MaxStep))
            {
                _bone = null;
                return;
            }
            transform.position = target;
        }
    }
}
