using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>Constrain the aim camera to the visible side of baked stage geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraObstruction : CinemachineExtension
    {
        /// <summary>Empty object a stage's art exports to opt in to <see cref="SeeThroughThinWalls"/>.</summary>
        public const string SeeThroughMarker = "Camera See-Through Thin Walls";

        /// <summary>Set by MatchBase per stage: the camera crosses collision walls thinner than
        /// <see cref="ThinWall"/> (clear tubes) instead of pulling in. Off everywhere else, because
        /// other stages use thin slabs for opaque bridges and floors.</summary>
        public static bool SeeThroughThinWalls;

        // Fits inside a fighter's wall clearance while enclosing the aim camera's near plane.
        private const float Radius = 0.25f;
        private const float ThinWall = 0.3f;
        private const float ReturnTime = 0.15f;
        private float _distance;
        private Transform _target;

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body || vcam.Follow == null) return;

            var target = vcam.Follow;
            Vector3 origin = TargetPositionCache.GetTargetPosition(target);
            Vector3 offset = state.GetCorrectedPosition() - origin;
            float desiredDistance = offset.magnitude;
            if (desiredDistance < 0.001f) return;
            Vector3 direction = offset / desiredDistance;
            float safeDistance = ClearDistance(origin, direction, desiredDistance);

            bool reset = !vcam.PreviousStateIsValid || deltaTime < 0f || _target != target;
            _target = target;
            // Never damp into a wall. Only ease back out, capped by this frame's clear segment.
            _distance = reset ? safeDistance : Mathf.Min(safeDistance,
                Mathf.Lerp(_distance, desiredDistance, 1f - Mathf.Exp(-deltaTime / ReturnTime)));
            state.PositionCorrection += origin + direction * _distance - state.GetCorrectedPosition();
        }

        private static float ClearDistance(Vector3 origin, Vector3 direction, float desired)
        {
            float travelled = 0f;
            for (int wall = 0; wall < 8; wall++)
            {
                Vector3 start = origin + direction * travelled;
                float remaining = desired - travelled;
                // The stage proxy is excluded from ordinary aim raycasts and contains no fighters.
                if (!Physics.SphereCast(start, Radius, direction, out var hit, remaining,
                        Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore))
                    return desired;
                // Cross a wall only when the sight line itself enters and leaves it within ThinWall;
                // a sphere grazing something else first still stops the camera there. A sphere that
                // starts overlapping the crossed wall does not report it again.
                if (!SeeThroughThinWalls
                    || !Physics.Raycast(start, direction, out var entry, remaining, Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore)
                    || entry.distance > hit.distance + 2f * Radius
                    || !Physics.Raycast(entry.point + direction * 0.005f, direction, out var exit, ThinWall,
                        Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore))
                    return travelled + Mathf.Max(0f, hit.distance - 0.01f);
                travelled += entry.distance + exit.distance + 0.01f;
                if (travelled >= desired) return desired;
            }
            return travelled;
        }
    }
}
