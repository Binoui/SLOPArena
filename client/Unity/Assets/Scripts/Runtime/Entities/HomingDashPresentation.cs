#nullable enable
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Placeholder homing dash look (ADR-0042), shared by every fighter: an Action Yellow trail
    /// from the chest while the dash travels. It follows the applied state and never drives
    /// gameplay. The final effect waits on approved concepts.
    /// </summary>
    public sealed class HomingDashPresentation : MonoBehaviour
    {
        private static readonly Color ActionYellow = new(1f, 0.8f, 0.133f, 1f);
        private const float ChestHeight = 1f;
        private static Material? _material;

        private TrailRenderer? _trail;

        public void Init()
        {
            if (_trail != null) return;
            _material ??= new Material(Resources.Load<Shader>("Shaders/HomingDashTrail"));
            var holder = new GameObject("Homing Dash Trail");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = Vector3.up * ChestHeight;
            _trail = holder.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = _material;
            _trail.time = 0.22f;
            _trail.minVertexDistance = 0.05f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0f));
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(ActionYellow, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.emitting = false;
        }

        /// <summary>Call once per applied simulation tick, after the renderer has applied the state.</summary>
        public void OnState(in CharacterState state)
        {
            if (_trail == null) return;
            bool travelling = state.State == ActionState.HomingDash;
            if (travelling && !_trail.emitting) _trail.Clear();
            _trail.emitting = travelling;
        }

        public void Clear()
        {
            if (_trail == null) return;
            _trail.emitting = false;
            _trail.Clear();
        }
    }
}
