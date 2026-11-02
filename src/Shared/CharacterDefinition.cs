using System;
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
        Bonk,
        // 5 was Nilus, retired: old match requests still carry it, so it must never parse again.
        Bashie = 6,
        // Working name until the fighter #6 salamander gunslinger has his own (2026-10-26).
        Salamander = 7,
        Nyx = 8
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
        public ushort RushTicks;           // Reserved authoring/cooked field; ground locomotion ignores it.
    }

    public sealed record CharacterDefinition
    {
        public CharacterClass Class { get; init; }
        public string DisplayName { get; init; } = "";
        public MovementStats Movement { get; init; }
        /// <summary>Static mass used by the ADR-0019 knockback formula.</summary>
        public float Weight { get; init; } = 100f;

        public float CapsuleRadius { get; init; }
        public float CapsuleHeight { get; init; }


        /// <summary>
        /// Y distance from character feet (ground contact) to Hips bone, in meters.
        /// The fixed hip pivot: attack-pose pitch and aim correction rotate around it.
        /// Baked points only use it when the bake has no
        /// <see cref="BakedAnimationData.HipsOffsetPointName"/> point; current cooks
        /// place the hips where the animation draws them.
        /// Manki: 0.50f, FightGuy: 0.82f.
        /// </summary>
        public float HipHeight { get; init; }

        /// <summary>
        /// World-space Y of a point <paramref name="boneLocalY"/> above the fixed hip pivot.
        /// Formula: capsuleCenterY - CapsuleHeight/2 + HipHeight + boneLocalY.
        /// When grounded (capsuleCenterY = CapsuleHeight/2), this reduces to HipHeight + boneLocalY.
        /// Use <see cref="HitboxGeometry.BakedPointToWorld"/> for baked points.
        /// </summary>
        public float BoneYToWorldY(float capsuleCenterY, float boneLocalY)
            => capsuleCenterY - CapsuleHeight * 0.5f + HipHeight + boneLocalY;
        public float HurtboxRadius { get; init; }
        /// <summary>World-space shield sphere radius in meters.</summary>
        public float ShieldRadius { get; init; }
        /// <summary>Cooked character-specific forward capture volume and paired placement anchors.</summary>
        public CookedCaptureGeometry? CaptureGeometry { get; init; }

        /// <summary>
        /// World-space offset from character position (legacy, used when no skeleton)
        /// </summary>
        private IReadOnlyList<HurtboxCapsule>? _hurtboxCapsules;
        public IReadOnlyList<HurtboxCapsule>? HurtboxCapsules
        {
            get => _hurtboxCapsules;
            init => _hurtboxCapsules = Copy(value);
        }

        /// <summary>
        /// Bone-attached hurtboxes (ServerSkeleton-based). Replaces HurtboxCapsules when loaded.
        /// Each entry defines a sphere or a capsule between named pose points.
        /// </summary>
        private IReadOnlyList<HurtboxBoneDef>? _hurtboxBoneDefs;
        public IReadOnlyList<HurtboxBoneDef>? HurtboxBoneDefs
        {
            get => _hurtboxBoneDefs;
            init => _hurtboxBoneDefs = Copy(value);
        }
        /// <summary>Path to the baked skeleton .bin file (pre-computed bone positions per frame).</summary>
        public string BakedDataPath { get; init; } = "";

        /// <summary>Unity Resources path for the model prefab. E.g. "Characters/Manki"</summary>
        public string ModelResourcePath { get; init; } = "";
        /// <summary>
        /// Body-bone attack reach retains the authored shrink. Fitted hurtboxes use scale 1.
        /// </summary>
        public float HurtboxBoneScale { get; init; } = 1.0f;
        /// <summary>
        /// Y offset for the visual model relative to capsule center.
        /// Aligns the model's feet with the capsule bottom.
        /// Calculated as: -(footY * HurtboxBoneScale + CapsuleHeight * 0.5f)
        /// For Mixamo: ≈ -0.52 (Manki), adjust per character.
        /// 0 = model origin at capsule center (if model is already centered).
        /// </summary>
        public float ModelYOffset { get; init; }
        /// <summary>
        /// Additional downward offset for the sole of the foot (below the lowest bone).
        /// Bones are inside the mesh; this accounts for sole thickness.
        /// Typical: 0.04-0.06m for humanoids, 0 for robots/mechs.
        /// </summary>
        public float ModelSoleOffset { get; init; }
        /// <summary>
        /// If true, ModelYOffset is computed from the baked skeleton data
        /// (lowest bone position at idle frame 0) instead of using the manual value.
        /// </summary>
        public bool AutoModelYOffset { get; init; }
        /// <summary>
        /// Scale factor from GLB skeleton units to world meters.
        /// Applied to both the visual model node and the baked bone positions
        /// so hurtboxes and visuals stay aligned.
        /// Manki: 1.0 (Mixamo cm→m handled by GLB import).
        /// FightGuy: 2.0 (custom export scale).
        /// </summary>
        public float VisualScale { get; init; } = 1.0f;

        // ── Animation catalog (defaults match Mixamo naming) ──

        /// <summary>Idle animation clip name. Default: "idle"</summary>
        public string IdleAnim { get; init; } = "idle";
        /// <summary>Run animation clip name. Default: "run"</summary>
        public string RunAnim { get; init; } = "run";
        /// <summary>Dash animation clip name. Default: "dash"</summary>
        public string DashAnim { get; init; } = "dash";
        /// <summary>Jump animation clip (BlendSpace1D position -1). Default: "jump"</summary>
        public string JumpAnim { get; init; } = "jump";
        /// <summary>Fall animation clip (BlendSpace1D position +1). Default: "fall"</summary>
        public string FallAnim { get; init; } = "fall";
        /// <summary>Small hit reaction clip. Default: "small_hit"</summary>
        public string HitSmallAnim { get; init; } = "hit_light";
        /// <summary>Medium hit reaction clip. Default: "medium_hit"</summary>
        public string HitMediumAnim { get; init; } = "hit_medium";
        /// <summary>Hard hit reaction clip. Default: "hard_hit"</summary>
        public string HitHardAnim { get; init; } = "hit_hard";
        /// <summary>Tumble animation clip. Legacy definitions default to "tumble".</summary>
        public string TumbleAnim { get; init; } = "tumble";
        /// <summary>Optional static low-posture semantic bindings; absent content remains upright.</summary>
        public string CrouchAnim { get; init; } = "";
        public string SlideAnim { get; init; } = "";
        public string WallJumpAnim { get; init; } = "";
        /// <summary>Optional package-owned semantic defense animation bindings.</summary>
        public string ShieldAnim { get; init; } = "";
        public string GrabAnim { get; init; } = "";
        public string GrabbedAnim { get; init; } = "";
        public string ThrowForwardAnim { get; init; } = "";
        public string AirDodgeAnim { get; init; } = "";
        /// <summary>The pose, and hurtbox, while in <see cref="ActionState.Shell"/> (ADR-0045).</summary>
        public string ShellAnim { get; init; } = "";
        public string FlyingAnim { get; init; } = "";
        public string MayhemLaughAnim { get; init; } = "";
        /// <summary>The shell stance (ADR-0045), or null for fighters without one.</summary>
        public CookedShellDefinition? Shell { get; init; }
        /// <summary>Landing uses JumpAnim clip with this start offset (seconds). Default: 0.49f</summary>
        public float LandStartOffset { get; init; } = 0.49f;
        /// <summary>Per-clip overrides for non-default timeline/loop settings.</summary>
        private IReadOnlyList<AnimationClipConfig>? _clipOverrides;
        public IReadOnlyList<AnimationClipConfig>? ClipOverrides
        {
            get => _clipOverrides;
            init => _clipOverrides = Copy(value);
        }

        private IReadOnlyList<CookedSlotDefinition>? _cookedSlots;
        public IReadOnlyList<CookedSlotDefinition>? CookedSlots
        {
            get => _cookedSlots;
            init => _cookedSlots = Copy(value);
        }

        public CookedSlotDefinition? GetCookedSlotAbility(byte wireSlot, bool airborne)
        {
            if (CookedSlots == null || !CanonicalSlotProjection.TryGet(wireSlot, airborne, out var address))
                return null;
            return address.Ordinal < CookedSlots.Count ? CookedSlots[address.Ordinal] : null;
        }

        /// <summary>The form this definition plays (ADR-0051): 0 is the package's top-level kit, 1 its alternateForm.</summary>
        public byte Form { get; private init; }
        private CharacterDefinition? _realForm;
        private CharacterDefinition? _alternateForm;
        /// <summary>The form-1 definition of a two-form fighter; null for fighters without forms.</summary>
        public CharacterDefinition? AlternateForm => (_realForm ?? this)._alternateForm;
        /// <summary>The definition of <paramref name="form"/>, from either form's definition. A fighter without forms
        /// always returns its one definition.</summary>
        public CharacterDefinition ForForm(byte form)
            => form == 1 && AlternateForm is { } alternate ? alternate : _realForm ?? this;

        /// <summary>Links a fighter's two forms and returns form 0. They share identity and body, and differ in
        /// movement, presentation roles and slots.</summary>
        public static CharacterDefinition WithForms(CharacterDefinition real, CharacterDefinition alternate)
        {
            var linkedReal = real with { Form = 0 };
            var linkedAlternate = alternate with { Form = 1 };
            linkedReal._realForm = null;
            linkedReal._alternateForm = linkedAlternate;
            linkedAlternate._realForm = linkedReal;
            linkedAlternate._alternateForm = null;
            return linkedReal;
        }

        private static IReadOnlyList<T>? Copy<T>(IReadOnlyList<T>? values)
            => values == null ? null : new ReadOnlyCollection<T>(new List<T>(values));

        // Definitions are admitted identities, including when their values happen to match.
        public bool Equals(CharacterDefinition? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
