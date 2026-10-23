using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public sealed class CharacterCookOutput
{
    public string PackageId;
    public string IntermediateDirectory;
    public string PoseFileName = "poses.bin";
    public string BindingFileName = "client.bindings";
    public string StatusFileName = "cook-status.json";
    public string GeneratedAssetPath;

    private CharacterCookOutput(string packageId)
    {
        PackageId = packageId;
        IntermediateDirectory = "Library/SlopArena/CharacterCook/" + packageId;
        string display = packageId == "fightguy" ? "FightGuy" : packageId;
        GeneratedAssetPath = "Assets/Resources/Generated/CharacterPackages/" + packageId + "/" + display + "_AnimationCatalog.asset";
    }

    public static CharacterCookOutput For(string packageId)
    {
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
            throw new ArgumentException("Package ID must be a stable lowercase identifier.", nameof(packageId));
        return new CharacterCookOutput(packageId);
    }

    public static CharacterCookOutput FightGuy => For("fightguy");
}

public sealed class CharacterCookDependencyRecord
{
    public string Kind = "";
    public string Identity = "";
    public string Guid = "";
    public string DependencyHash = "";
    public string MetaHash = "";
    public string ImporterSettings = "";
    public string Classification = "";
    public string SourcePackageId = "";
    public string SourcePath = "";
    public string ApprovalReason = "";
    public string ApprovalVersion = "";
}

public sealed class CharacterCookAnimationDefinition
{
    public string SemanticId = "";
    public string PoseTrackId = "";
    public AnimationClip Clip = null!;
    public string ClipGlobalObjectId = "";
    public string ClipAssetGuid = "";
    public string ClipAssetPath = "";
    public int FrameCount;
    public int ClipLengthBits;
    public int SampleRate;
    public ExtrapolationMode Extrapolation;
}

public sealed class CharacterAssetCookResult
{
    public CookedCharacterPackage? CookedPackage { get; internal set; }
    public byte[] PoseBytes { get; internal set; } = Array.Empty<byte>();
    public byte[] BindingBytes { get; internal set; } = Array.Empty<byte>();
    public IReadOnlyList<CharacterCookAnimationDefinition> Animations { get; internal set; } = Array.Empty<CharacterCookAnimationDefinition>();
    public IReadOnlyList<CharacterCookDependencyRecord> Dependencies { get; internal set; } = Array.Empty<CharacterCookDependencyRecord>();
    public IReadOnlyList<PackageDependencySource> PackageDependencies { get; internal set; } = Array.Empty<PackageDependencySource>();
    public IReadOnlyList<CookedCapabilityRequirement> CapabilityRequirements { get; internal set; } = Array.Empty<CookedCapabilityRequirement>();
    public string Creator { get; internal set; } = "";
    public string License { get; internal set; } = "";
    public string Attribution { get; internal set; } = "";
    public ushort AuthoringSchemaVersion { get; internal set; }
    public string SourceHash { get; internal set; } = "";
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; internal set; } = Array.Empty<CharacterDiagnostic>();
    public bool HasErrors => Diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
}

internal sealed class PackageMetadata
{
    public string Creator = "";
    public string License = "";
    public string Attribution = "";
    public ushort AuthoringSchemaVersion;
    public IReadOnlyList<PackageDependencySource> Dependencies = Array.Empty<PackageDependencySource>();
}
public static class UnityCharacterAssetCooker
{
    public const int BindingSchemaVersion = 2;
    public const int PoseVersion = 1;
    public const int SampleRate = 60;
    public const string CookerVersion = "package-presentation-2";

    public static CharacterAssetCookResult Cook(string packageRoot, CharacterAssetCatalog catalog, CharacterCookOutput output, CharacterCookProfile profile)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        string packagePath = ResolveFile(packageRoot, "package.json");
        string characterPath = ResolveFile(packageRoot, "character.json");
        if (!File.Exists(packagePath))
        {
            diagnostics.Add(Error("asset-catalog.schema", "package.json", "Package manifest is missing."));
            return Failure(diagnostics);
        }
        if (!File.Exists(characterPath))
        {
            diagnostics.Add(Error("asset-catalog.schema", "character.json", "Character document is missing."));
            return Failure(diagnostics);
        }

        string packageJson = File.ReadAllText(packagePath);
        string characterJson = File.ReadAllText(characterPath);
        CharacterCompileResult compiled = CharacterPackageCompiler.Compile(packageJson, characterJson, profile);
        diagnostics.AddRange(compiled.Diagnostics);
        if (compiled.CookedPackage == null || compiled.HasErrors)
            return Failure(diagnostics, compiled.CookedPackage);
        string packageId = ReadPackageId(packageJson);
        PackageMetadata packageMetadata = ReadPackageMetadata(packageJson, characterJson, diagnostics);
        ValidateCatalog(catalog, packageId, compiled.CookedPackage, diagnostics, out var requiredIds, instantiatePresentation: true);
        ValidateDependencyClassifications(packageRoot, catalog, diagnostics);
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return Failure(diagnostics, compiled.CookedPackage);

        var presentation = compiled.CookedPackage.Definition.Presentation;
        var definitions = new List<CharacterCookAnimationDefinition>(requiredIds.Count);
        foreach (var required in requiredIds)
        {
            CharacterAssetCatalog.AnimationBinding binding = catalog.Bindings.First(x => x.SemanticId == required.Id);
            string clipPath = AssetDatabase.GetAssetPath(binding.Clip);
            string clipGuid = AssetDatabase.AssetPathToGUID(clipPath);
            GlobalObjectId clipObjectId = GlobalObjectId.GetGlobalObjectIdSlow(binding.Clip);
            definitions.Add(new CharacterCookAnimationDefinition
            {
                SemanticId = required.Id,
                PoseTrackId = binding.PoseTrackId,
                Clip = binding.Clip,
                ClipGlobalObjectId = clipObjectId.ToString(),
                ClipAssetGuid = clipGuid,
                ClipAssetPath = NormalizeProjectPath(clipPath),
                FrameCount = Mathf.CeilToInt(binding.Clip.length * SampleRate),
                ClipLengthBits = BitConverter.SingleToInt32Bits(binding.Clip.length),
                SampleRate = SampleRate,
                Extrapolation = binding.Extrapolation,
            });
        }
        definitions.Sort((a, b) => StringComparer.Ordinal.Compare(a.SemanticId, b.SemanticId));

        IReadOnlyList<CharacterCookDependencyRecord> dependencies =
            CharacterCookDependencyTracker.Collect(packageRoot, catalog, definitions);
        string sourceHash = CharacterCookDependencyTracker.ComputeSourceHash(
            packageJson, characterJson, catalog, dependencies, definitions);
        byte[] poseBytes;
        try
        {
            poseBytes = DeterministicPoseTrackBaker.Bake(
                catalog.Rig,
                definitions.Select(x => new DeterministicPoseTrackBaker.SampledAnimation
                {
                    SemanticId = x.SemanticId,
                    PoseTrackId = x.PoseTrackId,
                    Clip = x.Clip,
                    FrameCount = x.FrameCount,
                }).ToArray(),
                SampleRate,
                catalog.WeaponConfig,
                presentation.VisualScale,
                presentation.ModelYOffset);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.rig.incompatible", "catalog.rig", ex.Message));
            return Failure(diagnostics, compiled.CookedPackage, dependencies, sourceHash);
        }
        try
        {
            var baked = BakedAnimationData.LoadFromBin(poseBytes);
            foreach (string attachmentId in compiled.CookedPackage.Definition.AttachmentBoneIds)
                if (baked.FindBoneIndex(attachmentId) < 0)
                    diagnostics.Add(Error("asset-catalog.attachment.missing", "character.attachmentBoneIds", $"Attachment '{attachmentId}' is missing from the baked pose payload."));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.pose.invalid", "poses.bin", ex.Message));
        }
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return Failure(diagnostics, compiled.CookedPackage, dependencies, sourceHash);

        byte[] bindings = CharacterBindingWriter.Write(catalog, definitions, sourceHash);
        diagnostics.AddRange(CharacterPackageAssembler.ValidateClientBindings(bindings));
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return Failure(diagnostics, compiled.CookedPackage, dependencies, sourceHash);
        return new CharacterAssetCookResult
        {
            CookedPackage = compiled.CookedPackage,
            PoseBytes = poseBytes,
            BindingBytes = bindings,
            Animations = definitions,
            Dependencies = dependencies,
            PackageDependencies = packageMetadata.Dependencies,
            CapabilityRequirements = compiled.CookedPackage.Definition.CapabilityRequirements
                .Select(x => new CookedCapabilityRequirement(x.CapabilityId, x.CapabilityVersion)).ToArray(),
            Creator = packageMetadata.Creator,
            License = packageMetadata.License,
            Attribution = packageMetadata.Attribution,
            AuthoringSchemaVersion = packageMetadata.AuthoringSchemaVersion,
            SourceHash = sourceHash,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// True for diagnostics that only mean a bound animation clip, presentation prefab
    /// or weapon prefab is not installed in this checkout. Such packages can still run in Editor
    /// play from their committed pose data (ADR-0038); every other error stays fatal.
    /// </summary>
    internal static bool IsMissingPresentationAsset(CharacterDiagnostic diagnostic)
        => diagnostic.Code is "asset-catalog.clip.missing" or "asset-catalog.presentation.missing" or "asset-catalog.weapon.missing";

    /// <summary>
    /// Compiles the current gameplay source and pairs it with the committed pose and
    /// binding payloads instead of re-sampling clips (ADR-0038). Used only for Editor
    /// play and preview when clips or presentation prefabs are not installed; the
    /// result is never published. Fails when the source changes an input the pose
    /// baker uses, because the committed poses would no longer match.
    /// </summary>
    internal static CharacterAssetCookResult CookWithCommittedPoses(
        string packageRoot,
        CharacterCookProfile profile,
        CookedCharacterPackage committedPackage,
        byte[] committedPoseBytes,
        byte[] committedBindingBytes,
        string committedSourceHash)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        string packageJson = File.ReadAllText(ResolveFile(packageRoot, "package.json"));
        string characterJson = File.ReadAllText(ResolveFile(packageRoot, "character.json"));
        CharacterCompileResult compiled = CharacterPackageCompiler.Compile(packageJson, characterJson, profile);
        diagnostics.AddRange(compiled.Diagnostics);
        if (compiled.CookedPackage == null || compiled.HasErrors)
            return Failure(diagnostics, compiled.CookedPackage);
        PackageMetadata packageMetadata = ReadPackageMetadata(packageJson, characterJson, diagnostics);

        CookedCharacterDefinition current = compiled.CookedPackage.Definition;
        CookedCharacterDefinition committed = committedPackage.Definition;
        void RequireUnchanged(bool unchanged, string path)
        {
            if (!unchanged)
                diagnostics.Add(Error("asset-catalog.pose.rebake-required", path,
                    "This edit changes how poses are baked, so it needs the animation clips to cook. Ask someone with the clips to cook it."));
        }
        RequireUnchanged(current.Presentation.VisualScale == committed.Presentation.VisualScale, "character.presentation.visualScale");
        RequireUnchanged(current.Presentation.HurtboxBoneScale == committed.Presentation.HurtboxBoneScale, "character.presentation.hurtboxBoneScale");
        RequireUnchanged(current.Presentation.ModelYOffset == committed.Presentation.ModelYOffset, "character.presentation.modelYOffset");
        RequireUnchanged(current.Presentation.Crouch == committed.Presentation.Crouch, "character.presentation.crouch");
        RequireUnchanged(current.Presentation.Slide == committed.Presentation.Slide, "character.presentation.slide");
        RequireUnchanged(current.CapsuleHeight == committed.CapsuleHeight, "character.capsuleHeight");
        RequireUnchanged(current.HipHeight == committed.HipHeight, "character.hipHeight");
        try
        {
            var baked = BakedAnimationData.LoadFromBin(committedPoseBytes);
            foreach (string attachmentId in current.AttachmentBoneIds)
                if (baked.FindBoneIndex(attachmentId) < 0)
                    diagnostics.Add(Error("asset-catalog.attachment.missing", "character.attachmentBoneIds", $"Attachment '{attachmentId}' is missing from the committed pose payload."));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.pose.invalid", "poses.bin", ex.Message));
        }
        if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return Failure(diagnostics, compiled.CookedPackage);

        return new CharacterAssetCookResult
        {
            CookedPackage = compiled.CookedPackage,
            PoseBytes = committedPoseBytes,
            BindingBytes = committedBindingBytes,
            PackageDependencies = packageMetadata.Dependencies,
            CapabilityRequirements = current.CapabilityRequirements
                .Select(x => new CookedCapabilityRequirement(x.CapabilityId, x.CapabilityVersion)).ToArray(),
            Creator = packageMetadata.Creator,
            License = packageMetadata.License,
            Attribution = packageMetadata.Attribution,
            AuthoringSchemaVersion = packageMetadata.AuthoringSchemaVersion,
            SourceHash = committedSourceHash,
            Diagnostics = diagnostics,
        };
    }

    internal static string ResolveFile(string packageRoot, string fileName)
    {
        string root = packageRoot;
        if (!Path.IsPathRooted(root)) root = Path.Combine(ProjectRoot(), root);
        return Path.Combine(root, fileName);
    }

    internal static string ProjectRoot()
        => Directory.GetParent(Application.dataPath)!.FullName;

    internal static CharacterPackageAssemblyInput BuildPackageInput(CharacterAssetCookResult result)
    {
        if (result.CookedPackage == null) throw new InvalidOperationException("Cook result has no cooked package.");
        return new CharacterPackageAssemblyInput(
            result.CookedPackage.Metadata.PackageId,
            result.CookedPackage.Metadata.Version,
            result.Creator,
            result.License,
            result.Attribution,
            result.AuthoringSchemaVersion,
            result.CookedPackage.Metadata.CookedSchemaVersion,
            result.CookedPackage.Metadata.RuntimeApiMin,
            result.CookedPackage.Metadata.RuntimeApiMax,
            result.SourceHash,
            result.PackageDependencies,
            result.CapabilityRequirements,
            CookerVersion,
            Application.unityVersion,
            BindingSchemaVersion,
            "SKEL",
            PoseVersion,
            SampleRate,
            result.Diagnostics.Where(x => x.Severity == CharacterDiagnosticSeverity.Warning).ToArray(),
            result.CookedPackage.CanonicalBytes,
            result.PoseBytes,
            result.BindingBytes,
            result.CookedPackage);
    }

    internal static bool TryComputeSourceHash(string packageRoot, CharacterAssetCatalog catalog, CharacterCookProfile profile, out string sourceHash, out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var errors = new List<CharacterDiagnostic>();
        sourceHash = "";
        try
        {
            string packageJson = File.ReadAllText(ResolveFile(packageRoot, "package.json"));
            string characterJson = File.ReadAllText(ResolveFile(packageRoot, "character.json"));
            CharacterCompileResult compiled = CharacterPackageCompiler.Compile(packageJson, characterJson, profile);
            errors.AddRange(compiled.Diagnostics);
            if (compiled.CookedPackage == null || compiled.HasErrors)
            {
                diagnostics = errors;
                return false;
            }
            ValidateCatalog(catalog, ReadPackageId(packageJson), compiled.CookedPackage, errors, out var requiredIds);
            ValidateDependencyClassifications(packageRoot, catalog, errors);
            if (errors.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            {
                diagnostics = errors;
                return false;
            }
            var definitions = new List<CharacterCookAnimationDefinition>(requiredIds.Count);
            foreach (var required in requiredIds)
            {
                var binding = catalog.Bindings.First(x => x.SemanticId == required.Id);
                string clipPath = AssetDatabase.GetAssetPath(binding.Clip);
                definitions.Add(new CharacterCookAnimationDefinition
                {
                    SemanticId = required.Id,
                    PoseTrackId = binding.PoseTrackId,
                    Clip = binding.Clip,
                    ClipGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(binding.Clip).ToString(),
                    ClipAssetGuid = AssetDatabase.AssetPathToGUID(clipPath),
                    ClipAssetPath = NormalizeProjectPath(clipPath),
                    FrameCount = Mathf.CeilToInt(binding.Clip.length * SampleRate),
                    ClipLengthBits = BitConverter.SingleToInt32Bits(binding.Clip.length),
                    SampleRate = SampleRate,
                    Extrapolation = binding.Extrapolation,
                });
            }
            definitions.Sort((a, b) => StringComparer.Ordinal.Compare(a.SemanticId, b.SemanticId));
            var dependencies = CharacterCookDependencyTracker.Collect(packageRoot, catalog, definitions);
            sourceHash = CharacterCookDependencyTracker.ComputeSourceHash(packageJson, characterJson, catalog, dependencies, definitions);
            diagnostics = errors;
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(Error("asset-catalog.schema", "source-hash", ex.Message));
            diagnostics = errors;
            return false;
        }
    }

    internal static string NormalizeProjectPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string root = ProjectRoot().Replace('\\', '/').TrimEnd('/') + "/";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(root.Length)
            : path.Replace('\\', '/');
    }

    private static PackageMetadata ReadPackageMetadata(string packageJson, string characterJson, List<CharacterDiagnostic> diagnostics)
    {
        var result = new PackageMetadata();
        try
        {
            using var packageDocument = JsonDocument.Parse(packageJson);
            var package = packageDocument.RootElement;
            result.Creator = package.TryGetProperty("creator", out var creator) ? creator.GetString() ?? "" : "";
            result.License = package.TryGetProperty("license", out var license) ? license.GetString() ?? "" : "";
            result.Attribution = package.TryGetProperty("attribution", out var attribution) ? attribution.GetString() ?? "" : "";
            var dependencies = new List<PackageDependencySource>();
            if (package.TryGetProperty("dependencies", out var dependencyArray) && dependencyArray.ValueKind == JsonValueKind.Array)
                foreach (var dependency in dependencyArray.EnumerateArray())
                    dependencies.Add(new PackageDependencySource(
                        dependency.GetProperty("packageId").GetString() ?? "",
                        dependency.GetProperty("version").GetString() ?? "",
                        dependency.GetProperty("cookedHash").GetString() ?? ""));
            result.Dependencies = dependencies;

            using var characterDocument = JsonDocument.Parse(characterJson);
            result.AuthoringSchemaVersion = characterDocument.RootElement.TryGetProperty("authoringSchemaVersion", out var schema)
                ? schema.GetUInt16()
                : (ushort)0;
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.schema", "package.metadata", ex.Message));
        }
        return result;
    }

    private static string ReadPackageId(string packageJson)
    {
        try { return JsonDocument.Parse(packageJson).RootElement.GetProperty("packageId").GetString() ?? ""; }
        catch { return ""; }
    }
    private static void ValidateCatalog(
        CharacterAssetCatalog catalog,
        string packageId,
        CookedCharacterPackage package,
        List<CharacterDiagnostic> diagnostics,
        out List<(string Id, string Path)> requiredIds, bool instantiatePresentation = false)
    {
        requiredIds = RequiredIds(package);
        if (catalog == null)
        {
            diagnostics.Add(Error("asset-catalog.schema", "catalog", "Character asset catalog is missing."));
            return;
        }
        if (catalog.PackageId != packageId || catalog.CatalogSchemaVersion != CharacterAssetCatalog.SchemaVersion)
            diagnostics.Add(Error("asset-catalog.schema", "catalog", "Catalog package ID/schema is unsupported."));
        if (catalog.SampleRate != SampleRate)
            diagnostics.Add(Error("asset-catalog.sample.invalid", "catalog.sampleRate", "Catalog sample rate must be exactly 60 Hz."));
        if (catalog.Rig == null)
        {
            diagnostics.Add(Error("asset-catalog.rig.missing", "catalog.rig", "Catalog rig is missing."));
        }
        else
        {
            string rigPath = AssetDatabase.GetAssetPath(catalog.Rig);
            string rigExtension = Path.GetExtension(rigPath).ToLowerInvariant();
            if (rigExtension != ".prefab" && rigExtension != ".fbx")
                diagnostics.Add(Error("asset-catalog.rig.invalid", "catalog.rig", "Rig must be a prefab or imported model asset."));
            Animator animator = catalog.Rig.GetComponent<Animator>();
            bool validHumanoid = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            if (animator == null)
                diagnostics.Add(Error("asset-catalog.rig.invalid", "catalog.rig", "Catalog rig has no Animator."));
            else if (!validHumanoid)
                diagnostics.Add(Error("asset-catalog.rig.incompatible", "catalog.rig", "Catalog rig requires a valid Humanoid Avatar."));
            if (validHumanoid)
                foreach (HumanBodyBones bone in DeterministicPoseTrackBaker.RequiredBones)
                    if (animator.GetBoneTransform(bone) == null)
                        diagnostics.Add(Error("asset-catalog.bone.missing", $"catalog.rig.{bone}", $"Required humanoid bone is missing: {bone}."));
        }
        if (catalog.WeaponConfig != null)
            ValidateWeaponConfig(catalog, diagnostics);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTracks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
        {
            string id = binding?.SemanticId ?? "";
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("anim.", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("asset-catalog.id.invalid", $"catalog.bindings[{id}]", "Binding semantic ID must use the anim.* namespace."));
                continue;
            }
            if (!seenIds.Add(id)) diagnostics.Add(Error("asset-catalog.id.duplicate", $"catalog.bindings[{id}]", "Binding semantic ID is duplicated."));
            if (string.IsNullOrWhiteSpace(binding.PoseTrackId))
                diagnostics.Add(Error("asset-catalog.id.invalid", $"catalog.bindings[{id}].poseTrackId", "Pose track ID is empty."));
            else if (!seenTracks.Add(binding.PoseTrackId))
                diagnostics.Add(Error("asset-catalog.id.duplicate", $"catalog.bindings[{id}].poseTrackId", "Pose track ID is duplicated."));
            if (binding.Clip == null)
            {
                diagnostics.Add(Error("asset-catalog.clip.missing", $"catalog.bindings[{id}].clip", "Animation clip is missing."));
                continue;
            }
            string clipPath = AssetDatabase.GetAssetPath(binding.Clip);
            string extension = Path.GetExtension(clipPath).ToLowerInvariant();
            if (extension != ".anim" && extension != ".fbx")
                diagnostics.Add(Error("asset-catalog.clip.unsupported", $"catalog.bindings[{id}].clip", "Clip must be a direct .anim or imported model sub-asset."));
            if (string.IsNullOrEmpty(clipPath) || GlobalObjectId.GetGlobalObjectIdSlow(binding.Clip).ToString().Length == 0)
                diagnostics.Add(Error("asset-catalog.clip.unresolved", $"catalog.bindings[{id}].clip", "Clip has no stable Unity global object ID."));
            if (float.IsNaN(binding.Clip.length) || float.IsInfinity(binding.Clip.length) || binding.Clip.length <= 0f)
                diagnostics.Add(Error("asset-catalog.clip.unsupported", $"catalog.bindings[{id}].clip", "Clip length must be finite and positive."));
            if (extension == ".fbx")
            {
                var importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;
                if (importer == null || importer.animationType != ModelImporterAnimationType.Human)
                    diagnostics.Add(Error("asset-catalog.clip.unsupported", $"catalog.bindings[{id}].clip", "Imported clip source is not Humanoid."));
            }
        }
        ValidatePresentationBindings(catalog, packageId, package, diagnostics);
        CharacterPresentationProfileValidation.Validate(catalog, diagnostics, instantiatePresentation);
        var requiredSet = new HashSet<string>(requiredIds.Select(x => x.Id), StringComparer.Ordinal);

        foreach (string id in seenIds)
            if (!requiredSet.Contains(id)) diagnostics.Add(Error("asset-catalog.id.orphan", $"catalog.bindings[{id}]", "Binding is not referenced by the canonical document."));
        foreach (var required in requiredIds)
            if (!seenIds.Contains(required.Id))
                diagnostics.Add(Error("reference.animation.unresolved", required.Path, $"Animation ID '{required.Id}' is not bound by the catalog."));
    }
    private static void ValidatePresentationBindings(
        CharacterAssetCatalog catalog,
        string packageId,
        CookedCharacterPackage package,
        List<CharacterDiagnostic> diagnostics)
    {
        if (catalog.Presentations == null || catalog.Presentations.Length == 0)
        {
            foreach (string id in package.Definition.PresentationIds)
                diagnostics.Add(Error("reference.presentation.unresolved", "character.presentationIds", $"Presentation ID '{id}' is not bound by the catalog."));
            foreach (var slot in package.Definition.Slots)
                if (!string.IsNullOrEmpty(slot.HitPresentationId))
                    diagnostics.Add(Error("reference.presentation.unresolved",
                        $"character.slots[{slot.Ordinal}].hitPresentationId",
                        $"Hit presentation '{slot.HitPresentationId}' is not bound by the catalog."));
            return;
        }
        var required = new HashSet<string>(package.Definition.PresentationIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in catalog.Presentations)
        {
            string id = binding?.SemanticId ?? "";
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("presentation.", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("asset-catalog.presentation.id.invalid", $"catalog.presentations[{id}]", "Presentation semantic ID must use the presentation.* namespace."));
                continue;
            }
            if (!seen.Add(id))
                diagnostics.Add(Error("asset-catalog.presentation.id.duplicate", $"catalog.presentations[{id}]", "Presentation semantic ID is duplicated."));
            if (binding.Prefab == null)
            {
                diagnostics.Add(Error("asset-catalog.presentation.missing", $"catalog.presentations[{id}].prefab", "Presentation prefab is missing."));
                continue;
            }
            string prefabPath = NormalizeProjectPath(AssetDatabase.GetAssetPath(binding.Prefab));
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Error("asset-catalog.presentation.invalid", $"catalog.presentations[{id}].prefab", "Presentation binding must reference a prefab."));
            if (!CharacterPackageAssetOwnershipRegistry.IsOwnedBy(packageId, prefabPath))
                diagnostics.Add(Error("asset-catalog.presentation.foreign", $"catalog.presentations[{id}].prefab", "Presentation prefab must be owned by the target character package."));
            if (GlobalObjectId.GetGlobalObjectIdSlow(binding.Prefab).ToString().Length == 0)
                diagnostics.Add(Error("asset-catalog.presentation.unresolved", $"catalog.presentations[{id}].prefab", "Presentation prefab has no stable Unity global object ID."));
        }
        foreach (string id in seen)
            if (!required.Contains(id))
                diagnostics.Add(Error("asset-catalog.presentation.orphan", $"catalog.presentations[{id}]", "Presentation binding is not referenced by the canonical document."));
        foreach (string id in required)
            if (!seen.Contains(id))
                diagnostics.Add(Error("reference.presentation.unresolved", "character.presentationIds", $"Presentation ID '{id}' is not bound by the catalog."));
    }

    private static void ValidateDependencyClassifications(
        string packageRoot,
        CharacterAssetCatalog catalog,
        List<CharacterDiagnostic> diagnostics)
    {
        ValidateDependency(packageRoot, catalog?.Rig == null ? "" : NormalizeProjectPath(AssetDatabase.GetAssetPath(catalog.Rig)), "catalog.rig", diagnostics);
        if (catalog?.WeaponConfig != null)
            ValidateDependency(packageRoot, NormalizeProjectPath(AssetDatabase.GetAssetPath(catalog.WeaponConfig)), "catalog.weaponConfig", diagnostics);
        foreach (var binding in catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
        {
            string path = binding?.Clip == null ? "" : NormalizeProjectPath(AssetDatabase.GetAssetPath(binding.Clip));
            ValidateDependency(packageRoot, path, $"catalog.bindings[{binding?.SemanticId ?? ""}].clip", diagnostics);
        }
    }

    private static void ValidateDependency(
        string packageRoot,
        string assetPath,
        string diagnosticPath,
        List<CharacterDiagnostic> diagnostics)
    {
        CharacterPackageDependencyInfo dependency = CharacterPackageAuthoringService.ClassifyDependency(packageRoot, assetPath);
        if (dependency.Classification == "foreign")
            diagnostics.Add(Error("asset-catalog.dependency.foreign", diagnosticPath, $"Dependency '{assetPath}' is foreign to the target package and is not approved shared content."));
    }

    private static void ValidateWeaponConfig(CharacterAssetCatalog catalog, List<CharacterDiagnostic> diagnostics)
    {
        if (catalog.WeaponConfig == null)
            return;
        var entry = Array.Find(catalog.WeaponConfig.Entries ?? Array.Empty<SlopArena.Client.Entities.WeaponEntry>(),
            x => x != null);
        if (entry == null)
        {
            diagnostics.Add(Error("asset-catalog.weapon.invalid", "catalog.weaponConfig", "Weapon config has no entries."));
            return;
        }
        if (entry.Prefab == null)
            diagnostics.Add(Error("asset-catalog.weapon.missing", "catalog.weaponConfig.entries", "Weapon entry prefab is missing."));
        if (catalog.Rig == null || !catalog.Rig.GetComponentsInChildren<Transform>(true).Any(x => x.name == entry.BoneName))
            diagnostics.Add(Error("asset-catalog.rig.incompatible", "catalog.rig", $"Weapon config bone is missing: {entry.BoneName}."));
        if (GlobalObjectId.GetGlobalObjectIdSlow(catalog.WeaponConfig).ToString().Length == 0)
            diagnostics.Add(Error("asset-catalog.weapon.unresolved", "catalog.weaponConfig", "Weapon config has no stable Unity global object ID."));
    }

    private static List<(string Id, string Path)> RequiredIds(CookedCharacterPackage package)
    {
        var result = new List<(string Id, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string id, string path)
        {
            if (!string.IsNullOrEmpty(id) && seen.Add(id)) result.Add((id, path));
        }
        var p = package.Definition.Presentation;
        Add(p.Idle, "character.presentation.idle");
        Add(p.Run, "character.presentation.run");
        Add(p.Dash, "character.presentation.dash");
        Add(p.Jump, "character.presentation.jump");
        Add(p.Fall, "character.presentation.fall");
        Add(p.Crouch, "character.presentation.crouch");
        Add(p.Slide, "character.presentation.slide");
        Add(p.Tumble, "character.presentation.tumble");
        Add(p.HitSmall, "character.presentation.hitSmall");
        Add(p.HitMedium, "character.presentation.hitMedium");
        Add(p.HitHard, "character.presentation.hitHard");
        foreach (var slot in package.Definition.Slots)
        {
            if (!string.IsNullOrEmpty(slot.AimAnimationId))
                Add(slot.AimAnimationId, $"character.slots[{slot.Ordinal}].aimAnimationId");
            for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
                for (int idIndex = 0; idIndex < slot.Timeline.Stages[stageIndex].AnimationIds.Count; idIndex++)
                {
                    string id = slot.Timeline.Stages[stageIndex].AnimationIds[idIndex];
                    Add(id, $"character.slots[{slot.Ordinal}].timeline.stages[{stageIndex}].animationIds[{idIndex}]");
                }
        }
        return result;
    }

    internal static List<(string Id, string Path)> GetRequiredIds(CookedCharacterPackage package)
        => RequiredIds(package);

    private static CharacterAssetCookResult Failure(
        List<CharacterDiagnostic> diagnostics,
        CookedCharacterPackage? package = null,
        IReadOnlyList<CharacterCookDependencyRecord>? dependencies = null,
        string sourceHash = "")
        => new CharacterAssetCookResult
        {
            CookedPackage = package,
            Diagnostics = diagnostics,
            Dependencies = dependencies ?? Array.Empty<CharacterCookDependencyRecord>(),
            SourceHash = sourceHash,
        };

    internal static CharacterDiagnostic Error(string code, string path, string message)
        => new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message);
}
