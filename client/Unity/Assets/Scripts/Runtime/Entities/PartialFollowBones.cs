using System;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Presentation-only helper bones for rigid costume parts that sit on a joint: a knee guard, an elbow cap, a
    /// pauldron. Each helper is a sibling of its driver bone with the driver's rest frame, and every frame it takes a
    /// share of the driver's turn away from that rest (half the knee's bend keeps a knee guard on the kneecap). The
    /// part is skinned to the helper alone, so it stays rigid. Skinning it to both bones of the joint instead blends
    /// two rotations, which shrinks and tilts it as the joint bends (Sir Bonk's knee guards, 2026-11-11).
    /// Runs in Play Mode; offline previews call <see cref="Step"/> after they pose the body. Without it a helper
    /// stays at rest on its parent, still rigid.
    /// </summary>
    [DefaultExecutionOrder(-50)] // after the clip; before WeaponAttach (0) places a prop that rides a helper
    public sealed class PartialFollowBones : MonoBehaviour
    {
        [Serializable]
        public struct Follow
        {
            public Transform Helper;
            public Transform Driver;
            [Range(0f, 1f)] public float Share;
            [Tooltip("The driver's local rotation in the bind pose; the helper's is the same.")]
            public Quaternion Rest;
        }

        [SerializeField] private Follow[] _follows = Array.Empty<Follow>();

        public void Configure(Follow[] follows) => _follows = follows ?? Array.Empty<Follow>();

        private void LateUpdate() => Step();

        /// <summary>Turns every helper for the pose the drivers hold now.</summary>
        public void Step()
        {
            foreach (var follow in _follows)
                if (follow.Helper != null && follow.Driver != null)
                    follow.Helper.localRotation = Quaternion.Slerp(follow.Rest, follow.Driver.localRotation, follow.Share);
        }
    }
}
