using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    public class ProjectileTrajectoryIndicator : MonoBehaviour
    {
        private const int ArcSegments = 30;
        private const float Gravity = 30f;
        private const float LaunchAngleDeg = 30f;
        private const float LaunchOffsetY = 1.2f;

        private LineRenderer _arcLine;
        private Material _arcMaterial;
        private readonly Vector3[] _arcPoints = new Vector3[ArcSegments + 1];

        private void Awake() => EnsureVisuals();

        private void EnsureVisuals()
        {
            if (_arcLine != null) return;

            var arcGO = new GameObject("ArcLine");
            arcGO.transform.SetParent(transform, false);
            _arcLine = arcGO.AddComponent<LineRenderer>();
            _arcLine.startWidth = 0.1f;
            _arcLine.endWidth = 0.05f;
            _arcLine.positionCount = 0;
            _arcMaterial = new Material(Shader.Find("Sprites/Default"));
            _arcMaterial.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            _arcLine.sharedMaterial = _arcMaterial;
            _arcLine.enabled = false;
        }

        private void OnDestroy()
        {
            if (_arcMaterial != null)
                Destroy(_arcMaterial);
        }

        /// <param name="origin">The thrower's body centre (state PY).</param>
        /// <param name="impact">Where the throw really lands, when known: the arc ends there
        /// instead of at foot height on a y = 0 floor.</param>
        public void SetTrajectory(Vector3 origin, float capsuleHeight, float yawRad, float distance, Vector3? impact = null)
        {
            EnsureVisuals();
            float launchRad = LaunchAngleDeg * Mathf.Deg2Rad;
            float dY = -capsuleHeight * 0.5f - LaunchOffsetY;

            CombatMath.ComputeProjectileLaunch(distance, launchRad, Gravity, dY,
                out float _, out float hSpeed, out float vSpeed);

            float aimCos = Mathf.Cos(yawRad);
            float aimSin = Mathf.Sin(yawRad);
            float startY = origin.y + LaunchOffsetY;
            float reach = distance;
            if (impact.HasValue)
                reach = new Vector2(impact.Value.x - origin.x, impact.Value.z - origin.z).magnitude;
            float totalTime = reach / Mathf.Max(hSpeed, 0.01f);

            for (int i = 0; i <= ArcSegments; i++)
            {
                float t = (float)i / ArcSegments * totalTime;
                float y = startY + vSpeed * t - 0.5f * Gravity * t * t;
                _arcPoints[i] = new Vector3(
                    origin.x + hSpeed * aimSin * t,
                    impact.HasValue ? y : Mathf.Max(y, 0f),
                    origin.z + hSpeed * aimCos * t);
            }

            _arcLine.positionCount = _arcPoints.Length;
            _arcLine.SetPositions(_arcPoints);
            _arcLine.enabled = true;
        }

        public void Clear()
        {
            if (_arcLine == null) return;
            _arcLine.enabled = false;
            _arcLine.positionCount = 0;
        }
    }
}
