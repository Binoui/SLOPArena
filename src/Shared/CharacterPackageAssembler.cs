using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;


public sealed record CharacterPackagePayloadInfo(string Path, string Sha256, long Size);

public sealed class CharacterPackageAssemblyInput
{
    public string PackageId { get; }
    public string Version { get; }
    public string Creator { get; }
    public string License { get; }
    public string Attribution { get; }
    public ushort AuthoringSchemaVersion { get; }
    public ushort CookedSchemaVersion { get; }
    public string RuntimeApiMin { get; }
    public string RuntimeApiMax { get; }
    public string SourceHash { get; }
    public IReadOnlyList<PackageDependencySource> Dependencies { get; }
    public IReadOnlyList<CookedCapabilityRequirement> CapabilityRequirements { get; }
    public string CookerVersion { get; }
    public string UnityVersion { get; }
    public int BindingSchemaVersion { get; }
    public string PoseFormat { get; }
    public int PoseVersion { get; }
    public int SampleRate { get; }
    public IReadOnlyList<CharacterDiagnostic> Warnings { get; }
    public byte[] RuntimeBytes { get; }
    public byte[] PoseBytes { get; }
    public byte[] BindingBytes { get; }
    public CookedCharacterPackage CookedPackage { get; }


    public CharacterPackageAssemblyInput(
        string packageId,
        string version,
        string creator,
        string license,
        string attribution,
        ushort authoringSchemaVersion,
        ushort cookedSchemaVersion,
        string runtimeApiMin,
        string runtimeApiMax,
        string sourceHash,
        IReadOnlyList<PackageDependencySource> dependencies,
        IReadOnlyList<CookedCapabilityRequirement> capabilityRequirements,
        string cookerVersion,
        string unityVersion,
        int bindingSchemaVersion,
        string poseFormat,
        int poseVersion,
        int sampleRate,
        IReadOnlyList<CharacterDiagnostic> warnings,
        byte[] runtimeBytes,
        byte[] poseBytes,
        byte[] bindingBytes,
        CookedCharacterPackage cookedPackage)
    {
        PackageId = packageId ?? "";
        Version = version ?? "";
        Creator = creator ?? "";
        License = license ?? "";
        Attribution = attribution ?? "";
        AuthoringSchemaVersion = authoringSchemaVersion;
        CookedSchemaVersion = cookedSchemaVersion;
        RuntimeApiMin = runtimeApiMin ?? "";
        RuntimeApiMax = runtimeApiMax ?? "";
        SourceHash = sourceHash ?? "";
        Dependencies = Copy(dependencies);
        CapabilityRequirements = Copy(capabilityRequirements);
        CookerVersion = cookerVersion ?? "";
        UnityVersion = unityVersion ?? "";
        BindingSchemaVersion = bindingSchemaVersion;
        PoseFormat = poseFormat ?? "";
        PoseVersion = poseVersion;
        SampleRate = sampleRate;
        Warnings = Copy(warnings);
        RuntimeBytes = Copy(runtimeBytes);
        PoseBytes = Copy(poseBytes);
        BindingBytes = Copy(bindingBytes);
        CookedPackage = cookedPackage ?? throw new ArgumentNullException(nameof(cookedPackage));
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        => new ReadOnlyCollection<T>(new List<T>(values ?? Array.Empty<T>()));

    private static byte[] Copy(byte[] value) => value == null ? Array.Empty<byte>() : (byte[])value.Clone();
}

public sealed class CharacterPackageAssemblyResult
{
    public byte[] ManifestBytes { get; }
    public byte[] RuntimeBytes { get; }
    public byte[] PoseBytes { get; }
    public byte[] BindingBytes { get; }
    public string SourceHash { get; }
    public string CookedContentHash { get; }
    public string PackageHash { get; }
    public IReadOnlyList<CharacterPackagePayloadInfo> Payloads { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
    public bool IsValid => !HasErrors && ManifestBytes.Length != 0;

    internal CharacterPackageAssemblyResult(
        byte[] manifestBytes,
        byte[] runtimeBytes,
        byte[] poseBytes,
        byte[] bindingBytes,
        string sourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackagePayloadInfo> payloads,
        IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        ManifestBytes = (byte[])manifestBytes.Clone();
        RuntimeBytes = (byte[])runtimeBytes.Clone();
        PoseBytes = (byte[])poseBytes.Clone();
        BindingBytes = (byte[])bindingBytes.Clone();
        SourceHash = sourceHash;
        CookedContentHash = cookedContentHash;
        PackageHash = packageHash;
        Payloads = new ReadOnlyCollection<CharacterPackagePayloadInfo>(new List<CharacterPackagePayloadInfo>(payloads));
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
    }

    internal static CharacterPackageAssemblyResult Failure(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => new(Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), "", "", "", Array.Empty<CharacterPackagePayloadInfo>(), diagnostics);
}

public sealed class CharacterPackageVerificationResult
{
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
    public bool IsValid => !HasErrors;

    internal CharacterPackageVerificationResult(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
}

public static class CharacterPackageAssembler
{
    public const int ManifestSchemaVersion = 1;
    public const string RuntimePath = "character.runtime.json";
    public const string PosePath = "poses.bin";
    public const string BindingPath = "client.bindings";
    public const string ManifestPath = "manifest.json";
    private static readonly string[] PayloadPaths = { RuntimePath, PosePath, BindingPath };

    /// <summary>Validates binding metadata without resolving Unity assets or requiring other payloads.</summary>
    public static IReadOnlyList<CharacterDiagnostic> ValidateClientBindings(byte[] bytes)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        ParseBindings(bytes, diagnostics);
        return diagnostics;
    }

    public static CharacterPackageAssemblyResult Assemble(CharacterPackageAssemblyInput input)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        ValidateInput(input, diagnostics);
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return CharacterPackageAssemblyResult.Failure(diagnostics);

        var payloads = new[]
        {
            (Path: RuntimePath, Bytes: input.RuntimeBytes),
            (Path: PosePath, Bytes: input.PoseBytes),
            (Path: BindingPath, Bytes: input.BindingBytes),
        };
        ValidateCrossPayloads(input, payloads, diagnostics);
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return CharacterPackageAssemblyResult.Failure(diagnostics);

        string cookedHash = HashFramed(payloads);
        var payloadInfo = payloads.Select(x => new CharacterPackagePayloadInfo(x.Path, Hash(x.Bytes), x.Bytes.LongLength)).ToArray();
        byte[] placeholder = WriteManifest(input, payloadInfo, cookedHash, "");
        string packageHash = Hash(Concat(FramedBytes(payloads), placeholder));
        byte[] manifest = WriteManifest(input, payloadInfo, cookedHash, packageHash);
        return new CharacterPackageAssemblyResult(manifest, input.RuntimeBytes, input.PoseBytes, input.BindingBytes,
            input.SourceHash, cookedHash, packageHash, payloadInfo, diagnostics);
    }

    public static CharacterPackageVerificationResult Verify(IReadOnlyDictionary<string, byte[]> files)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (files == null)
        {
            diagnostics.Add(Error("package.files.missing", "package", "Package files are missing."));
            return new CharacterPackageVerificationResult(diagnostics);
        }
        var expected = new HashSet<string>(PayloadPaths.Concat(new[] { ManifestPath }), StringComparer.Ordinal);
        foreach (string path in files.Keys)
        {
            if (!expected.Contains(path)) diagnostics.Add(Error("package.files.extra", path, "Unexpected package file."));
            else if (files[path] == null) diagnostics.Add(Error("package.files.null", path, "Package file bytes are null."));
        }
        foreach (string path in expected)
            if (!files.ContainsKey(path)) diagnostics.Add(Error("package.files.missing", path, "Required package file is missing."));
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return new CharacterPackageVerificationResult(diagnostics);

        ManifestData? manifest = ParseManifest(files[ManifestPath], diagnostics);
        if (manifest == null) return new CharacterPackageVerificationResult(diagnostics);
        ValidateManifestPayloads(manifest, files, diagnostics);
        ValidateRuntimeAndBindings(files[RuntimePath], files[BindingPath], files[PosePath], manifest, diagnostics);
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return new CharacterPackageVerificationResult(diagnostics);

        string cookedHash = HashFramed(Payloads(files));
        if (!StringEquals(manifest.CookedContentHash, cookedHash))
            diagnostics.Add(Error("package.hash.cooked-mismatch", "cookedContentHash", "Cooked content hash does not match payload bytes."));
        string packageHash = Hash(Concat(FramedBytes(Payloads(files)), WriteManifest(manifest, "")));
        if (!StringEquals(manifest.PackageHash, packageHash))
            diagnostics.Add(Error("package.hash.package-mismatch", "packageHash", "Package hash does not match payload bytes and placeholder manifest."));
        byte[] canonicalManifest = WriteManifest(manifest, manifest.PackageHash);
        if (!files[ManifestPath].SequenceEqual(canonicalManifest))
            diagnostics.Add(Error("package.manifest.noncanonical", ManifestPath, "Manifest bytes are not the canonical representation."));
        return new CharacterPackageVerificationResult(diagnostics);
    }

    private static void ValidateInput(CharacterPackageAssemblyInput input, List<CharacterDiagnostic> d)
    {
        if (input == null) { d.Add(Error("package.input.missing", "input", "Assembly input is missing.")); return; }
        if (!MatchContentCatalogBuilder.IsStablePackageId(input.PackageId)) d.Add(Error("package.id.invalid", "packageId", "Package ID must be a stable lowercase identifier."));
        if (string.IsNullOrEmpty(input.Version)) d.Add(Error("package.version.invalid", "version", "Package version is required."));
        if (input.AuthoringSchemaVersion == 0 || input.CookedSchemaVersion == 0) d.Add(Error("package.schema.invalid", "schema", "Schema versions must be non-zero."));
        if (string.IsNullOrEmpty(input.RuntimeApiMin) || string.IsNullOrEmpty(input.RuntimeApiMax)) d.Add(Error("package.api.invalid", "runtimeApi", "Runtime API range is required."));
        if (!IsLowerHash(input.SourceHash)) d.Add(Error("package.hash.source-invalid", "sourceHash", "Source hash must be lowercase SHA-256 hex."));
        if (input.BindingSchemaVersion <= 0 || input.PoseVersion <= 0 || input.SampleRate <= 0 || input.PoseFormat != "SKEL") d.Add(Error("package.toolchain.invalid", "toolchain", "Binding, pose, sample, and format metadata are invalid."));
        if (input.RuntimeBytes.Length == 0) d.Add(Error("package.payload.empty", RuntimePath, "Runtime payload is empty."));
        if (input.PoseBytes.Length == 0) d.Add(Error("package.payload.empty", PosePath, "Pose payload is empty."));
        if (input.BindingBytes.Length == 0) d.Add(Error("package.payload.empty", BindingPath, "Binding payload is empty."));
        if (input.CookedPackage.Metadata.PackageId != input.PackageId || input.CookedPackage.Metadata.Version != input.Version || input.CookedPackage.Metadata.CookedSchemaVersion != input.CookedSchemaVersion || input.CookedPackage.Metadata.RuntimeApiMin != input.RuntimeApiMin || input.CookedPackage.Metadata.RuntimeApiMax != input.RuntimeApiMax)
            d.Add(Error("package.metadata.mismatch", "cookedPackage.metadata", "Cooked package metadata does not match assembly input."));
        if (!input.RuntimeBytes.SequenceEqual(input.CookedPackage.CanonicalBytes)) d.Add(Error("package.runtime.mismatch", RuntimePath, "Runtime payload must equal CookedPackage.CanonicalBytes."));
        ValidateUniqueMetadata(input.Dependencies.Select(x => x.PackageId), "dependencies", d);
        ValidateUniqueMetadata(input.CapabilityRequirements.Select(x => x.CapabilityId), "capabilityRequirements", d);
        foreach (var warning in input.Warnings)
        {
            if (warning.Severity != CharacterDiagnosticSeverity.Warning)
                d.Add(Error("package.warning.invalid", "warnings", "Assembly warnings must have warning severity."));
            if (string.IsNullOrEmpty(warning.Code) || string.IsNullOrEmpty(warning.Path))
                d.Add(Error("package.warning.invalid", "warnings", "Warnings require code and path."));
        }
    }

    private static void ValidateCrossPayloads(CharacterPackageAssemblyInput input, (string Path, byte[] Bytes)[] payloads, List<CharacterDiagnostic> d)
    {
        try
        {
            BindingData binding = ParseBindings(input.BindingBytes, d);
            PoseData poses = ParsePoses(input.PoseBytes, d);
            HashSet<string> required = RequiredAnimations(input.CookedPackage, d);
            if (binding == null || poses == null) return;
            ValidateReferences(required, input.CookedPackage.Definition.PresentationIds,
                input.CookedPackage.Definition.Slots.Select(slot => slot.HitPresentationId).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!),
                binding, poses, d);
            if (binding.PackageId != input.PackageId || binding.SourceHash != input.SourceHash || binding.BindingSchemaVersion != input.BindingSchemaVersion || binding.PoseFormat != input.PoseFormat || binding.PoseVersion != input.PoseVersion || binding.SampleRate != input.SampleRate)
                d.Add(Error("package.binding.metadata-mismatch", BindingPath, "Binding metadata does not match assembly input."));
        }
        catch (Exception ex) { d.Add(Error("package.payload.malformed", "payload", ex.Message)); }
    }

    private static void ValidateManifestPayloads(ManifestData manifest, IReadOnlyDictionary<string, byte[]> files, List<CharacterDiagnostic> d)
    {
        if (manifest.Payloads.Count != PayloadPaths.Length) { d.Add(Error("package.manifest.payloads", "payloads", "Manifest must contain exactly three payloads.")); return; }
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < PayloadPaths.Length; i++)
        {
            var p = manifest.Payloads[i];
            if (!seenPaths.Add(p.Path)) d.Add(Error("package.manifest.payload-duplicate", $"payloads[{i}].path", "Manifest contains a duplicate payload path."));
            if (p.Path != PayloadPaths[i]) d.Add(Error("package.manifest.payload-order", $"payloads[{i}].path", "Payloads must use the canonical order."));
            if (!ExpectedPayloadPath(p.Path) || !files.TryGetValue(p.Path, out byte[] bytes) || bytes == null)
            {
                d.Add(Error("package.manifest.payload-path", $"payloads[{i}].path", "Manifest payload path is not a canonical package payload."));
                continue;
            }
            if (!IsLowerHash(p.Sha256)) d.Add(Error("package.hash.payload-invalid", $"payloads[{i}].sha256", "Payload hash must be lowercase SHA-256 hex."));
            if (p.Size != bytes.LongLength) d.Add(Error("package.payload.size-mismatch", p.Path, "Manifest payload size does not match bytes."));
            if (!StringEquals(p.Sha256, Hash(bytes))) d.Add(Error("package.hash.payload-mismatch", p.Path, "Manifest payload hash does not match bytes."));
        }
    }

    private static bool ExpectedPayloadPath(string path) => PayloadPaths.Contains(path, StringComparer.Ordinal);

    private static void ValidateRuntimeAndBindings(byte[] runtime, byte[] bindings, byte[] poses, ManifestData manifest, List<CharacterDiagnostic> d)
    {
        try
        {
            using var document = JsonDocument.Parse(runtime);
            var root = document.RootElement;
            bool validShape = root.ValueKind == JsonValueKind.Object && HasObject(root, "metadata") && HasObject(root, "character") && HasObject(root, "budget");
            if (!validShape)
                d.Add(Error("package.runtime.schema", RuntimePath, "Runtime payload is not a cooked character definition."));
            else
            {
                EnsureFields(root, new[] { "metadata", "character", "budget" }, RuntimePath, d);
                var metadata = root.GetProperty("metadata");
                EnsureFields(metadata, new[] { "packageId", "version", "cookedSchemaVersion", "compatibility" }, RuntimePath + ".metadata", d);
                if (HasObject(metadata, "compatibility"))
                    EnsureFields(metadata.GetProperty("compatibility"), new[] { "runtimeApiMin", "runtimeApiMax" }, RuntimePath + ".metadata.compatibility", d);
                EnsureFieldsOptional(root.GetProperty("character"), new[] { "displayName", "weight", "movement", "presentation", "capsuleRadius", "capsuleHeight", "hipHeight", "hurtboxRadius", "shieldRadius", "captureGeometry", "hurtboxCapsules", "hurtboxBoneDefs", "attachmentBoneIds", "presentationIds", "capabilityRequirements", "slots" }, RuntimePath + ".character", d);
                if (!root.GetProperty("character").TryGetProperty("shieldRadius", out _))
                    d.Add(Error("package.runtime.schema", RuntimePath + ".character.shieldRadius", "Shield radius is required."));
                if (HasObject(root.GetProperty("character"), "presentation"))
                    EnsureFields(root.GetProperty("character").GetProperty("presentation"), new[] { "idle", "run", "dash", "jump", "fall", "hitSmall", "hitMedium", "hitHard", "tumble", "crouch", "slide", "shield", "grab", "grabbed", "throwForward", "airDodge", "landStartOffsetSeconds", "modelResourcePath", "visualScale", "hurtboxBoneScale", "modelYOffset", "modelSoleOffset", "autoModelYOffset" }, RuntimePath + ".character.presentation", d);
                if (!HasObject(root.GetProperty("character"), "captureGeometry"))
                    d.Add(Error("package.runtime.schema", RuntimePath + ".character.captureGeometry", "Capture geometry is required."));
                else
                {
                    var capture = root.GetProperty("character").GetProperty("captureGeometry");
                    EnsureFields(capture, new[] { "reach", "width", "height", "offsetY", "attackerAnchor", "victimAnchor" }, RuntimePath + ".character.captureGeometry", d);
                    foreach (string anchorName in new[] { "attackerAnchor", "victimAnchor" })
                        if (HasObject(capture, anchorName))
                            EnsureFields(capture.GetProperty(anchorName), new[] { "x", "y", "z" }, RuntimePath + ".character.captureGeometry." + anchorName, d);
                }
                EnsureFields(root.GetProperty("budget"), new[] { "slotCount", "stageCount", "operationCount", "hitboxCount", "projectileCount", "capabilityCount", "maxTimelineDurationTicks" }, RuntimePath + ".budget", d);
                if (GetString(metadata, "packageId") != manifest.PackageId || GetString(metadata, "version") != manifest.Version || GetUInt16(metadata, "cookedSchemaVersion") != manifest.CookedSchemaVersion)
                    d.Add(Error("package.runtime.metadata-mismatch", RuntimePath, "Runtime metadata does not match manifest."));
                if (!HasObject(metadata, "compatibility") || GetString(metadata.GetProperty("compatibility"), "runtimeApiMin") != manifest.RuntimeApiMin || GetString(metadata.GetProperty("compatibility"), "runtimeApiMax") != manifest.RuntimeApiMax)
                    d.Add(Error("package.runtime.metadata-mismatch", RuntimePath, "Runtime API metadata does not match manifest."));
            }
            BindingData binding = ParseBindings(bindings, d);
            PoseData pose = ParsePoses(poses, d);
            if (binding != null && pose != null)
            {
                HashSet<string> required = RequiredAnimations(root, d);
                ValidateReferences(required, RequiredPresentations(root, d), RequiredHitPresentations(root, d), binding, pose, d);
                if (root.GetProperty("character").TryGetProperty("attachmentBoneIds", out var attachmentIds) && attachmentIds.ValueKind == JsonValueKind.Array)
                    foreach (var id in attachmentIds.EnumerateArray())
                        if (id.ValueKind != JsonValueKind.String || !pose.BoneNames.Contains(id.GetString() ?? ""))
                            d.Add(Error("package.pose.attachment-missing", "character.attachmentBoneIds", "Attachment ID is missing from poses.bin."));
                if (binding.PackageId != manifest.PackageId || binding.SourceHash != manifest.SourceHash || binding.BindingSchemaVersion != manifest.Toolchain.BindingSchemaVersion || binding.PoseFormat != manifest.Toolchain.PoseFormat || binding.PoseVersion != manifest.Toolchain.PoseVersion || binding.SampleRate != manifest.Toolchain.SampleRate)
                    d.Add(Error("package.binding.metadata-mismatch", BindingPath, "Binding metadata does not match manifest."));
            }
        }
        catch (Exception ex) { d.Add(Error("package.runtime.malformed", RuntimePath, ex.Message)); }
    }

    private static HashSet<string> RequiredAnimations(CookedCharacterPackage package, List<CharacterDiagnostic> d)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        Add(required, package.Definition.Presentation.Idle, d, "character.presentation");
        Add(required, package.Definition.Presentation.Run, d, "character.presentation");
        Add(required, package.Definition.Presentation.Dash, d, "character.presentation");
        Add(required, package.Definition.Presentation.Jump, d, "character.presentation");
        Add(required, package.Definition.Presentation.Fall, d, "character.presentation");
        Add(required, package.Definition.Presentation.HitSmall, d, "character.presentation");
        Add(required, package.Definition.Presentation.HitMedium, d, "character.presentation");
        Add(required, package.Definition.Presentation.HitHard, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Tumble, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Crouch, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Slide, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Shield, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Grab, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.Grabbed, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.ThrowForward, d, "character.presentation");
        AddOptional(required, package.Definition.Presentation.AirDodge, d, "character.presentation");
        foreach (var slot in package.Definition.Slots)
        {
            if (!string.IsNullOrEmpty(slot.AimAnimationId))
                Add(required, slot.AimAnimationId, d, "character.slots.aimAnimationId");
            foreach (var stage in slot.Timeline.Stages)
                foreach (string id in stage.AnimationIds) Add(required, id, d, "character.timeline.animationIds");
        }
        return required;
    }

    private static HashSet<string> RequiredAnimations(JsonElement root, List<CharacterDiagnostic> d)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object || !HasObject(root, "character")) return required;
        var character = root.GetProperty("character");
        if (HasObject(character, "presentation"))
        {
            var presentation = character.GetProperty("presentation");
            foreach (string name in new[] { "idle", "run", "dash", "jump", "fall", "hitSmall", "hitMedium", "hitHard" }) Add(required, GetString(presentation, name), d, "character.presentation");
            if (presentation.TryGetProperty("tumble", out var tumble) && tumble.ValueKind == JsonValueKind.String)
                AddOptional(required, tumble.GetString() ?? "", d, "character.presentation");
            if (presentation.TryGetProperty("crouch", out var crouch) && crouch.ValueKind == JsonValueKind.String)
                AddOptional(required, crouch.GetString() ?? "", d, "character.presentation");
            if (presentation.TryGetProperty("slide", out var slide) && slide.ValueKind == JsonValueKind.String)
                AddOptional(required, slide.GetString() ?? "", d, "character.presentation");
            foreach (string name in new[] { "shield", "grab", "grabbed", "throwForward", "airDodge" })
                if (presentation.TryGetProperty(name, out var role) && role.ValueKind == JsonValueKind.String)
                    AddOptional(required, role.GetString() ?? "", d, "character.presentation");
        }
        if (character.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
            foreach (var slot in slots.EnumerateArray())
            {
                if (slot.TryGetProperty("aimAnimationId", out var aim) && aim.ValueKind == JsonValueKind.String)
                    Add(required, aim.GetString() ?? "", d, "character.slots.aimAnimationId");
                if (slot.TryGetProperty("timeline", out var timeline) && timeline.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Array)
                    foreach (var stage in stages.EnumerateArray())
                        if (stage.TryGetProperty("animationIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                            foreach (var id in ids.EnumerateArray()) Add(required, id.GetString() ?? "", d, "character.timeline.animationIds");
            }
        return required;
    }
    private static HashSet<string> RequiredPresentations(JsonElement root, List<CharacterDiagnostic> d)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object || !HasObject(root, "character")) return required;
        var character = root.GetProperty("character");
        if (character.TryGetProperty("presentationIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
            foreach (var id in ids.EnumerateArray())
                Add(required, id.GetString() ?? "", d, "character.presentationIds");
        return required;
    }
    private static HashSet<string> RequiredHitPresentations(JsonElement root, List<CharacterDiagnostic> d)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (!HasObject(root, "character")) return required;
        var character = root.GetProperty("character");
        if (character.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
            foreach (var slot in slots.EnumerateArray())
                if (slot.TryGetProperty("hitPresentationId", out var hit))
                {
                    if (hit.ValueKind != JsonValueKind.String)
                        d.Add(Error("package.runtime.value", "character.slots.hitPresentationId", "Hit presentation ID must be a string."));
                    else
                        AddOptional(required, hit.GetString() ?? "", d, "character.slots.hitPresentationId");
                }
        return required;
    }


    private static void ValidateReferences(
        HashSet<string> required,
        IEnumerable<string> requiredPresentations,
        IEnumerable<string> requiredHitPresentations,
        BindingData binding,
        PoseData poses,
        List<CharacterDiagnostic> d)
    {
        foreach (string id in required)
            if (!binding.BySemantic.ContainsKey(id)) d.Add(Error("package.binding.missing", id, "Required animation binding is missing."));
        foreach (var pair in binding.BySemantic)
        {
            if (!required.Contains(pair.Key)) d.Add(Error("package.binding.orphan", pair.Key, "Binding references an animation not required by the cooked definition."));
            if (!poses.Names.Contains(pair.Value.PoseTrackId)) d.Add(Error("package.pose.missing", pair.Value.PoseTrackId, "Binding pose track is missing from poses.bin."));
            else if (poses.FrameCounts[pair.Value.PoseTrackId] != pair.Value.FrameCount) d.Add(Error("package.pose.frame-count-mismatch", pair.Key, "Binding frame count does not match poses.bin."));
        }
        foreach (string name in poses.Names)
            if (!binding.ByPose.ContainsKey(name)) d.Add(Error("package.pose.orphan", name, "poses.bin contains an unreferenced pose track."));

        var requiredPresentationSet = new HashSet<string>(requiredHitPresentations, StringComparer.Ordinal);
        foreach (string id in requiredPresentationSet)
            if (!requiredPresentations.Contains(id, StringComparer.Ordinal))
                d.Add(Error("package.binding.presentation-undeclared", id, "Hit presentation ID is not declared by the cooked definition."));
        if (binding.BindingSchemaVersion == 2 || binding.HasPresentations)
            requiredPresentationSet.UnionWith(requiredPresentations);
        foreach (string id in requiredPresentationSet)
            if (!binding.Presentations.Contains(id))
                d.Add(Error("package.binding.presentation-missing", id, "Required presentation binding is missing."));
        foreach (string id in binding.Presentations)
            if (!requiredPresentationSet.Contains(id))
                d.Add(Error("package.binding.presentation-orphan", id, "Presentation binding is not required by the cooked definition."));
    }

    private static BindingData? ParseBindings(byte[] bytes, List<CharacterDiagnostic> d)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { d.Add(Error("package.binding.schema", BindingPath, "Binding payload must be an object.")); return null; }
            int version = GetInt(root, "bindingSchemaVersion");
            if (version is not (1 or 2) || GetInt(root, "catalogSchemaVersion") != version)
                d.Add(Error("package.binding.schema", BindingPath, "Only matching catalog/binding schemas 1 or 2 are supported."));
            string[] fields = { "packageId", "catalogSchemaVersion", "bindingSchemaVersion", "poseFormat", "poseVersion", "sampleRate", "sourceHash", "rigGlobalObjectId", "weaponConfigGlobalObjectId", "animations", "presentations" };
            if (version == 2)
            {
                EnsureFields(root, fields.Concat(new[] { "presentationProfile" }).ToArray(), BindingPath, d);
                foreach (string field in fields.Concat(new[] { "presentationProfile" }))
                    if (!root.TryGetProperty(field, out _))
                        d.Add(Error("package.field.missing", BindingPath + "." + field, "Required schema 2 field is missing."));
                if (!MatchContentCatalogBuilder.IsStablePackageId(GetString(root, "packageId"))
                    || !IsLowerHash(GetString(root, "sourceHash")) || GetString(root, "poseFormat") != "SKEL"
                    || GetInt(root, "poseVersion") != 1 || GetInt(root, "sampleRate") != 60)
                    d.Add(Error("package.binding.metadata-invalid", BindingPath, "Schema 2 requires a stable package ID, SHA-256 source hash, SKEL pose version 1 and 60 Hz."));
                ValidateMediaId(root.GetProperty("rigGlobalObjectId"), false, "rigGlobalObjectId", d);
                ValidateMediaId(root.GetProperty("weaponConfigGlobalObjectId"), true, "weaponConfigGlobalObjectId", d);
                ValidatePresentationProfile(root.GetProperty("presentationProfile"), d);
            }
            else EnsureFieldsOptional(root, fields, BindingPath, d);
            var result = new BindingData
            {
                PackageId = GetString(root, "packageId"),
                BindingSchemaVersion = GetInt(root, "bindingSchemaVersion"),
                PoseFormat = GetString(root, "poseFormat"),
                PoseVersion = GetInt(root, "poseVersion"),
                SampleRate = GetInt(root, "sampleRate"),
                SourceHash = GetString(root, "sourceHash"),
            };
            if (!root.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Array) { d.Add(Error("package.binding.schema", BindingPath, "Binding animations must be an array.")); return null; }
            foreach (var element in animations.EnumerateArray())
            {
                EnsureFields(element, new[] { "semanticId", "poseTrackId", "clipGlobalObjectId", "poseName", "frameCount", "clipLengthBits", "sampleRate", "extrapolation" }, BindingPath + ".animations", d);
                var item = new BindingItem(GetString(element, "semanticId"), GetString(element, "poseTrackId"), GetInt(element, "frameCount"));
                if (string.IsNullOrEmpty(item.SemanticId) || string.IsNullOrEmpty(item.PoseTrackId) || item.FrameCount <= 0) d.Add(Error("package.binding.value", BindingPath, "Binding IDs and frame count are invalid."));
                if (version == 2)
                {
                    ValidateMediaId(element.GetProperty("clipGlobalObjectId"), false, "animations.clipGlobalObjectId", d);
                    float length = BitConverter.Int32BitsToSingle(GetInt(element, "clipLengthBits"));
                    if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0f
                        || GetInt(element, "sampleRate") != result.SampleRate
                        || GetString(element, "poseName") != item.PoseTrackId
                        || element.GetProperty("extrapolation").GetInt32() is < (int)ExtrapolationMode.None or > (int)ExtrapolationMode.Continuous)
                        d.Add(Error("package.binding.value", item.SemanticId, "Animation metadata is invalid."));
                }
                if (!result.BySemantic.TryAdd(item.SemanticId, item)) d.Add(Error("package.binding.duplicate", item.SemanticId, "Duplicate semantic animation ID."));
                if (!result.ByPose.TryAdd(item.PoseTrackId, item)) d.Add(Error("package.binding.duplicate", item.PoseTrackId, "Duplicate pose-track ID."));
            }
            if (root.TryGetProperty("presentations", out var presentations) && presentations.ValueKind == JsonValueKind.Array)
            {
                result.HasPresentations = presentations.GetArrayLength() > 0;
                foreach (var element in presentations.EnumerateArray())
                {
                    EnsureFields(element, new[] { "semanticId", "prefabGlobalObjectId" }, BindingPath + ".presentations", d);
                    string id = GetString(element, "semanticId");
                    string prefabId = GetString(element, "prefabGlobalObjectId");
                    if (version == 2) ValidateMediaId(element.GetProperty("prefabGlobalObjectId"), false, "presentations.prefabGlobalObjectId", d);
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(prefabId))
                        d.Add(Error("package.binding.value", BindingPath + ".presentations", "Presentation IDs and prefab object IDs are required."));
                    else if (!result.Presentations.Add(id))
                        d.Add(Error("package.binding.duplicate", id, "Duplicate semantic presentation ID."));
                }
            }
            if (root.TryGetProperty("presentations", out var presentationValue) && presentationValue.ValueKind != JsonValueKind.Array)
                d.Add(Error("package.binding.schema", "presentations", "Presentations must be an array."));
            return result;
        }
        catch (Exception ex) { d.Add(Error("package.binding.malformed", BindingPath, ex.Message)); return null; }
    }

    // v1 retains its symbolic-media fixture contract. v2 carries complete Unity media metadata.
    private static void ValidateMediaId(JsonElement value, bool allowEmpty, string path, List<CharacterDiagnostic> d)
    {
        string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        string[] parts = text.Split('-');
        bool valid = allowEmpty && value.ValueKind == JsonValueKind.String && text.Length == 0;
        valid |= parts.Length == 5 && parts[0] == "GlobalObjectId_V1"
            && parts[1] is "1" or "2" or "3"
            && parts[2].Length == 32 && parts[2].Any(c => c != '0')
            && parts[2].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            && ulong.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong fileId) && fileId != 0
            && ulong.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
        if (!valid) d.Add(Error("package.binding.media-id", path, "A stable Unity object ID is required."));
    }

    private static void ValidatePresentationProfile(JsonElement profile, List<CharacterDiagnostic> d)
    {
        const string path = "presentationProfile";
        EnsureFields(profile, new[] { "respawnTintBits", "swing", "lightImpact", "mediumImpact", "heavyImpact", "suppressRemovalExplosion", "projectiles" }, path, d);
        var tint = profile.GetProperty("respawnTintBits");
        if (tint.ValueKind != JsonValueKind.Array || tint.GetArrayLength() != 4)
            d.Add(Error("package.binding.profile", path + ".respawnTintBits", "Four finite RGBA float bits are required."));
        else foreach (var bits in tint.EnumerateArray()) FloatBits(bits, false, path + ".respawnTintBits");
        foreach (string name in new[] { "swing", "lightImpact", "mediumImpact", "heavyImpact" })
        {
            var pool = profile.GetProperty(name);
            EnsureFields(pool, new[] { "selection", "clipGlobalObjectIds" }, path + "." + name, d);
            int selection = pool.GetProperty("selection").GetInt32();
            var clips = pool.GetProperty("clipGlobalObjectIds");
            if (clips.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Audio pool clips must be an array.");
            int count = clips.GetArrayLength();
            if (selection is < 0 or > 1 || (selection == 0 && count != 1 && !(name == "swing" && count == 0))
                || (selection == 1 && count == 0))
                d.Add(Error("package.binding.profile", path + "." + name, "Fixed pools have one clip, Cycle pools at least one; empty Fixed Swing means silence."));
            foreach (var clip in clips.EnumerateArray()) ValidateMediaId(clip, true, path + "." + name, d);
        }
        if (profile.GetProperty("suppressRemovalExplosion").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            d.Add(Error("package.binding.profile", path, "Removal suppression must be boolean."));
        var rows = profile.GetProperty("projectiles");
        if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Projectile rows must be an array.");
        if (rows.GetArrayLength() > 16) d.Add(Error("package.binding.profile", path, "At most sixteen canonical projectile rows are supported."));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            EnsureFields(row, new[] { "slotId", "prefabGlobalObjectId", "scaleBits", "orientation", "heldBoneId" }, path + ".projectiles", d);
            string slot = GetString(row, "slotId");
            if (!CanonicalSlotProjection.TryGet(slot, out _) || !seen.Add(slot))
                d.Add(Error("package.binding.profile", path + ".projectiles", "Projectile slots must be unique canonical IDs."));
            ValidateMediaId(row.GetProperty("prefabGlobalObjectId"), false, path + ".projectiles.prefabGlobalObjectId", d);
            FloatBits(row.GetProperty("scaleBits"), true, path + ".projectiles.scaleBits");
            if (row.GetProperty("orientation").GetInt32() is < 0 or > 1)
                d.Add(Error("package.binding.profile", path + ".projectiles.orientation", "Unsupported projectile orientation."));
            string bone = row.GetProperty("heldBoneId").GetString() ?? throw new InvalidDataException("Held bone ID must be a string.");
            if (bone.Length != 0 && (!bone.StartsWith("bone.", StringComparison.Ordinal) || bone.Length == 5 || bone.Length > 64
                || !bone.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
                d.Add(Error("package.binding.profile", path + ".projectiles.heldBoneId", "Held attachment must be a semantic bone ID or empty."));
        }
        void FloatBits(JsonElement bits, bool positive, string field)
        {
            if (!bits.TryGetInt32(out int integer)) { d.Add(Error("package.binding.profile", field, "Float bits must be int32.")); return; }
            float value = BitConverter.Int32BitsToSingle(integer);
            if (float.IsNaN(value) || float.IsInfinity(value) || (positive && value <= 0f))
                d.Add(Error("package.binding.profile", field, "Float bits must encode a finite value, positive for scale."));
        }
    }

    private static PoseData? ParsePoses(byte[] bytes, List<CharacterDiagnostic> d)
    {
        try
        {
            var reader = new PoseReader(bytes);
            reader.ReadUInt32("magic", 0x4C454B53);
            reader.ReadUInt32("version", 1);
            uint boneCount = reader.ReadUInt32("boneCount");
            uint animationCount = reader.ReadUInt32("animationCount");
            if (boneCount == 0 || animationCount == 0 || boneCount > 4096 || animationCount > 4096) throw new InvalidDataException("Pose counts are outside safe bounds.");
            var result = new PoseData();
            for (uint i = 0; i < boneCount; i++) result.BoneNames.Add(reader.ReadString("bone name"));
            for (uint i = 0; i < animationCount; i++)
            {
                string name = reader.ReadString("animation name");
                uint frameCount = reader.ReadUInt32("frame count");
                if (string.IsNullOrEmpty(name) || frameCount == 0 || frameCount > 1_000_000) throw new InvalidDataException("Pose animation name or frame count is invalid.");
                if (!result.Names.Add(name)) throw new InvalidDataException("Duplicate pose animation name.");
                result.FrameCounts.Add(name, checked((int)frameCount));
                long values = checked((long)frameCount * boneCount * 3);
                reader.SkipFloats(values);
            }
            reader.RequireEnd();
            return result;
        }
        catch (Exception ex) { d.Add(Error("package.pose.malformed", PosePath, ex.Message)); return null; }
    }

    private static ManifestData? ParseManifest(byte[] bytes, List<CharacterDiagnostic> d)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
            EnsureFields(root, new[] { "manifestSchemaVersion", "packageId", "version", "creator", "license", "attribution", "authoringSchemaVersion", "cookedSchemaVersion", "runtimeApiMin", "runtimeApiMax", "sourceHash", "cookedContentHash", "packageHash", "dependencies", "capabilityRequirements", "payloads", "toolchain", "warnings" }, ManifestPath, d);
            var result = new ManifestData
            {
                PackageId = GetString(root, "packageId"), Version = GetString(root, "version"), Creator = GetString(root, "creator"), License = GetString(root, "license"), Attribution = GetString(root, "attribution"),
                AuthoringSchemaVersion = GetUInt16(root, "authoringSchemaVersion"), CookedSchemaVersion = GetUInt16(root, "cookedSchemaVersion"), RuntimeApiMin = GetString(root, "runtimeApiMin"), RuntimeApiMax = GetString(root, "runtimeApiMax"), SourceHash = GetString(root, "sourceHash"), CookedContentHash = GetString(root, "cookedContentHash"), PackageHash = GetString(root, "packageHash")
            };
            if (GetInt(root, "manifestSchemaVersion") != ManifestSchemaVersion) d.Add(Error("package.manifest.schema", ManifestPath, "Unsupported manifest schema version."));
            if (!IsLowerHash(result.SourceHash) || !IsLowerHash(result.CookedContentHash) || !IsLowerHash(result.PackageHash)) d.Add(Error("package.manifest.hash", ManifestPath, "Manifest hashes must be lowercase SHA-256 hex."));
            if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                foreach (var x in deps.EnumerateArray()) { EnsureFields(x, new[] { "packageId", "version", "cookedHash" }, "dependencies", d); result.Dependencies.Add(new PackageDependencySource(GetString(x, "packageId"), GetString(x, "version"), GetString(x, "cookedHash"))); }
            else d.Add(Error("package.manifest.schema", "dependencies", "Dependencies must be an array."));
            if (root.TryGetProperty("capabilityRequirements", out var caps) && caps.ValueKind == JsonValueKind.Array)
                foreach (var x in caps.EnumerateArray()) { EnsureFields(x, new[] { "capabilityId", "capabilityVersion" }, "capabilityRequirements", d); result.Capabilities.Add(new CookedCapabilityRequirement(GetString(x, "capabilityId"), GetString(x, "capabilityVersion"))); }
            else d.Add(Error("package.manifest.schema", "capabilityRequirements", "Capability requirements must be an array."));
            if (root.TryGetProperty("payloads", out var payloads) && payloads.ValueKind == JsonValueKind.Array)
                foreach (var x in payloads.EnumerateArray()) { EnsureFields(x, new[] { "path", "sha256", "size" }, "payloads", d); result.Payloads.Add(new CharacterPackagePayloadInfo(GetString(x, "path"), GetString(x, "sha256"), GetInt64(x, "size"))); }
            else d.Add(Error("package.manifest.schema", "payloads", "Payloads must be an array."));
            if (!root.TryGetProperty("toolchain", out var toolchain) || toolchain.ValueKind != JsonValueKind.Object) d.Add(Error("package.manifest.schema", "toolchain", "Toolchain must be an object."));
            else { EnsureFields(toolchain, new[] { "cookerVersion", "unityVersion", "bindingSchemaVersion", "poseFormat", "poseVersion", "sampleRate" }, "toolchain", d); result.Toolchain = new ToolchainData(GetString(toolchain, "cookerVersion"), GetString(toolchain, "unityVersion"), GetInt(toolchain, "bindingSchemaVersion"), GetString(toolchain, "poseFormat"), GetInt(toolchain, "poseVersion"), GetInt(toolchain, "sampleRate")); }
            if (root.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
                foreach (var x in warnings.EnumerateArray())
                {
                    EnsureFields(x, new[] { "severity", "code", "path", "message" }, "warnings", d);
                    if (GetString(x, "severity") != "warning") d.Add(Error("package.warning.severity", "warnings.severity", "Warning severity must be warning."));
                    result.Warnings.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, GetString(x, "code"), GetString(x, "path"), GetString(x, "message")));
                }
            else d.Add(Error("package.manifest.schema", "warnings", "Warnings must be an array."));
            ValidateUniqueMetadata(result.Dependencies.Select(x => x.PackageId), "dependencies", d);
            ValidateUniqueMetadata(result.Capabilities.Select(x => x.CapabilityId), "capabilityRequirements", d);
            foreach (var dependency in result.Dependencies)
                if (!IsLowerHash(dependency.CookedHash)) d.Add(Error("package.manifest.hash", "dependencies.cookedHash", "Dependency cooked hash must be lowercase SHA-256 hex."));
            foreach (var capability in result.Capabilities)
                if (string.IsNullOrEmpty(capability.CapabilityVersion)) d.Add(Error("package.manifest.capability", "capabilityRequirements", "Capability version is required."));
            return result;
        }
        catch (Exception ex) { d.Add(Error("package.manifest.malformed", ManifestPath, ex.Message)); return null; }
    }

    private static byte[] WriteManifest(CharacterPackageAssemblyInput input, IReadOnlyList<CharacterPackagePayloadInfo> payloads, string cookedHash, string packageHash)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteManifestFields(w, input.PackageId, input.Version, input.Creator, input.License, input.Attribution, input.AuthoringSchemaVersion, input.CookedSchemaVersion, input.RuntimeApiMin, input.RuntimeApiMax, input.SourceHash, cookedHash, packageHash, input.Dependencies, input.CapabilityRequirements, payloads, new ToolchainData(input.CookerVersion, input.UnityVersion, input.BindingSchemaVersion, input.PoseFormat, input.PoseVersion, input.SampleRate), input.Warnings);
            w.Flush();
        }
        return stream.ToArray();
    }

    private static byte[] WriteManifest(ManifestData m, string packageHash)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteManifestFields(w, m.PackageId, m.Version, m.Creator, m.License, m.Attribution, m.AuthoringSchemaVersion, m.CookedSchemaVersion, m.RuntimeApiMin, m.RuntimeApiMax, m.SourceHash, m.CookedContentHash, packageHash, m.Dependencies, m.Capabilities, m.Payloads, m.Toolchain, m.Warnings);
            w.Flush();
        }
        return stream.ToArray();
    }

    private static void WriteManifestFields(Utf8JsonWriter w, string packageId, string version, string creator, string license, string attribution, ushort authoringSchema, ushort cookedSchema, string apiMin, string apiMax, string sourceHash, string cookedHash, string packageHash, IEnumerable<PackageDependencySource> dependencies, IEnumerable<CookedCapabilityRequirement> capabilities, IEnumerable<CharacterPackagePayloadInfo> payloads, ToolchainData toolchain, IEnumerable<CharacterDiagnostic> warnings)
    {
        w.WriteStartObject(); w.WriteNumber("manifestSchemaVersion", ManifestSchemaVersion); w.WriteString("packageId", packageId); w.WriteString("version", version); w.WriteString("creator", creator); w.WriteString("license", license); w.WriteString("attribution", attribution); w.WriteNumber("authoringSchemaVersion", authoringSchema); w.WriteNumber("cookedSchemaVersion", cookedSchema); w.WriteString("runtimeApiMin", apiMin); w.WriteString("runtimeApiMax", apiMax); w.WriteString("sourceHash", sourceHash); w.WriteString("cookedContentHash", cookedHash); w.WriteString("packageHash", packageHash);
        w.WritePropertyName("dependencies"); w.WriteStartArray(); foreach (var x in dependencies.OrderBy(x => x.PackageId, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal).ThenBy(x => x.CookedHash, StringComparer.Ordinal)) { w.WriteStartObject(); w.WriteString("packageId", x.PackageId); w.WriteString("version", x.Version); w.WriteString("cookedHash", x.CookedHash); w.WriteEndObject(); } w.WriteEndArray();
        w.WritePropertyName("capabilityRequirements"); w.WriteStartArray(); foreach (var x in capabilities.OrderBy(x => x.CapabilityId, StringComparer.Ordinal).ThenBy(x => x.CapabilityVersion, StringComparer.Ordinal)) { w.WriteStartObject(); w.WriteString("capabilityId", x.CapabilityId); w.WriteString("capabilityVersion", x.CapabilityVersion); w.WriteEndObject(); } w.WriteEndArray();
        w.WritePropertyName("payloads"); w.WriteStartArray(); foreach (var x in payloads) { w.WriteStartObject(); w.WriteString("path", x.Path); w.WriteString("sha256", x.Sha256); w.WriteNumber("size", x.Size); w.WriteEndObject(); } w.WriteEndArray();
        w.WritePropertyName("toolchain"); w.WriteStartObject(); w.WriteString("cookerVersion", toolchain.CookerVersion); w.WriteString("unityVersion", toolchain.UnityVersion); w.WriteNumber("bindingSchemaVersion", toolchain.BindingSchemaVersion); w.WriteString("poseFormat", toolchain.PoseFormat); w.WriteNumber("poseVersion", toolchain.PoseVersion); w.WriteNumber("sampleRate", toolchain.SampleRate); w.WriteEndObject();
        w.WritePropertyName("warnings"); w.WriteStartArray(); foreach (var x in warnings) { w.WriteStartObject(); w.WriteString("severity", "warning"); w.WriteString("code", x.Code); w.WriteString("path", x.Path); w.WriteString("message", x.Message); w.WriteEndObject(); } w.WriteEndArray(); w.WriteEndObject();
    }

    private static (string Path, byte[] Bytes)[] Payloads(IReadOnlyDictionary<string, byte[]> files) => PayloadPaths.Select(x => (x, files[x])).ToArray();
    private static byte[] FramedBytes(IEnumerable<(string Path, byte[] Bytes)> payloads)
    {
        using var stream = new MemoryStream();
        foreach (var payload in payloads)
        {
            byte[] path = Encoding.UTF8.GetBytes(payload.Path); Span<byte> u32 = stackalloc byte[4]; Span<byte> u64 = stackalloc byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)path.Length); BinaryPrimitives.WriteUInt64LittleEndian(u64, (ulong)payload.Bytes.LongLength); stream.Write(u32.ToArray(), 0, 4); stream.Write(path, 0, path.Length); stream.Write(u64.ToArray(), 0, 8); stream.Write(payload.Bytes, 0, payload.Bytes.Length);
        }
        return stream.ToArray();
    }
    private static string HashFramed(IEnumerable<(string Path, byte[] Bytes)> payloads) => Hash(FramedBytes(payloads));
    private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static byte[] Concat(byte[] first, byte[] second) { var result = new byte[first.Length + second.Length]; Buffer.BlockCopy(first, 0, result, 0, first.Length); Buffer.BlockCopy(second, 0, result, first.Length, second.Length); return result; }
    private static bool IsLowerHash(string value) => value.Length == 64 && value.All(x => (x >= '0' && x <= '9') || (x >= 'a' && x <= 'f'));
    private static bool StringEquals(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    private static void AddOptional(HashSet<string> set, string value, List<CharacterDiagnostic> d, string path) { if (!string.IsNullOrEmpty(value)) set.Add(value); }
    private static CharacterDiagnostic Error(string code, string path, string message) => new(CharacterDiagnosticSeverity.Error, code, path, message);
    private static void Add(HashSet<string> set, string value, List<CharacterDiagnostic> d, string path) { if (string.IsNullOrEmpty(value)) d.Add(Error("package.animation.invalid", path, "Animation ID is empty.")); else set.Add(value); }
    private static void ValidateUniqueMetadata(IEnumerable<string> values, string path, List<CharacterDiagnostic> d) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var value in values) if (string.IsNullOrEmpty(value) || !seen.Add(value)) d.Add(Error("package.metadata.duplicate", path, "Metadata contains a duplicate or empty identifier.")); }
    private static bool HasObject(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object;
    private static string GetString(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int GetInt(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.TryGetInt32(out int result) ? result : 0;
    private static ushort GetUInt16(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.TryGetUInt16(out ushort result) ? result : (ushort)0;
    private static long GetInt64(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.TryGetInt64(out long result) ? result : -1;
    private static void EnsureFields(JsonElement element, string[] allowed, string path, List<CharacterDiagnostic> d)
    {
        if (element.ValueKind != JsonValueKind.Object) { d.Add(Error("package.field.object", path, "Object is required.")); return; }
        var set = new HashSet<string>(allowed, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject()) { if (!set.Contains(p.Name)) d.Add(Error("package.field.unknown", path + "." + p.Name, "Unknown field.")); if (!seen.Add(p.Name)) d.Add(Error("package.field.duplicate", path + "." + p.Name, "Duplicate field.")); }
    }
    private static void EnsureFieldsOptional(JsonElement element, string[] allowed, string path, List<CharacterDiagnostic> d)
    {
        if (element.ValueKind != JsonValueKind.Object) { d.Add(Error("package.field.object", path, "Object is required.")); return; }
        var set = new HashSet<string>(allowed, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject()) { if (!set.Contains(p.Name)) d.Add(Error("package.field.unknown", path + "." + p.Name, "Unknown field.")); if (!seen.Add(p.Name)) d.Add(Error("package.field.duplicate", path + "." + p.Name, "Duplicate field.")); }
    }

    private sealed class BindingData
    {
        public string PackageId = ""; public string SourceHash = ""; public int BindingSchemaVersion; public string PoseFormat = ""; public int PoseVersion; public int SampleRate; public bool HasPresentations;
        public readonly Dictionary<string, BindingItem> BySemantic = new(StringComparer.Ordinal); public readonly Dictionary<string, BindingItem> ByPose = new(StringComparer.Ordinal); public readonly HashSet<string> Presentations = new(StringComparer.Ordinal);
    }
    private readonly record struct BindingItem(string SemanticId, string PoseTrackId, int FrameCount);
    private sealed class PoseData { public readonly HashSet<string> BoneNames = new(StringComparer.Ordinal); public readonly HashSet<string> Names = new(StringComparer.Ordinal); public readonly Dictionary<string, int> FrameCounts = new(StringComparer.Ordinal); }
    private sealed class ManifestData
    {
        public string PackageId = ""; public string Version = ""; public string Creator = ""; public string License = ""; public string Attribution = ""; public ushort AuthoringSchemaVersion; public ushort CookedSchemaVersion; public string RuntimeApiMin = ""; public string RuntimeApiMax = ""; public string SourceHash = ""; public string CookedContentHash = ""; public string PackageHash = "";
        public readonly List<PackageDependencySource> Dependencies = new(); public readonly List<CookedCapabilityRequirement> Capabilities = new(); public readonly List<CharacterPackagePayloadInfo> Payloads = new(); public readonly List<CharacterDiagnostic> Warnings = new(); public ToolchainData Toolchain = new("", "", 0, "", 0, 0);
    }
    private readonly record struct ToolchainData(string CookerVersion, string UnityVersion, int BindingSchemaVersion, string PoseFormat, int PoseVersion, int SampleRate);

    private sealed class PoseReader
    {
        private readonly byte[] _bytes; private int _offset;
        public PoseReader(byte[] bytes) { _bytes = bytes; }
        public uint ReadUInt32(string field, uint? expected = null) { Ensure(4); uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(_offset, 4)); _offset += 4; if (expected.HasValue && value != expected.Value) throw new InvalidDataException($"Unexpected {field}."); return value; }
        public string ReadString(string field) { uint length = ReadUInt32(field + " length"); if (length > int.MaxValue) throw new InvalidDataException($"{field} is too long."); Ensure((int)length); string value = new UTF8Encoding(false, true).GetString(_bytes, _offset, (int)length); _offset += (int)length; return value; }
        public void SkipFloats(long count)
        {
            long bytes = checked(count * 4);
            if (bytes > int.MaxValue || bytes < 0) throw new InvalidDataException("Pose frame data is too large.");
            Ensure((int)bytes);
            for (int i = _offset; i < _offset + (int)bytes; i += 4)
            {
                float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(i, 4)));
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Pose frame contains a non-finite value.");
            }
            _offset += (int)bytes;
        }
        public void RequireEnd() { if (_offset != _bytes.Length) throw new InvalidDataException("Pose payload has trailing bytes."); }
        private void Ensure(int count) { if (count < 0 || _offset > _bytes.Length - count) throw new EndOfStreamException("Pose payload is truncated."); }
    }
}
