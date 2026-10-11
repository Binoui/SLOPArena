using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed class CharacterPackageSourceLoadResult
{
    public CharacterPackageSource? Source { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool IsValid => Source != null && Diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);

    public CharacterPackageSourceLoadResult(CharacterPackageSource? source, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Source = source;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics ?? System.Array.Empty<CharacterDiagnostic>()));
    }
}

public sealed class CharacterSourceEditResult
{
    public CharacterPackageSource? Source { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool IsValid => Source != null && Diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);

    public CharacterSourceEditResult(CharacterPackageSource? source, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Source = source;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics ?? System.Array.Empty<CharacterDiagnostic>()));
    }

    internal static CharacterSourceEditResult Success(CharacterPackageSource source)
        => new(source, System.Array.Empty<CharacterDiagnostic>());
    internal static CharacterSourceEditResult Failure(string code, string path, string message)
        => new(null, new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message) });
}

public sealed record CharacterAssetCatalogBindingSnapshot(string SemanticId, string PoseTrackId);

public static class CharacterPackageSourceCodec
{
    private const ushort ManifestSchemaVersion = 1;
    private const ushort AuthoringSchemaVersion = 3;
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = true,
    };

    public static CharacterPackageSourceLoadResult Load(string packageJson, string characterJson)
    {
        var diagnostics = new DiagnosticBag();
        if (packageJson == null) diagnostics.Error("schema.missing", "manifest", "Package manifest JSON is null.");
        if (characterJson == null) diagnostics.Error("schema.missing", "character", "Character JSON is null.");
        if (diagnostics.HasErrors) return new CharacterPackageSourceLoadResult(null, diagnostics.ToList());
        try
        {
            using var manifestDoc = JsonDocument.Parse(packageJson!, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            using var characterDoc = JsonDocument.Parse(characterJson!, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var manifest = ParseManifest(manifestDoc.RootElement, diagnostics);
            var character = ParseCharacter(characterDoc.RootElement, diagnostics);
            return diagnostics.HasErrors
                ? new CharacterPackageSourceLoadResult(null, diagnostics.ToList())
                : new CharacterPackageSourceLoadResult(new CharacterPackageSource(manifest, character), diagnostics.ToList());
        }
        catch (JsonException ex)
        {
            diagnostics.Error("schema.invalid-json", "", ex.Message);
            return new CharacterPackageSourceLoadResult(null, diagnostics.ToList());
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is FormatException || ex is OverflowException)
        {
            diagnostics.Error("schema.invalid", "", ex.Message);
            return new CharacterPackageSourceLoadResult(null, diagnostics.ToList());
        }
    }

    public static string SerializeManifest(PackageManifestSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("manifestSchemaVersion", source.ManifestSchemaVersion);
            writer.WriteString("packageId", source.PackageId);
            writer.WriteString("version", source.Version);
            writer.WriteString("creator", source.Creator);
            writer.WriteString("license", source.License);
            writer.WriteString("attribution", source.Attribution);
            writer.WritePropertyName("dependencies"); writer.WriteStartArray();
            foreach (var dependency in source.Dependencies ?? System.Array.Empty<PackageDependencySource>())
            {
                writer.WriteStartObject();
                writer.WriteString("packageId", dependency.PackageId);
                writer.WriteString("version", dependency.Version);
                writer.WriteString("cookedHash", dependency.CookedHash);
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string SerializeCharacter(CharacterAuthoringDocument source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteCharacter(writer, source);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static CharacterPackageSource CreateMinimal(string packageId, string displayName, string creator, string license, string attribution)
    {
        var slots = new List<CharacterSlotSource>(CanonicalSlotProjection.All.Count);
        foreach (var address in CanonicalSlotProjection.All)
        {
            string id = address.Id;
            string suffix = address.InputLabel;
            slots.Add(new CharacterSlotSource(id, suffix, "", "icon." + suffix.ToLowerInvariant(), AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, new CharacterTimelineSource(System.Array.Empty<CharacterStageSource>())));
        }
        return new CharacterPackageSource(
            new PackageManifestSource(ManifestSchemaVersion, packageId, "0.0.0-dev", creator, license, attribution, System.Array.Empty<PackageDependencySource>()),
            new CharacterAuthoringDocument(
                AuthoringSchemaVersion, displayName, 0f,
                new CharacterMovementSource(
                    0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                    0, 0, 0f, 0f, 0f, 0f, 0, 0, 0, 0),
                new CharacterPresentationSource("", "", "", "", "", "", "", "", 0f),
                0f, 0f, 0f, 0f, 1f,
                new CharacterCaptureGeometrySource(0.9f, 0.7f, 1.2f, 0.6f,
                    new CaptureAnchorSource(0f, 0.6f, 0.3f),
                    new CaptureAnchorSource(0f, 0.6f, 0.45f)),
                System.Array.Empty<HurtboxCapsuleSource>(),
                System.Array.Empty<HurtboxBoneSource>(),
                System.Array.Empty<string>(),
                System.Array.Empty<string>(),
                System.Array.Empty<CapabilityRequirementSource>(),
                slots,
                System.Array.Empty<CharacterAliasSource>()));
    }
    public static CharacterPackageSource CreateAuthoringReady(string packageId, string displayName, string creator, string license, string attribution)
    {
        CharacterPackageSource minimal = CreateMinimal(packageId, displayName, creator, license, attribution);
        var slots = minimal.Character.Slots.Select(slot => slot with
        {
            Timeline = new CharacterTimelineSource(new[]
            {
                new CharacterStageSource(
                    30, 4, 0, 0, 0,
                    new[] { "anim.move." + slot.Id.ToLowerInvariant().Replace('.', '-') },
                    System.Array.Empty<CharacterTimelineOperationSource>())
            })
        }).ToArray();
        return minimal with
        {
            Character = minimal.Character with
            {
                Weight = 100f,
                Movement = new CharacterMovementSource(
                    14f, 20f, 12f, 20f, 11f, 7.5f, 16f, 3.2f, 12f, 7.2f, 0.8f, 0.85f, 36f, 0f,
                    20, 48, 8f, 6f, 48f, 58f, 2, 4, 35, 10),
                Presentation = new CharacterPresentationSource(
                    "anim.idle", "anim.run", "anim.dash", "anim.jump", "anim.fall",
                    "anim.hit-light", "anim.hit-medium", "anim.hit-hard", 0f, "", 1f, 0.85f),
                CapsuleRadius = 0.35f,
                CapsuleHeight = 1.7f,
                HipHeight = 0.82f,
                HurtboxRadius = 1f,
                CaptureGeometry = new CharacterCaptureGeometrySource(0.9f, 0.7f, 1.2f, 0.6f,
                    new CaptureAnchorSource(0f, 0.6f, 0.3f),
                    new CaptureAnchorSource(0f, 0.6f, 0.45f)),
                HurtboxCapsules = new[]
                {
                    new HurtboxCapsuleSource(0f, 0.2f, 0f, 0f, 0.9f, 0f, 0.3f),
                    new HurtboxCapsuleSource(0f, 1.2f, 0f, 0f, 1.2f, 0f, 0.22f),
                    new HurtboxCapsuleSource(0.15f, 0f, 0f, 0.15f, -0.8f, 0f, 0.16f),
                    new HurtboxCapsuleSource(-0.15f, 0f, 0f, -0.15f, -0.8f, 0f, 0.16f),
                },
                HurtboxBoneDefs = new[]
                {
                    new HurtboxBoneSource("bone.head", 0f, 0f, 0f, 0.22f),
                    new HurtboxBoneSource("bone.spine2", 0f, 0f, 0f, 0.26f),
                    new HurtboxBoneSource("bone.hips", 0f, 0f, 0f, 0.26f),
                    new HurtboxBoneSource("bone.right-hand", 0f, 0f, 0f, 0.12f),
                    new HurtboxBoneSource("bone.left-hand", 0f, 0f, 0f, 0.12f),
                    new HurtboxBoneSource("bone.right-foot", 0f, 0f, 0f, 0.16f),
                    new HurtboxBoneSource("bone.left-foot", 0f, 0f, 0f, 0.16f),
                },
                Slots = slots,
            }
        };
    }

    public static CharacterSourceEditResult ReplaceGeneral(CharacterPackageSource source, string displayName, float weight, float capsuleRadius, float capsuleHeight, float hipHeight, float hurtboxRadius)
        => source == null
            ? CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source is required.")
            : CharacterSourceEditResult.Success(source with
            {
                Character = source.Character with
                {
                    DisplayName = displayName,
                    Weight = weight,
                    CapsuleRadius = capsuleRadius,
                    CapsuleHeight = capsuleHeight,
                    HipHeight = hipHeight,
                    HurtboxRadius = hurtboxRadius
                }
            });
    public static CharacterSourceEditResult ReplaceMovement(CharacterPackageSource source, CharacterMovementSource value)
        => source == null || value == null ? CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source and movement are required.") : CharacterSourceEditResult.Success(source with { Character = source.Character with { Movement = value } });
    public static CharacterSourceEditResult ReplaceCaptureGeometry(
        CharacterPackageSource source, CharacterCaptureGeometrySource value)
        => source == null || value == null
            ? CharacterSourceEditResult.Failure("edit.source.missing", "captureGeometry", "Source and capture geometry are required.")
            : CharacterSourceEditResult.Success(source with
            {
                Character = source.Character with { CaptureGeometry = value }
            });
    public static CharacterSourceEditResult ReplacePresentation(CharacterPackageSource source, CharacterPresentationSource value)
        => source == null || value == null ? CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source and presentation are required.") : CharacterSourceEditResult.Success(source with { Character = source.Character with { Presentation = value } });
    public static CharacterSourceEditResult ReplaceSlot(CharacterPackageSource source, int slotIndex, CharacterSlotSource value)
    {
        if (source == null || value == null) return CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source and slot are required.");
        if (slotIndex < 0 || slotIndex >= source.Character.Slots.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", $"character.slots[{slotIndex}]", "Slot index is out of range.");
        var slots = source.Character.Slots.ToList(); slots[slotIndex] = value;
        return CharacterSourceEditResult.Success(source with { Character = source.Character with { Slots = slots } });
    }
    public static CharacterSourceEditResult ReplaceStage(CharacterPackageSource source, int slotIndex, int stageIndex, CharacterStageSource value)
    {
        if (source == null || value == null) return CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source and stage are required.");
        if (!TryTimeline(source, slotIndex, out var slot, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Slot index is out of range.");
        if (stageIndex < 0 || stageIndex >= slot.Timeline.Stages.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path + ".timeline.stages[" + stageIndex + "]", "Stage index is out of range.");
        var stages = slot.Timeline.Stages.ToList(); stages[stageIndex] = value;
        return ReplaceSlot(source, slotIndex, slot with { Timeline = new CharacterTimelineSource(stages) });
    }
    public static CharacterSourceEditResult AddStage(CharacterPackageSource source, int slotIndex, CharacterStageSource value)
    {
        if (source == null || value == null) return CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source and stage are required.");
        if (!TryTimeline(source, slotIndex, out var slot, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Slot index is out of range.");
        var stages = slot.Timeline.Stages.ToList(); stages.Add(value);
        return ReplaceSlot(source, slotIndex, slot with { Timeline = new CharacterTimelineSource(stages) });
    }
    public static CharacterSourceEditResult RemoveStage(CharacterPackageSource source, int slotIndex, int stageIndex)
    {
        if (source == null) return CharacterSourceEditResult.Failure("edit.source.missing", "source", "Source is required.");
        if (!TryTimeline(source, slotIndex, out var slot, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Slot index is out of range.");
        if (stageIndex < 0 || stageIndex >= slot.Timeline.Stages.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path + ".timeline.stages[" + stageIndex + "]", "Stage index is out of range.");
        var stages = slot.Timeline.Stages.ToList(); stages.RemoveAt(stageIndex);
        return ReplaceSlot(source, slotIndex, slot with { Timeline = new CharacterTimelineSource(stages) });
    }
    public static CharacterSourceEditResult ReplaceOperation(CharacterPackageSource source, int slotIndex, int stageIndex, int operationIndex, CharacterTimelineOperationSource value)
    {
        if (value == null) return CharacterSourceEditResult.Failure("edit.source.missing", "operation", "Operation is required.");
        if (!TryStage(source, slotIndex, stageIndex, out var slot, out var stage, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path + ".operations[" + operationIndex + "]", "Operation index is out of range.");
        var operations = stage.Operations.ToList(); operations[operationIndex] = value;
        return ReplaceStage(source, slotIndex, stageIndex, stage with { Operations = operations });
    }
    public static CharacterSourceEditResult ReplaceOperationTick(CharacterPackageSource source, int slotIndex, int stageIndex, int operationIndex, int tick)
    {
        if (!TryStage(source, slotIndex, stageIndex, out _, out var stage, out var path))
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        var operationPath = path + ".operations[" + operationIndex + "]";
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count)
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", operationPath, "Operation index is out of range.");
        if (tick < 0 || tick >= stage.DurationTicks)
            return CharacterSourceEditResult.Failure("edit.tick.out-of-range", operationPath + ".tick", "Operation tick must be inside the stage.");
        var operation = stage.Operations[operationIndex];
        if (operation is GravityWindowOperationSource gravity &&
            (long)tick + gravity.DurationTicks > stage.DurationTicks)
            return CharacterSourceEditResult.Failure("edit.tick.out-of-range", operationPath + ".tick", "Gravity window must end inside the stage.");
        return ReplaceOperation(source, slotIndex, stageIndex, operationIndex, operation with { Tick = (ushort)tick });
    }

    public static CharacterSourceEditResult ReplaceHitboxDuration(CharacterPackageSource source, int slotIndex, int stageIndex, int operationIndex, int durationTicks)
    {
        if (!TryStage(source, slotIndex, stageIndex, out _, out var stage, out var path))
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        var operationPath = path + ".operations[" + operationIndex + "]";
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count)
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", operationPath, "Operation index is out of range.");
        if (stage.Operations[operationIndex] is not SpawnHitboxOperationSource hitbox)
            return CharacterSourceEditResult.Failure("edit.operation.not-hitbox", operationPath, "Selected operation is not a hitbox.");
        if (durationTicks <= 0 || hitbox.Tick < 0 || (long)hitbox.Tick + durationTicks > stage.DurationTicks)
            return CharacterSourceEditResult.Failure("edit.duration.out-of-range", operationPath + ".hitbox.durationTicks", "Hitbox duration must be positive and end inside the stage.");
        return ReplaceOperation(source, slotIndex, stageIndex, operationIndex, hitbox with { Hitbox = hitbox.Hitbox with { DurationTicks = (ushort)durationTicks } });
    }
    public static CharacterSourceEditResult ReplacePresentationPlacement(
        CharacterPackageSource source,
        int slotIndex,
        int stageIndex,
        int operationIndex,
        PresentationPlacement placement)
    {
        if (placement == null)
            return CharacterSourceEditResult.Failure("edit.source.missing", "presentation.placement", "Presentation placement is required.");
        if (!TryStage(source, slotIndex, stageIndex, out _, out var stage, out var path))
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        var operationPath = path + ".operations[" + operationIndex + "]";
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count)
            return CharacterSourceEditResult.Failure("edit.index.out-of-range", operationPath, "Operation index is out of range.");
        if (stage.Operations[operationIndex] is not EmitPresentationOperationSource presentation)
            return CharacterSourceEditResult.Failure("edit.operation.not-presentation", operationPath, "Selected operation is not a presentation operation.");
        if (placement.DurationTicks == 0 ||
            (long)presentation.Tick + placement.DurationTicks > stage.DurationTicks)
            return CharacterSourceEditResult.Failure("edit.duration.out-of-range", operationPath + ".placement.durationTicks", "Presentation duration must be positive and end inside the stage.");
        return ReplaceOperation(source, slotIndex, stageIndex, operationIndex, presentation with { Placement = placement });
    }


    public static CharacterSourceEditResult AddOperation(CharacterPackageSource source, int slotIndex, int stageIndex, CharacterTimelineOperationSource value)
    {
        if (value == null) return CharacterSourceEditResult.Failure("edit.source.missing", "operation", "Operation is required.");
        if (!TryStage(source, slotIndex, stageIndex, out var slot, out var stage, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        var operations = stage.Operations.ToList(); operations.Add(value);
        return ReplaceStage(source, slotIndex, stageIndex, stage with { Operations = operations });
    }
    public static CharacterSourceEditResult RemoveOperation(CharacterPackageSource source, int slotIndex, int stageIndex, int operationIndex)
    {
        if (!TryStage(source, slotIndex, stageIndex, out var slot, out var stage, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path + ".operations[" + operationIndex + "]", "Operation index is out of range.");
        var operations = stage.Operations.ToList(); operations.RemoveAt(operationIndex);
        return ReplaceStage(source, slotIndex, stageIndex, stage with { Operations = operations });
    }
    public static CharacterSourceEditResult MoveOperation(CharacterPackageSource source, int slotIndex, int stageIndex, int operationIndex, int destinationIndex)
    {
        if (!TryStage(source, slotIndex, stageIndex, out var slot, out var stage, out var path)) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path, "Stage index is out of range.");
        if (operationIndex < 0 || operationIndex >= stage.Operations.Count || destinationIndex < 0 || destinationIndex >= stage.Operations.Count) return CharacterSourceEditResult.Failure("edit.index.out-of-range", path + ".operations", "Operation index is out of range.");
        var operations = stage.Operations.ToList(); var value = operations[operationIndex]; operations.RemoveAt(operationIndex); operations.Insert(destinationIndex, value);
        return ReplaceStage(source, slotIndex, stageIndex, stage with { Operations = operations });
    }

    public static CharacterSourceEditResult RenameSemanticId(CharacterPackageSource source, string oldId, string newId, IReadOnlyList<CharacterAssetCatalogBindingSnapshot> catalog)
    {
        var error = ValidateRename(source, oldId, newId, catalog);
        if (error != null) return error;
        var c = source.Character;
        var p = c.Presentation;
        p = p with
        {
            Idle = Rename(p.Idle), Run = Rename(p.Run), Dash = Rename(p.Dash),
            Jump = Rename(p.Jump), Fall = Rename(p.Fall),
            HitSmall = Rename(p.HitSmall), HitMedium = Rename(p.HitMedium), HitHard = Rename(p.HitHard),
            Tumble = Rename(p.Tumble), Crouch = Rename(p.Crouch), Slide = Rename(p.Slide),
            Shield = Rename(p.Shield), Grab = Rename(p.Grab), Grabbed = Rename(p.Grabbed),
            ThrowForward = Rename(p.ThrowForward), AirDodge = Rename(p.AirDodge)
        };
        var presentations = c.PresentationIds.Select(Rename).ToArray();
        var slots = c.Slots.Select(slot =>
        {
            string hitPresentationId = Rename(slot.HitPresentationId ?? "");
            return slot with
            {
                HitPresentationId = string.IsNullOrEmpty(hitPresentationId) ? null : hitPresentationId,
                Timeline = new CharacterTimelineSource(slot.Timeline.Stages.Select(stage => stage with
                {
                    AnimationIds = stage.AnimationIds.Select(Rename).ToArray(),
                    Operations = stage.Operations.Select(op =>
                        op is EmitPresentationOperationSource emit && emit.PresentationId == oldId ? emit with { PresentationId = newId }
                        : op is StartCapabilityOperationSource capability ? capability with { Parameters = RenameCapabilityPresentation(CloneParameters(capability.Parameters), oldId, newId) }
                        : CloneOperation(op)).ToArray()
                }).ToArray())
            };
        }).ToArray();
        return CharacterSourceEditResult.Success(source with { Character = c with { Presentation = p, PresentationIds = presentations, Slots = slots } });
        string Rename(string value) => value == oldId ? newId : value;
    }

    private static CharacterSourceEditResult? ValidateRename(CharacterPackageSource source, string oldId, string newId, IReadOnlyList<CharacterAssetCatalogBindingSnapshot> catalog)
    {
        if (source == null) return CharacterSourceEditResult.Failure("rename.source.missing", "source", "Source is required.");
        if (!IsValidId(oldId) || !IsValidId(newId)) return CharacterSourceEditResult.Failure("id.invalid", "rename", "Semantic IDs must be lowercase ASCII IDs.");
        var sourceIds = new List<string>
        {
            source.Character.Presentation.Idle, source.Character.Presentation.Run,
            source.Character.Presentation.Dash, source.Character.Presentation.Jump,
            source.Character.Presentation.Fall, source.Character.Presentation.HitSmall,
            source.Character.Presentation.HitMedium, source.Character.Presentation.HitHard,
            source.Character.Presentation.Tumble, source.Character.Presentation.Crouch,
            source.Character.Presentation.Slide, source.Character.Presentation.Shield,
            source.Character.Presentation.Grab, source.Character.Presentation.Grabbed,
            source.Character.Presentation.ThrowForward, source.Character.Presentation.AirDodge
        };
        sourceIds.AddRange(source.Character.PresentationIds);
        sourceIds.AddRange(source.Character.Slots.Select(x => x.HitPresentationId ?? "").Where(x => !string.IsNullOrEmpty(x)));
        sourceIds.AddRange(source.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.AnimationIds));
        sourceIds.AddRange(source.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<EmitPresentationOperationSource>().Select(x => x.PresentationId));
        sourceIds.AddRange(source.Character.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<StartCapabilityOperationSource>().Select(x => x.Parameters switch { MankiRoundBombCapabilityParameters p => p.ExplosionPresentationId, MankiJetpackBoostCapabilityParameters p => p.ExplosionPresentationId, MankiBazookaCapabilityParameters p => p.ExplosionPresentationId, _ => "" }).Where(x => !string.IsNullOrEmpty(x)));
        if (!sourceIds.Contains(oldId, StringComparer.Ordinal)) return CharacterSourceEditResult.Failure("rename.old-missing", "rename.oldId", "Old semantic ID is not referenced by the source document.");
        if (oldId != newId && sourceIds.Contains(newId, StringComparer.Ordinal)) return CharacterSourceEditResult.Failure("rename.collision", "rename.newId", "New semantic ID collides with an existing source reference.");
        var bindings = catalog ?? System.Array.Empty<CharacterAssetCatalogBindingSnapshot>();
        if (!bindings.Any(x => x.SemanticId == oldId || x.PoseTrackId == oldId)) return CharacterSourceEditResult.Failure("rename.old-missing", "catalog", "Old semantic ID is not present in the asset catalog.");
        if (oldId != newId && bindings.Any(x => (x.SemanticId == newId || x.PoseTrackId == newId) && x.SemanticId != oldId)) return CharacterSourceEditResult.Failure("rename.collision", "catalog", "New semantic ID collides with an existing catalog binding.");
        return null;
    }

    private static bool IsValidId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64 && value[0] >= 'a' && value[0] <= 'z' && value.All(x => (x >= 'a' && x <= 'z') || (x >= '0' && x <= '9') || x == '.' || x == '-');
    private static bool TryTimeline(CharacterPackageSource source, int slotIndex, out CharacterSlotSource slot, out string path) { slot = null!; path = $"character.slots[{slotIndex}]"; if (slotIndex < 0 || slotIndex >= (source?.Character.Slots.Count ?? 0)) return false; slot = source.Character.Slots[slotIndex]; return true; }
    private static bool TryStage(CharacterPackageSource source, int slotIndex, int stageIndex, out CharacterSlotSource slot, out CharacterStageSource stage, out string path) { stage = null!; if (!TryTimeline(source, slotIndex, out slot, out path)) return false; path += ".timeline.stages[" + stageIndex + "]"; if (stageIndex < 0 || stageIndex >= slot.Timeline.Stages.Count) return false; stage = slot.Timeline.Stages[stageIndex]; return true; }
    private static CharacterTimelineOperationSource CloneOperation(CharacterTimelineOperationSource op)
        => op switch
        {
            SetVelocityOperationSource x => x with { },
            GravityWindowOperationSource x => x with { },
            ArmorWindowOperationSource x => x with { },
            ForwardLungeOperationSource x => x with { },
            SpawnHitboxOperationSource x => x with { Hitbox = x.Hitbox with { } },
            SpawnProjectileOperationSource x => x with { Projectile = x.Projectile with { } },
            SetAimStateOperationSource x => x with { },
            StartCapabilityOperationSource x => x with { Parameters = CloneParameters(x.Parameters) },
            EmitPresentationOperationSource x => x with { },
            CompleteTimelineOperationSource x => x with { },
            StartupAimCorrectionOperationSource x => x with { },
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
            MankiAerosolInfernoCapabilityParameters x => x with { ChargeTiers = x.ChargeTiers?.Select(tier => tier with { }).ToArray() },
            _ => throw new InvalidDataException("Unknown capability parameters.")
        };
    private static TypedCapabilityParameters RenameCapabilityPresentation(TypedCapabilityParameters p, string oldId, string newId)
        => p switch
        {
            MankiRoundBombCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
            MankiJetpackBoostCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
            MankiBazookaCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
            _ => p,
        };

    private static void WriteCharacter(Utf8JsonWriter w, CharacterAuthoringDocument x)
    {
        w.WriteStartObject();
        w.WriteNumber("authoringSchemaVersion", x.AuthoringSchemaVersion);
        w.WriteString("displayName", x.DisplayName);
        Number(w, "weight", x.Weight);
        WriteMovement(w, x.Movement);
        WritePresentation(w, x.Presentation);
        Number(w, "capsuleRadius", x.CapsuleRadius);
        Number(w, "capsuleHeight", x.CapsuleHeight);
        Number(w, "hipHeight", x.HipHeight);
        Number(w, "hurtboxRadius", x.HurtboxRadius);
        Number(w, "shieldRadius", x.ShieldRadius);
        WriteCaptureGeometry(w, x.CaptureGeometry);
        w.WritePropertyName("hurtboxCapsules"); w.WriteStartArray(); foreach (var h in x.HurtboxCapsules ?? System.Array.Empty<HurtboxCapsuleSource>()) { w.WriteStartObject(); Number(w,"startX",h.StartX); Number(w,"startY",h.StartY); Number(w,"startZ",h.StartZ); Number(w,"endX",h.EndX); Number(w,"endY",h.EndY); Number(w,"endZ",h.EndZ); Number(w,"radius",h.Radius); w.WriteEndObject(); } w.WriteEndArray();
        w.WritePropertyName("hurtboxBoneDefs"); w.WriteStartArray(); foreach (var h in x.HurtboxBoneDefs ?? System.Array.Empty<HurtboxBoneSource>()) { w.WriteStartObject(); w.WriteString("boneId",h.BoneId); Number(w,"offsetX",h.OffsetX); Number(w,"offsetY",h.OffsetY); Number(w,"offsetZ",h.OffsetZ); Number(w,"radius",h.Radius); w.WriteEndObject(); } w.WriteEndArray();
        WriteStringArray(w, "attachmentBoneIds", x.AttachmentBoneIds); WriteStringArray(w, "presentationIds", x.PresentationIds);
        w.WritePropertyName("capabilityRequirements"); w.WriteStartArray(); foreach (var c in x.CapabilityRequirements ?? System.Array.Empty<CapabilityRequirementSource>()) { w.WriteStartObject(); w.WriteString("capabilityId", c.CapabilityId); w.WriteString("capabilityVersion", c.CapabilityVersion); w.WriteEndObject(); } w.WriteEndArray();
        w.WritePropertyName("slots"); w.WriteStartArray(); foreach (var slot in x.Slots ?? System.Array.Empty<CharacterSlotSource>()) WriteSlot(w, slot); w.WriteEndArray();
        w.WritePropertyName("aliases"); w.WriteStartArray(); foreach (var a in x.Aliases ?? System.Array.Empty<CharacterAliasSource>()) { w.WriteStartObject(); w.WriteString("from",a.From); w.WriteString("to",a.To); w.WriteEndObject(); } w.WriteEndArray();
        w.WriteEndObject();
    }
    private static void WriteMovement(Utf8JsonWriter w, CharacterMovementSource x) { w.WritePropertyName("movement"); w.WriteStartObject(); Number(w,"runSpeed",x.RunSpeed); Number(w,"runAccelerationA",x.RunAccelerationA); Number(w,"runAccelerationB",x.RunAccelerationB); Number(w,"dashSpeed",x.DashSpeed); Number(w,"airDodgeSpeed",x.AirDodgeSpeed); Number(w,"airSpeedMax",x.AirSpeedMax); Number(w,"airAccelStick",x.AirAccelStick); Number(w,"airAccelBase",x.AirAccelBase); Number(w,"jumpForce",x.JumpForce); Number(w,"shortHopForce",x.ShortHopForce); Number(w,"airJumpVMultiplier",x.AirJumpVMultiplier); Number(w,"airJumpHMultiplier",x.AirJumpHMultiplier); Number(w,"gravity",x.Gravity); Number(w,"airFloatGravity",x.AirFloatGravity); w.WriteNumber("dashDurationTicks",x.DashDurationTicks); w.WriteNumber("dashCooldownTicks",x.DashCooldownTicks); Number(w,"groundFriction",x.GroundFriction); Number(w,"airFriction",x.AirFriction); Number(w,"maxFallSpeed",x.MaxFallSpeed); Number(w,"fastFallSpeed",x.FastFallSpeed); w.WriteNumber("maxJumps",x.MaxJumps); w.WriteNumber("jumpSquatTicks",x.JumpSquatTicks); w.WriteNumber("floatWindowTicks",x.FloatWindowTicks); w.WriteNumber("rushTicks",x.RushTicks); w.WriteEndObject(); }
    private static void WritePresentation(Utf8JsonWriter w, CharacterPresentationSource x)
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
        OptionalString(w, "tumble", x.Tumble);
        OptionalString(w, "crouch", x.Crouch);
        OptionalString(w, "slide", x.Slide);
        OptionalString(w, "shield", x.Shield);
        OptionalString(w, "grab", x.Grab);
        OptionalString(w, "grabbed", x.Grabbed);
        OptionalString(w, "throwForward", x.ThrowForward);
        OptionalString(w, "airDodge", x.AirDodge);
        Number(w, "landStartOffsetSeconds", x.LandStartOffsetSeconds);
        w.WriteString("modelResourcePath", x.ModelResourcePath);
        Number(w, "visualScale", x.VisualScale);
        Number(w, "hurtboxBoneScale", x.HurtboxBoneScale);
        Number(w, "modelYOffset", x.ModelYOffset);
        Number(w, "modelSoleOffset", x.ModelSoleOffset);
        w.WriteBoolean("autoModelYOffset", x.AutoModelYOffset);
        w.WriteEndObject();
    }

    private static void WriteCaptureGeometry(Utf8JsonWriter w, CharacterCaptureGeometrySource x)
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

    private static void WriteCaptureAnchor(Utf8JsonWriter w, string name, CaptureAnchorSource x)
    {
        w.WritePropertyName(name);
        w.WriteStartObject();
        Number(w, "x", x.X);
        Number(w, "y", x.Y);
        Number(w, "z", x.Z);
        w.WriteEndObject();
    }
    private static void WriteSlot(Utf8JsonWriter w, CharacterSlotSource x)
    {
        w.WriteStartObject();
        w.WriteString("id", x.Id);
        w.WriteString("name", x.Name);
        w.WriteString("description", x.Description);
        w.WriteString("iconId", x.IconId);
        w.WriteString("behavior", BehaviorText(x.Behavior));
        w.WriteString("aimMode", AimText(x.AimMode));
        w.WriteString("aimMovement", AimMovementText(x.AimMovement));
        if (x.AimAnimationId != null) w.WriteString("aimAnimationId", x.AimAnimationId);
        if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId);
        w.WriteNumber("cooldownTicks", x.CooldownTicks);
        w.WriteBoolean("isRecoveryMove", x.IsRecoveryMove);
        w.WriteBoolean("preserveMomentumOnStart", x.PreserveMomentumOnStart);
        w.WriteBoolean("allowSlideCarry", x.AllowSlideCarry);
        if (x.ChargePool != null)
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
    private static void WriteStage(Utf8JsonWriter w, CharacterStageSource x) { w.WriteStartObject(); w.WriteNumber("durationTicks",x.DurationTicks); w.WriteNumber("iasaTicks",x.IasaTicks); w.WriteNumber("landingLagTicks",x.LandingLagTicks); w.WriteNumber("autoCancelBeforeTicks",x.AutoCancelBeforeTicks); w.WriteNumber("autoCancelAfterTicks",x.AutoCancelAfterTicks); Number(w,"attackRange",x.AttackRange); Number(w,"warpRange",x.WarpRange); w.WriteBoolean("useTargetLock",x.UseTargetLock); w.WriteBoolean("rotateTowardTarget",x.RotateTowardTarget); Number(w,"trackingStrength",x.TrackingStrength); WriteStringArray(w,"animationIds",x.AnimationIds); w.WritePropertyName("operations"); w.WriteStartArray(); foreach(var op in x.Operations) WriteOperation(w,op); w.WriteEndArray(); w.WriteEndObject(); }
    private static void WriteOperation(Utf8JsonWriter w, CharacterTimelineOperationSource x)
    {
        w.WriteStartObject();
        w.WriteString("kind", OperationKind(x));
        w.WriteNumber("tick", x.Tick);
        w.WriteString("unit", UnitText(x.Unit));
        switch (x)
        {
            case SetVelocityOperationSource v:
                w.WriteString("velocityMode", VelocityText(v.VelocityMode));
                Number(w, "x", v.X);
                Number(w, "y", v.Y);
                Number(w, "z", v.Z);
                break;
            case ForwardLungeOperationSource lunge:
                Number(w, "speed", lunge.Speed);
                w.WriteNumber("durationTicks", lunge.DurationTicks);
                if (lunge.StopInAttackRange) w.WriteBoolean("stopInAttackRange", true);
                break;
            case GravityWindowOperationSource gravity:
                Number(w, "gravityScale", gravity.GravityScale);
                w.WriteNumber("durationTicks", gravity.DurationTicks);
                break;
            case ArmorWindowOperationSource armorWindow:
                w.WriteNumber("durationTicks", armorWindow.DurationTicks);
                break;
            case SpawnHitboxOperationSource h:
                WriteHitbox(w, h.Hitbox);
                break;
            case SpawnProjectileOperationSource p:
                WriteProjectile(w, p.Projectile);
                break;
            case SetAimStateOperationSource a:
                w.WriteString("aimState", AimText(a.AimState));
                break;
            case StartCapabilityOperationSource c:
                w.WriteString("capabilityId", c.CapabilityId);
                w.WriteString("capabilityVersion", c.CapabilityVersion);
                w.WritePropertyName("parameters");
                WriteParameters(w, c.Parameters);
                break;
            case EmitPresentationOperationSource e:
                w.WriteString("presentationId", e.PresentationId);
                WritePlacement(w, e.Placement);
                break;
            case StartupAimCorrectionOperationSource correction:
                w.WriteNumber("endTick", correction.EndTick);
                Number(w, "acquisitionRange", correction.AcquisitionRange);
                Number(w, "acquisitionHalfAngleDegrees", correction.AcquisitionHalfAngleDegrees);
                Number(w, "maxYawDegrees", correction.MaxYawDegrees);
                Number(w, "maxPitchDegrees", correction.MaxPitchDegrees);
                Number(w, "yawDegreesPerSecond", correction.YawDegreesPerSecond);
                Number(w, "pitchDegreesPerSecond", correction.PitchDegreesPerSecond);
                break;
        }
        w.WriteEndObject();
    }
    private static void WritePlacement(Utf8JsonWriter w, PresentationPlacement x)
    {
        x ??= new PresentationPlacement();
        w.WritePropertyName("placement");
        w.WriteStartObject();
        w.WriteString("attachmentMode", x.AttachmentMode == AuthoringPresentationAttachmentMode.Bone ? "bone" : "world");
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
    private static void WriteHitbox(Utf8JsonWriter w, HitboxSource x) { w.WritePropertyName("hitbox"); w.WriteStartObject(); w.WriteString("shape",ShapeText(x.Shape)); Number(w,"radius",x.Radius); Number(w,"offsetX",x.OffsetX); Number(w,"offsetY",x.OffsetY); Number(w,"offsetZ",x.OffsetZ); Number(w,"endOffsetX",x.EndOffsetX); Number(w,"endOffsetY",x.EndOffsetY); Number(w,"endOffsetZ",x.EndOffsetZ); if(x.StartBoneId == null) w.WriteNull("startBoneId"); else w.WriteString("startBoneId",x.StartBoneId); if(x.EndBoneId == null) w.WriteNull("endBoneId"); else w.WriteString("endBoneId",x.EndBoneId); Number(w,"damage",x.Damage); Number(w,"angle",x.Angle); Number(w,"baseKnockback",x.BaseKnockback); Number(w,"knockbackGrowth",x.KnockbackGrowth); w.WriteNumber("stunTicks",x.StunTicks); w.WriteNumber("durationTicks",x.DurationTicks); w.WriteBoolean("interruptible",x.Interruptible); w.WriteNumber("hitGroup",x.HitGroup); w.WriteString("knockbackDirection",KnockbackDirectionText(x.KnockbackDirection)); if (x.FixedHitstunTicks > 0) w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks); w.WriteEndObject(); }
    private static void WriteHitboxNamed(Utf8JsonWriter w, HitboxSource x, string name)
    {
        w.WritePropertyName(name); w.WriteStartObject(); w.WriteString("shape", ShapeText(x.Shape));
        Number(w, "radius", x.Radius); Number(w, "offsetX", x.OffsetX); Number(w, "offsetY", x.OffsetY);
        Number(w, "offsetZ", x.OffsetZ); Number(w, "endOffsetX", x.EndOffsetX);
        Number(w, "endOffsetY", x.EndOffsetY); Number(w, "endOffsetZ", x.EndOffsetZ);
        if (x.StartBoneId == null) w.WriteNull("startBoneId"); else w.WriteString("startBoneId", x.StartBoneId);
        if (x.EndBoneId == null) w.WriteNull("endBoneId"); else w.WriteString("endBoneId", x.EndBoneId);
        Number(w, "damage", x.Damage); Number(w, "angle", x.Angle);
        Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth);
        w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("durationTicks", x.DurationTicks);
        w.WriteBoolean("interruptible", x.Interruptible); w.WriteNumber("hitGroup", x.HitGroup);
        w.WriteString("knockbackDirection", KnockbackDirectionText(x.KnockbackDirection));
        if (x.FixedHitstunTicks > 0) w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks);
        w.WriteEndObject();
    }
    private static void WriteProjectile(Utf8JsonWriter w, ProjectileSource x) { w.WritePropertyName("projectile"); w.WriteStartObject(); Number(w,"launchOffsetX",x.LaunchOffsetX); Number(w,"launchOffsetY",x.LaunchOffsetY); Number(w,"launchOffsetZ",x.LaunchOffsetZ); Number(w,"speed",x.Speed); Number(w,"gravity",x.Gravity); Number(w,"radius",x.Radius); Number(w,"damage",x.Damage); Number(w,"angle",x.Angle); Number(w,"baseKnockback",x.BaseKnockback); Number(w,"knockbackGrowth",x.KnockbackGrowth); w.WriteNumber("stunTicks",x.StunTicks); w.WriteNumber("maxFlightTicks",x.MaxFlightTicks); Number(w,"yawOffsetDegrees",x.YawOffsetDegrees); w.WriteEndObject(); }
    private static void WriteParameters(Utf8JsonWriter w, TypedCapabilityParameters x)
    {
        w.WriteStartObject();
        switch (x)
        {
            case ChargedProjectileCapabilityParameters p:
                w.WriteNumber("maxChargeTicks", p.MaxChargeTicks); w.WriteNumber("tier2Ticks", p.Tier2Ticks);
                w.WriteNumber("tier3Ticks", p.Tier3Ticks); w.WriteNumber("fireTriggerTick", p.FireTriggerTick);
                Number(w, "tier2Radius", p.Tier2Radius); Number(w, "tier3Radius", p.Tier3Radius);
                WriteProjectile(w, p.Projectile); break;
            case RisingDragonCapabilityParameters p: Number(w, "riseSpeed", p.RiseSpeed); w.WriteNumber("riseTicks", p.RiseTicks); w.WriteNumber("riseDelay", p.RiseDelay); break;
            case CycloneKickCapabilityParameters p: w.WriteNumber("forwardSpeed", p.ForwardSpeed); w.WriteNumber("windupTicks", p.WindupTicks); w.WriteNumber("hitboxEndTick", p.HitboxEndTick); w.WriteNumber("durationTicks", p.DurationTicks); Number(w, "bodyRadius", p.BodyRadius); Number(w, "sideRadius", p.SideRadius); Number(w, "sideOffset", p.SideOffset); Number(w, "damage", p.Damage); Number(w, "knockbackAngle", p.KnockbackAngle); Number(w, "knockbackBase", p.KnockbackBase); Number(w, "knockbackGrowth", p.KnockbackGrowth); w.WriteNumber("stunTicks", p.StunTicks); Number(w, "bodyY", p.BodyY); Number(w, "sideY", p.SideY); break;
            case ChargedDirectionalDashCapabilityParameters p:
                w.WriteNumber("maxChargeTicks", p.MaxChargeTicks); w.WriteNumber("tier2Ticks", p.Tier2Ticks);
                w.WriteNumber("tier3Ticks", p.Tier3Ticks); Number(w, "minDistance", p.MinDistance);
                Number(w, "maxDistance", p.MaxDistance); Number(w, "dashSpeed", p.DashSpeed);
                w.WriteNumber("finisherLeadTicks", p.FinisherLeadTicks); w.WriteNumber("finisherSeekTick", p.FinisherSeekTick);
                w.WriteNumber("recoveryTicks", p.RecoveryTicks); Number(w, "tier2Damage", p.Tier2Damage);
                Number(w, "tier3Damage", p.Tier3Damage); WriteHitboxNamed(w, p.TraversalHitbox, "traversalHitbox");
                WriteHitboxNamed(w, p.FinisherHitbox, "finisherHitbox"); break;
            case WibouRisingSlashCapabilityParameters p: Number(w, "riseSpeed", p.RiseSpeed); w.WriteNumber("riseTicks", p.RiseTicks); Number(w, "homingRange", p.HomingRange); Number(w, "homingSpeed", p.HomingSpeed); break;
            case WibouBladeFlurryCapabilityParameters p: Number(w, "forwardSpeed", p.ForwardSpeed); w.WriteNumber("moveTicks", p.MoveTicks); break;
            case TargetedLeapCapabilityParameters p:
                w.WriteNumber("maxAimTicks", p.MaxAimTicks);
                w.WriteNumber("maxFlightTicks", p.MaxFlightTicks);
                Number(w, "minRange", p.MinRange);
                Number(w, "maxRange", p.MaxRange);
                Number(w, "launchVerticalSpeed", p.LaunchVerticalSpeed);
                w.WriteNumber("landingSeekTick", p.LandingSeekTick);
                w.WriteNumber("recoveryTicks", p.RecoveryTicks);
                WriteHitbox(w, p.Hitbox);
                OptionalString(w, "launchPresentationId", p.LaunchPresentationId);
                OptionalString(w, "landingPresentationId", p.LandingPresentationId);
                break;
            case MankiRoundBombCapabilityParameters p: w.WriteNumber("throwTriggerTick", p.ThrowTriggerTick); Number(w, "maxRange", p.MaxRange); Number(w, "launchAngle", p.LaunchAngle); Number(w, "gravity", p.Gravity); Number(w, "hitboxRadius", p.HitboxRadius); Number(w, "damage", p.Damage); w.WriteNumber("stunTicks", p.StunTicks); w.WriteNumber("maxFlightTicks", p.MaxFlightTicks); Number(w, "kbAngle", p.KbAngle); Number(w, "explosionDamage", p.ExplosionDamage); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); Number(w, "explosionKbAngle", p.ExplosionKbAngle); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); break;
            case MankiJetpackBoostCapabilityParameters p: w.WriteNumber("startupTicks", p.StartupTicks); Number(w, "verticalSpeed", p.VerticalSpeed); Number(w, "horizontalSpeed", p.HorizontalSpeed); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "explosionDamage", p.ExplosionDamage); Number(w, "explosionKbAngle", p.ExplosionKbAngle); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); break;
            case MankiBazookaCapabilityParameters p: w.WriteNumber("fireTriggerTick", p.FireTriggerTick); Number(w, "projectileSpeed", p.ProjectileSpeed); Number(w, "hitboxRadius", p.HitboxRadius); Number(w, "damage", p.Damage); Number(w, "gravity", p.Gravity); w.WriteNumber("maxFlightTicks", p.MaxFlightTicks); w.WriteNumber("stunTicks", p.StunTicks); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "kbAngle", p.KbAngle); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); Number(w, "explosionKbAngle", p.ExplosionKbAngle); w.WriteNumber("castDuration", p.CastDuration); w.WriteNumber("recoveryDuration", p.RecoveryDuration); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); break;
            case MankiAerosolInfernoCapabilityParameters p: if (p.MinimumChargeTicks != 0) w.WriteNumber("minimumChargeTicks", p.MinimumChargeTicks); if (p.ChargeTiers != null) { w.WritePropertyName("chargeTiers"); w.WriteStartArray(); foreach (var tier in p.ChargeTiers) { w.WriteStartObject(); w.WriteNumber("chargeTicks", tier.ChargeTicks); Number(w, "damage", tier.Damage); Number(w, "reachScale", tier.ReachScale); w.WriteEndObject(); } w.WriteEndArray(); } w.WriteNumber("fireTriggerTick", p.FireTriggerTick); w.WriteNumber("fireDurationTicks", p.FireDurationTicks); w.WriteNumber("hitboxDurationTicks", p.HitboxDurationTicks); Number(w, "hitboxRadius", p.HitboxRadius); Number(w, "offsetY", p.OffsetY); Number(w, "offsetZ", p.OffsetZ); Number(w, "endOffsetZ", p.EndOffsetZ); Number(w, "damage", p.Damage); Number(w, "knockbackAngle", p.KnockbackAngle); Number(w, "knockbackBase", p.KnockbackBase); Number(w, "knockbackGrowth", p.KnockbackGrowth); w.WriteNumber("stunTicks", p.StunTicks); w.WriteNumber("hitGroup", p.HitGroup); break;
            default: throw new InvalidDataException("Unknown capability parameters.");
        }
        w.WriteEndObject();
    }
    private static void WriteStringArray(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (var value in values ?? System.Array.Empty<string>())
            w.WriteStringValue(value);
        w.WriteEndArray();
    }
    private static void Number(Utf8JsonWriter w, string name, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
        w.WritePropertyName(name);
        w.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture), skipInputValidation: true);
    }
    private static void OptionalString(Utf8JsonWriter w, string name, string value)
    {
        if (!string.IsNullOrEmpty(value)) w.WriteString(name, value);
    }
    private static string AimMovementText(AuthoringAimMovementMode value)=>value switch { AuthoringAimMovementMode.Fixed=>"fixed", AuthoringAimMovementMode.Mobile=>"mobile", _=>throw new InvalidDataException("Unknown aim movement mode.") };
    private static string BehaviorText(AuthoringAbilityBehavior value)=>value switch { AuthoringAbilityBehavior.MeleeCombo=>"meleeCombo", AuthoringAbilityBehavior.ChargeAttack=>"chargeAttack", AuthoringAbilityBehavior.AimedProjectile=>"aimedProjectile", AuthoringAbilityBehavior.Projectile=>"projectile", AuthoringAbilityBehavior.AirGroundProjectile=>"airGroundProjectile", AuthoringAbilityBehavior.SelfBuff=>"selfBuff", AuthoringAbilityBehavior.AreaDenial=>"areaDenial", AuthoringAbilityBehavior.DirectionalDash=>"directionalDash", _=>throw new InvalidDataException("Unknown behavior.") };
    private static string AimText(AuthoringAimMode value)=>value switch { AuthoringAimMode.None=>"none", AuthoringAimMode.GroundCursor=>"groundCursor", AuthoringAimMode.CameraForward3D=>"cameraForward3D", AuthoringAimMode.GroundVector=>"groundVector", _=>throw new InvalidDataException("Unknown aim mode.") };
    private static string ShapeText(AuthoringHitboxShape value)=>value switch { AuthoringHitboxShape.Sphere=>"sphere", AuthoringHitboxShape.Capsule=>"capsule", _=>throw new InvalidDataException("Unknown shape.") };
    private static string KnockbackDirectionText(AuthoringKnockbackDirection value)=>value switch { AuthoringKnockbackDirection.AwayFromOwner=>"awayFromOwner", AuthoringKnockbackDirection.TowardOwner=>"towardOwner", _=>throw new InvalidDataException("Unknown knockback direction.") };
    private static string VelocityText(AuthoringVelocityMode value)=>value switch { AuthoringVelocityMode.Absolute=>"absolute", AuthoringVelocityMode.Additive=>"additive", _=>throw new InvalidDataException("Unknown velocity mode.") };
    private static string UnitText(AuthoringUnit value)=>value switch { AuthoringUnit.Meters=>"meters", AuthoringUnit.MetersPerSecond=>"metersPerSecond", AuthoringUnit.MetersPerSecondSquared=>"metersPerSecondSquared", AuthoringUnit.Degrees=>"degrees", AuthoringUnit.Normalized=>"normalized", AuthoringUnit.Damage=>"damage", AuthoringUnit.Knockback=>"knockback", AuthoringUnit.Ticks=>"ticks", _=>throw new InvalidDataException("Unknown unit.") };
    private static string OperationKind(CharacterTimelineOperationSource value)=>value switch { SetVelocityOperationSource=>"setVelocity", ForwardLungeOperationSource=>"forwardLunge", GravityWindowOperationSource=>"gravityWindow", ArmorWindowOperationSource=>"armorWindow", SpawnHitboxOperationSource=>"spawnHitbox", SpawnProjectileOperationSource=>"spawnProjectile", SetAimStateOperationSource=>"setAimState", StartCapabilityOperationSource=>"startCapability", EmitPresentationOperationSource=>"emitPresentation", CompleteTimelineOperationSource=>"completeTimeline", StartupAimCorrectionOperationSource=>"startupAimCorrection", _=>throw new InvalidDataException("Unknown operation.") };

    private static PackageManifestSource ParseManifest(JsonElement root, DiagnosticBag d)
    {
        var p = ReadObject(root, "manifest", d, "manifestSchemaVersion", "packageId", "version", "creator", "license", "attribution", "dependencies", "schemaVersion", "id", "class");
        if (p.ContainsKey("schemaVersion")) d.Error("schema.unsupported", "manifest.schemaVersion", "Legacy schemaVersion is not accepted.");
        if (p.ContainsKey("id")) d.Error("source.identity-forbidden", "manifest.id", "Package identity belongs in package.json fields.");
        if (p.ContainsKey("class")) d.Error("source.class-forbidden", "manifest.class", "Character class is not part of the source contract.");
        var version = UShort(p, "manifestSchemaVersion", "manifest.manifestSchemaVersion", d);
        if (version != ManifestSchemaVersion) d.Error("schema.unsupported", "manifest.manifestSchemaVersion", "Only manifest schema version 1 is supported.");
        var packageId = String(p, "packageId", "manifest.packageId", d);
        var packageVersion = String(p, "version", "manifest.version", d);
        var creator = String(p, "creator", "manifest.creator", d);
        var license = String(p, "license", "manifest.license", d);
        var attribution = String(p, "attribution", "manifest.attribution", d);
        var deps = new List<PackageDependencySource>();
        if (p.TryGetValue("dependencies", out var dependencyElement))
        {
            if (dependencyElement.ValueKind != JsonValueKind.Array) d.Error("value.out-of-range", "manifest.dependencies", "Dependencies must be an array.");
            else
            {
                var i = 0;
                foreach (var depElement in dependencyElement.EnumerateArray())
                {
                    var dp = ReadObject(depElement, $"manifest.dependencies[{i}]", d, "packageId", "version", "cookedHash");
                    deps.Add(new PackageDependencySource(String(dp, "packageId", $"manifest.dependencies[{i}].packageId", d), String(dp, "version", $"manifest.dependencies[{i}].version", d), String(dp, "cookedHash", $"manifest.dependencies[{i}].cookedHash", d)));
                    i++;
                }
            }
        }
        else d.Error("schema.missing", "manifest.dependencies", "Required property is missing.");
        return new PackageManifestSource(version, packageId, packageVersion, creator, license, attribution, deps);
    }

    private static CharacterAuthoringDocument ParseCharacter(JsonElement root, DiagnosticBag d)
    {
        var p = ReadObject(root, "character", d, "authoringSchemaVersion", "displayName", "weight", "movement", "presentation", "capsuleRadius", "capsuleHeight", "hipHeight", "hurtboxRadius", "shieldRadius", "captureGeometry", "hurtboxCapsules", "hurtboxBoneDefs", "attachmentBoneIds", "presentationIds", "capabilityRequirements", "slots", "aliases", "schemaVersion", "id", "class");
        if (p.ContainsKey("schemaVersion")) d.Error("schema.unsupported", "character.schemaVersion", "Legacy schemaVersion is not accepted.");
        if (p.ContainsKey("id")) d.Error("source.identity-forbidden", "character.id", "Character identity belongs in package.json.");
        if (p.ContainsKey("class")) d.Error("source.class-forbidden", "character.class", "Character class is not part of the source contract.");
        var version = UShort(p, "authoringSchemaVersion", "character.authoringSchemaVersion", d);
        if (version != AuthoringSchemaVersion) d.Error("schema.unsupported", "character.authoringSchemaVersion", "Only authoring schema version 3 is supported.");
        var displayName = String(p, "displayName", "character.displayName", d);
        var weight = Float(p, "weight", "character.weight", d);
        var movement = ParseMovement(p, d);
        var presentation = ParsePresentation(p, d);
        var capsules = ParseCapsules(p, d);
        var bones = ParseBones(p, d);
        var attachmentBoneIds = ParseStringArray(p, "attachmentBoneIds", "character.attachmentBoneIds", d);
        var presentationIds = ParseStringArray(p, "presentationIds", "character.presentationIds", d);
        var capabilities = ParseCapabilities(p, d);
        var slots = ParseSlots(p, d);
        var aliases = ParseAliases(p, d);
        return new CharacterAuthoringDocument(version, displayName, weight, movement, presentation,
            Float(p, "capsuleRadius", "character.capsuleRadius", d), Float(p, "capsuleHeight", "character.capsuleHeight", d),
            Float(p, "hipHeight", "character.hipHeight", d), Float(p, "hurtboxRadius", "character.hurtboxRadius", d),
            Float(p, "shieldRadius", "character.shieldRadius", d), ParseCaptureGeometry(p, d), capsules, bones, attachmentBoneIds, presentationIds, capabilities, slots, aliases);
    }

    private static CharacterMovementSource ParseMovement(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var p = Object(parent, "movement", "character.movement", d, "runSpeed", "runAccelerationA", "runAccelerationB", "dashSpeed", "airDodgeSpeed", "airSpeedMax", "airAccelStick", "airAccelBase", "jumpForce", "shortHopForce", "airJumpVMultiplier", "airJumpHMultiplier", "gravity", "airFloatGravity", "dashDurationTicks", "dashCooldownTicks", "groundFriction", "airFriction", "maxFallSpeed", "fastFallSpeed", "maxJumps", "jumpSquatTicks", "floatWindowTicks", "rushTicks");
        return new CharacterMovementSource(
            Float(p, "runSpeed", "character.movement.runSpeed", d), Float(p, "runAccelerationA", "character.movement.runAccelerationA", d), Float(p, "runAccelerationB", "character.movement.runAccelerationB", d),
            Float(p, "dashSpeed", "character.movement.dashSpeed", d), Float(p, "airDodgeSpeed", "character.movement.airDodgeSpeed", d), Float(p, "airSpeedMax", "character.movement.airSpeedMax", d), Float(p, "airAccelStick", "character.movement.airAccelStick", d), Float(p, "airAccelBase", "character.movement.airAccelBase", d),
            Float(p, "jumpForce", "character.movement.jumpForce", d), Float(p, "shortHopForce", "character.movement.shortHopForce", d), Float(p, "airJumpVMultiplier", "character.movement.airJumpVMultiplier", d), Float(p, "airJumpHMultiplier", "character.movement.airJumpHMultiplier", d),
            Float(p, "gravity", "character.movement.gravity", d), Float(p, "airFloatGravity", "character.movement.airFloatGravity", d), UShort(p, "dashDurationTicks", "character.movement.dashDurationTicks", d), UShort(p, "dashCooldownTicks", "character.movement.dashCooldownTicks", d),
            Float(p, "groundFriction", "character.movement.groundFriction", d), Float(p, "airFriction", "character.movement.airFriction", d), Float(p, "maxFallSpeed", "character.movement.maxFallSpeed", d), Float(p, "fastFallSpeed", "character.movement.fastFallSpeed", d),
            Byte(p, "maxJumps", "character.movement.maxJumps", d), UShort(p, "jumpSquatTicks", "character.movement.jumpSquatTicks", d), UShort(p, "floatWindowTicks", "character.movement.floatWindowTicks", d), UShort(p, "rushTicks", "character.movement.rushTicks", d));
    }

    private static CharacterPresentationSource ParsePresentation(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var p = Object(parent, "presentation", "character.presentation", d, "idle", "run", "dash", "jump", "fall", "hitSmall", "hitMedium", "hitHard", "tumble", "crouch", "slide", "shield", "grab", "grabbed", "throwForward", "airDodge", "landStartOffsetSeconds", "modelResourcePath", "visualScale", "hurtboxBoneScale", "modelYOffset", "modelSoleOffset", "autoModelYOffset");
        return new CharacterPresentationSource(
            String(p, "idle", "character.presentation.idle", d),
            String(p, "run", "character.presentation.run", d),
            String(p, "dash", "character.presentation.dash", d),
            String(p, "jump", "character.presentation.jump", d),
            String(p, "fall", "character.presentation.fall", d),
            String(p, "hitSmall", "character.presentation.hitSmall", d),
            String(p, "hitMedium", "character.presentation.hitMedium", d),
            String(p, "hitHard", "character.presentation.hitHard", d),
            Float(p, "landStartOffsetSeconds", "character.presentation.landStartOffsetSeconds", d),
            String(p, "modelResourcePath", "character.presentation.modelResourcePath", d),
            Float(p, "visualScale", "character.presentation.visualScale", d),
            Float(p, "hurtboxBoneScale", "character.presentation.hurtboxBoneScale", d),
            Float(p, "modelYOffset", "character.presentation.modelYOffset", d),
            Float(p, "modelSoleOffset", "character.presentation.modelSoleOffset", d),
            Bool(p, "autoModelYOffset", "character.presentation.autoModelYOffset", d),
            OptionalString(p, "tumble", "character.presentation.tumble", d) ?? "",
            OptionalString(p, "crouch", "character.presentation.crouch", d) ?? "",
            OptionalString(p, "slide", "character.presentation.slide", d) ?? "",
            OptionalString(p, "shield", "character.presentation.shield", d) ?? "",
            OptionalString(p, "grab", "character.presentation.grab", d) ?? "",
            OptionalString(p, "grabbed", "character.presentation.grabbed", d) ?? "",
            OptionalString(p, "throwForward", "character.presentation.throwForward", d) ?? "",
            OptionalString(p, "airDodge", "character.presentation.airDodge", d) ?? "");
    }

    private static CharacterCaptureGeometrySource ParseCaptureGeometry(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var p = Object(parent, "captureGeometry", "character.captureGeometry", d, "reach", "width", "height", "offsetY", "attackerAnchor", "victimAnchor");
        return new CharacterCaptureGeometrySource(
            Float(p, "reach", "character.captureGeometry.reach", d),
            Float(p, "width", "character.captureGeometry.width", d),
            Float(p, "height", "character.captureGeometry.height", d),
            Float(p, "offsetY", "character.captureGeometry.offsetY", d),
            ParseCaptureAnchor(p, "attackerAnchor", d),
            ParseCaptureAnchor(p, "victimAnchor", d));
    }

    private static CaptureAnchorSource ParseCaptureAnchor(Dictionary<string, JsonElement> parent, string name, DiagnosticBag d)
    {
        string path = "character.captureGeometry." + name;
        var p = Object(parent, name, path, d, "x", "y", "z");
        return new CaptureAnchorSource(
            Float(p, "x", path + ".x", d),
            Float(p, "y", path + ".y", d),
            Float(p, "z", path + ".z", d));
    }

    private static List<HurtboxCapsuleSource> ParseCapsules(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var result = new List<HurtboxCapsuleSource>();
        if (!Array(parent, "hurtboxCapsules", "character.hurtboxCapsules", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var p = ReadObject(e, $"character.hurtboxCapsules[{i}]", d, "startX", "startY", "startZ", "endX", "endY", "endZ", "radius");
            result.Add(new HurtboxCapsuleSource(Float(p, "startX", $"character.hurtboxCapsules[{i}].startX", d), Float(p, "startY", $"character.hurtboxCapsules[{i}].startY", d), Float(p, "startZ", $"character.hurtboxCapsules[{i}].startZ", d), Float(p, "endX", $"character.hurtboxCapsules[{i}].endX", d), Float(p, "endY", $"character.hurtboxCapsules[{i}].endY", d), Float(p, "endZ", $"character.hurtboxCapsules[{i}].endZ", d), Float(p, "radius", $"character.hurtboxCapsules[{i}].radius", d)));
            i++;
        }
        return result;
    }

    private static List<HurtboxBoneSource> ParseBones(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var result = new List<HurtboxBoneSource>();
        if (!Array(parent, "hurtboxBoneDefs", "character.hurtboxBoneDefs", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var p = ReadObject(e, $"character.hurtboxBoneDefs[{i}]", d, "boneId", "offsetX", "offsetY", "offsetZ", "radius");
            result.Add(new HurtboxBoneSource(String(p, "boneId", $"character.hurtboxBoneDefs[{i}].boneId", d), Float(p, "offsetX", $"character.hurtboxBoneDefs[{i}].offsetX", d), Float(p, "offsetY", $"character.hurtboxBoneDefs[{i}].offsetY", d), Float(p, "offsetZ", $"character.hurtboxBoneDefs[{i}].offsetZ", d), Float(p, "radius", $"character.hurtboxBoneDefs[{i}].radius", d)));
            i++;
        }
        return result;
    }

    private static List<CapabilityRequirementSource> ParseCapabilities(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var result = new List<CapabilityRequirementSource>();
        if (!Array(parent, "capabilityRequirements", "character.capabilityRequirements", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var p = ReadObject(e, $"character.capabilityRequirements[{i}]", d, "capabilityId", "capabilityVersion");
            result.Add(new CapabilityRequirementSource(String(p, "capabilityId", $"character.capabilityRequirements[{i}].capabilityId", d), String(p, "capabilityVersion", $"character.capabilityRequirements[{i}].capabilityVersion", d)));
            i++;
        }
        return result;
    }

    private static List<CharacterAliasSource> ParseAliases(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var result = new List<CharacterAliasSource>();
        if (!Array(parent, "aliases", "character.aliases", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var p = ReadObject(e, $"character.aliases[{i}]", d, "from", "to");
            result.Add(new CharacterAliasSource(String(p, "from", $"character.aliases[{i}].from", d), String(p, "to", $"character.aliases[{i}].to", d)));
            i++;
        }
        return result;
    }

    private static List<CharacterSlotSource> ParseSlots(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var result = new List<CharacterSlotSource>();
        if (!Array(parent, "slots", "character.slots", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var path = $"character.slots[{i}]";
            var p = ReadObject(e, path, d, "id", "name", "description", "iconId", "behavior", "aimMode", "aimMovement", "aimAnimationId", "hitPresentationId", "cooldownTicks", "isRecoveryMove", "preserveMomentumOnStart", "allowSlideCarry", "chargePool", "timeline");
            var chargePool = p.TryGetValue("chargePool", out var chargeElement) && chargeElement.ValueKind != JsonValueKind.Null
                ? ParseChargePool(chargeElement, path + ".chargePool", d)
                : null;
            result.Add(new CharacterSlotSource(String(p, "id", path + ".id", d), String(p, "name", path + ".name", d), String(p, "description", path + ".description", d), String(p, "iconId", path + ".iconId", d), EnumValue(p, "behavior", path + ".behavior", d, ParseBehavior), EnumValue(p, "aimMode", path + ".aimMode", d, ParseAimMode), UShort(p, "cooldownTicks", path + ".cooldownTicks", d), Bool(p, "isRecoveryMove", path + ".isRecoveryMove", d), Bool(p, "preserveMomentumOnStart", path + ".preserveMomentumOnStart", d), ParseTimeline(p, path, d), chargePool, OptionalEnumValue(p, "aimMovement", path + ".aimMovement", d, ParseAimMovement, AuthoringAimMovementMode.Fixed), OptionalString(p, "aimAnimationId", path + ".aimAnimationId", d), OptionalBool(p, "allowSlideCarry", path + ".allowSlideCarry", d), OptionalString(p, "hitPresentationId", path + ".hitPresentationId", d)));
            i++;
        }
        return result;
    }
    private static ChargePoolSource ParseChargePool(JsonElement element, string path, DiagnosticBag d)
    {
        var p = ReadObject(element, path, d, "maxCharges", "regenTicks");
        int maxCharges = p.TryGetValue("maxCharges", out var max) && max.TryGetInt32(out var value)
            ? value
            : 0;
        if (!p.ContainsKey("maxCharges")) d.Error("schema.missing", path + ".maxCharges", "Required integer is missing.");
        else if (!max.TryGetInt32(out _)) d.Error("value.out-of-range", path + ".maxCharges", "32-bit integer is required.");
        return new ChargePoolSource(maxCharges, UShort(p, "regenTicks", path + ".regenTicks", d));
    }

    private static CharacterTimelineSource ParseTimeline(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var p = Object(parent, "timeline", path + ".timeline", d, "stages");
        var stages = new List<CharacterStageSource>();
        if (!Array(p, "stages", path + ".timeline.stages", d, out var a)) return new CharacterTimelineSource(stages);
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var stagePath = path + ".timeline.stages[" + i + "]";
            var sp = ReadObject(e, stagePath, d, "durationTicks", "iasaTicks", "landingLagTicks", "autoCancelBeforeTicks", "autoCancelAfterTicks", "attackRange", "warpRange", "useTargetLock", "rotateTowardTarget", "trackingStrength", "animationIds", "operations");
            var anims = ParseStringArray(sp, "animationIds", stagePath + ".animationIds", d);
            var operations = ParseOperations(sp, stagePath, d);
            stages.Add(new CharacterStageSource(UShort(sp, "durationTicks", stagePath + ".durationTicks", d), UShort(sp, "iasaTicks", stagePath + ".iasaTicks", d), UShort(sp, "landingLagTicks", stagePath + ".landingLagTicks", d), UShort(sp, "autoCancelBeforeTicks", stagePath + ".autoCancelBeforeTicks", d), UShort(sp, "autoCancelAfterTicks", stagePath + ".autoCancelAfterTicks", d), anims, operations, OptionalFloat(sp, "attackRange", stagePath + ".attackRange", d), OptionalFloat(sp, "warpRange", stagePath + ".warpRange", d), OptionalBool(sp, "useTargetLock", stagePath + ".useTargetLock", d), OptionalBool(sp, "rotateTowardTarget", stagePath + ".rotateTowardTarget", d), OptionalFloat(sp, "trackingStrength", stagePath + ".trackingStrength", d)));
            i++;
        }
        return new CharacterTimelineSource(stages);
    }

    private static List<CharacterTimelineOperationSource> ParseOperations(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var result = new List<CharacterTimelineOperationSource>();
        if (!Array(parent, "operations", path + ".operations", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var opPath = path + ".operations[" + i + "]";
            var p = ReadObject(e, opPath, d, "kind", "tick", "unit", "velocityMode", "x", "y", "z", "speed", "durationTicks", "stopInAttackRange", "gravityScale", "hitbox", "projectile", "aimState", "capabilityId", "capabilityVersion", "parameters", "presentationId", "placement", "endTick", "acquisitionRange", "acquisitionHalfAngleDegrees", "maxYawDegrees", "maxPitchDegrees", "yawDegreesPerSecond", "pitchDegreesPerSecond");
            var kind = String(p, "kind", opPath + ".kind", d);
            var tick = UShort(p, "tick", opPath + ".tick", d);
            var unit = ParseUnit(p, "unit", opPath + ".unit", d);
            ValidateOperationFields(p, kind, opPath, d);
            switch (kind)
            {
                case "setVelocity":
                    result.Add(new SetVelocityOperationSource(tick, unit, EnumValue(p, "velocityMode", opPath + ".velocityMode", d, ParseVelocityMode), Float(p, "x", opPath + ".x", d), Float(p, "y", opPath + ".y", d), Float(p, "z", opPath + ".z", d))); break;
                case "forwardLunge":
                    result.Add(new ForwardLungeOperationSource(tick, unit, Float(p, "speed", opPath + ".speed", d), UShort(p, "durationTicks", opPath + ".durationTicks", d), OptionalBool(p, "stopInAttackRange", opPath + ".stopInAttackRange", d))); break;
                case "gravityWindow":
                    result.Add(new GravityWindowOperationSource(tick, unit, Float(p, "gravityScale", opPath + ".gravityScale", d), UShort(p, "durationTicks", opPath + ".durationTicks", d))); break;
                case "armorWindow":
                    result.Add(new ArmorWindowOperationSource(tick, unit,
                        UShort(p, "durationTicks", opPath + ".durationTicks", d)));
                    break;
                case "spawnHitbox": result.Add(new SpawnHitboxOperationSource(tick, unit, ParseHitbox(p, opPath, d))); break;
                case "spawnProjectile": result.Add(new SpawnProjectileOperationSource(tick, unit, ParseProjectile(p, opPath, d))); break;
                case "setAimState": result.Add(new SetAimStateOperationSource(tick, unit, EnumValue(p, "aimState", opPath + ".aimState", d, ParseAimMode))); break;
                case "startCapability": result.Add(new StartCapabilityOperationSource(tick, unit, String(p, "capabilityId", opPath + ".capabilityId", d), String(p, "capabilityVersion", opPath + ".capabilityVersion", d), ParseCapabilityParameters(p, opPath, d))); break;
                case "emitPresentation": result.Add(new EmitPresentationOperationSource(tick, unit, String(p, "presentationId", opPath + ".presentationId", d), ParsePlacement(p, opPath, d))); break;
                case "completeTimeline": result.Add(new CompleteTimelineOperationSource(tick, unit)); break;
                case "startupAimCorrection":
                    result.Add(new StartupAimCorrectionOperationSource(
                        tick, unit,
                        UShort(p, "endTick", opPath + ".endTick", d),
                        Float(p, "acquisitionRange", opPath + ".acquisitionRange", d),
                        Float(p, "acquisitionHalfAngleDegrees", opPath + ".acquisitionHalfAngleDegrees", d),
                        Float(p, "maxYawDegrees", opPath + ".maxYawDegrees", d),
                        Float(p, "maxPitchDegrees", opPath + ".maxPitchDegrees", d),
                        Float(p, "yawDegreesPerSecond", opPath + ".yawDegreesPerSecond", d),
                        Float(p, "pitchDegreesPerSecond", opPath + ".pitchDegreesPerSecond", d)));
                    break;
                default: d.Error("operation.unknown", opPath + ".kind", "Unknown timeline operation."); break;
            }
            i++;
        }
        return result;
    }
    private static void ValidateOperationFields(Dictionary<string, JsonElement> properties, string kind, string path, DiagnosticBag d)
    {
        var allowed = kind switch
        {
            "setVelocity" => new[] { "kind", "tick", "unit", "velocityMode", "x", "y", "z" },
            "forwardLunge" => new[] { "kind", "tick", "unit", "speed", "durationTicks", "stopInAttackRange" },
            "gravityWindow" => new[] { "kind", "tick", "unit", "gravityScale", "durationTicks" },
            "armorWindow" => new[] { "kind", "tick", "unit", "durationTicks" },
            "spawnHitbox" => new[] { "kind", "tick", "unit", "hitbox" },
            "spawnProjectile" => new[] { "kind", "tick", "unit", "projectile" },
            "setAimState" => new[] { "kind", "tick", "unit", "aimState" },
            "startCapability" => new[] { "kind", "tick", "unit", "capabilityId", "capabilityVersion", "parameters" },
            "emitPresentation" => new[] { "kind", "tick", "unit", "presentationId", "placement" },
            "completeTimeline" => new[] { "kind", "tick", "unit" },
            "startupAimCorrection" => new[] { "kind", "tick", "unit", "endTick", "acquisitionRange", "acquisitionHalfAngleDegrees", "maxYawDegrees", "maxPitchDegrees", "yawDegreesPerSecond", "pitchDegreesPerSecond" },
            _ => System.Array.Empty<string>(),
        };
        if (allowed.Length == 0) return;
        foreach (var name in properties.Keys) if (!allowed.Contains(name, StringComparer.Ordinal)) d.Error("operation.parameter-unknown", path + "." + name, "Field is not valid for this operation.");
        foreach (var name in allowed)
            if (!properties.ContainsKey(name) && !(kind == "emitPresentation" && name == "placement")
                && !(kind == "forwardLunge" && name == "stopInAttackRange"))
                d.Error("operation.parameter-missing", path + "." + name, "Field is required for this operation.");
    }

    private static PresentationPlacement ParsePlacement(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        if (!parent.TryGetValue("placement", out var element))
            return new PresentationPlacement();
        var p = ReadObject(element, path + ".placement", d,
            "attachmentMode", "boneId",
            "localPositionX", "localPositionY", "localPositionZ",
            "localRotationX", "localRotationY", "localRotationZ",
            "localScaleX", "localScaleY", "localScaleZ", "durationTicks");
        return new PresentationPlacement(
            OptionalEnumValue(p, "attachmentMode", path + ".placement.attachmentMode", d, ParsePresentationAttachmentMode, AuthoringPresentationAttachmentMode.World),
            OptionalString(p, "boneId", path + ".placement.boneId", d),
            OptionalFloat(p, "localPositionX", path + ".placement.localPositionX", d),
            OptionalFloat(p, "localPositionY", path + ".placement.localPositionY", d),
            OptionalFloat(p, "localPositionZ", path + ".placement.localPositionZ", d),
            OptionalFloat(p, "localRotationX", path + ".placement.localRotationX", d),
            OptionalFloat(p, "localRotationY", path + ".placement.localRotationY", d),
            OptionalFloat(p, "localRotationZ", path + ".placement.localRotationZ", d),
            OptionalFloatOrDefault(p, "localScaleX", path + ".placement.localScaleX", d, 1f),
            OptionalFloatOrDefault(p, "localScaleY", path + ".placement.localScaleY", d, 1f),
            OptionalFloatOrDefault(p, "localScaleZ", path + ".placement.localScaleZ", d, 1f),
            OptionalUShort(p, "durationTicks", path + ".placement.durationTicks", d, 28));
    }

    private static HitboxSource ParseHitboxNamed(Dictionary<string, JsonElement> parent, string name, string path, DiagnosticBag d)
    {
        var nested = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (parent.TryGetValue(name, out var hitbox)) nested.Add("hitbox", hitbox);
        else d.Error("operation.parameter-missing", path, "Hitbox is required.");
        return ParseHitbox(nested, path, d);
    }
    private static HitboxSource ParseHitbox(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var p = Object(parent, "hitbox", path + ".hitbox", d, "shape", "radius", "offsetX", "offsetY", "offsetZ", "endOffsetX", "endOffsetY", "endOffsetZ", "startBoneId", "endBoneId", "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks", "durationTicks", "interruptible", "hitGroup", "knockbackDirection", "fixedHitstunTicks");
        return new HitboxSource(
            EnumValue(p, "shape", path + ".hitbox.shape", d, ParseShape),
            Float(p, "radius", path + ".hitbox.radius", d),
            Float(p, "offsetX", path + ".hitbox.offsetX", d),
            Float(p, "offsetY", path + ".hitbox.offsetY", d),
            Float(p, "offsetZ", path + ".hitbox.offsetZ", d),
            Float(p, "endOffsetX", path + ".hitbox.endOffsetX", d),
            Float(p, "endOffsetY", path + ".hitbox.endOffsetY", d),
            Float(p, "endOffsetZ", path + ".hitbox.endOffsetZ", d),
            OptionalString(p, "startBoneId", path + ".hitbox.startBoneId", d),
            OptionalString(p, "endBoneId", path + ".hitbox.endBoneId", d),
            Float(p, "damage", path + ".hitbox.damage", d),
            Float(p, "angle", path + ".hitbox.angle", d),
            Float(p, "baseKnockback", path + ".hitbox.baseKnockback", d),
            Float(p, "knockbackGrowth", path + ".hitbox.knockbackGrowth", d),
            UShort(p, "stunTicks", path + ".hitbox.stunTicks", d),
            UShort(p, "durationTicks", path + ".hitbox.durationTicks", d),
            Bool(p, "interruptible", path + ".hitbox.interruptible", d),
            Byte(p, "hitGroup", path + ".hitbox.hitGroup", d),
            OptionalEnumValue(p, "knockbackDirection", path + ".hitbox.knockbackDirection", d, ParseKnockbackDirection, AuthoringKnockbackDirection.AwayFromOwner),
            OptionalUShort(p, "fixedHitstunTicks", path + ".hitbox.fixedHitstunTicks", d, 0));
    }

    private static ProjectileSource ParseProjectile(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var p = Object(parent, "projectile", path + ".projectile", d, "launchOffsetX", "launchOffsetY", "launchOffsetZ", "speed", "gravity", "radius", "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks", "maxFlightTicks", "yawOffsetDegrees");
        return new ProjectileSource(Float(p, "launchOffsetX", path + ".projectile.launchOffsetX", d), Float(p, "launchOffsetY", path + ".projectile.launchOffsetY", d), Float(p, "launchOffsetZ", path + ".projectile.launchOffsetZ", d), Float(p, "speed", path + ".projectile.speed", d), Float(p, "gravity", path + ".projectile.gravity", d), Float(p, "radius", path + ".projectile.radius", d), Float(p, "damage", path + ".projectile.damage", d), Float(p, "angle", path + ".projectile.angle", d), Float(p, "baseKnockback", path + ".projectile.baseKnockback", d), Float(p, "knockbackGrowth", path + ".projectile.knockbackGrowth", d), UShort(p, "stunTicks", path + ".projectile.stunTicks", d), UShort(p, "maxFlightTicks", path + ".projectile.maxFlightTicks", d), Float(p, "yawOffsetDegrees", path + ".projectile.yawOffsetDegrees", d));
    }

    private static IReadOnlyList<MankiAerosolChargeTier>? ParseAerosolChargeTiers(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        if (!p.ContainsKey("chargeTiers")) return null;
        var tiers = new List<MankiAerosolChargeTier>();
        if (!Array(p, "chargeTiers", path, d, out var values)) return tiers;
        int index = 0;
        foreach (var value in values.EnumerateArray())
        {
            var tierPath = path + "[" + index++ + "]";
            var tier = ReadObject(value, tierPath, d, "chargeTicks", "damage", "reachScale");
            tiers.Add(new MankiAerosolChargeTier(UShort(tier, "chargeTicks", tierPath + ".chargeTicks", d),
                Float(tier, "damage", tierPath + ".damage", d), Float(tier, "reachScale", tierPath + ".reachScale", d)));
        }
        return tiers;
    }

    private static TypedCapabilityParameters ParseCapabilityParameters(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var id = String(parent, "capabilityId", path + ".capabilityId", d);
        if (id == CharacterPackageCompiler.RetiredTargetedLeapCapabilityId)
        {
            d.Error("capability.retired", path + ".capabilityId", "The Bonk-only targeted jump slam capability has been retired.");
            return new RisingDragonCapabilityParameters(0, 0, 0);
        }
        if (id == CharacterPackageCompiler.RetiredWibouDashSlashCapabilityId)
        {
            d.Error("capability.retired", path + ".capabilityId", "The Wibou dash-slash capability has been retired.");
            return new RisingDragonCapabilityParameters(0, 0, 0);
        }
        if (!parent.TryGetValue("parameters", out var element)) { d.Error("operation.parameter-missing", path + ".parameters", "Capability parameters are required."); return new RisingDragonCapabilityParameters(0, 0, 0); }
        var allowed = id switch
        {
            "slop.ability.charged-projectile.v1" => new[] { "maxChargeTicks", "tier2Ticks", "tier3Ticks", "fireTriggerTick", "tier2Radius", "tier3Radius", "projectile" },
            "slop.internal.fightguy.rising-dragon.v1" => new[] { "riseSpeed", "riseTicks", "riseDelay" },
            "slop.internal.fightguy.cyclone-kick.v1" => new[] { "forwardSpeed", "windupTicks", "hitboxEndTick", "durationTicks", "bodyRadius", "sideRadius", "sideOffset", "damage", "knockbackAngle", "knockbackBase", "knockbackGrowth", "stunTicks", "bodyY", "sideY" },
            "slop.ability.charged-directional-dash.v1" => new[] { "maxChargeTicks", "tier2Ticks", "tier3Ticks", "minDistance", "maxDistance", "dashSpeed", "finisherLeadTicks", "finisherSeekTick", "recoveryTicks", "tier2Damage", "tier3Damage", "traversalHitbox", "finisherHitbox" },
            "slop.internal.wibou.rising-slash.v1" => new[] { "riseSpeed", "riseTicks", "homingRange", "homingSpeed" },
            "slop.internal.wibou.blade-flurry.v1" => new[] { "forwardSpeed", "moveTicks" },
            "slop.ability.targeted-leap.v1" => new[] { "maxAimTicks", "maxFlightTicks", "minRange", "maxRange", "launchVerticalSpeed", "landingSeekTick", "recoveryTicks", "hitbox", "launchPresentationId", "landingPresentationId" },
            "slop.internal.manki.round-bomb.v1" => new[] { "throwTriggerTick", "maxRange", "launchAngle", "gravity", "hitboxRadius", "damage", "stunTicks", "maxFlightTicks", "kbAngle", "explosionDamage", "explosionRadius", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "explosionPresentationId" },
            "slop.internal.manki.jetpack-boost.v1" => new[] { "startupTicks", "verticalSpeed", "horizontalSpeed", "explosionRadius", "explosionDamage", "explosionKbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionPresentationId" },
            "slop.internal.manki.bazooka.v1" => new[] { "fireTriggerTick", "projectileSpeed", "hitboxRadius", "damage", "gravity", "maxFlightTicks", "stunTicks", "explosionRadius", "kbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "castDuration", "recoveryDuration", "explosionPresentationId" },
            "slop.internal.manki.aerosol-inferno.v1" => new[] { "minimumChargeTicks", "chargeTiers", "fireTriggerTick", "fireDurationTicks", "hitboxDurationTicks", "hitboxRadius", "offsetY", "offsetZ", "endOffsetZ", "damage", "knockbackAngle", "knockbackBase", "knockbackGrowth", "stunTicks", "hitGroup" },

            _ => System.Array.Empty<string>(),
        };
        var p = ReadObjectWithCode(element, path + ".parameters", d, "operation.parameter-unknown", allowed);
        foreach (var required in allowed) if (!string.Equals(required, "explosionPresentationId", StringComparison.Ordinal) && !string.Equals(required, "launchPresentationId", StringComparison.Ordinal) && !string.Equals(required, "landingPresentationId", StringComparison.Ordinal) && !string.Equals(required, "minimumChargeTicks", StringComparison.Ordinal) && !string.Equals(required, "chargeTiers", StringComparison.Ordinal) && !p.ContainsKey(required)) d.Error("operation.parameter-missing", path + ".parameters." + required, "Required capability parameter is missing.");
        if (id == CharacterPackageCompiler.ChargedProjectileCapabilityId)
            return new ChargedProjectileCapabilityParameters(
                UShort(p, "maxChargeTicks", path + ".parameters.maxChargeTicks", d),
                UShort(p, "tier2Ticks", path + ".parameters.tier2Ticks", d),
                UShort(p, "tier3Ticks", path + ".parameters.tier3Ticks", d),
                UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d),
                Float(p, "tier2Radius", path + ".parameters.tier2Radius", d),
                Float(p, "tier3Radius", path + ".parameters.tier3Radius", d),
                ParseProjectile(p, path + ".parameters", d));
        if (id.EndsWith("rising-dragon.v1", StringComparison.Ordinal)) return new RisingDragonCapabilityParameters(Float(p, "riseSpeed", path + ".parameters.riseSpeed", d), UShort(p, "riseTicks", path + ".parameters.riseTicks", d), UShort(p, "riseDelay", path + ".parameters.riseDelay", d));
        if (id.EndsWith("cyclone-kick.v1", StringComparison.Ordinal)) return new CycloneKickCapabilityParameters(Float(p, "forwardSpeed", path + ".parameters.forwardSpeed", d), UShort(p, "windupTicks", path + ".parameters.windupTicks", d), UShort(p, "hitboxEndTick", path + ".parameters.hitboxEndTick", d), UShort(p, "durationTicks", path + ".parameters.durationTicks", d), Float(p, "bodyRadius", path + ".parameters.bodyRadius", d), Float(p, "sideRadius", path + ".parameters.sideRadius", d), Float(p, "sideOffset", path + ".parameters.sideOffset", d), Float(p, "damage", path + ".parameters.damage", d), Float(p, "knockbackAngle", path + ".parameters.knockbackAngle", d), Float(p, "knockbackBase", path + ".parameters.knockbackBase", d), Float(p, "knockbackGrowth", path + ".parameters.knockbackGrowth", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), Float(p, "bodyY", path + ".parameters.bodyY", d), Float(p, "sideY", path + ".parameters.sideY", d));
        if (id == CharacterPackageCompiler.ChargedDirectionalDashCapabilityId)
            return new ChargedDirectionalDashCapabilityParameters(
                UShort(p, "maxChargeTicks", path + ".parameters.maxChargeTicks", d),
                UShort(p, "tier2Ticks", path + ".parameters.tier2Ticks", d),
                UShort(p, "tier3Ticks", path + ".parameters.tier3Ticks", d),
                Float(p, "minDistance", path + ".parameters.minDistance", d),
                Float(p, "maxDistance", path + ".parameters.maxDistance", d),
                Float(p, "dashSpeed", path + ".parameters.dashSpeed", d),
                UShort(p, "finisherLeadTicks", path + ".parameters.finisherLeadTicks", d),
                UShort(p, "finisherSeekTick", path + ".parameters.finisherSeekTick", d),
                UShort(p, "recoveryTicks", path + ".parameters.recoveryTicks", d),
                Float(p, "tier2Damage", path + ".parameters.tier2Damage", d),
                Float(p, "tier3Damage", path + ".parameters.tier3Damage", d),
                ParseHitboxNamed(p, "traversalHitbox", path + ".parameters.traversalHitbox", d),
                ParseHitboxNamed(p, "finisherHitbox", path + ".parameters.finisherHitbox", d));
        if (id.EndsWith("wibou.rising-slash.v1", StringComparison.Ordinal)) return new WibouRisingSlashCapabilityParameters(Float(p, "riseSpeed", path + ".parameters.riseSpeed", d), UShort(p, "riseTicks", path + ".parameters.riseTicks", d), Float(p, "homingRange", path + ".parameters.homingRange", d), Float(p, "homingSpeed", path + ".parameters.homingSpeed", d));
        if (id.EndsWith("wibou.blade-flurry.v1", StringComparison.Ordinal)) return new WibouBladeFlurryCapabilityParameters(Float(p, "forwardSpeed", path + ".parameters.forwardSpeed", d), UShort(p, "moveTicks", path + ".parameters.moveTicks", d));
        if (id == CharacterPackageCompiler.TargetedLeapCapabilityId)
            return new TargetedLeapCapabilityParameters(
                UShort(p, "maxAimTicks", path + ".parameters.maxAimTicks", d),
                UShort(p, "maxFlightTicks", path + ".parameters.maxFlightTicks", d),
                Float(p, "minRange", path + ".parameters.minRange", d),
                Float(p, "maxRange", path + ".parameters.maxRange", d),
                Float(p, "launchVerticalSpeed", path + ".parameters.launchVerticalSpeed", d),
                UShort(p, "landingSeekTick", path + ".parameters.landingSeekTick", d),
                UShort(p, "recoveryTicks", path + ".parameters.recoveryTicks", d),
                ParseHitbox(p, path + ".parameters", d),
                OptionalString(p, "launchPresentationId", path + ".parameters.launchPresentationId", d) ?? "",
                OptionalString(p, "landingPresentationId", path + ".parameters.landingPresentationId", d) ?? "");
        if (id == "slop.internal.manki.round-bomb.v1") return new MankiRoundBombCapabilityParameters(UShort(p, "throwTriggerTick", path + ".parameters.throwTriggerTick", d), Float(p, "maxRange", path + ".parameters.maxRange", d), Float(p, "launchAngle", path + ".parameters.launchAngle", d), Float(p, "gravity", path + ".parameters.gravity", d), Float(p, "hitboxRadius", path + ".parameters.hitboxRadius", d), Float(p, "damage", path + ".parameters.damage", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), UShort(p, "maxFlightTicks", path + ".parameters.maxFlightTicks", d), Float(p, "kbAngle", path + ".parameters.kbAngle", d), Float(p, "explosionDamage", path + ".parameters.explosionDamage", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "");
        if (id == "slop.internal.manki.jetpack-boost.v1") return new MankiJetpackBoostCapabilityParameters(UShort(p, "startupTicks", path + ".parameters.startupTicks", d), Float(p, "verticalSpeed", path + ".parameters.verticalSpeed", d), Float(p, "horizontalSpeed", path + ".parameters.horizontalSpeed", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "explosionDamage", path + ".parameters.explosionDamage", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "");
        if (id == "slop.internal.manki.bazooka.v1") return new MankiBazookaCapabilityParameters(UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d), Float(p, "projectileSpeed", path + ".parameters.projectileSpeed", d), Float(p, "hitboxRadius", path + ".parameters.hitboxRadius", d), Float(p, "damage", path + ".parameters.damage", d), Float(p, "gravity", path + ".parameters.gravity", d), UShort(p, "maxFlightTicks", path + ".parameters.maxFlightTicks", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "kbAngle", path + ".parameters.kbAngle", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), UShort(p, "castDuration", path + ".parameters.castDuration", d), UShort(p, "recoveryDuration", path + ".parameters.recoveryDuration", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "");
        if (id == "slop.internal.manki.aerosol-inferno.v1") return new MankiAerosolInfernoCapabilityParameters(UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d), UShort(p, "fireDurationTicks", path + ".parameters.fireDurationTicks", d), UShort(p, "hitboxDurationTicks", path + ".parameters.hitboxDurationTicks", d), Float(p, "hitboxRadius", path + ".parameters.hitboxRadius", d), Float(p, "offsetY", path + ".parameters.offsetY", d), Float(p, "offsetZ", path + ".parameters.offsetZ", d), Float(p, "endOffsetZ", path + ".parameters.endOffsetZ", d), Float(p, "damage", path + ".parameters.damage", d), Float(p, "knockbackAngle", path + ".parameters.knockbackAngle", d), Float(p, "knockbackBase", path + ".parameters.knockbackBase", d), Float(p, "knockbackGrowth", path + ".parameters.knockbackGrowth", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), Byte(p, "hitGroup", path + ".parameters.hitGroup", d), OptionalUShort(p, "minimumChargeTicks", path + ".parameters.minimumChargeTicks", d, 0), ParseAerosolChargeTiers(p, path + ".parameters.chargeTiers", d));

        d.Error("capability.unknown", path + ".capabilityId", "Capability parameters are not supported.");
        return new RisingDragonCapabilityParameters(0, 0, 0);
    }
    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, string path, DiagnosticBag d, params string[] allowed)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object) { d.Error("value.out-of-range", path, "Object is required."); return result; }
        foreach (var property in element.EnumerateObject()) { var propertyPath = path + "." + property.Name; if (!set.Contains(property.Name)) d.Error("field.unknown", propertyPath, "Unknown field."); if (!result.TryAdd(property.Name, property.Value)) d.Error("field.duplicate", propertyPath, "Duplicate field."); }
        return result;
    }
    private static Dictionary<string, JsonElement> ReadObjectWithCode(JsonElement element, string path, DiagnosticBag d, string unknownCode, params string[] allowed)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object) { d.Error("value.out-of-range", path, "Object is required."); return result; }
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = path + "." + property.Name;
            if (!set.Contains(property.Name)) d.Error(unknownCode, propertyPath, "Unknown capability parameter.");
            if (!result.TryAdd(property.Name, property.Value)) d.Error("field.duplicate", propertyPath, "Duplicate field.");
        }
        return result;
    }
    private static Dictionary<string, JsonElement> Object(Dictionary<string, JsonElement> parent, string name, string path, DiagnosticBag d, params string[] allowed) => parent.TryGetValue(name, out var value) ? ReadObject(value, path, d, allowed) : MissingObject(path, d);
    private static Dictionary<string, JsonElement> MissingObject(string path, DiagnosticBag d) { d.Error("schema.missing", path, "Required object is missing."); return new Dictionary<string, JsonElement>(StringComparer.Ordinal); }
    private static bool Array(Dictionary<string, JsonElement> parent, string name, string path, DiagnosticBag d, out JsonElement value) { if (!parent.TryGetValue(name, out value)) { d.Error("schema.missing", path, "Required array is missing."); return false; } if (value.ValueKind != JsonValueKind.Array) { d.Error("value.out-of-range", path, "Array is required."); return false; } return true; }
    private static List<string> ParseStringArray(Dictionary<string, JsonElement> parent, string name, string path, DiagnosticBag d) { var result = new List<string>(); if (!Array(parent, name, path, d, out var a)) return result; var i = 0; foreach (var x in a.EnumerateArray()) { if (x.ValueKind != JsonValueKind.String) d.Error("value.out-of-range", path + "[" + i + "]", "String is required."); else result.Add(x.GetString() ?? ""); i++; } return result; }
    private static string String(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) { d.Error("schema.missing", path, "Required string is missing."); return ""; } if (x.ValueKind != JsonValueKind.String) { d.Error("value.out-of-range", path, "String is required."); return ""; } return x.GetString() ?? ""; }
    private static string? OptionalString(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x) || x.ValueKind == JsonValueKind.Null) return null; return String(p, name, path, d); }
    private static float OptionalFloat(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) return 0f; if (x.ValueKind != JsonValueKind.Number || !x.TryGetSingle(out var n)) { d.Error("value.out-of-range", path, "Finite number is required."); return 0f; } return n; }
    private static float OptionalFloatOrDefault(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d, float fallback) { if (!p.ContainsKey(name)) return fallback; return OptionalFloat(p, name, path, d); }
    private static ushort OptionalUShort(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d, ushort fallback) { if (!p.TryGetValue(name, out var x)) return fallback; if (x.ValueKind != JsonValueKind.Number || !x.TryGetUInt16(out var n)) { d.Error("value.out-of-range", path, "Unsigned 16-bit integer is required."); return fallback; } return n; }
    private static bool OptionalBool(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) return false; if (x.ValueKind != JsonValueKind.True && x.ValueKind != JsonValueKind.False) { d.Error("value.out-of-range", path, "Boolean is required."); return false; } return x.GetBoolean(); }
    private static float Float(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) { d.Error("schema.missing", path, "Required number is missing."); return 0; } if (x.ValueKind != JsonValueKind.Number || !x.TryGetSingle(out var n)) { d.Error("value.out-of-range", path, "Finite number is required."); return 0; } return n; }
    private static ushort UShort(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) { d.Error("schema.missing", path, "Required integer is missing."); return 0; } if (x.ValueKind != JsonValueKind.Number || !x.TryGetUInt16(out var n)) { d.Error("value.out-of-range", path, "Unsigned 16-bit integer is required."); return 0; } return n; }
    private static byte Byte(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) { d.Error("schema.missing", path, "Required integer is missing."); return 0; } if (x.ValueKind != JsonValueKind.Number || !x.TryGetByte(out var n)) { d.Error("value.out-of-range", path, "Unsigned 8-bit integer is required."); return 0; } return n; }
    private static bool Bool(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d) { if (!p.TryGetValue(name, out var x)) { d.Error("schema.missing", path, "Required boolean is missing."); return false; } if (x.ValueKind != JsonValueKind.True && x.ValueKind != JsonValueKind.False) { d.Error("value.out-of-range", path, "Boolean is required."); return false; } return x.GetBoolean(); }
    private static T EnumValue<T>(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d, Func<string, T?> parse) where T : struct
    {
        if (!p.TryGetValue(name, out var element)) { d.Error("schema.missing", path, "Required enum is missing."); return default; }
        if (element.ValueKind != JsonValueKind.String) { d.Error("enum.unknown", path, "Enum must be a string token."); return default; }
        var parsed = parse(element.GetString() ?? "");
        if (!parsed.HasValue) { d.Error("enum.unknown", path, "Unknown enum value."); return default; }
        return parsed.Value;
    }
    private static T OptionalEnumValue<T>(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d, Func<string, T?> parse, T fallback) where T : struct
    {
        if (!p.TryGetValue(name, out var element)) return fallback;
        if (element.ValueKind != JsonValueKind.String) { d.Error("enum.unknown", path, "Enum must be a string token."); return fallback; }
        var parsed = parse(element.GetString() ?? "");
        if (!parsed.HasValue) { d.Error("enum.unknown", path, "Unknown enum value."); return fallback; }
        return parsed.Value;
    }
    private static AuthoringUnit ParseUnit(Dictionary<string, JsonElement> p, string name, string path, DiagnosticBag d)
    {
        if (!p.TryGetValue(name, out var element)) { d.Error("schema.missing", path, "Required unit is missing."); return AuthoringUnit.Ticks; }
        if (element.ValueKind != JsonValueKind.String) { d.Error("unit.unknown", path, "Unit must be a string token."); return AuthoringUnit.Ticks; }
        var value = element.GetString() ?? "";
        return value switch { "meters" => AuthoringUnit.Meters, "metersPerSecond" => AuthoringUnit.MetersPerSecond, "metersPerSecondSquared" => AuthoringUnit.MetersPerSecondSquared, "degrees" => AuthoringUnit.Degrees, "normalized" => AuthoringUnit.Normalized, "damage" => AuthoringUnit.Damage, "knockback" => AuthoringUnit.Knockback, "ticks" => AuthoringUnit.Ticks, _ => UnknownUnit(path, d) };
    }
    private static AuthoringUnit UnknownUnit(string path, DiagnosticBag d) { d.Error("unit.unknown", path, "Unknown unit."); return AuthoringUnit.Ticks; }
    private static AuthoringPresentationAttachmentMode? ParsePresentationAttachmentMode(string x) => x switch { "world" => AuthoringPresentationAttachmentMode.World, "bone" => AuthoringPresentationAttachmentMode.Bone, _ => null };
    private static AuthoringAbilityBehavior? ParseBehavior(string x) => x switch { "meleeCombo" => AuthoringAbilityBehavior.MeleeCombo, "chargeAttack" => AuthoringAbilityBehavior.ChargeAttack, "aimedProjectile" => AuthoringAbilityBehavior.AimedProjectile, "projectile" => AuthoringAbilityBehavior.Projectile, "airGroundProjectile" => AuthoringAbilityBehavior.AirGroundProjectile, "selfBuff" => AuthoringAbilityBehavior.SelfBuff, "areaDenial" => AuthoringAbilityBehavior.AreaDenial, "directionalDash" => AuthoringAbilityBehavior.DirectionalDash, _ => null };
    private static AuthoringAimMode? ParseAimMode(string x) => x switch { "none" => AuthoringAimMode.None, "groundCursor" => AuthoringAimMode.GroundCursor, "cameraForward3D" => AuthoringAimMode.CameraForward3D, "groundVector" => AuthoringAimMode.GroundVector, _ => null };
    private static AuthoringAimMovementMode? ParseAimMovement(string x) => x switch { "fixed" => AuthoringAimMovementMode.Fixed, "mobile" => AuthoringAimMovementMode.Mobile, _ => null };
    private static AuthoringHitboxShape? ParseShape(string x) => x switch { "sphere" => AuthoringHitboxShape.Sphere, "capsule" => AuthoringHitboxShape.Capsule, _ => null };
    private static AuthoringKnockbackDirection? ParseKnockbackDirection(string x) => x switch { "awayFromOwner" => AuthoringKnockbackDirection.AwayFromOwner, "towardOwner" => AuthoringKnockbackDirection.TowardOwner, _ => null };
    private static AuthoringVelocityMode? ParseVelocityMode(string x) => x switch { "absolute" => AuthoringVelocityMode.Absolute, "additive" => AuthoringVelocityMode.Additive, _ => null };
    private sealed class DiagnosticBag
    {
        private readonly List<(int Order, CharacterDiagnostic Value)> _items = new(); private int _order;
        public bool HasErrors => _items.Any(x => x.Value.Severity == CharacterDiagnosticSeverity.Error);
        public void Error(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message)));
        public void Warning(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, code, path, message)));
        public List<CharacterDiagnostic> ToList() => _items.OrderBy(x => x.Order).ThenBy(x => x.Value.Code, StringComparer.Ordinal).Select(x => x.Value).ToList();
    }
}
 
