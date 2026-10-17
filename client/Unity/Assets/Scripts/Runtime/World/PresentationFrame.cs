using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The content frame of a bone-attached effect: put it on a child that holds the effect.
    /// <see cref="UnitScale"/> cancels the bone's scale, so offsets and local-space particles are
    /// in metres on any rig (FightGuy's bones carry a 1.5× import scale). <see cref="Upright"/>
    /// keeps the content upright and turned with its fighter, for shapes built around the
    /// vertical axis such as spirals, rings and signs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresentationFrame : MonoBehaviour
    {
        public bool UnitScale = true;
        public bool Upright;

        private Transform _fighter;

        // Fires when the effect root is parented to its bone, before the first particle update.
        private void OnTransformParentChanged() => Apply();

        private void LateUpdate() => Apply();

        private void Apply()
        {
            Transform root = transform.parent;
            Transform bone = root != null ? root.parent : null;
            if (UnitScale)
            {
                Vector3 scale = bone != null ? bone.lossyScale : Vector3.one;
                transform.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
            }
            if (Upright)
            {
                if (_fighter == null) _fighter = GetComponentInParent<PlayerRenderer>()?.transform;
                transform.rotation = Quaternion.Euler(0f, _fighter != null ? _fighter.eulerAngles.y : 0f, 0f);
            }
        }

        private static float Inverse(float value) => Mathf.Abs(value) > 1e-4f ? 1f / value : 1f;
    }
}
