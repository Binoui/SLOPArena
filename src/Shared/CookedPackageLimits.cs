using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>Shared admission work and memory ceilings. These are not source schema or wire limits.</summary>
internal static class CookedPackageLimits
{
    internal const int ManifestBytes = 16 * 1024;
    internal const int RuntimeBytes = 2 * 1024 * 1024;
    internal const int BindingBytes = 64 * 1024;
    internal const int PoseBytes = 4 * 1024 * 1024;
    internal const int IdentifierCharacters = 256;
    internal const int PoseIdentifierBytes = 128;
    internal const int BodyHurtboxes = 32;
    internal const int AttachmentPoints = 8;
    internal const int PresentationIds = 256;
    internal const int StageAnimations = 32;
    internal const int PoseBones = 128;
    internal const int PoseAnimations = 256;
    internal const int PoseFrames = 4096;
    internal const int PoseFloatValues = 1024 * 1024;
    internal const int OperationsPerStage = CookedBudget.MaxOperationsPerStage;
    internal const int OperationsPerTimeline = CookedBudget.MaxOperationsPerTimeline;
    internal const int SpawnsPerTick = 128;
    // Runtime pools: projectiles are conservatively all non-freezing resolver volumes.
    internal const int LiveVolumesPerOwner = 16;
    internal const int LiveVolumesPerWorld = 64;
    internal const int ProjectileVolumesPerOwner = 12;
    internal const int ProjectileVolumesPerWorld = 48;
    internal const int SweepPoseUnits = HitboxGeometry.MaxSweepSteps + 1;
    internal const int CollisionPoseUnitsPerOwner = 200;
    internal const int CollisionPoseUnitsPerWorld = 800;
    internal const int MatchSeats = 4;
    internal const int CollisionChecksPerTick = CollisionPoseUnitsPerWorld * (MatchSeats - 1) * (BodyHurtboxes + 1);
    internal const int RequestedCollisionPoseUnits = 10000;
    internal const int RequestedCollisionChecks = RequestedCollisionPoseUnits * (MatchSeats - 1) * (BodyHurtboxes + 1);
    internal const int HitboxLifetimeEnvelope = 2048;
    internal const int ProjectileLifetimeEnvelope = 4560;
    internal const int HitboxesPerTimeline = 64;
    internal const int ProjectilesPerTimeline = 16;
    internal const float ProjectileSpeed = 120f;
    internal const int ProjectileLifetime = 960;
    internal const int AttachedProjectileLifetime = 2280;

    internal static int FileBytes(string path) => path switch
    {
        CharacterPackageAssembler.ManifestPath => ManifestBytes,
        CharacterPackageAssembler.RuntimePath => RuntimeBytes,
        CharacterPackageAssembler.BindingPath => BindingBytes,
        CharacterPackageAssembler.PosePath => PoseBytes,
        _ => 0,
    };

    internal static bool CheckFiles(IReadOnlyDictionary<string, byte[]> files, List<CharacterDiagnostic> diagnostics)
    {
        if (files == null || files.Count != 4)
        { Add(diagnostics, "package.file.limit", "files", "Exactly four cooked files are required."); return false; }
        foreach (var file in files)
            if (file.Value == null || file.Value.Length > FileBytes(file.Key))
                Add(diagnostics, "package.file.limit", file.Key, "File exceeds the admission byte limit.");
        return diagnostics.Count == 0;
    }

    internal static bool CheckRuntime(byte[] bytes, List<CharacterDiagnostic> diagnostics, CharacterCookProfile profile)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        var character = document.RootElement.GetProperty("character");
        if (character.GetProperty("hurtboxCapsules").GetArrayLength() + character.GetProperty("hurtboxBoneDefs").GetArrayLength() > BodyHurtboxes)
            Add(diagnostics, "package.hurtboxes.limit", "character", "Body hurtbox count limit exceeded.");
        if (character.GetProperty("attachmentBoneIds").GetArrayLength() > AttachmentPoints)
            Add(diagnostics, "package.collection.limit", "character.attachmentBoneIds", "Attachment point count limit exceeded.");
        if (character.GetProperty("presentationIds").GetArrayLength() > PresentationIds)
            Add(diagnostics, "package.collection.limit", "character.presentationIds", "Presentation ID count limit exceeded.");
        if (character.GetProperty("capabilityRequirements").GetArrayLength() > CookedBudget.MaxCapabilityRequirements)
            Add(diagnostics, "package.collection.limit", "character.capabilityRequirements", "Capability requirement count limit exceeded.");
        CheckSlots(character.GetProperty("slots"), diagnostics);
        if (character.TryGetProperty("alternateForm", out var form)) CheckSlots(form.GetProperty("slots"), diagnostics);
        CheckValues(character, diagnostics, profile);
        return diagnostics.Count == 0;
    }

    private static void CheckSlots(JsonElement slots, List<CharacterDiagnostic> diagnostics)
    {
        var expected = new HashSet<string>(new[] { "ground", "air" }.SelectMany(prefix =>
            new[] { "1", "2", "3", "A", "E", "R", "F" }.Select(key => prefix + "." + key)), StringComparer.Ordinal);
        foreach (var slot in slots.EnumerateArray())
        {
            string id = slot.GetProperty("id").GetString()!;
            var keys = new[] { "1", "2", "3", "A", "E", "R", "F" };
            bool air = id.StartsWith("air.", StringComparison.Ordinal);
            int ordinal = Array.IndexOf(keys, id.Substring(id.IndexOf('.') + 1)) + (air ? 7 : 0);
            if (slot.GetProperty("ordinal").GetInt32() != ordinal || slot.GetProperty("isAir").GetBoolean() != air)
                Add(diagnostics, "package.slot.invalid", id, "Canonical slot ordinal and air identity must agree.");
            if (!expected.Remove(id)) Add(diagnostics, "package.slot.invalid", id, "Unknown or duplicate canonical slot.");
            CheckEnum<AuthoringAbilityBehavior>(slot, "behavior", diagnostics);
            CheckEnum<AuthoringAimMode>(slot, "aimMode", diagnostics);
            CheckEnum<AuthoringAimMovementMode>(slot, "aimMovement", diagnostics);
            CheckEnum<ShellPolicy>(slot, "shellPolicy", diagnostics);
            var stages = slot.GetProperty("timeline").GetProperty("stages");
            if (stages.GetArrayLength() == 0 || stages.GetArrayLength() > CookedBudget.MaxStagesPerTimeline)
                Add(diagnostics, "package.timeline.stages", id, "Timeline must have one to eight stages.");
            int total = 0, hitboxes = 0, projectiles = 0;
            foreach (var stage in stages.EnumerateArray())
            {
                int duration = stage.GetProperty("durationTicks").GetInt32();
                if (duration <= 0) Add(diagnostics, "package.timeline.duration", id, "Stage duration must be positive.");
                if (stage.GetProperty("animationIds").GetArrayLength() > StageAnimations)
                    Add(diagnostics, "package.collection.limit", id, "Stage animation count limit exceeded.");
                var operations = stage.GetProperty("operations");
                total += operations.GetArrayLength();
                if (operations.GetArrayLength() > OperationsPerStage) Add(diagnostics, "package.operations.limit", id, "Stage operation limit exceeded.");
                var spawns = new Dictionary<int, int>();
                int previous = -1;
                foreach (var operation in operations.EnumerateArray())
                {
                    CheckEnum<CookedOperationKind>(operation, "kind", diagnostics);
                    CheckEnum<AuthoringUnit>(operation, "unit", diagnostics);
                    int tick = operation.GetProperty("tick").GetInt32();
                    if (tick < previous || tick < 0 || tick >= duration)
                        Add(diagnostics, "package.operation.tick", id, "Operations must be ordered and inside their stage.");
                    previous = tick;
                    var kind = (CookedOperationKind)operation.GetProperty("kind").GetInt32();
                    if (kind == CookedOperationKind.SpawnHitbox) hitboxes++;
                    if (kind == CookedOperationKind.SpawnProjectile) projectiles++;
                    if (kind is CookedOperationKind.SpawnHitbox or CookedOperationKind.SpawnProjectile)
                    {
                        spawns.TryGetValue(tick, out int count);
                        spawns[tick] = count + 1;
                        if (count + 1 > SpawnsPerTick) Add(diagnostics, "package.spawns.limit", id, "Per tick spawn limit exceeded.");
                    }
                }
            }
            if (total > OperationsPerTimeline || hitboxes > HitboxesPerTimeline || projectiles > ProjectilesPerTimeline)
                Add(diagnostics, "package.timeline.limit", id, "Timeline work limit exceeded.");
        }
        if (expected.Count != 0) Add(diagnostics, "package.slot.missing", "slots", "Every form requires the canonical fourteen slots.");
    }

    internal static (long Hitboxes, long Projectiles, int Emitters) ResourceEnvelope(CookedCharacterDefinition definition)
    {
        long hitboxes = 0, projectiles = 0;
        int emitters = 0;
        void Hitbox(int lifetime) { hitboxes += Math.Max(1, lifetime); emitters++; }
        void Projectile(CookedProjectile projectile)
        {
            projectiles += projectile.MaxFlightTicks + (projectile.Perch?.PerchTicks ?? 0);
            emitters++;
        }
        foreach (var operation in definition.AllSlots.SelectMany(slot => slot.Timeline.Stages).SelectMany(stage => stage.Operations))
        {
            if (operation is CookedSpawnHitboxOperation hitbox) Hitbox(hitbox.Hitbox.DurationTicks);
            if (operation is CookedSpawnProjectileOperation projectile) Projectile(projectile.Projectile);
            if (operation is not CookedStartCapabilityOperation capability) continue;
            // Internal capabilities are engine-owned behavior and are refused under Workshop before admission.
            if (capability.CapabilityId.StartsWith("slop.internal.", StringComparison.Ordinal)) continue;
            switch (capability.Parameters)
            {
                case CookedTargetedLeapCapabilityParameters leap: Hitbox(leap.Hitbox.DurationTicks); break;
                case CookedThrustCapabilityParameters thrust:
                    if (thrust.TrailHitbox != null) Hitbox(thrust.ThrustTicks);
                    break;
                case CookedDashStrikeCapabilityParameters dash:
                    Hitbox(1); Hitbox(dash.TipHitbox.DurationTicks); break;
                case CookedFlashCutCapabilityParameters flash: Hitbox(flash.LegTicks + 1); break;
                case CookedCommandGrabCapabilityParameters: Hitbox(1); break;
                case CookedSteeredChargeCapabilityParameters:
                    // Both volumes are removed together on leaving gallop or on cancellation. They cannot overlap a later activation.
                    Hitbox(1); Hitbox(1); break;
                case CookedChargedProjectileCapabilityParameters charged: Projectile(charged.Projectile); break;
                case CookedChargedStreamCapabilityParameters stream: Projectile(stream.Projectile); break;
                case CookedFingerGunsCapabilityParameters guns: Projectile(guns.Projectile); break;
                case CookedTetherCapabilityParameters tether:
                    if (tether.Projectile != null) Projectile(tether.Projectile);
                    if (tether.AtTarget is { } at)
                    {
                        Hitbox(at.CatchTicks);
                        if (at.Unmarked is { } hand) Hitbox(hand.ActiveTicks);
                    }
                    break;
                case CookedAimedRecoveryCapabilityParameters:
                case CookedCannonballCapabilityParameters: break;
                default: throw new System.IO.InvalidDataException("Capability has no resource accounting contract.");
            }
        }
        if (definition.Shell?.Splash is { } splash) Hitbox(splash.Hitbox.DurationTicks);
        // The simulation maintains one owner wave window, replacing it rather than accumulating waves.
        if (definition.Shell?.Ride != null) Hitbox(1);
        return (hitboxes, projectiles, emitters);
    }

    internal static void CheckResources(CookedCharacterDefinition definition, List<CharacterDiagnostic> diagnostics)
    {
        // Each public emitter runs at most once per owner tick. This is a requested-work envelope; runtime pools bound actual overlap.
        var envelope = ResourceEnvelope(definition);
        if (envelope.Emitters > SpawnsPerTick)
            Add(diagnostics, "package.spawns.limit", "character", "Per fighter emitter limit exceeded.");
        if (envelope.Hitboxes > HitboxLifetimeEnvelope || envelope.Projectiles > ProjectileLifetimeEnvelope)
            Add(diagnostics, "package.live.limit", "character", "Requested effect lifetime envelope exceeds the fighter limit.");
        // Charge every melee emitter for a full bone sweep, even when its current geometry is cheaper.
        // Opponents may each use the largest admitted body plus a shield surface. The runtime weighted
        // pool clips overlapping activations independently of cooldowns, forms, hitstop, and emitters.
        long requested = (envelope.Hitboxes * SweepPoseUnits + envelope.Projectiles)
            * (MatchSeats - 1) * (BodyHurtboxes + 1);
        if (requested > RequestedCollisionChecks)
            Add(diagnostics, "package.collision.limit", "character", "Requested sweep/body collision work exceeds the fighter envelope.");
    }

    private static void CheckEnum<T>(JsonElement value, string field, List<CharacterDiagnostic> diagnostics) where T : struct, Enum
    {
        if (value.TryGetProperty(field, out var item) && (!item.TryGetByte(out byte number) || !Enum.IsDefined(typeof(T), number)))
            Add(diagnostics, "package.enum.invalid", field, "Unknown enum value.");
    }

    private static void CheckValues(JsonElement value, List<CharacterDiagnostic> diagnostics, CharacterCookProfile profile)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) CheckValues(child, diagnostics, profile);
        if (value.ValueKind != JsonValueKind.Object) return;
        if (profile != CharacterCookProfile.TrustedBuiltIn && value.TryGetProperty("capabilityId", out var capability)
            && capability.GetString()!.StartsWith("slop.internal.", StringComparison.Ordinal))
            Add(diagnostics, "capability.untrusted", "capabilityId", "Internal capabilities require the trusted built-in profile.");
        if (value.TryGetProperty("maxFlightTicks", out var lifetime))
        {
            long totalLifetime = lifetime.GetInt64();
            if (value.TryGetProperty("perch", out var perch) && perch.TryGetProperty("perchTicks", out var perched))
                totalLifetime += perched.GetInt64();
            if (totalLifetime > AttachedProjectileLifetime || lifetime.GetInt32() <= 0 || lifetime.GetInt32() > ProjectileLifetime)
                Add(diagnostics, "package.projectile.limit", "maxFlightTicks", "Projectile lifetime limit exceeded.");
            foreach (string name in new[] { "speed", "projectileSpeed", "returnSpeed" })
                if (value.TryGetProperty(name, out var speed) && Math.Abs(speed.GetSingle()) > ProjectileSpeed)
                    Add(diagnostics, "package.projectile.limit", name, "Projectile speed limit exceeded.");
        }
        if (value.TryGetProperty("returnSpeed", out var returnSpeed) && Math.Abs(returnSpeed.GetSingle()) > ProjectileSpeed)
            Add(diagnostics, "package.projectile.limit", "returnSpeed", "Projectile return speed limit exceeded.");
        CheckEnum<AuthoringHitboxShape>(value, "shape", diagnostics);
        CheckEnum<AuthoringKnockbackDirection>(value, "knockbackDirection", diagnostics);
        CheckEnum<AuthoringVelocityMode>(value, "velocityMode", diagnostics);
        CheckEnum<AuthoringAimMode>(value, "aimState", diagnostics);
        CheckEnum<AuthoringPresentationCondition>(value, "condition", diagnostics);
        CheckEnum<AuthoringPresentationAttachmentMode>(value, "attachmentMode", diagnostics);
        CheckEnum<ThrustAim>(value, "aim", diagnostics);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && (property.Name.EndsWith("Id", StringComparison.Ordinal) || property.Name == "id")
                && property.Value.GetString()!.Length > IdentifierCharacters)
                Add(diagnostics, "package.identifier.limit", property.Name, "Identifier length limit exceeded.");
            if (property.Name is "animationIds" or "attachmentBoneIds" or "presentationIds")
                foreach (var id in property.Value.EnumerateArray())
                    if (id.GetString()!.Length > IdentifierCharacters)
                        Add(diagnostics, "package.identifier.limit", property.Name, "Identifier length limit exceeded.");
            if (property.Name == "presentation" && property.Value.ValueKind == JsonValueKind.Object)
                foreach (var role in property.Value.EnumerateObject())
                    if (role.Value.ValueKind == JsonValueKind.String && role.Value.GetString()!.Length > IdentifierCharacters)
                        Add(diagnostics, "package.identifier.limit", role.Name, "Animation identifier length limit exceeded.");
            if (property.Value.ValueKind == JsonValueKind.Number && (!property.Value.TryGetSingle(out var number) || float.IsNaN(number) || float.IsInfinity(number)))
                Add(diagnostics, "package.number.invalid", property.Name, "Finite number required.");
            CheckValues(property.Value, diagnostics, profile);
        }
    }

    private static void Add(List<CharacterDiagnostic> diagnostics, string code, string path, string message)
    {
        if (diagnostics.Count < 64) diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message));
    }
}
