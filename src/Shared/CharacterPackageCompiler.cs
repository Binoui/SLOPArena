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
    public const string AimedRecoveryCapabilityId = "slop.ability.aimed-recovery.v1";
    public const string AimedRecoveryCapabilityVersion = "1";
    public const string ChargedProjectileCapabilityId = "slop.ability.charged-projectile.v1";
    public const string ChargedProjectileCapabilityVersion = "1";
    public const string ChargedStreamCapabilityId = "slop.ability.charged-stream.v1";
    public const string ChargedStreamCapabilityVersion = "1";
    public const string FingerGunsCapabilityId = "slop.ability.finger-guns.v1";
    public const string FingerGunsCapabilityVersion = "1";
    public const string DashStrikeCapabilityId = "slop.ability.dash-strike.v1";
    public const string DashStrikeCapabilityVersion = "1";
    public const string FlashCutCapabilityId = "slop.ability.flash-cut.v1";
    public const string FlashCutCapabilityVersion = "1";
    public const string TargetedLeapCapabilityId = "slop.ability.targeted-leap.v2";
    public const string TargetedLeapCapabilityVersion = "2";
    public const string SteeredChargeCapabilityId = "slop.ability.steered-charge.v2";
    public const string SteeredChargeCapabilityVersion = "2";
    public const string ShellStanceCapabilityId = "slop.ability.shell-stance.v2";
    public const string ShellStanceCapabilityVersion = "1";
    public const string CannonballCapabilityId = "slop.ability.cannonball.v1";
    public const string CannonballCapabilityVersion = "1";
    public const string ThrustCapabilityId = "slop.ability.thrust.v1";
    public const string ThrustCapabilityVersion = "1";
    public const string TetherCapabilityId = "slop.ability.tether.v1";
    public const string TetherCapabilityVersion = "1";
    public const string CommandGrabCapabilityId = "slop.ability.command-grab.v1";
    public const string CommandGrabCapabilityVersion = "1";
    public const string FormsCapabilityId = "slop.ability.forms.v1";
    public const string FormsCapabilityVersion = "1";
    internal const float MaxSteeredChargeTurnDegreesPerSecond = 1440f;
    private const ushort ManifestSchemaVersion = 1;
    private const ushort AuthoringSchemaVersion = 4;
    private const ushort CookedSchemaVersion = 5;
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
    private static readonly IReadOnlyDictionary<string, string> TrustedCapabilities = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["slop.internal.fightguy.rising-dragon.v1"] = "1",
        ["slop.internal.fightguy.cyclone-kick.v1"] = "1",
        ["slop.internal.wibou.rising-slash.v1"] = "1",
        ["slop.internal.manki.proximity-mine.v1"] = "1",
        ["slop.internal.manki.jetpack-boost.v3"] = "1",
        ["slop.internal.manki.kamikaze.v1"] = "1",
        ["slop.internal.manki.bazooka.v2"] = "1",
        ["slop.internal.manki.bazooka.v3"] = "1",
    };
    internal static bool IsTrustedCapability(string id, string version)
        => TrustedCapabilities.TryGetValue(id, out var supported) && supported == version;
    public static bool IsPublicCapability(string id, string version)
        => id == AimedRecoveryCapabilityId && version == AimedRecoveryCapabilityVersion
            || id == TargetedLeapCapabilityId && version == TargetedLeapCapabilityVersion
            || id == DashStrikeCapabilityId && version == DashStrikeCapabilityVersion
            || id == FlashCutCapabilityId && version == FlashCutCapabilityVersion
            || id == ChargedProjectileCapabilityId && version == ChargedProjectileCapabilityVersion
            || id == ChargedStreamCapabilityId && version == ChargedStreamCapabilityVersion
            || id == FingerGunsCapabilityId && version == FingerGunsCapabilityVersion
            || id == SteeredChargeCapabilityId && version == SteeredChargeCapabilityVersion
            || id == ShellStanceCapabilityId && version == ShellStanceCapabilityVersion
            || id == ThrustCapabilityId && version == ThrustCapabilityVersion
            || id == TetherCapabilityId && version == TetherCapabilityVersion
            || id == CommandGrabCapabilityId && version == CommandGrabCapabilityVersion
            || id == CannonballCapabilityId && version == CannonballCapabilityVersion
            || id == FormsCapabilityId && version == FormsCapabilityVersion;

    internal static bool IsRuntimeCapability(string id, string version)
        => IsPublicCapability(id, version) || IsTrustedCapability(id, version);
    private static bool RequiresRuntimeApi122(CharacterAuthoringDocument c)
        => AllSlots(c).SelectMany(s => s.Timeline.Stages).SelectMany(s => s.Operations).Any(op =>
            op is StartCapabilityOperationSource { Parameters: SteeredChargeCapabilityParameters or TargetedLeapCapabilityParameters }
            || op is SpawnProjectileOperationSource { Projectile: var p } && (p.ChallengeTicks > 0 || p.Return?.Hit != null)
            || op is StartCapabilityOperationSource { Parameters: ChargedProjectileCapabilityParameters { Projectile.ChallengeTicks: > 0 }
                or ChargedStreamCapabilityParameters { Projectile.ChallengeTicks: > 0 }
                or FingerGunsCapabilityParameters { Projectile.ChallengeTicks: > 0 } });
    private static bool RequiresRuntimeApi13(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is ArmorWindowOperationSource
            || operation is SpawnHitboxOperationSource hitbox && hitbox.Hitbox.FixedHitstunTicks > 0
            || operation is StartCapabilityOperationSource
            {
                Parameters: TargetedLeapCapabilityParameters leap
            } && leap.Hitbox.FixedHitstunTicks > 0)));
    private static bool RequiresRuntimeApi18(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource capability &&
            (capability.CapabilityId == SteeredChargeCapabilityId || capability.CapabilityId == ChargedStreamCapabilityId)
            || operation is SpawnProjectileOperationSource { Projectile.Return: not null })));
    /// <summary>The shell stance, tuck moves, thrust and vertical-only velocity (ADR-0045).</summary>
    private static bool RequiresRuntimeApi112(CharacterAuthoringDocument character)
        => character.Shell != null
            || character.Slots.Any(slot => slot.ShellPolicy != ShellPolicy.PopOut
                || slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                    operation is StartCapabilityOperationSource { CapabilityId: ThrustCapabilityId }
                        or SetVelocityOperationSource { VelocityMode: AuthoringVelocityMode.Vertical })));
    /// <summary>The shell's splash and ride, ride moves and the cannonball (ADR-0050).</summary>
    private static bool RequiresRuntimeApi114(CharacterAuthoringDocument character)
        => character.Shell != null
            || character.Slots.Any(slot => slot.ShellPolicy == ShellPolicy.Ride
                || slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                    operation is StartCapabilityOperationSource { CapabilityId: CannonballCapabilityId })));
    /// <summary>The tether (ADR-0048).</summary>
    private static bool RequiresRuntimeApi113(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource { CapabilityId: TetherCapabilityId })));
    /// <summary>Two forms and the swap (ADR-0051).</summary>
    private static bool RequiresRuntimeApi116(CharacterAuthoringDocument character)
        => character.AlternateForm != null
            || character.CapabilityRequirements.Any(requirement => requirement.CapabilityId == FormsCapabilityId);
    /// <summary>A hitbox's own hit presentation and a conditioned presentation (ADR-0053).</summary>
    private static bool RequiresRuntimeApi117(CharacterAuthoringDocument character)
        => AllSlots(character).Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is SpawnHitboxOperationSource { Hitbox.HitPresentationId: not null }
                or EmitPresentationOperationSource { Condition: not AuthoringPresentationCondition.Always })));
    /// <summary>Nyx's specials (ADR-0051): the tether's placed catch, homing and perching projectiles and the command grab.</summary>
    private static bool RequiresRuntimeApi118(CharacterAuthoringDocument character)
        => AllSlots(character).Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource { Parameters: TetherCapabilityParameters { AtTarget: not null } }
                or SpawnProjectileOperationSource { Projectile.Homing: not null }
                or SpawnProjectileOperationSource { Projectile.Perch: not null }
                or StartCapabilityOperationSource { CapabilityId: CommandGrabCapabilityId })));
    /// <summary>Nyx's round 2 (ADR-0051 sections 9 and 10): a perch's dive, peck or scatter and the placed catch's unmarked branch.</summary>
    private static bool RequiresRuntimeApi121(CharacterAuthoringDocument character)
        => AllSlots(character).Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource { Parameters: TetherCapabilityParameters { AtTarget.Unmarked: not null } }
            || operation is SpawnProjectileOperationSource { Projectile.Perch: { } perch }
                && ProjectilePerchContract.RequiresRuntimeApi121(ProjectilePerchContract.Cook(perch)))));
    /// <summary>A command grab with a level lunge, a hold stage, a lock release or a credited catch (ADR-0057).</summary>
    private static bool RequiresRuntimeApi120(CharacterAuthoringDocument character)
        => AllSlots(character).Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource { Parameters: CommandGrabCapabilityParameters grab }
                && ((CookedCommandGrabCapabilityParameters)CookParameters(grab)).NeedsRuntimeApi120)));
    private static bool RequiresRuntimeApi115(CharacterAuthoringDocument character)
        => character.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
            operation is StartCapabilityOperationSource capability &&
            capability.CapabilityId is DashStrikeCapabilityId or FlashCutCapabilityId)));

    private static bool FacingFilteredCapability(TypedCapabilityParameters parameters) => parameters switch
    {
        TargetedLeapCapabilityParameters x => x.Hitbox.RequiresFacingOwner,
        ThrustCapabilityParameters x => x.TrailHitbox?.RequiresFacingOwner == true,
        DashStrikeCapabilityParameters x => x.LightHitbox.RequiresFacingOwner || x.TipHitbox.RequiresFacingOwner,
        FlashCutCapabilityParameters x => x.CutHitbox.RequiresFacingOwner,
        SteeredChargeCapabilityParameters x => x.LanceHitbox.RequiresFacingOwner,
        CommandGrabCapabilityParameters x => x.Capture.RequiresFacingOwner,
        _ => false,
    };

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
        if (c.AuthoringSchemaVersion == 3) d.Error("schema.unsupported", "character.authoringSchemaVersion", "Authoring schema 3 numbers the normals 1, 3 and 4. Run `python3 tools/slot_renumber.py --packages-only` to move them to 1, 2 and 3.");
        else if (c.AuthoringSchemaVersion != AuthoringSchemaVersion) d.Error("schema.unsupported", "character.authoringSchemaVersion", "Only authoring schema version 4 is supported.");
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
            if (RetiredCapabilities.TryGetReason(requirement.CapabilityId, out var retiredReason))
                d.Error("capability.retired", requirementPath, retiredReason);
            else if (requirement.CapabilityId.StartsWith("slop.internal.", StringComparison.Ordinal))
            {
                if (profile == CharacterCookProfile.Workshop)
                    d.Error("capability.untrusted", requirementPath, "Trusted built-in capabilities are not allowed in Workshop profile.");
                else if (!IsTrustedCapability(requirement.CapabilityId, requirement.CapabilityVersion))
                    d.Error("capability.unknown", requirementPath, "Capability is not admitted by the trusted profile.");
            }
            else if (requirement.CapabilityId == AimedRecoveryCapabilityId ||
                     requirement.CapabilityId == TargetedLeapCapabilityId ||
                     requirement.CapabilityId == DashStrikeCapabilityId ||
                     requirement.CapabilityId == FlashCutCapabilityId ||
                     requirement.CapabilityId == ChargedProjectileCapabilityId ||
                     requirement.CapabilityId == ChargedStreamCapabilityId ||
                     requirement.CapabilityId == FingerGunsCapabilityId ||
                     requirement.CapabilityId == SteeredChargeCapabilityId ||
                     requirement.CapabilityId == ShellStanceCapabilityId ||
                     requirement.CapabilityId == ThrustCapabilityId ||
                     requirement.CapabilityId == TetherCapabilityId ||
                     requirement.CapabilityId == CommandGrabCapabilityId ||
                     requirement.CapabilityId == CannonballCapabilityId ||
                     requirement.CapabilityId == FormsCapabilityId)
            {
                string supportedVersion = requirement.CapabilityId == TargetedLeapCapabilityId
                    ? TargetedLeapCapabilityVersion : requirement.CapabilityId == SteeredChargeCapabilityId ? SteeredChargeCapabilityVersion : "1";
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
        ValidateShell(c, capabilityMap, d);
        ValidateAlternateForm(c, capabilityMap, d);
        var stageCount = 0; var operationCount = 0; var hitboxCount = 0; var projectileCount = 0; var capabilityOperationCount = 0; var maxDuration = 0; var operationOrdinal = 0;
        var cookedSlots = CookGrid(c, 0, d, out var resolved);
        CookedAlternateForm? alternateForm = null;
        if (c.AlternateForm is { } form)
        {
            var formDiagnostics = new DiagnosticBag();
            var formSlots = CookGrid(c with { Movement = form.Movement, Slots = form.Slots, Aliases = form.Aliases }, 1,
                formDiagnostics, out _);
            d.AddFrom(formDiagnostics, AlternateFormPath);
            alternateForm = new CookedAlternateForm(form.Id, form.DisplayName, CookMovement(form.Movement), form.Presentation, formSlots);
        }
        if (ShellPairingError(c.Shell?.Splash != null, c.Shell?.Ride != null,
                resolved.Select(pair => (pair.Key, pair.Value.ShellPolicy))) is { } pairing)
            d.Error("capability.ambiguous", "character.shell", pairing);
        if (d.HasErrors) return;
        foreach (var slot in resolved.Values)
            foreach (var op in slot.Timeline.Stages.SelectMany(s => s.Operations).OfType<StartCapabilityOperationSource>())
                if (op.Parameters is SteeredChargeCapabilityParameters or TargetedLeapCapabilityParameters
                    && BonkSpecialContract.Slot(slot, op) is { } slotError)
                    d.Error("capability.ambiguous", slot.Id, slotError);
        if (d.HasErrors) return;
        var metadata = new CookedPackageMetadata(
            m.PackageId, m.Version, CookedSchemaVersion,
            RequiresRuntimeApi122(c) ? "1.22.0"
            : RequiresRuntimeApi121(c) ? "1.21.0"
            : RequiresRuntimeApi120(c) ? "1.20.0"
            : c.Shell?.Splash?.Hitbox.RequiresFacingOwner == true || c.Shell?.Ride?.Wave.RequiresFacingOwner == true
            || AllSlots(c).Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(op =>
                op is SpawnHitboxOperationSource { Hitbox.RequiresFacingOwner: true }
                || op is StartCapabilityOperationSource capability && FacingFilteredCapability(capability.Parameters)))) ? "1.19.0"
            : RequiresRuntimeApi118(c) ? "1.18.0"
            : RequiresRuntimeApi117(c) ? "1.17.0"
            : RequiresRuntimeApi116(c) ? "1.16.0"
            : RequiresRuntimeApi115(c) ? "1.15.0"
            : RequiresRuntimeApi114(c) ? "1.14.0"
            : RequiresRuntimeApi113(c) ? "1.13.0"
            : RequiresRuntimeApi112(c) ? "1.12.0"
            : c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                operation is StartCapabilityOperationSource { CapabilityId: FingerGunsCapabilityId }
                    or StartCapabilityOperationSource { Parameters: AimedRecoveryCapabilityParameters { RiseSteerAcceleration: > 0f } }))) ? "1.11.0"
            : c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                operation is StartCapabilityOperationSource { Parameters: AimedRecoveryCapabilityParameters { Hop: not null } }))) ? "1.10.0"
            : c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                operation is StartCapabilityOperationSource { CapabilityId: AimedRecoveryCapabilityId }))) ? "1.9.0"
            : RequiresRuntimeApi18(c) ? "1.8.0"
            : c.Slots.Any(slot => slot.AimMovementSpeedScale != 1f) ? "1.7.0"
            : c.Slots.Any(slot => slot.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                operation is StartCapabilityOperationSource { Parameters: ChargedProjectileCapabilityParameters }))) ? "1.6.0"
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
            c.HurtboxBoneDefs.Select(x => new CookedHurtboxBone(x.BoneId, x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius, x.EndBoneId, x.EndOffsetX, x.EndOffsetY, x.EndOffsetZ)).ToList(),
            c.AttachmentBoneIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            c.PresentationIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            c.CapabilityRequirements.OrderBy(x => x.CapabilityId, StringComparer.Ordinal).Select(x => new CookedCapabilityRequirement(x.CapabilityId, x.CapabilityVersion)).ToList(),
            cookedSlots,
            c.Shell == null ? null : CookShell(c.Shell),
            alternateForm);
        if (FormsBoundsError(definition) is { } formsError)
        {
            d.Error("value.out-of-range", "character.alternateForm", formsError);
            return;
        }
        var budget = new CookedBudget(cookedSlots.Count + (alternateForm?.Slots.Count ?? 0), stageCount, operationCount, hitboxCount, projectileCount, capabilityOperationCount, maxDuration);
        var bytes = WriteCanonical(metadata, definition, budget);
        package = new CookedCharacterPackage(metadata, definition, budget, d.ToList(), bytes);

        List<CookedSlotDefinition> CookGrid(CharacterAuthoringDocument doc, byte form, DiagnosticBag bag,
            out Dictionary<string, CharacterSlotSource> resolved)
        {
            var explicitSlots = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
            for (var i = 0; i < (doc.Slots?.Count ?? 0); i++)
            {
                var slot = doc.Slots[i];
                if (!CanonicalSlots.Contains(slot.Id)) bag.Error("id.invalid", "character.slots[" + i + "].id", "Unknown canonical slot ID.");
                else if (!explicitSlots.TryAdd(slot.Id, slot)) bag.Error("id.duplicate", "character.slots[" + i + "].id", "Duplicate explicit slot.");
                ValidateSlot(slot, i, capabilityMap, doc, bag);
            }
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < (doc.Aliases?.Count ?? 0); i++)
            {
                var alias = doc.Aliases[i];
                if (!CanonicalSlots.Contains(alias.From) || !CanonicalSlots.Contains(alias.To)) bag.Error("id.invalid", "character.aliases[" + i + "]", "Alias IDs must be canonical slot IDs.");
                if (explicitSlots.ContainsKey(alias.From)) bag.Error("id.duplicate", "character.aliases[" + i + "].from", "Alias overwrites an explicit slot.");
                if (!aliases.TryAdd(alias.From, alias.To)) bag.Error("id.duplicate", "character.aliases[" + i + "].from", "Duplicate alias.");
            }
            resolved = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
            foreach (var id in CanonicalSlots) ResolveSlot(id, explicitSlots, aliases, resolved, new HashSet<string>(StringComparer.Ordinal), bag);
            var cookedSlots = new List<CookedSlotDefinition>(CanonicalSlots.Length);
            for (var ordinal = 0; ordinal < CanonicalSlots.Length; ordinal++)
            {
                if (!resolved.TryGetValue(CanonicalSlots[ordinal], out var slot)) continue;
                ValidateSlideCarry(CanonicalSlots[ordinal], slot, FindSourceSlotIndex(doc.Slots, slot.Id), bag);
                ValidateAimMovementSpeed(CanonicalSlots[ordinal], slot, FindSourceSlotIndex(doc.Slots, slot.Id), bag);
                ValidateShellPolicy(CanonicalSlots[ordinal], slot, FindSourceSlotIndex(doc.Slots, slot.Id), doc, bag);
                var timeline = CookTimeline(slot.Timeline, bag, ref stageCount, ref operationCount, ref hitboxCount, ref projectileCount, ref capabilityOperationCount, ref maxDuration, ref operationOrdinal);
                cookedSlots.Add(new CookedSlotDefinition(ordinal, CanonicalSlots[ordinal], ordinal >= CanonicalSlotProjection.VariantSlotCount, slot.Name, slot.Description, slot.IconId, slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove, slot.PreserveMomentumOnStart, timeline, slot.ChargePool == null ? null : new CookedChargePool(slot.ChargePool.MaxCharges, slot.ChargePool.RegenTicks), slot.AimMovement, slot.AimAnimationId, slot.AllowSlideCarry, slot.HitPresentationId, slot.AimMovementSpeedScale, slot.ShellPolicy, form));
            }
            if (resolved.Count != CanonicalSlots.Length) bag.Error("reference.unresolved", "character.slots", "Not all canonical slots resolve.");
            return cookedSlots;
        }
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
            if (slot.ChargePool.MaxCharges is < 1 or > 3) d.Error("value.out-of-range", $"character.slots[{index}].chargePool.maxCharges", "Max charges must be between one and three.");
            if (slot.ChargePool.RegenTicks == 0) d.Error("value.out-of-range", $"character.slots[{index}].chargePool.regenTicks", "Regen ticks must be positive.");
        }
        var targetedLeapCount = slot.Timeline.Stages.Sum(stage =>
            stage.Operations.Count(operation =>
                operation is StartCapabilityOperationSource capability &&
                capability.CapabilityId == TargetedLeapCapabilityId));
        var recoveries = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == AimedRecoveryCapabilityId).ToArray();
        if (recoveries.Length > 0)
        {
            var stage = slot.Timeline.Stages[0];
            if (recoveries.Length != 1 || slot.Timeline.Stages.Count != 1 || stage.IasaTicks != 0
                || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo || slot.AimMode != AuthoringAimMode.None
                || !slot.IsRecoveryMove || recoveries[0].Tick != 0
                || recoveries[0].Parameters is not AimedRecoveryCapabilityParameters
                || stage.Operations.Any(op => op != recoveries[0] && op is not EmitPresentationOperationSource && op is not SpawnHitboxOperationSource))
                d.Error("capability.ambiguous", $"character.slots[{index}]",
                    "Aimed recovery requires one tick-zero lifecycle in one recovery stage, no aim or IASA, and only presentation or hitbox operations alongside it.");
            if (recoveries[0].Parameters is AimedRecoveryCapabilityParameters recovery && recovery.StartupTicks >= stage.DurationTicks)
                d.Error("value.out-of-range", $"character.slots[{index}].timeline",
                    "Aimed recovery must launch before its stage ends.");
        }
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
        var chargedStreams = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == ChargedStreamCapabilityId).ToArray();
        if (chargedStreams.Length > 0)
        {
            if (chargedStreams.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.ChargeAttack ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0)
                d.Error("capability.ambiguous", $"character.slots[{index}]",
                    "Charged stream requires one chargeAttack lifecycle in one stage with IASA disabled.");
            var stage = slot.Timeline.Stages[0];
            // Phase clips play in real time. The cook sorts a stage's clips, and the first is the stage's own:
            // phase clips must sort after it, as the steered charge's do.
            var clips = stage.AnimationIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
            bool PhaseClip(string id) => clips.IndexOf(id) > 0;
            foreach (var stream in chargedStreams)
            {
                if (stream.Tick != 0 || stream.Parameters is not ChargedStreamCapabilityParameters p)
                    d.Error("value.out-of-range", $"character.slots[{index}].timeline",
                        "Charged stream must start at tick zero.");
                else if (p.SprayAnimationId.Length > 0 && !PhaseClip(p.SprayAnimationId)
                    || p.EndAnimationId.Length > 0 && !PhaseClip(p.EndAnimationId))
                    d.Error("reference.unresolved", $"character.slots[{index}].timeline.stages[0].animationIds",
                        "The charged stream's spray and end clips must be listed in its stage and sort after its first clip (name them after it).");
                else if (p.SprayLoopTicks > 0 && p.SprayAnimationId.Length == 0)
                    d.Error("value.out-of-range", $"character.slots[{index}].timeline",
                        "A charged stream loops only its own spray clip: set sprayAnimationId with sprayLoopTicks.");
            }
            if (slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>
                operation is SpawnProjectileOperationSource or SpawnHitboxOperationSource or SetAimStateOperationSource
                    or SetVelocityOperationSource or ForwardLungeOperationSource ||
                operation is StartCapabilityOperationSource capability && capability.CapabilityId != ChargedStreamCapabilityId))
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                    "Charged stream cannot share a slot with competing motion, aim, projectile, or lifecycle operations.");
        }
        var fingerGuns = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == FingerGunsCapabilityId).ToArray();
        if (fingerGuns.Length > 0)
        {
            var stage = slot.Timeline.Stages[0];
            if (fingerGuns.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo ||
                slot.Timeline.Stages.Count != 1 || stage.IasaTicks != 0 || slot.ChargePool != null ||
                slot.AllowSlideCarry ||
                slot.AimMode is not (AuthoringAimMode.None or AuthoringAimMode.CameraForward3D or AuthoringAimMode.GroundVector) ||
                stage.Operations.Any(operation => operation != fingerGuns[0] && operation is not EmitPresentationOperationSource))
                d.Error("capability.ambiguous", $"character.slots[{index}]",
                    "Finger Guns requires one tick-zero meleeCombo lifecycle, no IASA, slide carry or charge pool, and only presentation operations alongside it.");
            foreach (var guns in fingerGuns)
            {
                if (guns.Tick != 0 || guns.Parameters is not FingerGunsCapabilityParameters p)
                    d.Error("value.out-of-range", $"character.slots[{index}].timeline", "Finger Guns must start at tick zero.");
                else if (string.IsNullOrEmpty(p.ReloadAnimationId) ||
                    stage.AnimationIds.OrderBy(id => id, StringComparer.Ordinal).ToList().IndexOf(p.ReloadAnimationId) <= 0)
                    d.Error("reference.unresolved", $"character.slots[{index}].timeline.stages[0].animationIds",
                        "The reload clip must be listed in the stage and sort after its first clip.");
            }
        }
        if (targetedLeapCount > 1)
            d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                "A slot may contain only one targeted-leap lifecycle.");
        var dashStrikes = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>().Where(x => x.CapabilityId == DashStrikeCapabilityId).ToArray();
        if (dashStrikes.Length > 0 && dashStrikes[0].Parameters is DashStrikeCapabilityParameters dashStrike)
        {
            var stage = slot.Timeline.Stages[0];
            if (DashStrikeContract.SlotError(dashStrikes.Length, slot.Timeline.Stages.Count, dashStrikes[0].Tick,
                slot.Behavior, slot.AimMode, stage.IasaTicks,
                stage.Operations.Any(x => x != dashStrikes[0] && x is not EmitPresentationOperationSource),
                slot.AllowSlideCarry, slot.ChargePool != null, stage.DurationTicks,
                (CookedDashStrikeCapabilityParameters)CookParameters(dashStrike)) is { } error)
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline", error);
        }
        var flashCuts = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>().Where(x => x.CapabilityId == FlashCutCapabilityId).ToArray();
        if (flashCuts.Length > 0 && flashCuts[0].Parameters is FlashCutCapabilityParameters flashCut)
        {
            var stage = slot.Timeline.Stages[0];
            if (FlashCutContract.SlotError(flashCuts.Length, slot.Timeline.Stages.Count, flashCuts[0].Tick,
                slot.Behavior, slot.AimMode, stage.IasaTicks,
                stage.Operations.Any(x => x != flashCuts[0] && x is not EmitPresentationOperationSource),
                slot.AllowSlideCarry, slot.ChargePool != null, stage.DurationTicks, stage.AnimationIds,
                (CookedFlashCutCapabilityParameters)CookParameters(flashCut)) is { } error)
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline", error);
        }
        if (slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .Count(operation => operation is SpawnProjectileOperationSource { Projectile.Return: not null }) > 1)
            d.Error("capability.ambiguous", $"character.slots[{index}].timeline",
                "A slot may throw only one returning projectile: its slot waits for it to come back.");
        var steeredCharges = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == SteeredChargeCapabilityId).ToArray();
        if (steeredCharges.Length > 0)
            ValidateSteeredChargeSlot(slot, index, steeredCharges, c, d);
        var thrusts = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == ThrustCapabilityId).ToArray();
        if (thrusts.Length > 0)
            ValidateThrustSlot(slot, index, thrusts, d);
        var tethers = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == TetherCapabilityId).ToArray();
        if (tethers.Length > 0)
            ValidateTetherSlot(slot, index, tethers, d);
        var commandGrabs = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>().Where(x => x.CapabilityId == CommandGrabCapabilityId).ToArray();
        if (commandGrabs.Length > 0 && commandGrabs[0].Parameters is CommandGrabCapabilityParameters commandGrab)
        {
            var stages = slot.Timeline.Stages;
            var first = stages[0];
            var hold = stages.Count > 1 ? stages[1] : null;
            if (CommandGrabContract.SlotError(commandGrabs.Length, stages.Count, commandGrabs[0].Tick,
                    slot.Behavior, slot.AimMode, slot.ShellPolicy, slot.AllowSlideCarry, slot.ChargePool != null,
                    !first.Operations.Contains(commandGrabs[0])
                        || first.Operations.Any(x => x != commandGrabs[0] && x is not EmitPresentationOperationSource),
                    first.DurationTicks, first.IasaTicks, hold?.DurationTicks ?? 0, hold?.IasaTicks ?? 0,
                    (CookedCommandGrabCapabilityParameters)CookParameters(commandGrab)) is { } error)
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline", error);
        }
        var cannonballs = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Where(operation => operation.CapabilityId == CannonballCapabilityId).ToArray();
        if (cannonballs.Length > 0)
        {
            var stage = slot.Timeline.Stages[0];
            if (CannonballSlotError(cannonballs.Length, slot.Timeline.Stages.Count, cannonballs[0].Tick, slot.Behavior,
                    slot.AimMode, stage.IasaTicks, slot.ShellPolicy, stage.DurationTicks,
                    stage.Operations.Any(operation => operation != cannonballs[0] && operation is not EmitPresentationOperationSource),
                    cannonballs[0].Parameters is CannonballCapabilityParameters x ? (CookedCannonballCapabilityParameters)CookParameters(x) : null)
                is { } cannonballError)
                d.Error("capability.ambiguous", $"character.slots[{index}].timeline", cannonballError);
        }
        if (slot.ShellPolicy == ShellPolicy.Ride && RideSlotError(slot.Timeline.Stages.Count,
                slot.Timeline.Stages.Count == 0 ? (ushort)0 : slot.Timeline.Stages[0].IasaTicks,
                slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation => operation is not
                    (EmitPresentationOperationSource or StartupAimCorrectionOperationSource or GravityWindowOperationSource)))
            is { } rideError)
            d.Error("capability.ambiguous", $"character.slots[{index}].timeline", rideError);
        foreach (var stage in slot.Timeline.Stages)
        {
            if (stage.DurationTicks == 0 || stage.IasaTicks > stage.DurationTicks || stage.LandingLagTicks > stage.DurationTicks || stage.AutoCancelBeforeTicks > stage.DurationTicks || stage.AutoCancelAfterTicks > stage.DurationTicks) d.Error("value.out-of-range", "character.slots[" + index + "].timeline", "Stage timing is outside its duration.");
            if (float.IsNaN(stage.AttackRange) || float.IsInfinity(stage.AttackRange) || float.IsNaN(stage.WarpRange) || float.IsInfinity(stage.WarpRange) || float.IsNaN(stage.TrackingStrength) || float.IsInfinity(stage.TrackingStrength))
                d.Error("value.non-finite", $"character.slots[{index}].timeline", "Targeting metadata must be finite.");
            if (stage.AttackRange < 0f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.attackRange", "Attack range must be non-negative.");
            if (stage.WarpRange < 0f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.warpRange", "Warp range must be non-negative.");
            if (stage.TrackingStrength < 0f || stage.TrackingStrength > 1f) d.Error("value.out-of-range", $"character.slots[{index}].timeline.trackingStrength", "Tracking strength must be between 0 and 1.");
            if (stage.WarpRange != 0f || stage.UseTargetLock || stage.RotateTowardTarget || stage.TrackingStrength != 0f)
                d.Error("timeline.targeting.retired", $"character.slots[{index}].timeline",
                    "Automatic warp and per-stage target tracking are retired. Use authored movement and startup aim correction.");
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
                ValidateOperation(operation, c, d);
            }
        }
        ValidateStartupAimCorrection(slot, index, d);
    }
    /// <summary>
    /// A steered charge owns the end of its move: one per slot, in a single stage with IASA disabled,
    /// with no competing motion, hitboxes, projectiles, aim or lifecycle. Its startup (rear-up) may correct
    /// aim, slow the fall or carry armour, all ending by the time the charge starts; presentation cues may
    /// come at any tick. Its phase clips are listed in its stage after the startup clip, which keeps the
    /// stage's first (sorted) place.
    /// </summary>
    private static void ValidateSteeredChargeSlot(CharacterSlotSource slot, int index,
        StartCapabilityOperationSource[] charges, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        string path = $"character.slots[{index}].timeline";
        if (charges.Length != 1 || slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0)
        {
            d.Error("capability.ambiguous", path,
                "Steered charge requires one lifecycle in one stage with IASA disabled.");
            return;
        }
        var stage = slot.Timeline.Stages[0];
        var charge = charges[0];
        foreach (var operation in stage.Operations)
        {
            if (operation == charge) continue;
            if (operation is ForwardLungeOperationSource or SetVelocityOperationSource or SpawnHitboxOperationSource
                or SpawnProjectileOperationSource or SetAimStateOperationSource or StartCapabilityOperationSource
                or CompleteTimelineOperationSource)
                d.Error("capability.ambiguous", path,
                    "Steered charge cannot share its stage with motion, hitboxes, projectiles, aim, completion or another lifecycle.");
            if (operation is StartupAimCorrectionOperationSource correction && correction.EndTick > charge.Tick)
                d.Error("operation.cutoff-after-commitment", path + ".operations.startupAimCorrection.endTick",
                    "Startup aim correction must end no later than the charge starts.");
            if (operation is GravityWindowOperationSource gravity && gravity.Tick + gravity.DurationTicks > charge.Tick)
                d.Error("value.out-of-range", path + ".operations.gravityWindow.durationTicks",
                    "A gravity window must end no later than the charge starts; the charge owns its own height.");
            if (operation is ArmorWindowOperationSource armor && armor.Tick + armor.DurationTicks > charge.Tick)
                d.Error("value.out-of-range", path + ".operations.armorWindow.durationTicks",
                    "An armour window must end no later than the charge starts; the charge owns its own armour.");
        }
        if (charge.Parameters is not SteeredChargeCapabilityParameters x)
            return;
        var clips = stage.AnimationIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var (id, field) in new[]
        {
            (x.GallopAnimationId, "gallopAnimationId"),
            (x.HitEndingAnimationId, "hitEndingAnimationId"),
            (x.MissEndingAnimationId, "missEndingAnimationId"),
            (x.AirEndingAnimationId, "airEndingAnimationId"),
            (x.Carry.FlingAnimationId, "carry.flingAnimationId"),
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            int clip = clips.IndexOf(id);
            if (clip < 0)
                d.Error("reference.unresolved", path + ".operations.parameters." + field,
                    "A steered charge phase clip must be listed in its stage's animationIds.");
            else if (clip == 0)
                d.Error("value.out-of-range", path + ".operations.parameters." + field,
                    "The stage's first clip is the startup; phase clips sort after it (name them after it).");
        }
    }

    /// <summary>
    /// A thrust picks its direction when its move starts and owns the fighter's travel: one per slot, at tick
    /// zero of a single meleeCombo stage with no aim, alongside presentation and armour only. IASA is allowed
    /// (Hydro Jet hands off to Shell Dive mid-thrust).
    /// </summary>
    private static void ValidateThrustSlot(CharacterSlotSource slot, int index,
        StartCapabilityOperationSource[] thrusts, DiagnosticBag d)
    {
        string path = $"character.slots[{index}].timeline";
        var stage = slot.Timeline.Stages[0];
        if (thrusts.Length != 1 || slot.Timeline.Stages.Count != 1 || thrusts[0].Tick != 0
            || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo || slot.AimMode != AuthoringAimMode.None
            || stage.Operations.Any(operation => operation != thrusts[0]
                && operation is not (EmitPresentationOperationSource or ArmorWindowOperationSource)))
            d.Error("capability.ambiguous", path,
                "Thrust requires one tick-zero lifecycle in one meleeCombo stage with no aim, and only presentation or armour alongside it.");
        if (thrusts[0].Parameters is ThrustCapabilityParameters thrust && thrust.ThrustTicks > stage.DurationTicks)
            d.Error("value.out-of-range", path + ".operations.parameters.thrustTicks", "The thrust must end within its stage.");
    }

    /// <summary>
    /// A tether owns the end of its move (ADR-0048): one per slot, at tick zero of a single meleeCombo stage with
    /// no aim and IASA disabled, beside presentation only, and never tucked. Its phase clips sort after the
    /// stage's first clip, the throw.
    /// </summary>
    private static void ValidateTetherSlot(CharacterSlotSource slot, int index,
        StartCapabilityOperationSource[] tethers, DiagnosticBag d)
    {
        string path = $"character.slots[{index}].timeline";
        var stage = slot.Timeline.Stages[0];
        if (TetherSlotError(tethers.Length, slot.Timeline.Stages.Count, tethers[0].Tick, slot.Behavior, slot.AimMode,
                stage.IasaTicks, slot.ShellPolicy,
                stage.Operations.Any(operation => operation != tethers[0] && operation is not EmitPresentationOperationSource))
            is { } error)
            d.Error("capability.ambiguous", path, error);
        if (tethers[0].Parameters is not TetherCapabilityParameters x)
            return;
        var clips = stage.AnimationIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var (id, field) in new[] { (x.ReelAnimationId, "reelAnimationId"), (x.RetractAnimationId, "retractAnimationId") })
        {
            if (string.IsNullOrEmpty(id)) continue;
            int clip = clips.IndexOf(id);
            if (clip < 0)
                d.Error("reference.unresolved", path + ".operations.parameters." + field,
                    "A tether phase clip must be listed in its stage's animationIds.");
            else if (clip == 0)
                d.Error("value.out-of-range", path + ".operations.parameters." + field,
                    "The stage's first clip is the throw; phase clips sort after it (name them after it).");
        }
    }

    /// <summary>The tether slot's shape, shared by the compiler and the cooked loader; null when admissible.</summary>
    internal static string? TetherSlotError(int tethers, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ushort iasaTicks, ShellPolicy shellPolicy, bool otherOperations)
        => tethers != 1 || stages != 1 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo
            || aim != AuthoringAimMode.None || iasaTicks != 0 || otherOperations
            ? "Tether requires one tick-zero lifecycle in one meleeCombo stage with no aim and IASA disabled, and only presentation alongside it."
            : shellPolicy == ShellPolicy.Tuck ? "A tether pops her out of the shell; it cannot tuck." : null;

    /// <summary>Bounds shared by the compiler and the cooked loader; null when the tether is admissible.</summary>
    internal static string? TetherBoundsError(CookedTetherCapabilityParameters x)
    {
        if (x.AtTarget is { } at)
            return x.Projectile != null || x.RetractSpeed != 0f || x.RetractAnimationId.Length > 0
                || x.RetractPresentationId.Length > 0
                ? "A placed catch (atTarget) throws no ring: no projectile, retract speed, retract clip or retract presentation."
                : TetherAtTargetBoundsError(x, at);
        var ring = x.Projectile;
        if (ring == null) return "A thrown tether needs its projectile.";
        if (!AllFinite(x.ReelSpeed, x.AnchorDistance, x.RetractSpeed, x.AirGravityScale, ring.LaunchOffsetX,
                ring.LaunchOffsetY, ring.LaunchOffsetZ, ring.Speed, ring.Gravity, ring.Radius, ring.Damage, ring.Angle,
                ring.BaseKnockback, ring.KnockbackGrowth, ring.YawOffsetDegrees))
            return "Tether values must be finite.";
        if (x.ThrowTick == 0 || x.ThrowTick > 120) return "throwTick must be 1 to 120.";
        if (!(ring.Speed > 0f && ring.Speed <= 60f) || !(ring.Radius > 0f && ring.Radius <= 3f)
            || ring.MaxFlightTicks == 0 || ring.MaxFlightTicks > 240)
            return "The ring needs 0 < speed <= 60, 0 < radius <= 3 and maxFlightTicks 1 to 240.";
        if (ring.Gravity != 0f || ring.YawOffsetDegrees != 0f || ring.Return != null)
            return "The ring flies straight: no gravity, no yaw offset and no return block.";
        if (ring.Damage < 0f || ring.BaseKnockback != 0f || ring.KnockbackGrowth != 0f || ring.StunTicks != 0)
            return "The ring catches instead of launching: damage >= 0, no knockback and no stun.";
        if (!(x.ReelSpeed > 0f && x.ReelSpeed <= 60f) || !(x.RetractSpeed > 0f && x.RetractSpeed <= 60f))
            return "reelSpeed and retractSpeed must be above 0 and at most 60.";
        return TetherReelBoundsError(x);
    }

    private static string? TetherAtTargetBoundsError(CookedTetherCapabilityParameters x, CookedTetherAtTarget at)
    {
        if (!AllFinite(x.ReelSpeed, x.AnchorDistance, x.AirGravityScale, at.Radius, at.Height, at.Damage, at.MaxRange,
                at.NoTargetDistance))
            return "Tether values must be finite.";
        if (x.ThrowTick == 0 || x.ThrowTick > 120) return "throwTick must be 1 to 120.";
        if (at.WarningTicks == 0 || at.WarningTicks > 120) return "atTarget.warningTicks must be 1 to 120.";
        if (at.CatchTicks == 0 || at.CatchTicks > 60) return "atTarget.catchTicks must be 1 to 60.";
        if (!(at.Radius > 0f && at.Radius <= 3f) || !(at.Height >= 2f * at.Radius && at.Height <= 6f))
            return "The catch needs 0 < radius <= 3 and a height of two radii up to 6.";
        if (!(at.Damage >= 0f && at.Damage <= 100f)) return "atTarget.damage must be 0 to 100.";
        if (!(at.MaxRange > 0f && at.MaxRange <= MaxStartupAcquisitionRange)
            || !(at.NoTargetDistance >= 0f && at.NoTargetDistance <= at.MaxRange))
            return "atTarget needs 0 < maxRange <= 32 and 0 <= noTargetDistance <= maxRange.";
        if (!(x.ReelSpeed > 0f && x.ReelSpeed <= 60f)) return "reelSpeed must be above 0 and at most 60.";
        return TetherUnmarkedBoundsError(at.Unmarked) ?? TetherReelBoundsError(x);
    }

    private static string? TetherUnmarkedBoundsError(CookedTetherUnmarked? hand)
    {
        if (hand == null) return null;
        if (!AllFinite(hand.Distance, hand.Radius, hand.Height, hand.Damage, hand.Angle, hand.BaseKnockback,
                hand.KnockbackGrowth))
            return "atTarget.unmarked values must be finite.";
        if (hand.RiseTick == 0 || hand.RiseTick > 120) return "atTarget.unmarked.riseTick must be 1 to 120.";
        if (hand.ActiveTicks == 0 || hand.ActiveTicks > 60) return "atTarget.unmarked.activeTicks must be 1 to 60.";
        if (hand.RecoveryTicks > 240) return "atTarget.unmarked.recoveryTicks must be 0 to 240.";
        if (!(hand.Distance >= 0f && hand.Distance <= 6f)) return "atTarget.unmarked.distance must be 0 to 6.";
        if (!(hand.Radius > 0f && hand.Radius <= 3f) || !(hand.Height >= 2f * hand.Radius && hand.Height <= 6f))
            return "The unmarked hand needs 0 < radius <= 3 and a height of two radii up to 6.";
        if (!(hand.Damage > 0f && hand.Damage <= 100f)) return "atTarget.unmarked.damage must be above 0 and at most 100.";
        if (hand.Angle < -90f || hand.Angle > 90f) return "atTarget.unmarked.angle must be -90 to 90.";
        if (hand.BaseKnockback < 0f || hand.KnockbackGrowth < 0f) return "The unmarked hand's knockback cannot be negative.";
        if (hand.StunTicks > 240) return "atTarget.unmarked.stunTicks must be 0 to 240.";
        return null;
    }

    private static string? TetherReelBoundsError(CookedTetherCapabilityParameters x)
    {
        if (!(x.AnchorDistance >= 0f && x.AnchorDistance <= 3f)) return "anchorDistance must be 0 to 3.";
        if (x.ArrivalStunTicks == 0 || x.ArrivalStunTicks > 240) return "arrivalStunTicks must be 1 to 240.";
        if (x.MaxReelTicks == 0 || x.MaxReelTicks > 600) return "maxReelTicks must be 1 to 600.";
        if (x.MissRecoveryTicks > 240) return "missRecoveryTicks must be 0 to 240.";
        if (!(x.AirGravityScale >= 0f && x.AirGravityScale <= 1f)) return "airGravityScale must be 0 to 1.";
        return null;
    }

    private static void ValidateShell(CharacterAuthoringDocument c, Dictionary<string, string> capabilities, DiagnosticBag d)
    {
        bool stance = capabilities.ContainsKey(ShellStanceCapabilityId);
        if (c.Shell == null)
        {
            if (stance) d.Error("schema.missing", "character.shell", "The shell-stance capability needs a shell block.");
            if (!string.IsNullOrEmpty(c.Presentation.Shell))
                d.Error("capability.unknown", "character.presentation.shell", "A shell pose needs the shell-stance capability and a shell block.");
            return;
        }
        if (!stance) d.Error("capability.unknown", "character.shell", "A shell block needs the slop.ability.shell-stance.v2 requirement.");
        if (string.IsNullOrEmpty(c.Presentation.Shell))
            d.Error("schema.missing", "character.presentation.shell", "A shell block needs a shell pose.");
        if (c.Shell.Splash != null) ValidateCapabilityHitbox(c.Shell.Splash.Hitbox, c, "shell.splash.hitbox", d);
        if (c.Shell.Ride != null) ValidateCapabilityHitbox(c.Shell.Ride.Wave, c, "shell.ride.wave", d);
        if (ShellBoundsError(CookShell(c.Shell)) is { } error)
            d.Error("value.out-of-range", "character.shell", error);
        foreach (var (id, field) in new[] { (c.Shell.Splash?.PresentationId, "splash.presentationId"),
                     (c.Shell.Ride?.HitPresentationId, "ride.hitPresentationId") })
            if (id != null && !c.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", "character.shell." + field, "Presentation ID is not declared.");
    }

    /// <summary>The alternateForm block exists exactly when slop.ability.forms.v1 is required (ADR-0051).</summary>
    private static void ValidateAlternateForm(CharacterAuthoringDocument c, Dictionary<string, string> capabilities, DiagnosticBag d)
    {
        bool forms = capabilities.ContainsKey(FormsCapabilityId);
        if (c.AlternateForm is not { } form)
        {
            if (forms) d.Error("schema.missing", "character.alternateForm", "The forms capability needs an alternateForm block.");
            return;
        }
        if (!forms) d.Error("capability.unknown", "character.alternateForm", "An alternateForm block needs the slop.ability.forms.v1 requirement.");
        ValidateId(form.Id, "character.alternateForm.id", d);
        if (string.IsNullOrWhiteSpace(form.DisplayName))
            d.Error("value.out-of-range", "character.alternateForm.displayName", "Value must be non-empty.");
        foreach (var (name, id) in form.Presentation.Roles)
            if (!string.IsNullOrEmpty(id)) ValidateId(id, "character.alternateForm.presentation." + name, d);
    }

    /// <summary>Diagnostics of the alternate grid, validated as a projected document, point into its block.</summary>
    private static string AlternateFormPath(string path)
        => path.StartsWith("character.slots", StringComparison.Ordinal) || path.StartsWith("character.aliases", StringComparison.Ordinal)
            || path.StartsWith("character.movement", StringComparison.Ordinal)
            ? "character.alternateForm" + path.Substring("character".Length) : path;

    /// <summary>
    /// The forms rules shared by the compiler and the cooked loader (ADR-0051): the alternate form exists exactly with
    /// its capability, has its own fourteen canonical form-1 slots, a lowercase ID, a name and a positive air-dodge
    /// speed; the top-level grid is form 0; a move swaps at most once, and only in a fighter with forms.
    /// </summary>
    internal static string? FormsBoundsError(CookedCharacterDefinition x)
    {
        bool forms = x.CapabilityRequirements.Any(r => r.CapabilityId == FormsCapabilityId);
        if (forms != (x.AlternateForm != null))
            return "An alternateForm block exists exactly when the slop.ability.forms.v1 capability is required.";
        if (x.Slots.Any(slot => slot.Form != 0))
            return "The top-level grid is form 0.";
        foreach (var slot in x.AllSlots)
            if (slot.Timeline.Stages.SelectMany(stage => stage.Operations).Count(op => op is CookedSwapFormOperation) > (forms ? 1 : 0))
                return forms ? $"Move {slot.Id} swaps form more than once." : "swapForm needs the forms capability and an alternateForm block.";
        if (x.AlternateForm is not { } form) return null;
        if (!IsId(form.Id) || string.IsNullOrWhiteSpace(form.DisplayName))
            return "The alternate form needs a lowercase ID and a display name.";
        if (form.Slots.Count != CanonicalSlots.Length
            || form.Slots.Where((slot, i) => slot.Form != 1 || slot.Ordinal != i || slot.Id != CanonicalSlots[i]).Any())
            return "The alternate form has its own fourteen canonical slots.";
        if (!(form.Movement.AirDodgeSpeed > 0f))
            return "The alternate form's air-dodge speed must be greater than zero.";
        foreach (var (_, id) in form.Presentation.Roles)
            if (id.Length > 0 && !IsId(id)) return "Alternate form roles must be lowercase IDs.";
        return null;
    }

    /// <summary>A tuck or ride move is a special of a fighter with a shell block; a tuck move does not carry Run or Slide.</summary>
    private static void ValidateShellPolicy(string canonicalId, CharacterSlotSource slot, int sourceIndex,
        CharacterAuthoringDocument c, DiagnosticBag d)
    {
        if (slot.ShellPolicy == ShellPolicy.PopOut) return;
        string path = sourceIndex >= 0 ? $"character.slots[{sourceIndex}].shellPolicy" : $"character.slots.{canonicalId}.shellPolicy";
        if (slot.ShellPolicy is not (ShellPolicy.Tuck or ShellPolicy.Ride))
            d.Error("value.out-of-range", path, "Unknown shell policy.");
        else if (c.Shell == null)
            d.Error("capability.unknown", path, "A tuck or ride move needs the character's shell block.");
        else if (!IsSpecial(canonicalId))
            d.Error("value.out-of-range", path, "Only specials (A, E, R, F) can tuck or ride.");
        else if (slot.ShellPolicy == ShellPolicy.Tuck && (slot.AllowSlideCarry || slot.PreserveMomentumOnStart))
            d.Error("capability.ambiguous", path, "A tuck move already keeps her momentum: leave allowSlideCarry and preserveMomentumOnStart off.");
    }

    internal static bool IsSpecial(string slotId)
        => slotId.EndsWith(".A", StringComparison.Ordinal) || slotId.EndsWith(".E", StringComparison.Ordinal)
            || slotId.EndsWith(".R", StringComparison.Ordinal) || slotId.EndsWith(".F", StringComparison.Ordinal);

    /// <summary>
    /// Shared by the compiler and the cooked loader, over the canonical slots: a splash exists exactly when some slot
    /// tucks, a ride exactly when some slot rides, and every riding slot is one special with its ground/air variants.
    /// </summary>
    internal static string? ShellPairingError(bool hasSplash, bool hasRide, IEnumerable<(string Id, ShellPolicy Policy)> slots)
    {
        var list = slots.ToList();
        if (hasSplash != list.Any(slot => slot.Policy == ShellPolicy.Tuck))
            return "The shell's splash exists exactly when a slot tucks.";
        var riders = list.Where(slot => slot.Policy == ShellPolicy.Ride).Select(slot => slot.Id[^1]).Distinct().Count();
        if (hasRide != riders > 0)
            return "The shell's ride exists exactly when a slot rides.";
        return riders > 1 ? "Only one special (and its air or ground variant) can ride." : null;
    }

    /// <summary>A ride move's shape, shared by the compiler and the cooked loader; null when admissible.</summary>
    internal static string? RideSlotError(int stages, ushort iasaTicks, bool otherOperations)
        => stages != 1 || iasaTicks != 0 || otherOperations
            ? "A ride move is one stage with IASA disabled and only presentation, startup aim correction and gravity windows: the ride is the shell's."
            : null;

    /// <summary>The cannonball slot's shape, shared by the compiler and the cooked loader; null when admissible.</summary>
    internal static string? CannonballSlotError(int cannonballs, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ushort iasaTicks, ShellPolicy shellPolicy, ushort durationTicks, bool otherOperations,
        CookedCannonballCapabilityParameters? parameters)
    {
        if (cannonballs != 1 || stages != 1 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo
            || aim != AuthoringAimMode.None || iasaTicks != 0 || otherOperations)
            return "Cannonball requires one tick-zero lifecycle in one meleeCombo stage with no aim and IASA disabled, and only presentation alongside it.";
        if (shellPolicy != ShellPolicy.Tuck)
            return "A cannonball tucks: the shell's splash lands it.";
        if (parameters == null)
            return "Cannonball parameters are missing.";
        return parameters.TakeoffTick + parameters.MaxFlightTicks >= durationTicks
            ? "The cannonball's stage must outlast its takeoff and longest flight." : null;
    }

    /// <summary>Bounds shared by the compiler and the cooked loader; null when the cannonball is admissible.</summary>
    internal static string? CannonballBoundsError(CookedCannonballCapabilityParameters x)
    {
        if (!AllFinite(x.MaxRange, x.NoTargetDistance, x.MaxLaunchSpeed)) return "Cannonball values must be finite.";
        if (x.TakeoffTick > 60) return "takeoffTick must be 0 to 60.";
        if (!(x.NoTargetDistance > 0f && x.NoTargetDistance <= x.MaxRange && x.MaxRange <= 30f))
            return "Ranges need 0 < noTargetDistance <= maxRange <= 30.";
        if (x.MinFlightTicks < 2 || x.MinFlightTicks > x.MaxFlightTicks || x.MaxFlightTicks > 240)
            return "Flight ticks need 2 <= minFlightTicks <= maxFlightTicks <= 240.";
        if (!(x.MaxLaunchSpeed > 0f && x.MaxLaunchSpeed <= 60f)) return "maxLaunchSpeed must be above 0 and at most 60.";
        return null;
    }

    /// <summary>Bounds shared by the compiler and the cooked loader; null when the shell is admissible.</summary>
    internal static string? ShellBoundsError(CookedShellDefinition x)
    {
        if (!AllFinite(x.PopOutDeceleration, x.BlockedRecoilSpeed)) return "Shell values must be finite.";
        if (!(x.PopOutDeceleration >= 0f && x.PopOutDeceleration <= 600f)) return "popOutDeceleration must be 0 to 600.";
        if (!(x.BlockedRecoilSpeed >= 0f && x.BlockedRecoilSpeed <= 30f)) return "blockedRecoilSpeed must be 0 to 30.";
        if (x.Splash == null && x.Ride == null) return "A shell needs a splash, a ride or both.";
        if (x.Splash is { } splash)
        {
            if (splash.Hitbox.StartBoneId != null || splash.Hitbox.EndBoneId != null || splash.Hitbox.HitGroup != 0)
                return "The splash hitbox is root-relative with its own hit history (no bones, hitGroup 0).";
            if (splash.PopUpTicks == 0 || splash.PopUpTicks > 240) return "splash.popUpTicks must be 1 to 240.";
        }
        if (x.Ride is { } ride)
        {
            if (!AllFinite(ride.Speed, ride.TurnDegreesPerSecond)) return "Ride values must be finite.";
            if (ride.Ticks == 0 || ride.Ticks > 600) return "ride.ticks must be 1 to 600.";
            if (!(ride.Speed > 0f && ride.Speed <= 60f)) return "ride.speed must be above 0 and at most 60.";
            if (!(ride.TurnDegreesPerSecond >= 0f && ride.TurnDegreesPerSecond <= 720f))
                return "ride.turnDegreesPerSecond must be 0 to 720.";
            if (ride.Wave.StartBoneId != null || ride.Wave.EndBoneId != null || ride.Wave.HitGroup != 0)
                return "The wave is root-relative with its own hit history (no bones, hitGroup 0).";
            if (ride.Wave.StunTicks != 0 || ride.Wave.FixedHitstunTicks != 0)
                return "The wave carries instead of stunning: stunTicks and fixedHitstunTicks are 0.";
        }
        return null;
    }

    /// <summary>Bounds shared by the compiler and the cooked loader; null when the thrust is admissible.</summary>
    internal static string? ThrustBoundsError(CookedThrustCapabilityParameters x)
    {
        if (!AllFinite(x.TargetConeDegrees, x.NeutralPitchDegrees, x.StickPitchDegrees, x.Speed, x.Acceleration, x.MaxSpeed))
            return "Thrust values must be finite.";
        if (x.Aim is not (ThrustAim.Stick or ThrustAim.TargetAhead)) return "Unknown thrust aim.";
        if (!(x.TargetConeDegrees >= 0f && x.TargetConeDegrees <= 180f)
            || x.Aim == ThrustAim.Stick && x.TargetConeDegrees != 0f)
            return "targetConeDegrees is 0 to 180 and only applies to targetAhead.";
        if (!(MathF.Abs(x.NeutralPitchDegrees) <= 90f && MathF.Abs(x.StickPitchDegrees) <= 90f))
            return "Pitches must be -90 to 90 degrees.";
        if (!(x.Speed > 0f && x.Speed <= x.MaxSpeed && x.MaxSpeed <= 60f)) return "Speeds need 0 < speed <= maxSpeed <= 60.";
        if (!(x.Acceleration >= 0f && x.Acceleration <= 240f)) return "acceleration must be 0 to 240.";
        if (x.ThrustTicks == 0 || x.ThrustTicks > 600) return "thrustTicks must be 1 to 600.";
        if (x.TrailHitbox is { } trail && (trail.StartBoneId != null || trail.EndBoneId != null || trail.HitGroup != 0))
            return "The trail hitbox is root-relative with its own hit history (no bones, hitGroup 0).";
        return null;
    }

    private static bool AllFinite(params float[] values)
        => values.All(value => !float.IsNaN(value) && !float.IsInfinity(value));

    private static CookedShellDefinition CookShell(ShellSource x)
        => new(x.PopOutDeceleration, x.BlockedRecoilSpeed,
            x.Splash == null ? null : new CookedShellSplash(CookHitbox(x.Splash.Hitbox), x.Splash.PopUpTicks, x.Splash.PresentationId),
            x.Ride == null ? null : new CookedShellRide(x.Ride.Ticks, x.Ride.Speed, x.Ride.TurnDegreesPerSecond,
                CookHitbox(x.Ride.Wave), x.Ride.HitPresentationId));

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

    private static void ValidateAimMovementSpeed(string canonicalId, CharacterSlotSource slot, int sourceIndex, DiagnosticBag d)
    {
        if (slot.AimMovementSpeedScale == 1f)
            return;
        var path = sourceIndex >= 0
            ? $"character.slots[{sourceIndex}].aimMovementSpeedScale"
            : $"character.slots.{canonicalId}.aimMovementSpeedScale";
        if (!(slot.AimMovementSpeedScale > 0f && slot.AimMovementSpeedScale <= 1f))
            d.Error("value.out-of-range", path, "Aim movement speed scale must be greater than 0 and at most 1.");
        if (slot.AimMovement != AuthoringAimMovementMode.Mobile)
            d.Error("slot.aim-movement-speed.requires-mobile", path, "Aim movement speed scale requires aimMovement mobile.");
    }

    private static void ValidateSlideCarry(string canonicalId, CharacterSlotSource slot, int sourceIndex, DiagnosticBag d)
    {
        if (!slot.AllowSlideCarry || slot.Timeline?.Stages == null)
            return;

        var path = sourceIndex >= 0
            ? $"character.slots[{sourceIndex}].allowSlideCarry"
            : $"character.slots.{canonicalId}.allowSlideCarry";
        const string code = "slot.slide-carry.motion-conflict";
        if (canonicalId != "ground.1" && canonicalId != "ground.2" && canonicalId != "ground.3")
            d.Error(code, path, "Slide carry is only valid on grounded canonical normals.");
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


    internal static bool ValidAimedRecovery(ushort startup, float vertical, float directional, float horizontal, float preparation,
        RecoveryHopParameters? hop = null, float riseSteer = 0f)
        => startup is > 0 and <= 120 && vertical is > 0f and <= 60f
            && directional > 0f && directional <= vertical && horizontal is >= 0f and <= 60f
            && preparation is >= 0f and <= 4f && riseSteer is >= 0f and <= 120f
            && (hop == null || preparation == 0f && hop.StartTicks > 0
                && hop.ContactTicks >= hop.StartTicks + 4 && hop.ContactTicks < startup
                && hop.ForwardDistance is > 0f and <= 3f
                && hop.ArcHeight is > 0f and <= 3f && hop.LandingHeight is >= 0f and <= 1f);

    internal static bool ValidFingerGuns(ushort fire, ushort recovery, byte shots, ushort reload, float fall, float recoil)
        => fire is > 0 and <= 120 && recovery is > 0 and <= 240 && shots is > 0 and <= 24
            && reload is > 0 and <= 600 && fall is >= 0f and <= 30f && recoil is >= 0f and <= 30f;

    private static bool IsKnownAnimation(string id, CharacterAuthoringDocument c)
        => id.StartsWith("anim.", StringComparison.Ordinal);

    private static void ValidateOperation(CharacterTimelineOperationSource operation, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var expected = operation switch { SetVelocityOperationSource or ForwardLungeOperationSource => AuthoringUnit.MetersPerSecond, GravityWindowOperationSource => AuthoringUnit.Normalized, SpawnHitboxOperationSource => AuthoringUnit.Meters, SpawnProjectileOperationSource => AuthoringUnit.Meters, _ => AuthoringUnit.Ticks };
        if (operation.Unit != expected) d.Error("unit.unknown", "character.operation.unit", "Unit does not match operation contract.");
        if (operation is StartCapabilityOperationSource { Parameters: var embedded }
            && embedded switch
            {
                ChargedProjectileCapabilityParameters x => x.Projectile,
                FingerGunsCapabilityParameters x => x.Projectile,
                ChargedStreamCapabilityParameters x => x.Projectile,
                TetherCapabilityParameters x => x.Projectile,
                _ => null,
            } is { } inner && (inner.Homing != null || inner.Perch != null))
            d.Error("value.out-of-range", "character.operation.parameters.projectile",
                "homing and perch belong to spawnProjectile operations, not to a capability's projectile.");
        switch (operation)
        {
            case StartCapabilityOperationSource capability when capability.Parameters is AimedRecoveryCapabilityParameters recovery:
                ValidateFiniteValues(new[] { recovery.VerticalSpeed, recovery.DirectionalVerticalSpeed,
                    recovery.HorizontalSpeed, recovery.PreparationRiseSpeed, recovery.RiseSteerAcceleration }, "character.operation.parameters", d);
                if (recovery.Hop is { } hop)
                    ValidateFiniteValues(new[] { hop.ForwardDistance, hop.ArcHeight, hop.LandingHeight },
                        "character.operation.parameters.hop", d);
                if (!ValidAimedRecovery(recovery.StartupTicks, recovery.VerticalSpeed, recovery.DirectionalVerticalSpeed,
                    recovery.HorizontalSpeed, recovery.PreparationRiseSpeed, recovery.Hop, recovery.RiseSteerAcceleration))
                    d.Error("value.out-of-range", "character.operation.parameters",
                        "Aimed recovery needs bounded speeds, rise steering 0–120 m/s² and startup 1–120 ticks. An optional hop requires zero preparation rise, start>0, at least 4 flight ticks, contact before launch, forward/arc distances in (0,3] m and landing lift in [0,1] m.");
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is MankiRoundBombCapabilityParameters or MankiJetpackBoostCapabilityParameters or MankiKamikazeCapabilityParameters:
                if (MankiCapabilityBounds.Error(CookParameters(capability.Parameters)) is { } mankiError)
                    d.Error("value.out-of-range", "character.operation.parameters", mankiError);
                break;
            case StartCapabilityOperationSource { Parameters: MankiBazookaCapabilityParameters bazooka } rocket:
                if (bazooka.ChargeTicks > 600 || bazooka.ChargeTicks + bazooka.CastDuration > ushort.MaxValue
                    || rocket.CapabilityId == "slop.internal.manki.bazooka.v3" && bazooka.ChargeTicks == 0)
                    d.Error("value.out-of-range", "character.operation.parameters.chargeTicks", "Bazooka charge must be at most 600 ticks.");
                ValidateFiniteValues(new[] { bazooka.HomingTurnDegPerTick }, "character.operation.parameters.homingTurnDegPerTick", d);
                if (bazooka.HomingTurnDegPerTick <= 0f || bazooka.HomingTurnDegPerTick > 90f)
                    d.Error("value.out-of-range", "character.operation.parameters.homingTurnDegPerTick",
                        "Homing turn must be greater than zero and at most 90 degrees per tick.");
                break;
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
                ValidateHitPresentation(hitbox.Hitbox, c, "character.hitbox.hitPresentationId", d);
                break;
            case SpawnProjectileOperationSource projectile:
                if (projectile.Projectile.Return != null)
                    ValidateProjectileReturn(projectile.Projectile, c, d);
                if (projectile.Projectile.Homing != null || projectile.Projectile.Perch != null)
                    ValidateHomingAndPerch(projectile.Projectile, c, d);
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
            case StartCapabilityOperationSource capability when capability.Parameters is DashStrikeCapabilityParameters dash:
                ValidateDashStrike(dash, c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is CommandGrabCapabilityParameters commandGrab:
                ValidateCapabilityHitbox(commandGrab.Capture, c, "capture", d);
                if (CommandGrabContract.BoundsError((CookedCommandGrabCapabilityParameters)CookParameters(commandGrab)) is { } grabError)
                    d.Error("value.out-of-range", "character.operation.parameters", grabError);
                foreach (var (id, field) in new[] { (commandGrab.CatchPresentationId, "catchPresentationId"),
                             (commandGrab.ReleasePresentationId, "releasePresentationId") })
                    if (!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal))
                        d.Error("reference.unresolved", "character.operation.parameters." + field, "Presentation ID is not declared.");
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is FlashCutCapabilityParameters flash:
                ValidateFlashCut(flash, c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is SteeredChargeCapabilityParameters charge:
                ValidateSteeredCharge(charge, c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is ThrustCapabilityParameters thrust:
                if (thrust.TrailHitbox != null) ValidateCapabilityHitbox(thrust.TrailHitbox, c, "trailHitbox", d);
                if (ThrustBoundsError((CookedThrustCapabilityParameters)CookParameters(thrust)) is { } thrustError)
                    d.Error("value.out-of-range", "character.operation.parameters", thrustError);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is CannonballCapabilityParameters cannonball:
                if (CannonballBoundsError((CookedCannonballCapabilityParameters)CookParameters(cannonball)) is { } cannonballError)
                    d.Error("value.out-of-range", "character.operation.parameters", cannonballError);
                if (!string.IsNullOrEmpty(cannonball.LaunchPresentationId)
                    && !c.PresentationIds.Contains(cannonball.LaunchPresentationId, StringComparer.Ordinal))
                    d.Error("reference.unresolved", "character.operation.parameters.launchPresentationId", "Presentation ID is not declared.");
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is TetherCapabilityParameters tether:
                if (TetherBoundsError((CookedTetherCapabilityParameters)CookParameters(tether)) is { } tetherError)
                    d.Error("value.out-of-range", "character.operation.parameters", tetherError);
                foreach (var (id, field) in new[] { (tether.ThrowPresentationId, "throwPresentationId"),
                             (tether.RetractPresentationId, "retractPresentationId"),
                             (tether.AtTarget?.WarningPresentationId ?? "", "atTarget.warningPresentationId"),
                             (tether.AtTarget?.CatchPresentationId ?? "", "atTarget.catchPresentationId"),
                             (tether.AtTarget?.Unmarked?.PresentationId ?? "", "atTarget.unmarked.presentationId") })
                    if (!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal))
                        d.Error("reference.unresolved", "character.operation.parameters." + field, "Presentation ID is not declared.");
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is ChargedProjectileCapabilityParameters charge:
                if (charge.MaxChargeTicks == 0 || charge.Tier2Ticks == 0 ||
                    charge.Tier2Ticks >= charge.Tier3Ticks || charge.Tier3Ticks > charge.MaxChargeTicks)
                    d.Error("value.out-of-range", "character.operation.parameters.tier2Ticks",
                        "Charge thresholds must satisfy 0 < tier2Ticks < tier3Ticks <= maxChargeTicks.");
                ValidateFiniteValues(new[] { charge.Tier2Radius, charge.Tier3Radius }, "character.operation.parameters", d);
                if (charge.Projectile.Return != null)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile.return",
                        "A charged projectile cannot return; use spawnProjectile.");
                if (charge.Projectile.Radius <= 0f || charge.Tier2Radius <= charge.Projectile.Radius ||
                    charge.Tier3Radius <= charge.Tier2Radius || charge.Projectile.Speed <= 0f ||
                    charge.Projectile.MaxFlightTicks == 0 || charge.FireTriggerTick == 0)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile",
                        "Tier radii must be positive and increasing; speed, flight lifetime, and fire startup must be positive.");
                ValidateOperation(new SpawnProjectileOperationSource(0, AuthoringUnit.Meters, charge.Projectile), c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is FingerGunsCapabilityParameters guns:
                if (!ValidFingerGuns(guns.FireTriggerTick, guns.RecoveryTicks, guns.ShotsPerReload,
                    guns.ReloadTicks, guns.AirMaxFallSpeed, guns.AirRecoilSpeed))
                    d.Error("value.out-of-range", "character.operation.parameters",
                        "Finger Guns requires fire ticks 1..120, recovery 1..240, shots 1..24, reload 1..600, and air speeds 0..30 m/s.");
                if (guns.Projectile.Return != null || guns.Projectile.Radius <= 0f ||
                    guns.Projectile.Speed <= 0f || guns.Projectile.MaxFlightTicks == 0)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile",
                        "Finger Guns needs a non-returning projectile with positive radius, speed and flight lifetime.");
                ValidateOperation(new SpawnProjectileOperationSource(0, AuthoringUnit.Meters, guns.Projectile), c, d);
                break;
            case StartCapabilityOperationSource capability when capability.Parameters is ChargedStreamCapabilityParameters stream:
                ValidateFiniteValues(new[] { stream.MaxBaseKnockback, stream.MinRecoilSpeed, stream.MaxRecoilSpeed },
                    "character.operation.parameters", d);
                if (stream.MaxChargeTicks == 0 || stream.FireTriggerTick == 0 || stream.EmitIntervalTicks == 0 ||
                    stream.MinStreamTicks == 0 || stream.MaxStreamTicks < stream.MinStreamTicks)
                    d.Error("value.out-of-range", "character.operation.parameters.minStreamTicks",
                        "Charge, startup, interval and stream lengths must be positive, and maxStreamTicks at least minStreamTicks.");
                if (stream.MaxBaseKnockback < stream.Projectile.BaseKnockback || stream.MinRecoilSpeed < 0f ||
                    stream.MaxRecoilSpeed < stream.MinRecoilSpeed)
                    d.Error("value.out-of-range", "character.operation.parameters.maxBaseKnockback",
                        "maxBaseKnockback must be at least the projectile's baseKnockback, and 0 <= minRecoilSpeed <= maxRecoilSpeed.");
                if (stream.Projectile.Return != null)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile.return",
                        "A stream's projectiles cannot return.");
                if (stream.Projectile.Radius <= 0f || stream.Projectile.Speed <= 0f || stream.Projectile.MaxFlightTicks == 0)
                    d.Error("value.out-of-range", "character.operation.parameters.projectile",
                        "A stream's projectiles need a positive radius, speed and flight lifetime.");
                ValidateOperation(new SpawnProjectileOperationSource(0, AuthoringUnit.Meters, stream.Projectile), c, d);
                break;
            case StartupAimCorrectionOperationSource:
                break;
        }
    }

    private static void ValidateBoneReference(string? id, CharacterAuthoringDocument c, string path, DiagnosticBag d)
    {
        if (id != null && CharacterBoneRegistry.Find(id)?.Required != true && !c.HurtboxBoneDefs.Any(x => x.BoneId == id || x.EndBoneId == id) && !c.AttachmentBoneIds.Contains(id, StringComparer.Ordinal))
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
             (placement.BoneId != null && CharacterBoneRegistry.Find(placement.BoneId)?.Required != true && !c.HurtboxBoneDefs.Any(x => x.BoneId == placement.BoneId || x.EndBoneId == placement.BoneId) &&
              !c.AttachmentBoneIds.Contains(placement.BoneId, StringComparer.Ordinal))))
            d.Error("reference.unresolved", "character.operation.placement.boneId", "Presentation bone must be a declared bone.");
    }
    private static void ValidateDashStrike(
        DashStrikeCapabilityParameters x, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        ValidateCapabilityHitbox(x.LightHitbox, c, "lightHitbox", d);
        ValidateCapabilityHitbox(x.TipHitbox, c, "tipHitbox", d);
        if (DashStrikeContract.BoundsError((CookedDashStrikeCapabilityParameters)CookParameters(x)) is { } error)
            d.Error("value.out-of-range", "character.operation.parameters", error);
        if (!string.IsNullOrEmpty(x.TipPresentationId) && !c.PresentationIds.Contains(x.TipPresentationId, StringComparer.Ordinal))
            d.Error("reference.unresolved", "character.operation.parameters.tipPresentationId", "Tip presentation ID is not declared.");
    }

    private static void ValidateFlashCut(
        FlashCutCapabilityParameters x, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        ValidateCapabilityHitbox(x.CutHitbox, c, "cutHitbox", d);
        var cooked = (CookedFlashCutCapabilityParameters)CookParameters(x);
        if (FlashCutContract.BoundsError(cooked) is { } error)
            d.Error("value.out-of-range", "character.operation.parameters", error);
        foreach (var (id, field) in FlashCutContract.PresentationIds(cooked))
            if (!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", "character.operation.parameters." + field, "Flash cut presentation ID is not declared.");
    }

    /// <summary>
    /// The homing and perch blocks (ADR-0051). A perched projectile pops on a hit from its thrower's other form, so
    /// it needs the second form; its pop presentation must be declared.
    /// </summary>
    private static void ValidateHomingAndPerch(ProjectileSource projectile, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var cooked = new CookedProjectile(projectile.LaunchOffsetX, projectile.LaunchOffsetY, projectile.LaunchOffsetZ,
            projectile.Speed, projectile.Gravity, projectile.Radius, projectile.Damage, projectile.Angle,
            projectile.BaseKnockback, projectile.KnockbackGrowth, projectile.StunTicks, projectile.MaxFlightTicks,
            projectile.YawOffsetDegrees, projectile.Return == null ? null : new CookedProjectileReturn(0, 0f, 0f, 0f, 0),
            ProjectilePerchContract.Cook(projectile.Homing), ProjectilePerchContract.Cook(projectile.Perch));
        if (ProjectilePerchContract.BoundsError(cooked) is { } error)
            d.Error("value.out-of-range", "character.projectile", error);
        if (projectile.ChallengeTicks > 600)
            d.Error("value.out-of-range", "character.projectile.challengeTicks", "challengeTicks must be at most 600.");
        if (projectile.Perch == null) return;
        if (c.AlternateForm == null)
            d.Error("capability.ambiguous", "character.projectile.perch",
                "A perched projectile pops on a hit from the thrower's other form: it needs slop.ability.forms.v1.");
        foreach (var (id, field) in new[] { (projectile.Perch.Pop.PresentationId, "pop.presentationId"),
                     (projectile.Perch.Peck?.PresentationId ?? "", "peck.presentationId"),
                     (projectile.Perch.ScatterPresentationId, "scatterPresentationId") })
            if (id.Length > 0 && !c.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", "character.projectile.perch." + field, "Presentation ID is not declared.");
    }

    private static void ValidateProjectileReturn(ProjectileSource projectile, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        const string path = "character.projectile.return";
        var x = projectile.Return!;
        if (x.Hit is { } hit && BonkSpecialContract.LegHit(hit) is { } hitError)
            d.Error("value.out-of-range", path + ".hit", hitError);
        if (projectile.ChallengeTicks > 600) d.Error("value.out-of-range", path, "challengeTicks must be at most 600.");
        ValidateFiniteValues(new[] { x.ReturnSpeed, x.ReturnAcceleration, x.SelfHitDamage }, path, d);
        if (!(projectile.Speed > 0f) || projectile.Gravity != 0f)
            d.Error("value.out-of-range", path, "A returning projectile needs a positive speed and no gravity.");
        if (x.OutboundTicks == 0 || x.OutboundTicks >= projectile.MaxFlightTicks)
            d.Error("value.out-of-range", path + ".outboundTicks",
                "Outbound time must be positive and shorter than the projectile's flight lifetime.");
        if (!(x.ReturnSpeed > 0f) || !(x.ReturnAcceleration > 0f))
            d.Error("value.out-of-range", path + ".returnSpeed", "Return speed and acceleration must be positive.");
        ValidateNonNegative(x.SelfHitDamage, path + ".selfHitDamage", d);
        if (x.SelfHitStunTicks == 0 || x.SelfHitStunTicks > MaxFixedHitstunTicks)
            d.Error("value.out-of-range", path + ".selfHitStunTicks", "Self-hit stun must be between 1 and 240 ticks.");
        foreach (var (id, field) in new[] { (x.CatchPresentationId, "catchPresentationId"), (x.SelfHitPresentationId, "selfHitPresentationId") })
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!id.StartsWith("presentation.", StringComparison.Ordinal))
                d.Error("id.invalid", path + "." + field, "Return presentation ID must use the presentation. prefix.");
            else if (!c.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", path + "." + field, "Return presentation ID is not declared.");
        }
    }

    private static void ValidateSteeredCharge(SteeredChargeCapabilityParameters x, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        const string path = "character.operation.parameters";
        ValidateFiniteValues(new[] { x.GallopSpeed, x.TurnDegreesPerSecond }, path, d);
        if (!(x.GallopSpeed > 0f))
            d.Error("value.out-of-range", path + ".gallopSpeed", "Gallop speed must be positive.");
        if (!(x.TurnDegreesPerSecond >= 0f && x.TurnDegreesPerSecond <= MaxSteeredChargeTurnDegreesPerSecond))
            d.Error("value.out-of-range", path + ".turnDegreesPerSecond",
                $"Turn rate must be between 0 and {MaxSteeredChargeTurnDegreesPerSecond} degrees per second.");
        if (x.MaxGallopTicks == 0 || x.HitEndingTicks == 0 || x.MissEndingTicks == 0)
            d.Error("value.out-of-range", path + ".maxGallopTicks", "Gallop, hit ending and miss ending must be positive.");
        if (x.SkidTicks > x.MissEndingTicks)
            d.Error("value.out-of-range", path + ".skidTicks", "The skid must fit within the miss ending.");
        if (!string.IsNullOrEmpty(x.GallopAnimationId) && x.GallopLoopTicks == 0)
            d.Error("value.out-of-range", path + ".gallopLoopTicks", "A gallop clip needs a positive loop length.");
        ValidateCapabilityHitbox(x.LanceHitbox, c, "lanceHitbox", d);
        ValidateCapabilityHitbox(x.ChestHitbox, c, "chestHitbox", d);
        if (BonkSpecialContract.Charge((CookedSteeredChargeCapabilityParameters)CookParameters(x)) is { } error)
            d.Error("value.out-of-range", path, error);
        if (x.LanceHitbox.HitGroup != 0)
            d.Error("value.out-of-range", path + ".lanceHitbox.hitGroup", "The lance keeps its own hit history (hitGroup 0).");
        foreach (var (id, field) in new[]
        {
            (x.GallopAnimationId, "gallopAnimationId"),
            (x.HitEndingAnimationId, "hitEndingAnimationId"),
            (x.MissEndingAnimationId, "missEndingAnimationId"),
            (x.AirEndingAnimationId, "airEndingAnimationId"),
            (x.Carry.FlingAnimationId, "carry.flingAnimationId"),
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            ValidateId(id, path + "." + field, d);
            if (!IsKnownAnimation(id, c))
                d.Error("reference.unresolved", path + "." + field, "Animation ID is not declared.");
        }
        foreach (var (id, field) in new[]
        {
            (x.GallopPresentationId, "gallopPresentationId"),
            (x.HitEndingPresentationId, "hitEndingPresentationId"),
            (x.MissEndingPresentationId, "missEndingPresentationId"),
            (x.AirEndingPresentationId, "airEndingPresentationId"),
            (x.ChallengePresentationId, "challengePresentationId"), (x.Carry.ScoopPresentationId, "carry.scoopPresentationId"),
            (x.Carry.FlingPresentationId, "carry.flingPresentationId"),
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!id.StartsWith("presentation.", StringComparison.Ordinal))
                d.Error("id.invalid", path + "." + field, "Charge presentation ID must use the presentation. prefix.");
            else if (!c.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", path + "." + field, "Charge presentation ID is not declared.");
        }
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
        ValidateHitPresentation(x, c, path + ".hitPresentationId", d);
    }

    private static void ValidateHitPresentation(HitboxSource x, CharacterAuthoringDocument c, string path, DiagnosticBag d)
    {
        if (x.HitPresentationId != null && !c.PresentationIds.Contains(x.HitPresentationId, StringComparer.Ordinal))
            d.Error("reference.unresolved", path, "Hit presentation ID is not declared.");
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
        foreach (var (id, field) in new[]
        {
            (parameters.RisePresentationId, "risePresentationId"),
            (parameters.HangPresentationId, "hangPresentationId"), (parameters.LockPresentationId, "lockPresentationId"),
            (parameters.LandingPresentationId, "landingPresentationId"),
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!id.StartsWith("presentation.", StringComparison.Ordinal))
                d.Error("id.invalid", path + "." + field, "Leap presentation ID must use the presentation. prefix.");
            else if (!character.PresentationIds.Contains(id, StringComparer.Ordinal))
                d.Error("reference.unresolved", path + "." + field, "Leap presentation ID is not declared.");
        }
        ValidateFiniteValues(new[] { parameters.RiseHeight, parameters.MaxRange, parameters.NoTargetDistance }, path, d);
        if (parameters.RiseHeight <= 0 || parameters.MaxRange <= 0 || parameters.NoTargetDistance < 0
            || parameters.NoTargetDistance > parameters.MaxRange || parameters.RiseTicks == 0 || parameters.HangTicks == 0
            || parameters.DropTicks == 0 || parameters.MaxFallTicks == 0 || parameters.HitRecoveryTicks == 0
            || parameters.MissRecoveryTicks < parameters.HitRecoveryTicks)
            d.Error("value.out-of-range", path, "Leap beats, range and recoveries are invalid.");
        if (BonkSpecialContract.Leap((CookedTargetedLeapCapabilityParameters)CookParameters(parameters)) is { } leapError)
            d.Error("value.out-of-range", path, leapError);
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
        if (hitbox.DurationTicks == 0 || hitbox.DurationTicks > parameters.MissRecoveryTicks)
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
        for (var i = 0; i < c.HurtboxBoneDefs.Count; i++)
        {
            var bone = c.HurtboxBoneDefs[i];
            string path = $"character.hurtboxBoneDefs[{i}]";
            ValidateHurtboxBoneId(bone.BoneId, path + ".boneId", d);
            if (bone.EndBoneId != null) ValidateHurtboxBoneId(bone.EndBoneId, path + ".endBoneId", d);
            if (!seenHurtboxBones.Add(bone.BoneId)) d.Error("id.duplicate", path + ".boneId", "Duplicate bone ID.");
            ValidateNonNegative(bone.Radius, path + ".radius", d);
            if (bone.EndBoneId == null && (bone.EndOffsetX != 0f || bone.EndOffsetY != 0f || bone.EndOffsetZ != 0f))
                d.Error("reference.unresolved", path + ".endBoneId", "End offsets require an end bone.");
        }
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
            (c.Presentation.WallJump, "wallJump"),
            (c.Presentation.Shield, "shield"),
            (c.Presentation.Grab, "grab"),
            (c.Presentation.Grabbed, "grabbed"),
            (c.Presentation.ThrowForward, "throwForward"),
            (c.Presentation.AirDodge, "airDodge"),
            (c.Presentation.Shell, "shell"),
            (c.Presentation.Flying, "flying"),
            (c.Presentation.MayhemLaugh, "mayhemLaugh")
        })
        {
            if (string.IsNullOrEmpty(id)) continue;
            ValidateId(id, $"character.presentation.{field}", d);
            if (!standardAnimations.Add(id)) d.Error("id.duplicate", "character.presentation", "Duplicate standard animation ID.");
        }
        foreach (var stageId in c.PresentationIds) if (!PresentationUsed(stageId, c)) d.Warning("presentation.unused-id", "character.presentationIds", "Declared presentation ID is not referenced by an attack or timeline operation.");
    }

    private static bool PresentationUsed(string id, CharacterAuthoringDocument c)
        => AllSlots(c).Any(slot => slot.HitPresentationId == id)
            || c.Shell?.Splash?.PresentationId == id || c.Shell?.Ride?.HitPresentationId == id
            || AllSlots(c).SelectMany(s => s.Timeline.Stages).SelectMany(s => s.Operations).Any(op =>
            op is EmitPresentationOperationSource emit && emit.PresentationId == id ||
            op is SpawnHitboxOperationSource hitbox && hitbox.Hitbox.HitPresentationId == id ||
            op is SpawnProjectileOperationSource { Projectile.Return: { } back }
                && (back.CatchPresentationId == id || back.SelfHitPresentationId == id) ||
            op is StartCapabilityOperationSource capability && (capability.Parameters switch
            {
                TargetedLeapCapabilityParameters p => p.RisePresentationId == id || p.HangPresentationId == id || p.LockPresentationId == id || p.LandingPresentationId == id,
                CannonballCapabilityParameters p => p.LaunchPresentationId == id,
                FlashCutCapabilityParameters p => p.LockPresentationId == id || p.RevealPresentationId == id
                    || p.SheathePresentationId == id || p.PopPresentationId == id,
                SteeredChargeCapabilityParameters p => p.GallopPresentationId == id || p.HitEndingPresentationId == id
                    || p.MissEndingPresentationId == id || p.AirEndingPresentationId == id || p.ChallengePresentationId == id
                    || p.Carry.ScoopPresentationId == id || p.Carry.FlingPresentationId == id,
                MankiRoundBombCapabilityParameters p => p.ExplosionPresentationId == id,
                MankiJetpackBoostCapabilityParameters p => p.ExplosionPresentationId == id,
                MankiKamikazeCapabilityParameters p => p.ExplosionPresentationId == id,
                MankiBazookaCapabilityParameters p => p.ExplosionPresentationId == id,
                _ => false,
            }));

    private static IEnumerable<CharacterSlotSource> AllSlots(CharacterAuthoringDocument c)
        => c.AlternateForm == null ? c.Slots : c.Slots.Concat(c.AlternateForm.Slots);

    private static void ValidateExplosionPresentationId(TypedCapabilityParameters parameters, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        string id = parameters switch
        {
            MankiRoundBombCapabilityParameters x => x.ExplosionPresentationId,
            MankiJetpackBoostCapabilityParameters x => x.ExplosionPresentationId,
            MankiKamikazeCapabilityParameters x => x.ExplosionPresentationId,
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
        floats.AddRange(c.HurtboxBoneDefs.SelectMany(x => new[] { x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius, x.EndOffsetX, x.EndOffsetY, x.EndOffsetZ }));
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
            SwapFormOperationSource x => x with { },
        _ => throw new InvalidDataException("Unknown operation.")
        };

    private static TypedCapabilityParameters CloneParameters(TypedCapabilityParameters p)
        => p switch
        {
            AimedRecoveryCapabilityParameters x => x with { },
            RisingDragonCapabilityParameters x => x with { },
            ChargedProjectileCapabilityParameters x => x with { Projectile = x.Projectile with { } },
            ChargedStreamCapabilityParameters x => x with { Projectile = x.Projectile with { } },
            FingerGunsCapabilityParameters x => x with { Projectile = x.Projectile with { } },
            CycloneKickCapabilityParameters x => x with { },
            DashStrikeCapabilityParameters x => x with
            {
                LightHitbox = x.LightHitbox with { },
                TipHitbox = x.TipHitbox with { }
            },
            WibouRisingSlashCapabilityParameters x => x with { },
            FlashCutCapabilityParameters x => x with { CutHitbox = x.CutHitbox with { } },
            TargetedLeapCapabilityParameters x => x with { Hitbox = x.Hitbox with { } },
            SteeredChargeCapabilityParameters x => x with { LanceHitbox = x.LanceHitbox with { }, ChestHitbox = x.ChestHitbox with { }, Carry = x.Carry with { Fling = x.Carry.Fling with { } } },
            ThrustCapabilityParameters x => x with { TrailHitbox = x.TrailHitbox == null ? null : x.TrailHitbox with { } },
            TetherCapabilityParameters x => x with { Projectile = x.Projectile == null ? null : x.Projectile with { }, AtTarget = x.AtTarget == null ? null : x.AtTarget with { } },
            CommandGrabCapabilityParameters x => x with { Capture = x.Capture with { } },
            CannonballCapabilityParameters x => x with { },
            MankiRoundBombCapabilityParameters x => x with { },
            MankiJetpackBoostCapabilityParameters x => x with { },
            MankiKamikazeCapabilityParameters x => x with { },
            MankiBazookaCapabilityParameters x => x with { },
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
                    case SpawnProjectileOperationSource x: projectiles++; cookedOps.Add(new CookedSpawnProjectileOperation(x.Tick, x.Unit, new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ, x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle, x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks, x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees,
                        x.Projectile.Return is { } back ? new CookedProjectileReturn(back.OutboundTicks, back.ReturnSpeed, back.ReturnAcceleration, back.SelfHitDamage, back.SelfHitStunTicks, back.CatchPresentationId, back.SelfHitPresentationId, back.Hit) : null,
                        ProjectilePerchContract.Cook(x.Projectile.Homing), ProjectilePerchContract.Cook(x.Projectile.Perch), x.Projectile.ChallengeTicks))); break;
                    case SetAimStateOperationSource x: cookedOps.Add(new CookedSetAimStateOperation(x.Tick, x.Unit, x.AimState)); break;
                    case StartCapabilityOperationSource x:
                        capabilities++;
                        if (x.Parameters is TargetedLeapCapabilityParameters or SteeredChargeCapabilityParameters
                            or ThrustCapabilityParameters { TrailHitbox: not null }) hitboxes++;
                        else if (x.Parameters is DashStrikeCapabilityParameters) hitboxes += 2;
                        else if (x.Parameters is FlashCutCapabilityParameters or CommandGrabCapabilityParameters) hitboxes++;
                        else if (x.Parameters is ChargedProjectileCapabilityParameters or TetherCapabilityParameters { AtTarget: null }) projectiles++;
                        else if (x.Parameters is TetherCapabilityParameters) hitboxes++;
                        cookedOps.Add(new CookedStartCapabilityOperation(x.Tick, x.Unit, x.CapabilityId, x.CapabilityVersion, CookParameters(x.Parameters)));
                        break;
                    case StartupAimCorrectionOperationSource x:
                        cookedOps.Add(new CookedStartupAimCorrectionOperation(
                            x.Tick, x.Unit, x.EndTick, x.AcquisitionRange,
                            x.AcquisitionHalfAngleDegrees, x.MaxYawDegrees, x.MaxPitchDegrees,
                            x.YawDegreesPerSecond, x.PitchDegreesPerSecond));
                        break;
                    case EmitPresentationOperationSource x: cookedOps.Add(new CookedEmitPresentationOperation(x.Tick, x.Unit, x.PresentationId, cookedOperationOrdinal, x.Placement, x.Condition)); break;
                    case CompleteTimelineOperationSource x: cookedOps.Add(new CookedCompleteTimelineOperation(x.Tick, x.Unit)); break;
                    case SwapFormOperationSource x: cookedOps.Add(new CookedSwapFormOperation(x.Tick, x.Unit)); break;
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
            x.KnockbackDirection, x.FixedHitstunTicks, x.HitPresentationId, x.RequiresFacingOwner);

    private static CookedCapabilityParameters CookParameters(TypedCapabilityParameters p) => p switch
    {
        AimedRecoveryCapabilityParameters x => new CookedAimedRecoveryCapabilityParameters(
            x.StartupTicks, x.VerticalSpeed, x.DirectionalVerticalSpeed, x.HorizontalSpeed, x.PreparationRiseSpeed, x.Hop,
            x.RiseSteerAcceleration),
        ChargedProjectileCapabilityParameters x => new CookedChargedProjectileCapabilityParameters(
            x.MaxChargeTicks, x.Tier2Ticks, x.Tier3Ticks, x.FireTriggerTick, x.Tier2Radius, x.Tier3Radius,
            new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ,
                x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle,
                x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks,
                x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees, ChallengeTicks: x.Projectile.ChallengeTicks)),
        ChargedStreamCapabilityParameters x => new CookedChargedStreamCapabilityParameters(
            x.MaxChargeTicks, x.FireTriggerTick, x.EmitIntervalTicks, x.MinStreamTicks, x.MaxStreamTicks, x.RecoveryTicks,
            x.MaxBaseKnockback, x.MinRecoilSpeed, x.MaxRecoilSpeed,
            new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ,
                x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle,
                x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks,
                x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees, ChallengeTicks: x.Projectile.ChallengeTicks),
            x.SprayAnimationId, x.SprayLoopTicks, x.EndAnimationId),
        FingerGunsCapabilityParameters x => new CookedFingerGunsCapabilityParameters(
            x.FireTriggerTick, x.RecoveryTicks, x.ShotsPerReload, x.ReloadTicks, x.ReloadAnimationId,
            x.AirMaxFallSpeed, x.AirRecoilSpeed,
            new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ,
                x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle,
                x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks,
                x.Projectile.MaxFlightTicks, x.Projectile.YawOffsetDegrees, ChallengeTicks: x.Projectile.ChallengeTicks)),
        RisingDragonCapabilityParameters x => new CookedRisingDragonCapabilityParameters(x.RiseSpeed, x.RiseTicks, x.RiseDelay),
        CycloneKickCapabilityParameters x => new CookedCycloneKickCapabilityParameters(x.ForwardSpeed, x.WindupTicks, x.HitboxEndTick, x.DurationTicks, x.BodyRadius, x.SideRadius, x.SideOffset, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.StunTicks, x.BodyY, x.SideY, x.HitIntervalTicks, x.FixedHitstunTicks, x.FinisherTick, x.FinisherDamage, x.FinisherKnockbackAngle, x.FinisherKnockbackBase, x.FinisherKnockbackGrowth),
        DashStrikeCapabilityParameters x => new CookedDashStrikeCapabilityParameters(
            x.Distance, x.Speed, x.TipPoseTick, x.RecoveryTicks,
            CookHitbox(x.LightHitbox), CookHitbox(x.TipHitbox), x.TipPresentationId),
        WibouRisingSlashCapabilityParameters x => new CookedWibouRisingSlashCapabilityParameters(x.RiseSpeed, x.RiseTicks, x.HomingRange, x.HomingSpeed),
        FlashCutCapabilityParameters x => new CookedFlashCutCapabilityParameters(
            x.StanceTicks, x.LockRange, x.MaxTargets, x.LegTicks, x.PassThroughDistance, x.SheatheTicks,
            x.RevealLeadTicks, x.RecoveryTicks, x.WhiffTicks, CookHitbox(x.CutHitbox),
            x.StanceAnimationId, x.LegAnimationId, x.SheatheAnimationId,
            x.LockPresentationId, x.RevealPresentationId, x.SheathePresentationId, x.PopPresentationId),
        TargetedLeapCapabilityParameters x => new CookedTargetedLeapCapabilityParameters(x.RiseTicks, x.RiseHeight, x.HangTicks, x.DropTicks, x.MaxRange, x.NoTargetDistance, x.MaxFallTicks, x.HitRecoveryTicks, x.MissRecoveryTicks, CookHitbox(x.Hitbox), x.RiseAnimationId, x.HangAnimationId, x.HangPoseTick, x.DropAnimationId, x.LandingAnimationId, x.HitLandingSeekTick, x.RisePresentationId, x.HangPresentationId, x.LockPresentationId, x.LandingPresentationId),
        TetherCapabilityParameters x => new CookedTetherCapabilityParameters(x.ThrowTick,
            x.Projectile is not { } ring ? null : new CookedProjectile(ring.LaunchOffsetX, ring.LaunchOffsetY, ring.LaunchOffsetZ,
                ring.Speed, ring.Gravity, ring.Radius, ring.Damage, ring.Angle,
                ring.BaseKnockback, ring.KnockbackGrowth, ring.StunTicks,
                ring.MaxFlightTicks, ring.YawOffsetDegrees,
                ring.Return is { } back ? new CookedProjectileReturn(back.OutboundTicks, back.ReturnSpeed,
                    back.ReturnAcceleration, back.SelfHitDamage, back.SelfHitStunTicks) : null),
            x.ReelSpeed, x.AnchorDistance, x.ArrivalStunTicks, x.MaxReelTicks, x.RetractSpeed, x.MissRecoveryTicks,
            x.AirGravityScale, x.ReelAnimationId, x.RetractAnimationId, x.ThrowPresentationId, x.RetractPresentationId,
            x.AtTarget is not { } at ? null : new CookedTetherAtTarget(at.WarningTicks, at.CatchTicks, at.Radius,
                at.Height, at.Damage, at.MaxRange, at.NoTargetDistance, at.WarningPresentationId, at.CatchPresentationId,
                at.Unmarked is not { } hand ? null : new CookedTetherUnmarked(hand.RiseTick, hand.ActiveTicks, hand.Distance,
                    hand.Radius, hand.Height, hand.Damage, hand.Angle, hand.BaseKnockback, hand.KnockbackGrowth,
                    hand.StunTicks, hand.RecoveryTicks, hand.PresentationId))),
        CommandGrabCapabilityParameters x => new CookedCommandGrabCapabilityParameters(x.LungeTicks, x.LungeSpeed,
            x.StopDistance, x.CaptureTick, CookHitbox(x.Capture), x.ReleaseTick,
            x.Launch is not { } launch ? null : new CookedGrabLaunch(launch.Damage, launch.Angle, launch.BaseKnockback,
                launch.KnockbackGrowth, launch.StunTicks), x.CatchPresentationId, x.ReleasePresentationId,
            x.Level, x.AnchorDistance, x.ReleaseLockTicks, x.WhiffRecoveryTicks),
        CannonballCapabilityParameters x => new CookedCannonballCapabilityParameters(x.TakeoffTick, x.MaxRange,
            x.NoTargetDistance, x.MinFlightTicks, x.MaxFlightTicks, x.MaxLaunchSpeed, x.LaunchPresentationId),
        ThrustCapabilityParameters x => new CookedThrustCapabilityParameters(x.Aim, x.TargetConeDegrees, x.NeutralPitchDegrees,
            x.StickPitchDegrees, x.Speed, x.Acceleration, x.MaxSpeed, x.KeepIncomingSpeed, x.ThrustTicks,
            x.TrailHitbox == null ? null : CookHitbox(x.TrailHitbox)),
        SteeredChargeCapabilityParameters x => new CookedSteeredChargeCapabilityParameters(x.GallopSpeed, x.TurnDegreesPerSecond, x.MaxGallopTicks, x.Armored, CookHitbox(x.LanceHitbox), x.HitEndingTicks, x.MissEndingTicks, x.SkidTicks, x.TurnAroundOnMiss, x.AirStrideTicks, x.LookDownTicks, x.ChallengedRearTicks, x.TrackDegreesPerSecond, x.ChallengedTrackDegreesPerSecond, CookHitbox(x.ChestHitbox), new CookedSteeredChargeCarry(x.Carry.AnchorDistance, x.Carry.AnchorHeight, x.Carry.MaxCarryTicks, new CookedGrabLaunch(x.Carry.Fling.Damage, x.Carry.Fling.Angle, x.Carry.Fling.BaseKnockback, x.Carry.Fling.KnockbackGrowth, x.Carry.Fling.StunTicks), x.Carry.FlingTicks, x.Carry.FlingAnimationId, x.Carry.ScoopPresentationId, x.Carry.FlingPresentationId), x.ChallengePresentationId, x.GallopAnimationId, x.GallopLoopTicks, x.HitEndingAnimationId, x.MissEndingAnimationId, x.AirEndingAnimationId, x.GallopPresentationId, x.HitEndingPresentationId, x.MissEndingPresentationId, x.AirEndingPresentationId, x.Assist),
        MankiRoundBombCapabilityParameters x => new CookedMankiRoundBombCapabilityParameters(x.ArmTicks, x.MaxFlightTicks, x.Gravity, x.HitboxRadius, x.ExplosionDamage, x.ExplosionRadius, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionKbAngle, x.ExplosionPresentationId, x.TriggerRadius, x.WarningTicks, x.LifetimeTicks, x.MaxMines),
        MankiJetpackBoostCapabilityParameters x => new CookedMankiJetpackBoostCapabilityParameters(x.StartupTicks, x.VerticalSpeed, x.HorizontalSpeed, x.ExplosionRadius, x.ExplosionDamage, x.ExplosionKbAngle, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionPresentationId, x.FuelTicks, x.SinkSpeed, x.JumpBurstSpeed, x.JumpBurstTicks),
        MankiKamikazeCapabilityParameters x => new CookedMankiKamikazeCapabilityParameters(x.RunTicks, x.Speed, x.ContactRadius, x.ExplosionDamage, x.ExplosionRadius, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionKbAngle, x.SelfPushSpeed, x.ExplosionPresentationId),
        MankiBazookaCapabilityParameters x => new CookedMankiBazookaCapabilityParameters(x.FireTriggerTick, x.ProjectileSpeed, x.HitboxRadius, x.Damage, x.Gravity, x.MaxFlightTicks, x.StunTicks, x.ExplosionRadius, x.KbAngle, x.ExplosionKbBase, x.ExplosionKbGrowth, x.ExplosionStunTicks, x.ExplosionDurationTicks, x.ExplosionKbAngle, x.CastDuration, x.RecoveryDuration, x.HomingTurnDegPerTick,
            (float)Math.Cos(x.HomingTurnDegPerTick * Math.PI / 180d),
            (float)Math.Sin(x.HomingTurnDegPerTick * Math.PI / 180d), x.ExplosionPresentationId, x.ChargeTicks),
        _ => throw new InvalidDataException("Unknown capability parameters.")
    };

    private static CookedMovement CookMovement(CharacterMovementSource x) => new(x.RunSpeed, x.RunAccelerationA, x.RunAccelerationB, x.DashSpeed, x.AirDodgeSpeed, x.AirSpeedMax, x.AirAccelStick, x.AirAccelBase, x.JumpForce, x.ShortHopForce, x.AirJumpVMultiplier, x.AirJumpHMultiplier, x.Gravity, x.AirFloatGravity, x.DashDurationTicks, x.DashCooldownTicks, x.GroundFriction, x.AirFriction, x.MaxFallSpeed, x.FastFallSpeed, x.MaxJumps, x.JumpSquatTicks, x.FloatWindowTicks, x.RushTicks);
    private static CookedPresentation CookPresentation(CharacterPresentationSource x)
        => new(x.Idle, x.Run, x.Dash, x.Jump, x.Fall, x.HitSmall, x.HitMedium, x.HitHard,
            x.LandStartOffsetSeconds, x.ModelResourcePath, x.VisualScale, x.HurtboxBoneScale,
            x.ModelYOffset, x.ModelSoleOffset, x.AutoModelYOffset, x.Tumble, x.Crouch, x.Slide,
            x.Shield, x.Grab, x.Grabbed, x.ThrowForward, x.AirDodge, x.Shell, x.Flying, x.MayhemLaugh, x.WallJump);
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
            writer.WritePropertyName("hurtboxBoneDefs"); writer.WriteStartArray(); foreach (var x in definition.HurtboxBoneDefs.OrderBy(x => x.BoneId, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("boneId", x.BoneId); Number(writer, "offsetX", x.OffsetX); Number(writer, "offsetY", x.OffsetY); Number(writer, "offsetZ", x.OffsetZ); Number(writer, "radius", x.Radius); if (x.EndBoneId != null) writer.WriteString("endBoneId", x.EndBoneId); Number(writer, "endOffsetX", x.EndOffsetX); Number(writer, "endOffsetY", x.EndOffsetY); Number(writer, "endOffsetZ", x.EndOffsetZ); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("attachmentBoneIds"); writer.WriteStartArray(); foreach (var x in definition.AttachmentBoneIds) writer.WriteStringValue(x); writer.WriteEndArray();
            writer.WritePropertyName("presentationIds"); writer.WriteStartArray(); foreach (var x in definition.PresentationIds) writer.WriteStringValue(x); writer.WriteEndArray();
            writer.WritePropertyName("capabilityRequirements"); writer.WriteStartArray(); foreach (var x in definition.CapabilityRequirements) { writer.WriteStartObject(); writer.WriteString("capabilityId", x.CapabilityId); writer.WriteString("capabilityVersion", x.CapabilityVersion); writer.WriteEndObject(); } writer.WriteEndArray();
            if (definition.Shell != null) WriteShell(writer, definition.Shell);
            writer.WritePropertyName("slots"); writer.WriteStartArray(); foreach (var x in definition.Slots.OrderBy(x => x.Ordinal)) WriteSlot(writer, x); writer.WriteEndArray();
            if (definition.AlternateForm != null) WriteAlternateForm(writer, definition.AlternateForm);
            writer.WriteEndObject();
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
        if (!string.IsNullOrEmpty(x.WallJump)) w.WriteString("wallJump", x.WallJump);
        if (!string.IsNullOrEmpty(x.Shield)) w.WriteString("shield", x.Shield);
        if (!string.IsNullOrEmpty(x.Grab)) w.WriteString("grab", x.Grab);
        if (!string.IsNullOrEmpty(x.Grabbed)) w.WriteString("grabbed", x.Grabbed);
        if (!string.IsNullOrEmpty(x.ThrowForward)) w.WriteString("throwForward", x.ThrowForward);
        if (!string.IsNullOrEmpty(x.AirDodge)) w.WriteString("airDodge", x.AirDodge);
        if (!string.IsNullOrEmpty(x.Shell)) w.WriteString("shell", x.Shell);
        if (!string.IsNullOrEmpty(x.Flying)) w.WriteString("flying", x.Flying);
        if (!string.IsNullOrEmpty(x.MayhemLaugh)) w.WriteString("mayhemLaugh", x.MayhemLaugh);
        Number(w, "landStartOffsetSeconds", x.LandStartOffsetSeconds);
        w.WriteString("modelResourcePath", x.ModelResourcePath);
        Number(w, "visualScale", x.VisualScale);
        Number(w, "hurtboxBoneScale", x.HurtboxBoneScale);
        Number(w, "modelYOffset", x.ModelYOffset);
        Number(w, "modelSoleOffset", x.ModelSoleOffset);
        w.WriteBoolean("autoModelYOffset", x.AutoModelYOffset);
        w.WriteEndObject();
    }

    private static void WriteAlternateForm(Utf8JsonWriter w, CookedAlternateForm x)
    {
        w.WritePropertyName("alternateForm");
        w.WriteStartObject();
        w.WriteString("id", x.Id);
        w.WriteString("displayName", x.DisplayName);
        WriteMovement(w, x.Movement);
        w.WritePropertyName("presentation");
        w.WriteStartObject();
        foreach (var (name, id) in x.Presentation.Roles)
            if (!string.IsNullOrEmpty(id)) w.WriteString(name, id);
        w.WriteEndObject();
        w.WritePropertyName("slots"); w.WriteStartArray(); foreach (var slot in x.Slots.OrderBy(slot => slot.Ordinal)) WriteSlot(w, slot); w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteShell(Utf8JsonWriter w, CookedShellDefinition x)
    {
        w.WritePropertyName("shell");
        w.WriteStartObject();
        Number(w, "popOutDeceleration", x.PopOutDeceleration);
        Number(w, "blockedRecoilSpeed", x.BlockedRecoilSpeed);
        if (x.Splash is { } splash)
        {
            w.WritePropertyName("splash");
            w.WriteStartObject();
            WriteHitboxObject(w, splash.Hitbox, "hitbox");
            w.WriteNumber("popUpTicks", splash.PopUpTicks);
            if (!string.IsNullOrEmpty(splash.PresentationId)) w.WriteString("presentationId", splash.PresentationId);
            w.WriteEndObject();
        }
        if (x.Ride is { } ride)
        {
            w.WritePropertyName("ride");
            w.WriteStartObject();
            w.WriteNumber("ticks", ride.Ticks);
            Number(w, "speed", ride.Speed);
            Number(w, "turnDegreesPerSecond", ride.TurnDegreesPerSecond);
            WriteHitboxObject(w, ride.Wave, "wave");
            if (!string.IsNullOrEmpty(ride.HitPresentationId)) w.WriteString("hitPresentationId", ride.HitPresentationId);
            w.WriteEndObject();
        }
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
        if (x.AimMovementSpeedScale != 1f) Number(w, "aimMovementSpeedScale", x.AimMovementSpeedScale);
        if (x.AimAnimationId != null)
            w.WriteString("aimAnimationId", x.AimAnimationId);
        w.WriteNumber("cooldownTicks", x.CooldownTicks);
        w.WriteBoolean("isRecoveryMove", x.IsRecoveryMove);
        w.WriteBoolean("preserveMomentumOnStart", x.PreserveMomentumOnStart);
        w.WriteBoolean("allowSlideCarry", x.AllowSlideCarry);
        if (!string.IsNullOrEmpty(x.HitPresentationId)) w.WriteString("hitPresentationId", x.HitPresentationId);
        if (x.ShellPolicy != ShellPolicy.PopOut) w.WriteNumber("shellPolicy", (byte)x.ShellPolicy);
        if (x.Form != 0) w.WriteNumber("form", x.Form);
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
                if (p.Condition != AuthoringPresentationCondition.Always) w.WriteNumber("condition", (byte)p.Condition);
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
            case CookedSwapFormOperation:
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
    private static void WriteHitbox(Utf8JsonWriter w, CookedHitbox x) { w.WritePropertyName("hitbox"); w.WriteStartObject(); w.WriteNumber("shape", (byte)x.Shape); Number(w, "radius", x.Radius); Number(w, "offsetX", x.OffsetX); Number(w, "offsetY", x.OffsetY); Number(w, "offsetZ", x.OffsetZ); Number(w, "endOffsetX", x.EndOffsetX); Number(w, "endOffsetY", x.EndOffsetY); Number(w, "endOffsetZ", x.EndOffsetZ); if (x.StartBoneId != null) w.WriteString("startBoneId", x.StartBoneId); else w.WriteNull("startBoneId"); if (x.EndBoneId != null) w.WriteString("endBoneId", x.EndBoneId); else w.WriteNull("endBoneId"); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("durationTicks", x.DurationTicks); w.WriteBoolean("interruptible", x.Interruptible); w.WriteNumber("hitGroup", x.HitGroup); w.WriteNumber("knockbackDirection", (byte)x.KnockbackDirection); if (x.FixedHitstunTicks > 0) w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks); if (x.RequiresFacingOwner) w.WriteBoolean("requiresFacingOwner", true); if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId); w.WriteEndObject(); }
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
        w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks);
        if (x.RequiresFacingOwner) w.WriteBoolean("requiresFacingOwner", true); if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId);
        w.WriteEndObject();
    }
    private static void WriteProjectile(Utf8JsonWriter w, CookedProjectile x) { w.WritePropertyName("projectile"); w.WriteStartObject(); Number(w, "launchOffsetX", x.LaunchOffsetX); Number(w, "launchOffsetY", x.LaunchOffsetY); Number(w, "launchOffsetZ", x.LaunchOffsetZ); Number(w, "speed", x.Speed); Number(w, "gravity", x.Gravity); Number(w, "radius", x.Radius); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); Number(w, "yawOffsetDegrees", x.YawOffsetDegrees); if (x.Return is { } back) { w.WritePropertyName("return"); w.WriteStartObject(); w.WriteNumber("outboundTicks", back.OutboundTicks); Number(w, "returnSpeed", back.ReturnSpeed); Number(w, "returnAcceleration", back.ReturnAcceleration); Number(w, "selfHitDamage", back.SelfHitDamage); w.WriteNumber("selfHitStunTicks", back.SelfHitStunTicks); OptionalString(w, "catchPresentationId", back.CatchPresentationId); OptionalString(w, "selfHitPresentationId", back.SelfHitPresentationId); if (back.Hit is { } h) { w.WritePropertyName("hit"); System.Text.Json.JsonSerializer.Serialize(w, h, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); } w.WriteEndObject(); } if (x.ChallengeTicks > 0) w.WriteNumber("challengeTicks", x.ChallengeTicks); WriteHomingAndPerch(w, x); w.WriteEndObject(); }

    private static void WriteHomingAndPerch(Utf8JsonWriter w, CookedProjectile x)
    {
        if (x.Homing is { } homing)
        {
            w.WritePropertyName("homing"); w.WriteStartObject();
            Number(w, "turnDegreesPerTick", homing.TurnDegreesPerTick);
            Number(w, "turnCos", homing.TurnCos); Number(w, "turnSin", homing.TurnSin);
            w.WriteEndObject();
        }
        if (x.Perch is not { } perch) return;
        w.WritePropertyName("perch"); w.WriteStartObject();
        w.WriteNumber("perchTicks", perch.PerchTicks);
        w.WritePropertyName("pop"); w.WriteStartObject();
        Number(w, "damage", perch.Pop.Damage); Number(w, "angle", perch.Pop.Angle);
        Number(w, "baseKnockback", perch.Pop.BaseKnockback); Number(w, "knockbackGrowth", perch.Pop.KnockbackGrowth);
        w.WriteNumber("stunTicks", perch.Pop.StunTicks);
        OptionalString(w, "presentationId", perch.Pop.PresentationId);
        w.WriteEndObject();
        if (perch.Dive is { } dive)
        {
            w.WritePropertyName("dive"); w.WriteStartObject();
            Number(w, "hoverHeight", dive.HoverHeight); w.WriteNumber("maxRiseTicks", dive.MaxRiseTicks);
            w.WriteNumber("markTicks", dive.MarkTicks); Number(w, "diveSpeed", dive.DiveSpeed);
            Number(w, "noTargetDistance", dive.NoTargetDistance);
            w.WriteEndObject();
        }
        if (perch.Peck is { } peck)
        {
            w.WritePropertyName("peck"); w.WriteStartObject();
            w.WriteNumber("intervalTicks", peck.IntervalTicks); Number(w, "damage", peck.Damage);
            OptionalString(w, "presentationId", peck.PresentationId);
            w.WriteEndObject();
        }
        OptionalString(w, "scatterPresentationId", perch.ScatterPresentationId);
        w.WriteEndObject();
    }
    private static void WriteParameters(Utf8JsonWriter w, CookedCapabilityParameters p)
    {
        w.WriteStartObject();
        switch (p)
        {
            case CookedAimedRecoveryCapabilityParameters x:
                w.WriteNumber("startupTicks", x.StartupTicks); Number(w, "verticalSpeed", x.VerticalSpeed);
                Number(w, "directionalVerticalSpeed", x.DirectionalVerticalSpeed); Number(w, "horizontalSpeed", x.HorizontalSpeed);
                Number(w, "preparationRiseSpeed", x.PreparationRiseSpeed);
                WriteRecoveryHop(w, x.Hop);
                if (x.RiseSteerAcceleration > 0f) Number(w, "riseSteerAcceleration", x.RiseSteerAcceleration); break;
            case CookedChargedProjectileCapabilityParameters x:
                w.WriteNumber("maxChargeTicks", x.MaxChargeTicks); w.WriteNumber("tier2Ticks", x.Tier2Ticks);
                w.WriteNumber("tier3Ticks", x.Tier3Ticks); w.WriteNumber("fireTriggerTick", x.FireTriggerTick);
                Number(w, "tier2Radius", x.Tier2Radius); Number(w, "tier3Radius", x.Tier3Radius);
                WriteProjectile(w, x.Projectile); break;
            case CookedChargedStreamCapabilityParameters x:
                w.WriteNumber("maxChargeTicks", x.MaxChargeTicks); w.WriteNumber("fireTriggerTick", x.FireTriggerTick);
                w.WriteNumber("emitIntervalTicks", x.EmitIntervalTicks); w.WriteNumber("minStreamTicks", x.MinStreamTicks);
                w.WriteNumber("maxStreamTicks", x.MaxStreamTicks); w.WriteNumber("recoveryTicks", x.RecoveryTicks);
                Number(w, "maxBaseKnockback", x.MaxBaseKnockback); Number(w, "minRecoilSpeed", x.MinRecoilSpeed);
                Number(w, "maxRecoilSpeed", x.MaxRecoilSpeed); WriteProjectile(w, x.Projectile);
                if (x.SprayAnimationId.Length > 0) w.WriteString("sprayAnimationId", x.SprayAnimationId);
                if (x.SprayLoopTicks > 0) w.WriteNumber("sprayLoopTicks", x.SprayLoopTicks);
                if (x.EndAnimationId.Length > 0) w.WriteString("endAnimationId", x.EndAnimationId);
                break;
            case CookedFingerGunsCapabilityParameters x:
                w.WriteNumber("fireTriggerTick", x.FireTriggerTick); w.WriteNumber("recoveryTicks", x.RecoveryTicks);
                w.WriteNumber("shotsPerReload", x.ShotsPerReload); w.WriteNumber("reloadTicks", x.ReloadTicks);
                w.WriteString("reloadAnimationId", x.ReloadAnimationId);
                Number(w, "airMaxFallSpeed", x.AirMaxFallSpeed); Number(w, "airRecoilSpeed", x.AirRecoilSpeed);
                WriteProjectile(w, x.Projectile); break;
            case CookedRisingDragonCapabilityParameters x: Number(w, "riseSpeed", x.RiseSpeed); w.WriteNumber("riseTicks", x.RiseTicks); w.WriteNumber("riseDelay", x.RiseDelay); break;
            case CookedCycloneKickCapabilityParameters x: Number(w, "forwardSpeed", x.ForwardSpeed); w.WriteNumber("windupTicks", x.WindupTicks); w.WriteNumber("hitboxEndTick", x.HitboxEndTick); w.WriteNumber("durationTicks", x.DurationTicks); Number(w, "bodyRadius", x.BodyRadius); Number(w, "sideRadius", x.SideRadius); Number(w, "sideOffset", x.SideOffset); Number(w, "damage", x.Damage); Number(w, "knockbackAngle", x.KnockbackAngle); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); Number(w, "bodyY", x.BodyY); Number(w, "sideY", x.SideY); WriteCycloneCarry(w, x.HitIntervalTicks, x.FixedHitstunTicks, x.FinisherTick, x.FinisherDamage, x.FinisherKnockbackAngle, x.FinisherKnockbackBase, x.FinisherKnockbackGrowth); break;
            case CookedDashStrikeCapabilityParameters x:
                Number(w, "distance", x.Distance); Number(w, "speed", x.Speed);
                w.WriteNumber("tipPoseTick", x.TipPoseTick); w.WriteNumber("recoveryTicks", x.RecoveryTicks);
                WriteHitboxObject(w, x.LightHitbox, "lightHitbox"); WriteHitboxObject(w, x.TipHitbox, "tipHitbox");
                w.WriteString("tipPresentationId", x.TipPresentationId); break;
            case CookedWibouRisingSlashCapabilityParameters x: Number(w, "riseSpeed", x.RiseSpeed); w.WriteNumber("riseTicks", x.RiseTicks); Number(w, "homingRange", x.HomingRange); Number(w, "homingSpeed", x.HomingSpeed); break;
            case CookedFlashCutCapabilityParameters x:
                w.WriteNumber("stanceTicks", x.StanceTicks); Number(w, "lockRange", x.LockRange);
                w.WriteNumber("maxTargets", x.MaxTargets); w.WriteNumber("legTicks", x.LegTicks);
                Number(w, "passThroughDistance", x.PassThroughDistance); w.WriteNumber("sheatheTicks", x.SheatheTicks);
                w.WriteNumber("revealLeadTicks", x.RevealLeadTicks); w.WriteNumber("recoveryTicks", x.RecoveryTicks);
                w.WriteNumber("whiffTicks", x.WhiffTicks); WriteHitboxObject(w, x.CutHitbox, "cutHitbox");
                w.WriteString("stanceAnimationId", x.StanceAnimationId); w.WriteString("legAnimationId", x.LegAnimationId);
                w.WriteString("sheatheAnimationId", x.SheatheAnimationId);
                w.WriteString("lockPresentationId", x.LockPresentationId); w.WriteString("revealPresentationId", x.RevealPresentationId);
                w.WriteString("sheathePresentationId", x.SheathePresentationId); w.WriteString("popPresentationId", x.PopPresentationId);
                break;
            case CookedTargetedLeapCapabilityParameters x:
                w.WriteNumber("riseTicks", x.RiseTicks);
                Number(w, "riseHeight", x.RiseHeight);
                w.WriteNumber("hangTicks", x.HangTicks);
                w.WriteNumber("dropTicks", x.DropTicks);
                Number(w, "maxRange", x.MaxRange);
                Number(w, "noTargetDistance", x.NoTargetDistance);
                w.WriteNumber("maxFallTicks", x.MaxFallTicks);
                w.WriteNumber("hitRecoveryTicks", x.HitRecoveryTicks);
                w.WriteNumber("missRecoveryTicks", x.MissRecoveryTicks);
                WriteHitbox(w, x.Hitbox);
                OptionalString(w, "riseAnimationId", x.RiseAnimationId);
                OptionalString(w, "hangAnimationId", x.HangAnimationId);
                w.WriteNumber("hangPoseTick", x.HangPoseTick);
                OptionalString(w, "dropAnimationId", x.DropAnimationId);
                OptionalString(w, "landingAnimationId", x.LandingAnimationId);
                w.WriteNumber("hitLandingSeekTick", x.HitLandingSeekTick);
                OptionalString(w, "risePresentationId", x.RisePresentationId);
                OptionalString(w, "hangPresentationId", x.HangPresentationId);
                OptionalString(w, "lockPresentationId", x.LockPresentationId);
                OptionalString(w, "landingPresentationId", x.LandingPresentationId);
                break;
            case CookedThrustCapabilityParameters x:
                w.WriteNumber("aim", (byte)x.Aim);
                Number(w, "targetConeDegrees", x.TargetConeDegrees);
                Number(w, "neutralPitchDegrees", x.NeutralPitchDegrees); Number(w, "stickPitchDegrees", x.StickPitchDegrees);
                Number(w, "speed", x.Speed); Number(w, "acceleration", x.Acceleration); Number(w, "maxSpeed", x.MaxSpeed);
                w.WriteBoolean("keepIncomingSpeed", x.KeepIncomingSpeed); w.WriteNumber("thrustTicks", x.ThrustTicks);
                if (x.TrailHitbox != null) WriteHitboxObject(w, x.TrailHitbox, "trailHitbox");
                break;
            case CookedCannonballCapabilityParameters x:
                w.WriteNumber("takeoffTick", x.TakeoffTick);
                Number(w, "maxRange", x.MaxRange); Number(w, "noTargetDistance", x.NoTargetDistance);
                w.WriteNumber("minFlightTicks", x.MinFlightTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks);
                Number(w, "maxLaunchSpeed", x.MaxLaunchSpeed);
                OptionalString(w, "launchPresentationId", x.LaunchPresentationId);
                break;
            case CookedTetherCapabilityParameters x:
                w.WriteNumber("throwTick", x.ThrowTick);
                if (x.Projectile != null) WriteProjectile(w, x.Projectile);
                Number(w, "reelSpeed", x.ReelSpeed); Number(w, "anchorDistance", x.AnchorDistance);
                w.WriteNumber("arrivalStunTicks", x.ArrivalStunTicks); w.WriteNumber("maxReelTicks", x.MaxReelTicks);
                if (x.AtTarget == null) Number(w, "retractSpeed", x.RetractSpeed);
                w.WriteNumber("missRecoveryTicks", x.MissRecoveryTicks);
                Number(w, "airGravityScale", x.AirGravityScale);
                OptionalString(w, "reelAnimationId", x.ReelAnimationId);
                OptionalString(w, "retractAnimationId", x.RetractAnimationId);
                OptionalString(w, "throwPresentationId", x.ThrowPresentationId);
                OptionalString(w, "retractPresentationId", x.RetractPresentationId);
                if (x.AtTarget is { } at)
                {
                    w.WritePropertyName("atTarget"); w.WriteStartObject();
                    w.WriteNumber("warningTicks", at.WarningTicks); w.WriteNumber("catchTicks", at.CatchTicks);
                    Number(w, "radius", at.Radius); Number(w, "height", at.Height); Number(w, "damage", at.Damage);
                    Number(w, "maxRange", at.MaxRange); Number(w, "noTargetDistance", at.NoTargetDistance);
                    OptionalString(w, "warningPresentationId", at.WarningPresentationId);
                    OptionalString(w, "catchPresentationId", at.CatchPresentationId);
                    if (at.Unmarked is { } hand)
                    {
                        w.WritePropertyName("unmarked"); w.WriteStartObject();
                        w.WriteNumber("riseTick", hand.RiseTick); w.WriteNumber("activeTicks", hand.ActiveTicks);
                        Number(w, "distance", hand.Distance); Number(w, "radius", hand.Radius); Number(w, "height", hand.Height);
                        Number(w, "damage", hand.Damage); Number(w, "angle", hand.Angle);
                        Number(w, "baseKnockback", hand.BaseKnockback); Number(w, "knockbackGrowth", hand.KnockbackGrowth);
                        w.WriteNumber("stunTicks", hand.StunTicks); w.WriteNumber("recoveryTicks", hand.RecoveryTicks);
                        OptionalString(w, "presentationId", hand.PresentationId);
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                break;
            case CookedCommandGrabCapabilityParameters x:
                w.WriteNumber("lungeTicks", x.LungeTicks); Number(w, "lungeSpeed", x.LungeSpeed);
                Number(w, "stopDistance", x.StopDistance); w.WriteNumber("captureTick", x.CaptureTick);
                WriteHitboxObject(w, x.Capture, "capture"); w.WriteNumber("releaseTick", x.ReleaseTick);
                if (x.Launch is { } launch)
                {
                    w.WritePropertyName("launch"); w.WriteStartObject();
                    Number(w, "damage", launch.Damage); Number(w, "angle", launch.Angle);
                    Number(w, "baseKnockback", launch.BaseKnockback); Number(w, "knockbackGrowth", launch.KnockbackGrowth);
                    w.WriteNumber("stunTicks", launch.StunTicks); w.WriteEndObject();
                }
                OptionalString(w, "catchPresentationId", x.CatchPresentationId);
                OptionalString(w, "releasePresentationId", x.ReleasePresentationId);
                if (x.Level) w.WriteBoolean("level", true);
                if (x.AnchorDistance is { } anchor) Number(w, "anchorDistance", anchor);
                if (x.ReleaseLockTicks != 0) w.WriteNumber("releaseLockTicks", x.ReleaseLockTicks);
                if (x.WhiffRecoveryTicks != 0) w.WriteNumber("whiffRecoveryTicks", x.WhiffRecoveryTicks);
                break;
            case CookedSteeredChargeCapabilityParameters x:
                Number(w, "gallopSpeed", x.GallopSpeed); Number(w, "turnDegreesPerSecond", x.TurnDegreesPerSecond);
                w.WriteNumber("maxGallopTicks", x.MaxGallopTicks); w.WriteBoolean("armored", x.Armored);
                WriteHitboxObject(w, x.LanceHitbox, "lanceHitbox");
                w.WriteNumber("hitEndingTicks", x.HitEndingTicks); w.WriteNumber("missEndingTicks", x.MissEndingTicks);
                w.WriteNumber("skidTicks", x.SkidTicks); w.WriteBoolean("turnAroundOnMiss", x.TurnAroundOnMiss);
                w.WriteNumber("airStrideTicks", x.AirStrideTicks); w.WriteNumber("lookDownTicks", x.LookDownTicks);
                w.WriteNumber("challengedRearTicks", x.ChallengedRearTicks); Number(w, "trackDegreesPerSecond", x.TrackDegreesPerSecond);
                Number(w, "challengedTrackDegreesPerSecond", x.ChallengedTrackDegreesPerSecond);
                if (x.Assist != null) { w.WritePropertyName("assist"); JsonSerializer.Serialize(w, x.Assist, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); }
                WriteHitboxObject(w, x.ChestHitbox, "chestHitbox");
                w.WritePropertyName("carry"); JsonSerializer.Serialize(w, x.Carry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                OptionalString(w, "challengePresentationId", x.ChallengePresentationId);
                OptionalString(w, "gallopAnimationId", x.GallopAnimationId);
                if (x.GallopLoopTicks != 0) w.WriteNumber("gallopLoopTicks", x.GallopLoopTicks);
                OptionalString(w, "hitEndingAnimationId", x.HitEndingAnimationId);
                OptionalString(w, "missEndingAnimationId", x.MissEndingAnimationId);
                OptionalString(w, "airEndingAnimationId", x.AirEndingAnimationId);
                OptionalString(w, "gallopPresentationId", x.GallopPresentationId);
                OptionalString(w, "hitEndingPresentationId", x.HitEndingPresentationId);
                OptionalString(w, "missEndingPresentationId", x.MissEndingPresentationId);
                OptionalString(w, "airEndingPresentationId", x.AirEndingPresentationId);
                break;
            case CookedMankiRoundBombCapabilityParameters x: w.WriteNumber("armTicks", x.ArmTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); Number(w, "gravity", x.Gravity); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "explosionDamage", x.ExplosionDamage); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); Number(w, "explosionKbAngle", x.ExplosionKbAngle); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); Number(w, "triggerRadius", x.TriggerRadius); w.WriteNumber("warningTicks", x.WarningTicks); w.WriteNumber("lifetimeTicks", x.LifetimeTicks); w.WriteNumber("maxMines", x.MaxMines); break;
            case CookedMankiJetpackBoostCapabilityParameters x: w.WriteNumber("startupTicks", x.StartupTicks); Number(w, "verticalSpeed", x.VerticalSpeed); Number(w, "horizontalSpeed", x.HorizontalSpeed); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "explosionDamage", x.ExplosionDamage); Number(w, "explosionKbAngle", x.ExplosionKbAngle); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); w.WriteNumber("fuelTicks", x.FuelTicks); Number(w, "sinkSpeed", x.SinkSpeed); Number(w, "jumpBurstSpeed", x.JumpBurstSpeed); w.WriteNumber("jumpBurstTicks", x.JumpBurstTicks); break;
            case CookedMankiBazookaCapabilityParameters x: if (x.ChargeTicks > 0) w.WriteNumber("chargeTicks", x.ChargeTicks); Number(w, "homingTurnDegPerTick", x.HomingTurnDegPerTick); Number(w, "homingTurnCos", x.HomingTurnCos); Number(w, "homingTurnSin", x.HomingTurnSin); w.WriteNumber("fireTriggerTick", x.FireTriggerTick); Number(w, "projectileSpeed", x.ProjectileSpeed); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "damage", x.Damage); Number(w, "gravity", x.Gravity); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); w.WriteNumber("stunTicks", x.StunTicks); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "kbAngle", x.KbAngle); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); Number(w, "explosionKbAngle", x.ExplosionKbAngle); w.WriteNumber("castDuration", x.CastDuration); w.WriteNumber("recoveryDuration", x.RecoveryDuration); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); break;
            case CookedMankiKamikazeCapabilityParameters x: w.WriteNumber("runTicks", x.RunTicks); Number(w, "speed", x.Speed); Number(w, "contactRadius", x.ContactRadius); Number(w, "explosionDamage", x.ExplosionDamage); Number(w, "explosionRadius", x.ExplosionRadius); Number(w, "explosionKbBase", x.ExplosionKbBase); Number(w, "explosionKbGrowth", x.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", x.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", x.ExplosionDurationTicks); Number(w, "explosionKbAngle", x.ExplosionKbAngle); Number(w, "selfPushSpeed", x.SelfPushSpeed); OptionalString(w, "explosionPresentationId", x.ExplosionPresentationId); break;
            default: throw new InvalidDataException("Unknown capability parameters.");
        }
        w.WriteEndObject();
    }
    /// <summary>Optional carry/finisher fields; absent when zero so single-hit tunings keep their hashes.</summary>
    internal static void WriteCycloneCarry(Utf8JsonWriter w, ushort hitIntervalTicks, ushort fixedHitstunTicks,
        ushort finisherTick, float finisherDamage, float finisherAngle, float finisherBase, float finisherGrowth)
    {
        if (hitIntervalTicks != 0) w.WriteNumber("hitIntervalTicks", hitIntervalTicks);
        if (fixedHitstunTicks != 0) w.WriteNumber("fixedHitstunTicks", fixedHitstunTicks);
        if (finisherTick == 0) return;
        w.WriteNumber("finisherTick", finisherTick);
        Number(w, "finisherDamage", finisherDamage);
        Number(w, "finisherKnockbackAngle", finisherAngle);
        Number(w, "finisherKnockbackBase", finisherBase);
        Number(w, "finisherKnockbackGrowth", finisherGrowth);
    }

    internal static void WriteRecoveryHop(Utf8JsonWriter w, RecoveryHopParameters? hop)
    {
        if (hop == null) return;
        w.WritePropertyName("hop"); w.WriteStartObject();
        w.WriteNumber("startTicks", hop.StartTicks); w.WriteNumber("contactTicks", hop.ContactTicks);
        Number(w, "forwardDistance", hop.ForwardDistance); Number(w, "arcHeight", hop.ArcHeight);
        Number(w, "landingHeight", hop.LandingHeight); w.WriteEndObject();
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
        public void AddFrom(DiagnosticBag other, Func<string, string> path)
        {
            foreach (var item in other.ToList()) _items.Add((_order++, item with { Path = path(item.Path) }));
        }
    }

    private static bool IsId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64 && value[0] >= 'a' && value[0] <= 'z' && value.All(x => (x >= 'a' && x <= 'z') || (x >= '0' && x <= '9') || x == '.' || x == '-');
    private static void ValidateAttachmentId(string value, string path, DiagnosticBag d) { if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] != '_' || value.Skip(1).Any(x => !(x >= 'a' && x <= 'z') && !(x >= '0' && x <= '9') && x != '.' && x != '-' && x != '_')) d.Error("id.invalid", path, "Attachment ID must be underscore-prefixed lowercase ASCII."); }
    private static void ValidateHurtboxBoneId(string value, string path, DiagnosticBag d)
    {
        if (!CharacterBoneRegistry.IsRigId(value)) ValidateId(value, path, d);
    }
    private static void ValidateId(string value, string path, DiagnosticBag d) { if (!IsId(value)) d.Error("id.invalid", path, "ID must be lowercase ASCII and start with a letter."); }
    private static bool IsSemVer(string value)
    {
        var parts = value.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].All(char.IsDigit) && parts[1].All(char.IsDigit) && parts[2].All(char.IsDigit);
    }
    private static void ValidateNonNegative(float value, string path, DiagnosticBag d) { if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", path, "Value must be finite."); else if (value < 0) d.Error("value.out-of-range", path, "Value must be non-negative."); }
    private static void ValidateAngle(float value, string path, DiagnosticBag d) { if (value < -90 || value > 90) d.Error("value.out-of-range", path, "Angle must be between -90 and 90 degrees."); }
    private static void ValidateYaw(float value, string path, DiagnosticBag d) { if (value < -180 || value > 180) d.Error("value.out-of-range", path, "Yaw offset must be between -180 and 180 degrees."); }
}
