using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// A bonus Character Course target that takes several hits (a car to wreck, a punch machine to
    /// max out). Each accepted hit shows the next damage stage, jolts the model and fills the
    /// optional meter; Shared decides the hits and the break. The root stays on the Shared target
    /// centre (aim selection and lock-on read it) and keeps its authored facing. Presentation only.
    /// </summary>
    public sealed class CourseBonusTarget : MonoBehaviour
    {
        public Transform Model;
        [Tooltip("Shown one at a time: stage 0 untouched, the last just before the break.")]
        public GameObject[] Stages = System.Array.Empty<GameObject>();
        [Tooltip("Scaled along Y from MeterEmpty to 1 as the hits add up (a punch machine's score), optional.")]
        public Transform Meter;
        public float MeterEmpty = .08f;
        public float JoltDegrees = 9f;
        public float JoltSeconds = .35f;
        public Vector3 JoltAxis = new(1f, 0f, .4f);
        public float IdleSwayDegrees;
        public float IdleSwayHz = .4f;
        private float _jolt = -1f, _phase;
        private int _taken, _total = 1;

        public void Init(float phase) { _phase = phase; ResetDamage(); }

        /// <summary>A fresh attempt: untouched, meter empty.</summary>
        public void ResetDamage()
        {
            _taken = 0; _jolt = -1f;
            Present();
        }

        /// <summary>The <paramref name="taken"/>-th of <paramref name="total"/> hits landed.</summary>
        public void Hit(int taken, int total)
        {
            _taken = Mathf.Clamp(taken, 0, total); _total = Mathf.Max(1, total);
            _jolt = 0f;
            Present();
        }

        private void Present()
        {
            int stage = Stages.Length == 0 ? 0 : Mathf.Clamp(_taken * Stages.Length / Mathf.Max(1, _total), 0, Stages.Length - 1);
            for (int i = 0; i < Stages.Length; i++) if (Stages[i] != null) Stages[i].SetActive(i == stage);
            if (Meter != null)
            {
                var scale = Meter.localScale;
                Meter.localScale = new Vector3(scale.x, Mathf.Lerp(MeterEmpty, 1f, _taken / (float)Mathf.Max(1, _total)), scale.z);
            }
        }

        private void LateUpdate()
        {
            if (Model == null) return;
            float sway = IdleSwayDegrees * Mathf.Sin(Time.time * Mathf.PI * 2f * IdleSwayHz + _phase);
            float jolt = 0f;
            if (_jolt >= 0f)
            {
                _jolt += Time.deltaTime;
                float t = _jolt / Mathf.Max(.01f, JoltSeconds);
                jolt = t >= 1f ? 0f : JoltDegrees * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t);
                if (t >= 1f) _jolt = -1f;
            }
            Model.localRotation = Quaternion.AngleAxis(sway + jolt, JoltAxis.normalized);
        }
    }
}
