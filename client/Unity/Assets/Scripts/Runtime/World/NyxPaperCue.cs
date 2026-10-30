using System;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Package paper shapes on the restored attack clock, including hitstop and interruption.</summary>
    public sealed class NyxPaperCue : MonoBehaviour, IPresentationOwnerReceiver
    {
        [Serializable]
        public struct Layer
        {
            public Transform Content;
            public int StartTick, EndTick;
            public Vector3 From, To, Scale, ScaleTo;
            public float TurnDegreesPerTick;
        }

        public bool FollowOwner = true;
        public string OriginBoneId = "";
        public Layer[] Layers = Array.Empty<Layer>();
        private PlayerRenderer _owner;
        private byte _sequence;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            _sequence = owner != null ? owner.CurrentAttackSequence : (byte)0;
            // The cue tracks its owner itself. Left under the hips bone it would inherit the hit-feel
            // squash through a rotated bone as shear, tipping floor shapes into the floor.
            if (FollowOwner && transform.parent != null) transform.SetParent(null, true);
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            bool current = _owner != null && _owner.isActiveAndEnabled
                && PresentationLifetime.IsCurrentAttack(_owner.CurrentActionState, _owner.CurrentAttackSequence, _sequence);
            int tick = current ? _owner.CurrentAttackElapsedTicks : -1;
            if (current && FollowOwner)
            {
                var bone = string.IsNullOrEmpty(OriginBoneId) ? null : _owner.ResolvePresentationBone(OriginBoneId);
                transform.SetPositionAndRotation(bone != null ? bone.position : _owner.transform.position - Vector3.up * _owner.ModelYOffset,
                    Quaternion.Euler(0f, _owner.transform.eulerAngles.y, 0f));
                Vector3 scale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                transform.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
            }
            foreach (var layer in Layers)
            {
                if (layer.Content == null) continue;
                bool show = current && tick >= layer.StartTick && tick < layer.EndTick;
                layer.Content.gameObject.SetActive(show);
                if (!show) continue;
                float age = tick - layer.StartTick;
                float t = Mathf.Clamp01(age / Mathf.Max(1f, layer.EndTick - layer.StartTick - 1));
                layer.Content.localPosition = Vector3.LerpUnclamped(layer.From, layer.To, t);
                layer.Content.localRotation = Quaternion.Euler(0f, age * layer.TurnDegreesPerTick, 0f);
                // The full silhouette is present on the first active tick and eases toward ScaleTo
                // (a burst pops outward, a slash thins). Snap away after the last.
                Vector3 end = layer.ScaleTo == Vector3.zero ? layer.Scale : layer.ScaleTo;
                float eased = 1f - (1f - t) * (1f - t);
                layer.Content.localScale = Vector3.LerpUnclamped(layer.Scale, end, eased) * (1f + .035f * Mathf.Sin(t * Mathf.PI));
            }
        }

        private static float Inverse(float x) => Mathf.Abs(x) > .0001f ? 1f / x : 1f;
    }
}
