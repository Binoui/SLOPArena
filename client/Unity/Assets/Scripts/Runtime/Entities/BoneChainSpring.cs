using System;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Presentation-only verlet spring for an unanimated bone chain such as a tail. Each frame the chain is
    /// reset to its rest pose under the animated parent, then every bone is turned toward its lagging tip.
    /// Runs in Play Mode only, so the authoritative pose bake and edit-mode previews keep the rest pose.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class BoneChainSpring : MonoBehaviour
    {
        [SerializeField] private Transform[] _bones = Array.Empty<Transform>();
        [Tooltip("Per-frame (60 Hz) pull of each tip toward its rest position.")]
        [SerializeField, Range(0f, 1f)] private float _stiffness = 0.2f;
        [Tooltip("Per-frame (60 Hz) loss of tip velocity.")]
        [SerializeField, Range(0f, 1f)] private float _damping = 0.2f;
        [SerializeField] private Vector3 _gravity = new Vector3(0f, -2f, 0f);
        [Tooltip("Snap to rest when the chain root jumps farther than this in one frame (respawn, teleport).")]
        [SerializeField] private float _teleportDistance = 1.5f;

        private Quaternion[] _restLocal;
        private Vector3[] _tipLocal;
        private Vector3[] _position;
        private Vector3[] _previous;
        private Vector3 _lastRoot;
        private bool _reset = true;

        public void Configure(Transform[] bones)
        {
            _bones = bones ?? Array.Empty<Transform>();
            _restLocal = null;
        }

        private void OnEnable() => _reset = true;

        /// <summary>Snaps the chain to rest on the next step (a new clip in an offline preview).</summary>
        public void ResetSimulation() => _reset = true;

        private bool EnsureRest()
        {
            if (_restLocal != null)
                return true;
            int count = _bones.Length;
            if (count == 0 || Array.Exists(_bones, bone => bone == null))
                return false;
            _restLocal = new Quaternion[count];
            _tipLocal = new Vector3[count];
            _position = new Vector3[count];
            _previous = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var bone = _bones[i];
                _restLocal[i] = bone.localRotation;
                Vector3 tip;
                if (i + 1 < count)
                    tip = _bones[i + 1].position;
                else if (bone.childCount > 0)
                    tip = bone.GetChild(0).position;
                else
                    tip = bone.position + (count > 1 ? bone.position - _bones[i - 1].position : bone.up * 0.05f);
                _tipLocal[i] = bone.InverseTransformPoint(tip);
            }
            _reset = true;
            return true;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        /// <summary>
        /// Advances the spring by <paramref name="deltaTime"/> seconds after the pose is evaluated. LateUpdate calls it
        /// in Play Mode; offline previews (the genra real-character render) call it once per evaluated tick.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (!EnsureRest())
                return;
            float dt = Mathf.Min(deltaTime, 1f / 20f);
            float frames = dt * 60f;
            float pull = 1f - Mathf.Pow(1f - _stiffness, frames);
            float keep = Mathf.Pow(1f - _damping, frames);

            for (int i = 0; i < _bones.Length; i++)
                _bones[i].localRotation = _restLocal[i];
            Vector3 root = _bones[0].position;
            if ((root - _lastRoot).sqrMagnitude > _teleportDistance * _teleportDistance)
                _reset = true;
            _lastRoot = root;

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                Vector3 head = bone.position;
                Vector3 target = bone.TransformPoint(_tipLocal[i]);
                if (_reset)
                {
                    _position[i] = _previous[i] = target;
                    continue;
                }
                Vector3 velocity = (_position[i] - _previous[i]) * keep;
                _previous[i] = _position[i];
                Vector3 next = _position[i] + velocity + _gravity * (dt * dt);
                next = Vector3.Lerp(next, target, pull);
                Vector3 toNext = next - head;
                Vector3 toTarget = target - head;
                float length = toTarget.magnitude;
                if (toNext.sqrMagnitude < 1e-10f || length < 1e-5f)
                {
                    _position[i] = target;
                    continue;
                }
                _position[i] = head + toNext.normalized * length;
                bone.rotation = Quaternion.FromToRotation(toTarget, _position[i] - head) * bone.rotation;
            }
            _reset = false;
        }
    }
}
