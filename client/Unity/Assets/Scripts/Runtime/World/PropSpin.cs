using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Spins part of a presentation prop about one of its own axes: Sir Bonk's thrown pot lid skims flat, spinning about
    /// its face. Cosmetic only; it runs on game time, so it stops when the game pauses.
    /// </summary>
    public sealed class PropSpin : MonoBehaviour
    {
        public Vector3 Axis = Vector3.up;
        public float DegreesPerSecond = 1080f;

        private void Update() => transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
    }
}
