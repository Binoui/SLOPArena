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
            ThrowForward = Rename(p.ThrowForward), AirDodge = Rename(p.AirDodge), Shell = Rename(p.Shell), Flying = Rename(p.Flying), MayhemLaugh = Rename(p.MayhemLaugh)
        };
        var shell = c.Shell is { } stance
            ? stance with
            {
                Splash = stance.Splash is { } splash && splash.PresentationId == oldId ? splash with { PresentationId = newId } : stance.Splash,
                Ride = stance.Ride is { } ride && ride.HitPresentationId == oldId ? ride with { HitPresentationId = newId } : stance.Ride,
            }
            : null;
        var presentations = c.PresentationIds.Select(Rename).ToArray();
        var slots = c.Slots.Select(RenameSlot).ToArray();
        var alternate = c.AlternateForm is { } form
            ? form with
            {
                Presentation = new FormPresentation(Rename(form.Presentation.Idle), Rename(form.Presentation.Run),
                    Rename(form.Presentation.Dash), Rename(form.Presentation.Jump), Rename(form.Presentation.Fall),
                    Rename(form.Presentation.HitSmall), Rename(form.Presentation.HitMedium), Rename(form.Presentation.HitHard),
                    Rename(form.Presentation.Tumble), Rename(form.Presentation.Shield), Rename(form.Presentation.Grab),
                    Rename(form.Presentation.Grabbed), Rename(form.Presentation.ThrowForward), Rename(form.Presentation.AirDodge)),
                Slots = form.Slots.Select(RenameSlot).ToArray(),
            }
            : null;
        return CharacterSourceEditResult.Success(source with { Character = c with { Presentation = p, PresentationIds = presentations, Slots = slots, Shell = shell, AlternateForm = alternate } });
        string Rename(string value) => value == oldId ? newId : value;
        CharacterSlotSource RenameSlot(CharacterSlotSource slot)
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
                        : op is SpawnHitboxOperationSource hit && hit.Hitbox.HitPresentationId == oldId ? hit with { Hitbox = hit.Hitbox with { HitPresentationId = newId } }
                        : op is StartCapabilityOperationSource capability ? capability with { Parameters = RenameCapabilityPresentation(CloneParameters(capability.Parameters), oldId, newId) }
                        : CloneOperation(op)).ToArray()
                }).ToArray())
            };
        }
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
            source.Character.Presentation.ThrowForward, source.Character.Presentation.AirDodge,
            source.Character.Presentation.Shell, source.Character.Presentation.Flying, source.Character.Presentation.MayhemLaugh, source.Character.Shell?.Splash?.PresentationId ?? "",
            source.Character.Shell?.Ride?.HitPresentationId ?? ""
        };
        sourceIds.AddRange(source.Character.PresentationIds);
        var allSlots = source.Character.Slots.Concat(source.Character.AlternateForm?.Slots ?? System.Array.Empty<CharacterSlotSource>()).ToArray();
        if (source.Character.AlternateForm is { } form)
            sourceIds.AddRange(form.Presentation.Roles.Select(role => role.Id).Where(x => !string.IsNullOrEmpty(x)));
        sourceIds.AddRange(allSlots.Select(x => x.HitPresentationId ?? "").Where(x => !string.IsNullOrEmpty(x)));
        sourceIds.AddRange(allSlots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.AnimationIds));
        sourceIds.AddRange(allSlots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<EmitPresentationOperationSource>().Select(x => x.PresentationId));
        sourceIds.AddRange(allSlots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<SpawnHitboxOperationSource>().Select(x => x.Hitbox.HitPresentationId ?? "").Where(x => x.Length > 0));
        sourceIds.AddRange(allSlots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<StartCapabilityOperationSource>().Select(x => x.Parameters switch { MankiRoundBombCapabilityParameters p => p.ExplosionPresentationId, MankiJetpackBoostCapabilityParameters p => p.ExplosionPresentationId, MankiBazookaCapabilityParameters p => p.ExplosionPresentationId, MankiKamikazeCapabilityParameters p => p.ExplosionPresentationId, _ => "" }).Where(x => !string.IsNullOrEmpty(x)));
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
            SwapFormOperationSource x => x with { },
            StartupAimCorrectionOperationSource x => x with { },
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
            SteeredChargeCapabilityParameters x => x with { LanceHitbox = x.LanceHitbox with { } },
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
    private static TypedCapabilityParameters RenameCapabilityPresentation(TypedCapabilityParameters p, string oldId, string newId)
        => p switch
        {
            MankiRoundBombCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
            MankiJetpackBoostCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
            MankiKamikazeCapabilityParameters x => x with { ExplosionPresentationId = x.ExplosionPresentationId == oldId ? newId : x.ExplosionPresentationId },
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
        w.WritePropertyName("hurtboxBoneDefs"); w.WriteStartArray(); foreach (var h in x.HurtboxBoneDefs ?? System.Array.Empty<HurtboxBoneSource>()) { w.WriteStartObject(); w.WriteString("boneId",h.BoneId); Number(w,"offsetX",h.OffsetX); Number(w,"offsetY",h.OffsetY); Number(w,"offsetZ",h.OffsetZ); Number(w,"radius",h.Radius); if (h.EndBoneId != null) w.WriteString("endBoneId",h.EndBoneId); Number(w,"endOffsetX",h.EndOffsetX); Number(w,"endOffsetY",h.EndOffsetY); Number(w,"endOffsetZ",h.EndOffsetZ); w.WriteEndObject(); } w.WriteEndArray();
        WriteStringArray(w, "attachmentBoneIds", x.AttachmentBoneIds); WriteStringArray(w, "presentationIds", x.PresentationIds);
        w.WritePropertyName("capabilityRequirements"); w.WriteStartArray(); foreach (var c in x.CapabilityRequirements ?? System.Array.Empty<CapabilityRequirementSource>()) { w.WriteStartObject(); w.WriteString("capabilityId", c.CapabilityId); w.WriteString("capabilityVersion", c.CapabilityVersion); w.WriteEndObject(); } w.WriteEndArray();
        if (x.Shell != null) WriteShell(w, x.Shell);
        w.WritePropertyName("slots"); w.WriteStartArray(); foreach (var slot in x.Slots ?? System.Array.Empty<CharacterSlotSource>()) WriteSlot(w, slot); w.WriteEndArray();
        WriteAliases(w, x.Aliases);
        if (x.AlternateForm != null) WriteAlternateForm(w, x.AlternateForm);
        w.WriteEndObject();
    }
    private static void WriteAliases(Utf8JsonWriter w, IReadOnlyList<CharacterAliasSource>? aliases)
    {
        w.WritePropertyName("aliases"); w.WriteStartArray(); foreach (var a in aliases ?? System.Array.Empty<CharacterAliasSource>()) { w.WriteStartObject(); w.WriteString("from",a.From); w.WriteString("to",a.To); w.WriteEndObject(); } w.WriteEndArray();
    }
    private static void WriteAlternateForm(Utf8JsonWriter w, AlternateFormSource x)
    {
        w.WritePropertyName("alternateForm");
        w.WriteStartObject();
        w.WriteString("id", x.Id);
        w.WriteString("displayName", x.DisplayName);
        WriteMovement(w, x.Movement);
        w.WritePropertyName("presentation");
        w.WriteStartObject();
        foreach (var (name, id) in x.Presentation.Roles) OptionalString(w, name, id);
        w.WriteEndObject();
        w.WritePropertyName("slots"); w.WriteStartArray(); foreach (var slot in x.Slots ?? System.Array.Empty<CharacterSlotSource>()) WriteSlot(w, slot); w.WriteEndArray();
        WriteAliases(w, x.Aliases);
        w.WriteEndObject();
    }
    private static void WriteShell(Utf8JsonWriter w, ShellSource x)
    {
        w.WritePropertyName("shell");
        w.WriteStartObject();
        Number(w, "popOutDeceleration", x.PopOutDeceleration);
        Number(w, "blockedRecoilSpeed", x.BlockedRecoilSpeed);
        if (x.Splash is { } splash)
        {
            w.WritePropertyName("splash");
            w.WriteStartObject();
            WriteHitbox(w, splash.Hitbox);
            w.WriteNumber("popUpTicks", splash.PopUpTicks);
            if (splash.PresentationId != null) w.WriteString("presentationId", splash.PresentationId);
            w.WriteEndObject();
        }
        if (x.Ride is { } ride)
        {
            w.WritePropertyName("ride");
            w.WriteStartObject();
            w.WriteNumber("ticks", ride.Ticks);
            Number(w, "speed", ride.Speed);
            Number(w, "turnDegreesPerSecond", ride.TurnDegreesPerSecond);
            WriteHitboxNamed(w, ride.Wave, "wave");
            if (ride.HitPresentationId != null) w.WriteString("hitPresentationId", ride.HitPresentationId);
            w.WriteEndObject();
        }
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
        OptionalString(w, "shell", x.Shell);
        OptionalString(w, "flying", x.Flying);
        OptionalString(w, "mayhemLaugh", x.MayhemLaugh);
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
        if (x.AimMovementSpeedScale != 1f) Number(w, "aimMovementSpeedScale", x.AimMovementSpeedScale);
        if (x.AimAnimationId != null) w.WriteString("aimAnimationId", x.AimAnimationId);
        if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId);
        w.WriteNumber("cooldownTicks", x.CooldownTicks);
        w.WriteBoolean("isRecoveryMove", x.IsRecoveryMove);
        w.WriteBoolean("preserveMomentumOnStart", x.PreserveMomentumOnStart);
        w.WriteBoolean("allowSlideCarry", x.AllowSlideCarry);
        if (x.ShellPolicy != ShellPolicy.PopOut) w.WriteString("shellPolicy", x.ShellPolicy == ShellPolicy.Ride ? "ride" : "tuck");
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
                if (e.Condition != AuthoringPresentationCondition.Always)
                    w.WriteString("condition", PresentationConditionText(e.Condition));
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
    private static void WriteHitbox(Utf8JsonWriter w, HitboxSource x) { w.WritePropertyName("hitbox"); w.WriteStartObject(); w.WriteString("shape",ShapeText(x.Shape)); Number(w,"radius",x.Radius); Number(w,"offsetX",x.OffsetX); Number(w,"offsetY",x.OffsetY); Number(w,"offsetZ",x.OffsetZ); Number(w,"endOffsetX",x.EndOffsetX); Number(w,"endOffsetY",x.EndOffsetY); Number(w,"endOffsetZ",x.EndOffsetZ); if(x.StartBoneId == null) w.WriteNull("startBoneId"); else w.WriteString("startBoneId",x.StartBoneId); if(x.EndBoneId == null) w.WriteNull("endBoneId"); else w.WriteString("endBoneId",x.EndBoneId); Number(w,"damage",x.Damage); Number(w,"angle",x.Angle); Number(w,"baseKnockback",x.BaseKnockback); Number(w,"knockbackGrowth",x.KnockbackGrowth); w.WriteNumber("stunTicks",x.StunTicks); w.WriteNumber("durationTicks",x.DurationTicks); w.WriteBoolean("interruptible",x.Interruptible); w.WriteNumber("hitGroup",x.HitGroup); w.WriteString("knockbackDirection",KnockbackDirectionText(x.KnockbackDirection)); if (x.FixedHitstunTicks > 0) w.WriteNumber("fixedHitstunTicks", x.FixedHitstunTicks); if (x.RequiresFacingOwner) w.WriteBoolean("requiresFacingOwner", true); if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId); w.WriteEndObject(); }
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
        if (x.RequiresFacingOwner) w.WriteBoolean("requiresFacingOwner", true); if (x.HitPresentationId != null) w.WriteString("hitPresentationId", x.HitPresentationId);
        w.WriteEndObject();
    }
    private static void WriteProjectile(Utf8JsonWriter w, ProjectileSource x) { w.WritePropertyName("projectile"); w.WriteStartObject(); Number(w,"launchOffsetX",x.LaunchOffsetX); Number(w,"launchOffsetY",x.LaunchOffsetY); Number(w,"launchOffsetZ",x.LaunchOffsetZ); Number(w,"speed",x.Speed); Number(w,"gravity",x.Gravity); Number(w,"radius",x.Radius); Number(w,"damage",x.Damage); Number(w,"angle",x.Angle); Number(w,"baseKnockback",x.BaseKnockback); Number(w,"knockbackGrowth",x.KnockbackGrowth); w.WriteNumber("stunTicks",x.StunTicks); w.WriteNumber("maxFlightTicks",x.MaxFlightTicks); Number(w,"yawOffsetDegrees",x.YawOffsetDegrees); if (x.Return is { } back) { w.WritePropertyName("return"); w.WriteStartObject(); w.WriteNumber("outboundTicks",back.OutboundTicks); Number(w,"returnSpeed",back.ReturnSpeed); Number(w,"returnAcceleration",back.ReturnAcceleration); Number(w,"selfHitDamage",back.SelfHitDamage); w.WriteNumber("selfHitStunTicks",back.SelfHitStunTicks); OptionalString(w,"catchPresentationId",back.CatchPresentationId); OptionalString(w,"selfHitPresentationId",back.SelfHitPresentationId); w.WriteEndObject(); } WriteHomingAndPerch(w, x); w.WriteEndObject(); }

    private static void WriteHomingAndPerch(Utf8JsonWriter w, ProjectileSource x)
    {
        if (x.Homing is { } homing)
        {
            w.WritePropertyName("homing"); w.WriteStartObject();
            Number(w, "turnDegreesPerTick", homing.TurnDegreesPerTick);
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
    private static void WriteParameters(Utf8JsonWriter w, TypedCapabilityParameters x)
    {
        w.WriteStartObject();
        switch (x)
        {
            case AimedRecoveryCapabilityParameters p:
                w.WriteNumber("startupTicks", p.StartupTicks); Number(w, "verticalSpeed", p.VerticalSpeed);
                Number(w, "directionalVerticalSpeed", p.DirectionalVerticalSpeed); Number(w, "horizontalSpeed", p.HorizontalSpeed);
                Number(w, "preparationRiseSpeed", p.PreparationRiseSpeed);
                CharacterPackageCompiler.WriteRecoveryHop(w, p.Hop);
                if (p.RiseSteerAcceleration > 0f) Number(w, "riseSteerAcceleration", p.RiseSteerAcceleration); break;
            case ChargedProjectileCapabilityParameters p:
                w.WriteNumber("maxChargeTicks", p.MaxChargeTicks); w.WriteNumber("tier2Ticks", p.Tier2Ticks);
                w.WriteNumber("tier3Ticks", p.Tier3Ticks); w.WriteNumber("fireTriggerTick", p.FireTriggerTick);
                Number(w, "tier2Radius", p.Tier2Radius); Number(w, "tier3Radius", p.Tier3Radius);
                WriteProjectile(w, p.Projectile); break;
            case ChargedStreamCapabilityParameters p:
                w.WriteNumber("maxChargeTicks", p.MaxChargeTicks); w.WriteNumber("fireTriggerTick", p.FireTriggerTick);
                w.WriteNumber("emitIntervalTicks", p.EmitIntervalTicks); w.WriteNumber("minStreamTicks", p.MinStreamTicks);
                w.WriteNumber("maxStreamTicks", p.MaxStreamTicks); w.WriteNumber("recoveryTicks", p.RecoveryTicks);
                Number(w, "maxBaseKnockback", p.MaxBaseKnockback); Number(w, "minRecoilSpeed", p.MinRecoilSpeed);
                Number(w, "maxRecoilSpeed", p.MaxRecoilSpeed); WriteProjectile(w, p.Projectile);
                if (p.SprayAnimationId.Length > 0) w.WriteString("sprayAnimationId", p.SprayAnimationId);
                if (p.SprayLoopTicks > 0) w.WriteNumber("sprayLoopTicks", p.SprayLoopTicks);
                if (p.EndAnimationId.Length > 0) w.WriteString("endAnimationId", p.EndAnimationId);
                break;
            case FingerGunsCapabilityParameters p:
                w.WriteNumber("fireTriggerTick", p.FireTriggerTick); w.WriteNumber("recoveryTicks", p.RecoveryTicks);
                w.WriteNumber("shotsPerReload", p.ShotsPerReload); w.WriteNumber("reloadTicks", p.ReloadTicks);
                w.WriteString("reloadAnimationId", p.ReloadAnimationId);
                Number(w, "airMaxFallSpeed", p.AirMaxFallSpeed); Number(w, "airRecoilSpeed", p.AirRecoilSpeed);
                WriteProjectile(w, p.Projectile); break;
            case RisingDragonCapabilityParameters p: Number(w, "riseSpeed", p.RiseSpeed); w.WriteNumber("riseTicks", p.RiseTicks); w.WriteNumber("riseDelay", p.RiseDelay); break;
            case CycloneKickCapabilityParameters p: w.WriteNumber("forwardSpeed", p.ForwardSpeed); w.WriteNumber("windupTicks", p.WindupTicks); w.WriteNumber("hitboxEndTick", p.HitboxEndTick); w.WriteNumber("durationTicks", p.DurationTicks); Number(w, "bodyRadius", p.BodyRadius); Number(w, "sideRadius", p.SideRadius); Number(w, "sideOffset", p.SideOffset); Number(w, "damage", p.Damage); Number(w, "knockbackAngle", p.KnockbackAngle); Number(w, "knockbackBase", p.KnockbackBase); Number(w, "knockbackGrowth", p.KnockbackGrowth); w.WriteNumber("stunTicks", p.StunTicks); Number(w, "bodyY", p.BodyY); Number(w, "sideY", p.SideY); CharacterPackageCompiler.WriteCycloneCarry(w, p.HitIntervalTicks, p.FixedHitstunTicks, p.FinisherTick, p.FinisherDamage, p.FinisherKnockbackAngle, p.FinisherKnockbackBase, p.FinisherKnockbackGrowth); break;
            case DashStrikeCapabilityParameters p:
                Number(w, "distance", p.Distance); Number(w, "speed", p.Speed);
                w.WriteNumber("tipPoseTick", p.TipPoseTick); w.WriteNumber("recoveryTicks", p.RecoveryTicks);
                WriteHitboxNamed(w, p.LightHitbox, "lightHitbox"); WriteHitboxNamed(w, p.TipHitbox, "tipHitbox");
                w.WriteString("tipPresentationId", p.TipPresentationId); break;
            case WibouRisingSlashCapabilityParameters p: Number(w, "riseSpeed", p.RiseSpeed); w.WriteNumber("riseTicks", p.RiseTicks); Number(w, "homingRange", p.HomingRange); Number(w, "homingSpeed", p.HomingSpeed); break;
            case FlashCutCapabilityParameters p:
                w.WriteNumber("stanceTicks", p.StanceTicks); Number(w, "lockRange", p.LockRange);
                w.WriteNumber("maxTargets", p.MaxTargets); w.WriteNumber("legTicks", p.LegTicks);
                Number(w, "passThroughDistance", p.PassThroughDistance); w.WriteNumber("sheatheTicks", p.SheatheTicks);
                w.WriteNumber("revealLeadTicks", p.RevealLeadTicks); w.WriteNumber("recoveryTicks", p.RecoveryTicks);
                w.WriteNumber("whiffTicks", p.WhiffTicks); WriteHitboxNamed(w, p.CutHitbox, "cutHitbox");
                w.WriteString("stanceAnimationId", p.StanceAnimationId); w.WriteString("legAnimationId", p.LegAnimationId);
                w.WriteString("sheatheAnimationId", p.SheatheAnimationId);
                w.WriteString("lockPresentationId", p.LockPresentationId); w.WriteString("revealPresentationId", p.RevealPresentationId);
                w.WriteString("sheathePresentationId", p.SheathePresentationId); w.WriteString("popPresentationId", p.PopPresentationId);
                break;
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
            case SteeredChargeCapabilityParameters p:
                Number(w, "gallopSpeed", p.GallopSpeed); Number(w, "turnDegreesPerSecond", p.TurnDegreesPerSecond);
                w.WriteNumber("maxGallopTicks", p.MaxGallopTicks); w.WriteBoolean("armored", p.Armored);
                WriteHitboxNamed(w, p.LanceHitbox, "lanceHitbox");
                w.WriteNumber("hitEndingTicks", p.HitEndingTicks); w.WriteNumber("missEndingTicks", p.MissEndingTicks);
                w.WriteNumber("skidTicks", p.SkidTicks); w.WriteBoolean("turnAroundOnMiss", p.TurnAroundOnMiss);
                w.WriteNumber("airStrideTicks", p.AirStrideTicks); w.WriteNumber("lookDownTicks", p.LookDownTicks);
                OptionalString(w, "gallopAnimationId", p.GallopAnimationId);
                if (p.GallopLoopTicks != 0) w.WriteNumber("gallopLoopTicks", p.GallopLoopTicks);
                OptionalString(w, "hitEndingAnimationId", p.HitEndingAnimationId);
                OptionalString(w, "missEndingAnimationId", p.MissEndingAnimationId);
                OptionalString(w, "airEndingAnimationId", p.AirEndingAnimationId);
                OptionalString(w, "gallopPresentationId", p.GallopPresentationId);
                OptionalString(w, "hitEndingPresentationId", p.HitEndingPresentationId);
                OptionalString(w, "missEndingPresentationId", p.MissEndingPresentationId);
                OptionalString(w, "airEndingPresentationId", p.AirEndingPresentationId);
                break;
            case ThrustCapabilityParameters p:
                w.WriteString("aim", p.Aim == ThrustAim.TargetAhead ? "targetAhead" : "stick");
                if (p.Aim == ThrustAim.TargetAhead) Number(w, "targetConeDegrees", p.TargetConeDegrees);
                Number(w, "neutralPitchDegrees", p.NeutralPitchDegrees); Number(w, "stickPitchDegrees", p.StickPitchDegrees);
                Number(w, "speed", p.Speed); Number(w, "acceleration", p.Acceleration); Number(w, "maxSpeed", p.MaxSpeed);
                w.WriteBoolean("keepIncomingSpeed", p.KeepIncomingSpeed); w.WriteNumber("thrustTicks", p.ThrustTicks);
                if (p.TrailHitbox != null) WriteHitboxNamed(w, p.TrailHitbox, "trailHitbox");
                break;
            case CannonballCapabilityParameters p:
                w.WriteNumber("takeoffTick", p.TakeoffTick);
                Number(w, "maxRange", p.MaxRange); Number(w, "noTargetDistance", p.NoTargetDistance);
                w.WriteNumber("minFlightTicks", p.MinFlightTicks); w.WriteNumber("maxFlightTicks", p.MaxFlightTicks);
                Number(w, "maxLaunchSpeed", p.MaxLaunchSpeed);
                OptionalString(w, "launchPresentationId", p.LaunchPresentationId);
                break;
            case TetherCapabilityParameters p:
                w.WriteNumber("throwTick", p.ThrowTick);
                if (p.Projectile != null) WriteProjectile(w, p.Projectile);
                Number(w, "reelSpeed", p.ReelSpeed); Number(w, "anchorDistance", p.AnchorDistance);
                w.WriteNumber("arrivalStunTicks", p.ArrivalStunTicks); w.WriteNumber("maxReelTicks", p.MaxReelTicks);
                if (p.AtTarget == null) Number(w, "retractSpeed", p.RetractSpeed);
                w.WriteNumber("missRecoveryTicks", p.MissRecoveryTicks);
                Number(w, "airGravityScale", p.AirGravityScale);
                OptionalString(w, "reelAnimationId", p.ReelAnimationId);
                OptionalString(w, "retractAnimationId", p.RetractAnimationId);
                OptionalString(w, "throwPresentationId", p.ThrowPresentationId);
                OptionalString(w, "retractPresentationId", p.RetractPresentationId);
                if (p.AtTarget is { } at)
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
            case CommandGrabCapabilityParameters p:
                w.WriteNumber("lungeTicks", p.LungeTicks); Number(w, "lungeSpeed", p.LungeSpeed);
                Number(w, "stopDistance", p.StopDistance); w.WriteNumber("captureTick", p.CaptureTick);
                WriteHitboxNamed(w, p.Capture, "capture"); w.WriteNumber("releaseTick", p.ReleaseTick);
                if (p.Launch is { } launch)
                {
                    w.WritePropertyName("launch"); w.WriteStartObject();
                    Number(w, "damage", launch.Damage); Number(w, "angle", launch.Angle);
                    Number(w, "baseKnockback", launch.BaseKnockback); Number(w, "knockbackGrowth", launch.KnockbackGrowth);
                    w.WriteNumber("stunTicks", launch.StunTicks); w.WriteEndObject();
                }
                OptionalString(w, "catchPresentationId", p.CatchPresentationId);
                OptionalString(w, "releasePresentationId", p.ReleasePresentationId);
                if (p.Level) w.WriteBoolean("level", true);
                if (p.AnchorDistance is { } anchor) Number(w, "anchorDistance", anchor);
                if (p.ReleaseLockTicks != 0) w.WriteNumber("releaseLockTicks", p.ReleaseLockTicks);
                if (p.WhiffRecoveryTicks != 0) w.WriteNumber("whiffRecoveryTicks", p.WhiffRecoveryTicks);
                break;
            case MankiRoundBombCapabilityParameters p: w.WriteNumber("armTicks", p.ArmTicks); w.WriteNumber("maxFlightTicks", p.MaxFlightTicks); Number(w, "gravity", p.Gravity); Number(w, "hitboxRadius", p.HitboxRadius); Number(w, "explosionDamage", p.ExplosionDamage); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); Number(w, "explosionKbAngle", p.ExplosionKbAngle); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); Number(w, "triggerRadius", p.TriggerRadius); w.WriteNumber("warningTicks", p.WarningTicks); w.WriteNumber("lifetimeTicks", p.LifetimeTicks); w.WriteNumber("maxMines", p.MaxMines); break;
            case MankiJetpackBoostCapabilityParameters p: w.WriteNumber("startupTicks", p.StartupTicks); Number(w, "verticalSpeed", p.VerticalSpeed); Number(w, "horizontalSpeed", p.HorizontalSpeed); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "explosionDamage", p.ExplosionDamage); Number(w, "explosionKbAngle", p.ExplosionKbAngle); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); w.WriteNumber("fuelTicks", p.FuelTicks); Number(w, "sinkSpeed", p.SinkSpeed); Number(w, "jumpBurstSpeed", p.JumpBurstSpeed); w.WriteNumber("jumpBurstTicks", p.JumpBurstTicks); break;
            case MankiBazookaCapabilityParameters p: if (p.ChargeTicks > 0) w.WriteNumber("chargeTicks", p.ChargeTicks); Number(w, "homingTurnDegPerTick", p.HomingTurnDegPerTick); w.WriteNumber("fireTriggerTick", p.FireTriggerTick); Number(w, "projectileSpeed", p.ProjectileSpeed); Number(w, "hitboxRadius", p.HitboxRadius); Number(w, "damage", p.Damage); Number(w, "gravity", p.Gravity); w.WriteNumber("maxFlightTicks", p.MaxFlightTicks); w.WriteNumber("stunTicks", p.StunTicks); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "kbAngle", p.KbAngle); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); Number(w, "explosionKbAngle", p.ExplosionKbAngle); w.WriteNumber("castDuration", p.CastDuration); w.WriteNumber("recoveryDuration", p.RecoveryDuration); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); break;
            case MankiKamikazeCapabilityParameters p: w.WriteNumber("runTicks", p.RunTicks); Number(w, "speed", p.Speed); Number(w, "contactRadius", p.ContactRadius); Number(w, "explosionDamage", p.ExplosionDamage); Number(w, "explosionRadius", p.ExplosionRadius); Number(w, "explosionKbBase", p.ExplosionKbBase); Number(w, "explosionKbGrowth", p.ExplosionKbGrowth); w.WriteNumber("explosionStunTicks", p.ExplosionStunTicks); w.WriteNumber("explosionDurationTicks", p.ExplosionDurationTicks); Number(w, "explosionKbAngle", p.ExplosionKbAngle); Number(w, "selfPushSpeed", p.SelfPushSpeed); OptionalString(w, "explosionPresentationId", p.ExplosionPresentationId); break;
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
    private static string VelocityText(AuthoringVelocityMode value)=>value switch { AuthoringVelocityMode.Absolute=>"absolute", AuthoringVelocityMode.Additive=>"additive", AuthoringVelocityMode.Vertical=>"vertical", _=>throw new InvalidDataException("Unknown velocity mode.") };
    private static string UnitText(AuthoringUnit value)=>value switch { AuthoringUnit.Meters=>"meters", AuthoringUnit.MetersPerSecond=>"metersPerSecond", AuthoringUnit.MetersPerSecondSquared=>"metersPerSecondSquared", AuthoringUnit.Degrees=>"degrees", AuthoringUnit.Normalized=>"normalized", AuthoringUnit.Damage=>"damage", AuthoringUnit.Knockback=>"knockback", AuthoringUnit.Ticks=>"ticks", _=>throw new InvalidDataException("Unknown unit.") };
    private static string OperationKind(CharacterTimelineOperationSource value)=>value switch { SetVelocityOperationSource=>"setVelocity", ForwardLungeOperationSource=>"forwardLunge", GravityWindowOperationSource=>"gravityWindow", ArmorWindowOperationSource=>"armorWindow", SpawnHitboxOperationSource=>"spawnHitbox", SpawnProjectileOperationSource=>"spawnProjectile", SetAimStateOperationSource=>"setAimState", StartCapabilityOperationSource=>"startCapability", EmitPresentationOperationSource=>"emitPresentation", CompleteTimelineOperationSource=>"completeTimeline", SwapFormOperationSource=>"swapForm", StartupAimCorrectionOperationSource=>"startupAimCorrection", _=>throw new InvalidDataException("Unknown operation.") };

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
        var p = ReadObject(root, "character", d, "authoringSchemaVersion", "displayName", "weight", "movement", "presentation", "capsuleRadius", "capsuleHeight", "hipHeight", "hurtboxRadius", "shieldRadius", "captureGeometry", "hurtboxCapsules", "hurtboxBoneDefs", "attachmentBoneIds", "presentationIds", "capabilityRequirements", "shell", "slots", "aliases", "alternateForm", "schemaVersion", "id", "class");
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
            Float(p, "shieldRadius", "character.shieldRadius", d), ParseCaptureGeometry(p, d), capsules, bones, attachmentBoneIds, presentationIds, capabilities, slots, aliases,
            p.ContainsKey("shell") ? ParseShell(p, d) : null,
            p.ContainsKey("alternateForm") ? ParseAlternateForm(p, d) : null);
    }

    private static AlternateFormSource ParseAlternateForm(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        const string path = "character.alternateForm";
        var p = Object(parent, "alternateForm", path, d, "id", "displayName", "movement", "presentation", "slots", "aliases");
        var roles = new FormPresentation().Roles.Select(role => role.Name).ToArray();
        var q = Object(p, "presentation", path + ".presentation", d, roles);
        string Role(string name) => OptionalString(q, name, path + ".presentation." + name, d) ?? "";
        return new AlternateFormSource(
            String(p, "id", path + ".id", d),
            String(p, "displayName", path + ".displayName", d),
            ParseMovement(p, d, path),
            new FormPresentation(Role("idle"), Role("run"), Role("dash"), Role("jump"), Role("fall"),
                Role("hitSmall"), Role("hitMedium"), Role("hitHard"), Role("tumble"), Role("shield"), Role("grab"),
                Role("grabbed"), Role("throwForward"), Role("airDodge")),
            ParseSlots(p, d, path),
            ParseAliases(p, d, path));
    }

    private static ShellSource ParseShell(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var p = Object(parent, "shell", "character.shell", d, "popOutDeceleration", "blockedRecoilSpeed", "splash", "ride");
        ShellSplashSource? splash = null;
        if (p.ContainsKey("splash"))
        {
            var q = Object(p, "splash", "character.shell.splash", d, "hitbox", "popUpTicks", "presentationId");
            splash = new ShellSplashSource(
                ParseHitboxNamed(q, "hitbox", "character.shell.splash.hitbox", d),
                UShort(q, "popUpTicks", "character.shell.splash.popUpTicks", d),
                OptionalString(q, "presentationId", "character.shell.splash.presentationId", d));
        }
        ShellRideSource? ride = null;
        if (p.ContainsKey("ride"))
        {
            var q = Object(p, "ride", "character.shell.ride", d, "ticks", "speed", "turnDegreesPerSecond", "wave",
                "hitPresentationId");
            ride = new ShellRideSource(
                UShort(q, "ticks", "character.shell.ride.ticks", d),
                Float(q, "speed", "character.shell.ride.speed", d),
                Float(q, "turnDegreesPerSecond", "character.shell.ride.turnDegreesPerSecond", d),
                ParseHitboxNamed(q, "wave", "character.shell.ride.wave", d),
                OptionalString(q, "hitPresentationId", "character.shell.ride.hitPresentationId", d));
        }
        return new ShellSource(
            Float(p, "popOutDeceleration", "character.shell.popOutDeceleration", d),
            Float(p, "blockedRecoilSpeed", "character.shell.blockedRecoilSpeed", d),
            splash, ride);
    }

    private static CharacterMovementSource ParseMovement(Dictionary<string, JsonElement> parent, DiagnosticBag d, string path = "character")
    {
        var p = Object(parent, "movement", path + ".movement", d, "runSpeed", "runAccelerationA", "runAccelerationB", "dashSpeed", "airDodgeSpeed", "airSpeedMax", "airAccelStick", "airAccelBase", "jumpForce", "shortHopForce", "airJumpVMultiplier", "airJumpHMultiplier", "gravity", "airFloatGravity", "dashDurationTicks", "dashCooldownTicks", "groundFriction", "airFriction", "maxFallSpeed", "fastFallSpeed", "maxJumps", "jumpSquatTicks", "floatWindowTicks", "rushTicks");
        return new CharacterMovementSource(
            Float(p, "runSpeed", path + ".movement.runSpeed", d), Float(p, "runAccelerationA", path + ".movement.runAccelerationA", d), Float(p, "runAccelerationB", path + ".movement.runAccelerationB", d),
            Float(p, "dashSpeed", path + ".movement.dashSpeed", d), Float(p, "airDodgeSpeed", path + ".movement.airDodgeSpeed", d), Float(p, "airSpeedMax", path + ".movement.airSpeedMax", d), Float(p, "airAccelStick", path + ".movement.airAccelStick", d), Float(p, "airAccelBase", path + ".movement.airAccelBase", d),
            Float(p, "jumpForce", path + ".movement.jumpForce", d), Float(p, "shortHopForce", path + ".movement.shortHopForce", d), Float(p, "airJumpVMultiplier", path + ".movement.airJumpVMultiplier", d), Float(p, "airJumpHMultiplier", path + ".movement.airJumpHMultiplier", d),
            Float(p, "gravity", path + ".movement.gravity", d), Float(p, "airFloatGravity", path + ".movement.airFloatGravity", d), UShort(p, "dashDurationTicks", path + ".movement.dashDurationTicks", d), UShort(p, "dashCooldownTicks", path + ".movement.dashCooldownTicks", d),
            Float(p, "groundFriction", path + ".movement.groundFriction", d), Float(p, "airFriction", path + ".movement.airFriction", d), Float(p, "maxFallSpeed", path + ".movement.maxFallSpeed", d), Float(p, "fastFallSpeed", path + ".movement.fastFallSpeed", d),
            Byte(p, "maxJumps", path + ".movement.maxJumps", d), UShort(p, "jumpSquatTicks", path + ".movement.jumpSquatTicks", d), UShort(p, "floatWindowTicks", path + ".movement.floatWindowTicks", d), UShort(p, "rushTicks", path + ".movement.rushTicks", d));
    }

    private static CharacterPresentationSource ParsePresentation(Dictionary<string, JsonElement> parent, DiagnosticBag d)
    {
        var p = Object(parent, "presentation", "character.presentation", d, "idle", "run", "dash", "jump", "fall", "hitSmall", "hitMedium", "hitHard", "tumble", "crouch", "slide", "shield", "grab", "grabbed", "throwForward", "airDodge", "shell", "flying", "mayhemLaugh", "landStartOffsetSeconds", "modelResourcePath", "visualScale", "hurtboxBoneScale", "modelYOffset", "modelSoleOffset", "autoModelYOffset");
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
            OptionalString(p, "airDodge", "character.presentation.airDodge", d) ?? "",
            OptionalString(p, "shell", "character.presentation.shell", d) ?? "",
            OptionalString(p, "flying", "character.presentation.flying", d) ?? "",
            OptionalString(p, "mayhemLaugh", "character.presentation.mayhemLaugh", d) ?? "");
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
            var p = ReadObject(e, $"character.hurtboxBoneDefs[{i}]", d, "boneId", "offsetX", "offsetY", "offsetZ", "radius", "endBoneId", "endOffsetX", "endOffsetY", "endOffsetZ");
            result.Add(new HurtboxBoneSource(String(p, "boneId", $"character.hurtboxBoneDefs[{i}].boneId", d), Float(p, "offsetX", $"character.hurtboxBoneDefs[{i}].offsetX", d), Float(p, "offsetY", $"character.hurtboxBoneDefs[{i}].offsetY", d), Float(p, "offsetZ", $"character.hurtboxBoneDefs[{i}].offsetZ", d), Float(p, "radius", $"character.hurtboxBoneDefs[{i}].radius", d), OptionalString(p, "endBoneId", $"character.hurtboxBoneDefs[{i}].endBoneId", d), OptionalFloat(p, "endOffsetX", $"character.hurtboxBoneDefs[{i}].endOffsetX", d), OptionalFloat(p, "endOffsetY", $"character.hurtboxBoneDefs[{i}].endOffsetY", d), OptionalFloat(p, "endOffsetZ", $"character.hurtboxBoneDefs[{i}].endOffsetZ", d)));
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

    private static List<CharacterAliasSource> ParseAliases(Dictionary<string, JsonElement> parent, DiagnosticBag d, string path = "character")
    {
        var result = new List<CharacterAliasSource>();
        if (!Array(parent, "aliases", path + ".aliases", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var p = ReadObject(e, $"{path}.aliases[{i}]", d, "from", "to");
            result.Add(new CharacterAliasSource(String(p, "from", $"{path}.aliases[{i}].from", d), String(p, "to", $"{path}.aliases[{i}].to", d)));
            i++;
        }
        return result;
    }

    private static List<CharacterSlotSource> ParseSlots(Dictionary<string, JsonElement> parent, DiagnosticBag d, string root = "character")
    {
        var result = new List<CharacterSlotSource>();
        if (!Array(parent, "slots", root + ".slots", d, out var a)) return result;
        var i = 0;
        foreach (var e in a.EnumerateArray())
        {
            var path = $"{root}.slots[{i}]";
            var p = ReadObject(e, path, d, "id", "name", "description", "iconId", "behavior", "aimMode", "aimMovement", "aimMovementSpeedScale", "aimAnimationId", "hitPresentationId", "cooldownTicks", "isRecoveryMove", "preserveMomentumOnStart", "allowSlideCarry", "shellPolicy", "chargePool", "timeline");
            var chargePool = p.TryGetValue("chargePool", out var chargeElement) && chargeElement.ValueKind != JsonValueKind.Null
                ? ParseChargePool(chargeElement, path + ".chargePool", d)
                : null;
            result.Add(new CharacterSlotSource(String(p, "id", path + ".id", d), String(p, "name", path + ".name", d), String(p, "description", path + ".description", d), String(p, "iconId", path + ".iconId", d), EnumValue(p, "behavior", path + ".behavior", d, ParseBehavior), EnumValue(p, "aimMode", path + ".aimMode", d, ParseAimMode), UShort(p, "cooldownTicks", path + ".cooldownTicks", d), Bool(p, "isRecoveryMove", path + ".isRecoveryMove", d), Bool(p, "preserveMomentumOnStart", path + ".preserveMomentumOnStart", d), ParseTimeline(p, path, d), chargePool, OptionalEnumValue(p, "aimMovement", path + ".aimMovement", d, ParseAimMovement, AuthoringAimMovementMode.Fixed), OptionalString(p, "aimAnimationId", path + ".aimAnimationId", d), OptionalBool(p, "allowSlideCarry", path + ".allowSlideCarry", d), OptionalString(p, "hitPresentationId", path + ".hitPresentationId", d),
                p.ContainsKey("aimMovementSpeedScale") ? Float(p, "aimMovementSpeedScale", path + ".aimMovementSpeedScale", d) : 1f,
                OptionalEnumValue(p, "shellPolicy", path + ".shellPolicy", d, ParseShellPolicy, ShellPolicy.PopOut)));
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
            var p = ReadObject(e, opPath, d, "kind", "tick", "unit", "velocityMode", "x", "y", "z", "speed", "durationTicks", "stopInAttackRange", "gravityScale", "hitbox", "projectile", "aimState", "capabilityId", "capabilityVersion", "parameters", "presentationId", "placement", "condition", "endTick", "acquisitionRange", "acquisitionHalfAngleDegrees", "maxYawDegrees", "maxPitchDegrees", "yawDegreesPerSecond", "pitchDegreesPerSecond");
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
                case "emitPresentation": result.Add(new EmitPresentationOperationSource(tick, unit, String(p, "presentationId", opPath + ".presentationId", d), ParsePlacement(p, opPath, d), OptionalEnumValue(p, "condition", opPath + ".condition", d, ParsePresentationCondition, AuthoringPresentationCondition.Always))); break;
                case "completeTimeline": result.Add(new CompleteTimelineOperationSource(tick, unit)); break;
                case "swapForm": result.Add(new SwapFormOperationSource(tick, unit)); break;
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
            "emitPresentation" => new[] { "kind", "tick", "unit", "presentationId", "placement", "condition" },
            "completeTimeline" => new[] { "kind", "tick", "unit" },
            "swapForm" => new[] { "kind", "tick", "unit" },
            "startupAimCorrection" => new[] { "kind", "tick", "unit", "endTick", "acquisitionRange", "acquisitionHalfAngleDegrees", "maxYawDegrees", "maxPitchDegrees", "yawDegreesPerSecond", "pitchDegreesPerSecond" },
            _ => System.Array.Empty<string>(),
        };
        if (allowed.Length == 0) return;
        foreach (var name in properties.Keys) if (!allowed.Contains(name, StringComparer.Ordinal)) d.Error("operation.parameter-unknown", path + "." + name, "Field is not valid for this operation.");
        foreach (var name in allowed)
            if (!properties.ContainsKey(name) && !(kind == "emitPresentation" && name is "placement" or "condition")
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
        var p = Object(parent, "hitbox", path + ".hitbox", d, "shape", "radius", "offsetX", "offsetY", "offsetZ", "endOffsetX", "endOffsetY", "endOffsetZ", "startBoneId", "endBoneId", "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks", "durationTicks", "interruptible", "hitGroup", "knockbackDirection", "fixedHitstunTicks", "hitPresentationId", "requiresFacingOwner");
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
            OptionalUShort(p, "fixedHitstunTicks", path + ".hitbox.fixedHitstunTicks", d, 0),
            OptionalString(p, "hitPresentationId", path + ".hitbox.hitPresentationId", d),
            OptionalBool(p, "requiresFacingOwner", path + ".hitbox.requiresFacingOwner", d));
    }

    private static ProjectileSource ParseProjectile(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var p = Object(parent, "projectile", path + ".projectile", d, "launchOffsetX", "launchOffsetY", "launchOffsetZ", "speed", "gravity", "radius", "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks", "maxFlightTicks", "yawOffsetDegrees", "return", "homing", "perch");
        ProjectileReturnSource? back = null;
        if (p.ContainsKey("return"))
        {
            var r = Object(p, "return", path + ".projectile.return", d, "outboundTicks", "returnSpeed", "returnAcceleration",
                "selfHitDamage", "selfHitStunTicks", "catchPresentationId", "selfHitPresentationId");
            string rp = path + ".projectile.return.";
            back = new ProjectileReturnSource(UShort(r, "outboundTicks", rp + "outboundTicks", d),
                Float(r, "returnSpeed", rp + "returnSpeed", d), Float(r, "returnAcceleration", rp + "returnAcceleration", d),
                Float(r, "selfHitDamage", rp + "selfHitDamage", d), UShort(r, "selfHitStunTicks", rp + "selfHitStunTicks", d),
                OptionalString(r, "catchPresentationId", rp + "catchPresentationId", d) ?? "",
                OptionalString(r, "selfHitPresentationId", rp + "selfHitPresentationId", d) ?? "");
        }
        return new ProjectileSource(Float(p, "launchOffsetX", path + ".projectile.launchOffsetX", d), Float(p, "launchOffsetY", path + ".projectile.launchOffsetY", d), Float(p, "launchOffsetZ", path + ".projectile.launchOffsetZ", d), Float(p, "speed", path + ".projectile.speed", d), Float(p, "gravity", path + ".projectile.gravity", d), Float(p, "radius", path + ".projectile.radius", d), Float(p, "damage", path + ".projectile.damage", d), Float(p, "angle", path + ".projectile.angle", d), Float(p, "baseKnockback", path + ".projectile.baseKnockback", d), Float(p, "knockbackGrowth", path + ".projectile.knockbackGrowth", d), UShort(p, "stunTicks", path + ".projectile.stunTicks", d), UShort(p, "maxFlightTicks", path + ".projectile.maxFlightTicks", d), Float(p, "yawOffsetDegrees", path + ".projectile.yawOffsetDegrees", d), back,
            ParseHoming(p, path + ".projectile.homing", d), ParsePerch(p, path + ".projectile.perch", d));
    }

    private static ProjectileHomingSource? ParseHoming(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        if (!p.ContainsKey("homing")) return null;
        var h = Object(p, "homing", path, d, "turnDegreesPerTick");
        return new ProjectileHomingSource(Float(h, "turnDegreesPerTick", path + ".turnDegreesPerTick", d));
    }

    private static ProjectilePerchSource? ParsePerch(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        if (!p.ContainsKey("perch")) return null;
        var perch = Object(p, "perch", path, d, "perchTicks", "pop", "dive", "peck", "scatterPresentationId");
        var pop = Object(perch, "pop", path + ".pop", d, "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks",
            "presentationId");
        string pp = path + ".pop.";
        ProjectileDiveSource? dive = null;
        if (perch.ContainsKey("dive"))
        {
            var v = Object(perch, "dive", path + ".dive", d, "hoverHeight", "maxRiseTicks", "markTicks", "diveSpeed",
                "noTargetDistance");
            string vp = path + ".dive.";
            dive = new ProjectileDiveSource(Float(v, "hoverHeight", vp + "hoverHeight", d),
                UShort(v, "maxRiseTicks", vp + "maxRiseTicks", d), UShort(v, "markTicks", vp + "markTicks", d),
                Float(v, "diveSpeed", vp + "diveSpeed", d), Float(v, "noTargetDistance", vp + "noTargetDistance", d));
        }
        ProjectilePeckSource? peck = null;
        if (perch.ContainsKey("peck"))
        {
            var k = Object(perch, "peck", path + ".peck", d, "intervalTicks", "damage", "presentationId");
            string kp = path + ".peck.";
            peck = new ProjectilePeckSource(UShort(k, "intervalTicks", kp + "intervalTicks", d), Float(k, "damage", kp + "damage", d),
                OptionalString(k, "presentationId", kp + "presentationId", d) ?? "");
        }
        return new ProjectilePerchSource(UShort(perch, "perchTicks", path + ".perchTicks", d), new ProjectilePopSource(
            Float(pop, "damage", pp + "damage", d), Float(pop, "angle", pp + "angle", d),
            Float(pop, "baseKnockback", pp + "baseKnockback", d), Float(pop, "knockbackGrowth", pp + "knockbackGrowth", d),
            UShort(pop, "stunTicks", pp + "stunTicks", d), OptionalString(pop, "presentationId", pp + "presentationId", d) ?? ""),
            dive, peck, OptionalString(perch, "scatterPresentationId", path + ".scatterPresentationId", d) ?? "");
    }

    private static TypedCapabilityParameters ParseCapabilityParameters(Dictionary<string, JsonElement> parent, string path, DiagnosticBag d)
    {
        var id = String(parent, "capabilityId", path + ".capabilityId", d);
        if (RetiredCapabilities.TryGetReason(id, out var retiredReason))
        {
            d.Error("capability.retired", path + ".capabilityId", retiredReason);
            return new RisingDragonCapabilityParameters(0, 0, 0);
        }
        if (!parent.TryGetValue("parameters", out var element)) { d.Error("operation.parameter-missing", path + ".parameters", "Capability parameters are required."); return new RisingDragonCapabilityParameters(0, 0, 0); }
        var allowed = id switch
        {
            "slop.ability.aimed-recovery.v1" => new[] { "startupTicks", "verticalSpeed", "directionalVerticalSpeed", "horizontalSpeed", "preparationRiseSpeed", "hop", "riseSteerAcceleration" },
            "slop.ability.charged-projectile.v1" => new[] { "maxChargeTicks", "tier2Ticks", "tier3Ticks", "fireTriggerTick", "tier2Radius", "tier3Radius", "projectile" },
            "slop.ability.charged-stream.v1" => new[] { "maxChargeTicks", "fireTriggerTick", "emitIntervalTicks", "minStreamTicks", "maxStreamTicks", "recoveryTicks", "maxBaseKnockback", "minRecoilSpeed", "maxRecoilSpeed", "projectile", "sprayAnimationId", "sprayLoopTicks", "endAnimationId" },
            "slop.ability.finger-guns.v1" => new[] { "fireTriggerTick", "recoveryTicks", "shotsPerReload", "reloadTicks", "reloadAnimationId", "airMaxFallSpeed", "airRecoilSpeed", "projectile" },
            "slop.internal.fightguy.rising-dragon.v1" => new[] { "riseSpeed", "riseTicks", "riseDelay" },
            "slop.internal.fightguy.cyclone-kick.v1" => new[] { "forwardSpeed", "windupTicks", "hitboxEndTick", "durationTicks", "bodyRadius", "sideRadius", "sideOffset", "damage", "knockbackAngle", "knockbackBase", "knockbackGrowth", "stunTicks", "bodyY", "sideY", "hitIntervalTicks", "fixedHitstunTicks", "finisherTick", "finisherDamage", "finisherKnockbackAngle", "finisherKnockbackBase", "finisherKnockbackGrowth" },
            "slop.ability.dash-strike.v1" => new[] { "distance", "speed", "tipPoseTick", "recoveryTicks", "lightHitbox", "tipHitbox", "tipPresentationId" },
            "slop.internal.wibou.rising-slash.v1" => new[] { "riseSpeed", "riseTicks", "homingRange", "homingSpeed" },
            CharacterPackageCompiler.FlashCutCapabilityId => FlashCutFields,
            "slop.ability.targeted-leap.v1" => new[] { "maxAimTicks", "maxFlightTicks", "minRange", "maxRange", "launchVerticalSpeed", "landingSeekTick", "recoveryTicks", "hitbox", "launchPresentationId", "landingPresentationId" },
            "slop.ability.steered-charge.v1" => SteeredChargeFields,
            "slop.ability.thrust.v1" => ThrustFields,
            "slop.ability.tether.v1" => TetherFields,
            CharacterPackageCompiler.CommandGrabCapabilityId => new[] { "lungeTicks", "lungeSpeed", "stopDistance", "captureTick", "capture", "releaseTick", "launch", "catchPresentationId", "releasePresentationId", "level", "anchorDistance", "releaseLockTicks", "whiffRecoveryTicks" },
            "slop.ability.cannonball.v1" => CannonballFields,
            "slop.internal.manki.proximity-mine.v1" => new[] { "armTicks", "maxFlightTicks", "gravity", "hitboxRadius", "explosionDamage", "explosionRadius", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "explosionPresentationId", "triggerRadius", "warningTicks", "lifetimeTicks", "maxMines" },
            "slop.internal.manki.jetpack-boost.v3" => new[] { "startupTicks", "verticalSpeed", "horizontalSpeed", "explosionRadius", "explosionDamage", "explosionKbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionPresentationId", "fuelTicks", "sinkSpeed", "jumpBurstSpeed", "jumpBurstTicks" },
            "slop.internal.manki.kamikaze.v1" => new[] { "runTicks", "speed", "contactRadius", "explosionDamage", "explosionRadius", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "selfPushSpeed", "explosionPresentationId" },
            "slop.internal.manki.bazooka.v2" => new[] { "homingTurnDegPerTick", "fireTriggerTick", "projectileSpeed", "hitboxRadius", "damage", "gravity", "maxFlightTicks", "stunTicks", "explosionRadius", "kbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "castDuration", "recoveryDuration", "explosionPresentationId" },
            "slop.internal.manki.bazooka.v3" => new[] { "chargeTicks", "homingTurnDegPerTick", "fireTriggerTick", "projectileSpeed", "hitboxRadius", "damage", "gravity", "maxFlightTicks", "stunTicks", "explosionRadius", "kbAngle", "explosionKbBase", "explosionKbGrowth", "explosionStunTicks", "explosionDurationTicks", "explosionKbAngle", "castDuration", "recoveryDuration", "explosionPresentationId" },

            _ => System.Array.Empty<string>(),
        };
        var p = ReadObjectWithCode(element, path + ".parameters", d, "operation.parameter-unknown", allowed);
        if (id == CharacterPackageCompiler.SteeredChargeCapabilityId)
            return ParseSteeredCharge(p, path + ".parameters", d);
        if (id == CharacterPackageCompiler.ThrustCapabilityId)
            return ParseThrust(p, path + ".parameters", d);
        if (id == CharacterPackageCompiler.TetherCapabilityId)
            return ParseTether(p, path + ".parameters", d);
        if (id == CharacterPackageCompiler.CommandGrabCapabilityId)
        {
            string at = path + ".parameters.";
            GrabLaunchSource? launch = null;
            if (p.ContainsKey("launch"))
            {
                var l = Object(p, "launch", at + "launch", d, "damage", "angle", "baseKnockback", "knockbackGrowth", "stunTicks");
                launch = new GrabLaunchSource(Float(l, "damage", at + "launch.damage", d), Float(l, "angle", at + "launch.angle", d),
                    Float(l, "baseKnockback", at + "launch.baseKnockback", d),
                    Float(l, "knockbackGrowth", at + "launch.knockbackGrowth", d),
                    UShort(l, "stunTicks", at + "launch.stunTicks", d));
            }
            return new CommandGrabCapabilityParameters(
                UShort(p, "lungeTicks", at + "lungeTicks", d), Float(p, "lungeSpeed", at + "lungeSpeed", d),
                Float(p, "stopDistance", at + "stopDistance", d), UShort(p, "captureTick", at + "captureTick", d),
                ParseHitboxNamed(p, "capture", at + "capture", d), UShort(p, "releaseTick", at + "releaseTick", d), launch,
                OptionalString(p, "catchPresentationId", at + "catchPresentationId", d) ?? "",
                OptionalString(p, "releasePresentationId", at + "releasePresentationId", d) ?? "",
                OptionalBool(p, "level", at + "level", d),
                p.ContainsKey("anchorDistance") ? Float(p, "anchorDistance", at + "anchorDistance", d) : null,
                OptionalUShort(p, "releaseLockTicks", at + "releaseLockTicks", d, 0),
                OptionalUShort(p, "whiffRecoveryTicks", at + "whiffRecoveryTicks", d, 0));
        }
        if (id == CharacterPackageCompiler.CannonballCapabilityId)
            return ParseCannonball(p, path + ".parameters", d);
        if (id == CharacterPackageCompiler.AimedRecoveryCapabilityId)
        {
            foreach (var required in allowed)
                if (required is not ("hop" or "riseSteerAcceleration") && !p.ContainsKey(required))
                    d.Error("operation.parameter-missing", path + ".parameters." + required, "Required capability parameter is missing.");
            RecoveryHopParameters? hop = null;
            if (p.TryGetValue("hop", out var elementHop))
            {
                string hp = path + ".parameters.hop";
                var values = ReadObjectWithCode(elementHop, hp, d, "operation.parameter-unknown",
                    "startTicks", "contactTicks", "forwardDistance", "arcHeight", "landingHeight");
                hop = new RecoveryHopParameters(UShort(values, "startTicks", hp + ".startTicks", d),
                    UShort(values, "contactTicks", hp + ".contactTicks", d),
                    Float(values, "forwardDistance", hp + ".forwardDistance", d),
                    Float(values, "arcHeight", hp + ".arcHeight", d),
                    Float(values, "landingHeight", hp + ".landingHeight", d));
            }
            return new AimedRecoveryCapabilityParameters(UShort(p, "startupTicks", path + ".parameters.startupTicks", d),
                Float(p, "verticalSpeed", path + ".parameters.verticalSpeed", d),
                Float(p, "directionalVerticalSpeed", path + ".parameters.directionalVerticalSpeed", d),
                Float(p, "horizontalSpeed", path + ".parameters.horizontalSpeed", d),
                Float(p, "preparationRiseSpeed", path + ".parameters.preparationRiseSpeed", d), hop,
                p.ContainsKey("riseSteerAcceleration")
                    ? Float(p, "riseSteerAcceleration", path + ".parameters.riseSteerAcceleration", d) : 0f);
        }
        foreach (var required in allowed) if (!string.Equals(required, "explosionPresentationId", StringComparison.Ordinal) && !string.Equals(required, "launchPresentationId", StringComparison.Ordinal) && !string.Equals(required, "landingPresentationId", StringComparison.Ordinal) && !string.Equals(required, "minimumChargeTicks", StringComparison.Ordinal) && !string.Equals(required, "chargeTiers", StringComparison.Ordinal) && !string.Equals(required, "sprayAnimationId", StringComparison.Ordinal) && !string.Equals(required, "sprayLoopTicks", StringComparison.Ordinal) && !string.Equals(required, "endAnimationId", StringComparison.Ordinal) && System.Array.IndexOf(CycloneKickOptional, required) < 0 && !p.ContainsKey(required)) d.Error("operation.parameter-missing", path + ".parameters." + required, "Required capability parameter is missing.");
        if (id == CharacterPackageCompiler.ChargedProjectileCapabilityId)
            return new ChargedProjectileCapabilityParameters(
                UShort(p, "maxChargeTicks", path + ".parameters.maxChargeTicks", d),
                UShort(p, "tier2Ticks", path + ".parameters.tier2Ticks", d),
                UShort(p, "tier3Ticks", path + ".parameters.tier3Ticks", d),
                UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d),
                Float(p, "tier2Radius", path + ".parameters.tier2Radius", d),
                Float(p, "tier3Radius", path + ".parameters.tier3Radius", d),
                ParseProjectile(p, path + ".parameters", d));
        if (id == CharacterPackageCompiler.FingerGunsCapabilityId)
            return new FingerGunsCapabilityParameters(
                UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d),
                UShort(p, "recoveryTicks", path + ".parameters.recoveryTicks", d),
                Byte(p, "shotsPerReload", path + ".parameters.shotsPerReload", d),
                UShort(p, "reloadTicks", path + ".parameters.reloadTicks", d),
                String(p, "reloadAnimationId", path + ".parameters.reloadAnimationId", d),
                Float(p, "airMaxFallSpeed", path + ".parameters.airMaxFallSpeed", d),
                Float(p, "airRecoilSpeed", path + ".parameters.airRecoilSpeed", d),
                ParseProjectile(p, path + ".parameters", d));
        if (id == CharacterPackageCompiler.ChargedStreamCapabilityId)
            return new ChargedStreamCapabilityParameters(
                UShort(p, "maxChargeTicks", path + ".parameters.maxChargeTicks", d),
                UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d),
                UShort(p, "emitIntervalTicks", path + ".parameters.emitIntervalTicks", d),
                UShort(p, "minStreamTicks", path + ".parameters.minStreamTicks", d),
                UShort(p, "maxStreamTicks", path + ".parameters.maxStreamTicks", d),
                UShort(p, "recoveryTicks", path + ".parameters.recoveryTicks", d),
                Float(p, "maxBaseKnockback", path + ".parameters.maxBaseKnockback", d),
                Float(p, "minRecoilSpeed", path + ".parameters.minRecoilSpeed", d),
                Float(p, "maxRecoilSpeed", path + ".parameters.maxRecoilSpeed", d),
                ParseProjectile(p, path + ".parameters", d),
                OptionalString(p, "sprayAnimationId", path + ".parameters.sprayAnimationId", d) ?? "",
                p.ContainsKey("sprayLoopTicks") ? UShort(p, "sprayLoopTicks", path + ".parameters.sprayLoopTicks", d) : (ushort)0,
                OptionalString(p, "endAnimationId", path + ".parameters.endAnimationId", d) ?? "");
        if (id.EndsWith("rising-dragon.v1", StringComparison.Ordinal)) return new RisingDragonCapabilityParameters(Float(p, "riseSpeed", path + ".parameters.riseSpeed", d), UShort(p, "riseTicks", path + ".parameters.riseTicks", d), UShort(p, "riseDelay", path + ".parameters.riseDelay", d));
        if (id.EndsWith("cyclone-kick.v1", StringComparison.Ordinal)) return new CycloneKickCapabilityParameters(Float(p, "forwardSpeed", path + ".parameters.forwardSpeed", d), UShort(p, "windupTicks", path + ".parameters.windupTicks", d), UShort(p, "hitboxEndTick", path + ".parameters.hitboxEndTick", d), UShort(p, "durationTicks", path + ".parameters.durationTicks", d), Float(p, "bodyRadius", path + ".parameters.bodyRadius", d), Float(p, "sideRadius", path + ".parameters.sideRadius", d), Float(p, "sideOffset", path + ".parameters.sideOffset", d), Float(p, "damage", path + ".parameters.damage", d), Float(p, "knockbackAngle", path + ".parameters.knockbackAngle", d), Float(p, "knockbackBase", path + ".parameters.knockbackBase", d), Float(p, "knockbackGrowth", path + ".parameters.knockbackGrowth", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), Float(p, "bodyY", path + ".parameters.bodyY", d), Float(p, "sideY", path + ".parameters.sideY", d),
            OptionalUShort(p, "hitIntervalTicks", path + ".parameters.hitIntervalTicks", d, 0),
            OptionalUShort(p, "fixedHitstunTicks", path + ".parameters.fixedHitstunTicks", d, 0),
            OptionalUShort(p, "finisherTick", path + ".parameters.finisherTick", d, 0),
            OptionalFloat(p, "finisherDamage", path + ".parameters.finisherDamage", d),
            OptionalFloat(p, "finisherKnockbackAngle", path + ".parameters.finisherKnockbackAngle", d),
            OptionalFloat(p, "finisherKnockbackBase", path + ".parameters.finisherKnockbackBase", d),
            OptionalFloat(p, "finisherKnockbackGrowth", path + ".parameters.finisherKnockbackGrowth", d));
        if (id == CharacterPackageCompiler.DashStrikeCapabilityId)
            return new DashStrikeCapabilityParameters(
                Float(p, "distance", path + ".parameters.distance", d), Float(p, "speed", path + ".parameters.speed", d),
                UShort(p, "tipPoseTick", path + ".parameters.tipPoseTick", d), UShort(p, "recoveryTicks", path + ".parameters.recoveryTicks", d),
                ParseHitboxNamed(p, "lightHitbox", path + ".parameters.lightHitbox", d),
                ParseHitboxNamed(p, "tipHitbox", path + ".parameters.tipHitbox", d),
                String(p, "tipPresentationId", path + ".parameters.tipPresentationId", d));
        if (id.EndsWith("wibou.rising-slash.v1", StringComparison.Ordinal)) return new WibouRisingSlashCapabilityParameters(Float(p, "riseSpeed", path + ".parameters.riseSpeed", d), UShort(p, "riseTicks", path + ".parameters.riseTicks", d), Float(p, "homingRange", path + ".parameters.homingRange", d), Float(p, "homingSpeed", path + ".parameters.homingSpeed", d));
        if (id == CharacterPackageCompiler.FlashCutCapabilityId)
        {
            string at = path + ".parameters.";
            return new FlashCutCapabilityParameters(
                UShort(p, "stanceTicks", at + "stanceTicks", d), Float(p, "lockRange", at + "lockRange", d),
                UShort(p, "maxTargets", at + "maxTargets", d), UShort(p, "legTicks", at + "legTicks", d),
                Float(p, "passThroughDistance", at + "passThroughDistance", d), UShort(p, "sheatheTicks", at + "sheatheTicks", d),
                UShort(p, "revealLeadTicks", at + "revealLeadTicks", d), UShort(p, "recoveryTicks", at + "recoveryTicks", d),
                UShort(p, "whiffTicks", at + "whiffTicks", d), ParseHitboxNamed(p, "cutHitbox", at + "cutHitbox", d),
                String(p, "stanceAnimationId", at + "stanceAnimationId", d), String(p, "legAnimationId", at + "legAnimationId", d),
                String(p, "sheatheAnimationId", at + "sheatheAnimationId", d),
                String(p, "lockPresentationId", at + "lockPresentationId", d), String(p, "revealPresentationId", at + "revealPresentationId", d),
                String(p, "sheathePresentationId", at + "sheathePresentationId", d), String(p, "popPresentationId", at + "popPresentationId", d));
        }
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
        if (id == "slop.internal.manki.proximity-mine.v1") return new MankiRoundBombCapabilityParameters(UShort(p, "armTicks", path + ".parameters.armTicks", d), UShort(p, "maxFlightTicks", path + ".parameters.maxFlightTicks", d), Float(p, "gravity", path + ".parameters.gravity", d), Float(p, "hitboxRadius", path + ".parameters.hitboxRadius", d), Float(p, "explosionDamage", path + ".parameters.explosionDamage", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "", Float(p, "triggerRadius", path + ".parameters.triggerRadius", d), UShort(p, "warningTicks", path + ".parameters.warningTicks", d), UShort(p, "lifetimeTicks", path + ".parameters.lifetimeTicks", d), Byte(p, "maxMines", path + ".parameters.maxMines", d));
        if (id == "slop.internal.manki.jetpack-boost.v3") return new MankiJetpackBoostCapabilityParameters(UShort(p, "startupTicks", path + ".parameters.startupTicks", d), Float(p, "verticalSpeed", path + ".parameters.verticalSpeed", d), Float(p, "horizontalSpeed", path + ".parameters.horizontalSpeed", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "explosionDamage", path + ".parameters.explosionDamage", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "", UShort(p, "fuelTicks", path + ".parameters.fuelTicks", d), Float(p, "sinkSpeed", path + ".parameters.sinkSpeed", d), Float(p, "jumpBurstSpeed", path + ".parameters.jumpBurstSpeed", d), UShort(p, "jumpBurstTicks", path + ".parameters.jumpBurstTicks", d));
        if (id == "slop.internal.manki.kamikaze.v1") return new MankiKamikazeCapabilityParameters(UShort(p, "runTicks", path + ".parameters.runTicks", d), Float(p, "speed", path + ".parameters.speed", d), Float(p, "contactRadius", path + ".parameters.contactRadius", d), Float(p, "explosionDamage", path + ".parameters.explosionDamage", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), Float(p, "selfPushSpeed", path + ".parameters.selfPushSpeed", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "");
        if (id is "slop.internal.manki.bazooka.v2" or "slop.internal.manki.bazooka.v3") return new MankiBazookaCapabilityParameters(UShort(p, "fireTriggerTick", path + ".parameters.fireTriggerTick", d), Float(p, "projectileSpeed", path + ".parameters.projectileSpeed", d), Float(p, "hitboxRadius", path + ".parameters.hitboxRadius", d), Float(p, "damage", path + ".parameters.damage", d), Float(p, "gravity", path + ".parameters.gravity", d), UShort(p, "maxFlightTicks", path + ".parameters.maxFlightTicks", d), UShort(p, "stunTicks", path + ".parameters.stunTicks", d), Float(p, "explosionRadius", path + ".parameters.explosionRadius", d), Float(p, "kbAngle", path + ".parameters.kbAngle", d), Float(p, "explosionKbBase", path + ".parameters.explosionKbBase", d), Float(p, "explosionKbGrowth", path + ".parameters.explosionKbGrowth", d), UShort(p, "explosionStunTicks", path + ".parameters.explosionStunTicks", d), UShort(p, "explosionDurationTicks", path + ".parameters.explosionDurationTicks", d), Float(p, "explosionKbAngle", path + ".parameters.explosionKbAngle", d), UShort(p, "castDuration", path + ".parameters.castDuration", d), UShort(p, "recoveryDuration", path + ".parameters.recoveryDuration", d), Float(p, "homingTurnDegPerTick", path + ".parameters.homingTurnDegPerTick", d), OptionalString(p, "explosionPresentationId", path + ".parameters.explosionPresentationId", d) ?? "", id.EndsWith(".v3", StringComparison.Ordinal) ? UShort(p, "chargeTicks", path + ".parameters.chargeTicks", d) : (ushort)0);

        d.Error("capability.unknown", path + ".capabilityId", "Capability parameters are not supported.");
        return new RisingDragonCapabilityParameters(0, 0, 0);
    }
    private static readonly string[] CycloneKickOptional = { "hitIntervalTicks", "fixedHitstunTicks", "finisherTick",
        "finisherDamage", "finisherKnockbackAngle", "finisherKnockbackBase", "finisherKnockbackGrowth" };
    private static readonly string[] ThrustFields =
    {
        "aim", "targetConeDegrees", "neutralPitchDegrees", "stickPitchDegrees", "speed", "acceleration", "maxSpeed",
        "keepIncomingSpeed", "thrustTicks", "trailHitbox",
    };

    private static ThrustCapabilityParameters ParseThrust(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        var aim = EnumValue(p, "aim", path + ".aim", d, x => x switch
        {
            "stick" => ThrustAim.Stick,
            "targetAhead" => ThrustAim.TargetAhead,
            _ => (ThrustAim?)null,
        });
        return new ThrustCapabilityParameters(
            aim,
            OptionalFloat(p, "targetConeDegrees", path + ".targetConeDegrees", d),
            Float(p, "neutralPitchDegrees", path + ".neutralPitchDegrees", d),
            Float(p, "stickPitchDegrees", path + ".stickPitchDegrees", d),
            Float(p, "speed", path + ".speed", d),
            Float(p, "acceleration", path + ".acceleration", d),
            Float(p, "maxSpeed", path + ".maxSpeed", d),
            Bool(p, "keepIncomingSpeed", path + ".keepIncomingSpeed", d),
            UShort(p, "thrustTicks", path + ".thrustTicks", d),
            p.ContainsKey("trailHitbox") ? ParseHitboxNamed(p, "trailHitbox", path + ".trailHitbox", d) : null);
    }

    private static readonly string[] FlashCutFields =
    {
        "stanceTicks", "lockRange", "maxTargets", "legTicks", "passThroughDistance", "sheatheTicks", "revealLeadTicks",
        "recoveryTicks", "whiffTicks", "cutHitbox", "stanceAnimationId", "legAnimationId", "sheatheAnimationId",
        "lockPresentationId", "revealPresentationId", "sheathePresentationId", "popPresentationId",
    };

    private static readonly string[] CannonballFields =
    {
        "takeoffTick", "maxRange", "noTargetDistance", "minFlightTicks", "maxFlightTicks", "maxLaunchSpeed",
        "launchPresentationId",
    };

    private static CannonballCapabilityParameters ParseCannonball(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
        => new(
            UShort(p, "takeoffTick", path + ".takeoffTick", d),
            Float(p, "maxRange", path + ".maxRange", d),
            Float(p, "noTargetDistance", path + ".noTargetDistance", d),
            UShort(p, "minFlightTicks", path + ".minFlightTicks", d),
            UShort(p, "maxFlightTicks", path + ".maxFlightTicks", d),
            Float(p, "maxLaunchSpeed", path + ".maxLaunchSpeed", d),
            OptionalString(p, "launchPresentationId", path + ".launchPresentationId", d) ?? "");

    private static readonly string[] TetherFields =
    {
        "throwTick", "projectile", "reelSpeed", "anchorDistance", "arrivalStunTicks", "maxReelTicks", "retractSpeed",
        "missRecoveryTicks", "airGravityScale", "reelAnimationId", "retractAnimationId", "throwPresentationId",
        "retractPresentationId", "atTarget",
    };

    private static readonly string[] TetherAtTargetFields =
    {
        "warningTicks", "catchTicks", "radius", "height", "damage", "maxRange", "noTargetDistance",
        "warningPresentationId", "catchPresentationId", "unmarked",
    };

    private static readonly string[] TetherUnmarkedFields =
    {
        "riseTick", "activeTicks", "distance", "radius", "height", "damage", "angle", "baseKnockback", "knockbackGrowth",
        "stunTicks", "recoveryTicks", "presentationId",
    };

    private static TetherUnmarkedSource ParseUnmarked(Dictionary<string, JsonElement> u, string up, DiagnosticBag d)
        => new(UShort(u, "riseTick", up + "riseTick", d), UShort(u, "activeTicks", up + "activeTicks", d),
            Float(u, "distance", up + "distance", d), Float(u, "radius", up + "radius", d), Float(u, "height", up + "height", d),
            Float(u, "damage", up + "damage", d), Float(u, "angle", up + "angle", d),
            Float(u, "baseKnockback", up + "baseKnockback", d), Float(u, "knockbackGrowth", up + "knockbackGrowth", d),
            UShort(u, "stunTicks", up + "stunTicks", d), UShort(u, "recoveryTicks", up + "recoveryTicks", d),
            OptionalString(u, "presentationId", up + "presentationId", d) ?? "");

    /// <summary>A thrown ring needs its projectile and retract speed; a placed catch (atTarget) has neither.</summary>
    private static TetherCapabilityParameters ParseTether(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        TetherAtTargetSource? at = null;
        if (p.ContainsKey("atTarget"))
        {
            var a = Object(p, "atTarget", path + ".atTarget", d, TetherAtTargetFields);
            string ap = path + ".atTarget.";
            at = new TetherAtTargetSource(UShort(a, "warningTicks", ap + "warningTicks", d),
                UShort(a, "catchTicks", ap + "catchTicks", d), Float(a, "radius", ap + "radius", d),
                Float(a, "height", ap + "height", d), Float(a, "damage", ap + "damage", d),
                Float(a, "maxRange", ap + "maxRange", d), Float(a, "noTargetDistance", ap + "noTargetDistance", d),
                OptionalString(a, "warningPresentationId", ap + "warningPresentationId", d) ?? "",
                OptionalString(a, "catchPresentationId", ap + "catchPresentationId", d) ?? "",
                a.ContainsKey("unmarked") ? ParseUnmarked(Object(a, "unmarked", ap + "unmarked", d, TetherUnmarkedFields),
                    ap + "unmarked.", d) : null);
            foreach (string thrown in new[] { "projectile", "retractSpeed" })
                if (p.ContainsKey(thrown))
                    d.Error("operation.parameter-unknown", path + "." + thrown, "A placed catch (atTarget) throws no ring.");
        }
        return new(
            UShort(p, "throwTick", path + ".throwTick", d),
            at == null ? ParseProjectile(p, path, d) : null,
            Float(p, "reelSpeed", path + ".reelSpeed", d),
            Float(p, "anchorDistance", path + ".anchorDistance", d),
            UShort(p, "arrivalStunTicks", path + ".arrivalStunTicks", d),
            UShort(p, "maxReelTicks", path + ".maxReelTicks", d),
            at == null ? Float(p, "retractSpeed", path + ".retractSpeed", d) : 0f,
            UShort(p, "missRecoveryTicks", path + ".missRecoveryTicks", d),
            Float(p, "airGravityScale", path + ".airGravityScale", d),
            OptionalString(p, "reelAnimationId", path + ".reelAnimationId", d) ?? "",
            OptionalString(p, "retractAnimationId", path + ".retractAnimationId", d) ?? "",
            OptionalString(p, "throwPresentationId", path + ".throwPresentationId", d) ?? "",
            OptionalString(p, "retractPresentationId", path + ".retractPresentationId", d) ?? "",
            at);
    }

    private static readonly string[] SteeredChargeRequired =
    {
        "gallopSpeed", "turnDegreesPerSecond", "maxGallopTicks", "armored", "lanceHitbox", "hitEndingTicks",
        "missEndingTicks", "skidTicks", "turnAroundOnMiss", "airStrideTicks", "lookDownTicks",
    };
    private static readonly string[] SteeredChargeFields = SteeredChargeRequired.Concat(new[]
    {
        "gallopAnimationId", "gallopLoopTicks", "hitEndingAnimationId", "missEndingAnimationId", "airEndingAnimationId",
        "gallopPresentationId", "hitEndingPresentationId", "missEndingPresentationId", "airEndingPresentationId",
    }).ToArray();

    private static SteeredChargeCapabilityParameters ParseSteeredCharge(Dictionary<string, JsonElement> p, string path, DiagnosticBag d)
    {
        foreach (var required in SteeredChargeRequired)
            if (!p.ContainsKey(required))
                d.Error("operation.parameter-missing", path + "." + required, "Required capability parameter is missing.");
        return new SteeredChargeCapabilityParameters(
            Float(p, "gallopSpeed", path + ".gallopSpeed", d),
            Float(p, "turnDegreesPerSecond", path + ".turnDegreesPerSecond", d),
            UShort(p, "maxGallopTicks", path + ".maxGallopTicks", d),
            Bool(p, "armored", path + ".armored", d),
            ParseHitboxNamed(p, "lanceHitbox", path + ".lanceHitbox", d),
            UShort(p, "hitEndingTicks", path + ".hitEndingTicks", d),
            UShort(p, "missEndingTicks", path + ".missEndingTicks", d),
            UShort(p, "skidTicks", path + ".skidTicks", d),
            Bool(p, "turnAroundOnMiss", path + ".turnAroundOnMiss", d),
            UShort(p, "airStrideTicks", path + ".airStrideTicks", d),
            UShort(p, "lookDownTicks", path + ".lookDownTicks", d),
            OptionalString(p, "gallopAnimationId", path + ".gallopAnimationId", d) ?? "",
            OptionalUShort(p, "gallopLoopTicks", path + ".gallopLoopTicks", d, 0),
            OptionalString(p, "hitEndingAnimationId", path + ".hitEndingAnimationId", d) ?? "",
            OptionalString(p, "missEndingAnimationId", path + ".missEndingAnimationId", d) ?? "",
            OptionalString(p, "airEndingAnimationId", path + ".airEndingAnimationId", d) ?? "",
            OptionalString(p, "gallopPresentationId", path + ".gallopPresentationId", d) ?? "",
            OptionalString(p, "hitEndingPresentationId", path + ".hitEndingPresentationId", d) ?? "",
            OptionalString(p, "missEndingPresentationId", path + ".missEndingPresentationId", d) ?? "",
            OptionalString(p, "airEndingPresentationId", path + ".airEndingPresentationId", d) ?? "");
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
    private static AuthoringPresentationCondition? ParsePresentationCondition(string x) => x switch { "always" => AuthoringPresentationCondition.Always, "sweetspotHit" => AuthoringPresentationCondition.SweetspotHit, _ => null };
    private static string PresentationConditionText(AuthoringPresentationCondition x) => x switch { AuthoringPresentationCondition.Always => "always", AuthoringPresentationCondition.SweetspotHit => "sweetspotHit", _ => throw new InvalidDataException("Unknown presentation condition.") };
    private static AuthoringAbilityBehavior? ParseBehavior(string x) => x switch { "meleeCombo" => AuthoringAbilityBehavior.MeleeCombo, "chargeAttack" => AuthoringAbilityBehavior.ChargeAttack, "aimedProjectile" => AuthoringAbilityBehavior.AimedProjectile, "projectile" => AuthoringAbilityBehavior.Projectile, "airGroundProjectile" => AuthoringAbilityBehavior.AirGroundProjectile, "selfBuff" => AuthoringAbilityBehavior.SelfBuff, "areaDenial" => AuthoringAbilityBehavior.AreaDenial, "directionalDash" => AuthoringAbilityBehavior.DirectionalDash, _ => null };
    private static AuthoringAimMode? ParseAimMode(string x) => x switch { "none" => AuthoringAimMode.None, "groundCursor" => AuthoringAimMode.GroundCursor, "cameraForward3D" => AuthoringAimMode.CameraForward3D, "groundVector" => AuthoringAimMode.GroundVector, _ => null };
    private static AuthoringAimMovementMode? ParseAimMovement(string x) => x switch { "fixed" => AuthoringAimMovementMode.Fixed, "mobile" => AuthoringAimMovementMode.Mobile, _ => null };
    private static AuthoringHitboxShape? ParseShape(string x) => x switch { "sphere" => AuthoringHitboxShape.Sphere, "capsule" => AuthoringHitboxShape.Capsule, _ => null };
    private static AuthoringKnockbackDirection? ParseKnockbackDirection(string x) => x switch { "awayFromOwner" => AuthoringKnockbackDirection.AwayFromOwner, "towardOwner" => AuthoringKnockbackDirection.TowardOwner, _ => null };
    private static AuthoringVelocityMode? ParseVelocityMode(string x) => x switch { "absolute" => AuthoringVelocityMode.Absolute, "additive" => AuthoringVelocityMode.Additive, "vertical" => AuthoringVelocityMode.Vertical, _ => null };
    private static ShellPolicy? ParseShellPolicy(string x) => x switch { "popOut" => ShellPolicy.PopOut, "tuck" => ShellPolicy.Tuck, "ride" => ShellPolicy.Ride, _ => null };
    private sealed class DiagnosticBag
    {
        private readonly List<(int Order, CharacterDiagnostic Value)> _items = new(); private int _order;
        public bool HasErrors => _items.Any(x => x.Value.Severity == CharacterDiagnosticSeverity.Error);
        public void Error(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message)));
        public void Warning(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, code, path, message)));
        public List<CharacterDiagnostic> ToList() => _items.OrderBy(x => x.Order).ThenBy(x => x.Value.Code, StringComparer.Ordinal).Select(x => x.Value).ToList();
    }
}
 
