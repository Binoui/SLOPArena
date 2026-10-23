using UnityEngine;

namespace SlopArena.Client.Combat
{
    [CreateAssetMenu(fileName = "NewProjectileVFXConfig", menuName = "SLOPArena/Projectile VFX Config")]
    public class ProjectileVFXConfig : ScriptableObject
    {
        public GameObject ExplosionPrefab;
        public float ExplosionScale = 1f;
    }
}
