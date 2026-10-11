using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SlopArena.Shared;

public enum CharacterCookProfile : byte
{
    Workshop = 0,
    TrustedBuiltIn = 1,
}

public sealed class CharacterCompileResult
{
    public CookedCharacterPackage? CookedPackage { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == CharacterDiagnosticSeverity.Error);

    public CharacterCompileResult(CookedCharacterPackage? cookedPackage, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        CookedPackage = cookedPackage;
        Diagnostics = new System.Collections.ObjectModel.ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
    }
}

public static class CharacterPackageCompiler
{
    public const string ChargedProjectileCapabilityId = "slop.ability.charged-projectile.v1";
    public const string ChargedProjectileCapabilityVersion = "1";
    public const string ChargedDirectionalDashCapabilityId = "slop.ability.charged-directional-dash.v1";
    public const string ChargedDirectionalDashCapabilityVersion = "1";
    internal const string RetiredWibouDashSlashCapabilityId = "slop.internal.wibou.dash-slash.v1";
    public const string TargetedLeapCapabilityId = "slop.ability.targeted-leap.v1";
    public const string TargetedLeapCapabilityVersion = "1";
    internal const string RetiredTargetedLeapCapabilityId = "slop.internal.bonk.targeted-jump-slam.v1";
    private const ushort ManifestSchemaVersion = 1;
    private const ushort AuthoringSchemaVersion = 3;
    private const ushort CookedSchemaVersion = 3;
    private const string RuntimeApiMin = "1.2.0";
    private const string RuntimeApiMax = "1.x";
    private const ushort MaxFixedHitstunTicks = 240;
    internal const float MaxStartupAcquisitionRange = 32f;
    internal const float MaxStartupAngleDegrees = 180f;
    internal const float MaxStartupPitchDegrees = 90f;
    internal const float MaxStartupRateDegreesPerSecond = 1440f;
    private static readonly string[] CanonicalSlots = CanonicalSlotProjection.All
        .Select(slot => slot.Id)
        .ToArray();
    private static readonly IReadOnlyList<string> CanonicalSlotIdsReadOnly = Array.AsReadOnly(CanonicalSlots);
    public static IReadOnlyList<string> CanonicalSlotIds => CanonicalSlotIdsReadOnly;
    private static readonly string[] TrustedCapabilities =
    {
        "slop.internal.fightguy.rising-dragon.v1",
        "slop.internal.fightguy.cyclone-kick.v1",
        "slop.internal.wibou.rising-slash.v1",
        "slop.internal.wibou.blade-flurry.v1",
        "slop.internal.manki.round-bomb.v1",
        "slop.internal.manki.jetpack-boost.v1",
        "slop.internal.manki.bazooka.v1",
        "slop.internal.manki.aerosol-inferno.v1",

    };
    internal static bool IsTrustedCapability(string id) => TrustedCapabilities.Contains(id);
    public static bool IsPublicCapability(string id, string version)
        => id == TargetedLeapCapabilityId && version == TargetedLeapCapabilityVersion
            || id == ChargedDirectionalDashCapabilityId && version == ChargedDirectionalDashCapabilityVersion
            || id == ChargedProjectileCapabilityId && version == ChargedProjectileCapabilityVersion;

    internal static bool IsRuntimeCapability(string id, string version)
        => IsPublicCapability(id, version) || version == "1" && IsTrustedCapability(id);
    private static bool RequiresRuntimeApi13(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is ArmorWindowOperationSource
            || operation is SpawnHitboxOperationSource hitbox && hitbox.Hitbox.FixedHitstunTicks > 0
            || operation is StartCapabilityOperationSource
            {
                Parameters: TargetedLeapCapabilityParameters leap
            } && leap.Hitbox.FixedHitstunTicks > 0)));
    private static bool RequiresRuntimeApi15(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource capability &&
            capability.CapabilityId == ChargedDirectionalDashCapabilityId)));

    public static CharacterCompileResult Compile(string packageManifestJson, string characterJson, CharacterCookProfile profile = CharacterCookProfile.Workshop)
    {
        var parsed = CharacterPackageSourceCodec.Load(packageManifestJson, characterJson);
        if (!parsed.IsValid)
            return new CharacterCompileResult(null, parsed.Diagnostics);
        return Compile(parsed.Source!, profile);
    }

    public static CharacterCompileResult Compile(CharacterPackageSource source, CharacterCookProfile profile = CharacterCookProfile.Workshop)
    {
        var diagnostics = new DiagnosticBag();
        if (source == null)
        {
            diagnostics.Error("schema.missing", "source", "Character package source is null.");
            return new CharacterCompileResult(null, diagnostics.ToList());
        }

        try
        {
            ValidateAndCook(source, profile, diagnostics, out var package);
            return new CharacterCompileResult(package, diagnostics.ToList());
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is FormatException || ex is OverflowException || ex is NullReferenceException || ex is ArgumentException)
        {
            diagnostics.Error("schema.invalid", "source", ex.Message);
            return new CharacterCompileResult(null, diagnostics.ToList());
        }
    }


    private static void ValidateAndCook(CharacterPackageSource source, CharacterCookProfile profile, DiagnosticBag d, out CookedCharacterPackage? package)
    {
        package = null;
        var m = source.Manifest;
        var c = source.Character;
        if (m == null || c == null) { d.Error("schema.missing", "source", "Manifest and character are required."); return; }
        if (m.ManifestSchemaVersion != ManifestSchemaVersion) d.Error("schema.unsupported", "manifest.manifestSchemaVersion", "Only manifest schema version 1 is supported.");
        if (c.AuthoringSchemaVersion != AuthoringSchemaVersion) d.Error("schema.unsupported", "character.authoringSchemaVersion", "Only authoring schema version 3 is supported.");
        ValidateId(m.PackageId, "manifest.packageId", d);
        if (string.IsNullOrWhiteSpace(m.Version) || !IsSemVer(m.Version)) d.Error("value.out-of-range", "manifest.version", "Version must be SemVer 2.0 text.");
        foreach (var field in new[] { (m.Creator, "manifest.creator"), (m.License, "manifest.license"), (m.Attribution, "manifest.attribution") }) if (string.IsNullOrWhiteSpace(field.Item1)) d.Error("value.out-of-range", field.Item2, "Value must be non-empty.");
        for (var i = 0; i < (m.Dependencies?.Count ?? 0); i++)
        {
            var dependency = m.Dependencies[i];
            ValidateId(dependency.PackageId, $"manifest.dependencies[{i}].packageId", d);
            if (string.IsNullOrWhiteSpace(dependency.Version) || string.IsNullOrWhiteSpace(dependency.CookedHash)) d.Error("value.out-of-range", $"manifest.dependencies[{i}]", "Dependency version and cooked hash are required.");
        }
        var capabilityMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var capabilityCount = 0;
        foreach (var requirement in c.CapabilityRequirements ?? System.Array.Empty<CapabilityRequirementSource>())
        {
            capabilityCount++;
            ValidateId(requirement.CapabilityId, "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", d);
            if (!capabilityMap.TryAdd(requirement.CapabilityId, requirement.CapabilityVersion)) d.Error("id.duplicate", "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", "Duplicate capability requirement.");
            string requirementPath = "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId";
            if (requirement.CapabilityId == RetiredTargetedLeapCapabilityId ||
                requirement.CapabilityId == RetiredWibouDashSlashCapabilityId)
                d.Error("capability.retired", requirementPath, "This capability has been retired.");
            else if (requirement.CapabilityId.StartsWith("slop.internal.", StringComparison.Ordinal))
            {
                if (profile == CharacterCookProfile.Workshop)
                    d.Error("capability.untrusted", requirementPath, "Trusted built-in capabilities are not allowed in Workshop profile.");
                else if (!IsTrustedCapability(requirement.CapabilityId) || requirement.CapabilityVersion != "1")
                    d.Error("capability.unknown", requirementPath, "Capability is not admitted by the trusted profile.");
            }
            else if (requirement.CapabilityId == TargetedLeapCapabilityId ||
                     requirement.CapabilityId == ChargedDirectionalDashCapabilityId ||
                     requirement.CapabilityId == ChargedProjectileCapabilityId)
            {
                string supportedVersion = requirement.CapabilityId == TargetedLeapCapabilityId
                    ? TargetedLeapCapabilityVersion : "1";
                if (requirement.CapabilityVersion != supportedVersion)
                    d.Error("capability.version-mismatch", requirementPath, "Capability version is not supported.");
            }
            else
            {
                d.Error("capability.unknown", requirementPath, "Capability is not admitted by this profile.");
            }
        }
        if (capabilityCount > CookedBudget.MaxCapabilityRequirements) d.Error("budget.exceeded", "character.capabilityRequirements", "Capability requirement budget exceeded.");
        ValidateFinite(c, d);
        if (c.ShieldRadius <= 0f || c.ShieldRadius <= c.CapsuleHeight * 0.5f)
            d.Error("value.out-of-range", "character.shieldRadius", "Shield radius must be positive and greater than half capsule height.");
        ValidateCaptureGeometry(c.CaptureGeometry, d);
        ValidateIds(c, d);
        var explicitSlots = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        for (var i = 0; i < (c.Slots?.Count ?? 0); i++)
        {
            var slot = c.Slots[i];
            if (!CanonicalSlots.Contains(slot.Id)) d.Error("id.invalid", "character.slots[" + i + "].id", "Unknown canonical slot ID.");
            else if (!explicitSlots.TryAdd(slot.Id, slot)) d.Error("id.duplicate", "character.slots[" + i + "].id", "Duplicate explicit slot.");
            ValidateSlot(slot, i, capabilityMap, c, d);
        }
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < (c.Aliases?.Count ?? 0); i++)
        {
            var alias = c.Aliases[i];
            if (!CanonicalSlots.Contains(alias.From) || !CanonicalSlots.Contains(alias.To)) d.Error("id.invalid", "character.aliases[" + i + "]", "Alias IDs must be canonical slot IDs.");
            if (explicitSlots.ContainsKey(alias.From)) d.Error("id.duplicate", "character.aliases[" + i + "].from", "Alias overwrites an explicit slot.");
            if (!aliases.TryAdd(alias.From, alias.To)) d.Error("id.duplicate", "character.aliases[" + i + "].from", "Duplicate alias.");
        }
        var resolved = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        foreach (var id in CanonicalSlots) ResolveSlot(id, explicitSlots, aliases, resolved, new HashSet<string>(StringComparer.Ordinal), d);
        var cookedSlots = new List<CookedSlotDefinition>(CanonicalSlots.Length);
        var stageCount = 0; var operationCount = 0; var hitboxCount = 0; var projectileCount = 0; var capabilityOperationCount = 0; var maxDuration = 0; var operationOrdinal = 0;
        for (var ordinal = 0; ordinal < CanonicalSlots.Length; ordinal++)
        {
            if (!resolved.TryGetValue(CanonicalSlots[ordinal], out var slot)) continue;
            ValidateSlideCarry(CanonicalSlots[ordinal], slot, FindSourceSlotIndex(c.Slots, slot.Id), d);
            var timeline = CookTimeline(slot.Timeline, d, ref stageCount, ref operationCount, ref hitboxCount, ref projectileCount, ref capabilityOperationCount, ref maxDuration, ref operationOrdinal);
            cookedSlots.Add(new CookedSlotDefinition(ordinal, CanonicalSlots[ordinal], ordinal >= 8, slot.Name, slot.Description, slot.IconId, slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove, slot.PreserveMomentumOnStart, timeline, slot.ChargePool == null ? null : new CookedChargePool(slot.ChargePool.MaxCharges, slot.ChargePool.RegenTicks), slot.AimMovement, slot.AimAnimationId, slot.AllowSlideCarry, slot.HitPresentationId));
        }
        if (resolved.Count != CanonicalSlots.Length) d.Error("reference.unresolved", "character.slots", "Not all canonical slots resolve.");
        if (d.HasErrors) return;
        var metadata = new CookedPackageMetadata(
            m.PackageId, m.Version, CookedSchemaVersion,
            c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                operation is StartCapabilityOperationSource { Parameters: ChargedProjectileCapabilityParameters }))) ? "1.6.0"
                : RequiresRuntimeApi15(c) ? "1.5.0"
                : c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                    operation is StartupAimCorrectionOperationSource))) ? "1.4.0"
                : RequiresRuntimeApi13(c) ? "1.3.0" : RuntimeApiMin, RuntimeApiMax);
        var definition = new CookedCharacterDefinition(
            c.DisplayName,
            c.Weight,
            CookMovement(c.Movement),
            CookPresentation(c.Presentation),
            c.CapsuleRadius,
            c.CapsuleHeight,
            c.HipHeight,
            c.HurtboxRadius,
            c.ShieldRadius,
            CookCaptureGeometry(c.CaptureGeometry),
            c.HurtboxCapsules.Select(x => new CookedHurtboxCapsule(x.StartX, x.StartY, x.StartZ, x.EndX, x.EndY, x.EndZ, x.Radius)).ToList(),
            c.HurtboxBoneDefs.Select(x => new CookedHurtboxBone(x.BoneId, x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius)).ToList(),
            c.AttachmentBoneIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            c.PresentationIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            c.CapabilityRequirements.OrderBy(x => x.CapabilityId, StringComparer.Ordinal).Select(x => new CookedCapabilityRequirement(x.CapabilityId, x.CapabilityVersion)).ToList(),
            cookedSlots);
        var budget = new CookedBudget(cookedSlots.Count, stageCount, operationCount, hitboxCount, projectileCount, capabilityOperationCount, maxDuration);
        var bytes = WriteCanonical(metadata, definition, budget);
        package = new CookedCharacterPackage(metadata, definition, budget, d.ToList(), bytes);
    }


    private static void ValidateSlot(CharacterSlotSource slot, int index, Dictionary<string, string> capabilities, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        ValidateId(slot.IconId, $"character.slots[{index}].iconId", d);
        if (!string.IsNullOrEmpty(slot.HitPresentationId))
        {
            ValidateId(slot.HitPresentationId, $"character.slots[{index}].hitPresentationId", d);
            if (!c.PresentationIds.Contains(slot.HitPresentationId, StringComparer.Ordinal))
                d.Error("reference.unresolved", $"character.slots[{index}].hitPresentationId", "Presentation ID is not declared.");
        }
        if (slot.Timeline.Stages.Count == 0 || slot.Timeline.Stages.Count > CookedBudget.MaxStagesPerTimeline) d.Error("budget.exceeded", $"character.slots[{index}].timeline.stages", "Timeline stage budget exceeded or empty.");
        if (slot.ChargePool != null)
        {
            if (slot.ChargePool.MaxCharges <= 0) d.Error("value.out-of-range", $"character.slots[{index}].chargePool.maxCharges", "Max charges must be positive.");
            if (slot.ChargePool.RegenTicks == 0) d.Error("value.out-of-range", $"character.slots[{index}].chargePool.regenTicks", "Regen ticks must be positive.");
        }
        var targetedLeapCount = slot.Timeline.Stages.Sum(stage =>
            stage.Operations.Count(operation =>
                operation is StartCapabilityOperationSource capability &&
                capability.CapabilityId == TargetedLeapCapabilityId));
        var chargedProjectiles = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == ChargedProjectileCapabilityId).ToArray();
        if (chargedProjectiles.Length > 0)
        {
            if (chargedProjectiles.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.ChargeAttack ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0)
                d.Error("capability.ambiguous", $"character.slots[{index}]",
                    "Charged projectile requires one chargeAttack lifecycle in one stage with IASA disabled.");
            foreach (var projectile in chargedProjectiles)
            {
                if (projectile.Tick != 0 || projectile.Parameters is not ChargedProjectileCapabilityParameters charge ||
                    charge.FireTriggerTick >= slot.Timeline.Stages[0].DurationTicks)
                    d.Error("value.out-of-range", $"character.slots[{index}].timeline",
                        "Charged projectile must start at tick zero and fire before its release stage ends.");
            }
            if (slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>
                operation is SpawnProjectileOperationSource or SpawnHitboxOperationSource or SetAimStateOperationSource
                    or SetVelocityOperationSource or ForwardLungeOperationSource ||
                operation is StartCapabilityOperationSource capability && capability.CapabilityId != ChargedProjectileCapabilityId))
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                    "Charged projectile cannot share a slot with competing motion, aim, projectile, or lifecycle operations.");
        }
        if (targetedLeapCount > 1)
            d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                "A slot may contain only one targeted-leap lifecycle.");
        var chargedDashCount = slot.Timeline.Stages.Sum(stage =>
            stage.Operations.Count(operation =>
                operation is StartCapabilityOperationSource capability &&
                capability.CapabilityId == ChargedDirectionalDashCapabilityId));
        if (chargedDashCount > 1)
            d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                "A slot may contain only one charged directional dash lifecycle.");
        if (chargedDashCount > 0 &&
            (slot.Behavior != AuthoringAbilityBehavior.DirectionalDash || slot.AimMode != AuthoringAimMode.GroundVector ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0))
            d.Error("capability.ambiguous", $"character.slots[{index}]",
                "Charged directional dash requires directionalDash, groundVector aim, and one stage with IASA disabled.");
        foreach (var stage in slot.Timeline.Stages)
        {
            if (stage.DurationTicks == 0 || stage.IasaTicks > stage.DurationTicks || stage.LandingLagTicks > stage.DurationTicks || stage.AutoCancelBeforeTicks > stage.DurationTicks || stage.AutoCancelAfterTicks > stage.DurationTicks) d.Error("value.out-of-range", "character.slots[" + index + "].timeline", "Stage timing is outside its duration.");
            if (float.IsNaN(stage.AttackRange) || float.IsInfinity(stage.AttackRange) || float.IsNaN(stage.WarpRange) || float.IsInfinity(stage.WarpRange) || float.IsNaN(stage.TrackingStrength) || float.IsInfinity(stage.TrackingStrength))
                d.Error("value.non-finite", $"character.slots[{index}].timeline", "Targeting metadata must be finite.");
            if (stage.AttackRange < 0f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.attackRange", "Attack range must be non-negative.");
            if (stage.WarpRange < 0f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.warpRange", "Warp range must be non-negative.");
            if (stage.TrackingStrength < 0f || stage.TrackingStrength > 1f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.trackingStrength", "Tracking strength must be between 0 and 1.");
            if (stage.Operations.Count > CookedBudget.MaxOperationsPerStage) d.Error("budget.exceeded", "character.slots[" + index + "].timeline", "Stage operation budget exceeded.");
            foreach (var lunge in stage.Operations.OfType<ForwardLungeOperationSource>()
                .Where(operation => operation.StopInAttackRange))
            {
                if (!stage.Operations.Any(operation =>
                    operation is SpawnHitboxOperationSource hitbox && hitbox.Tick > lunge.Tick))
                    d.Error("operation.forward-lunge.no-subsequent-hitbox",
                        $"character.slots[{index}].timeline",
                        "A forward lunge that stops in attack range requires a subsequent spawnHitbox in the same stage.");
            }

            foreach (var id in stage.AnimationIds) { ValidateId(id, "character.animationId", d); if (!IsKnownAnimation(id, c)) d.Error("reference.unresolved", "character.animationId", "Animation ID is not declared."); }
            foreach (var operation in stage.Operations)
            {
                if (operation.Tick >= stage.DurationTicks) d.Error("value.out-of-range", "character.operation.tick", "Operation tick must be within its stage.");
                if (operation is ForwardLungeOperationSource lunge &&
                    (int)lunge.Tick + lunge.DurationTicks > stage.DurationTicks)
                    d.Error("value.out-of-range", "character.forwardLunge.durationTicks", "Forward lunge must end within its stage.");
                if (operation is GravityWindowOperationSource gravity &&
                    (int)gravity.Tick + gravity.DurationTicks > stage.DurationTicks)
                    d.Error("value.out-of-range", "character.gravityWindow.durationTicks", "Gravity window must end within its stage.");
                if (operation is ArmorWindowOperationSource armorWindow &&
                    (int)armorWindow.Tick + armorWindow.DurationTicks > stage.DurationTicks)
                    d.Error("value.out-of-range", "character.armorWindow.durationTicks",
                        "Armor window must end within its stage.");
                if (operation is StartCapabilityOperationSource capability)
                {
                    if (!capabilities.TryGetValue(capability.CapabilityId, out var version)) d.Error("capability.unknown", "character.operation.capabilityId", "Capability is not declared.");
                    else if (version != capability.CapabilityVersion) d.Error("capability.version-mismatch", "character.operation.capabilityVersion", "Capability version does not match its declaration.");
                }
                if (operation is StartCapabilityOperationSource chargedDash &&
                    chargedDash.Parameters is ChargedDirectionalDashCapabilityParameters dash)
                {
                    if (operation.Tick != 0 || stage != slot.Timeline.Stages[0])
                        d.Error("value.out-of-range", "character.operation.tick", "Charged dash lifecycle must start at tick zero in the first stage.");
                    if (stage.Operations.Any(other => other != operation &&
                        (other is ForwardLungeOperationSource or SetVelocityOperationSource or SpawnHitboxOperationSource
                            or StartCapabilityOperationSource or StartupAimCorrectionOperationSource)))
                        d.Error("capability.ambiguous", "character.operation.parameters",
                            "Charged dash cannot share its stage with another lifecycle, startup correction, motion, or extra hitboxes.");
                    double maxTravelTicks = Math.Ceiling(dash.MaxDistance / (dash.DashSpeed / 60d));
                    if (maxTravelTicks + dash.RecoveryTicks > stage.DurationTicks ||
                        dash.FinisherSeekTick + dash.FinisherLeadTicks + dash.RecoveryTicks > stage.DurationTicks)
                        d.Error("value.out-of-range", "character.operation.parameters.recoveryTicks",
                            "Charged dash travel and finisher recovery must fit within its stage.");
                }
                if (operation is StartCapabilityOperationSource targetedLeap &&
                    targetedLeap.Parameters is TargetedLeapCapabilityParameters leap &&
                    (int)leap.LandingSeekTick + leap.RecoveryTicks > stage.DurationTicks)
                    d.Error("value.out-of-range", "character.operation.parameters.recoveryTicks",
                        "Landing seek and recovery must fit within the authored timeline duration.");
                ValidateOperation(operation, c, d);
            }
        }
        ValidateStartupAimCorrection(slot, index, d);
    }
    private static void ValidateStartupAimCorrection(CharacterSlotSource slot, int slotIndex, DiagnosticBag d)
    {
        string timelinePath = $"character.slots[{slotIndex}].timeline";
        int windowCount = 0;
        int stageStart = 0;
        var commitments = new List<int>();
        var windows = new List<(int StageIndex, int StageStart, StartupAimCorrectionOperationSource Operation)>();
        for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
        {
            var stage = slot.Timeline.Stages[stageIndex];
            foreach (var operation in stage.Operations)
            {
                if (operation is StartupAimCorrectionOperationSource correction)
                {
                    windowCount++;
                    windows.Add((stageIndex, stageStart, correction));
                }
                else if (IsStartupCommitment(operation))
                {
                    commitments.Add(stageStart + operation.Tick);
                }
            }
            stageStart += stage.DurationTicks;
        }

        if (windowCount > 1)
            d.Error("operation.ambiguous", timelinePath,
                "A timeline may contain only one startup aim-correction window.");

        foreach (var (stageIndex, start, correction) in windows)
        {
            string path = $"{timelinePath}.stages[{stageIndex}].operations.startupAimCorrection";
            ValidateFiniteValues(new[]
            {
                correction.AcquisitionRange, correction.AcquisitionHalfAngleDegrees,
                correction.MaxYawDegrees, correction.MaxPitchDegrees,
                correction.YawDegreesPerSecond, correction.PitchDegreesPerSecond,
            }, path, d);
            if (correction.EndTick <= correction.Tick || correction.EndTick > slot.Timeline.Stages[stageIndex].DurationTicks)
                d.Error("value.out-of-range", path + ".endTick", "End tick must be exclusive, after the operation tick, and inside the stage.");
            if (correction.AcquisitionRange <= 0f || correction.AcquisitionRange > MaxStartupAcquisitionRange)
                d.Error("value.out-of-range", path + ".acquisitionRange", $"Acquisition range must be greater than zero and at most {MaxStartupAcquisitionRange} meters.");
            if (correction.AcquisitionHalfAngleDegrees <= 0f || correction.AcquisitionHalfAngleDegrees > MaxStartupAngleDegrees)
                d.Error("value.out-of-range", path + ".acquisitionHalfAngleDegrees", $"Acquisition half-angle must be greater than zero and at most {MaxStartupAngleDegrees} degrees.");
            if (correction.MaxYawDegrees < 0f || correction.MaxYawDegrees > MaxStartupAngleDegrees)
                d.Error("value.out-of-range", path + ".maxYawDegrees", $"Maximum yaw must be between zero and {MaxStartupAngleDegrees} degrees.");
            if (correction.MaxPitchDegrees < 0f || correction.MaxPitchDegrees > MaxStartupPitchDegrees)
                d.Error("value.out-of-range", path + ".maxPitchDegrees", $"Maximum pitch must be between zero and {MaxStartupPitchDegrees} degrees.");
            if (correction.YawDegreesPerSecond < 0f || correction.YawDegreesPerSecond > MaxStartupRateDegreesPerSecond ||
                correction.MaxYawDegrees > 0f && correction.YawDegreesPerSecond == 0f)
                d.Error("value.out-of-range", path + ".yawDegreesPerSecond", $"Yaw rate must be positive for nonzero yaw correction and at most {MaxStartupRateDegreesPerSecond} degrees per second.");
            if (correction.PitchDegreesPerSecond < 0f || correction.PitchDegreesPerSecond > MaxStartupRateDegreesPerSecond ||
                correction.MaxPitchDegrees > 0f && correction.PitchDegreesPerSecond == 0f)
                d.Error("value.out-of-range", path + ".pitchDegreesPerSecond", $"Pitch rate must be positive for nonzero pitch correction and at most {MaxStartupRateDegreesPerSecond} degrees per second.");

            int globalStart = start + correction.Tick;
            if (commitments.Any(commitment => commitment < globalStart))
                d.Error("operation.commitment-precedes-correction", path,
                    "Startup aim correction cannot follow a prior active or launch commitment.");
            int cutoff = start + correction.EndTick;
            int? nextCommitment = commitments.Where(commitment => commitment >= globalStart).Select(commitment => (int?)commitment).Min();
            if (nextCommitment.HasValue && cutoff > nextCommitment.Value)
                d.Error("operation.cutoff-after-commitment", path + ".endTick",
                    "Startup aim correction must end no later than the earliest contact, projectile, or directional launch commitment.");
        }
    }

    private static bool IsStartupCommitment(CharacterTimelineOperationSource operation)
        => operation is SpawnHitboxOperationSource or SpawnProjectileOperationSource or ForwardLungeOperationSource
            || operation is SetVelocityOperationSource velocity
                && (velocity.X != 0f || velocity.Z != 0f);
    private static int FindSourceSlotIndex(IReadOnlyList<CharacterSlotSource> slots, string id)
    {
        for (var i = 0; i < (slots?.Count ?? 0); i++)
            if (string.Equals(slots[i].Id, id, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static void ValidateSlideCarry(string canonicalId, CharacterSlotSource slot, int sourceIndex, DiagnosticBag d)
    {
        if (!slot.AllowSlideCarry || slot.Timeline?.Stages == null)
            return;

        var path = sourceIndex >= 0
            ? $"character.slots[{sourceIndex}].allowSlideCarry"
            : $"character.slots.{canonicalId}.allowSlideCarry";
        const string code = "slot.slide-carry.motion-conflict";
        if (canonicalId != "ground.1" && canonicalId != "ground.2"
            && canonicalId != "ground.3" && canonicalId != "ground.4")
            d.Error(code, path, "Slide carry is only valid on grounded canonical normals 1-4.");
        if (slot.AimMode != AuthoringAimMode.None)
            d.Error(code, path, "Slide carry cannot be enabled on an aimed slot.");
        if (slot.IsRecoveryMove)
            d.Error(code, path, "Slide carry cannot be enabled on a recovery move.");

        var hasPositiveWarp = false;
        var hasMotionOperation = false;
        foreach (var stage in slot.Timeline.Stages)
        {
            hasPositiveWarp |= stage.WarpRange > 0f;
            hasMotionOperation |= stage.Operations.Any(operation => operation is SetVelocityOperationSource
                or ForwardLungeOperationSource
                or SetAimStateOperationSource
                or StartCapabilityOperationSource);
        }
        if (hasPositiveWarp)
            d.Error(code, path, "Slide carry cannot be enabled on a slot with positive warp range.");
        if (hasMotionOperation)
            d.Error(code, path, "Slide carry cannot be enabled on a slot with motion-owning timeline operations.");
    }


    private static bool IsKnownAnimation(string id, CharacterAuthoringDocument c)
        => id.StartsWith("anim.", StringComparison.Ordinal);

    private static void ValidateOperation(CharacterTimelineOperationSource operation, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var expected = operation switch { SetVelocityOperationSource or ForwardLungeOperationSource => AuthoringUnit.MetersPerSecond, GravityWindowOperationSource => AuthoringUnit.Normalized, SpawnHitboxOperationSource => AuthoringUnit.Meters, SpawnProjectileOperationSource => AuthoringUnit.Meters, _ => AuthoringUnit.Ticks };
        if (operation.Unit != expected) d.Error("unit.unknown", "character.operation.unit", "Unit does not match operation contract.");
        switch (operation)
        {
            case SetVelocityOperationSource velocity:
                ValidateFiniteValues(new[] { velocity.X, velocity.Y, velocity.Z }, "character.operation", d);
                break;
            case ForwardLungeOperationSource lunge:
                ValidateFiniteValues(new[] { lunge.Speed }, "character.forwardLunge.speed", d);
                if (lunge.Speed <= 0f) d.Error("value.out-of-range", "character.forwardLunge.speed", "Speed must be greater than zero.");
                if (lunge.DurationTicks == 0) d.Error("value.out-of-range", "character.forwardLunge.durationTicks", "Duration must be greater than zero.");
                break;
            case GravityWindowOperationSource gravity:
                ValidateFiniteValues(new[] { gravity.GravityScale }, "character.gravityWindow.gravityScale", d);
                if (gravity.GravityScale < 0f || gravity.GravityScale > 1f)
                    d.Error("value.out-of-range", "character.gravityWindow.gravityScale", "Gravity scale must be between zero and one.");
                if (gravity.DurationTicks == 0)
                    d.Error("value.out-of-range", "character.gravityWindow.durationTicks", "Duration must be greater than zero.");
                break;
            case ArmorWindowOperationSource armorWindow:
                if (armorWindow.DurationTicks == 0)
                    d.Error("value.out-of-range", "character.armorWindow.durationTicks",
                        "Duration must be greater than zero.");
                break;
            case SpawnHitboxOperationSource hitbox:
                ValidateFiniteValues(new[] { hitbox.Hitbox.Radius, hitbox.Hitbox.OffsetX, hitbox.Hitbox.OffsetY, hitbox.Hitbox.OffsetZ, hitbox.Hitbox.EndOffsetX, hitbox.Hitbox.EndOffsetY, hitbox.Hitbox.EndOffsetZ, hitbox.Hitbox.Damage, hitbox.Hitbox.Angle, hitbox.Hitbox.BaseKnockback, hitbox.Hitbox.KnockbackGrowth }, "character.hitbox", d);
                ValidateNonNegative(hitbox.Hitbox.Radius, "character.hitbox.radius", d); ValidateNonNegative(hitbox.Hitbox.Damage, "character.hitbox.damage", d); ValidateAngle(hitbox.Hitbox.Angle, "character.hitbox.angle", d); ValidateNonNegative(hitbox.Hitbox.BaseKnockback, "character.hitbox.baseKnockback", d); ValidateNonNegative(hitbox.Hitbox.KnockbackGrowth, "character.hitbox.knockbackGrowth", d);
                if (hitbox.Hitbox.DurationTicks == 0) d.Error("value.out-of-range", "character.hitbox.durationTicks", "Duration must be greater than zero.");
                ValidateFixedHitstun(hitbox.Hitbox.FixedHitstunTicks, hitbox.Hitbox.StunTicks,
                    "character.hitbox.fixedHitstunTicks", d);
                ValidateBoneReference(hitbox.Hitbox.StartBoneId, c, "character.hitbox.startBoneId", d); ValidateBoneReference(hitbox.Hitbox.EndBoneId, c, "character.hitbox.endBoneId", d);
                break;
            case SpawnProjectileOperationSource projectile:
                ValidateFiniteValues(new[] { projectile.Projectile.LaunchOffsetX, projectile.Projectile.LaunchOffsetY, projectile.Projectile.LaunchOffsetZ, projectile.Projectile.Speed, projectile.Projectile.Gravity, projectile.Projectile.Radius, projectile.Projectile.Damage, projectile.Projectile.Angle, projectile.Projectile.BaseKnockback, projectile.Projectile.KnockbackGrowth, projectile.Projectile.YawOffsetDegrees }, "character.projectile", d);
                ValidateNonNegative(projectile.Projectile.Speed, "character.projectile.speed", d); ValidateNonNegative(projectile.Projectile.Radius, "character.projectile.radius", d); ValidateNonNegative(projectile.Projectile.Damage, "character.projectile.damage", d); ValidateAngle(projectile.Projectile.Angle, "character.projectile.angle", d); ValidateYaw(projectile.Projectile.YawOffsetDegrees, "character.projectile.yawOffsetDegrees", d);
                break;
            case EmitPresentationOperationSource presentation:
                ValidatePresentationPlacement(presentation.Placement, c, d);
                if (!c.PresentationIds.Contains(presentation.PresentationId, StringComparer.Ordinal))
                    d.Error("reference.unresolved", "character.operation.presentationId", "Presentation ID is not declared.");
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is TargetedLeapCapabilityParameters leap:
                ValidateTargetedLeap(leap, c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is ChargedDirectionalDashCapabilityParameters dash:
                ValidateChargedDirectionalDash(dash, c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is ChargedProjectileCapabilityParameters charge:
                if (charge.MaxChargeTicks == 0 || charge.Tier2Ticks == 0 ||
                    charge.Tier2Ticks >= charge.Tier3Ticks || charge.Tier3Ticks > charge.MaxChargeTicks)
                    d.Error("value.out-of-range", "character.operation.parameters.tier2Ticks",
                        "Charge thresholds must satisfy 0 < tier2Ticks < tier3Ticks <= maxChargeTicks.");
                ValidateFiniteValues(new[] { charge.Tier2Radius, charge.Tier3Radius }, "character.operation.parameters", d);
                if (charge.Projectile.Radius <= 0f || charge.Tier2Radius <= charge.Projectile.Radius ||
                    charge.Tier3Radius <= charge.Tier2Radius || charge.Projectile.Speed <= 0f ||
                    charge.Projectile.MaxFlightTicks == 0 || charge.FireTriggerTick == 0)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile",
                        "Tier radii must be positive and increasing; speed, flight lifetime, and fire startup must be positive.");
                ValidateOperation(new SpawnProjectileOperationSource(0, AuthoringUnit.Meters, charge.Projectile), c, d);
                break;
            case StartupAimCorrectionOperationSource:
                break;
        }
    }

    private static void ValidateBoneReference(string? id, CharacterAuthoringDocument c, string path, DiagnosticBag d)
    {
        if (id != null && !c.HurtboxBoneDefs.Any(x => x.BoneId == id) && !c.AttachmentBoneIds.Contains(id, StringComparer.Ordinal))
            d.Error("reference.unresolved", path, "Bone ID is not declared.");
    }
    private static void ValidatePresentationPlacement(PresentationPlacement placement, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        placement ??= new PresentationPlacement();
        ValidateFiniteValues(new[]
        {
            placement.LocalPositionX, placement.LocalPositionY, placement.LocalPositionZ,
            placement.LocalRotationX, placement.LocalRotationY, placement.LocalRotationZ,
            placement.LocalScaleX, placement.LocalScaleY, placement.LocalScaleZ,
        }, "character.operation.placement", d);
        if (placement.DurationTicks == 0)
            d.Error("value.out-of-range", "character.operation.placement.durationTicks", "Presentation duration must be greater than zero.");
        if (placement.LocalScaleX <= 0f || placement.LocalScaleY <= 0f || placement.LocalScaleZ <= 0f)
            d.Error("value.out-of-range", "character.operation.placement.scale", "Presentation scale components must be greater than zero.");
        if (placement.AttachmentMode != AuthoringPresentationAttachmentMode.World &&
            placement.AttachmentMode != AuthoringPresentationAttachmentMode.Bone)
            d.Error("value.out-of-range", "character.operation.placement.attachmentMode", "Unknown presentation attachment mode.");
        if (placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone &&
            (string.IsNullOrEmpty(placement.BoneId) ||
             (placement.BoneId != null && !c.HurtboxBoneDefs.Any(x => x.BoneId == placement.BoneId) &&
              !c.AttachmentBoneIds.Contains(placement.BoneId, StringComparer.Ordinal))))
            d.Error("reference.unresolved", "character.operation.placement.boneId", "Presentation bone must be a declared bone.");
    }
    private static void ValidateChargedDirectionalDash(
        ChargedDirectionalDashCapabilityParameters x, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        if (x.MaxChargeTicks == 0 || x.Tier2Ticks == 0 ||
            x.Tier2Ticks >= x.Tier3Ticks || x.Tier3Ticks > x.MaxChargeTicks)
            d.Error("value.out-of-range", "character.operation.parameters.tier2Ticks",
                "Charge thresholds must satisfy 0 < tier2Ticks < tier3Ticks <= maxChargeTicks.");
        ValidateFiniteValues(new[] { x.MinDistance, x.MaxDistance, x.DashSpeed, x.Tier2Damage, x.Tier3Damage },
            "character.operation.parameters", d);
        if (x.MinDistance <= 0f || x.MaxDistance < x.MinDistance)
            d.Error("value.out-of-range", "character.operation.parameters.minDistance",
                "Distances must satisfy 0 < minDistance <= maxDistance.");
        if (x.DashSpeed <= 0f)
            d.Error("value.out-of-range", "character.operation.parameters.dashSpeed", "Dash speed must be positive.");
        if (x.FinisherLeadTicks == 0 || x.FinisherSeekTick == 0 || x.RecoveryTicks == 0)
            d.Error("value.out-of-range", "character.operation.parameters.finisherSeekTick",
                "Finisher seek, lead, and recovery must be positive.");
        if (x.Tier2Damage < x.FinisherHitbox.Damage || x.Tier3Damage < x.Tier2Damage)
            d.Error("value.out-of-range", "character.operation.parameters.tier2Damage",
                "Finisher and tier damage must be non-negative and nondecreasing.");
        ValidateCapabilityHitbox(x.TraversalHitbox, c, "traversalHitbox", d);
        if (x.TraversalHitbox.HitGroup != 0 || x.FinisherHitbox.HitGroup != 0)
            d.Error("value.out-of-range", "character.operation.parameters.hitGroup",
                "Traversal and finisher require independent hit histories (hitGroup 0).");
        ValidateCapabilityHitbox(x.FinisherHitbox, c, "finisherHitbox", d);
        if (x.FinisherHitbox.DurationTicks > (int)x.FinisherLeadTicks + x.RecoveryTicks)
            d.Error("value.out-of-range", "character.operation.parameters.finisherHitbox.durationTicks",
                "Finisher hitbox duration must fit within finisher lead and recovery.");
    }

    private static void ValidateCapabilityHitbox(HitboxSource x, CharacterAuthoringDocument c, string name, DiagnosticBag d)
    {
        string path = "character.operation.parameters." + name;
        ValidateFiniteValues(new[] { x.Radius, x.OffsetX, x.OffsetY, x.OffsetZ, x.EndOffsetX, x.EndOffsetY,
            x.EndOffsetZ, x.Damage, x.Angle, x.BaseKnockback, x.KnockbackGrowth }, path, d);
        ValidateNonNegative(x.Radius, path + ".radius", d);
        ValidateNonNegative(x.Damage, path + ".damage", d);
        ValidateAngle(x.Angle, path + ".angle", d);
        ValidateNonNegative(x.BaseKnockback, path + ".baseKnockback", d);
        ValidateNonNegative(x.KnockbackGrowth, path + ".knockbackGrowth", d);
        if (x.DurationTicks == 0) d.Error("value.out-of-range", path + ".durationTicks", "Duration must be positive.");
        ValidateFixedHitstun(x.FixedHitstunTicks, x.StunTicks, path + ".fixedHitstunTicks", d);
        if (x.Shape != AuthoringHitboxShape.Sphere && x.Shape != AuthoringHitboxShape.Capsule)
            d.Error("value.out-of-range", path + ".shape", "Unknown hitbox shape.");
        if (x.KnockbackDirection != AuthoringKnockbackDirection.AwayFromOwner &&
            x.KnockbackDirection != AuthoringKnockbackDirection.TowardOwner)
            d.Error("value.out-of-range", path + ".knockbackDirection", "Unknown knockback direction.");
        ValidateBoneReference(x.StartBoneId, c, path + ".startBoneId", d);
        ValidateBoneReference(x.EndBoneId, c, path + ".endBoneId", d);
    }


    private static void ValidateFiniteValues(IEnumerable<float> values, string path, DiagnosticBag d)
    {
        foreach (var value in values) if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", path, "Numeric value must be finite.");
    }

    private static void ValidateFixedHitstun(ushort fixedTicks, ushort stunGate, string path, DiagnosticBag d)
    {
        if (fixedTicks > MaxFixedHitstunTicks)
            d.Error("value.out-of-range", path, "Fixed hitstun must not exceed 240 ticks.");
        if (fixedTicks > 0 && stunGate == 0)
            d.Error("value.out-of-range", path, "Fixed hitstun requires a nonzero stun gate.");
    }
    private static void ValidateTargetedLeap(
        TargetedLeapCapabilityParameters parameters,
        CharacterAuthoringDocument character,
        DiagnosticBag d)
    {
        const string path = "character.operation.parameters";
        ValidateFiniteValues(new[] { parameters.MinRange, parameters.MaxRange, parameters.LaunchVerticalSpeed }, path, d);
        ValidateNonNegative(parameters.MinRange, path + ".minRange", d);
        ValidateNonNegative(parameters.MaxRange, path + ".maxRange", d);
        ValidateNonNegative(parameters.LaunchVerticalSpeed, path + ".launchVerticalSpeed", d);
        if (parameters.MinRange <= 0f)
            d.Error("value.out-of-range", path + ".minRange", "Minimum target range must be greater than zero.");
        if (parameters.MaxRange < parameters.MinRange)
            d.Error("value.out-of-range", path + ".maxRange", "Maximum range must not be below minimum range.");
        if (parameters.LaunchVerticalSpeed <= 0f)
            d.Error("value.out-of-range", path + ".launchVerticalSpeed", "Launch vertical speed must be greater than zero.");
        if (parameters.MaxFlightTicks == 0 || parameters.RecoveryTicks == 0)
            d.Error("value.out-of-range", path, "Flight and recovery durations must be positive.");

        var hitbox = parameters.Hitbox;
        if (hitbox == null)
        {
            d.Error("schema.missing", path + ".hitbox", "Landing hitbox is required.");
            return;
        }
        string hitboxPath = path + ".hitbox";
        ValidateFiniteValues(new[]
        {
            hitbox.Radius, hitbox.OffsetX, hitbox.OffsetY, hitbox.OffsetZ,
            hitbox.EndOffsetX, hitbox.EndOffsetY, hitbox.EndOffsetZ,
            hitbox.Damage, hitbox.Angle, hitbox.BaseKnockback, hitbox.KnockbackGrowth,
        }, hitboxPath, d);
        ValidateNonNegative(hitbox.Radius, hitboxPath + ".radius", d);
        ValidateNonNegative(hitbox.Damage, hitboxPath + ".damage", d);
        ValidateAngle(hitbox.Angle, hitboxPath + ".angle", d);
        ValidateNonNegative(hitbox.BaseKnockback, hitboxPath + ".baseKnockback", d);
        ValidateNonNegative(hitbox.KnockbackGrowth, hitboxPath + ".knockbackGrowth", d);
        if (hitbox.DurationTicks == 0 || hitbox.DurationTicks > parameters.RecoveryTicks)
            d.Error("value.out-of-range", hitboxPath + ".durationTicks", "Landing hitbox duration must be positive and fit within recovery.");
        ValidateFixedHitstun(hitbox.FixedHitstunTicks, hitbox.StunTicks,
            hitboxPath + ".fixedHitstunTicks", d);
        if (hitbox.Shape != AuthoringHitboxShape.Sphere && hitbox.Shape != AuthoringHitboxShape.Capsule)
            d.Error("value.out-of-range", hitboxPath + ".shape", "Unknown hitbox shape.");
        if (hitbox.KnockbackDirection != AuthoringKnockbackDirection.AwayFromOwner &&
            hitbox.KnockbackDirection != AuthoringKnockbackDirection.TowardOwner)
            d.Error("value.out-of-range", hitboxPath + ".knockbackDirection", "Unknown knockback direction.");
        ValidateBoneReference(hitbox.StartBoneId, character, hitboxPath + ".startBoneId", d);
        ValidateBoneReference(hitbox.EndBoneId, character, hitboxPath + ".endBoneId", d);
    }

    private static void ValidateIds(CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var seenHurtboxBones = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < c.HurtboxBoneDefs.Count; i++) { var id = c.HurtboxBoneDefs[i].BoneId; ValidateId(id, $"character.hurtboxBoneDefs[{i}].boneId", d); if (!seenHurtboxBones.Add(id)) d.Error("id.duplicate", $"character.hurtboxBoneDefs[{i}].boneId", "Duplicate bone ID."); }
        var seenAttachmentBones = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < c.AttachmentBoneIds.Count; i++) { var id = c.AttachmentBoneIds[i]; ValidateAttachmentId(id, $"character.attachmentBoneIds[{i}]", d); if (!seenAttachmentBones.Add(id)) d.Error("id.duplicate", $"character.attachmentBoneIds[{i}]", "Duplicate attachment bone ID."); }
        var seenPresentation = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < c.PresentationIds.Count; i++) { ValidateId(c.PresentationIds[i], $"character.presentationIds[{i}]", d); if (!seenPresentation.Add(c.PresentationIds[i])) d.Error("id.duplicate", $"character.presentationIds[{i}]", "Duplicate presentation ID."); }
        var standardAnimations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in new[] { c.Presentation.Idle, c.Presentation.Run, c.Presentation.Dash, c.Presentation.Jump, c.Presentation.Fall, c.Presentation.HitSmall, c.Presentation.HitMedium, c.Presentation.HitHard })
        {
            ValidateId(id, "character.presentation", d);
            if (!standardAnimations.Add(id)) d.Error("id.duplicate", "character.presentation", "Duplicate standard animation ID.");
        }
        if (!string.IsNullOrEmpty(c.Presentation.Tumble))
        {
            ValidateId(c.Presentation.Tumble, "character.presentation.tumble", d);
            if (!standardAnimations.Add(c.Presentation.Tumble)) d.Error("id.duplicate", "character.presentation", "Duplicate standard animation ID.");
        }
        foreach (var (id, field) in new[]
        {
            (c.Presentation.Crouch, "crouch"),
            (c.Presentation.Slide, "slide"),
            (c.Presentation.Shield, "shield"),
            (c.Presentation.Grab, "grab"),
            (c.Presentation.Grabbed, "grabbed"),
            (c.Presentation.ThrowForward, "throwForward"),
            (c.Presentation.AirDodge, "airDodge")
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            ValidateId(id, $"character.presentation.{field}", d);
            if (!standardAnimations.Add(id)) d.Error("id.duplicate", "character.presentation", "Duplicate standard animation ID.");
        }
        foreach (var stageId in c.PresentationIds) if (!PresentationUsed(stageId, c)) d.Warning("presentation.unused-id", "character.presentationIds", "Declared presentation ID is not referenced by an attack or timeline operation.");
    }

    private static bool PresentationUsed(string id, CharacterAuthoringDocument c)
        => c.Slots.Any(slot => slot.HitPresentationId == id)
            || c.Slots.SelectMany(s => s.Timeline.Stages).SelectMany(s => s.Operations).Any(op =>
            op is EmitPresentationOperationSource emit && emit.PresentationId == id ||
            op is StartCapabilityOperationSource capability && (capability.Parameters switch
            {
                MankiRoundBombCapabilityParameters p => p.ExplosionPresentationId == id,
                MankiJetpackBoostCapabilityParameters p => p.ExplosionPresentationId == id,
                MankiBazookaCapabilityParameters p => p.ExplosionPresentationId == id,
                _ => false,
            }));

    private static void ValidateExplosionPresentationId(TypedCapabilityParameters parameters, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        string id = parameters switch
        {
            MankiRoundBombCapabilityParameters x => x.ExplosionPresentationId,
            MankiJetpackBoostCapabilityParameters x => x.ExplosionPresentationId,
            MankiBazookaCapabilityParameters x => x.ExplosionPresentationId,
            _ => "",
        };
        if (string.IsNullOrEmpty(id)) return;
        if (!id.StartsWith("presentation.", StringComparison.Ordinal))
            d.Error("id.invalid", "character.operation.parameters.explosionPresentationId", "Explosion presentation ID must use the presentation. prefix.");
        else if (!c.PresentationIds.Contains(id, StringComparer.Ordinal))
            d.Error("reference.unresolved", "character.operation.parameters.explosionPresentationId", "Presentation ID is not declared.");
    }
    private static void ValidateFinite(CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var floats = new List<float> { c.Weight, c.CapsuleRadius, c.CapsuleHeight, c.HipHeight, c.HurtboxRadius, c.ShieldRadius, c.Movement.AirDodgeSpeed, c.Presentation.LandStartOffsetSeconds, c.Presentation.VisualScale, c.Presentation.HurtboxBoneScale, c.Presentation.ModelYOffset, c.Presentation.ModelSoleOffset };
        floats.AddRange(c.HurtboxCapsules.SelectMany(x => new[] { x.StartX, x.StartY, x.StartZ, x.EndX, x.EndY, x.EndZ, x.Radius }));
        floats.AddRange(c.HurtboxBoneDefs.SelectMany(x => new[] { x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius }));
        foreach (var value in floats) if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", "character", "Numeric value must be finite.");
        if (c.Movement.AirDodgeSpeed <= 0f) d.Error("value.out-of-range", "character.movement.airDodgeSpeed", "Air-dodge speed must be greater than zero.");
    }
    private static void ValidateCaptureGeometry(CharacterCaptureGeometrySource? geometry, DiagnosticBag d)
    {
        if (geometry == null || geometry.AttackerAnchor == null || geometry.VictimAnchor == null)
        {
            d.Error("schema.missing", "character.captureGeometry", "Capture geometry and both anchors are required.");
            return;
        }

        ValidateFiniteValues(
            new[]
            {
                geometry.Reach, geometry.Width, geometry.Height, geometry.OffsetY,
                geometry.AttackerAnchor.X, geometry.AttackerAnchor.Y, geometry.AttackerAnchor.Z,
                geometry.VictimAnchor.X, geometry.VictimAnchor.Y, geometry.VictimAnchor.Z
            },
            "character.captureGeometry",
            d);
        if (geometry.Reach <= 0f || geometry.Width <= 0f || geometry.Height <= 0f)
            d.Error("value.out-of-range", "character.captureGeometry", "Capture reach, width, and height must be greater than zero.");
    }

    private static CharacterSlotSource? ResolveSlot(string id, Dictionary<string, CharacterSlotSource> explicitSlots, Dictionary<string, string> aliases, Dictionary<string, CharacterSlotSource> resolved, HashSet<string> visiting, DiagnosticBag d)
    {
        if (resolved.TryGetValue(id, out var existing)) return existing;
        if (!visiting.Add(id)) { d.Error("alias.cycle", "character.aliases", "Alias cycle detected."); return null; }
        CharacterSlotSource? slot = null;
        if (explicitSlots.TryGetValue(id, out var explicitSlot)) slot = explicitSlot;
        else if (aliases.TryGetValue(id, out var target)) slot = ResolveSlot(target, explicitSlots, aliases, resolved, visiting, d);
        else d.Error("alias.missing-target", "character.slots." + id, "Canonical slot has no explicit definition or alias.");
        visiting.Remove(id);
        if (slot != null) resolved[id] = CloneSlot(slot);
        return slot;
    }

    private static CharacterSlotSource CloneSlot(CharacterSlotSource source)
        => source with
        {
            Timeline = new CharacterTimelineSource(
                source.Timeline.Stages.Select(stage => new CharacterStageSource(
                    stage.DurationTicks,
                    stage.IasaTicks,
                    stage.LandingLagTicks,
                    stage.AutoCancelBeforeTicks,
                    stage.AutoCancelAfterTicks,
                    stage.AnimationIds.ToList(),
                    stage.Operations.Select(CloneOperation).ToList(),
                    stage.AttackRange,
                    stage.WarpRange,
                    stage.UseTargetLock,
                    stage.RotateTowardTarget,
                    stage.TrackingStrength)).ToList())
        };

    private static CharacterTimelineOperationSource CloneOperation(CharacterTimelineOperationSource op)
        => op switch
        {
            SetVelocityOperationSource x => x with { },
            ForwardLungeOperationSource x => x with { },
            GravityWindowOperationSource x => x with { },
            ArmorWindowOperationSource x => x with { },
            SpawnHitboxOperationSource x => x with { Hitbox = x.Hitbox with { } },
            SpawnProjectileOperationSource x => x with { Projectile = x.Projectile with { } },
            SetAimStateOperationSource x => x with { },
            StartCapabilityOperationSource x => x with { Parameters = CloneParameters(x.Parameters) },
            EmitPresentationOperationSource x => x with { },
            StartupAimCorrectionOperationSource x => x with { },
            CompleteTimelineOperationSource x => x with { },
            _ => throw new InvalidDataException("Unknown operation.")
        };

    private static TypedCapabilityParameters CloneParameters(TypedCapabilityParameters p)
        => p switch
        {
            RisingDragonCapabilityParameters x => x with { },
            ChargedProjectileCapabilityParameters x => x with { Projectile = x.Projectile with { } },
            CycloneKickCapabilityParameters x => x with { },
            ChargedDirectionalDashCapabilityParameters x => x with
            {
                TraversalHitbox = x.TraversalHitbox with { },
                FinisherHitbox = x.FinisherHitbox with { }
            },
            WibouRisingSlashCapabilityParameters x => x with { },
            WibouBladeFlurryCapabilityParameters x => x with { },
            TargetedLeapCapabilityParameters x => x with { Hitbox = x.Hitbox with { } },
            MankiRoundBombCapabilityParameters x => x with { },
            MankiJetpackBoostCapabilityParameters x => x with { },
            MankiBazookaCapabilityParameters x => x with { },
            MankiAerosolInfernoCapabilityParameters x => x with { },
            _ => throw new InvalidDataException("Unknown capability parameters.")
        };

    private static CookedTimeline CookTimeline(CharacterTimelineSource source, DiagnosticBag d, ref int stages, ref int operations, ref int hitboxes, ref int projectiles, ref int capabilities, ref int maxDuration, ref int operationOrdinal)
    {
        var cookedStages = new List<CookedStage>();
        var duration = 0;
        var timelineOperations = 0;
        foreach (var stage in source.Stages)
        {
            stages++; duration += stage.DurationTicks; maxDuration = Math.Max(maxDuration, duration);
            var cookedOps = new List<CookedTimelineOperation>();
            foreach (var op in stage.Operations.OrderBy(x => x.Tick))
            {
                operations++;
                timelineOperations++;
                var cookedOperationOrdinal = operationOrdinal++;
                switch (op)
                {
                    case SetVelocityOperationSource x: cookedOps.Add(new CookedSetVelocityOperation(x.Tick, x.Unit, x.VelocityMode, x.X, x.Y, x.Z)); break;
                    case ForwardLungeOperationSource x: cookedOps.Add(new CookedForwardLungeOperation(x.Tick, x.Unit, x.Speed, x.DurationTicks, x.StopInAttackRange)); break;
                    case GravityWindowOperationSource x: cookedOps.Add(new CookedGravityWindowOperation(x.Tick, x.Unit, x.GravityScale, x.DurationTicks)); break;
                    case ArmorWindowOperationSource x: cookedOps.Add(new CookedArmorWindowOperation(x.Tick, x.Unit, x.DurationTicks)); break;
                    case SpawnHitboxOperationSource x: hitboxes++; cookedOps.Add(new CookedSpawnHitboxOperation(x.Tick, x.Unit, CookHitbox(x.Hitbox))); break;
                    case SpawnProjectileOperationSource x: projectiles++; cookedOps.Add(new CookedSpawnProjectileOperation(x.Tick, x.Unit, new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ, x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle, x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks, x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees))); break;
                    case SetAimStateOperationSource x: cookedOps.Add(new CookedSetAimStateOperation(x.Tick, x.Unit, x.AimState)); break;
                    case StartCapabilityOperationSource x:
                        capabilities++;
                        if (x.Parameters is TargetedLeapCapabilityParameters) hitboxes++;
                        else if (x.Parameters is ChargedDirectionalDashCapabilityParameters) hitboxes += 2;
                        else if (x.Parameters is ChargedProjectileCapabilityParameters) projectiles++;
                        cookedOps.Add(new CookedStartCapabilityOperation(x.Tick, x.Unit, x.CapabilityId, x.CapabilityVersion, CookParameters(x.Parameters)));
                        break;
                    case StartupAimCorrectionOperationSource x:
                        cookedOps.Add(new CookedStartupAimCorrectionOperation(
                            x.Tick, x.Unit, x.EndTick, x.AcquisitionRange,
                            x.AcquisitionHalfAngleDegrees, x.MaxYawDegrees, x.MaxPitchDegrees,
                            x.YawDegreesPerSecond, x.PitchDegreesPerSecond));
                        break;
                    case EmitPresentationOperationSource x: cookedOps.Add(new CookedEmitPresentationOperation(x.Tick, x.Unit, x.PresentationId, cookedOperationOrdinal, x.Placement)); break;
                    case CompleteTimelineOperationSource x: cookedOps.Add(new CookedCompleteTimelineOperation(x.Tick, x.Unit)); break;
                }
            }
            cookedStages.Add(new CookedStage(stage.DurationTicks, stage.IasaTicks, stage.LandingLagTicks, stage.AutoCancelBeforeTicks, stage.AutoCancelAfterTicks, stage.AnimationIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), cookedOps, stage.AttackRange, stage.WarpRange, stage.UseTargetLock, stage.RotateTowardTarget, stage.TrackingStrength));
        }
        if (timelineOperations > CookedBudget.MaxOperationsPerTimeline) d.Error("budget.exceeded", "character.timeline.operations", "Timeline operation budget exceeded.");
        return new CookedTimeline(cookedStages);
    }
    private static CookedHitbox CookHitbox(HitboxSource x)
        => new(x.Shape, x.Radius, x.OffsetX, x.OffsetY, x.OffsetZ, x.EndOffsetX, x.EndOffsetY,
            x.EndOffsetZ, x.StartBoneId, x.EndBoneId, x.Damage, x.Angle, x.BaseKnockback,
            x.KnockbackGrowth, x.StunTicks, x.DurationTicks, x.Interruptible, x.HitGroup,
            x.KnockbackDirection, x.FixedHitstunTicks);

    private static CookedCapabilityParameters CookParameters(TypedCapabilityParameters p) => p switch
    {
        ChargedProjectileCapabilityParameters x => new CookedChargedProjectileCapabilityParameters(
            x.MaxChargeTicks, x.Tier2Ticks, x.Tier3Ticks, x.FireTriggerTick, x.Tier2Radius, x.Tier3Radius,
            new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ,
                x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle,
                x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks,
                x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees)),
        RisingDragonCapabilityParameters x => new CookedRisingDragonCapabilityParameters(x.RiseSpeed, x.RiseTicks, x.RiseDelay),
        CycloneKickCapabilityParameters x => new CookedCycloneKickCapabilityParameters(x.ForwardSpeed, x.WindupTicks, x.HitboxEndTick, x.DurationTicks, x.BodyRadius, x.SideRadius, x.SideOffset, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.StunTicks, x.BodyY, x.SideY),
        ChargedDirectionalDashCapabilityParameters x => new CookedChargedDirectionalDashCapabilityParameters(
            x.MaxChargeTicks, x.Tier2Ticks, x.Tier3Ticks, x.MinDistance, x.MaxDistance, x.DashSpeed,
            x.FinisherLeadTicks, x.FinisherSeekTick, x.RecoveryTicks, x.Tier2Damage, x.Tier3Damage,
            CookHitbox(x.TraversalHitbox), CookHitbox(x.FinisherHitbox)),
        WibouRisingSlashCapabilityParameters x => new CookedWibouRisingSlashCapabilityParameters(x.RiseSpeed, x.RiseTicks, x.HomingRange, x.HomingSpeed),
        WibouBladeFlurryCapabilityParameters x => new CookedWibouBladeFlurryCapabilityParameters(x.ForwardSpeed, x.MoveTicks),
        TargetedLeapCapabilityParameters x => new CookedTargetedLeapCapabilityParameters(x.MaxAimTicks, x.MaxFlightTicks, x.MinRange, x.MaxRange, x.LaunchVerticalSpeed, x.LandingSeekTick, x.RecoveryTicks, CookHitbox(x.Hitbox)),
        MankiRoundBombCapabilityParameters x => new CookedMankiRoundBombCapabilityParameters(x.ThrowTriggerTick, x.MaxRange, x.LaunchAngle, x.Gravity, x.HitboxRadius, x.Damage, x.StunTicks, x.MaxFlightTicks, x.KbAngle, x.ExplosionDamage, x.ExplosionRadius, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionKbAngle, x.ExplosionPresentationId),
        MankiJetpackBoostCapabilityParameters x => new CookedMankiJetpackBoostCapabilityParameters(x.StartupTicks, x.VerticalSpeed, x.HorizontalSpeed, x.ExplosionRadius, x.ExplosionDamage, x.ExplosionKbAngle, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionPresentationId),
        MankiBazookaCapabilityParameters x => new CookedMankiBazookaCapabilityParameters(x.FireTriggerTick, x.ProjectileSpeed, x.HitboxRadius, x.Damage, x.Gravity, x.MaxFlightTicks, x.StunTicks, x.ExplosionRadius, x.KbAngle, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionKbAngle, x.CastDuration, x.RecoveryDuration, x.ExplosionPresentationId),
        MankiAerosolInfernoCapabilityParameters x => new CookedMankiAerosolInfernoCapabilityParameters(x.FireTriggerTick, x.FireDurationTicks, x.HitboxDurationTicks, x.HitboxRadius, x.OffsetY, x.OffsetZ, x.EndOffsetZ, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.StunTicks, x.HitGroup),
        _ => throw new InvalidDataException("Unknown capability parameters.")
    };

    private static CookedMovement CookMovement(CharacterMovementSource x) => new(x.RunSpeed, x.RunAccelerationA, x.RunAccelerationB, x.DashSpeed, x.AirDodgeSpeed, x.AirSpeedMax, x.AirAccelStick, x.AirAccelBase, x.JumpForce, x.ShortHopForce, x.AirJumpVMultiplier, x.AirJumpHMultiplier, x.Gravity, x.AirFloatGravity, x.DashDurationTicks, x.DashCooldownTicks, x.GroundFriction, x.AirFriction, x.MaxFallSpeed, x.FastFallSpeed, x.MaxJumps, x.JumpSquatTicks, x.FloatWindowTicks, x.RushTicks);
    private static CookedPresentation CookPresentation(CharacterPresentationSource x)
        => new(x.Idle, x.Run, x.Dash, x.Jump, x.Fall, x.HitSmall, x.HitMedium, x.HitHard,
            x.LandStartOffsetSeconds, x.ModelResourcePath, x.VisualScale, x.HurtboxBoneScale,
            x.ModelYOffset, x.ModelSoleOffset, x.AutoModelYOffset, x.Tumble, x.Crouch, x.Slide,
            x.Shield, x.Grab, x.Grabbed, x.ThrowForward, x.AirDodge);
    private static CookedCaptureGeometry CookCaptureGeometry(CharacterCaptureGeometrySource x)
        => new(x.Reach, x.Width, x.Height, x.OffsetY,
            new CaptureAnchor(x.AttackerAnchor.X, x.AttackerAnchor.Y, x.AttackerAnchor.Z),
            new CaptureAnchor(x.VictimAnchor.X, x.VictimAnchor.Y, x.VictimAnchor.Z));

    private static byte[] WriteCanonical(CookedPackageMetadata metadata, CookedCharacterDefinition definition, CookedBudget budget)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default, Indented = false }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("metadata"); writer.WriteStartObject(); writer.WriteString("packageId", metadata.PackageId); writer.WriteString("version", metadata.Version); writer.WriteNumber("cookedSchemaVersion", metadata.CookedSchemaVersion); writer.WritePropertyName("compatibility"); writer.WriteStartObject(); writer.WriteString("runtimeApiMin", metadata.RuntimeApiMin); writer.WriteString("runtimeApiMax", metadata.RuntimeApiMax); writer.WriteEndObject(); writer.WriteEndObject();
            writer.WritePropertyName("character"); writer.WriteStartObject(); writer.WriteString("displayName", definition.DisplayName); Number(writer, "weight", definition.Weight); WriteMovement(writer, definition.Movement); WritePresentation(writer, definition.Presentation); Number(writer, "capsuleRadius", definition.CapsuleRadius); Number(writer, "capsuleHeight", definition.CapsuleHeight); Number(writer, "hipHeight", definition.HipHeight); Number(writer, "hurtboxRadius", definition.HurtboxRadius); Number(writer, "shieldRadius", definition.ShieldRadius);
            WriteCaptureGeometry(writer, definition.CaptureGeometry);
            writer.WritePropertyName("hurtboxCapsules"); writer.WriteStartArray(); foreach (var x in definition.HurtboxCapsules) { writer.WriteStartObject(); Number(writer, "startX", x.StartX); Number(writer, "startY", x.StartY); Number(writer, "startZ", x.StartZ); Number(writer, "endX", x.EndX); Number(writer, "endY", x.EndY); Number(writer, "endZ", x.EndZ); Number(writer, "radius", x.Radius); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("hurtboxBoneDefs"); writer.WriteStartArray(); foreach (var x in definition.HurtboxBoneDefs.OrderBy(x => x.BoneId, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("boneId", x.BoneId); Number(writer, "offsetX", x.OffsetX); Number(writer, "offsetY", x.OffsetY); Number(writer, "offsetZ", x.OffsetZ); Number(writer, "radius", x.Radius); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("attachmentBoneIds"); writer.WriteStartArray(); foreach (var x in definition.AttachmentBoneIds) writer.WriteStringValue(x); writer.WriteEndArray();
            writer.WritePropertyName("presentationIds"); writer.WriteStartArray(); foreach (var x in definition.PresentationIds) writer.WriteStringValue(x); writer.WriteEndArray();
            writer.WritePropertyName("capabilityRequirements"); writer.WriteStartArray(); foreach (var x in definition.CapabilityRequirements) { writer.WriteStartObject(); writer.WriteString("capabilityId", x.CapabilityId); writer.WriteString("capabilityVersion", x.CapabilityVersion); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("slots"); writer.WriteStartArray(); foreach (var x in definition.Slots.OrderBy(x => x.Ordinal)) WriteSlot(writer, x); writer.WriteEndArray(); writer.WriteEndObject();
            writer.WritePropertyName("budget"); writer.WriteStartObject(); writer.WriteNumber("slotCount", budget.SlotCount); writer.WriteNumber("stageCount", budget.StageCount); writer.WriteNumber("operationCount", budget.OperationCount); writer.WriteNumber("hitboxCount", budget.HitboxCount); writer.WriteNumber("projectileCount", budget.ProjectileCount); writer.WriteNumber("capabilityCount", budget.CapabilityCount); writer.WriteNumber("maxTimelineDurationTicks", budget.MaxTimelineDurationTicks); writer.WriteEndObject(); writer.WriteEndObject(); writer.Flush();
        }
        return stream.ToArray();
    }

    private static void WriteMovement(Utf8JsonWriter w, CookedMovement x) { w.WritePropertyName("movement"); w.WriteStartObject(); Number(w, "runSpeed", x.RunSpeed); Number(w, "runAccelerationA", x.RunAccelerationA); Number(w, "runAccelerationB", x.RunAccelerationB); Number(w, "dashSpeed", x.DashSpeed); Number(w, "airDodgeSpeed", x.AirDodgeSpeed); Number(w, "airSpeedMax", x.AirSpeedMax); Number(w, "airAccelStick", x.AirAccelStick); Number(w, "airAccelBase", x.AirAccelBase); Number(w, "jumpForce", x.JumpForce); Number(w, "shortHopForce", x.ShortHopForce); Number(w, "airJumpVMultiplier", x.AirJumpVMultiplier); Number(w, "airJumpHMultiplier", x.AirJumpHMultiplier); Number(w, "gravity", x.Gravity); Number(w, "airFloatGravity", x.AirFloatGravity); w.WriteNumber("dashDurationTicks", x.DashDurationTicks); w.WriteNumber("dashCooldownTicks", x.DashCooldownTicks); Number(w, "groundFriction", x.GroundFriction); Number(w, "airFriction", x.AirFriction); Number(w, "maxFallSpeed", x.MaxFallSpeed); Number(w, "fastFallSpeed", x.FastFallSpeed); w.WriteNumber("maxJumps", x.MaxJumps); w.WriteNumber("jumpSquatTicks", x.JumpSquatTicks); w.WriteNumber("floatWindowTicks", x.FloatWindowTicks); w.WriteNumber("rushTicks", x.RushTicks); w.WriteEndObject(); }
    private static void WritePresentation(Utf8JsonWriter w, CookedPresentation x)
    {
        w.WritePropertyName("presentation");
        w.WriteStartObject();
        w.WriteString("idle", x.Idle);
        w.WriteString("run", x.Run);
        w.WriteString("dash", x.Dash);
        w.WriteString("jump", x.Jump);
        w.WriteString("fall", x.Fall);
        w.WriteString("hitSmall", x.HitSmall);
        w.WriteString("hitMedium", x.HitMedium);
        w.WriteString("hitHard", x.HitHard);
        if (!string.IsNullOrEmpty(x.Tumble)) w.WriteString("tumble", x.Tumble);
        if (!string.IsNullOrEmpty(x.Crouch)) w.WriteString("crouch", x.Crouch);
        if (!string.IsNullOrEmpty(x.Slide)) w.WriteString("slide", x.Slide);
        if (!string.IsNullOrEmpty(x.Shield)) w.WriteString("shield", x.Shield);
        if (!string.IsNullOrEmpty(x.Grab)) w.WriteString("grab", x.Grab);
        if (!string.IsNullOrEmpty(x.Grabbed)) w.WriteString("grabbed", x.Grabbed);
        if (!string.IsNullOrEmpty(x.ThrowForward)) w.WriteString("throwForward", x.ThrowForward);
        if (!string.IsNullOrEmpty(x.AirDodge)) w.WriteString("airDodge", x.AirDodge);
        Number(w, "landStartOffsetSeconds", x.LandStartOffsetSeconds);
        w.WriteString("modelResourcePath", x.ModelResourcePath);
        Number(w, "visualScale", x.VisualScale);
        Number(w, "hurtboxBoneScale", x.HurtboxBoneScale);
        Number(w, "modelYOffset", x.ModelYOffset);
        Number(w, "modelSoleOffset", x.ModelSoleOffset);
        w.WriteBoolean("autoModelYOffset", x.AutoModelYOffset);
        w.WriteEndObject();
    }

    private static void WriteCaptureGeometry(Utf8JsonWriter w, CookedCaptureGeometry x)
    {
        w.WritePropertyName("captureGeometry");
        w.WriteStartObject();
        Number(w, "reach", x.Reach);
        Number(w, "width", x.Width);
        Number(w, "height", x.Height);
        Number(w, "offsetY", x.OffsetY);
        WriteCaptureAnchor(w, "attackerAnchor", x.AttackerAnchor);
        WriteCaptureAnchor(w, "victimAnchor", x.VictimAnchor);
        w.WriteEndObject();
    }

    private static void WriteCaptureAnchor(Utf8JsonWriter w, string name, CaptureAnchor x)
    {
        w.WritePropertyName(name);
        w.WriteStartObject();
        Number(w, "x", x.X);
        Number(w, "y", x.Y);
        Number(w, "z", x.Z);
        w.WriteEndObject();
    }
    private static void WriteSlot(Utf8JsonWriter w, CookedSlotDefinition x)
    {
        w.WriteStartObject();
        w.WriteNumber("ordinal", x.Ordinal);
        w.WriteString("id", x.Id);
        w.WriteBoolean("isAir", x.IsAir);
        w.WriteString("name", x.Name);
        w.WriteString("description", x.Description);
        w.WriteString("iconId", x.IconId);
        w.WriteNumber("behavior", (byte)x.Behavior);
        w.WriteNumber("aimMode", (byte)x.AimMode);
        w.WriteNumber("aimMovement", (byte)x.AimMovement);
        if (x.AimAnimationId != null)
            w.WriteString("aimAnimationId", x.AimAnimationId);
        w.WriteNumber("cooldownTicks", x.CooldownTicks);
        w.WriteBoolean("isRecoveryMove", x.IsRecoveryMove);
        w.WriteBoolean("preserveMomentumOnStart", x.PreserveMomentumOnStart);
        w.WriteBoolean("allowSlideCarry", x.AllowSlideCarry);
        if (!string.IsNullOrEmpty(x.HitPresentationId)) w.WriteString("hitPresentationId", x.HitPresentationId);
        if (x.ChargePool == null) w.WriteNull("chargePool");
        else
        {
            w.WritePropertyName("chargePool");
            w.WriteStartObject();
            w.WriteNumber("maxCharges", x.ChargePool.MaxCharges);
            w.WriteNumber("regenTicks", x.ChargePool.RegenTicks);
            w.WriteEndObject();
        }
        w.WritePropertyName("timeline");
        w.WriteStartObject();
        w.WritePropertyName("stages");
        w.WriteStartArray();
        foreach (var stage in x.Timeline.Stages) WriteStage(w, stage);
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }
    private static void WriteStage(Utf8JsonWriter w, CookedStage x) { w.WriteStartObject(); w.WriteNumber("durationTicks", x.DurationTicks); w.WriteNumber("iasaTicks", x.IasaTicks); w.WriteNumber("landingLagTicks", x.LandingLagTicks); w.WriteNumber("autoCancelBeforeTicks", x.AutoCancelBeforeTicks); w.WriteNumber("autoCancelAfterTicks", x.AutoCancelAfterTicks); Number(w, "attackRange", x.AttackRange); Number(w, "warpRange", x.WarpRange); w.WriteBoolean("useTargetLock", x.UseTargetLock); w.WriteBoolean("rotateTowardTarget", x.RotateTowardTarget); Number(w, "trackingStrength", x.TrackingStrength); w.WritePropertyName("animationIds"); w.WriteStartArray(); foreach (var id in x.AnimationIds) w.WriteStringValue(id); w.WriteEndArray(); w.WritePropertyName("operations"); w.WriteStartArray(); foreach (var op in x.Operations) WriteOperation(w, op); w.WriteEndArray(); w.WriteEndObject(); }
    private static void WriteOperation(Utf8JsonWriter w, CookedTimelineOperation x)
    {
        w.WriteStartObject();
        w.WriteNumber("kind", (byte)x.Kind);
        w.WriteNumber("tick", x.Tick);
        w.WriteNumber("unit", (byte)x.Unit);
        switch (x)
        {
            case CookedSetVelocityOperation v:
                w.WriteNumber("velocityMode", (byte)v.VelocityMode);
                Number(w, "x", v.X);
                Number(w, "y", v.Y);
                Number(w, "z", v.Z);
                break;
            case CookedEmitPresentationOperation p:
                w.WriteNumber("operationIndex", p.OperationIndex);
                w.WriteString("presentationId", p.PresentationId);
                WritePlacement(w, p.Placement);
                break;
            case CookedSpawnHitboxOperation h:
                WriteHitbox(w, h.Hitbox);
                break;
            case CookedSpawnProjectileOperation p:
                WriteProjectile(w, p.Projectile);
                break;
            case CookedSetAimStateOperation a:
                w.WriteNumber("aimState", (byte)a.AimState);
                break;
            case CookedStartCapabilityOperation c:
                w.WriteString("capabilityId", c.CapabilityId);
                w.WriteString("capabilityVersion", c.CapabilityVersion);
                w.WritePropertyName("parameters");
                WriteParameters(w, c.Parameters);
                break;
            case CookedForwardLungeOperation l:
                Number(w, "speed", l.Speed);
                w.WriteNumber("durationTicks", l.DurationTicks);
                if (l.StopInAttackRange) w.WriteBoolean("stopInAttackRange", true);
                break;
            case CookedGravityWindowOperation gravity:
                Number(w, "gravityScale", gravity.GravityScale);
                w.WriteNumber("durationTicks", gravity.DurationTicks);
                break;
            case CookedArmorWindowOperation armor:
                w.WriteNumber("durationTicks", armor.DurationTicks);
                break;
            case CookedStartupAimCorrectionOperation correction:
                w.WriteNumber("endTick", correction.EndTick);
                Number(w, "acquisitionRange", correction.AcquisitionRange);
                Number(w, "acquisitionHalfAngleDegrees", correction.AcquisitionHalfAngleDegrees);
                Number(w, "maxYawDegrees", correction.MaxYawDegrees);
                Number(w, "maxPitchDegrees", correction.MaxPitchDegrees);
                Number(w, "yawDegreesPerSecond", correction.YawDegreesPerSecond);
                Number(w, "pitchDegreesPerSecond", correction.PitchDegreesPerSecond);
                break;
            case CookedCompleteTimelineOperation:
                break;
        }
        w.WriteEndObject();
    }
    private static void WritePlacement(Utf8JsonWriter w, PresentationPlacement x)
    {
        x ??= new PresentationPlacement();
        w.WritePropertyName("placement");
        w.WriteStartObject();
        w.WriteNumber("attachmentMode", (byte)x.AttachmentMode);
        if (x.BoneId == null) w.WriteNull("boneId"); else w.WriteString("boneId", x.BoneId);
        Number(w, "localPositionX", x.LocalPositionX);
        Number(w, "localPositionY", x.LocalPositionY);
        Number(w, "localPositionZ", x.LocalPositionZ);
        Number(w, "localRotationX", x.LocalRotationX);
        Number(w, "localRotationY", x.LocalRotationY);
        Number(w, "localRotationZ", x.LocalRotationZ);
        Number(w, "localScaleX", x.LocalScaleX);
        Number(w, "localScaleY", x.LocalScaleY);
        Number(w, "localScaleZ", x.LocalScaleZ);
        w.WriteNumber("durationTicks", x.DurationTicks);
        w.WriteEndObject();
    }
    private static void WriteHitbox(Utf8JsonWriter w, CookedHitbox x) { w.WritePropertyName("hitbox"); w.WriteStartObject(); w.WriteNumber("shape", (byte)x.Shape); Number(w, "radius", x.Radius); Number(w, "offsetX", x.OffsetX); Number(w, "offsetY", x.OffsetY); Number(w, "offsetZ", x.OffsetZ); Number(w, "endOffsetX", x.EndOffsetX); Number(w, "endOffsetY", x.EndOffsetY); Number(w, "endOffsetZ", x.EndOffsetZ); if (x.StartBoneId != null) w.WriteString("startBoneId", x.StartBoneId); else w.WriteNull("startBoneId"); if (x.EndBoneId != null) w.WriteString("endBoneId", x.EndBoneId); else w.WriteNull("endBoneId"); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("durationTicks", x.DurationTicks); w.WriteBoolean("interruptible", x.Interruptible); w.WriteNumber("hitGroup", x.HitGroup); w.WriteNumber("knockbackDirection", (byte)x.KnockbackDirection); if (x.FixedHitstunTicks > 0) w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks); w.WriteEndObject(); }
    private static void WriteHitboxObject(Utf8JsonWriter w, CookedHitbox x, string name)
    {
        w.WritePropertyName(name); w.WriteStartObject();
        w.WriteNumber("shape", (byte)x.Shape); Number(w, "radius", x.Radius);
        Number(w, "offsetX", x.OffsetX); Number(w, "offsetY", x.OffsetY); Number(w, "offsetZ", x.OffsetZ);
        Number(w, "endOffsetX", x.EndOffsetX); Number(w, "endOffsetY", x.EndOffsetY); Number(w, "endOffsetZ", x.EndOffsetZ);
        if (x.StartBoneId != null) w.WriteString("startBoneId", x.StartBoneId); else w.WriteNull("startBoneId");
        if (x.EndBoneId != null) w.WriteString("endBoneId", x.EndBoneId); else w.WriteNull("endBoneId");
        Number(w, "damage", x.Damage); Number(w, "angle", x.Angle);
        Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth);
        w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("durationTicks", x.DurationTicks);
        w.WriteBoolean("interruptible", x.Interruptible); w.WriteNumber("hitGroup", x.HitGroup);
        w.WriteNumber("knockbackDirection", (byte)x.KnockbackDirection);
        w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks); w.WriteEndObject();
    }
    private static void WriteProjectile(Utf8JsonWriter w, CookedProjectile x) { w.WritePropertyName("projectile"); w.WriteStartObject(); Number(w, "launchOffsetX", x.LaunchOffsetX); Number(w, "launchOffsetY", x.LaunchOffsetY); Number(w, "launchOffsetZ", x.LaunchOffsetZ); Number(w, "speed", x.Speed); Number(w, "gravity", x.Gravity); Number(w, "radius", x.Radius); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); Number(w, "yawOffsetDegrees", x.YawOffsetDegrees); w.WriteEndObject(); }
    private static void WriteParameters(Utf8JsonWriter w, CookedCapabilityParameters p)
    {
        w.WriteStartObject();
        switch (p)
        {
            case CookedChargedProjectileCapabilityParameters x:
                w.WriteNumber("maxChargeTicks", x.MaxChargeTicks); w.WriteNumber("tier2Ticks", x.Tier2Ticks);
                w.WriteNumber("tier3Ticks", x.Tier3Ticks); w.WriteNumber("fireTriggerTick", x.FireTriggerTick);
                Number(w, "tier2Radius", x.Tier2Radius); Number(w, "tier3Radius", x.Tier3Radius);
                WriteProjectile(w, x.Projectile); break;
            case CookedRisingDragonCapabilityParameters x: Number(w, "riseSpeed", x.RiseSpeed); w.WriteNumber("riseTicks", x.RiseTicks); w.WriteNumber("riseDelay", x.RiseDelay); break;
            case CookedCycloneKickCapabilityParameters x: Number(w, "forwardSpeed", x.ForwardSpeed); w.WriteNumber("windupTicks", x.WindupTicks); w.WriteNumber("hitboxEndTick", x.HitboxEndTick); w.WriteNumber("durationTicks", x.DurationTicks); Number(w, "bodyRadius", x.BodyRadius); Number(w, "sideRadius", x.SideRadius); Number(w, "sideOffset", x.SideOffset); Number(w, "damage", x.Damage); Number(w, "knockbackAngle", x.KnockbackAngle); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); Number(w, "bodyY", x.BodyY); Number(w, "sideY", x.SideY); break;
            case CookedChargedDirectionalDashCapabilityParameters x:
                w.WriteNumber("maxChargeTicks", x.MaxChargeTicks); w.WriteNumber("tier2Ticks", x.Tier2Ticks);
                w.WriteNumber("tier3Ticks", x.Tier3Ticks); Number(w, "minDistance", x.MinDistance);
                Number(w, "maxDistance", x.MaxDistance); Number(w, "dashSpeed", x.DashSpeed);
                w.WriteNumber("finisherLeadTicks", x.FinisherLeadTicks); w.WriteNumber("finisherSeekTick", x.FinisherSeekTick);
                w.WriteNumber("recoveryTicks", x.RecoveryTicks); Number(w, "tier2Damage", x.Tier2Damage);
                Number(w, "tier3Damage", x.Tier3Damage);
                WriteHitboxObject(w, x.TraversalHitbox, "traversalHitbox");
                WriteHitboxObject(w, x.FinisherHitbox, "finisherHitbox");
                break;
            case CookedWibouRisingSlashCapabilityParameters x: Number(w, "riseSpeed", x.RiseSpeed); w.WriteNumber("riseTicks", x.RiseTicks); Number(w, "homingRange", x.HomingRange); Number(w, "homingSpeed", x.HomingSpeed); break;
            case CookedWibouBladeFlurryCapabilityParameters x: Number(w, "forwardSpeed", x.ForwardSpeed); w.WriteNumber("moveTicks", x.MoveTicks); break;
            case CookedTargetedLeapCapabilityParameters x:
                w.WriteNumber("maxAimTicks", x.MaxAimTicks);
                w.WriteNumber("maxFlightTicks", x.MaxFlightTicks);
                Number(w, "minRange", x.MinRange);
                Number(w, "maxRange", x.MaxRange);
                Number(w, "launchVerticalSpeed", x.LaunchVerticalSpeed);
                w.WriteNumber("landingSeekTick", x.LandingSeekTick);
                w.WriteNumber("recoveryTicks", x.RecoveryTicks);
                WriteHitbox(w, x.Hitbox);
                break;
            case CookedMankiRoundBombCapabilityParameters x: w.WriteNumber("throwTriggerTick", x.ThrowTriggerTick); Number(w, "maxRange", x.MaxRange); Number(w, "launchAngle", x.LaunchAngle); Number(w, "gravity", x.Gravity); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "damage", x.Damage); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); Number(w, "kbAngle", x.KbAngle); Number(w, "explosionDamage", x.ExplosionDamage); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); Number(w, "explosionKbAngle", x.ExplosionKbAngle); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); break;
            case CookedMankiJetpackBoostCapabilityParameters x: w.WriteNumber("startupTicks", x.StartupTicks); Number(w, "verticalSpeed", x.VerticalSpeed); Number(w, "horizontalSpeed", x.HorizontalSpeed); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "explosionDamage", x.ExplosionDamage); Number(w, "explosionKbAngle", x.ExplosionKbAngle); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); break;
            case CookedMankiBazookaCapabilityParameters x: w.WriteNumber("fireTriggerTick", x.FireTriggerTick); Number(w, "projectileSpeed", x.ProjectileSpeed); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "damage", x.Damage); Number(w, "gravity", x.Gravity); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); w.WriteNumber("stunTicks", x.StunTicks); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "kbAngle", x.KbAngle); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); Number(w, "explosionKbAngle", x.ExplosionKbAngle); w.WriteNumber("castDuration", x.CastDuration); w.WriteNumber("recoveryDuration", x.RecoveryDuration); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); break;
            case CookedMankiAerosolInfernoCapabilityParameters x: w.WriteNumber("fireTriggerTick", x.FireTriggerTick); w.WriteNumber("fireDurationTicks", x.FireDurationTicks); w.WriteNumber("hitboxDurationTicks", x.HitboxDurationTicks); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "offsetY", x.OffsetY); Number(w, "offsetZ", x.OffsetZ); Number(w, "endOffsetZ", x.EndOffsetZ); Number(w, "damage", x.Damage); Number(w, "knockbackAngle", x.KnockbackAngle); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("hitGroup", x.HitGroup); break;
            default: throw new InvalidDataException("Unknown capability parameters.");
        }
        w.WriteEndObject();
    }
    private static void Number(Utf8JsonWriter w, string name, float value) => w.WriteNumber(name, value);
    private static void OptionalString(Utf8JsonWriter w, string name, string value)
    {
        if (!string.IsNullOrEmpty(value)) w.WriteString(name, value);
    }
    private static string EnumText(Enum x) => x.ToString();

    private sealed class DiagnosticBag
    {
        private readonly List<(int Order, CharacterDiagnostic Value)> _items = new(); private int _order;
        public bool HasErrors => _items.Any(x => x.Value.Severity == CharacterDiagnosticSeverity.Error);
        public void Error(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message)));
        public void Warning(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, code, path, message)));
        public List<CharacterDiagnostic> ToList() => _items.OrderBy(x => x.Order).ThenBy(x => x.Value.Code, StringComparer.Ordinal).Select(x => x.Value).ToList();
    }

    private static void ValidateAttachmentId(string value, string path, DiagnosticBag d) { if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] != '_' || value.Skip(1).Any(x => !(x >= 'a' && x <= 'z') && !(x >= '0' && x <= '9') && x != '.' && x != '-' && x != '_')) d.Error("id.invalid", path, "Attachment ID must be underscore-prefixed lowercase ASCII."); }
    private static void ValidateId(string value, string path, DiagnosticBag d) { if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] < 'a' || value[0] > 'z' || value.Any(x => !(x >= 'a' && x <= 'z') && !(x >= '0' && x <= '9') && x != '.' && x != '-')) d.Error("id.invalid", path, "ID must be lowercase ASCII and start with a letter."); }
    private static bool IsSemVer(string value)
    {
        var parts = value.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].All(char.IsDigit) && parts[1].All(char.IsDigit) && parts[2].All(char.IsDigit);
    }
    private static void ValidateNonNegative(float value, string path, DiagnosticBag d) { if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", path, "Value must be finite."); else if (value < 0) d.Error("value.out-of-range", path, "Value must be non-negative."); }
    private static void ValidateAngle(float value, string path, DiagnosticBag d) { if (value < -90 || value > 90) d.Error("value.out-of-range", path, "Angle must be between -90 and 90 degrees."); }
    private static void ValidateYaw(float value, string path, DiagnosticBag d) { if (value < -180 || value > 180) d.Error("value.out-of-range", path, "Yaw offset must be between -180 and 180 degrees."); }
}
