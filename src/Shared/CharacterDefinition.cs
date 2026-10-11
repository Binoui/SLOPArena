using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;

#nullable enable

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        None,
        Manki,
        FightGuy,
        Wibou,
        Bonk
    }

    [Serializable]
    public struct MovementStats
    {
        public float RunSpeed;              // m/s (replaces SprintSpeed)
        public float RunAccelerationA;      // m/s² stick coefficient (Run accel = A·|stick| + B)
        public float RunAccelerationB;      // m/s² base
        public float DashSpeed;
        public float AirDodgeSpeed;         // m/s
        public float AirSpeedMax;           // m/s
        public float AirAccelStick;         // m/s²
        public float AirAccelBase;          // m/s²
        public float JumpForce;
        public float ShortHopForce;         // m/s (replaces ShortHopVelocityMultiplier)
        public float AirJumpVMultiplier;    // factor on JumpForce
        public float AirJumpHMultiplier;    // factor on AirSpeedMax
        public float Gravity;
        public float AirFloatGravity;
        public ushort DashDurationTicks;
        public ushort DashCooldownTicks;
        public float GroundFriction;        // m/s² (linear)
        public float AirFriction;           // m/s² (linear)
        public float MaxFallSpeed;
        public float FastFallSpeed;         // m/s
        public byte MaxJumps;
        public ushort JumpSquatTicks;
        public ushort FloatWindowTicks;
        public ushort RushTicks;           // Rush window (ground dash-dance), ticks
    }

    public class CharacterDefinition
    {
        public CharacterClass Class;
        public string DisplayName = "";
        public MovementStats Movement;
        /// <summary>Static mass used by the ADR-0019 knockback formula.</summary>
        public float Weight = 100f;

        public float CapsuleRadius;
        public float CapsuleHeight;


        /// <summary>
        /// Y distance from character feet (ground contact) to Hips bone, in meters.
        /// Bridges the gap between capsule center (py) and the Hips-relative origin
        /// of baked bone data. Derived from abs(lowest bone Y) at idle frame 0.
        /// Manki: 0.50f, FightGuy: 0.82f.
        /// </summary>
        public float HipHeight;

        /// <summary>
        /// Convert a bone-local Y (Hips-relative from baked data) to world-space Y.
        /// Formula: capsuleCenterY - CapsuleHeight/2 + HipHeight + boneLocalY.
        /// When grounded (capsuleCenterY = CapsuleHeight/2), this reduces to HipHeight + boneLocalY.
        /// </summary>
        public float BoneYToWorldY(float capsuleCenterY, float boneLocalY)
            => capsuleCenterY - CapsuleHeight * 0.5f + HipHeight + boneLocalY;
        public float HurtboxRadius;
        /// <summary>World-space shield sphere radius in meters.</summary>
        public float ShieldRadius;
        /// <summary>Cooked character-specific forward capture volume and paired placement anchors.</summary>
        public CookedCaptureGeometry? CaptureGeometry;

        /// <summary>
        /// World-space offset from character position (legacy, used when no skeleton)
        /// </summary>
        public HurtboxCapsule[]? HurtboxCapsules;

        /// <summary>
        /// Bone-attached hurtboxes (ServerSkeleton-based). Replaces HurtboxCapsules when loaded.
        /// Each entry defines a sphere at a bone position with an offset.
        /// </summary>
        public HurtboxBoneDef[]? HurtboxBoneDefs;
        /// <summary>Path to the baked skeleton .bin file (pre-computed bone positions per frame).</summary>
        public string BakedDataPath = "";

        /// <summary>
        /// Shallow copy with HurtboxBoneDefs replaced (Ability Lab override, spec #119).
        /// The original definition is left untouched — registry defs may be shared.
        /// </summary>
        public CharacterDefinition WithHurtboxBoneDefs(HurtboxBoneDef[] defs)
        {
            var clone = (CharacterDefinition)MemberwiseClone();
            clone.HurtboxBoneDefs = defs;
            return clone;
        }

        /// <summary>Copy mutable match data while sharing immutable cooked values.</summary>
        internal CharacterDefinition DeepClone()
        {
            var clone = (CharacterDefinition)MemberwiseClone();
            clone.HurtboxCapsules = (HurtboxCapsule[]?)HurtboxCapsules?.Clone();
            clone.HurtboxBoneDefs = (HurtboxBoneDef[]?)HurtboxBoneDefs?.Clone();
            clone.ClipOverrides = (AnimationClipConfig[]?)ClipOverrides?.Clone();
            clone.CookedSlots = CookedSlots == null ? null
                : new ReadOnlyCollection<CookedSlotDefinition>(new List<CookedSlotDefinition>(CookedSlots));
            clone.Slot1 = Slot1?.DeepClone();
            clone.Slot2 = Slot2?.DeepClone();
            clone.Slot3 = Slot3?.DeepClone();
            clone.Slot4 = Slot4?.DeepClone();
            clone.Slot5 = Slot5?.DeepClone();
            clone.A = A?.DeepClone();
            clone.E = E?.DeepClone();
            clone.R = R?.DeepClone();
            clone.F = F?.DeepClone();
            clone.AirSlot1 = AirSlot1?.DeepClone();
            clone.AirSlot2 = AirSlot2?.DeepClone();
            clone.AirSlot3 = AirSlot3?.DeepClone();
            clone.AirSlot4 = AirSlot4?.DeepClone();
            clone.AirSlot5 = AirSlot5?.DeepClone();
            clone.AirA = AirA?.DeepClone();
            clone.AirE = AirE?.DeepClone();
            clone.AirR = AirR?.DeepClone();
            clone.AirF = AirF?.DeepClone();
            return clone;
        }

        /// <summary>Unity Resources path for the model prefab. E.g. "Characters/Manki"</summary>
        public string ModelResourcePath = "";
        /// <summary>
        /// Scale factor for baked bone positions. Mixamo GLBs are in cm (0.01),
        /// Blender/Maya exports with meters are 1.0. Default: 1.0
        /// </summary>
        public float HurtboxBoneScale = 1.0f;
        /// <summary>
        /// Y offset for the visual model relative to capsule center.
        /// Aligns the model's feet with the capsule bottom.
        /// Calculated as: -(footY * HurtboxBoneScale + CapsuleHeight * 0.5f)
        /// For Mixamo: ≈ -0.52 (Manki), adjust per character.
        /// 0 = model origin at capsule center (if model is already centered).
        /// </summary>
        public float ModelYOffset;
        /// <summary>
        /// Additional downward offset for the sole of the foot (below the lowest bone).
        /// Bones are inside the mesh; this accounts for sole thickness.
        /// Typical: 0.04-0.06m for humanoids, 0 for robots/mechs.
        /// </summary>
        public float ModelSoleOffset;
        /// <summary>
        /// If true, ModelYOffset is computed from the baked skeleton data
        /// (lowest bone position at idle frame 0) instead of using the manual value.
        /// </summary>
        public bool AutoModelYOffset;
        /// <summary>
        /// Scale factor from GLB skeleton units to world meters.
        /// Applied to both the visual model node and the baked bone positions
        /// so hurtboxes and visuals stay aligned.
        /// Manki: 1.0 (Mixamo cm→m handled by GLB import).
        /// FightGuy: 2.0 (custom export scale).
        /// </summary>
        public float VisualScale = 1.0f;

        // ── Animation catalog (defaults match Mixamo naming) ──

        /// <summary>Idle animation clip name. Default: "idle"</summary>
        public string IdleAnim = "idle";
        /// <summary>Run animation clip name. Default: "run"</summary>
        public string RunAnim = "run";
        /// <summary>Dash animation clip name. Default: "dash"</summary>
        public string DashAnim = "dash";
        /// <summary>Jump animation clip (BlendSpace1D position -1). Default: "jump"</summary>
        public string JumpAnim = "jump";
        /// <summary>Fall animation clip (BlendSpace1D position +1). Default: "fall"</summary>
        public string FallAnim = "fall";
        /// <summary>Small hit reaction clip. Default: "small_hit"</summary>
        public string HitSmallAnim = "hit_light";
        /// <summary>Medium hit reaction clip. Default: "medium_hit"</summary>
        public string HitMediumAnim = "hit_medium";
        /// <summary>Hard hit reaction clip. Default: "hard_hit"</summary>
        public string HitHardAnim = "hit_hard";
        /// <summary>Tumble animation clip. Legacy definitions default to "tumble".</summary>
        public string TumbleAnim = "tumble";
        /// <summary>Optional static low-posture semantic bindings; absent content remains upright.</summary>
        public string CrouchAnim = "";
        public string SlideAnim = "";
        /// <summary>Optional package-owned semantic defense animation bindings.</summary>
        public string ShieldAnim = "";
        public string GrabAnim = "";
        public string GrabbedAnim = "";
        public string ThrowForwardAnim = "";
        public string AirDodgeAnim = "";
        /// <summary>Landing uses JumpAnim clip with this start offset (seconds). Default: 0.49f</summary>
        public float LandStartOffset = 0.49f;
        /// <summary>Per-clip overrides for non-default timeline/loop settings.</summary>
        public AnimationClipConfig[]? ClipOverrides;

        /// <summary>Slot 2 (key "1" — the FG-normal tier, issue #117).</summary>
        public AbilitySpec? Slot1;
        public AbilitySpec? E;
        public AbilitySpec? R;
        public AbilitySpec? F;
        /// <summary>Slots 6-10 (keys "2"-"5" + "A"/Q) — issue #117 fills these.</summary>
        public AbilitySpec? Slot2;
        public AbilitySpec? Slot3;
        public AbilitySpec? Slot4;
        public AbilitySpec? Slot5;
        public AbilitySpec? A;
        // null = grounded-only; shared = works identically in the air; separate spec = distinct air move.
        public AbilitySpec? AirSlot1;
        public AbilitySpec? AirE;
        public AbilitySpec? AirR;
        public AbilitySpec? AirF;
        public AbilitySpec? AirSlot2;
        public AbilitySpec? AirSlot3;
        public AbilitySpec? AirSlot4;
        public AbilitySpec? AirSlot5;
        public AbilitySpec? AirA;
        // No constructor needed — class fields auto-default
        /// <summary>Immutable cooked timeline catalog used by migrated characters.</summary>
        public IReadOnlyList<CookedSlotDefinition>? CookedSlots;

        /// <summary>Resolve a cooked slot from the wire slot and airborne state.</summary>
        public CookedSlotDefinition? GetCookedSlotAbility(byte wireSlot, bool airborne)
        {
            if (CookedSlots == null) return null;
            int index = wireSlot switch
            {
                AbilitySlots.Slot1 => airborne ? 8 : 0,
                AbilitySlots.E => airborne ? 13 : 5,
                AbilitySlots.R => airborne ? 14 : 6,
                AbilitySlots.F => airborne ? 15 : 7,
                AbilitySlots.Slot2 => airborne ? 9 : 1,
                AbilitySlots.Slot3 => airborne ? 10 : 2,
                AbilitySlots.Slot4 => airborne ? 11 : 3,
                AbilitySlots.A => airborne ? 12 : 4,
                _ => -1,
            };
            return index >= 0 && index < CookedSlots.Count ? CookedSlots[index] : null;
        }
        /// <summary>Resolve the explicit aim movement policy for a wire slot.</summary>
        public AimMovementMode GetAimMovementMode(byte wireSlot, bool airborne)
        {
            if (wireSlot == AbilitySlots.None)
                return AimMovementMode.Fixed;
            var cooked = GetCookedSlotAbility(wireSlot, airborne);
            if (cooked != null)
                return cooked.AimMovement == AuthoringAimMovementMode.Mobile ? AimMovementMode.Mobile : AimMovementMode.Fixed;
            return GetSlotAbility(wireSlot - 1, airborne)?.AimMovement ?? AimMovementMode.Fixed;
        }



        /// <summary>
        /// Resolve the ability spec for a slot index (0-10) and airborne state (issue #117).
        /// Air semantics: an air spec is REQUIRED to fire a move in the air — null Air =
        /// grounded-only; shared = Air references the ground spec; distinct = separate spec.
        /// Returns null for data-less slots.
        /// </summary>
        public AbilitySpec? GetSlotAbility(int slotIndex, bool airborne = false) => (slotIndex, airborne) switch
        {
            (2, true) => AirSlot1,
            (3, true) => AirE,
            (4, true) => AirR,
            (5, true) => AirF,
            (6, true) => AirSlot2,
            (7, true) => AirSlot3,
            (8, true) => AirSlot4,
            (9, true) => AirSlot5,
            (10, true) => AirA,
            (2, _) => Slot1,
            (3, _) => E,
            (4, _) => R,
            (5, _) => F,
            (6, _) => Slot2,
            (7, _) => Slot3,
            (8, _) => Slot4,
            (9, _) => Slot5,
            (10, _) => A,
            _ => null
        };
    }

}
