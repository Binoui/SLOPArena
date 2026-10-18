using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// An idle Character Course target. It turns its front toward the camera, bobs and sways a
    /// little and keeps its glow behind it. The root stays on the Shared target centre (aim
    /// selection and lock-on read it); only <see cref="Model"/> moves. Presentation only.
    /// </summary>
    public sealed class CourseLantern : MonoBehaviour
    {
        public Transform Model;
        [Tooltip("Camera-facing glow, kept behind the centre.")]
        public Transform Halo;
        [Tooltip("Metres the halo sits behind the centre, away from the camera (unscaled).")]
        public float HaloDepth = .7f;
        public float BobHeight = .05f;
        public float BobHz = .32f;
        public float SwayDegrees = 4f;
        private Transform _viewer;
        private float _phase;

        public void Init(Transform viewer, float phase)
        {
            _viewer = viewer;
            _phase = phase;
        }

        private void LateUpdate()
        {
            Vector3 toViewer = _viewer != null ? _viewer.position - transform.position : Vector3.back;
            var flat = new Vector3(toViewer.x, 0f, toViewer.z);
            if (flat.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(flat);
            float time = Time.time * Mathf.PI * 2f * BobHz + _phase;
            if (Model != null)
            {
                Model.localPosition = new Vector3(0f, Mathf.Sin(time) * BobHeight, 0f);
                Model.localRotation = Quaternion.Euler(Mathf.Sin(time * .71f + 1.3f) * SwayDegrees * .5f, 0f,
                    Mathf.Sin(time * .53f) * SwayDegrees);
            }
            if (Halo != null && toViewer.sqrMagnitude > 1e-4f)
            {
                var direction = toViewer.normalized;
                Halo.SetPositionAndRotation(transform.position - direction * HaloDepth * transform.lossyScale.x,
                    Quaternion.LookRotation(direction));
            }
        }
    }
}
