using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed class CookedCharacterPackageLoadResult
{
    public CookedCharacterPackage? Package { get; }
    public BakedAnimationData? BakedAnimation { get; }
    public MatchContentIdentity Identity { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool IsValid => Package != null && Diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);

    internal CookedCharacterPackageLoadResult(CookedCharacterPackage? package, BakedAnimationData? baked, MatchContentIdentity identity, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Package = package; BakedAnimation = baked; Identity = identity;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
    }

    public CharacterDefinition ToCharacterDefinition(CharacterClass legacySelector = CharacterClass.None)
    {
        if (!IsValid || Package == null) throw new InvalidDataException("Cooked package is not valid.");
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(Package, legacySelector);
    }
}
public static class CookedCharacterPackageLoader
{
    private static readonly HashSet<string> SupportedRuntimeApiMinimums = new(
        Enumerable.Range(0, 12).Select(minor => $"1.{minor}.0"), StringComparer.Ordinal);

    public static CookedCharacterPackageLoadResult LoadAssembly(CharacterPackageAssemblyResult assembly)
    {
        if (assembly == null)
            return Failure(new List<CharacterDiagnostic> { Error("package.assembly.missing", "assembly", "Assembly result is required.") });
        try
        {
            using var document = JsonDocument.Parse(assembly.ManifestBytes);
            var root = document.RootElement;
            var requirement = new MatchContentPackageRequirement(
                root.GetProperty("packageId").GetString() ?? "",
                root.GetProperty("version").GetString() ?? "",
                root.GetProperty("cookedContentHash").GetString() ?? "",
                root.GetProperty("packageHash").GetString() ?? "");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [CharacterPackageAssembler.ManifestPath] = assembly.ManifestBytes,
                [CharacterPackageAssembler.RuntimePath] = assembly.RuntimeBytes,
                [CharacterPackageAssembler.PosePath] = assembly.PoseBytes,
                [CharacterPackageAssembler.BindingPath] = assembly.BindingBytes,
            };
            return LoadFiles(files, requirement);
        }
        catch (Exception ex)
        {
            return Failure(new List<CharacterDiagnostic> { Error("package.assembly.malformed", "manifest", ex.Message) });
        }
    }
    public static CookedCharacterPackageLoadResult LoadDirectory(string directory, MatchContentPackageRequirement requirement)
    {
        var d = new List<CharacterDiagnostic>();
        if (string.IsNullOrWhiteSpace(directory)) { d.Add(Error("package.directory.missing","directory","Package directory is required.")); return Failure(d); }
        var files = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach (var name in new[]{CharacterPackageAssembler.ManifestPath,CharacterPackageAssembler.RuntimePath,CharacterPackageAssembler.PosePath,CharacterPackageAssembler.BindingPath})
        {
            try { files[name]=File.ReadAllBytes(Path.Combine(directory,name)); }
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is NotSupportedException) { d.Add(Error("package.file.missing",name,ex.Message)); }
        }
        return d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error) ? Failure(d) : LoadFiles(files,requirement);
    }

    public static CookedCharacterPackageLoadResult LoadFiles(IReadOnlyDictionary<string,byte[]> files, MatchContentPackageRequirement requirement)
    {
        var d=new List<CharacterDiagnostic>();
        if(requirement==null) d.Add(Error("package.requirement.missing","requirement","Package requirement is required."));
        else if(!MatchContentCatalogBuilder.IsStablePackageId(requirement.PackageId)||string.IsNullOrWhiteSpace(requirement.Version)||!MatchContentCatalogBuilder.IsSha(requirement.CookedContentHash)||!MatchContentCatalogBuilder.IsSha(requirement.PackageHash)) d.Add(Error("package.requirement.invalid","requirement","Package requirement is incomplete or invalid."));
        var copied=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        if(files!=null) foreach(var p in files) copied[p.Key]=p.Value==null?null!:(byte[])p.Value.Clone();
        d.AddRange(CharacterPackageAssembler.Verify(copied).Diagnostics);
        if(d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error)) return Failure(d);
        try
        {
            var m=ParseManifest(copied[CharacterPackageAssembler.ManifestPath]);
            if(m.PackageId!=requirement!.PackageId||m.Version!=requirement.Version||m.CookedContentHash!=requirement.CookedContentHash||m.PackageHash!=requirement.PackageHash) d.Add(Error("package.identity.mismatch","manifest","Package identity does not match the requested requirement."));
            if(m.CookedSchemaVersion!=4||!SupportedRuntimeApiMinimums.Contains(m.RuntimeApiMin)||m.RuntimeApiMax!="1.x") d.Add(Error("package.compatibility.unsupported","manifest","Cooked package schema/API is not supported."));
            if(m.Dependencies.Count!=0) d.Add(Error("package.dependencies.unsupported","manifest.dependencies","Unresolved package dependencies are not supported."));
            foreach (var c in m.Capabilities)
            {
                if (RetiredCapabilities.TryGetReason(c.CapabilityId, out var retiredReason))
                    d.Add(Error("package.capability.retired", c.CapabilityId, retiredReason));
                else if (!CharacterPackageCompiler.IsRuntimeCapability(c.CapabilityId, c.CapabilityVersion))
                    d.Add(Error("package.capability.unsupported", c.CapabilityId, "Cooked capability is not supported by this runtime."));
            }
            var package=RuntimeParser.Parse(copied[CharacterPackageAssembler.RuntimePath]);
            foreach (var slot in package.Definition.Slots)
            foreach (var stage in slot.Timeline.Stages)
                if (stage.WarpRange != 0f || stage.UseTargetLock || stage.RotateTowardTarget || stage.TrackingStrength != 0f)
                    d.Add(Error("package.targeting.retired", slot.Id,
                        "Cooked automatic warp and per-stage target tracking are retired."));
            ValidateCapabilityOperations(package.Definition, d);
            ValidateMankiBazookas(package.Definition, d);
            ValidateChargedDirectionalDashes(package.Definition, m.RuntimeApiMin, d);
            ValidateChargedProjectiles(package.Definition, m.RuntimeApiMin, d);
            ValidateChargedStreams(package.Definition, m.RuntimeApiMin, d);
            ValidateFingerGuns(package.Definition, m.RuntimeApiMin, d);
            ValidateArmorAndFixedHitstun(package.Definition, m.RuntimeApiMin, d);
            ValidateStartupAimCorrections(package.Definition, m.RuntimeApiMin, d);
            ValidateAimMovementSpeeds(package.Definition, m.RuntimeApiMin, d);
            ValidateSteeredCharges(package.Definition, m.RuntimeApiMin, d);
            ValidateAimedRecoveries(package.Definition, m.RuntimeApiMin, d);
            ValidateReturningProjectiles(package.Definition, m.RuntimeApiMin, d);
            if(package.Metadata.PackageId!=m.PackageId||package.Metadata.Version!=m.Version||package.Metadata.CookedSchemaVersion!=m.CookedSchemaVersion) d.Add(Error("package.runtime.metadata-mismatch",CharacterPackageAssembler.RuntimePath,"Runtime package metadata does not match manifest."));
            var baked=BakedAnimationData.LoadFromBin(copied[CharacterPackageAssembler.PosePath]);
            using var bindings = JsonDocument.Parse(copied[CharacterPackageAssembler.BindingPath]);
            var semanticByPose = bindings.RootElement.GetProperty("animations").EnumerateArray()
                .ToDictionary(x => x.GetProperty("poseTrackId").GetString()!,
                    x => x.GetProperty("semanticId").GetString()!, StringComparer.Ordinal);
            // The verified bindings are one-to-one; runtime lookup uses semantic IDs.
            baked = new BakedAnimationData
            {
                BoneNames = baked.BoneNames,
                Animations = baked.Animations.Select(animation => animation.WithName(semanticByPose[animation.Name])).ToArray(),
            };
            if(d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error)) return Failure(d);
            return new CookedCharacterPackageLoadResult(package,baked,new MatchContentIdentity(m.PackageId,m.Version,m.SourceHash,m.CookedContentHash,m.PackageHash),d);
        }
        catch (RetiredCapabilityException ex)
        {
            d.Add(Error("package.capability.retired", ex.CapabilityId, ex.Message));
            return Failure(d);
        }
        catch (Exception ex) { d.Add(Error("package.runtime.malformed", "package", ex.Message)); return Failure(d); }
    }

    private static CookedCharacterPackageLoadResult Failure(List<CharacterDiagnostic> d)=>new(null,null,new MatchContentIdentity("","","","",""),d);
    private static CharacterDiagnostic Error(string c,string p,string m)=>new(CharacterDiagnosticSeverity.Error,c,p,m);
    private static void ValidateCapabilityOperations(
        CookedCharacterDefinition definition,
        List<CharacterDiagnostic> diagnostics)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var requirement in definition.CapabilityRequirements)
        {
            if (RetiredCapabilities.TryGetReason(requirement.CapabilityId, out var retiredReason))
                diagnostics.Add(Error("package.capability.retired", requirement.CapabilityId, retiredReason));
            else if (!CharacterPackageCompiler.IsRuntimeCapability(requirement.CapabilityId, requirement.CapabilityVersion))
                diagnostics.Add(Error("package.capability.unsupported", requirement.CapabilityId, "Runtime capability requirement is not supported by this runtime."));
            if (!declared.TryAdd(requirement.CapabilityId, requirement.CapabilityVersion))
                diagnostics.Add(Error("package.capability.duplicate", requirement.CapabilityId, "Runtime contains a duplicate capability requirement."));
        }

        foreach (var slot in definition.Slots)
        foreach (var stage in slot.Timeline.Stages)
        foreach (var operation in stage.Operations)
        {
            if (operation is not CookedStartCapabilityOperation capability)
                continue;
            if (capability.Parameters is CookedWibouBladeFlurryCapabilityParameters flurry &&
                (!CharacterPackageCompiler.ValidBladeFlurry(flurry.ForwardSpeed, flurry.AscentStartTick,
                    flurry.AscentEndTick, flurry.DescentStartTick, flurry.RiseSpeed, flurry.DescentSpeed)
                    || flurry.DescentStartTick >= stage.DurationTicks))
                diagnostics.Add(Error("package.capability.invalid", slot.Id, "Blade Flurry motion must use valid speeds and ordered ticks inside its stage."));
            if (RetiredCapabilities.TryGetReason(capability.CapabilityId, out var retiredReason))
                diagnostics.Add(Error("package.capability.retired", capability.CapabilityId, retiredReason));
            else if (!CharacterPackageCompiler.IsRuntimeCapability(capability.CapabilityId, capability.CapabilityVersion))
                diagnostics.Add(Error("package.capability.unsupported", capability.CapabilityId, "Cooked capability version is not supported by this runtime."));
            else if (!declared.TryGetValue(capability.CapabilityId, out var version))
                diagnostics.Add(Error("package.capability.undeclared", capability.CapabilityId, "Runtime capability is not declared by the package."));
            else if (version != capability.CapabilityVersion)
                diagnostics.Add(Error("package.capability.version-mismatch", capability.CapabilityId, "Runtime capability version does not match its declaration."));
        }
    }
    private static void ValidateFingerGuns(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var guns = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartCapabilityOperation>()
                .Where(operation => operation.CapabilityId == CharacterPackageCompiler.FingerGunsCapabilityId).ToArray();
            if (guns.Length == 0) continue;
            if (runtimeApiMin != "1.11.0")
                diagnostics.Add(Error("package.compatibility.unsupported", slot.Id, "Finger Guns requires runtime API 1.11.0."));
            bool invalid = guns.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0 ||
                slot.ChargePool != null || slot.AllowSlideCarry ||
                slot.AimMode is not (AuthoringAimMode.None or AuthoringAimMode.CameraForward3D);
            foreach (var operation in guns)
            {
                if (operation.Parameters is not CookedFingerGunsCapabilityParameters g)
                { invalid = true; continue; }
                var p = g.Projectile;
                var stage = slot.Timeline.Stages[0];
                var values = new[] { p.Radius, p.Speed, p.Damage, p.Angle, p.BaseKnockback, p.KnockbackGrowth,
                    p.Gravity, p.LaunchOffsetX, p.LaunchOffsetY, p.LaunchOffsetZ, p.YawOffsetDegrees };
                invalid |= values.Any(value => float.IsNaN(value) || float.IsInfinity(value));
                invalid |= operation.Tick != 0 || !CharacterPackageCompiler.ValidFingerGuns(
                    g.FireTriggerTick, g.RecoveryTicks, g.ShotsPerReload, g.ReloadTicks, g.AirMaxFallSpeed, g.AirRecoilSpeed) ||
                    string.IsNullOrEmpty(g.ReloadAnimationId) || !PhaseClip(stage, g.ReloadAnimationId) ||
                    p.Return != null || p.Radius <= 0f || p.Speed <= 0f || p.MaxFlightTicks == 0 ||
                    p.Damage < 0f || p.Angle < -90f || p.Angle > 90f;
            }
            invalid |= slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .Any(operation => operation != guns[0] && operation is not CookedEmitPresentationOperation);
            if (invalid)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "Finger Guns lifecycle, cylinder, reload, air motion or projectile parameters are invalid."));
        }
    }

    private static void ValidateChargedStreams(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var streams = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartCapabilityOperation>()
                .Where(operation => operation.CapabilityId == CharacterPackageCompiler.ChargedStreamCapabilityId).ToArray();
            if (streams.Length == 0) continue;
            if (runtimeApiMin is not ("1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                diagnostics.Add(Error("package.compatibility.unsupported", slot.Id, "Charged stream requires runtime API 1.8.0."));
            bool invalid = streams.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.ChargeAttack ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0;
            foreach (var operation in streams)
            {
                if (operation.Parameters is not CookedChargedStreamCapabilityParameters stream)
                { invalid = true; continue; }
                var p = stream.Projectile;
                var values = new[] { stream.MaxBaseKnockback, stream.MinRecoilSpeed, stream.MaxRecoilSpeed, p.Radius, p.Speed,
                    p.Damage, p.Angle, p.BaseKnockback, p.KnockbackGrowth, p.Gravity,
                    p.LaunchOffsetX, p.LaunchOffsetY, p.LaunchOffsetZ, p.YawOffsetDegrees };
                var stage = slot.Timeline.Stages[0];
                invalid |= values.Any(value => float.IsNaN(value) || float.IsInfinity(value));
                invalid |= operation.Tick != 0 || stream.MaxChargeTicks == 0 || stream.FireTriggerTick == 0 ||
                    stream.EmitIntervalTicks == 0 || stream.MinStreamTicks == 0 || stream.MaxStreamTicks < stream.MinStreamTicks ||
                    stream.MaxBaseKnockback < p.BaseKnockback || stream.MinRecoilSpeed < 0f ||
                    stream.MaxRecoilSpeed < stream.MinRecoilSpeed ||
                    p.Radius <= 0f || p.Speed <= 0f || p.MaxFlightTicks == 0 || p.Damage < 0f || p.Angle < -90f || p.Angle > 90f ||
                    p.Return != null || stream.SprayLoopTicks > 0 && stream.SprayAnimationId.Length == 0 ||
                    !PhaseClip(stage, stream.SprayAnimationId) || !PhaseClip(stage, stream.EndAnimationId);
            }
            invalid |= slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>
                operation is CookedSpawnProjectileOperation or CookedSpawnHitboxOperation or CookedSetAimStateOperation
                    or CookedSetVelocityOperation or CookedForwardLungeOperation ||
                operation is CookedStartCapabilityOperation capability && capability.CapabilityId != CharacterPackageCompiler.ChargedStreamCapabilityId);
            if (invalid)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "Charged stream lifecycle, lengths, push or projectile are invalid."));
        }
    }

    private static void ValidateMankiBazookas(CookedCharacterDefinition definition, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        foreach (var bazooka in slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedStartCapabilityOperation>().Select(operation => operation.Parameters)
            .OfType<CookedMankiBazookaCapabilityParameters>())
        {
            float norm = bazooka.HomingTurnCos * bazooka.HomingTurnCos + bazooka.HomingTurnSin * bazooka.HomingTurnSin;
            if (!float.IsFinite(bazooka.HomingTurnDegPerTick) || bazooka.HomingTurnDegPerTick <= 0f
                || bazooka.HomingTurnDegPerTick > 90f || !float.IsFinite(norm) || MathF.Abs(norm - 1f) > .00001f
                || bazooka.HomingTurnCos < 0f || bazooka.HomingTurnCos > 1f
                || bazooka.HomingTurnSin <= 0f || bazooka.HomingTurnSin > 1f)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "Bazooka requires a turn rate in (0, 90] degrees per tick and normalized nonnegative turn coefficients."));
        }
    }

    /// <summary>An optional phase clip: empty, or listed in the stage after its first clip.</summary>
    private static bool PhaseClip(CookedStage stage, string id)
        => id.Length == 0 || stage.AnimationIds.Count > 1 && stage.AnimationIds[0] != id
            && stage.AnimationIds.Skip(1).Contains(id, StringComparer.Ordinal);

    private static void ValidateChargedProjectiles(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var charges = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartCapabilityOperation>()
                .Where(operation => operation.CapabilityId == CharacterPackageCompiler.ChargedProjectileCapabilityId).ToArray();
            if (charges.Length == 0) continue;
            if (runtimeApiMin is not ("1.6.0" or "1.7.0" or "1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                diagnostics.Add(Error("package.compatibility.unsupported", slot.Id, "Charged projectile requires runtime API 1.6.0 or later."));
            bool invalid = charges.Length != 1 || slot.Behavior != AuthoringAbilityBehavior.ChargeAttack ||
                slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0;
            foreach (var operation in charges)
            {
                if (operation.Parameters is not CookedChargedProjectileCapabilityParameters charge)
                { invalid = true; continue; }
                var p = charge.Projectile;
                var values = new[] { charge.Tier2Radius, charge.Tier3Radius, p.Radius, p.Speed,
                    p.Damage, p.Angle, p.BaseKnockback, p.KnockbackGrowth, p.Gravity,
                    p.LaunchOffsetX, p.LaunchOffsetY, p.LaunchOffsetZ, p.YawOffsetDegrees };
                invalid |= values.Any(value => float.IsNaN(value) || float.IsInfinity(value));
                invalid |= operation.Tick != 0 || charge.MaxChargeTicks == 0 || charge.Tier2Ticks == 0 ||
                    charge.Tier2Ticks >= charge.Tier3Ticks || charge.Tier3Ticks > charge.MaxChargeTicks ||
                    charge.FireTriggerTick == 0 || charge.FireTriggerTick >= slot.Timeline.Stages[0].DurationTicks ||
                    p.Radius <= 0f || charge.Tier2Radius <= p.Radius || charge.Tier3Radius <= charge.Tier2Radius ||
                    p.Speed <= 0f || p.MaxFlightTicks == 0 || p.Damage < 0f || p.Angle < -90f || p.Angle > 90f ||
                    p.Return != null;
            }
            invalid |= slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>
                operation is CookedSpawnProjectileOperation or CookedSpawnHitboxOperation or CookedSetAimStateOperation
                    or CookedSetVelocityOperation or CookedForwardLungeOperation ||
                operation is CookedStartCapabilityOperation capability && capability.CapabilityId != CharacterPackageCompiler.ChargedProjectileCapabilityId);
            if (invalid)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "Charged projectile lifecycle, thresholds, or projectile dimensions are invalid."));
        }
    }

    private static void ValidateChargedDirectionalDashes(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            int lifecycleCount = 0;
            for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
            {
                var stage = slot.Timeline.Stages[stageIndex];
                foreach (var operation in stage.Operations)
                {
                    if (operation is not CookedStartCapabilityOperation capability ||
                        capability.CapabilityId != CharacterPackageCompiler.ChargedDirectionalDashCapabilityId)
                        continue;
                    lifecycleCount++;
                    string path = slot.Id + ".chargedDirectionalDash";
                    if (runtimeApiMin is not ("1.5.0" or "1.6.0" or "1.7.0" or "1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                        diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                            "Charged directional dash requires runtime API 1.5.0."));
                    if (capability.Parameters is not CookedChargedDirectionalDashCapabilityParameters dash)
                    {
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charged directional dash parameters have the wrong type."));
                        continue;
                    }
                    if (slot.Behavior != AuthoringAbilityBehavior.DirectionalDash || slot.AimMode != AuthoringAimMode.GroundVector ||
                        slot.Timeline.Stages.Count != 1 || stage.IasaTicks != 0)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charged dash requires directionalDash, groundVector aim, and one stage with IASA disabled."));
                    if (stageIndex != 0 || operation.Tick != 0)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charged dash lifecycle must start at tick zero in the first stage."));
                    if (stage.Operations.Any(other => other != operation &&
                        (other is CookedForwardLungeOperation or CookedSetVelocityOperation or CookedSpawnHitboxOperation
                            or CookedStartCapabilityOperation or CookedStartupAimCorrectionOperation)))
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charged dash cannot share its stage with another lifecycle, startup correction, motion, or extra hitboxes."));
                    if (dash.MaxChargeTicks == 0 || dash.Tier2Ticks == 0 ||
                        dash.Tier2Ticks >= dash.Tier3Ticks || dash.Tier3Ticks > dash.MaxChargeTicks)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charge thresholds are invalid."));
                    float[] values = { dash.MinDistance, dash.MaxDistance, dash.DashSpeed,
                        dash.Tier2Damage, dash.Tier3Damage };
                    if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)) ||
                        dash.MinDistance <= 0f || dash.MaxDistance < dash.MinDistance || dash.DashSpeed <= 0f)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Distances and dash speed must be finite and in range."));
                    if (dash.FinisherLeadTicks == 0 || dash.FinisherSeekTick == 0 || dash.RecoveryTicks == 0 ||
                        dash.Tier2Damage < 0f || dash.Tier2Damage < dash.FinisherHitbox.Damage ||
                        dash.Tier3Damage < dash.Tier2Damage)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Finisher timing and tier damage are invalid."));
                    ValidateChargedHitbox(dash.TraversalHitbox, definition, path + ".traversalHitbox", diagnostics);
                    ValidateChargedHitbox(dash.FinisherHitbox, definition, path + ".finisherHitbox", diagnostics);
                    if (dash.FinisherHitbox.DurationTicks > (int)dash.FinisherLeadTicks + dash.RecoveryTicks)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Finisher duration exceeds its lead and recovery."));
                    double travelTicks = Math.Ceiling(dash.MaxDistance / (dash.DashSpeed / 60d));
                    if (travelTicks + dash.RecoveryTicks > stage.DurationTicks ||
                        (int)dash.FinisherSeekTick + dash.FinisherLeadTicks + dash.RecoveryTicks > stage.DurationTicks)
                        diagnostics.Add(Error("package.capability.parameters-invalid", path,
                            "Charged dash travel and finisher recovery exceed stage duration."));
                }
            }
            if (lifecycleCount > 1)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "A slot may contain only one charged directional dash lifecycle."));
        }
    }

    private static void ValidateChargedHitbox(CookedHitbox hitbox, CookedCharacterDefinition definition,
        string path, List<CharacterDiagnostic> diagnostics)
    {
        float[] values = { hitbox.Radius, hitbox.OffsetX, hitbox.OffsetY, hitbox.OffsetZ,
            hitbox.EndOffsetX, hitbox.EndOffsetY, hitbox.EndOffsetZ, hitbox.Damage,
            hitbox.Angle, hitbox.BaseKnockback, hitbox.KnockbackGrowth };
        if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)) ||
            hitbox.Radius < 0f || hitbox.Damage < 0f || hitbox.Angle < -90f || hitbox.Angle > 90f ||
            hitbox.BaseKnockback < 0f || hitbox.KnockbackGrowth < 0f || hitbox.DurationTicks == 0 ||
            hitbox.Shape is not (AuthoringHitboxShape.Sphere or AuthoringHitboxShape.Capsule) ||
            hitbox.KnockbackDirection is not (AuthoringKnockbackDirection.AwayFromOwner or AuthoringKnockbackDirection.TowardOwner) ||
            hitbox.FixedHitstunTicks > 240 || hitbox.FixedHitstunTicks > 0 && hitbox.StunTicks == 0 || hitbox.HitGroup != 0)
            diagnostics.Add(Error("package.capability.parameters-invalid", path,
                "Charged dash hitbox geometry or values are invalid."));
        bool HasBone(string? id) => id == null ||
            definition.HurtboxBoneDefs.Any(bone => bone.BoneId == id) ||
            definition.AttachmentBoneIds.Contains(id, StringComparer.Ordinal);
        if (!HasBone(hitbox.StartBoneId) || !HasBone(hitbox.EndBoneId))
            diagnostics.Add(Error("package.capability.parameters-invalid", path,
                "Charged dash hitbox references an undeclared bone."));
    }
    private static void ValidateArmorAndFixedHitstun(
        CookedCharacterDefinition definition,
        string runtimeApiMin,
        List<CharacterDiagnostic> diagnostics)
    {
        bool requiresApi13 = false;
        foreach (var slot in definition.Slots)
        foreach (var stage in slot.Timeline.Stages)
        foreach (var operation in stage.Operations)
        {
            if (operation is CookedArmorWindowOperation armor)
            {
                requiresApi13 = true;
                if (armor.Unit != AuthoringUnit.Ticks || armor.DurationTicks == 0
                    || (int)armor.Tick + armor.DurationTicks > stage.DurationTicks)
                    diagnostics.Add(Error("package.operation.invalid", slot.Id,
                        "Armor window must use ticks and fit within its stage."));
            }
            if (operation is CookedSpawnHitboxOperation hitbox)
                ValidateFixedHitstun(hitbox.Hitbox.FixedHitstunTicks, hitbox.Hitbox.StunTicks,
                    slot.Id, diagnostics, ref requiresApi13);
            if (operation is CookedStartCapabilityOperation
                {
                    Parameters: CookedTargetedLeapCapabilityParameters leap
                })
                ValidateFixedHitstun(leap.Hitbox.FixedHitstunTicks, leap.Hitbox.StunTicks,
                    slot.Id, diagnostics, ref requiresApi13);
        }
        if (requiresApi13 && runtimeApiMin is not ("1.3.0" or "1.4.0" or "1.5.0" or "1.6.0" or "1.7.0" or "1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
            diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                "Armor windows and fixed hitstun require runtime API 1.3.0 or later."));
    }

    private static void ValidateAimMovementSpeeds(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            if (slot.AimMovementSpeedScale == 1f) continue;
            if (runtimeApiMin is not ("1.7.0" or "1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                    "Aim movement speed scale requires runtime API 1.7.0."));
            if (!(slot.AimMovementSpeedScale > 0f && slot.AimMovementSpeedScale <= 1f)
                || slot.AimMovement != AuthoringAimMovementMode.Mobile)
                diagnostics.Add(Error("package.slot.invalid", slot.Id,
                    "Aim movement speed scale must be in (0, 1] on a mobile aim slot."));
        }
    }

    private static void ValidateAimedRecoveries(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var recoveries = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartCapabilityOperation>()
                .Where(operation => operation.CapabilityId == CharacterPackageCompiler.AimedRecoveryCapabilityId).ToArray();
            if (recoveries.Length == 0) continue;
            if (runtimeApiMin is not ("1.9.0" or "1.10.0" or "1.11.0") || recoveries.Any(op =>
                op.Parameters is CookedAimedRecoveryCapabilityParameters { Hop: not null }) && runtimeApiMin is not ("1.10.0" or "1.11.0")
                || recoveries.Any(op => op.Parameters is CookedAimedRecoveryCapabilityParameters { RiseSteerAcceleration: > 0f })
                    && runtimeApiMin != "1.11.0")
                diagnostics.Add(Error("package.compatibility.unsupported", slot.Id, "Aimed recovery requires runtime API 1.9.0, 1.10.0 with a setup hop, or 1.11.0 with rise steering."));
            var stage = slot.Timeline.Stages[0];
            if (recoveries.Length != 1 || slot.Timeline.Stages.Count != 1 || stage.IasaTicks != 0
                || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo || slot.AimMode != AuthoringAimMode.None
                || !slot.IsRecoveryMove || recoveries[0].Tick != 0
                || stage.Operations.Any(op => op != recoveries[0] && op is not CookedEmitPresentationOperation)
                || recoveries[0].Parameters is not CookedAimedRecoveryCapabilityParameters recovery
                || !CharacterPackageCompiler.ValidAimedRecovery(recovery.StartupTicks, recovery.VerticalSpeed,
                    recovery.DirectionalVerticalSpeed, recovery.HorizontalSpeed, recovery.PreparationRiseSpeed, recovery.Hop,
                    recovery.RiseSteerAcceleration)
                || recovery.StartupTicks >= stage.DurationTicks)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "Aimed recovery requires bounded movement parameters and one tick-zero recovery lifecycle that launches within its stage, with no competing operations, aim or IASA."));
        }
    }

    private static void ValidateSteeredCharges(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var charges = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartCapabilityOperation>()
                .Where(operation => operation.CapabilityId == CharacterPackageCompiler.SteeredChargeCapabilityId)
                .ToArray();
            if (charges.Length == 0) continue;
            string path = slot.Id + ".steeredCharge";
            if (runtimeApiMin is not ("1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                    "Steered charge requires runtime API 1.8.0."));
            if (charges.Length != 1 || slot.Timeline.Stages.Count != 1 || slot.Timeline.Stages[0].IasaTicks != 0)
            {
                diagnostics.Add(Error("package.capability.parameters-invalid", path,
                    "Steered charge requires one lifecycle in one stage with IASA disabled."));
                continue;
            }
            if (charges[0].Parameters is not CookedSteeredChargeCapabilityParameters charge)
            {
                diagnostics.Add(Error("package.capability.parameters-invalid", path,
                    "Steered charge parameters have the wrong type."));
                continue;
            }
            var stage = slot.Timeline.Stages[0];
            if (stage.Operations.Any(other => other != charges[0] &&
                (other is CookedForwardLungeOperation or CookedSetVelocityOperation or CookedSpawnHitboxOperation
                    or CookedSpawnProjectileOperation or CookedSetAimStateOperation or CookedStartCapabilityOperation
                    or CookedCompleteTimelineOperation)))
                diagnostics.Add(Error("package.capability.parameters-invalid", path,
                    "Steered charge cannot share its stage with motion, hitboxes, projectiles, aim, completion or another lifecycle."));
            if (!(charge.GallopSpeed > 0f) || float.IsInfinity(charge.GallopSpeed)
                || !(charge.TurnDegreesPerSecond >= 0f && charge.TurnDegreesPerSecond <= CharacterPackageCompiler.MaxSteeredChargeTurnDegreesPerSecond)
                || charge.MaxGallopTicks == 0 || charge.HitEndingTicks == 0 || charge.MissEndingTicks == 0
                || charge.SkidTicks > charge.MissEndingTicks
                || !string.IsNullOrEmpty(charge.GallopAnimationId) && charge.GallopLoopTicks == 0
                || charge.LanceHitbox.HitGroup != 0)
                diagnostics.Add(Error("package.capability.parameters-invalid", path,
                    "Steered charge speed, turn rate, timings or lance are invalid."));
            foreach (string id in new[] { charge.GallopAnimationId, charge.HitEndingAnimationId,
                         charge.MissEndingAnimationId, charge.AirEndingAnimationId })
                if (!string.IsNullOrEmpty(id) && stage.AnimationIds.ToList().IndexOf(id) <= 0)
                    diagnostics.Add(Error("package.capability.parameters-invalid", path,
                        "Steered charge phase clips must be listed in its stage after the startup clip."));
        }
    }

    private static void ValidateReturningProjectiles(
        CookedCharacterDefinition definition, string runtimeApiMin, List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            var throws = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedSpawnProjectileOperation>()
                .Where(operation => operation.Projectile.Return != null).ToArray();
            if (throws.Length == 0) continue;
            if (runtimeApiMin is not ("1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                    "Returning projectiles require runtime API 1.8.0."));
            if (throws.Length > 1)
                diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id,
                    "A slot may throw only one returning projectile."));
            foreach (var operation in throws)
            {
                var p = operation.Projectile;
                var back = p.Return!;
                if (!(p.Speed > 0f) || float.IsInfinity(p.Speed) || p.Gravity != 0f
                    || back.OutboundTicks == 0 || back.OutboundTicks >= p.MaxFlightTicks
                    || !(back.ReturnSpeed > 0f) || float.IsInfinity(back.ReturnSpeed)
                    || !(back.ReturnAcceleration > 0f) || float.IsInfinity(back.ReturnAcceleration)
                    || !(back.SelfHitDamage >= 0f) || float.IsInfinity(back.SelfHitDamage)
                    || back.SelfHitStunTicks == 0 || back.SelfHitStunTicks > 240)
                    diagnostics.Add(Error("package.capability.parameters-invalid", slot.Id + ".return",
                        "Returning projectile speed, timings or self-hit are invalid."));
            }
        }
    }

    private static void ValidateStartupAimCorrections(
        CookedCharacterDefinition definition,
        string runtimeApiMin,
        List<CharacterDiagnostic> diagnostics)
    {
        foreach (var slot in definition.Slots)
        {
            int stageStart = 0;
            int windowCount = 0;
            var commitments = new List<int>();
            var windows = new List<(int StageIndex, int StageStart, CookedStartupAimCorrectionOperation Operation)>();
            for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
            {
                var stage = slot.Timeline.Stages[stageIndex];
                foreach (var operation in stage.Operations)
                {
                    if (operation is CookedStartupAimCorrectionOperation correction)
                    {
                        if (runtimeApiMin is not ("1.4.0" or "1.5.0" or "1.6.0" or "1.7.0" or "1.8.0" or "1.9.0" or "1.10.0" or "1.11.0"))
                            diagnostics.Add(Error("package.compatibility.unsupported", "manifest.runtimeApiMin",
                                "Startup aim correction requires runtime API 1.4.0 or later."));
                        windowCount++;
                        windows.Add((stageIndex, stageStart, correction));
                    }
                    else if (operation is CookedSpawnHitboxOperation or CookedSpawnProjectileOperation
                        or CookedForwardLungeOperation
                        || operation is CookedSetVelocityOperation velocity
                            && (velocity.X != 0f || velocity.Z != 0f))
                    {
                        commitments.Add(stageStart + operation.Tick);
                    }
                }
                stageStart += stage.DurationTicks;
            }
            if (windowCount > 1)
                diagnostics.Add(Error("package.operation.invalid", slot.Id,
                    "A timeline may contain only one startup aim-correction window."));
            foreach (var (stageIndex, start, correction) in windows)
            {
                var stage = slot.Timeline.Stages[stageIndex];
                string path = slot.Id + ".timeline.stages[" + stageIndex + "].startupAimCorrection";
                float[] values =
                {
                    correction.AcquisitionRange, correction.AcquisitionHalfAngleDegrees,
                    correction.MaxYawDegrees, correction.MaxPitchDegrees,
                    correction.YawDegreesPerSecond, correction.PitchDegreesPerSecond,
                };
                bool finite = values.All(value => !float.IsNaN(value) && !float.IsInfinity(value));
                int globalStart = start + correction.Tick;
                int cutoff = start + correction.EndTick;
                int? nextCommitment = commitments.Where(value => value >= globalStart)
                    .Select(value => (int?)value).Min();
                if (!finite || correction.Unit != AuthoringUnit.Ticks
                    || correction.EndTick <= correction.Tick || correction.EndTick > stage.DurationTicks
                    || correction.AcquisitionRange <= 0f || correction.AcquisitionRange > CharacterPackageCompiler.MaxStartupAcquisitionRange
                    || correction.AcquisitionHalfAngleDegrees <= 0f || correction.AcquisitionHalfAngleDegrees > CharacterPackageCompiler.MaxStartupAngleDegrees
                    || correction.MaxYawDegrees < 0f || correction.MaxYawDegrees > CharacterPackageCompiler.MaxStartupAngleDegrees
                    || correction.MaxPitchDegrees < 0f || correction.MaxPitchDegrees > CharacterPackageCompiler.MaxStartupPitchDegrees
                    || correction.YawDegreesPerSecond < 0f || correction.YawDegreesPerSecond > CharacterPackageCompiler.MaxStartupRateDegreesPerSecond
                    || correction.MaxYawDegrees > 0f && correction.YawDegreesPerSecond == 0f
                    || correction.PitchDegreesPerSecond < 0f || correction.PitchDegreesPerSecond > CharacterPackageCompiler.MaxStartupRateDegreesPerSecond
                    || correction.MaxPitchDegrees > 0f && correction.PitchDegreesPerSecond == 0f
                    || commitments.Any(value => value < globalStart)
                    || nextCommitment.HasValue && cutoff > nextCommitment.Value)
                    diagnostics.Add(Error("package.operation.invalid", path,
                        "Startup aim correction has invalid bounds, cutoff, unit, or follows a prior commitment."));
            }
        }
    }
    private static void ValidateFixedHitstun(
        ushort fixedTicks,
        ushort stunGate,
        string path,
        List<CharacterDiagnostic> diagnostics,
        ref bool requiresApi13)
    {
        if (fixedTicks == 0) return;
        requiresApi13 = true;
        if (fixedTicks > 240 || stunGate == 0)
            diagnostics.Add(Error("package.hitbox.invalid", path,
                "Fixed hitstun must be at most 240 ticks and requires a nonzero stun gate."));
    }
    private sealed class ManifestInfo { public string PackageId=""; public string Version=""; public ushort CookedSchemaVersion; public string RuntimeApiMin=""; public string RuntimeApiMax=""; public string SourceHash=""; public string CookedContentHash=""; public string PackageHash=""; public readonly List<PackageDependencySource> Dependencies=new(); public readonly List<CookedCapabilityRequirement> Capabilities=new(); }
    private static ManifestInfo ParseManifest(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes); var r=doc.RootElement; if(r.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
        var m=new ManifestInfo{PackageId=S(r,"packageId"),Version=S(r,"version"),CookedSchemaVersion=U(r,"cookedSchemaVersion"),SourceHash=S(r,"sourceHash"),CookedContentHash=S(r,"cookedContentHash"),PackageHash=S(r,"packageHash")};
        m.RuntimeApiMin=S(r,"runtimeApiMin"); m.RuntimeApiMax=S(r,"runtimeApiMax");
        foreach(var x in A(r,"dependencies").EnumerateArray()){var q=O(x,"packageId","version","cookedHash");m.Dependencies.Add(new PackageDependencySource(S(q,"packageId"),S(q,"version"),S(q,"cookedHash")));}
        foreach(var x in A(r,"capabilityRequirements").EnumerateArray()){var q=O(x,"capabilityId","capabilityVersion");m.Capabilities.Add(new CookedCapabilityRequirement(S(q,"capabilityId"),S(q,"capabilityVersion")));} return m;
    }
    private static Dictionary<string,JsonElement> O(JsonElement e,params string[] f){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var set=new HashSet<string>(f,StringComparer.Ordinal);var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!set.Contains(p.Name)||!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Unknown or duplicate field: "+p.Name);foreach(var x in f)if(!d.ContainsKey(x))throw new InvalidDataException("Missing field: "+x);return d;}
    private static string S(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()!:throw new InvalidDataException(n+" must be a string.");
    private static ushort U(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be an unsigned integer.");
    private static string S(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw new InvalidDataException(n+" must be a string.");
    private static ushort U(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be an unsigned integer.");
    private static JsonElement A(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");
    private static JsonElement A(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");

    private static class RuntimeParser
    {
        public static CookedCharacterPackage Parse(byte[] bytes)
        {
            using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;var mm=O(root.GetProperty("metadata"),"packageId","version","cookedSchemaVersion","compatibility");var api=O(mm["compatibility"],"runtimeApiMin","runtimeApiMax");var metadata=new CookedPackageMetadata(S(mm,"packageId"),S(mm,"version"),U(mm,"cookedSchemaVersion"),S(api,"runtimeApiMin"),S(api,"runtimeApiMax"));
            var c = O(root.GetProperty("character"),
                "displayName", "weight", "movement", "presentation", "capsuleRadius", "capsuleHeight",
                "hipHeight", "hurtboxRadius", "shieldRadius", "captureGeometry", "hurtboxCapsules", "hurtboxBoneDefs",
                "attachmentBoneIds", "presentationIds", "capabilityRequirements", "slots");
            var mv = O(c["movement"], "runSpeed", "runAccelerationA", "runAccelerationB", "dashSpeed", "airDodgeSpeed",
                "airSpeedMax", "airAccelStick", "airAccelBase", "jumpForce", "shortHopForce",
                "airJumpVMultiplier", "airJumpHMultiplier", "gravity", "airFloatGravity",
                "dashDurationTicks", "dashCooldownTicks", "groundFriction", "airFriction",
                "maxFallSpeed", "fastFallSpeed", "maxJumps", "jumpSquatTicks", "floatWindowTicks", "rushTicks");
            var movement = new CookedMovement(F(mv, "runSpeed"), F(mv, "runAccelerationA"), F(mv, "runAccelerationB"),
                F(mv, "dashSpeed"), F(mv, "airDodgeSpeed"), F(mv, "airSpeedMax"), F(mv, "airAccelStick"), F(mv, "airAccelBase"),
                F(mv, "jumpForce"), F(mv, "shortHopForce"), F(mv, "airJumpVMultiplier"), F(mv, "airJumpHMultiplier"),
                F(mv, "gravity"), F(mv, "airFloatGravity"), U(mv, "dashDurationTicks"), U(mv, "dashCooldownTicks"),
                F(mv, "groundFriction"), F(mv, "airFriction"), F(mv, "maxFallSpeed"), F(mv, "fastFallSpeed"),
                B(mv, "maxJumps"), U(mv, "jumpSquatTicks"), U(mv, "floatWindowTicks"), U(mv, "rushTicks"));
            var pr = OOptional(c["presentation"],
                new[] { "tumble", "crouch", "slide", "shield", "grab", "grabbed", "throwForward", "airDodge" },
                "idle", "run", "dash", "jump", "fall", "hitSmall", "hitMedium", "hitHard",
                "tumble", "crouch", "slide", "shield", "grab", "grabbed", "throwForward", "airDodge",
                "landStartOffsetSeconds", "modelResourcePath", "visualScale", "hurtboxBoneScale",
                "modelYOffset", "modelSoleOffset", "autoModelYOffset");
            var presentation = new CookedPresentation(
                S(pr, "idle"), S(pr, "run"), S(pr, "dash"), S(pr, "jump"), S(pr, "fall"),
                S(pr, "hitSmall"), S(pr, "hitMedium"), S(pr, "hitHard"), F(pr, "landStartOffsetSeconds"),
                S(pr, "modelResourcePath"), F(pr, "visualScale"), F(pr, "hurtboxBoneScale"),
                F(pr, "modelYOffset"), F(pr, "modelSoleOffset"), Bo(pr, "autoModelYOffset"),
                SO(pr, "tumble", ""), SO(pr, "crouch", ""), SO(pr, "slide", ""),
                SO(pr, "shield", ""), SO(pr, "grab", ""), SO(pr, "grabbed", ""),
                SO(pr, "throwForward", ""), SO(pr, "airDodge", ""));
            var capture = O(c["captureGeometry"], "reach", "width", "height", "offsetY", "attackerAnchor", "victimAnchor");
            var attacker = O(capture["attackerAnchor"], "x", "y", "z");
            var victim = O(capture["victimAnchor"], "x", "y", "z");
            var captureGeometry = new CookedCaptureGeometry(
                F(capture, "reach"), F(capture, "width"), F(capture, "height"), F(capture, "offsetY"),
                new CaptureAnchor(F(attacker, "x"), F(attacker, "y"), F(attacker, "z")),
                new CaptureAnchor(F(victim, "x"), F(victim, "y"), F(victim, "z")));
            var capsules = A(c["hurtboxCapsules"]).EnumerateArray().Select(x =>
            {
                var q = O(x, "startX", "startY", "startZ", "endX", "endY", "endZ", "radius");
                return new CookedHurtboxCapsule(F(q, "startX"), F(q, "startY"), F(q, "startZ"), F(q, "endX"), F(q, "endY"), F(q, "endZ"), F(q, "radius"));
            }).ToList();
            var bones = A(c["hurtboxBoneDefs"]).EnumerateArray().Select(x =>
            {
                var q = O(x, "boneId", "offsetX", "offsetY", "offsetZ", "radius");
                return new CookedHurtboxBone(S(q, "boneId"), F(q, "offsetX"), F(q, "offsetY"), F(q, "offsetZ"), F(q, "radius"));
            }).ToList();
            var attachments = A(c["attachmentBoneIds"]).EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new InvalidDataException("Attachment bone ID must be a string."))
                .ToList();
            var ids = A(c["presentationIds"]).EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new InvalidDataException("Presentation ID must be a string."))
                .ToList();
            var caps = A(c["capabilityRequirements"]).EnumerateArray().Select(x =>
            {
                var q = O(x, "capabilityId", "capabilityVersion");
                return new CookedCapabilityRequirement(S(q, "capabilityId"), S(q, "capabilityVersion"));
            }).ToList();
            var slots = A(c["slots"]).EnumerateArray().Select(ParseSlot).ToList();
            float capsuleHeight = F(c, "capsuleHeight");
            float shieldRadius = F(c, "shieldRadius");
            if (float.IsNaN(shieldRadius) || float.IsInfinity(shieldRadius)
                || shieldRadius <= 0f || shieldRadius <= capsuleHeight * 0.5f)
                throw new InvalidDataException("Shield radius must be finite, positive, and greater than half capsule height.");
            var definition = new CookedCharacterDefinition(
                S(c, "displayName"), F(c, "weight"), movement, presentation,
                F(c, "capsuleRadius"), capsuleHeight, F(c, "hipHeight"),
                F(c, "hurtboxRadius"), shieldRadius, captureGeometry, capsules, bones, attachments, ids, caps, slots);
            var b = O(root.GetProperty("budget"), "slotCount", "stageCount", "operationCount", "hitboxCount",
                "projectileCount", "capabilityCount", "maxTimelineDurationTicks");
            var budget = new CookedBudget(I(b, "slotCount"), I(b, "stageCount"), I(b, "operationCount"),
                I(b, "hitboxCount"), I(b, "projectileCount"), I(b, "capabilityCount"), I(b, "maxTimelineDurationTicks"));
            return new CookedCharacterPackage(metadata, definition, budget, Array.Empty<CharacterDiagnostic>(), bytes);
        }
        private static CookedSlotDefinition ParseSlot(JsonElement e)
        {
            var q = OOptional(e, new[] { "aimMovement", "aimMovementSpeedScale", "aimAnimationId", "allowSlideCarry", "hitPresentationId" },
                "ordinal", "id", "isAir", "name", "description", "iconId", "behavior", "aimMode", "aimMovement", "aimMovementSpeedScale",
                "aimAnimationId", "hitPresentationId", "cooldownTicks", "isRecoveryMove", "preserveMomentumOnStart",
                "allowSlideCarry", "chargePool", "timeline");
            var pool = q["chargePool"].ValueKind == JsonValueKind.Null ? null : ParseChargePool(q["chargePool"]);
            var t = O(q["timeline"], "stages");
            string? hitPresentationId = null;
            if (q.TryGetValue("hitPresentationId", out var hit))
            {
                if (hit.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("hitPresentationId must be string.");
                hitPresentationId = hit.GetString();
            }
            return new CookedSlotDefinition(I(q, "ordinal"), S(q, "id"), Bo(q, "isAir"), S(q, "name"), S(q, "description"),
                S(q, "iconId"), (AuthoringAbilityBehavior)B(q, "behavior"), (AuthoringAimMode)B(q, "aimMode"),
                U(q, "cooldownTicks"), Bo(q, "isRecoveryMove"), Bo(q, "preserveMomentumOnStart"),
                new CookedTimeline(A(t, "stages").EnumerateArray().Select(ParseStage).ToList()), pool,
                (AuthoringAimMovementMode)BOrDefault(q, "aimMovement", 0), SO(q, "aimAnimationId", ""),
                BoOrDefault(q, "allowSlideCarry", false), hitPresentationId, FOrDefault(q, "aimMovementSpeedScale", 1f));
        }
        private static CookedChargePool ParseChargePool(JsonElement e){var q=O(e,"maxCharges","regenTicks");return new CookedChargePool(I(q,"maxCharges"),U(q,"regenTicks"));}
        private static CookedStage ParseStage(JsonElement e){var q=OOptional(e,new[]{"attackRange","warpRange","useTargetLock","rotateTowardTarget","trackingStrength"},"durationTicks","iasaTicks","landingLagTicks","autoCancelBeforeTicks","autoCancelAfterTicks","attackRange","warpRange","useTargetLock","rotateTowardTarget","trackingStrength","animationIds","operations");return new CookedStage(U(q,"durationTicks"),U(q,"iasaTicks"),U(q,"landingLagTicks"),U(q,"autoCancelBeforeTicks"),U(q,"autoCancelAfterTicks"),A(q["animationIds"]).EnumerateArray().Select(x=>x.GetString()!).ToList(),A(q["operations"]).EnumerateArray().Select(ParseOperation).ToList(),FOrDefault(q,"attackRange",0f),FOrDefault(q,"warpRange",0f),BoOrDefault(q,"useTargetLock",false),BoOrDefault(q,"rotateTowardTarget",false),FOrDefault(q,"trackingStrength",0f));}
        private static CookedTimelineOperation ParseOperation(JsonElement e)
        {
            var common = All(e);
            var kind = (CookedOperationKind)B(common, "kind");
            var tick = U(common, "tick");
            var unit = (AuthoringUnit)B(common, "unit");
            return kind switch
            {
                CookedOperationKind.SetVelocity => Velocity(e, tick, unit),
                CookedOperationKind.ForwardLunge => ForwardLunge(e, tick, unit),
                CookedOperationKind.GravityWindow => GravityWindow(e, tick, unit),
                CookedOperationKind.ArmorWindow => ArmorWindow(e, tick, unit),
                CookedOperationKind.StartupAimCorrection => StartupAimCorrection(e, tick, unit),
                CookedOperationKind.SpawnHitbox => new CookedSpawnHitboxOperation(tick, unit, ParseHitbox(O(e, "kind", "tick", "unit", "hitbox")["hitbox"])),
                CookedOperationKind.SpawnProjectile => new CookedSpawnProjectileOperation(tick, unit, ParseProjectile(O(e, "kind", "tick", "unit", "projectile")["projectile"])),
                CookedOperationKind.SetAimState => new CookedSetAimStateOperation(tick, unit, (AuthoringAimMode)B(O(e, "kind", "tick", "unit", "aimState"), "aimState")),
                CookedOperationKind.StartCapability => Capability(e, tick, unit),
                CookedOperationKind.EmitPresentation => Emit(e, tick, unit),
                CookedOperationKind.CompleteTimeline => new CookedCompleteTimelineOperation(tick, unit),
                _ => throw new InvalidDataException("Unknown cooked operation kind."),
            };
        }

        private static CookedSetVelocityOperation Velocity(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = O(e, "kind", "tick", "unit", "velocityMode", "x", "y", "z");
            return new CookedSetVelocityOperation(tick, unit, (AuthoringVelocityMode)B(q, "velocityMode"), F(q, "x"), F(q, "y"), F(q, "z"));
        }

        private static CookedForwardLungeOperation ForwardLunge(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = OOptional(e, new[] { "stopInAttackRange" }, "kind", "tick", "unit", "speed", "durationTicks");
            return new CookedForwardLungeOperation(tick, unit, F(q, "speed"), U(q, "durationTicks"), BoOrDefault(q, "stopInAttackRange", false));
        }
        private static CookedGravityWindowOperation GravityWindow(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = O(e, "kind", "tick", "unit", "gravityScale", "durationTicks");
            return new CookedGravityWindowOperation(tick, unit, F(q, "gravityScale"), U(q, "durationTicks"));
        }
        private static CookedArmorWindowOperation ArmorWindow(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = O(e, "kind", "tick", "unit", "durationTicks");
            return new CookedArmorWindowOperation(tick, unit, U(q, "durationTicks"));
        }
        private static CookedStartupAimCorrectionOperation StartupAimCorrection(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = O(e, "kind", "tick", "unit", "endTick", "acquisitionRange",
                "acquisitionHalfAngleDegrees", "maxYawDegrees", "maxPitchDegrees",
                "yawDegreesPerSecond", "pitchDegreesPerSecond");
            return new CookedStartupAimCorrectionOperation(tick, unit, U(q, "endTick"),
                F(q, "acquisitionRange"), F(q, "acquisitionHalfAngleDegrees"),
                F(q, "maxYawDegrees"), F(q, "maxPitchDegrees"),
                F(q, "yawDegreesPerSecond"), F(q, "pitchDegreesPerSecond"));
        }

        private static CookedStartCapabilityOperation Capability(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = O(e, "kind", "tick", "unit", "capabilityId", "capabilityVersion", "parameters");
            var id = S(q, "capabilityId");
            return new CookedStartCapabilityOperation(tick, unit, id, S(q, "capabilityVersion"), ParseParameters(id, q["parameters"]));
        }

        private static CookedEmitPresentationOperation Emit(JsonElement e, ushort tick, AuthoringUnit unit)
        {
            var q = OOptional(e, new[] { "placement" }, "kind", "tick", "unit", "presentationId", "operationIndex");
            var placement = q.TryGetValue("placement", out var value) ? Placement(value) : new PresentationPlacement();
            return new CookedEmitPresentationOperation(tick, unit, S(q, "presentationId"), I(q, "operationIndex"), placement);
        }

        private static PresentationPlacement Placement(JsonElement e)
        {
            var q = OOptional(e, new[]
            {
                "attachmentMode", "boneId",
                "localPositionX", "localPositionY", "localPositionZ",
                "localRotationX", "localRotationY", "localRotationZ",
                "localScaleX", "localScaleY", "localScaleZ", "durationTicks",
            });
            return new PresentationPlacement(
                (AuthoringPresentationAttachmentMode)BOrDefault(q, "attachmentMode", (byte)AuthoringPresentationAttachmentMode.World),
                N(q, "boneId"),
                FOrDefault(q, "localPositionX", 0f),
                FOrDefault(q, "localPositionY", 0f),
                FOrDefault(q, "localPositionZ", 0f),
                FOrDefault(q, "localRotationX", 0f),
                FOrDefault(q, "localRotationY", 0f),
                FOrDefault(q, "localRotationZ", 0f),
                FOrDefault(q, "localScaleX", 1f),
                FOrDefault(q, "localScaleY", 1f),
                FOrDefault(q, "localScaleZ", 1f),
                UOrDefault(q, "durationTicks", 28));
        }
        
        private static CookedHitbox ParseHitbox(JsonElement e)
        {
            var q = OOptional(e, new[] { "knockbackDirection", "fixedHitstunTicks" },
                "shape", "radius", "offsetX", "offsetY", "offsetZ", "endOffsetX", "endOffsetY", "endOffsetZ",
                "startBoneId", "endBoneId", "damage", "angle", "baseKnockback", "knockbackGrowth",
                "stunTicks", "durationTicks", "interruptible", "hitGroup", "knockbackDirection");
            return new CookedHitbox(
                (AuthoringHitboxShape)B(q, "shape"), F(q, "radius"), F(q, "offsetX"), F(q, "offsetY"),
                F(q, "offsetZ"), F(q, "endOffsetX"), F(q, "endOffsetY"), F(q, "endOffsetZ"),
                N(q, "startBoneId"), N(q, "endBoneId"), F(q, "damage"), F(q, "angle"),
                F(q, "baseKnockback"), F(q, "knockbackGrowth"), U(q, "stunTicks"), U(q, "durationTicks"),
                Bo(q, "interruptible"), B(q, "hitGroup"),
                (AuthoringKnockbackDirection)BOrDefault(q, "knockbackDirection",
                    (byte)AuthoringKnockbackDirection.AwayFromOwner),
                UOrDefault(q, "fixedHitstunTicks", 0));
        }
        private static CookedProjectile ParseProjectile(JsonElement e)
        {
            var q = OOptional(e, new[] { "return" }, "launchOffsetX", "launchOffsetY", "launchOffsetZ", "speed", "gravity", "radius", "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks", "maxFlightTicks", "yawOffsetDegrees", "return");
            CookedProjectileReturn? back = null;
            if (q.TryGetValue("return", out var r))
            {
                var b = OOptional(r, new[] { "catchPresentationId", "selfHitPresentationId" }, "outboundTicks", "returnSpeed",
                    "returnAcceleration", "selfHitDamage", "selfHitStunTicks", "catchPresentationId", "selfHitPresentationId");
                back = new CookedProjectileReturn(U(b, "outboundTicks"), F(b, "returnSpeed"), F(b, "returnAcceleration"),
                    F(b, "selfHitDamage"), U(b, "selfHitStunTicks"), SO(b, "catchPresentationId", ""), SO(b, "selfHitPresentationId", ""));
            }
            return new CookedProjectile(F(q, "launchOffsetX"), F(q, "launchOffsetY"), F(q, "launchOffsetZ"), F(q, "speed"), F(q, "gravity"), F(q, "radius"), F(q, "damage"), F(q, "angle"), F(q, "baseKnockback"), F(q, "knockbackGrowth"), U(q, "stunTicks"), U(q, "maxFlightTicks"), F(q, "yawOffsetDegrees"), back);
        }
        private static CookedChargedProjectileCapabilityParameters ChargedProjectile(Dictionary<string, JsonElement> q)
            => new(U(q, "maxChargeTicks"), U(q, "tier2Ticks"), U(q, "tier3Ticks"), U(q, "fireTriggerTick"),
                F(q, "tier2Radius"), F(q, "tier3Radius"), ParseProjectile(q["projectile"]));
        private static CookedFingerGunsCapabilityParameters FingerGuns(Dictionary<string, JsonElement> q)
            => new(U(q, "fireTriggerTick"), U(q, "recoveryTicks"), B(q, "shotsPerReload"), U(q, "reloadTicks"),
                S(q, "reloadAnimationId"), F(q, "airMaxFallSpeed"), F(q, "airRecoilSpeed"), ParseProjectile(q["projectile"]));
        private static CookedChargedStreamCapabilityParameters ChargedStream(Dictionary<string, JsonElement> q)
            => new(U(q, "maxChargeTicks"), U(q, "fireTriggerTick"), U(q, "emitIntervalTicks"), U(q, "minStreamTicks"),
                U(q, "maxStreamTicks"), U(q, "recoveryTicks"), F(q, "maxBaseKnockback"), F(q, "minRecoilSpeed"),
                F(q, "maxRecoilSpeed"), ParseProjectile(q["projectile"]), SO(q, "sprayAnimationId", ""),
                q.ContainsKey("sprayLoopTicks") ? U(q, "sprayLoopTicks") : (ushort)0, SO(q, "endAnimationId", ""));
        private static CookedCapabilityParameters ParseParameters(string id, JsonElement e)
        {
            if (RetiredCapabilities.TryGetReason(id, out var retiredReason))
                throw new RetiredCapabilityException(id, retiredReason);
            return id switch
            {
                "slop.ability.aimed-recovery.v1" => AimedRecovery(OOptional(e, new[] { "hop", "riseSteerAcceleration" }, "startupTicks", "verticalSpeed", "directionalVerticalSpeed", "horizontalSpeed", "preparationRiseSpeed")),
                "slop.ability.charged-projectile.v1" => ChargedProjectile(O(e, "maxChargeTicks", "tier2Ticks", "tier3Ticks", "fireTriggerTick", "tier2Radius", "tier3Radius", "projectile")),
                "slop.ability.finger-guns.v1" => FingerGuns(O(e, "fireTriggerTick", "recoveryTicks", "shotsPerReload", "reloadTicks", "reloadAnimationId", "airMaxFallSpeed", "airRecoilSpeed", "projectile")),
                "slop.ability.charged-stream.v1" => ChargedStream(OOptional(e, new[] { "sprayAnimationId", "sprayLoopTicks", "endAnimationId" }, "maxChargeTicks", "fireTriggerTick", "emitIntervalTicks", "minStreamTicks", "maxStreamTicks", "recoveryTicks", "maxBaseKnockback", "minRecoilSpeed", "maxRecoilSpeed", "projectile", "sprayAnimationId", "sprayLoopTicks", "endAnimationId")),
                "slop.internal.fightguy.rising-dragon.v1" => RisingDragon(O(e, "riseSpeed", "riseTicks", "riseDelay")),
                "slop.internal.fightguy.cyclone-kick.v1" => CycloneKick(O(e, "forwardSpeed", "windupTicks", "hitboxEndTick", "durationTicks", "bodyRadius", "sideRadius", "sideOffset", "damage", "knockbackAngle", "knockbackBase", "knockbackGrowth", "stunTicks", "bodyY", "sideY")),
                "slop.ability.charged-directional-dash.v1" => ChargedDirectionalDash(O(e, "maxChargeTicks", "tier2Ticks", "tier3Ticks", "minDistance", "maxDistance", "dashSpeed", "finisherLeadTicks", "finisherSeekTick", "recoveryTicks", "tier2Damage", "tier3Damage", "traversalHitbox", "finisherHitbox")),
                "slop.internal.wibou.rising-slash.v1" => WibouRisingSlash(O(e, "riseSpeed", "riseTicks", "homingRange", "homingSpeed")),
                CharacterPackageCompiler.BladeFlurryCapabilityId => WibouBladeFlurry(O(e, "forwardSpeed", "ascentStartTick", "ascentEndTick", "descentStartTick", "riseSpeed", "descentSpeed")),
                "slop.ability.steered-charge.v1" => SteeredCharge(OOptional(e, SteeredChargeOptional, SteeredChargeFields)),
                "slop.ability.targeted-leap.v1" => TargetedLeap(OOptional(e, new[] { "launchPresentationId", "landingPresentationId" }, "maxAimTicks", "maxFlightTicks", "minRange", "maxRange", "launchVerticalSpeed", "landingSeekTick", "recoveryTicks", "hitbox")),
                "slop.internal.manki.round-bomb.v1" => MankiRoundBomb(OOptional(e, new[] { "explosionPresentationId" }, "throwTriggerTick", "maxRange", "launchAngle", "gravity", "hitboxRadius", "damage", "stunTicks", "maxFlightTicks", "kbAngle", "explosionDamage", "explosionRadius", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle")),
                "slop.internal.manki.jetpack-boost.v1" => MankiJetpackBoost(OOptional(e, new[] { "explosionPresentationId" }, "startupTicks", "verticalSpeed", "horizontalSpeed", "explosionRadius", "explosionDamage", "explosionKbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks")),
                "slop.internal.manki.bazooka.v2" => MankiBazooka(OOptional(e, new[] { "explosionPresentationId" }, "homingTurnDegPerTick", "homingTurnCos", "homingTurnSin", "fireTriggerTick", "projectileSpeed", "hitboxRadius", "damage", "gravity", "maxFlightTicks", "stunTicks", "explosionRadius", "kbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "castDuration", "recoveryDuration")),
                "slop.internal.manki.aerosol-inferno.v1" => MankiAerosolInferno(OOptional(e, new[] { "minimumChargeTicks", "chargeTiers" }, "fireTriggerTick", "fireDurationTicks", "hitboxDurationTicks", "hitboxRadius", "offsetY", "offsetZ", "endOffsetZ", "damage", "knockbackAngle", "knockbackBase", "knockbackGrowth", "stunTicks", "hitGroup")),

                _ => throw new InvalidDataException("Unknown capability parameters.")
            };
        }
        private static CookedCapabilityParameters AimedRecovery(Dictionary<string, JsonElement> q)
            => new CookedAimedRecoveryCapabilityParameters(U(q, "startupTicks"), F(q, "verticalSpeed"),
                F(q, "directionalVerticalSpeed"), F(q, "horizontalSpeed"), F(q, "preparationRiseSpeed"),
                q.TryGetValue("hop", out var hop) ? RecoveryHop(O(hop, "startTicks", "contactTicks", "forwardDistance", "arcHeight", "landingHeight")) : null,
                FOrDefault(q, "riseSteerAcceleration", 0f));
        private static RecoveryHopParameters RecoveryHop(Dictionary<string, JsonElement> q)
            => new(U(q, "startTicks"), U(q, "contactTicks"), F(q, "forwardDistance"), F(q, "arcHeight"), F(q, "landingHeight"));
        private static CookedCapabilityParameters RisingDragon(Dictionary<string, JsonElement> q) => new CookedRisingDragonCapabilityParameters(F(q, "riseSpeed"), U(q, "riseTicks"), U(q, "riseDelay"));
        private static CookedCapabilityParameters CycloneKick(Dictionary<string, JsonElement> q) => new CookedCycloneKickCapabilityParameters(F(q, "forwardSpeed"), U(q, "windupTicks"), U(q, "hitboxEndTick"), U(q, "durationTicks"), F(q, "bodyRadius"), F(q, "sideRadius"), F(q, "sideOffset"), F(q, "damage"), F(q, "knockbackAngle"), F(q, "knockbackBase"), F(q, "knockbackGrowth"), U(q, "stunTicks"), F(q, "bodyY"), F(q, "sideY"));
        private static CookedCapabilityParameters ChargedDirectionalDash(Dictionary<string, JsonElement> q)
            => new CookedChargedDirectionalDashCapabilityParameters(
                U(q, "maxChargeTicks"), U(q, "tier2Ticks"), U(q, "tier3Ticks"),
                F(q, "minDistance"), F(q, "maxDistance"), F(q, "dashSpeed"),
                U(q, "finisherLeadTicks"), U(q, "finisherSeekTick"), U(q, "recoveryTicks"),
                F(q, "tier2Damage"), F(q, "tier3Damage"),
                ParseHitbox(q["traversalHitbox"]), ParseHitbox(q["finisherHitbox"]));
        private static CookedCapabilityParameters WibouRisingSlash(Dictionary<string, JsonElement> q) => new CookedWibouRisingSlashCapabilityParameters(F(q, "riseSpeed"), U(q, "riseTicks"), F(q, "homingRange"), F(q, "homingSpeed"));
        private static CookedCapabilityParameters WibouBladeFlurry(Dictionary<string, JsonElement> q) => new CookedWibouBladeFlurryCapabilityParameters(F(q, "forwardSpeed"), U(q, "ascentStartTick"), U(q, "ascentEndTick"), U(q, "descentStartTick"), F(q, "riseSpeed"), F(q, "descentSpeed"));
        private static CookedCapabilityParameters TargetedLeap(Dictionary<string, JsonElement> q)
            => new CookedTargetedLeapCapabilityParameters(
                U(q, "maxAimTicks"), U(q, "maxFlightTicks"), F(q, "minRange"), F(q, "maxRange"),
                F(q, "launchVerticalSpeed"), U(q, "landingSeekTick"), U(q, "recoveryTicks"),
                ParseHitbox(q["hitbox"]), SO(q, "launchPresentationId", ""), SO(q, "landingPresentationId", ""));
        private static readonly string[] SteeredChargeOptional =
        {
            "gallopAnimationId", "gallopLoopTicks", "hitEndingAnimationId", "missEndingAnimationId", "airEndingAnimationId",
            "gallopPresentationId", "hitEndingPresentationId", "missEndingPresentationId", "airEndingPresentationId",
        };
        private static readonly string[] SteeredChargeFields =
        {
            "gallopSpeed", "turnDegreesPerSecond", "maxGallopTicks", "armored", "lanceHitbox", "hitEndingTicks",
            "missEndingTicks", "skidTicks", "turnAroundOnMiss", "airStrideTicks", "lookDownTicks",
        };
        private static CookedCapabilityParameters SteeredCharge(Dictionary<string, JsonElement> q)
            => new CookedSteeredChargeCapabilityParameters(
                F(q, "gallopSpeed"), F(q, "turnDegreesPerSecond"), U(q, "maxGallopTicks"), Bo(q, "armored"),
                ParseHitbox(q["lanceHitbox"]), U(q, "hitEndingTicks"), U(q, "missEndingTicks"), U(q, "skidTicks"),
                Bo(q, "turnAroundOnMiss"), U(q, "airStrideTicks"), U(q, "lookDownTicks"),
                SO(q, "gallopAnimationId", ""), UOrDefault(q, "gallopLoopTicks", 0),
                SO(q, "hitEndingAnimationId", ""), SO(q, "missEndingAnimationId", ""), SO(q, "airEndingAnimationId", ""),
                SO(q, "gallopPresentationId", ""), SO(q, "hitEndingPresentationId", ""),
                SO(q, "missEndingPresentationId", ""), SO(q, "airEndingPresentationId", ""));
        private static CookedCapabilityParameters MankiRoundBomb(Dictionary<string,JsonElement> q) => new CookedMankiRoundBombCapabilityParameters(U(q, "throwTriggerTick"), F(q, "maxRange"), F(q, "launchAngle"), F(q, "gravity"), F(q, "hitboxRadius"), F(q, "damage"), U(q, "stunTicks"), U(q, "maxFlightTicks"), F(q, "kbAngle"), F(q, "explosionDamage"), F(q, "explosionRadius"), F(q, "explosionKbBase"), F(q, "explosionKbGrowth"), U(q, "explosionStunTicks"), U(q, "explosionDurationTicks"), F(q, "explosionKbAngle"), SO(q, "explosionPresentationId", ""));
        private static CookedCapabilityParameters MankiJetpackBoost(Dictionary<string,JsonElement> q) => new CookedMankiJetpackBoostCapabilityParameters(U(q, "startupTicks"), F(q, "verticalSpeed"), F(q, "horizontalSpeed"), F(q, "explosionRadius"), F(q, "explosionDamage"), F(q, "explosionKbAngle"), F(q, "explosionKbBase"), F(q, "explosionKbGrowth"), U(q, "explosionStunTicks"), U(q, "explosionDurationTicks"), SO(q, "explosionPresentationId", ""));
        private static CookedCapabilityParameters MankiBazooka(Dictionary<string,JsonElement> q) => new CookedMankiBazookaCapabilityParameters(U(q, "fireTriggerTick"), F(q, "projectileSpeed"), F(q, "hitboxRadius"), F(q, "damage"), F(q, "gravity"), U(q, "maxFlightTicks"), U(q, "stunTicks"), F(q, "explosionRadius"), F(q, "kbAngle"), F(q, "explosionKbBase"), F(q, "explosionKbGrowth"), U(q, "explosionStunTicks"), U(q, "explosionDurationTicks"), F(q, "explosionKbAngle"), U(q, "castDuration"), U(q, "recoveryDuration"), F(q, "homingTurnDegPerTick"), F(q, "homingTurnCos"), F(q, "homingTurnSin"), SO(q, "explosionPresentationId", ""));
        private static CookedCapabilityParameters MankiAerosolInferno(Dictionary<string, JsonElement> q) => new CookedMankiAerosolInfernoCapabilityParameters(U(q, "fireTriggerTick"), U(q, "fireDurationTicks"), U(q, "hitboxDurationTicks"), F(q, "hitboxRadius"), F(q, "offsetY"), F(q, "offsetZ"), F(q, "endOffsetZ"), F(q, "damage"), F(q, "knockbackAngle"), F(q, "knockbackBase"), F(q, "knockbackGrowth"), U(q, "stunTicks"), B(q, "hitGroup"), q.ContainsKey("minimumChargeTicks") ? U(q, "minimumChargeTicks") : (ushort)0,
            q.ContainsKey("chargeTiers") ? Array.AsReadOnly(A(q["chargeTiers"]).EnumerateArray().Select(value =>
            {
                var tier = O(value, "chargeTicks", "damage", "reachScale");
                return new MankiAerosolChargeTier(U(tier, "chargeTicks"), F(tier, "damage"), F(tier, "reachScale"));
            }).ToArray()) : null);

        private static Dictionary<string,JsonElement> O(JsonElement e,params string[] f){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var set=new HashSet<string>(f,StringComparer.Ordinal);var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!set.Contains(p.Name)||!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Unknown or duplicate field: "+p.Name);foreach(var x in f)if(!d.ContainsKey(x))throw new InvalidDataException("Missing field: "+x);return d;}
        private static Dictionary<string,JsonElement> OOptional(JsonElement e,string[] optional,params string[] f){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var set=new HashSet<string>(f,StringComparer.Ordinal);foreach(var x in optional)set.Add(x);var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!set.Contains(p.Name)||!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Unknown or duplicate field.");foreach(var x in f)if(!optional.Contains(x,StringComparer.Ordinal)&&!d.ContainsKey(x))throw new InvalidDataException("Missing field.");return d;}
        private static byte BOrDefault(Dictionary<string,JsonElement>d,string n,byte fallback){if(!d.TryGetValue(n,out var e))return fallback;if(e.TryGetByte(out var v))return v;throw new InvalidDataException(n+" must be unsigned byte.");}
        private static float FOrDefault(Dictionary<string,JsonElement>d,string n,float fallback){if(!d.TryGetValue(n,out var e))return fallback;if(e.TryGetSingle(out var v))return v;throw new InvalidDataException(n+" must be number.");}
        private static bool BoOrDefault(Dictionary<string,JsonElement>d,string n,bool fallback){if(!d.TryGetValue(n,out var e))return fallback;if(e.ValueKind==JsonValueKind.True||e.ValueKind==JsonValueKind.False)return e.GetBoolean();throw new InvalidDataException(n+" must be boolean.");}
        private static Dictionary<string,JsonElement> All(JsonElement e){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Duplicate field.");return d;}
        private static JsonElement A(JsonElement e)=>e.ValueKind==JsonValueKind.Array?e:throw new InvalidDataException("Array required.");
        private static JsonElement A(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");
        private static ushort UOrDefault(Dictionary<string,JsonElement>d,string n,ushort fallback){if(!d.TryGetValue(n,out var e))return fallback;if(e.TryGetUInt16(out var v))return v;throw new InvalidDataException(n+" must be unsigned integer.");}
        private static string SO(Dictionary<string,JsonElement>d,string n,string fallback){if(!d.TryGetValue(n,out var e))return fallback;if(e.ValueKind!=JsonValueKind.String)throw new InvalidDataException(n+" must be string.");return e.GetString()!;}
        private static string S(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()!:throw new InvalidDataException(n+" must be string.");private static string S(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw new InvalidDataException(n+" must be string.");private static string? N(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;private static float F(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetSingle(out var v)?v:throw new InvalidDataException(n+" must be number.");private static float F(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetSingle(out var v)?v:throw new InvalidDataException(n+" must be number.");private static ushort U(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be unsigned integer.");private static ushort U(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be unsigned integer.");private static byte B(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetByte(out var v)?v:throw new InvalidDataException(n+" must be unsigned byte.");private static byte B(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetByte(out var v)?v:throw new InvalidDataException(n+" must be unsigned byte.");private static bool Bo(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&(e.ValueKind==JsonValueKind.True||e.ValueKind==JsonValueKind.False)?e.GetBoolean():throw new InvalidDataException(n+" must be boolean.");private static int I(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetInt32(out var v)?v:throw new InvalidDataException(n+" must be integer.");private static int I(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetInt32(out var v)?v:throw new InvalidDataException(n+" must be integer.");
    }
}
