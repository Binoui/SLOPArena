using UnityEngine;

namespace SlopArena.Client.World
{
    public enum StageHazardAnchorKind { Point, Origin, Capsule, Box, Cells, Loop }

    public sealed class StageHazardAnchorMarker : MonoBehaviour
    {
        public StageHazardAnchorKind Kind;
        public Vector3 HalfExtents = Vector3.one;
        public float Radius = 1f;
    }
}
