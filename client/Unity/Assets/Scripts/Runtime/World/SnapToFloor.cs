using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Moves a cosmetic child (a blast ring, a scorch mark, a debris landing plane) onto the stage
    /// floor below it when the effect spawns. Without a stage floor (Ability Lab previews) it keeps
    /// its authored offset; with no floor within <see cref="MaxDrop"/> metres, or off the stage,
    /// it hides, so a mid-air blast leaves no floating ring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnapToFloor : MonoBehaviour
    {
        [Tooltip("Metres the floor may lie below the effect root.")]
        public float MaxDrop = 3f;
        [Tooltip("Height above the floor, to avoid z-fighting.")]
        public float Lift = 0.03f;

        private void Start()
        {
            if (!PresentationFloor.Available) return;
            Vector3 origin = transform.parent != null ? transform.parent.position : transform.position;
            if (!PresentationFloor.TrySample(origin.x, origin.z, out float floor)
                || origin.y - floor > MaxDrop || origin.y - floor < -0.5f)
            {
                gameObject.SetActive(false);
                return;
            }
            transform.position = new Vector3(origin.x, floor + Lift, origin.z);
        }
    }
}
