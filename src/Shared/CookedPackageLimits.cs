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
    internal const int PoseBones = 128;
    internal const int PoseAnimations = 256;
    internal const int PoseFrames = 4096;
    internal const int PoseFloatValues = 1024 * 1024;
    internal const int OperationsPerStage = CookedBudget.MaxOperationsPerStage;
    internal const int OperationsPerTimeline = CookedBudget.MaxOperationsPerTimeline;
    internal const int SpawnsPerTick = 64;
    internal const int LiveHitboxes = 4096;
    internal const int LiveProjectiles = 2160;
    internal const int HitboxesPerTimeline = 64;
    internal const int ProjectilesPerTimeline = 16;
    internal const float ProjectileSpeed = 120f;
    internal const int ProjectileLifetime = 960;

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

    internal static bool CheckRuntime(byte[] bytes, List<CharacterDiagnostic> diagnostics)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        var character = document.RootElement.GetProperty("character");
        CheckSlots(character.GetProperty("slots"), diagnostics);
        if (character.TryGetProperty("alternateForm", out var form)) CheckSlots(form.GetProperty("slots"), diagnostics);
        var emitters = CountEmitters(character);
        if (emitters.Hitboxes + emitters.Projectiles > SpawnsPerTick)
            Add(diagnostics, "package.spawns.limit", "character", "Per fighter emitter limit exceeded.");
        var envelope = LiveEnvelope(character);
        if (envelope.Hitboxes > LiveHitboxes || envelope.Projectiles > LiveProjectiles)
            Add(diagnostics, "package.live.limit", "character", "Conservative live effect envelope exceeds the fighter limit.");
        CheckValues(character, diagnostics);
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

    // Assume each authored emitter can run every tick. This also covers overlap between activations.
    private static (long Hitboxes, long Projectiles) LiveEnvelope(JsonElement value, int capabilityLifetime = 0)
    {
        long hitboxes = 0, projectiles = 0;
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
            {
                var child = LiveEnvelope(item, capabilityLifetime);
                hitboxes += child.Hitboxes; projectiles += child.Projectiles;
            }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("capabilityId", out _) && value.TryGetProperty("parameters", out var parameters))
                capabilityLifetime = MaxTicks(parameters) + 1;
            if (value.TryGetProperty("shape", out _) && value.TryGetProperty("durationTicks", out var duration))
                hitboxes += Math.Min(LiveHitboxes + 1L, Math.Max(capabilityLifetime, Math.Max(0, duration.GetInt64())));
            if (value.TryGetProperty("maxFlightTicks", out var flight)) projectiles += Math.Min(LiveProjectiles + 1L, Math.Max(0, flight.GetInt64()));
            foreach (var property in value.EnumerateObject())
            {
                var child = LiveEnvelope(property.Value, capabilityLifetime);
                hitboxes += child.Hitboxes; projectiles += child.Projectiles;
            }
        }
        return (hitboxes, projectiles);
    }

    private static (int Hitboxes, int Projectiles) CountEmitters(JsonElement value)
    {
        int hitboxes = 0, projectiles = 0;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("shape", out _) && value.TryGetProperty("durationTicks", out _)) hitboxes++;
            if (value.TryGetProperty("maxFlightTicks", out _)) projectiles++;
            foreach (var property in value.EnumerateObject())
            {
                var child = CountEmitters(property.Value);
                hitboxes += child.Hitboxes; projectiles += child.Projectiles;
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
            {
                var child = CountEmitters(item);
                hitboxes += child.Hitboxes; projectiles += child.Projectiles;
            }
        return (hitboxes, projectiles);
    }

    private static int MaxTicks(JsonElement value)
    {
        int maximum = 0;
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.EndsWith("Ticks", StringComparison.Ordinal) && property.Value.TryGetInt32(out int ticks))
                    maximum = Math.Max(maximum, Math.Min(ushort.MaxValue, ticks));
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    maximum = Math.Max(maximum, MaxTicks(property.Value));
            }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) maximum = Math.Max(maximum, MaxTicks(item));
        return maximum;
    }

    private static void CheckEnum<T>(JsonElement value, string field, List<CharacterDiagnostic> diagnostics) where T : struct, Enum
    {
        if (value.TryGetProperty(field, out var item) && (!item.TryGetByte(out byte number) || !Enum.IsDefined(typeof(T), number)))
            Add(diagnostics, "package.enum.invalid", field, "Unknown enum value.");
    }

    private static void CheckValues(JsonElement value, List<CharacterDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) CheckValues(child, diagnostics);
        if (value.ValueKind != JsonValueKind.Object) return;
        if (value.TryGetProperty("maxFlightTicks", out var lifetime))
        {
            if (lifetime.GetInt32() <= 0 || lifetime.GetInt32() > ProjectileLifetime)
                Add(diagnostics, "package.projectile.limit", "maxFlightTicks", "Projectile lifetime limit exceeded.");
            foreach (string name in new[] { "speed", "projectileSpeed", "returnSpeed" })
                if (value.TryGetProperty(name, out var speed) && Math.Abs(speed.GetSingle()) > ProjectileSpeed)
                    Add(diagnostics, "package.projectile.limit", name, "Projectile speed limit exceeded.");
        }
        CheckEnum<AuthoringHitboxShape>(value, "shape", diagnostics);
        CheckEnum<AuthoringKnockbackDirection>(value, "knockbackDirection", diagnostics);
        CheckEnum<AuthoringVelocityMode>(value, "velocityMode", diagnostics);
        CheckEnum<AuthoringAimMode>(value, "aimState", diagnostics);
        CheckEnum<AuthoringPresentationCondition>(value, "condition", diagnostics);
        CheckEnum<AuthoringPresentationAttachmentMode>(value, "attachmentMode", diagnostics);
        CheckEnum<ThrustAim>(value, "aim", diagnostics);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && (!property.Value.TryGetSingle(out var number) || float.IsNaN(number) || float.IsInfinity(number)))
                Add(diagnostics, "package.number.invalid", property.Name, "Finite number required.");
            CheckValues(property.Value, diagnostics);
        }
    }

    private static void Add(List<CharacterDiagnostic> diagnostics, string code, string path, string message)
    {
        if (diagnostics.Count < 64) diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message));
    }
}
