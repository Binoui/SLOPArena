using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A Character Course light pillar: it rises when raised, turns its beam toward the camera
    /// (yaw only), breathes slowly and fades out when released. Missed targets get a red one and
    /// the finish a gold one once every target is broken. Presentation only.
    /// </summary>
    public sealed class CourseBeacon : MonoBehaviour
    {
        [Tooltip("Vertical quad with its pivot at the base.")]
        public Transform Beam;
        public float RiseSeconds = .45f;
        public float FadeSeconds = .5f;
        public float PulseHz = .5f;
        [Tooltip("Width breathing, as a fraction of the beam width.")]
        public float Pulse = .1f;
        private Transform _viewer;
        private Vector3 _beamScale;
        private float _age, _released = -1f;

        public bool Released => _released >= 0f;

        public void Init(Transform viewer)
        {
            _viewer = viewer;
            if (Beam != null) _beamScale = Beam.localScale;
            Present();
        }

        /// <summary>Fades the pillar out, stops its particles and destroys it.</summary>
        public void Release()
        {
            if (_released >= 0f) return;
            _released = 0f;
            foreach (var system in GetComponentsInChildren<ParticleSystem>())
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_released >= 0f && (_released += Time.deltaTime) >= FadeSeconds)
            {
                Destroy(gameObject);
                return;
            }
            Present();
        }

        private void Present()
        {
            if (Beam == null) return;
            if (_viewer != null)
            {
                Vector3 toViewer = _viewer.position - Beam.position;
                toViewer.y = 0f;
                if (toViewer.sqrMagnitude > 1e-4f) Beam.rotation = Quaternion.LookRotation(toViewer);
            }
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(_age / RiseSeconds), 3f);
            float fade = _released >= 0f ? 1f - Mathf.Clamp01(_released / FadeSeconds) : 1f;
            float width = (1f + Pulse * Mathf.Sin(_age * Mathf.PI * 2f * PulseHz)) * fade;
            Beam.localScale = new Vector3(_beamScale.x * width, _beamScale.y * Mathf.Max(.001f, rise), _beamScale.z);
        }
    }
}
