using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    [CreateAssetMenu(fileName = "NewProjectileVFXConfig", menuName = "SLOPArena/Projectile VFX Config")]
    public class ProjectileVFXConfig : ScriptableObject
    {
        public GameObject ExplosionPrefab;
        public float ExplosionScale = 1f;
        public ProjectileVisualEntry[] ProjectileEntries = System.Array.Empty<ProjectileVisualEntry>();
        public ExplosionOverride[] ExplosionOverrides = System.Array.Empty<ExplosionOverride>();
    }

    [System.Serializable]
    public class ProjectileVisualEntry
    {
        public CharacterClass Character;
        public byte AttackSlot;      // CharacterState.AttackSlot wire value (11 = A)
        public bool Airborne;
        public GameObject Prefab;
        public float Scale = 1f;
    }

    [System.Serializable]
    public class ExplosionOverride
    {
        public CharacterClass Character;
        public byte AttackSlot;       // 0 = any explosion; otherwise CharacterState.AttackSlot wire value
        public GameObject Prefab;
        public float Scale = 1f;
    }
}
