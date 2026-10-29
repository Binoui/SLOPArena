using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public sealed class CharacterPackageAuthoringService
{
    private const string CharacterPackagesRoot = "Assets/CharacterPackages";
    private readonly string _projectRoot;
    private Func<byte[], string, string> _generateCatalog = CharacterAnimationCatalogGenerator.Generate;
    private Action<string> _publicationCheckpoint;

    public CharacterPackageAuthoringService(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
        _projectRoot = Path.GetFullPath(projectRoot);
    }
    private static CharacterAssetCatalog.AnimationBinding[] CreateStarterBindings(CharacterAuthoringDocument character)
    {
        var ids = new[]
        {
            character.Presentation.Idle, character.Presentation.Run, character.Presentation.Dash,
            character.Presentation.Jump, character.Presentation.Fall, character.Presentation.Tumble,
            character.Presentation.HitSmall, character.Presentation.HitMedium, character.Presentation.HitHard,
        }.Concat((character.Slots ?? Array.Empty<CharacterSlotSource>())
            .SelectMany(slot => slot.Timeline?.Stages ?? Array.Empty<CharacterStageSource>())
            .SelectMany(stage => stage.AnimationIds ?? Array.Empty<string>()))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);
        return ids.Select(id => new CharacterAssetCatalog.AnimationBinding
        {
            SemanticId = id,
            PoseTrackId = id,
        }).ToArray();
    }

    public CharacterPackageCreateResult NewPackage(
        string packageId,
        string displayName,
        string creator = "Binoui",
        string license = "MIT",
        string attribution = "SlopArena")
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
            return CharacterPackageCreateResult.Failure(null, null, null, new[] { Error("id.invalid", "packageId", "Package ID must be a stable lowercase identifier.") });

        string root = CharacterPackagesRoot + "/" + packageId;
        string fullRoot = Path.Combine(_projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(fullRoot) && Directory.EnumerateFileSystemEntries(fullRoot).Any())
            return CharacterPackageCreateResult.Failure(packageId, root, root + "/CharacterAssetCatalog.asset",
                new[] { Error("package.exists", root, "Package folder already exists and is not empty.") });

        bool createdRoot = !Directory.Exists(fullRoot);
        string catalogPath = root + "/CharacterAssetCatalog.asset";
        try
        {
            Directory.CreateDirectory(fullRoot);
            CharacterPackageSource source = CharacterPackageSourceCodec.CreateAuthoringReady(packageId, displayName, creator, license, attribution);
            WriteDurably(Path.Combine(fullRoot, "package.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeManifest(source.Manifest)));
            WriteDurably(Path.Combine(fullRoot, "character.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeCharacter(source.Character)));
            var catalog = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
            catalog.SetPresentationProfile(new CharacterPresentationProfile());
            catalog.PackageId = packageId;
            catalog.CatalogSchemaVersion = CharacterAssetCatalog.SchemaVersion;
            catalog.SampleRate = UnityCharacterAssetCooker.SampleRate;
            catalog.Rig = null;
            catalog.Bindings = CreateStarterBindings(source.Character);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.IsValidFolder(root))
            {
                string parent = "Assets/CharacterPackages";
                AssetDatabase.CreateFolder(parent, packageId);
            }
            AssetDatabase.CreateAsset(catalog, catalogPath);
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath) == null)
                throw new InvalidOperationException("Unity did not import the new CharacterAssetCatalog asset.");
            return CharacterPackageCreateResult.Successful(packageId, root, catalogPath);
        }
        catch (Exception ex)
        {
            AssetDatabase.DeleteAsset(catalogPath);
            foreach (string file in new[] { "package.json", "character.json" })
            {
                string path = Path.Combine(fullRoot, file);
                if (File.Exists(path)) File.Delete(path);
            }
            if (createdRoot && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
            diagnostics.Add(Error("package.create.failed", root, ex.Message));
            return CharacterPackageCreateResult.Failure(packageId, root, catalogPath, diagnostics);
        }
    }

    public CharacterPackageBindingResult Bind(string target, string semanticId, string assetPath, ExtrapolationMode? extrapolation = null)
    {
        string normalizedPath = NormalizeProjectAssetPath(assetPath);
        var clips = string.IsNullOrEmpty(normalizedPath) ? Array.Empty<AnimationClip>() :
            AssetDatabase.LoadAllAssetsAtPath(normalizedPath).OfType<AnimationClip>().ToArray();
        if (clips.Length > 1)
            return CharacterPackageBindingResult.Failure(null, target, semanticId, normalizedPath,
                new[] { Error("binding.asset.ambiguous", normalizedPath, "Asset contains multiple AnimationClips. Select the clip object explicitly.") });
        return BindClip(target, semanticId, clips.SingleOrDefault(), normalizedPath, extrapolation);
    }

    public CharacterPackageBindingResult Bind(string target, string semanticId, AnimationClip clip, ExtrapolationMode? extrapolation = null)
    {
        return BindClip(target, semanticId, clip, clip == null ? "" : AssetDatabase.GetAssetPath(clip), extrapolation);
    }

    private CharacterPackageBindingResult BindClip(string target, string semanticId, AnimationClip clip, string assetPath, ExtrapolationMode? extrapolation)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageBindingResult.Failure(package?.PackageId, package?.ProjectRelativeRoot, semanticId, assetPath, diagnostics);
        if (package.Catalog == null)
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, assetPath,
                diagnostics.Concat(new[] { Error("asset-catalog.missing", package.ProjectRelativeRoot + "/CharacterAssetCatalog.asset", "Character asset catalog is required.") }));
        if (string.IsNullOrWhiteSpace(semanticId))
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, assetPath,
                diagnostics.Concat(new[] { Error("binding.semantic-id.missing", "semanticId", "Semantic ID is required.") }));
        var binding = (package.Catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(x => x != null && StringEquals(x.SemanticId, semanticId));
        if (binding == null)
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, assetPath,
                diagnostics.Concat(new[] { Error("binding.semantic-id.missing", semanticId, "Catalog binding does not exist; automatic binding creation is not supported.") }));

        string normalizedPath = NormalizeProjectAssetPath(assetPath);
        if (clip == null || string.IsNullOrEmpty(normalizedPath) || !AssetDatabase.Contains(clip))
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, normalizedPath,
                diagnostics.Concat(new[] { Error("binding.asset.invalid", normalizedPath, "Select a persisted AnimationClip or animation subasset.") }));
        string selectedObjectId = GlobalObjectId.GetGlobalObjectIdSlow(clip).ToString();

        CharacterPackageDependencyInfo classification = ClassifyDependency(package.ProjectRelativeRoot, normalizedPath);
        if (classification.Classification == "foreign")
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, normalizedPath,
                diagnostics.Concat(new[] { Error("asset-catalog.dependency.foreign", normalizedPath, "Binding references an asset outside the target package and approved shared registry.") }),
                classification);

        binding.Clip = clip;
        if (extrapolation.HasValue) binding.Extrapolation = extrapolation.Value;
        if (!PersistCatalog(package.ProjectRelativeRoot, package.Catalog, out var persistedCatalog, out _, out var persistDiagnostics))
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, normalizedPath,
                diagnostics.Concat(persistDiagnostics), classification);
        var persistedBinding = (persistedCatalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(x => x != null && StringEquals(x.SemanticId, semanticId));
        if (persistedBinding == null || persistedBinding.Clip == null ||
            GlobalObjectId.GetGlobalObjectIdSlow(persistedBinding.Clip).ToString() != selectedObjectId)
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, normalizedPath,
                diagnostics.Concat(new[] { Error("binding.persist.failed", normalizedPath, "Catalog binding did not round-trip after persistence.") }), classification);
        return CharacterPackageBindingResult.Successful(package.PackageId, package.ProjectRelativeRoot, semanticId, normalizedPath, classification, persistedBinding);
    }

    public CharacterPackageBindingResult Unbind(string target, string semanticId)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageBindingResult.Failure(package?.PackageId, package?.ProjectRelativeRoot, semanticId, "", diagnostics);
        var binding = (package.Catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(x => x != null && StringEquals(x.SemanticId, semanticId));
        if (binding == null)
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, "",
                diagnostics.Concat(new[] { Error("binding.semantic-id.missing", semanticId, "Catalog binding does not exist; automatic binding creation is not supported.") }));
        string priorPath = binding.Clip == null ? "" : NormalizeProjectAssetPath(AssetDatabase.GetAssetPath(binding.Clip));
        CharacterPackageDependencyInfo priorClassification = string.IsNullOrEmpty(priorPath)
            ? CharacterPackageDependencyInfo.Missing("")
            : ClassifyDependency(package.ProjectRelativeRoot, priorPath);
        binding.Clip = null;
        if (!PersistCatalog(package.ProjectRelativeRoot, package.Catalog, out var persistedCatalog, out _, out var persistDiagnostics))
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, priorPath,
                diagnostics.Concat(persistDiagnostics), priorClassification);
        var persistedBinding = (persistedCatalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(x => x != null && StringEquals(x.SemanticId, semanticId));
        if (persistedBinding == null || persistedBinding.Clip != null)
            return CharacterPackageBindingResult.Failure(package.PackageId, package.ProjectRelativeRoot, semanticId, "",
                diagnostics.Concat(new[] { Error("binding.persist.failed", semanticId, "Catalog unbind did not round-trip after persistence.") }),
                CharacterPackageDependencyInfo.Missing(""));
        return CharacterPackageBindingResult.Successful(package.PackageId, package.ProjectRelativeRoot, semanticId, "", CharacterPackageDependencyInfo.Missing(""), persistedBinding);
    }

    public CharacterPackageInspectionResult Inspect(string target)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageInspectionResult.CreateFailure(diagnostics);

        var compilerResult = package.Source == null
            ? null
            : CharacterPackageCompiler.Compile(package.PackageJson, package.CharacterJson, ProfileFor(package.PackageId));
        AddUnique(diagnostics, compilerResult?.Diagnostics);

        string currentSourceHash = null;
        if (package.Source != null && package.Catalog != null && compilerResult?.CookedPackage != null && !compilerResult.HasErrors)
        {
            if (UnityCharacterAssetCooker.TryComputeSourceHash(
                    package.ProjectRelativeRoot,
                    package.Catalog,
                    ProfileFor(package.PackageId),
                    out var computedHash,
                    out var hashDiagnostics))
            {
                currentSourceHash = computedHash;
            }
            AddUnique(diagnostics, hashDiagnostics);
        }

        ArtifactSnapshot artifact = ReadArtifact(package.PackageId);
        AddUnique(diagnostics, artifact.Diagnostics);
        var slots = BuildSlotSummaries(package.Source, compilerResult?.CookedPackage);
        bool hasErrors = diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
        string status;
        bool dirtyOrStale;
        var staleReasons = new List<CharacterPackageStaleReason>();

        if (hasErrors)
        {
            status = "invalid";
            dirtyOrStale = true;
            if (package.Source == null) staleReasons.Add(new CharacterPackageStaleReason("source-invalid", package.ProjectRelativeRoot + "/character.json", "Character source could not be parsed."));
            if (package.Catalog == null) staleReasons.Add(new CharacterPackageStaleReason("catalog-invalid", package.ProjectRelativeRoot + "/CharacterAssetCatalog.asset", "Character asset catalog is missing or invalid."));
            if (artifact.IsInvalid) staleReasons.Add(new CharacterPackageStaleReason("artifact-invalid", CookedPath(package.PackageId), "Cooked package verification failed."));
            if (staleReasons.Count == 0) staleReasons.Add(new CharacterPackageStaleReason("source-invalid", package.ProjectRelativeRoot, "Character source failed compiler validation."));
        }
        else if (artifact.IsInvalid)
        {
            status = "invalid";
            dirtyOrStale = true;
            staleReasons.Add(new CharacterPackageStaleReason("artifact-invalid", CookedPath(package.PackageId), "Cooked package verification failed."));
        }
        else if (artifact.IsMissing)
        {
            status = "missing";
            dirtyOrStale = false;
            staleReasons.Add(new CharacterPackageStaleReason("cooked-missing", CookedPath(package.PackageId), "No cooked package is present."));
        }
        else if (!StringEquals(currentSourceHash, artifact.SourceHash))
        {
            status = "stale";
            dirtyOrStale = true;
            staleReasons.Add(new CharacterPackageStaleReason("cook-input-changed", package.ProjectRelativeRoot, "Current source or Unity-owned cook dependencies differ from the cooked package."));
        }
        else
        {
            status = "valid";
            dirtyOrStale = false;
        }

        CharacterCookStatus cookStatus = ReadStatus(package.PackageId);
        CharacterPackageProvenance provenance = artifact.Manifest == null
            ? null
            : CharacterPackageProvenance.Create(
                package.ProjectRelativeRoot,
                CookedPath(package.PackageId),
                artifact.Manifest,
                ProfileFor(package.PackageId),
                cookStatus);
        (bool rostered, string selector) roster = ReadRosterState(package.PackageId);
        bool previewReady = artifact.Manifest != null && !artifact.IsInvalid && !artifact.IsMissing;
        return CharacterPackageInspectionResult.CreateSuccess(
            package.PackageId,
            package.DisplayName,
            package.ProjectRelativeRoot,
            status,
            dirtyOrStale,
            currentSourceHash,
            artifact.SourceHash,
            artifact.CookedContentHash,
            artifact.PackageHash,
            staleReasons,
            slots,
            diagnostics,
            package.Source,
            package.Catalog,
            provenance,
            roster.rostered,
            roster.selector,
            previewReady);
    }
    public CharacterPackageVerificationResult Verify(string target)
    {
        CharacterPackageInspectionResult inspection = Inspect(target);
        var diagnostics = new List<CharacterDiagnostic>(inspection.RawDiagnostics ?? Array.Empty<CharacterDiagnostic>());
        if (!inspection.Success)
            return CharacterPackageVerificationResult.Failure(inspection.PackageId, diagnostics, inspection, null);

        CharacterPackageCookResult plan = Cook(target, true);
        AddUnique(diagnostics, plan.RawDiagnostics);
        if (!StringEquals(inspection.Status, "valid"))
            diagnostics.Add(Error("verify.cooked.invalid", inspection.SourcePath ?? target, "Package inspection is not a verified, current cooked artifact."));
        if (plan.Success && !StringEquals(plan.CookedContentHash, inspection.CookedContentHash))
            diagnostics.Add(Error("verify.cooked.mismatch", inspection.SourcePath ?? target, "Deterministic cook output does not match the installed cooked artifact."));
        if (plan.Success && !StringEquals(plan.PackageHash, inspection.PackageHash))
            diagnostics.Add(Error("verify.package.mismatch", inspection.SourcePath ?? target, "Deterministic package hash does not match the installed cooked artifact."));
        bool success = diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);
        return CharacterPackageVerificationResult.Create(success, inspection.PackageId, diagnostics, inspection, plan);
    }


    public CharacterPackageAssetDiscoveryResult DiscoverAssets(string target, string semanticId)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageAssetDiscoveryResult.Failure(semanticId, diagnostics);
        if (package.Catalog == null || !(package.Catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Any(x => x != null && StringEquals(x.SemanticId, semanticId)))
        {
            diagnostics.Add(Error("binding.semantic-id.missing", semanticId ?? "semanticId", "Catalog binding does not exist; discovery is scoped to declared semantic IDs."));
            return CharacterPackageAssetDiscoveryResult.Failure(semanticId, diagnostics);
        }
        var candidates = new List<CharacterPackageAssetCandidate>();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            string path = NormalizeProjectAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(path)) continue;
            CharacterPackageDependencyInfo dependency = ClassifyDependency(package.ProjectRelativeRoot, path);
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
            if (clips.Length == 0)
            {
                candidates.Add(new CharacterPackageAssetCandidate(path, "", "missing", dependency.SourcePackageId, "No AnimationClip subasset found."));
                continue;
            }
            foreach (AnimationClip clip in clips)
            {
                bool accepted = dependency.Classification == "package" || dependency.Classification == "shared-approved";
                string rejection = accepted ? "" : dependency.Classification == "foreign" ? "Asset belongs to a foreign package or unapproved project location." : "Asset is not an approved animation source.";
                candidates.Add(new CharacterPackageAssetCandidate(path, clip.name, dependency.Classification, dependency.SourcePackageId, rejection));
            }
        }
        return CharacterPackageAssetDiscoveryResult.Successful(semanticId, candidates
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal));
    }
    public CharacterRosterAdmissionResult AdmitRoster(
        string packageId,
        string selectorName,
        string version = "",
        string cookedHash = "",
        string packageHash = "")
    {
        CharacterPackageVerificationResult verification = Verify(packageId);
        var diagnostics = new List<CharacterDiagnostic>();
        if (verification.Diagnostics != null)
            diagnostics.AddRange(verification.Diagnostics.Select(x => new CharacterDiagnostic(
                x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                x.Code, x.Path, x.Message)));
        if (!verification.Success)
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);
        if (!Enum.TryParse(selectorName, true, out CharacterClass selector) || selector == CharacterClass.None)
        {
            diagnostics.Add(Error("roster.selector.invalid", "selector", "Roster selector must be a supported CharacterClass value."));
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);
        }

        string verifiedVersion = verification.Inspection?.Provenance?.Version ?? "";
        string verifiedCookedHash = verification.Plan?.CookedContentHash ?? "";
        string verifiedPackageHash = verification.Plan?.PackageHash ?? "";
        version = string.IsNullOrEmpty(version) ? verifiedVersion : version;
        cookedHash = string.IsNullOrEmpty(cookedHash) ? verifiedCookedHash : cookedHash;
        packageHash = string.IsNullOrEmpty(packageHash) ? verifiedPackageHash : packageHash;
        if (!StringEquals(version, verifiedVersion) || !StringEquals(cookedHash, verifiedCookedHash) || !StringEquals(packageHash, verifiedPackageHash))
        {
            diagnostics.Add(Error("roster.requirement.mismatch", packageId, "Roster admission hashes/version do not match the verified cooked artifact."));
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);
        }

        string rosterPath = Path.Combine(RepositoryRoot(), "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        try
        {
            BuiltInRosterManifest manifest = BuiltInRosterManifestCodec.ParseCooked(File.ReadAllText(rosterPath));
            if (manifest.TryGetByPackageId(packageId, out _))
                diagnostics.Add(Error("roster.package.exists", packageId, "Package is already admitted to the roster."));
            if (manifest.TryGetBySelector(selector, out _))
                diagnostics.Add(Error("roster.selector.exists", selector.ToString(), "Roster selector is already assigned."));
            if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
                return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);
            var entries = manifest.Entries.Concat(new[]
            {
                new BuiltInRosterEntry(selector, packageId, new MatchContentPackageRequirement(packageId, version, cookedHash, packageHash))
            }).ToArray();
            WriteDurably(rosterPath, Encoding.UTF8.GetBytes(BuiltInRosterManifestCodec.Serialize(
                new BuiltInRosterManifest(manifest.SchemaVersion, entries))));
            return CharacterRosterAdmissionResult.Successful(packageId, selector.ToString());
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("roster.admit.failed", rosterPath, ex.Message));
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);
        }
    }
    public CharacterRosterAdmissionResult RefreshRoster(string packageId)
    {
        CharacterPackageVerificationResult verification = Verify(packageId);
        var diagnostics = new List<CharacterDiagnostic>();
        if (verification.Diagnostics != null)
            diagnostics.AddRange(verification.Diagnostics.Select(x => new CharacterDiagnostic(
                x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                x.Code, x.Path, x.Message)));
        if (!verification.Success)
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics);

        var requirement = new MatchContentPackageRequirement(
            packageId,
            verification.Inspection?.Provenance?.Version ?? "",
            verification.Plan?.CookedContentHash ?? "",
            verification.Plan?.PackageHash ?? "");
        if (!WriteRosterRequirement(packageId, requirement, out var selector, out var refreshDiagnostics))
            return CharacterRosterAdmissionResult.Failure(packageId, diagnostics.Concat(refreshDiagnostics));
        return CharacterRosterAdmissionResult.Successful(packageId, selector);
    }


    public CharacterPackageCookResult Cook(string target) => Cook(target, false);

    public CharacterPackageCookResult Cook(string target, bool dryRun)
    {
        if (!TryPrepareCook(target, out var package, out var cooked, out var assembly, out var diagnostics))
        {
            return CharacterPackageCookResult.CreateFailure(
                package?.PackageId,
                package?.ProjectRelativeRoot,
                package == null ? null : CookedPath(package.PackageId),
                cooked?.SourceHash,
                assembly?.PackageHash,
                diagnostics,
                assembly,
                dryRun,
                package == null ? Array.Empty<string>() : ExpectedOutputPaths(package.PackageId));
        }

        string cookedPath = CookedPath(package.PackageId);
        string[] expectedOutputs = ExpectedOutputPaths(package.PackageId);
        if (dryRun)
            return CharacterPackageCookResult.CreateSuccess(package.PackageId, package.ProjectRelativeRoot, cookedPath,
                assembly.SourceHash, assembly.CookedContentHash, assembly.PackageHash, diagnostics, assembly, true, expectedOutputs);

        if (!Publish(package, cooked, assembly, out var publishDiagnostics))
        {
            AddUnique(diagnostics, publishDiagnostics);
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, cookedPath,
                cooked.SourceHash, assembly.PackageHash, diagnostics, assembly, false, expectedOutputs);
        }

        AddUnique(diagnostics, publishDiagnostics);

        return CharacterPackageCookResult.CreateSuccess(
            package.PackageId,
            package.ProjectRelativeRoot,
            cookedPath,
            assembly.SourceHash,
            assembly.CookedContentHash,
            assembly.PackageHash,
            diagnostics,
            assembly,
            false,
            expectedOutputs);
    }



    public bool TryCompileForEditorPlay(
        string target,
        out CookedCharacterPackageLoadResult package,
        out CharacterAnimationCatalog animationCatalog,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        package = null;
        animationCatalog = null;
        bool usesCommittedPoses = false;
        if (!TryPrepareCook(target, out var context, out _, out var assembly, out var preparationDiagnostics))
        {
            // Clips or presentation prefabs that are not installed (a fresh clone, or
            // unshared licensed packs) degrade to the committed pose data instead of
            // blocking play (ADR-0038). Any other error still fails closed.
            var errors = preparationDiagnostics.Where(x => x.Severity == CharacterDiagnosticSeverity.Error).ToList();
            if (context == null || errors.Count == 0 || !errors.All(UnityCharacterAssetCooker.IsMissingPresentationAsset))
            {
                diagnostics = preparationDiagnostics;
                return false;
            }
            if (!TryAssembleWithCommittedPoses(context, out assembly, out var fallbackDiagnostics))
            {
                diagnostics = preparationDiagnostics.Concat(fallbackDiagnostics).ToList();
                return false;
            }
            preparationDiagnostics = fallbackDiagnostics;
            usesCommittedPoses = true;
        }

        var allDiagnostics = new List<CharacterDiagnostic>(preparationDiagnostics);
        CookedCharacterPackageLoadResult loaded = CookedCharacterPackageLoader.LoadAssembly(assembly);
        AddUnique(allDiagnostics, loaded.Diagnostics);
        if (!loaded.IsValid || loaded.Package == null)
        {
            diagnostics = allDiagnostics;
            return false;
        }

        if (usesCommittedPoses)
        {
            if (!CharacterAnimationCatalogGenerator.TryCreateAllowingMissingPresentation(
                    assembly.BindingBytes, out animationCatalog, out string catalogError))
            {
                allDiagnostics.Add(Error("asset-catalog.binding.invalid", "bindings.json", catalogError));
                diagnostics = allDiagnostics;
                return false;
            }
            allDiagnostics.Add(new CharacterDiagnostic(
                CharacterDiagnosticSeverity.Warning,
                CommittedPosesDiagnosticCode,
                CookedPath(context.PackageId),
                $"{CharacterAnimationCatalogGenerator.CountMissingClips(animationCatalog)} animation clip(s) and " +
                $"{CharacterAnimationCatalogGenerator.CountMissingPresentationPrefabs(animationCatalog)} presentation prefab(s) are not installed. " +
                "Playing with the committed pose data: hitboxes work, but those moves won't animate. Cooking still needs the clips."));
        }
        else
        {
            try
            {
                animationCatalog = CharacterAnimationCatalogGenerator.Create(assembly.BindingBytes);
            }
            catch (Exception ex)
            {
                allDiagnostics.Add(Error("asset-catalog.binding.invalid", "bindings.json", ex.Message));
                diagnostics = allDiagnostics;
                return false;
            }
        }

        package = loaded;
        diagnostics = allDiagnostics;
        return true;
    }

    /// <summary>Warning code reported when Editor play reuses committed pose data (ADR-0038).</summary>
    public const string CommittedPosesDiagnosticCode = "presentation.committed-poses";

    private bool TryAssembleWithCommittedPoses(
        PackageContext package,
        out CharacterPackageAssemblyResult assembly,
        out List<CharacterDiagnostic> diagnostics)
    {
        assembly = null;
        diagnostics = new List<CharacterDiagnostic>();
        string directory = Path.Combine(RepositoryRoot(), "content-cooked", package.PackageId);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (string name in new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath })
                files[name] = File.ReadAllBytes(Path.Combine(directory, name));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.files.missing", CookedPath(package.PackageId),
                "No committed cooked package to play without the animation clips: " + ex.Message));
            return false;
        }

        string sourceHash, version, cookedContentHash, packageHash;
        try
        {
            using var manifest = JsonDocument.Parse(files[CharacterPackageAssembler.ManifestPath]);
            var root = manifest.RootElement;
            sourceHash = root.GetProperty("sourceHash").GetString() ?? "";
            version = root.GetProperty("version").GetString() ?? "";
            cookedContentHash = root.GetProperty("cookedContentHash").GetString() ?? "";
            packageHash = root.GetProperty("packageHash").GetString() ?? "";
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.manifest.invalid", CookedPath(package.PackageId), ex.Message));
            return false;
        }

        // Loading verifies every payload hash, so the poses are exactly what was cooked.
        var committed = CookedCharacterPackageLoader.LoadFiles(
            files, new MatchContentPackageRequirement(package.PackageId, version, cookedContentHash, packageHash));
        AddUnique(diagnostics, committed.Diagnostics);
        if (!committed.IsValid || committed.Package == null)
            return false;

        CharacterAssetCookResult cooked;
        try
        {
            cooked = UnityCharacterAssetCooker.CookWithCommittedPoses(
                package.ProjectRelativeRoot,
                ProfileFor(package.PackageId),
                committed.Package,
                files[CharacterPackageAssembler.PosePath],
                files[CharacterPackageAssembler.BindingPath],
                sourceHash);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.schema", "cook", ex.Message));
            return false;
        }
        AddUnique(diagnostics, cooked.Diagnostics);
        if (cooked.CookedPackage == null || cooked.HasErrors)
            return false;

        try
        {
            // The assembler checks that the committed bindings and poses still cover
            // every animation and presentation the current source requires.
            assembly = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(cooked));
            AddUnique(diagnostics, assembly.Diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.assembly.failed", "assembly", ex.Message));
            return false;
        }
        return assembly.IsValid;
    }

    private bool TryPrepareCook(
        string target,
        out PackageContext package,
        out CharacterAssetCookResult cooked,
        out CharacterPackageAssemblyResult assembly,
        out List<CharacterDiagnostic> diagnostics)
    {
        package = null;
        cooked = null;
        assembly = null;
        diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out package))
            return false;

        AddUnique(diagnostics, package.Diagnostics);
        if (package.Source == null || package.Catalog == null ||
            diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return false;

        byte[] packageBytesBefore;
        byte[] characterBytesBefore;
        try
        {
            packageBytesBefore = File.ReadAllBytes(Path.Combine(package.FullRoot, "package.json"));
            characterBytesBefore = File.ReadAllBytes(Path.Combine(package.FullRoot, "character.json"));
            package.PackageBytes = packageBytesBefore;
            package.CharacterBytes = characterBytesBefore;
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("source.read.failed", package.ProjectRelativeRoot, ex.Message));
            return false;
        }

        try
        {
            cooked = UnityCharacterAssetCooker.Cook(
                package.ProjectRelativeRoot,
                package.Catalog,
                CharacterCookOutput.For(package.PackageId),
                ProfileFor(package.PackageId));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.schema", "cook", ex.Message));
            return false;
        }

        AddUnique(diagnostics, cooked.Diagnostics);
        if (cooked.CookedPackage == null || cooked.HasErrors)
            return false;

        try
        {
            assembly = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(cooked));
            AddUnique(diagnostics, assembly.Diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.assembly.failed", "assembly", ex.Message));
            return false;
        }
        if (!assembly.IsValid)
            return false;

        if (!InputsUnchanged(package, packageBytesBefore, characterBytesBefore, cooked.SourceHash, out var conflictDiagnostics))
        {
            AddUnique(diagnostics, conflictDiagnostics);
            return false;
        }

        return true;
    }

    private string[] ExpectedOutputPaths(string packageId)
    {
        CharacterCookOutput output = CharacterCookOutput.For(packageId);
        return new[]
        {
            CookedPath(packageId) + "/" + CharacterPackageAssembler.ManifestPath,
            CookedPath(packageId) + "/" + CharacterPackageAssembler.RuntimePath,
            CookedPath(packageId) + "/" + CharacterPackageAssembler.PosePath,
            CookedPath(packageId) + "/" + CharacterPackageAssembler.BindingPath,
            output.IntermediateDirectory + "/" + output.PoseFileName,
            output.IntermediateDirectory + "/" + output.BindingFileName,
            output.IntermediateDirectory + "/" + output.StatusFileName,
            output.GeneratedAssetPath,
        };
    }

    public CharacterCookStatus ReadStatus(string packageId)
    {
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
            return new CharacterCookStatus { State = "Unknown", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        string path = Path.Combine(_projectRoot, CharacterCookOutput.For(packageId).IntermediateDirectory, CharacterCookOutput.For(packageId).StatusFileName);
        if (!File.Exists(path)) return new CharacterCookStatus { State = "Unknown", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        try
        {
            return JsonUtility.FromJson<CharacterCookStatus>(File.ReadAllText(path)) ?? new CharacterCookStatus();
        }
        catch
        {
            return new CharacterCookStatus { State = "Failed", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        }
    }

    internal void MarkStale(string packageId)
    {
        CharacterCookStatus status = ReadStatus(packageId);
        if (!StringEquals(status.State, "Valid")) return;
        string packageRoot = PackageRootFor(packageId);
        string catalogPath = packageRoot + "/CharacterAssetCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
        if (catalog != null && UnityCharacterAssetCooker.TryComputeSourceHash(packageRoot, catalog, ProfileFor(packageId), out string currentHash, out _))
        {
            status.SourceHash = currentHash;
            status.CurrentSourceHash = currentHash;
        }
        else
        {
            status.CurrentSourceHash = status.SourceHash;
        }
        if (string.IsNullOrEmpty(status.CookedSourceHash)) status.CookedSourceHash = status.LastCookedSourceHash;
        if (string.IsNullOrEmpty(status.LastCookedSourceHash)) status.LastCookedSourceHash = status.CookedSourceHash;
        status.State = "Stale";
        status.Diagnostics = new List<CharacterCookStatusDiagnostic>
        {
            new CharacterCookStatusDiagnostic { Severity = "warning", Code = "asset-catalog.stale", Path = "catalog", Message = "A package dependency changed; recook is queued." }
        };
        WriteStatus(status, packageId);
    }

    private (bool rostered, string selector) ReadRosterState(string packageId)
    {
        string path = Path.Combine(RepositoryRoot(), "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        try
        {
            if (!File.Exists(path)) return (false, "");
            BuiltInRosterManifest manifest = BuiltInRosterManifestCodec.ParseCooked(File.ReadAllText(path));
            return manifest.TryGetByPackageId(packageId, out var entry)
                ? (true, entry.Selector.ToString())
                : (false, "");
        }
        catch
        {
            return (false, "");
        }
    }
    private bool WriteRosterRequirement(
        string packageId,
        MatchContentPackageRequirement requirement,
        out string selector,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        selector = "";
        PublicationFile roster = null;
        try
        {
            roster = PrepareRosterWrite(packageId, requirement, out selector);
            if (string.IsNullOrEmpty(selector)) throw new InvalidDataException("Package is not admitted to the roster.");
            roster.Stage(null);
            roster.Install();
            diagnostics = Array.Empty<CharacterDiagnostic>();
            return true;
        }
        catch (Exception ex)
        {
            diagnostics = new[] { Error("roster.refresh.failed", packageId, ex.Message) };
            return false;
        }
        finally
        {
            if (roster != null && File.Exists(roster.TemporaryPath))
            {
                try { File.Delete(roster.TemporaryPath); }
                catch (Exception ex) { Debug.LogWarning("[SLOPArena] Roster temporary cleanup failed: " + ex.Message); }
            }
        }
    }

    private PublicationFile PrepareRosterWrite(string packageId, MatchContentPackageRequirement requirement, out string selector)
    {
        string path = Path.Combine(RepositoryRoot(), "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        byte[] before = File.ReadAllBytes(path);
        int prefix = before.Length >= 3 && before[0] == 0xef && before[1] == 0xbb && before[2] == 0xbf ? 3 : 0;
        string json = Encoding.UTF8.GetString(before, prefix, before.Length - prefix);
        BuiltInRosterManifest manifest = BuiltInRosterManifestCodec.ParseCooked(json);
        var matches = manifest.Entries.Where(entry => entry.PackageId == packageId).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Roster contains duplicate entries for " + packageId + ".");
        selector = matches.Length == 0 ? "" : matches[0].Selector.ToString();
        if (matches.Length == 0) return new PublicationFile(path, before, before);

        int targetIndex = manifest.Entries.ToList().FindIndex(entry => entry.PackageId == packageId);
        var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(before, prefix, before.Length - prefix));
        int entryIndex = -1;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 2) entryIndex++;
            if (entryIndex != targetIndex || reader.TokenType != JsonTokenType.PropertyName ||
                reader.CurrentDepth != 3 || !reader.ValueTextEquals("requirement")) continue;
            reader.Read();
            int start = prefix + checked((int)reader.TokenStartIndex);
            using (JsonDocument.ParseValue(ref reader)) { }
            int end = prefix + checked((int)reader.BytesConsumed);
            using var replacement = new MemoryStream();
            using (var writer = new Utf8JsonWriter(replacement))
            {
                writer.WriteStartObject();
                writer.WriteString("packageId", requirement.PackageId);
                writer.WriteString("version", requirement.Version);
                writer.WriteString("cookedContentHash", requirement.CookedContentHash);
                writer.WriteString("packageHash", requirement.PackageHash);
                writer.WriteEndObject();
            }
            byte[] value = replacement.ToArray();
            byte[] after = new byte[before.Length - (end - start) + value.Length];
            Buffer.BlockCopy(before, 0, after, 0, start);
            Buffer.BlockCopy(value, 0, after, start, value.Length);
            Buffer.BlockCopy(before, end, after, start + value.Length, before.Length - end);
            BuiltInRosterManifestCodec.ParseCooked(Encoding.UTF8.GetString(after, prefix, after.Length - prefix));
            return new PublicationFile(path, before, after);
        }
        throw new InvalidDataException("Roster target requirement was not found.");
    }


    private bool TryResolve(string target, List<CharacterDiagnostic> diagnostics, out PackageContext package)
    {
        package = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            diagnostics.Add(Error("package.target.missing", "target", "Package ID or package root is required."));
            return false;
        }

        string normalizedTarget = target.Replace('\\', '/').TrimEnd('/');
        bool pathLike = Path.IsPathRooted(target) || normalizedTarget.Contains("/") || normalizedTarget.StartsWith(".", StringComparison.Ordinal);
        string fullRoot = null;
        if (pathLike)
        {
            fullRoot = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(_projectRoot, normalizedTarget));
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(Error("package.path.missing", normalizedTarget, "Package root directory does not exist."));
                return false;
            }
            if (!IsInsideCharacterPackages(fullRoot))
            {
                diagnostics.Add(Error("package.path.invalid", normalizedTarget, "Package must be inside Assets/CharacterPackages."));
                return false;
            }
        }
        else
        {
            if (!MatchContentCatalogBuilder.IsStablePackageId(normalizedTarget))
            {
                diagnostics.Add(Error("id.invalid", "target", "Package ID must be a stable lowercase identifier."));
                return false;
            }
            fullRoot = Path.Combine(CharacterPackagesFullRoot(), normalizedTarget);
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(Error("package.missing", normalizedTarget, "Character package was not found."));
                return false;
            }
        }

        package = LoadContext(fullRoot, diagnostics);
        return true;
    }

    internal static string NormalizeProjectAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            string root = UnityCharacterAssetCooker.ProjectRoot().Replace('\\', '/').TrimEnd('/') + "/";
            normalized = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : "";
        }
        return normalized.StartsWith("Assets/", StringComparison.Ordinal) ? normalized : "";
    }

    internal static CharacterPackageDependencyInfo ClassifyDependency(string packageRoot, string assetPath)
    {
        string normalized = NormalizeProjectAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalized))
            return CharacterPackageDependencyInfo.Missing(normalized);
        string packageRootNormalized = packageRoot.Replace('\\', '/').TrimEnd('/');
        string packageId = packageRootNormalized.Substring("Assets/CharacterPackages/".Length);
        if (normalized.StartsWith(packageRootNormalized + "/", StringComparison.Ordinal)
            || CharacterPackageAssetOwnershipRegistry.IsOwnedBy(packageId, normalized))
            return CharacterPackageDependencyInfo.Package(normalized, packageId);
        if (CharacterSharedAssetRegistry.IsApproved(normalized, out string reason, out string version))
            return CharacterPackageDependencyInfo.Shared(normalized, reason, version);
        if (normalized.StartsWith("Assets/CharacterPackages/", StringComparison.Ordinal))
        {
            string remainder = normalized.Substring("Assets/CharacterPackages/".Length);
            string sourcePackage = remainder.Split('/')[0];
            return CharacterPackageDependencyInfo.Foreign(normalized, sourcePackage);
        }
        if (CharacterPackageAssetOwnershipRegistry.TryGetOwner(normalized, out string foreignPackage))
            return CharacterPackageDependencyInfo.Foreign(normalized, foreignPackage);
        return CharacterPackageDependencyInfo.Foreign(normalized, "");
    }

    internal static bool PersistCatalog(
        string packageRoot,
        CharacterAssetCatalog catalog,
        out CharacterAssetCatalog persistedCatalog,
        out string fingerprint,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var errors = new List<CharacterDiagnostic>();
        persistedCatalog = catalog;
        fingerprint = "";
        if (catalog == null)
        {
            errors.Add(Error("asset-catalog.missing", packageRoot + "/CharacterAssetCatalog.asset", "Character asset catalog is required."));
            diagnostics = errors;
            return false;
        }
        try
        {
            string catalogPath = NormalizeProjectAssetPath(packageRoot + "/CharacterAssetCatalog.asset");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(catalogPath))
                AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceSynchronousImport);
            persistedCatalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
            if (persistedCatalog == null)
            {
                errors.Add(Error("asset-catalog.persist.failed", catalogPath, "Catalog could not be reloaded after persistence."));
                diagnostics = errors;
                return false;
            }
            fingerprint = ComputeCatalogFingerprint(persistedCatalog);
            diagnostics = errors;
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(Error("asset-catalog.persist.failed", packageRoot, ex.Message));
            diagnostics = errors;
            return false;
        }
    }

    internal static string ComputeCatalogFingerprint(CharacterAssetCatalog catalog)
    {
        if (catalog == null) return "";
        var builder = new StringBuilder();
        builder.Append(catalog.PackageId).Append('\n')
            .Append(catalog.CatalogSchemaVersion).Append('\n')
            .Append(catalog.SampleRate).Append('\n')
            .Append(catalog.Rig == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(catalog.Rig).ToString()).Append('\n');
        foreach (var binding in catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
        {
            builder.Append(binding?.SemanticId ?? "").Append('|')
                .Append(binding?.PoseTrackId ?? "").Append('|')
                .Append(binding == null || binding.Clip == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(binding.Clip).ToString()).Append('|')
                .Append(binding?.Extrapolation.ToString() ?? "").Append('\n');
        }
        foreach (var presentation in catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
        {
            builder.Append(presentation?.SemanticId ?? "").Append('|')
                .Append(presentation == null || presentation.Prefab == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(presentation.Prefab).ToString()).Append('\n');
        }
        builder.Append("weapon=").Append(CharacterBindingWriter.ObjectId(catalog.WeaponConfig)).Append('\n');
        builder.Append("profile=").Append(CharacterBindingWriter.ProfileJson(catalog.PresentationProfile)).Append('\n');
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))).Replace("-", "").ToLowerInvariant();
    }

    private PackageContext LoadContext(string fullRoot, List<CharacterDiagnostic> diagnostics)
    {
        var context = new PackageContext
        {
            FullRoot = Path.GetFullPath(fullRoot),
            ProjectRelativeRoot = ProjectRelative(fullRoot),
            PackageId = new DirectoryInfo(fullRoot).Name,
        };

        if (!MatchContentCatalogBuilder.IsStablePackageId(context.PackageId))
            diagnostics.Add(Error("package.id.invalid", context.ProjectRelativeRoot, "Package folder name must be a stable lowercase identifier."));

        string packagePath = Path.Combine(fullRoot, "package.json");
        string characterPath = Path.Combine(fullRoot, "character.json");
        if (!File.Exists(packagePath)) diagnostics.Add(Error("schema.missing", context.ProjectRelativeRoot + "/package.json", "Package manifest is required."));
        if (!File.Exists(characterPath)) diagnostics.Add(Error("schema.missing", context.ProjectRelativeRoot + "/character.json", "Character document is required."));
        if (File.Exists(packagePath) && File.Exists(characterPath))
        {
            try
            {
                context.PackageJson = File.ReadAllText(packagePath);
                context.CharacterJson = File.ReadAllText(characterPath);
                var loaded = CharacterPackageSourceCodec.Load(context.PackageJson, context.CharacterJson);
                AddUnique(diagnostics, loaded.Diagnostics);
                if (loaded.Source != null)
                {
                    context.Source = loaded.Source;
                    context.PackageId = loaded.Source.Manifest.PackageId;
                    context.DisplayName = loaded.Source.Character.DisplayName;
                    if (!StringEquals(new DirectoryInfo(fullRoot).Name, context.PackageId))
                        diagnostics.Add(Error("package.identity.mismatch", context.ProjectRelativeRoot, "Package folder name does not match manifest package ID."));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(Error("source.read.failed", context.ProjectRelativeRoot, ex.Message));
            }
        }

        string catalogPath = context.ProjectRelativeRoot + "/CharacterAssetCatalog.asset";
        context.Catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
        if (context.Catalog == null)
            diagnostics.Add(Error("asset-catalog.missing", catalogPath, "CharacterAssetCatalog.asset is required."));
        else if (!string.IsNullOrEmpty(context.PackageId) && !StringEquals(context.Catalog.PackageId, context.PackageId))
            diagnostics.Add(Error("asset-catalog.schema", catalogPath + ".packageId", "Catalog package ID does not match package manifest ID."));

        context.Diagnostics = diagnostics.ToArray();
        return context;
    }

    private bool InputsUnchanged(
        PackageContext package,
        byte[] packageBytesBefore,
        byte[] characterBytesBefore,
        string cookedSourceHash,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var result = new List<CharacterDiagnostic>();
        try
        {
            byte[] packageBytesAfter = File.ReadAllBytes(Path.Combine(package.FullRoot, "package.json"));
            byte[] characterBytesAfter = File.ReadAllBytes(Path.Combine(package.FullRoot, "character.json"));
            if (!packageBytesBefore.SequenceEqual(packageBytesAfter) || !characterBytesBefore.SequenceEqual(characterBytesAfter))
                result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Package source changed while cooking."));

            if (result.Count == 0 && package.Catalog != null)
            {
                if (!UnityCharacterAssetCooker.TryComputeSourceHash(
                        package.ProjectRelativeRoot,
                        package.Catalog,
                        ProfileFor(package.PackageId),
                        out string currentHash,
                        out var hashDiagnostics))
                {
                    AddUnique(result, hashDiagnostics);
                }
                else if (!StringEquals(currentHash, cookedSourceHash))
                {
                    result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Package or Unity-owned cook dependencies changed while cooking."));
                }
            }
        }
        catch (Exception ex)
        {
            result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Could not confirm package inputs after cooking: " + ex.Message));
        }

        diagnostics = result;
        return result.Count == 0;
    }

    private bool Publish(
        PackageContext package,
        CharacterAssetCookResult result,
        CharacterPackageAssemblyResult assembled,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var messages = new List<CharacterDiagnostic>();
        var files = new List<PublicationFile>();
        CharacterCookOutput output = CharacterCookOutput.For(package.PackageId);
        string parent = Path.Combine(RepositoryRoot(), "content-cooked");
        string canonical = Path.Combine(parent, package.PackageId);
        string temporary = Path.Combine(parent, "." + package.PackageId + ".tmp-" + Guid.NewGuid().ToString("N"));
        string recovery = Path.Combine(parent, "." + package.PackageId + ".publish-backup");
        string canonicalBackup = Path.Combine(recovery, "canonical");
        string generated = Path.Combine(_projectRoot, output.GeneratedAssetPath);
        string generatedTemp = CharacterAnimationCatalogGenerator.TemporaryPath(output.GeneratedAssetPath);
        bool ownsRecovery = false;
        bool ownsGeneratedTemp = false;
        bool canonicalMoved = false;
        bool canonicalInstalled = false;
        Dictionary<string, byte[]> previousCanonical = null;
        Dictionary<string, byte[]> installedCanonical = null;
        try
        {
            if (Directory.Exists(recovery) || File.Exists(recovery) ||
                Directory.Exists(parent) && Directory.EnumerateDirectories(parent, "." + package.PackageId + ".backup-*").Any())
                throw new IOException("Unresolved publication evidence blocks retry. Inspect " + recovery + " and prior package backups.");
            if (new[] { generatedTemp, generatedTemp + ".meta", output.GeneratedAssetPath + ".previous", output.GeneratedAssetPath + ".previous.meta" }
                .Any(path => File.Exists(Path.Combine(_projectRoot, path)) || Directory.Exists(Path.Combine(_projectRoot, path))))
                throw new IOException("Unresolved generated catalog evidence blocks retry: " + generatedTemp);

            PublicationFile roster = PrepareRosterWrite(package.PackageId,
                new MatchContentPackageRequirement(package.PackageId, package.Source.Manifest.Version, assembled.CookedContentHash, assembled.PackageHash), out _);
            previousCanonical = SnapshotDirectory(canonical);
            Directory.CreateDirectory(recovery);
            ownsRecovery = true;
            Directory.CreateDirectory(temporary);
            WriteDurably(Path.Combine(temporary, CharacterPackageAssembler.ManifestPath), assembled.ManifestBytes);
            WriteDurably(Path.Combine(temporary, CharacterPackageAssembler.RuntimePath), assembled.RuntimeBytes);
            WriteDurably(Path.Combine(temporary, CharacterPackageAssembler.PosePath), assembled.PoseBytes);
            WriteDurably(Path.Combine(temporary, CharacterPackageAssembler.BindingPath), assembled.BindingBytes);
            VerifyInstalledPackage(temporary, assembled);
            installedCanonical = SnapshotDirectory(temporary);

            files.Add(new PublicationFile(Path.Combine(_projectRoot, output.IntermediateDirectory, output.PoseFileName), assembled.PoseBytes));
            files.Add(new PublicationFile(Path.Combine(_projectRoot, output.IntermediateDirectory, output.BindingFileName), assembled.BindingBytes));
            var generatedFile = new PublicationFile(generated, Snapshot(generated), null);
            var generatedMeta = new PublicationFile(generated + ".meta", Snapshot(generated + ".meta"), null);
            ownsGeneratedTemp = true;
            string generatedPath = _generateCatalog(assembled.BindingBytes, output.GeneratedAssetPath);
            generatedFile.After = File.ReadAllBytes(Path.Combine(_projectRoot, generatedPath));
            generatedMeta.After = generatedMeta.Before ?? File.ReadAllBytes(Path.Combine(_projectRoot, generatedPath) + ".meta");
            files.Add(generatedFile);
            files.Add(generatedMeta);
            files.Add(new PublicationFile(Path.Combine(_projectRoot, output.IntermediateDirectory, output.StatusFileName), CreateSuccessStatus(result, assembled)));
            files.Add(roster);
            for (int i = 0; i < files.Count; i++) files[i].Stage(Path.Combine(recovery, i + "-" + Path.GetFileName(files[i].Path)));

            // Unity must release the staged GUID before the final path can be imported.
            DeleteGeneratedTemporaryCatalog(generatedTemp);
            _publicationCheckpoint?.Invoke("before-install");
            if (!InputsUnchanged(package, package.PackageBytes, package.CharacterBytes, result.SourceHash, out var conflicts))
                throw new IOException(string.Join("\n", conflicts.Select(diagnostic => diagnostic.Message)));
            foreach (var file in files) file.RequirePreimage();
            if (!DirectoryMatches(canonical, previousCanonical)) throw new IOException("Cooked package changed before publication: " + canonical);
            if (previousCanonical != null)
            {
                Directory.Move(canonical, canonicalBackup);
                canonicalMoved = true;
            }
            Directory.Move(temporary, canonical);
            canonicalInstalled = true;
            foreach (var file in files.Where(file => file != roster)) file.Install();
            generatedFile.MutationAttempted = true;
            generatedMeta.MutationAttempted = true;
            ImportGeneratedCatalog(output.GeneratedAssetPath);
            _publicationCheckpoint?.Invoke("before-roster-install");
            roster.Install();
            _publicationCheckpoint?.Invoke("after-roster-install");
            VerifyInstalledPackage(canonical, assembled);
            if (!DirectoryMatches(canonical, installedCanonical)) throw new IOException("Installed package file set changed: " + canonical);
            foreach (var file in files) file.RequireInstalled();
        }
        catch (Exception ex)
        {
            messages.Add(Error("cook.failed", package.ProjectRelativeRoot, ex.Message));
            bool restored = true;
            foreach (var file in files.AsEnumerable().Reverse())
            {
                try { file.Restore(); }
                catch (Exception rollback)
                {
                    restored = false;
                    messages.Add(Error("cook.rollback.failed", file.Path, rollback.Message + " Recovery evidence: " + recovery));
                }
            }
            try
            {
                if (canonicalMoved && !DirectoryMatches(canonicalBackup, previousCanonical))
                    throw new IOException("Package backup changed; retaining recovery evidence.");
                if (canonicalInstalled)
                {
                    if (!DirectoryMatches(canonical, installedCanonical)) throw new IOException("Installed package changed; refusing to overwrite it.");
                    Directory.Delete(canonical, true);
                }
                if (canonicalMoved)
                {
                    if (Directory.Exists(canonical)) throw new IOException("Package path was claimed by another writer.");
                    Directory.Move(canonicalBackup, canonical);
                }
                if ((canonicalInstalled || canonicalMoved) && !DirectoryMatches(canonical, previousCanonical))
                    throw new IOException("Package restoration did not match its preimage.");
            }
            catch (Exception rollback)
            {
                restored = false;
                messages.Add(Error("cook.rollback.failed", canonical, rollback.Message + " Recovery evidence: " + recovery));
            }
            try { if (files.Any(file => file.MutationAttempted)) ImportGeneratedCatalog(output.GeneratedAssetPath); }
            catch (Exception rollback)
            {
                restored = false;
                messages.Add(Error("cook.rollback.failed", output.GeneratedAssetPath, rollback.Message + " Recovery evidence: " + recovery));
            }
            if (restored) CleanupPublication(temporary, ownsRecovery ? recovery : null, ownsGeneratedTemp ? generatedTemp : null, files, messages);
            diagnostics = messages;
            return false;
        }
        CleanupPublication(temporary, recovery, generatedTemp, files, messages);
        diagnostics = messages;
        return true;
    }

    private static void VerifyInstalledPackage(string directory, CharacterPackageAssemblyResult assembled)
    {
        var files = ReadPackageFiles(directory);
        var verification = CharacterPackageAssembler.Verify(files);
        if (!verification.IsValid || !files[CharacterPackageAssembler.ManifestPath].SequenceEqual(assembled.ManifestBytes) ||
            !files[CharacterPackageAssembler.RuntimePath].SequenceEqual(assembled.RuntimeBytes) ||
            !files[CharacterPackageAssembler.PosePath].SequenceEqual(assembled.PoseBytes) ||
            !files[CharacterPackageAssembler.BindingPath].SequenceEqual(assembled.BindingBytes))
            throw new InvalidDataException("Installed package verification failed: " + directory);
    }

    private void ImportGeneratedCatalog(string path)
    {
        if (!StringEquals(_projectRoot, UnityCharacterAssetCooker.ProjectRoot())) return;
        if (!File.Exists(Path.Combine(_projectRoot, path)))
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return;
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(path) == null)
            throw new IOException("Generated catalog did not import: " + path);
    }

    private void DeleteGeneratedTemporaryCatalog(string path)
    {
        string full = Path.Combine(_projectRoot, path);
        if (StringEquals(_projectRoot, UnityCharacterAssetCooker.ProjectRoot()))
        {
            if (File.Exists(full) && !AssetDatabase.DeleteAsset(path))
                throw new IOException("Could not delete temporary catalog " + path);
            if (!File.Exists(full)) File.Delete(full + ".meta");
        }
        else
        {
            File.Delete(full);
            File.Delete(full + ".meta");
        }
    }

    private void CleanupPublication(string temporary, string recovery, string generatedTemp, IEnumerable<PublicationFile> files, List<CharacterDiagnostic> diagnostics)
    {
        var cleanup = files.Select<PublicationFile, Action>(file => () => { if (File.Exists(file.TemporaryPath)) File.Delete(file.TemporaryPath); }).ToList();
        if (generatedTemp != null) cleanup.Add(() => DeleteGeneratedTemporaryCatalog(generatedTemp));
        cleanup.Add(() => { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); });
        if (recovery != null) cleanup.Add(() => { if (Directory.Exists(recovery)) Directory.Delete(recovery, true); });
        foreach (var action in cleanup)
        {
            try { action(); }
            catch (Exception ex) { diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "cook.cleanup.failed", recovery ?? temporary, ex.Message)); }
        }
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string path)
        => !Directory.Exists(path) ? null : Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file.Substring(path.Length + 1), File.ReadAllBytes, StringComparer.Ordinal);

    private static bool DirectoryMatches(string path, Dictionary<string, byte[]> expected)
    {
        var actual = SnapshotDirectory(path);
        return expected == null ? actual == null : actual != null && expected.Count == actual.Count &&
            expected.All(file => actual.TryGetValue(file.Key, out var bytes) && file.Value.SequenceEqual(bytes));
    }

    private sealed class PublicationFile
    {
        internal readonly string Path;
        internal readonly byte[] Before;
        internal byte[] After;
        internal readonly string TemporaryPath;
        internal bool MutationAttempted;

        internal PublicationFile(string path, byte[] after) : this(path, Snapshot(path), after) { }
        internal PublicationFile(string path, byte[] before, byte[] after)
        {
            Path = path;
            Before = before;
            After = after;
            TemporaryPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,
                "." + System.IO.Path.GetFileName(path) + ".publish-" + Guid.NewGuid().ToString("N") + ".tmp");
        }

        internal void Stage(string backupPath)
        {
            if (backupPath != null && Before != null) WriteDurably(backupPath, Before);
            WriteDurably(TemporaryPath, After);
        }

        internal void RequirePreimage()
        {
            if (!BytesMatch(Path, Before)) throw new IOException("Output changed before replacement: " + Path);
        }

        internal void Install()
        {
            RequirePreimage();
            if (Before != null && Before.SequenceEqual(After)) return;
            MutationAttempted = true;
            if (Before == null) File.Move(TemporaryPath, Path);
            else File.Replace(TemporaryPath, Path, null);
        }

        internal void RequireInstalled()
        {
            if (!BytesMatch(Path, After)) throw new IOException("Output changed after replacement: " + Path);
        }

        internal void Restore()
        {
            if (!MutationAttempted || BytesMatch(Path, Before)) return;
            RequireInstalled();
            if (Before == null) File.Delete(Path);
            else
            {
                WriteDurably(TemporaryPath, Before);
                RequireInstalled();
                File.Replace(TemporaryPath, Path, null);
            }
            if (!BytesMatch(Path, Before)) throw new IOException("Output restoration failed: " + Path);
        }
    }

    private static bool BytesMatch(string path, byte[] bytes)
        => bytes == null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes);

    private static CharacterPackageSlotSummary[] BuildSlotSummaries(
        CharacterPackageSource source,
        CookedCharacterPackage cooked)
    {
        var result = new List<CharacterPackageSlotSummary>(CharacterPackageCompiler.CanonicalSlotIds.Count);
        var cookedById = cooked?.Definition.Slots.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var explicitSlots = source?.Character.Slots?.Where(x => x != null).GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        var aliases = source?.Character.Aliases?.Where(x => x != null).GroupBy(x => x.From, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().To, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string id in CharacterPackageCompiler.CanonicalSlotIds)
        {
            if (cookedById != null && cookedById.TryGetValue(id, out var cookedSlot))
            {
                result.Add(new CharacterPackageSlotSummary(id, cookedSlot.Name, cookedSlot.Timeline.Stages.Count, true));
                continue;
            }
            CharacterSlotSource slot = ResolveSourceSlot(id, explicitSlots, aliases, new HashSet<string>(StringComparer.Ordinal));
            result.Add(new CharacterPackageSlotSummary(id, slot?.Name, slot?.Timeline?.Stages?.Count ?? 0, slot != null));
        }
        return result.ToArray();
    }

    private static CharacterSlotSource ResolveSourceSlot(
        string id,
        IReadOnlyDictionary<string, CharacterSlotSource> explicitSlots,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<string> visiting)
    {
        if (!visiting.Add(id)) return null;
        if (explicitSlots.TryGetValue(id, out var slot)) return slot;
        if (aliases.TryGetValue(id, out var target)) return ResolveSourceSlot(target, explicitSlots, aliases, visiting);
        return null;
    }

    private ArtifactSnapshot ReadArtifact(string packageId)
    {
        string directory = Path.Combine(RepositoryRoot(), "content-cooked", packageId);
        if (!Directory.Exists(directory)) return ArtifactSnapshot.Missing();
        var diagnostics = new List<CharacterDiagnostic>();
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(directory.Length + 1).Replace('\\', '/');
                files[relative] = File.ReadAllBytes(path);
            }
            var verification = CharacterPackageAssembler.Verify(files);
            AddUnique(diagnostics, verification.Diagnostics);
            if (!verification.IsValid) return ArtifactSnapshot.Invalid(diagnostics);
            using var document = JsonDocument.Parse(files[CharacterPackageAssembler.ManifestPath]);
            var root = document.RootElement;
            string manifestPackageId = root.GetProperty("packageId").GetString() ?? "";
            if (!StringEquals(manifestPackageId, packageId))
                diagnostics.Add(Error("package.identity.mismatch", CharacterPackageAssembler.ManifestPath, "Cooked manifest package ID does not match the package target."));
            if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return ArtifactSnapshot.Invalid(diagnostics);
            return ArtifactSnapshot.Valid(
                root.GetProperty("sourceHash").GetString(),
                root.GetProperty("cookedContentHash").GetString(),
                root.GetProperty("packageHash").GetString(),
                diagnostics,
                ParseManifestProjection(root));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.files.invalid", "content-cooked/" + packageId, ex.Message));
            return ArtifactSnapshot.Invalid(diagnostics);
        }
    }
    private static CharacterPackageManifestProjection ParseManifestProjection(JsonElement root)
    {
        var result = new CharacterPackageManifestProjection
        {
            PackageId = ReadString(root, "packageId"),
            Version = ReadString(root, "version"),
            Creator = ReadString(root, "creator"),
            License = ReadString(root, "license"),
            Attribution = ReadString(root, "attribution"),
            AuthoringSchemaVersion = ReadUShort(root, "authoringSchemaVersion"),
            CookedSchemaVersion = ReadUShort(root, "cookedSchemaVersion"),
            RuntimeApiMin = ReadString(root, "runtimeApiMin"),
            RuntimeApiMax = ReadString(root, "runtimeApiMax"),
        };
        if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array)
            foreach (var dependency in dependencies.EnumerateArray())
                result.Dependencies.Add(new PackageDependencySource(
                    ReadString(dependency, "packageId"),
                    ReadString(dependency, "version"),
                    ReadString(dependency, "cookedHash")));
        if (root.TryGetProperty("capabilityRequirements", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
            foreach (var capability in capabilities.EnumerateArray())
                result.CapabilityRequirements.Add(new CookedCapabilityRequirement(
                    ReadString(capability, "capabilityId"),
                    ReadString(capability, "capabilityVersion")));
        if (root.TryGetProperty("payloads", out var payloads) && payloads.ValueKind == JsonValueKind.Array)
            foreach (var payload in payloads.EnumerateArray())
                result.Payloads.Add(new CharacterPackagePayloadInfo(
                    ReadString(payload, "path"),
                    ReadString(payload, "sha256"),
                    ReadLong(payload, "size")));
        if (root.TryGetProperty("toolchain", out var toolchain) && toolchain.ValueKind == JsonValueKind.Object)
        {
            result.CookerVersion = ReadString(toolchain, "cookerVersion");
            result.UnityVersion = ReadString(toolchain, "unityVersion");
            result.BindingSchemaVersion = ReadInt(toolchain, "bindingSchemaVersion");
            result.PoseFormat = ReadString(toolchain, "poseFormat");
            result.PoseVersion = ReadInt(toolchain, "poseVersion");
            result.SampleRate = ReadInt(toolchain, "sampleRate");
        }
        if (root.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            foreach (var warning in warnings.EnumerateArray())
                result.Warnings.Add(new CharacterPackageDiagnosticResult(
                    ReadString(warning, "severity"),
                    ReadString(warning, "code"),
                    ReadString(warning, "path"),
                    ReadString(warning, "message")));
        return result;
    }

    private static string ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int ReadInt(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.TryGetInt32(out int result) ? result : 0;
    private static long ReadLong(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.TryGetInt64(out long result) ? result : 0;
    private static ushort ReadUShort(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.TryGetUInt16(out ushort result) ? result : (ushort)0;


    internal static CharacterCookProfile ProfileFor(string packageId)
        => packageId == "fightguy" || packageId == "wibou" || packageId == "bonk" || packageId == "manki" || packageId == "bashie"
           || packageId == "salamander" || packageId == "nyx" ? CharacterCookProfile.TrustedBuiltIn : CharacterCookProfile.Workshop;

    private string CharacterPackagesFullRoot() => Path.Combine(_projectRoot, CharacterPackagesRoot.Replace('/', Path.DirectorySeparatorChar));
    private string PackageRootFor(string packageId) => CharacterPackagesRoot + "/" + packageId;
    private string CookedPath(string packageId) => "content-cooked/" + packageId;
    private string RepositoryRoot() => Path.GetFullPath(Path.Combine(_projectRoot, "..", ".."));

    private bool IsInsideCharacterPackages(string fullPath)
    {
        string root = Path.GetFullPath(CharacterPackagesFullRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || StringEquals(candidate, root);
    }

    private string ProjectRelative(string fullPath)
    {
        string root = _projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalized = Path.GetFullPath(fullPath).Replace('\\', '/');
        string normalizedRoot = root.Replace('\\', '/');
        return normalized.StartsWith(normalizedRoot, StringComparison.Ordinal)
            ? normalized.Substring(normalizedRoot.Length).TrimEnd('/')
            : normalized;
    }

    private byte[] CreateSuccessStatus(CharacterAssetCookResult result, CharacterPackageAssemblyResult assembled)
    {
        var status = new CharacterCookStatus
        {
            State = "Valid",
            CookedAtUtc = DateTime.UtcNow.ToString("O"),
            CookTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            CookUnityVersion = Application.unityVersion,
            CookPackagesManifestHash = FileHash(Path.Combine(_projectRoot, "Packages/manifest.json")),
            CookPackageLockHash = FileHash(Path.Combine(_projectRoot, "Packages/packages-lock.json")),
            SourceHash = assembled.SourceHash,
            CurrentSourceHash = assembled.SourceHash,
            CookedSourceHash = assembled.SourceHash,
            LastCookedSourceHash = assembled.SourceHash,
            CookedContentHash = assembled.CookedContentHash,
            PackageHash = assembled.PackageHash,
            GeneratedSourceHash = assembled.SourceHash,
            Dependencies = result.Dependencies.Select(ToStatusDependency).ToList(),
            Payloads = assembled.Payloads.Select(ToStatusPayload).ToList(),
            Diagnostics = assembled.Diagnostics.Select(ToStatusDiagnostic).ToList(),
        };
        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(status, true));
    }

    private void WriteStatus(CharacterCookStatus status, string packageId)
    {
        string path = Path.Combine(_projectRoot, CharacterCookOutput.For(packageId).IntermediateDirectory, CharacterCookOutput.For(packageId).StatusFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonUtility.ToJson(status, true));
    }

    private static string FileHash(string path)
    {
        if (!File.Exists(path)) return "";
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }


    private static Dictionary<string, byte[]> ReadPackageFiles(string directory)
        => new[]
        {
            CharacterPackageAssembler.ManifestPath,
            CharacterPackageAssembler.RuntimePath,
            CharacterPackageAssembler.PosePath,
            CharacterPackageAssembler.BindingPath,
        }.ToDictionary(x => x, x => File.ReadAllBytes(Path.Combine(directory, x)), StringComparer.Ordinal);

    private static void WriteDurably(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static byte[] Snapshot(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static CharacterCookStatusDependency ToStatusDependency(CharacterCookDependencyRecord record)
        => new CharacterCookStatusDependency
        {
            Kind = record.Kind,
            Identity = record.Identity,
            Guid = record.Guid,
            DependencyHash = record.DependencyHash,
            MetaHash = record.MetaHash,
            ImporterSettings = record.ImporterSettings,
            Classification = record.Classification,
            SourcePackageId = record.SourcePackageId,
            SourcePath = record.SourcePath,
            ApprovalReason = record.ApprovalReason,
            ApprovalVersion = record.ApprovalVersion,
        };

    private static CharacterCookStatusPayload ToStatusPayload(CharacterPackagePayloadInfo payload)
        => new CharacterCookStatusPayload { Path = payload.Path, Sha256 = payload.Sha256, Size = payload.Size };

    private static CharacterCookStatusDiagnostic ToStatusDiagnostic(CharacterDiagnostic diagnostic)
        => new CharacterCookStatusDiagnostic
        {
            Severity = diagnostic.Severity == CharacterDiagnosticSeverity.Error ? "error" : "warning",
            Code = diagnostic.Code,
            Path = diagnostic.Path,
            Message = diagnostic.Message,
        };

    private static CharacterDiagnostic Error(string code, string path, string message)
        => new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message);

    private static void AddUnique(List<CharacterDiagnostic> target, IEnumerable<CharacterDiagnostic> values)
    {
        if (values == null) return;
        foreach (var value in values)
            if (!target.Any(x => x.Severity == value.Severity && x.Code == value.Code && x.Path == value.Path && x.Message == value.Message))
                target.Add(value);
    }

    private static bool StringEquals(string left, string right)
        => !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.Ordinal);

    private sealed class PackageContext
    {
        public string FullRoot = "";
        public string ProjectRelativeRoot = "";
        public string PackageId = "";
        public string DisplayName = "";
        public byte[] PackageBytes;
        public byte[] CharacterBytes;
        public string PackageJson = "";
        public string CharacterJson = "";
        public CharacterPackageSource Source;
        public CharacterAssetCatalog Catalog;
        public IReadOnlyList<CharacterDiagnostic> Diagnostics = Array.Empty<CharacterDiagnostic>();
    }

    private sealed class ArtifactSnapshot
    {
        public bool IsMissing { get; private set; }
        public bool IsInvalid { get; private set; }
        public string SourceHash { get; private set; }
        public string CookedContentHash { get; private set; }
        public string PackageHash { get; private set; }
        public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; private set; }
        public CharacterPackageManifestProjection Manifest { get; private set; }

        public static ArtifactSnapshot Missing() => new ArtifactSnapshot { IsMissing = true, Diagnostics = Array.Empty<CharacterDiagnostic>() };
        public static ArtifactSnapshot Invalid(IReadOnlyList<CharacterDiagnostic> diagnostics) => new ArtifactSnapshot { IsInvalid = true, Diagnostics = diagnostics };
        public static ArtifactSnapshot Valid(string sourceHash, string cookedContentHash, string packageHash, IReadOnlyList<CharacterDiagnostic> diagnostics, CharacterPackageManifestProjection manifest)
            => new ArtifactSnapshot { SourceHash = sourceHash, CookedContentHash = cookedContentHash, PackageHash = packageHash, Diagnostics = diagnostics, Manifest = manifest };
    }
}

internal sealed class CharacterPackageManifestProjection
{
    public string PackageId = "";
    public string Version = "";
    public string Creator = "";
    public string License = "";
    public string Attribution = "";
    public ushort AuthoringSchemaVersion;
    public ushort CookedSchemaVersion;
    public string RuntimeApiMin = "";
    public string RuntimeApiMax = "";
    public string CookerVersion = "";
    public string UnityVersion = "";
    public int BindingSchemaVersion;
    public string PoseFormat = "";
    public int PoseVersion;
    public int SampleRate;
    public readonly List<PackageDependencySource> Dependencies = new();
    public readonly List<CookedCapabilityRequirement> CapabilityRequirements = new();
    public readonly List<CharacterPackagePayloadInfo> Payloads = new();
    public readonly List<CharacterPackageDiagnosticResult> Warnings = new();
}

public sealed class CharacterPackageDependencyInfo
{
    [JsonProperty("classification")] public string Classification { get; }
    [JsonProperty("sourcePackageId")] public string SourcePackageId { get; }
    [JsonProperty("sourcePath")] public string SourcePath { get; }
    [JsonProperty("approvalReason")] public string ApprovalReason { get; }
    [JsonProperty("approvalVersion")] public string ApprovalVersion { get; }

    private CharacterPackageDependencyInfo(string classification, string sourcePackageId, string sourcePath, string approvalReason, string approvalVersion)
    {
        Classification = classification;
        SourcePackageId = sourcePackageId;
        SourcePath = sourcePath;
        ApprovalReason = approvalReason;
        ApprovalVersion = approvalVersion;
    }

    internal static CharacterPackageDependencyInfo Package(string path, string packageId)
        => new("package", packageId, path, "package-owned content including registered external media roots", CharacterPackageAssetOwnershipRegistry.RegistryVersion);
    internal static CharacterPackageDependencyInfo Shared(string path, string reason, string version)
        => new("shared-approved", "", path, reason, version);
    internal static CharacterPackageDependencyInfo Foreign(string path, string packageId)
        => new("foreign", packageId, path, "", "");
    internal static CharacterPackageDependencyInfo Missing(string path)
        => new("missing", "", path ?? "", "", "");
}

public static class CharacterSharedAssetRegistry
{
    private static readonly HashSet<string> SharedCombatAudio = new(StringComparer.Ordinal)
    {
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_01.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_02.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_03.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_04.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_05.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_06.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_07.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_08.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_09.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_10.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_11.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_12.wav",
        "Assets/Resources/Audio/SFX/Whoosh/whoosh_13.wav",
        "Assets/Resources/Audio/SFX/punch_light_01.mp3",
        "Assets/Resources/Audio/SFX/punch_light_02.mp3",
        "Assets/Resources/Audio/SFX/punch_light_03.mp3",
        "Assets/Resources/Audio/SFX/punch_medium_01.mp3",
        "Assets/Resources/Audio/SFX/punch_medium_02.mp3",
        "Assets/Resources/Audio/SFX/punch_medium_03.mp3",
        "Assets/Resources/Audio/SFX/punch_heavy_01.mp3",
        "Assets/Resources/Audio/SFX/punch_heavy_02.mp3",
    };

    public const string ApprovedRoot = "Assets/Art/Characters/shared/";
    public const string RegistryVersion = "2";

    public static bool IsApproved(string projectRelativePath, out string reason, out string version)
    {
        string normalized = (projectRelativePath ?? "").Replace('\\', '/');
        bool approved = normalized.StartsWith(ApprovedRoot, StringComparison.Ordinal)
            && (normalized.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
        bool sharedAudio = SharedCombatAudio.Contains(normalized);
        approved |= sharedAudio;
        reason = sharedAudio ? "existing shared combat punch/Whoosh media; preserve GUIDs and importer settings" : approved ? "project-owned shared presentation asset registry" : "";
        version = approved ? RegistryVersion : "";
        return approved;
    }
}
public static class CharacterPackageAssetOwnershipRegistry
{
    public const string RegistryVersion = "2-package-presentation-audio";
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PackageOwnedRoots =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["fightguy"] = new[] { "Assets/Art/Characters/fightguy/", "Assets/CharacterPackages/fightguy/", "Assets/Resources/Characters/FightGuy.prefab" },
            ["wibou"] = new[] { "Assets/Resources/Audio/SFX/Wibou/", "Assets/Art/Characters/wibou/", "Assets/CharacterPackages/wibou/", "Assets/Resources/Characters/Wibou.prefab" },
            ["bonk"] = new[] { "Assets/Resources/Audio/SFX/Bonk/", "Assets/Art/Characters/bonk/", "Assets/CharacterPackages/bonk/", "Assets/Resources/Characters/bonk.prefab" },
            ["manki"] = new[] { "Assets/Resources/Audio/SFX/Manki/", "Assets/Art/Characters/manki/", "Assets/CharacterPackages/manki/", "Assets/Resources/Characters/Manki.prefab", "Assets/Resources/WeaponConfigs/Manki.asset" },
            ["bashie"] = new[] { "Assets/Resources/Audio/SFX/Bashie/", "Assets/Art/Characters/bashie/", "Assets/CharacterPackages/bashie/" },
            ["salamander"] = new[] { "Assets/Art/Characters/salamander/", "Assets/CharacterPackages/salamander/" },
            ["nyx"] = new[] { "Assets/Art/Characters/nyx/", "Assets/CharacterPackages/nyx/" },
        };

    public static bool IsOwnedBy(string packageId, string projectRelativePath)
    {
        string normalized = (projectRelativePath ?? "").Replace('\\', '/');
        return PackageOwnedRoots.TryGetValue(packageId ?? "", out var roots)
            && roots.Any(root => normalized.StartsWith(root, StringComparison.Ordinal));
    }

    public static bool TryGetOwner(string projectRelativePath, out string packageId)
    {
        string normalized = (projectRelativePath ?? "").Replace('\\', '/');
        foreach (var entry in PackageOwnedRoots)
            if (entry.Value.Any(root => normalized.StartsWith(root, StringComparison.Ordinal)))
            {
                packageId = entry.Key;
                return true;
            }
        packageId = "";
        return false;
    }


}

public sealed class CharacterPackageCreateResult
{
    [JsonProperty("success")] public bool Success { get; }
    [JsonProperty("packageId")] public string PackageId { get; }
    [JsonProperty("sourcePath")] public string SourcePath { get; }
    [JsonProperty("catalogPath")] public string CatalogPath { get; }
    [JsonProperty("diagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }

    private CharacterPackageCreateResult(bool success, string packageId, string sourcePath, string catalogPath, IEnumerable<CharacterDiagnostic> diagnostics)
    {
        Success = success;
        PackageId = packageId;
        SourcePath = sourcePath;
        CatalogPath = catalogPath;
        Diagnostics = CharacterPackageDiagnosticResult.From(diagnostics);
    }

    internal static CharacterPackageCreateResult Successful(string packageId, string sourcePath, string catalogPath)
        => new(true, packageId, sourcePath, catalogPath, Array.Empty<CharacterDiagnostic>());
    internal static CharacterPackageCreateResult Failure(string packageId, string sourcePath, string catalogPath, IEnumerable<CharacterDiagnostic> diagnostics)
        => new(false, packageId, sourcePath, catalogPath, diagnostics);
}

public sealed class CharacterPackageBindingResult
{
    [JsonProperty("success")] public bool Success { get; }
    [JsonProperty("packageId")] public string PackageId { get; }
    [JsonProperty("sourcePath")] public string SourcePath { get; }
    [JsonProperty("semanticId")] public string SemanticId { get; }

    [JsonProperty("assetPath")] public string AssetPath { get; }
    [JsonProperty("persisted")] public bool Persisted { get; }
    [JsonProperty("dependency")] public CharacterPackageDependencyInfo Dependency { get; }
    [JsonProperty("diagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    internal CharacterAssetCatalog.AnimationBinding Binding { get; }

    private CharacterPackageBindingResult(bool success, string packageId, string sourcePath, string semanticId, string assetPath, bool persisted, CharacterPackageDependencyInfo dependency, IEnumerable<CharacterDiagnostic> diagnostics, CharacterAssetCatalog.AnimationBinding binding)
    {
        Success = success;
        PackageId = packageId;
        SourcePath = sourcePath;
        SemanticId = semanticId;
        AssetPath = assetPath;
        Persisted = persisted;
        Dependency = dependency;
        Diagnostics = CharacterPackageDiagnosticResult.From(diagnostics);
        Binding = binding;
    }

    internal static CharacterPackageBindingResult Successful(string packageId, string sourcePath, string semanticId, string assetPath, CharacterPackageDependencyInfo dependency, CharacterAssetCatalog.AnimationBinding binding)
        => new(true, packageId, sourcePath, semanticId, assetPath, true, dependency, Array.Empty<CharacterDiagnostic>(), binding);
    internal static CharacterPackageBindingResult Failure(string packageId, string sourcePath, string semanticId, string assetPath, IEnumerable<CharacterDiagnostic> diagnostics, CharacterPackageDependencyInfo dependency = null)
        => new(false, packageId, sourcePath, semanticId, assetPath, false, dependency, diagnostics, null);
}

public sealed class CharacterPackageProvenance
{
    [JsonProperty("packagePath")] public string PackagePath { get; }
    [JsonProperty("sourcePath")] public string SourcePath { get; }
    [JsonProperty("cookedPath")] public string CookedPath { get; }
    [JsonProperty("packageId")] public string PackageId { get; }
    [JsonProperty("version")] public string Version { get; }
    [JsonProperty("creator")] public string Creator { get; }
    [JsonProperty("license")] public string License { get; }
    [JsonProperty("attribution")] public string Attribution { get; }
    [JsonProperty("authoringSchemaVersion")] public ushort AuthoringSchemaVersion { get; }
    [JsonProperty("cookedSchemaVersion")] public ushort CookedSchemaVersion { get; }
    [JsonProperty("runtimeApiMin")] public string RuntimeApiMin { get; }
    [JsonProperty("runtimeApiMax")] public string RuntimeApiMax { get; }
    [JsonProperty("profile")] public CharacterCookProfile Profile { get; }
    [JsonProperty("cookerVersion")] public string CookerVersion { get; }
    [JsonProperty("unityVersion")] public string UnityVersion { get; }
    [JsonProperty("bindingSchemaVersion")] public int BindingSchemaVersion { get; }
    [JsonProperty("poseFormat")] public string PoseFormat { get; }
    [JsonProperty("poseVersion")] public int PoseVersion { get; }
    [JsonProperty("sampleRate")] public int SampleRate { get; }
    [JsonProperty("dependencies")] public IReadOnlyList<PackageDependencySource> Dependencies { get; }
    [JsonProperty("capabilityRequirements")] public IReadOnlyList<CookedCapabilityRequirement> CapabilityRequirements { get; }
    [JsonProperty("payloads")] public IReadOnlyList<CharacterPackagePayloadInfo> Payloads { get; }
    [JsonProperty("unityDependencies")] public IReadOnlyList<CharacterPackageUnityDependency> UnityDependencies { get; }
    [JsonProperty("warnings")] public IReadOnlyList<CharacterPackageDiagnosticResult> Warnings { get; }
    [JsonProperty("cookStatus")] public string CookStatus { get; }
    [JsonProperty("cookTarget")] public string CookTarget { get; }
    [JsonProperty("cookUnityVersion")] public string CookUnityVersion { get; }
    [JsonProperty("cookStatusDiagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> CookStatusDiagnostics { get; }

    private CharacterPackageProvenance(
        string sourcePath,
        string cookedPath,
        CharacterPackageManifestProjection manifest,
        CharacterCookProfile profile,
        CharacterCookStatus status)
    {
        PackagePath = sourcePath;
        SourcePath = sourcePath + "/character.json";
        CookedPath = cookedPath;
        PackageId = manifest.PackageId;
        Version = manifest.Version;
        Creator = manifest.Creator;
        License = manifest.License;
        Attribution = manifest.Attribution;
        AuthoringSchemaVersion = manifest.AuthoringSchemaVersion;
        CookedSchemaVersion = manifest.CookedSchemaVersion;
        RuntimeApiMin = manifest.RuntimeApiMin;
        RuntimeApiMax = manifest.RuntimeApiMax;
        Profile = profile;
        CookerVersion = manifest.CookerVersion;
        UnityVersion = manifest.UnityVersion;
        BindingSchemaVersion = manifest.BindingSchemaVersion;
        PoseFormat = manifest.PoseFormat;
        PoseVersion = manifest.PoseVersion;
        SampleRate = manifest.SampleRate;
        Dependencies = manifest.Dependencies.ToArray();
        CapabilityRequirements = manifest.CapabilityRequirements.ToArray();
        Payloads = manifest.Payloads.ToArray();
        UnityDependencies = (status?.Dependencies ?? new List<CharacterCookStatusDependency>())
            .Select(dependency => new CharacterPackageUnityDependency(dependency)).ToArray();
        Warnings = manifest.Warnings.ToArray();
        CookStatus = status?.State ?? "Unknown";
        CookTarget = status?.CookTarget ?? "";
        CookUnityVersion = status?.CookUnityVersion ?? "";
        CookStatusDiagnostics = (status?.Diagnostics ?? new List<CharacterCookStatusDiagnostic>())
            .Select(diagnostic => new CharacterPackageDiagnosticResult(diagnostic.Severity, diagnostic.Code, diagnostic.Path, diagnostic.Message))
            .ToArray();
    }

    internal static CharacterPackageProvenance Create(
        string sourcePath,
        string cookedPath,
        CharacterPackageManifestProjection manifest,
        CharacterCookProfile profile,
        CharacterCookStatus status)
        => new(sourcePath, cookedPath, manifest, profile, status);
}

public sealed class CharacterPackageUnityDependency
{
    [JsonProperty("kind")] public string Kind { get; }
    [JsonProperty("classification")] public string Classification { get; }
    [JsonProperty("sourcePackageId")] public string SourcePackageId { get; }
    [JsonProperty("sourcePath")] public string SourcePath { get; }
    [JsonProperty("approvalReason")] public string ApprovalReason { get; }
    [JsonProperty("approvalVersion")] public string ApprovalVersion { get; }
    [JsonProperty("identity")] public string Identity { get; }
    [JsonProperty("guid")] public string Guid { get; }
    [JsonProperty("dependencyHash")] public string DependencyHash { get; }
    [JsonProperty("metaHash")] public string MetaHash { get; }
    [JsonProperty("importerSettings")] public string ImporterSettings { get; }

    internal CharacterPackageUnityDependency(CharacterCookStatusDependency dependency)
    {
        Kind = dependency?.Kind ?? "";
        Classification = dependency?.Classification ?? "";
        SourcePackageId = dependency?.SourcePackageId ?? "";
        SourcePath = dependency?.SourcePath ?? "";
        ApprovalReason = dependency?.ApprovalReason ?? "";
        ApprovalVersion = dependency?.ApprovalVersion ?? "";
        Identity = dependency?.Identity ?? "";
        Guid = dependency?.Guid ?? "";
        DependencyHash = dependency?.DependencyHash ?? "";
        MetaHash = dependency?.MetaHash ?? "";
        ImporterSettings = dependency?.ImporterSettings ?? "";
    }

}

public sealed class CharacterPackageInspectionResult
{
    [JsonProperty("success", NullValueHandling = NullValueHandling.Include)] public bool Success { get; }
    [JsonProperty("packageId", NullValueHandling = NullValueHandling.Include)] public string PackageId { get; }
    [JsonProperty("displayName", NullValueHandling = NullValueHandling.Include)] public string DisplayName { get; }
    [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Include)] public string SourcePath { get; }
    [JsonProperty("catalogPath", NullValueHandling = NullValueHandling.Include)] public string CatalogPath
        => string.IsNullOrEmpty(SourcePath) ? null : SourcePath + "/CharacterAssetCatalog.asset";
    [JsonProperty("status", NullValueHandling = NullValueHandling.Include)] public string Status { get; }
    [JsonProperty("dirtyOrStale", NullValueHandling = NullValueHandling.Include)] public bool DirtyOrStale { get; }
    [JsonProperty("sourceHash", NullValueHandling = NullValueHandling.Include)] public string SourceHash { get; }
    [JsonProperty("cookedSourceHash", NullValueHandling = NullValueHandling.Include)] public string CookedSourceHash { get; }
    [JsonProperty("cookedContentHash", NullValueHandling = NullValueHandling.Include)] public string CookedContentHash { get; }
    [JsonProperty("packageHash", NullValueHandling = NullValueHandling.Include)] public string PackageHash { get; }
    [JsonProperty("staleReasons", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageStaleReason> StaleReasons { get; }
    [JsonProperty("slots", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageSlotSummary> Slots { get; }
    [JsonProperty("diagnostics", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    [JsonProperty("provenance", NullValueHandling = NullValueHandling.Include)] public CharacterPackageProvenance Provenance { get; }
    [JsonProperty("rostered")] public bool Rostered { get; }
    [JsonProperty("rosterSelector")] public string RosterSelector { get; }
    [JsonProperty("previewReady")] public bool PreviewReady { get; }
    internal CharacterPackageSource Source { get; }
    internal CharacterAssetCatalog Catalog { get; }
    internal IReadOnlyList<CharacterDiagnostic> RawDiagnostics { get; }

    private CharacterPackageInspectionResult(
        bool success,
        string packageId,
        string displayName,
        string sourcePath,
        string status,
        bool dirtyOrStale,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackageStaleReason> staleReasons,
        IReadOnlyList<CharacterPackageSlotSummary> slots,
        IReadOnlyList<CharacterPackageDiagnosticResult> diagnostics,
        CharacterPackageSource source,
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterDiagnostic> rawDiagnostics,
        CharacterPackageProvenance provenance,
        bool rostered,
        string rosterSelector,
        bool previewReady)
    {
        Success = success;
        PackageId = packageId;
        DisplayName = displayName;
        SourcePath = sourcePath;
        Status = status;
        DirtyOrStale = dirtyOrStale;
        SourceHash = sourceHash;
        CookedSourceHash = cookedSourceHash;
        CookedContentHash = cookedContentHash;
        PackageHash = packageHash;
        StaleReasons = staleReasons;
        Slots = slots;
        Diagnostics = diagnostics;
        Provenance = provenance;
        Source = source;
        Catalog = catalog;
        Rostered = rostered;
        RosterSelector = rosterSelector;
        PreviewReady = previewReady;
        RawDiagnostics = rawDiagnostics;
    }

    internal static CharacterPackageInspectionResult CreateSuccess(
        string packageId,
        string displayName,
        string sourcePath,
        string status,
        bool dirtyOrStale,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackageStaleReason> staleReasons,
        IReadOnlyList<CharacterPackageSlotSummary> slots,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageSource source,
        CharacterAssetCatalog catalog,
        CharacterPackageProvenance provenance,
        bool rostered = false,
        string rosterSelector = "",
        bool previewReady = false)
        => new CharacterPackageInspectionResult(
            true,
            packageId,
            displayName,
            sourcePath,
            status,
            dirtyOrStale,
            sourceHash,
            cookedSourceHash,
            cookedContentHash,
            packageHash,
            staleReasons.ToArray(),
            slots.ToArray(),
            CharacterPackageDiagnosticResult.From(diagnostics),
            source,
            catalog,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>(),
            provenance,
            rostered,
            rosterSelector,
            previewReady);

    internal static CharacterPackageInspectionResult CreateFailure(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => new CharacterPackageInspectionResult(
            false,
            null,
            null,
            null,
            "invalid",
            true,
            null,
            null,
            null,
            null,
            Array.Empty<CharacterPackageStaleReason>(),
            Array.Empty<CharacterPackageSlotSummary>(),
            CharacterPackageDiagnosticResult.From(diagnostics),
            null,
            null,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>(),
            null,
            false,
            "",
            false);
}

public sealed class CharacterRosterAdmissionResult
{
    [JsonProperty("success")] public bool Success { get; }
    [JsonProperty("packageId")] public string PackageId { get; }
    [JsonProperty("selector")] public string Selector { get; }
    [JsonProperty("diagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }

    private CharacterRosterAdmissionResult(bool success, string packageId, string selector, IEnumerable<CharacterDiagnostic> diagnostics)
    {
        Success = success;
        PackageId = packageId;
        Selector = selector;
        Diagnostics = CharacterPackageDiagnosticResult.From(diagnostics);
    }

    internal static CharacterRosterAdmissionResult Successful(string packageId, string selector)
        => new(true, packageId, selector, Array.Empty<CharacterDiagnostic>());
    internal static CharacterRosterAdmissionResult Failure(string packageId, IEnumerable<CharacterDiagnostic> diagnostics)
        => new(false, packageId, "", diagnostics);
}

public sealed class CharacterPackageAssetCandidate
{
    [JsonProperty("path")] public string Path { get; }
    [JsonProperty("name")] public string Name { get; }
    [JsonProperty("classification")] public string Classification { get; }
    [JsonProperty("sourcePackageId")] public string SourcePackageId { get; }
    [JsonProperty("rejectionReason")] public string RejectionReason { get; }
    [JsonProperty("accepted")] public bool Accepted => string.IsNullOrEmpty(RejectionReason);

    public CharacterPackageAssetCandidate(string path, string name, string classification, string sourcePackageId, string rejectionReason)
    {
        Path = path;
        Name = name;
        Classification = classification;
        SourcePackageId = sourcePackageId;
        RejectionReason = rejectionReason;
    }
}

public sealed class CharacterPackageAssetDiscoveryResult
{
    [JsonProperty("success")] public bool Success { get; }
    [JsonProperty("semanticId")] public string SemanticId { get; }
    [JsonProperty("candidates")] public IReadOnlyList<CharacterPackageAssetCandidate> Candidates { get; }
    [JsonProperty("diagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }

    private CharacterPackageAssetDiscoveryResult(bool success, string semanticId, IEnumerable<CharacterPackageAssetCandidate> candidates, IEnumerable<CharacterDiagnostic> diagnostics)
    {
        Success = success;
        SemanticId = semanticId;
        Candidates = (candidates ?? Array.Empty<CharacterPackageAssetCandidate>()).ToArray();
        Diagnostics = CharacterPackageDiagnosticResult.From(diagnostics);
    }

    internal static CharacterPackageAssetDiscoveryResult Successful(string semanticId, IEnumerable<CharacterPackageAssetCandidate> candidates)
        => new(true, semanticId, candidates, Array.Empty<CharacterDiagnostic>());
    internal static CharacterPackageAssetDiscoveryResult Failure(string semanticId, IEnumerable<CharacterDiagnostic> diagnostics)
        => new(false, semanticId, Array.Empty<CharacterPackageAssetCandidate>(), diagnostics);
}

public sealed class CharacterPackageVerificationResult
{
    [JsonProperty("success")] public bool Success { get; }
    [JsonProperty("packageId")] public string PackageId { get; }
    [JsonProperty("diagnostics")] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    [JsonProperty("inspection")] public CharacterPackageInspectionResult Inspection { get; }
    [JsonProperty("plan")] public CharacterPackageCookResult Plan { get; }

    private CharacterPackageVerificationResult(bool success, string packageId, IEnumerable<CharacterDiagnostic> diagnostics, CharacterPackageInspectionResult inspection, CharacterPackageCookResult plan)
    {
        Success = success;
        PackageId = packageId;
        Diagnostics = CharacterPackageDiagnosticResult.From(diagnostics);
        Inspection = inspection;
        Plan = plan;
    }

    internal static CharacterPackageVerificationResult Create(bool success, string packageId, IEnumerable<CharacterDiagnostic> diagnostics, CharacterPackageInspectionResult inspection, CharacterPackageCookResult plan)
        => new(success, packageId, diagnostics, inspection, plan);
    internal static CharacterPackageVerificationResult Failure(string packageId, IEnumerable<CharacterDiagnostic> diagnostics, CharacterPackageInspectionResult inspection, CharacterPackageCookResult plan)
        => new(false, packageId, diagnostics, inspection, plan);
}

public sealed class CharacterPackageCookResult
{
    [JsonProperty("success", NullValueHandling = NullValueHandling.Include)] public bool Success { get; }
    [JsonProperty("dryRun", NullValueHandling = NullValueHandling.Include)] public bool DryRun { get; }
    [JsonProperty("packageId", NullValueHandling = NullValueHandling.Include)] public string PackageId { get; }
    [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Include)] public string SourcePath { get; }
    [JsonProperty("cookedPath", NullValueHandling = NullValueHandling.Include)] public string CookedPath { get; }
    [JsonProperty("expectedOutputs", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<string> ExpectedOutputs { get; }
    [JsonProperty("sourceHash", NullValueHandling = NullValueHandling.Include)] public string SourceHash { get; }
    [JsonProperty("cookedSourceHash", NullValueHandling = NullValueHandling.Include)] public string CookedSourceHash { get; }
    [JsonProperty("cookedContentHash", NullValueHandling = NullValueHandling.Include)] public string CookedContentHash { get; }
    [JsonProperty("packageHash", NullValueHandling = NullValueHandling.Include)] public string PackageHash { get; }
    [JsonProperty("diagnostics", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    internal CharacterPackageAssemblyResult Assembly { get; }
    internal IReadOnlyList<CharacterDiagnostic> RawDiagnostics { get; }

    private CharacterPackageCookResult(
        bool success,
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IEnumerable<CharacterPackageDiagnosticResult> diagnostics,
        CharacterPackageAssemblyResult assembly,
        IReadOnlyList<CharacterDiagnostic> rawDiagnostics,
        bool dryRun,
        IEnumerable<string> expectedOutputs)
    {
        Success = success;
        DryRun = dryRun;
        PackageId = packageId;
        SourcePath = sourcePath;
        CookedPath = cookedPath;
        ExpectedOutputs = (expectedOutputs ?? Array.Empty<string>()).ToArray();
        SourceHash = string.IsNullOrEmpty(sourceHash) ? null : sourceHash;
        CookedSourceHash = string.IsNullOrEmpty(cookedSourceHash) ? null : cookedSourceHash;
        CookedContentHash = string.IsNullOrEmpty(cookedContentHash) ? null : cookedContentHash;
        PackageHash = string.IsNullOrEmpty(packageHash) ? null : packageHash;
        Diagnostics = (diagnostics ?? Array.Empty<CharacterPackageDiagnosticResult>()).ToArray();
        Assembly = assembly;
        RawDiagnostics = rawDiagnostics ?? Array.Empty<CharacterDiagnostic>();
    }

    internal static CharacterPackageCookResult CreateSuccess(
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageAssemblyResult assembly,
        bool dryRun = false,
        IReadOnlyList<string> expectedOutputs = null)
        => new CharacterPackageCookResult(
            true,
            packageId,
            sourcePath,
            cookedPath,
            sourceHash,
            sourceHash,
            cookedContentHash,
            packageHash,
            CharacterPackageDiagnosticResult.From(diagnostics),
            assembly,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>(),
            dryRun,
            expectedOutputs);

    internal static CharacterPackageCookResult CreateFailure(
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string packageHash,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageAssemblyResult assembly,
        bool dryRun = false,
        IReadOnlyList<string> expectedOutputs = null)
        => new CharacterPackageCookResult(
            false,
            packageId,
            sourcePath,
            cookedPath,
            sourceHash,
            null,
            null,
            packageHash,
            CharacterPackageDiagnosticResult.From(diagnostics),
            assembly,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>(),
            dryRun,
            expectedOutputs);
}

public sealed class CharacterPackageStaleReason
{
    [JsonProperty("code", NullValueHandling = NullValueHandling.Include)] public string Code { get; }
    [JsonProperty("path", NullValueHandling = NullValueHandling.Include)] public string Path { get; }
    [JsonProperty("message", NullValueHandling = NullValueHandling.Include)] public string Message { get; }

    public CharacterPackageStaleReason(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }
}

public sealed class CharacterPackageSlotSummary
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Include)] public string Id { get; }
    [JsonProperty("name", NullValueHandling = NullValueHandling.Include)] public string Name { get; }
    [JsonProperty("stageCount", NullValueHandling = NullValueHandling.Include)] public int StageCount { get; }
    [JsonProperty("present", NullValueHandling = NullValueHandling.Include)] public bool Present { get; }

    public CharacterPackageSlotSummary(string id, string name, int stageCount, bool present)
    {
        Id = id;
        Name = name;
        StageCount = stageCount;
        Present = present;
    }
}

public sealed class CharacterPackageDiagnosticResult
{
    [JsonProperty("severity", NullValueHandling = NullValueHandling.Include)] public string Severity { get; }
    [JsonProperty("code", NullValueHandling = NullValueHandling.Include)] public string Code { get; }
    [JsonProperty("path", NullValueHandling = NullValueHandling.Include)] public string Path { get; }
    [JsonProperty("message", NullValueHandling = NullValueHandling.Include)] public string Message { get; }

    internal CharacterPackageDiagnosticResult(string severity, string code, string path, string message)
    {
        Severity = severity;
        Code = code;
        Path = path;
        Message = message;
    }

    internal static IReadOnlyList<CharacterPackageDiagnosticResult> From(IEnumerable<CharacterDiagnostic> diagnostics)
        => (diagnostics ?? Array.Empty<CharacterDiagnostic>())
            .Select(x => new CharacterPackageDiagnosticResult(
                x.Severity == CharacterDiagnosticSeverity.Error ? "error" : "warning",
                x.Code,
                x.Path,
                x.Message))
            .ToArray();
}

[Serializable]
public sealed class CharacterCookStatus
{
    public string State = "Unknown";
    // Advisory authoring provenance, separate from the immutable runtime hash contract.
    public string CookedAtUtc = "";
    public string CookTarget = "";
    public string CookUnityVersion = "";
    public string CookPackagesManifestHash = "";
    public string CookPackageLockHash = "";
    public string SourceHash = "";
    public string CurrentSourceHash = "";
    public string CookedSourceHash = "";
    public string LastCookedSourceHash = "";
    public string CookedContentHash = "";
    public string PackageHash = "";
    public string GeneratedSourceHash = "";
    public List<CharacterCookStatusDependency> Dependencies = new List<CharacterCookStatusDependency>();
    public List<CharacterCookStatusPayload> Payloads = new List<CharacterCookStatusPayload>();
    public List<CharacterCookStatusDiagnostic> Diagnostics = new List<CharacterCookStatusDiagnostic>();
}

[Serializable]
public sealed class CharacterCookStatusDependency
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
[Serializable]
public sealed class CharacterCookStatusPayload
{
    public string Path = "";
    public string Sha256 = "";
    public long Size;
}

[Serializable]
public sealed class CharacterCookStatusDiagnostic
{
    public string Severity = "warning";
    public string Code = "";
    public string Path = "";
    public string Message = "";
}
