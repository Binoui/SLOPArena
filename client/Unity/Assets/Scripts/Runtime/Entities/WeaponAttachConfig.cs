using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// One weapon entry: which attack slot triggers it, which bone it follows,
    /// which prefab to spawn, and its bind-space attachment transform.
    /// </summary>

    [System.Serializable]
    public class WeaponEntry
    {
        [Tooltip("AttackSlot value that makes this weapon visible (0 = always visible; otherwise the CharacterState AttackSlot wire value, e.g. 11 = A).")]
        public byte AttackSlot;

        [Tooltip("Exact bone name from the SkinnedMeshRenderer (e.g. mixamorig:RightHand).")]
        public string BoneName;

        [Tooltip("Weapon prefab. Drag from Project view.")]
        public GameObject Prefab;

        [Tooltip("Position in the attached bone's local bind space.")]
        public Vector3 PositionOffset;
        [Tooltip("Use a separate local attachment transform during the fire/release phase.")]
        public bool HasFirePhaseOverride;
        [Tooltip("Fire/release position in the attached bone's local bind space.")]
        public Vector3 FirePositionOffset;
        [Tooltip("Fire/release rotation in the attached bone's local bind space.")]
        public Vector3 FireRotationOffset;
        [Tooltip("Rotation in the attached bone's local bind space.")]
        public Vector3 RotationOffset;
        [Tooltip("Hide the prop this many ticks after the attack starts (0 = keep for the whole attack).")]
        public int HideAfterTicks;
        [Tooltip("Optional prefab supplying the TECH material and color for a blade-motion trail.")]
        public GameObject TrailStylePrefab;
        [Tooltip("How much recent blade movement stays visible, in seconds.")]
        public float HitboxMotionTime = 0.12f;
        [Range(0.01f, 1f), Tooltip("Fraction of blade length covered by the trail, measured inward from the tip. TECH Thin Spiral uses a narrow outer edge.")]
        public float TrailBladeWidth = 0.8f;

        [Tooltip("Optional blade hilt transform name used to position the hitbox trail.")]
        public string TrailHiltAnchor;

        [Tooltip("Optional blade tip transform name used to position the hitbox trail.")]
        public string TrailTipAnchor;


    }

    /// <summary>
    /// Data asset that describes all weapon props for one character.
    /// Place in Resources/WeaponConfigs/ named after the CharacterClass enum value
    /// (e.g. Resources/WeaponConfigs/Manki.asset).
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeaponAttachConfig", menuName = "SLOPArena/Weapon Attach Config")]
    public class WeaponAttachConfig : ScriptableObject
    {
        public WeaponEntry[] Entries = System.Array.Empty<WeaponEntry>();
    }
}
