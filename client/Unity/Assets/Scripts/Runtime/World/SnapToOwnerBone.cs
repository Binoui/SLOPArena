using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Moves part of a hit effect from the contact onto one of its owner's bones when it spawns,
    /// e.g. Bonk's bucket ringing on his own head after a headbutt. Use local-space particles:
    /// the move happens after the instance is created, before its first particle update.
    /// </summary>
    public sealed class SnapToOwnerBone : MonoBehaviour, IPresentationOwnerReceiver
    {
        public string BoneId = "bone.head";
        [Tooltip("World-space offset from the bone, in metres.")]
        public Vector3 Offset;

        public void BindOwner(PlayerRenderer owner)
        {
            Transform? bone = owner != null ? owner.ResolvePresentationBone(BoneId) : null;
            if (bone != null) transform.position = bone.position + Offset;
        }
    }
}
