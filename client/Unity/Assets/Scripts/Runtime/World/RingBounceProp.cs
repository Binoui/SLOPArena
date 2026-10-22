using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>A presentation pose that recorded previews can seek after posing the fighter.</summary>
    public interface IPresentationPoseReceiver
    {
        void SamplePresentation(PlayerRenderer owner);
    }

    /// <summary>
    /// The independently squashed life ring of Ring Bounce. The throw waits ahead in the entry
    /// model frame, contact follows the feet, and rebound stays at its launch event's position.
    /// Samples use the attack clock, including
    /// Hitstop and recorded seeks. This prop has no collision or movement authority.
    /// </summary>
    [DefaultExecutionOrder(750)]
    public sealed class RingBounceProp : MonoBehaviour, IPresentationOwnerReceiver, IPresentationPoseReceiver
    {
        public Transform Ring;
        public Vector3[] Positions = System.Array.Empty<Vector3>();
        public Vector3[] Scales = System.Array.Empty<Vector3>();
        public Vector3[] EulerAngles = System.Array.Empty<Vector3>();
        public Vector3[] ActorOffsets = System.Array.Empty<Vector3>();
        public int ContactTick;
        public int FirstTick;
        public bool WorldRebound;

        private PlayerRenderer _owner;
        private byte _sequence;
        private bool _bound;
        private bool _hasEntryFrame;
        private Vector3 _entryOrigin;
        private Quaternion _entryRotation;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            _sequence = owner != null ? owner.CurrentAttackSequence : (byte)0;
            _bound = true;
            CaptureEntryFrame(owner);
            SamplePresentation(owner);
        }

        private void LateUpdate()
        {
            if (_bound) SamplePresentation(_owner);
        }

        public void SamplePresentation(PlayerRenderer owner)
        {
            if (!_bound)
            {
                _owner = owner;
                _sequence = owner != null ? owner.CurrentAttackSequence : (byte)0;
                _bound = true;
                CaptureEntryFrame(owner);
            }
            if (Ring == null) return;
            bool current = owner != null && owner.isActiveAndEnabled
                && PresentationLifetime.IsCurrentAttack(owner.CurrentActionState, owner.CurrentAttackSequence, _sequence);
            Ring.gameObject.SetActive(current);
            if (!current || Positions.Length == 0 || Scales.Length != Positions.Length
                || EulerAngles.Length != Positions.Length) return;

            int tick = Mathf.Clamp(owner.CurrentAttackElapsedTicks - FirstTick, 0, Positions.Length - 1);
            float visualScale = owner.CharacterDef?.VisualScale ?? 1f;
            if (WorldRebound)
            {
                Vector3 position = Positions[tick] * visualScale + Vector3.up * owner.ModelYOffset;
                Ring.SetPositionAndRotation(transform.TransformPoint(position),
                    transform.rotation * Quaternion.Euler(EulerAngles[tick]));
            }
            else
            {
                Transform frame = owner.Animator != null ? owner.Animator.transform : owner.transform;
                if (_hasEntryFrame && tick < ContactTick)
                    Ring.SetPositionAndRotation(_entryOrigin + _entryRotation * (Positions[tick] * visualScale),
                        _entryRotation * Quaternion.Euler(EulerAngles[tick]));
                else
                    Ring.SetPositionAndRotation(frame.TransformPoint(Positions[tick]
                            - (ActorOffsets.Length == Positions.Length ? ActorOffsets[tick] : Vector3.zero)),
                        owner.transform.rotation * Quaternion.Euler(EulerAngles[tick]));
            }
            // The effect can be parented to an animated bone; cancel that inherited scale.
            Vector3 inherited = Ring.parent != null ? Ring.parent.lossyScale : Vector3.one;
            Vector3 scale = Scales[tick] * visualScale;
            Ring.localScale = new Vector3(scale.x / Safe(inherited.x), scale.y / Safe(inherited.y), scale.z / Safe(inherited.z));
        }

        private void CaptureEntryFrame(PlayerRenderer owner)
        {
            if (WorldRebound || owner == null || Positions.Length == 0 || ActorOffsets.Length != Positions.Length) return;
            int tick = Mathf.Clamp(owner.CurrentAttackElapsedTicks - FirstTick, 0, Positions.Length - 1);
            Transform frame = owner.Animator != null ? owner.Animator.transform : owner.transform;
            _entryRotation = owner.transform.rotation;
            _entryOrigin = frame.position - _entryRotation * (ActorOffsets[tick] * (owner.CharacterDef?.VisualScale ?? 1f));
            _hasEntryFrame = true;
        }

        private static float Safe(float value) => Mathf.Abs(value) > 0.0001f ? value : 1f;
    }
}
