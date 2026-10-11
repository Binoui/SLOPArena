using System.Collections.Generic;

namespace SlopArena.Shared
{
    public enum AimMode : byte
    {
        None,            // No aim input; camera free, cursor locked
        GroundCursor,    // Cursor unlocked; raycast ground → AimYaw + AimDistance
        CameraForward3D, // Cursor locked; camera yaw+pitch → AimYaw + AimPitch
        /// <summary>Camera locked; mouse delta rotates a ground direction vector around the character (Wibou E).</summary>
        GroundVector,    // Camera frozen; mouse delta → AimYaw, fixed distance
    }
    public enum AimMovementMode : byte
    {
        Fixed = 0,
        Mobile = 1,
    }


    public enum AbilityBehavior : byte
    {
        MeleeCombo,          // LMB melee — single move per press (chains removed, issue #115)
        ChargeAttack,        // Hold briefly to charge (2 anims: attack, charged). RMB.
        AimedProjectile,     // Hold to aim parabola, release to throw (3 anims: start, loop, throw). Q.
        Projectile,          // Fire-and-forget projectile. Single anim.
        AirGroundProjectile, // Behaves differently when airborne vs grounded.
        SelfBuff,            // Applies a buff to self.
        AreaDenial,          // Places persistent hazard (mine, flame wall).
        DirectionalDash,     // Hold to aim a ground direction, release to dash a set distance (Wibou E).
    }

    /// <summary>Describes a bone trail particle VFX for an ability stage.</summary>
    public struct BoneTrailDef
    {
        /// <summary>Bone name in the skeleton (e.g. "mixamorig:RightHand").</summary>
        public string BoneName;
        /// <summary>Trail particle width in meters.</summary>
        public float Width;
        /// <summary>Trail color (RGBA).</summary>
        public float R, G, B, A;
    }

    /// <summary>
    /// Compatibility projection for ability input, presentation, and pose geometry.
    /// Admitted gameplay executes the cooked timeline and its typed capabilities.
    /// </summary>
    public class AbilitySpec
    {
        public AbilityBehavior Behavior = AbilityBehavior.MeleeCombo;
        public AimMode AimMode = AimMode.None;
        public AimMovementMode AimMovement = AimMovementMode.Fixed;
        /// <summary>
        /// Semantic id of the aim-loop clip played while holding an aimed ability
        /// (cooked AimedProjectile slots). Null = no authored aim loop — the
        /// presentation falls back to the slot's first animation (fixed policy)
        /// or locomotion (mobile policy).
        /// </summary>
        public string? AimAnimationId;
        /// <summary>Optional semantic hit VFX override. Null or empty preserves default presentation.</summary>
        public string? HitPresentationId;
        public string Name = "";
        public string Description = "";
        /// <summary>Resource filename (without extension) under Resources/Icons/{CharacterClass}/. Null/empty = no icon.</summary>
        public string? IconName;
        /// <summary>0 = no cooldown</summary>
        public ushort CooldownTicks;
        /// <summary>
        /// ADR-0015 / issue #115: recovery-designated move (Smash up-B analog). Only
        /// moves with this flag reset the FloatWindow (AirTimeTicks = 0 at activation) —
        /// normal air attacks no longer hover. Recovery abilities set this flag to opt into
        /// the reset.
        /// </summary>
        public bool IsRecoveryMove;
        /// <summary>
        /// Momentum override (ADR-0015 §2 refinement, 2026-08-12): a grounded activation
        /// zeroes the incoming horizontal velocity (VX/VZ) by default — "grounded moves stop
        /// movement". Set to true to keep run/dash momentum through the attack start
        /// (dash-attack style moves). Aerials always preserve momentum regardless of this
        /// flag — momentum-preserve is an aerial property.
        /// </summary>
        public bool PreserveMomentumOnStart;
        public AttackStage[] Stages = System.Array.Empty<AttackStage>();
        /// <summary>Animation name per combo stage.</summary>
        public string[]? AnimationNames;
        /// <summary>Optional bone trail VFX. Enabled during this ability's active period.</summary>
        public BoneTrailDef[]? BoneTrails;
        /// <summary>Animation playback speed multiplier. 0 = auto-compute from frames/duration.</summary>
        public float AnimSpeed = 0f;
        /// <summary>Named float parameters for server-side abilities (e.g., "backflip_damage").</summary>
        public Dictionary<string, float> Params = new();

        internal AbilitySpec DeepClone()
        {
            var clone = (AbilitySpec)MemberwiseClone();
            clone.Params = new Dictionary<string, float>(Params, Params.Comparer);
            clone.AnimationNames = (string[]?)AnimationNames?.Clone();
            clone.BoneTrails = (BoneTrailDef[]?)BoneTrails?.Clone();
            clone.Stages = (AttackStage[])Stages.Clone();
            for (int i = 0; i < clone.Stages.Length; i++)
            {
                clone.Stages[i].HitboxEvents = (HitboxEvent[]?)Stages[i].HitboxEvents?.Clone()
                    ?? System.Array.Empty<HitboxEvent>();
                clone.Stages[i].BoneTrails = (BoneTrailDef[]?)Stages[i].BoneTrails?.Clone();
            }
            return clone;
        }
    }
}
