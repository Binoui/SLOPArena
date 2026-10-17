using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Marks a projectile visual's child, such as a smoke trail, that should outlive the
    /// projectile. <c>ProjectileVFXManager</c> calls <see cref="Release"/> before it destroys the
    /// visual: the child detaches in place, stops emitting and is destroyed after
    /// <see cref="Seconds"/>, so a trail fades instead of vanishing on impact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LingerAfterProjectile : MonoBehaviour
    {
        public float Seconds = 1f;

        public void Release()
        {
            transform.SetParent(null, true);
            foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, Seconds);
        }
    }
}
