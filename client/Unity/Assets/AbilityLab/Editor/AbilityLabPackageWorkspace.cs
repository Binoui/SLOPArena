using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using SlopArena.Client.Entities;

namespace SlopArena.EditorTools;

public sealed class AbilityLabPackageWorkspace
{
    public string PackageRoot { get; private set; } = "";
    public PackageManifestSource Manifest { get; private set; } = null!;
    public CharacterAuthoringDocument Draft { get; private set; } = null!;
    public string LoadedDiskHash { get; private set; } = "";
    private string LoadedCatalogFingerprint { get; set; } = "";
    public bool IsDirty { get; private set; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics => _diagnostics;
    public string CookedSourceHash { get; private set; } = "";
    public string CookedContentHash { get; private set; } = "";
    public string PackageHash { get; private set; } = "";
    public CharacterAssetCatalog Catalog { get; private set; }
    public CharacterPackageAssemblyResult LastValidAssembly { get; private set; }
    public AbilityLabPackagePreviewResult Preview { get; private set; }
    public CookedCharacterPackage? LiveDraftPackage { get; private set; }
    public bool LiveDraftInvalid { get; private set; }
    public string Status { get; private set; } = "Unknown";
    private bool _persistedSourceCurrent;
    private string _persistedStatusStamp = "";
    private const int MaxUndoDepth = 50;
    private readonly Stack<WorkspaceSnapshot> _undo = new();
    private readonly Stack<WorkspaceSnapshot> _redo = new();
    public bool HasAttachmentDraft => _attachmentDraft != null;
    public bool AttachmentDraftDirty => _attachmentDraft != null && _attachmentDraft.Value != _attachmentDraft.Saved;
    public int AttachmentDraftEntryIndex => _attachmentDraft?.EntryIndex ?? -1;
    public WeaponAttachConfig AttachmentDraftSourceConfig => _attachmentDraft?.Config;
    public string AttachmentDraftError { get; private set; } = "";
    public Vector3 AttachmentPositionOffset => _attachmentDraft?.Value.PositionOffset ?? Vector3.zero;
    public Vector3 AttachmentRotationOffset => _attachmentDraft?.Value.RotationOffset ?? Vector3.zero;
    public bool AttachmentHasFirePhaseOverride => _attachmentDraft?.Value.HasFirePhaseOverride ?? false;
    public Vector3 AttachmentFirePositionOffset => _attachmentDraft?.Value.FirePositionOffset ?? Vector3.zero;
    public Vector3 AttachmentFireRotationOffset => _attachmentDraft?.Value.FireRotationOffset ?? Vector3.zero;
    public bool CanUndoAttachmentDraft => _attachmentDraft != null && _attachmentUndo.Count > 0;
    public bool CanRedoAttachmentDraft => _attachmentDraft != null && _attachmentRedo.Count > 0;
    private AttachmentDraftState _attachmentDraft;
    private readonly Stack<AttachmentValues> _attachmentUndo = new();
    private readonly Stack<AttachmentValues> _attachmentRedo = new();

    private readonly List<CharacterDiagnostic> _diagnostics = new();
    public event Action? StatusChanged;

    public bool HasPackage => !string.IsNullOrEmpty(PackageRoot) && Manifest != null && Draft != null;
    public string PackageId => Manifest?.PackageId ?? "";

    public bool NewPackage(string packageId, string displayName, string creator = "Binoui", string license = "MIT", string attribution = "SlopArena")
    {
        if (AttachmentDraftDirty)
            return SetAttachmentDraftError("Save or revert the attachment draft before creating another package.");
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageCreateResult result = service.NewPackage(packageId, displayName, creator, license, attribution);
        if (!result.Success)
        {
            SetDiagnostics((result.Diagnostics ?? Array.Empty<CharacterPackageDiagnosticResult>())
                .Select(x => new CharacterDiagnostic(
                    x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                    x.Code, x.Path, x.Message)), "Failed");
            return false;
        }
        return OpenPackage(result.SourcePath);

    }
    public bool OpenPackage(string packageRoot)
    {
        if (AttachmentDraftDirty)
            return SamePackage(PackageRoot, packageRoot)
                ? true
                : SetAttachmentDraftError("Save or revert the attachment draft before opening another package.");
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(packageRoot);
        if (!inspection.Success || inspection.Source == null || inspection.Catalog == null)
        {
            SetDiagnostics(inspection.RawDiagnostics, "Failed");
            return false;
        }
        return OpenPackage(inspection);
    }

    internal bool OpenPackage(CharacterPackageInspectionResult inspection)
    {
        if (inspection == null || !inspection.Success || inspection.Source == null || inspection.Catalog == null)
            return false;
        if (AttachmentDraftDirty)
            return SamePackage(PackageRoot, inspection.SourcePath)
                ? true
                : SetAttachmentDraftError("Save or revert the attachment draft before opening another package.");

        _attachmentDraft = null;
        _attachmentUndo.Clear();
        _attachmentRedo.Clear();
        AttachmentDraftError = "";
        LiveDraftPackage = null;
        LiveDraftInvalid = false;
        PackageRoot = inspection.SourcePath;
        Manifest = inspection.Source.Manifest;
        Draft = inspection.Source.Character;
        Catalog = inspection.Catalog;
        _undo.Clear();
        _redo.Clear();
        AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
        AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
        LoadedDiskHash = ComputeDiskHash();
        LoadedCatalogFingerprint = ComputeCatalogFingerprint();
        IsDirty = false;
        CookedSourceHash = inspection.CookedSourceHash ?? "";
        CookedContentHash = inspection.CookedContentHash ?? "";
        PackageHash = inspection.PackageHash ?? "";
        _persistedSourceCurrent = inspection.Status == "valid";
        _persistedStatusStamp = PersistedStatusStamp();
        var inspectionDiagnostics = new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Status == "stale")
            inspectionDiagnostics.AddRange((inspection.StaleReasons ?? Array.Empty<CharacterPackageStaleReason>())
                .Select(reason => new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, reason.Code, reason.Path, reason.Message)));
        SetDiagnostics(inspectionDiagnostics, inspection.Status == "valid" ? "Valid" : inspection.Status == "stale" ? "Stale" : "Failed");
        Preview = AbilityLabPackagePreviewLoader.Load(Manifest.PackageId);
        if (Preview.IsAvailable)
            AbilityLab.Instance?.ApplyPackagePreview(Preview);
        else
            AbilityLab.Instance?.ApplyPreviewUnavailable(Preview.Diagnostics);

        if (!Preview.IsAvailable)
        {
            var previewDiagnostics = _diagnostics.Concat(Preview.Diagnostics).ToArray();
            SetDiagnostics(previewDiagnostics, Status);
        }
        return true;
    }

    public bool ReloadPackage() => HasPackage && OpenPackage(PackageRoot);

    /// <summary>Observe external source/cook changes without replacing the draft, Undo history or Lab cursor.</summary>
    public bool RefreshPersistedStatus(bool force = false)
    {
        if (!HasPackage || Status == "Cooking") return false;
        string stamp = PersistedStatusStamp();
        if (!force && stamp == _persistedStatusStamp) return false;
        _persistedStatusStamp = stamp;
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(PackageRoot);
        bool sourceMatches = ComputeDiskHash() == LoadedDiskHash && ComputeCatalogFingerprint() == LoadedCatalogFingerprint;
        _persistedSourceCurrent = sourceMatches && inspection.Status == "valid";
        var diagnostics = LiveDraftInvalid ? _diagnostics.ToList() : new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Status == "stale")
            diagnostics.AddRange((inspection.StaleReasons ?? Array.Empty<CharacterPackageStaleReason>())
                .Select(reason => new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, reason.Code, reason.Path, reason.Message)));
        if (!sourceMatches)
            diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "workspace.external.changed", PackageRoot,
                "Package source or asset bindings changed outside this workspace. Your current draft is retained; reopen the package to load those changes."));
        CookedSourceHash = inspection.CookedSourceHash ?? "";
        CookedContentHash = inspection.CookedContentHash ?? "";
        PackageHash = inspection.PackageHash ?? "";
        string status = LiveDraftInvalid ? "Failed" : _persistedSourceCurrent && !IsDirty ? "Valid" : "Stale";
        SetDiagnosticsWithoutNotify(diagnostics, status);
        return true;
    }

    private string PersistedStatusStamp()
    {
        string project = UnityCharacterAssetCooker.ProjectRoot();
        string[] paths =
        {
            ToFull(PackageRoot + "/package.json"), ToFull(PackageRoot + "/character.json"),
            ToFull(PackageRoot + "/CharacterAssetCatalog.asset"),
            Path.Combine(project, CharacterCookOutput.For(PackageId).IntermediateDirectory, "cook-status.json"),
        };
        return string.Join("|", paths.Select(path => File.Exists(path)
            ? $"{new FileInfo(path).LastWriteTimeUtc.Ticks}:{new FileInfo(path).Length}" : "missing"))
            + "|" + AssetDatabase.GetAssetDependencyHash(PackageRoot + "/CharacterAssetCatalog.asset");
    }
    public bool SavePackage()
    {
        if (ComputeDiskHash() != LoadedDiskHash)
            return Fail("workspace.conflict", PackageRoot, "Package source changed externally; reload before saving.");
        if (ComputeCatalogFingerprint() != LoadedCatalogFingerprint)
            return Fail("workspace.conflict", PackageRoot + "/CharacterAssetCatalog.asset", "Character asset catalog changed externally; reload before saving.");
        string packagePath = ToFull(PackageRoot + "/package.json"); string characterPath = ToFull(PackageRoot + "/character.json");
        try
        {
            string packageJson = CharacterPackageSourceCodec.SerializeManifest(Manifest);
            string characterJson = CharacterPackageSourceCodec.SerializeCharacter(Draft);
            ReplaceSources(packagePath, characterPath, Encoding.UTF8.GetBytes(packageJson), Encoding.UTF8.GetBytes(characterJson));
            LoadedDiskHash = ComputeDiskHash();
            IsDirty = false;
            SetDiagnostics(Array.Empty<CharacterDiagnostic>(), "Cooking");
            var cook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(PackageRoot);
            if (!cook.Success || cook.Assembly == null)
            {
                SetDiagnostics(cook.RawDiagnostics, "Failed");
                return false;
            }

            var loadedAssembly = CookedCharacterPackageLoader.LoadAssembly(cook.Assembly);
            if (!loadedAssembly.IsValid)
            {
                SetDiagnostics(loadedAssembly.Diagnostics, "Failed");
                return false;
            }

            var candidatePreview = AbilityLabPackagePreviewLoader.Load(PackageId);
            if (!candidatePreview.IsAvailable)
            {
                SetDiagnostics(loadedAssembly.Diagnostics.Concat(candidatePreview.Diagnostics), "Failed");
                return false;
            }

            LastValidAssembly = cook.Assembly;
            CookedSourceHash = cook.SourceHash;
            CookedContentHash = cook.CookedContentHash;
            PackageHash = cook.PackageHash;
            _persistedSourceCurrent = true;
            _persistedStatusStamp = PersistedStatusStamp();
            Preview = candidatePreview;
            LiveDraftPackage = null;
            LiveDraftInvalid = false;
            SetDiagnostics(loadedAssembly.Diagnostics.Concat(candidatePreview.Diagnostics), "Valid");
            AbilityLab.Instance?.ApplyPackagePreview(candidatePreview);
            return true;
        }
        catch (Exception ex)
        {
            SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "workspace.save.failed", PackageRoot, ex.Message) }, "Failed");
            return false;
        }
    }

    public bool RenameSemanticId(string oldId, string newId)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        var snapshots = (Catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Where(x => x != null)
            .Select(x => new CharacterAssetCatalogBindingSnapshot(x.SemanticId ?? "", x.PoseTrackId ?? ""))
            .Concat((Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Where(x => x != null)
                .Select(x => new CharacterAssetCatalogBindingSnapshot(x.SemanticId ?? "", "")))
            .ToArray();
        var result = CharacterPackageSourceCodec.RenameSemanticId(new CharacterPackageSource(Manifest, Draft), oldId, newId, snapshots);
        if (!result.IsValid || result.Source == null) { SetDiagnosticsWithoutNotify(result.Diagnostics, "Failed"); return false; }
        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Rename semantic ID");
            Manifest = result.Source.Manifest;
            Draft = result.Source.Character;
            Catalog.Bindings = CloneBindings(Catalog.Bindings)
                .Select(x => x == null ? null : new CharacterAssetCatalog.AnimationBinding
                {
                    SemanticId = x.SemanticId == oldId ? newId : x.SemanticId,
                    PoseTrackId = x.PoseTrackId == oldId ? newId : x.PoseTrackId,
                    Clip = x.Clip,
                    Extrapolation = x.Extrapolation,
                }).ToArray();
            Catalog.Presentations = ClonePresentations(Catalog.Presentations)
                .Select(x => x == null ? null : new CharacterAssetCatalog.PresentationBinding
                {
                    SemanticId = x.SemanticId == oldId ? newId : x.SemanticId,
                    Prefab = x.Prefab,
                }).ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            AbilityLab.Instance?.SetSourceDocument(result.Source);
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
            IsDirty = true;
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            SetDiagnosticsWithoutNotify(result.Diagnostics, "Stale");
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            SetDiagnosticsWithoutNotify(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "rename.failed", "catalog", ex.Message) }, "Failed");
            return false;
        }
    }

    public bool BeginAttachmentDraft(int entryIndex)
    {
        if (AttachmentDraftDirty && AttachmentDraftEntryIndex != entryIndex)
            return SetAttachmentDraftError("Save or revert the current attachment draft before selecting another entry.");
        var config = Catalog?.WeaponConfig;
        if (config?.Entries == null || entryIndex < 0 || entryIndex >= config.Entries.Length || config.Entries[entryIndex] == null)
            return SetAttachmentDraftError("The selected weapon attachment entry is unavailable.");
        if (_attachmentDraft != null && _attachmentDraft.Config == config && _attachmentDraft.EntryIndex == entryIndex)
            return true;
        _attachmentDraft = new AttachmentDraftState(config, entryIndex, CaptureAttachmentValues(config.Entries[entryIndex]));
        _attachmentUndo.Clear();
        _attachmentRedo.Clear();
        AttachmentDraftError = "";
        return true;
    }

    public bool SetAttachmentPositionOffset(Vector3 value) =>
        ChangeAttachmentDraft(x => { x.PositionOffset = value; return x; });
    public bool SetAttachmentRotationOffset(Vector3 value) =>
        ChangeAttachmentDraft(x => { x.RotationOffset = value; return x; });
    public bool SetAttachmentFirePhaseOverride(bool value) => ChangeAttachmentDraft(x =>
    {
        if (value && !x.HasFirePhaseOverride)
        {
            x.FirePositionOffset = x.PositionOffset;
            x.FireRotationOffset = x.RotationOffset;
        }
        x.HasFirePhaseOverride = value;
        return x;
    });
    public bool SetAttachmentFirePositionOffset(Vector3 value) =>
        ChangeAttachmentDraft(x => { x.FirePositionOffset = value; return x; });
    public bool SetAttachmentFireRotationOffset(Vector3 value) =>
        ChangeAttachmentDraft(x => { x.FireRotationOffset = value; return x; });

    public bool UndoAttachmentDraft()
    {
        if (!CanUndoAttachmentDraft) return false;
        _attachmentRedo.Push(_attachmentDraft.Value);
        _attachmentDraft.Value = _attachmentUndo.Pop();
        AttachmentDraftError = "";
        SceneView.RepaintAll();
        return true;
    }

    public bool RedoAttachmentDraft()
    {
        if (!CanRedoAttachmentDraft) return false;
        _attachmentUndo.Push(_attachmentDraft.Value);
        _attachmentDraft.Value = _attachmentRedo.Pop();
        AttachmentDraftError = "";
        SceneView.RepaintAll();
        return true;
    }

    public bool RevertAttachmentDraft()
    {
        if (_attachmentDraft == null) return false;
        _attachmentDraft = null;
        _attachmentUndo.Clear();
        _attachmentRedo.Clear();
        AttachmentDraftError = "";
        SceneView.RepaintAll();
        return true;
    }

    public bool SaveAttachmentDraft()
    {
        if (_attachmentDraft == null) return SetAttachmentDraftError("No attachment draft is selected.");
        var draft = _attachmentDraft;
        if (draft.Config == null || draft.Config.Entries == null || draft.EntryIndex < 0
            || draft.EntryIndex >= draft.Config.Entries.Length || draft.Config.Entries[draft.EntryIndex] == null)
            return SetAttachmentDraftError("The attachment source entry no longer exists.");
        var entry = draft.Config.Entries[draft.EntryIndex];
        if (CaptureAttachmentValues(entry) != draft.Saved)
            return SetAttachmentDraftError("Attachment source changed externally; revert and reload before saving.");
        if (!AttachmentDraftDirty)
        {
            AttachmentDraftError = "";
            return true;
        }
        try
        {
            UnityEditor.Undo.RecordObject(draft.Config, "Save Ability Lab attachment placement");
            ApplyAttachmentValues(entry, draft.Value);
            EditorUtility.SetDirty(draft.Config);
            AssetDatabase.SaveAssetIfDirty(draft.Config);
        }
        catch (Exception ex)
        {
            ApplyAttachmentValues(entry, draft.Saved);
            EditorUtility.SetDirty(draft.Config);
            return SetAttachmentDraftError("Could not save attachment source: " + ex.Message);
        }
        draft.Saved = draft.Value;
        IsDirty = true;
        Status = "Stale";
        AttachmentDraftError = "";
        StatusChanged?.Invoke();
        SceneView.RepaintAll();
        return true;
    }

    public WeaponAttachConfig CreateAttachmentPreviewConfig()
    {
        var source = _attachmentDraft?.Config ?? Catalog?.WeaponConfig;
        if (source == null) return null;
        var preview = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        preview.hideFlags = HideFlags.HideAndDontSave;
        preview.Entries = (source.Entries ?? Array.Empty<WeaponEntry>())
            .Select(CloneWeaponEntry).ToArray();
        if (_attachmentDraft != null && _attachmentDraft.EntryIndex >= 0
            && _attachmentDraft.EntryIndex < preview.Entries.Length && preview.Entries[_attachmentDraft.EntryIndex] != null)
            ApplyAttachmentValues(preview.Entries[_attachmentDraft.EntryIndex], _attachmentDraft.Value);
        return preview;
    }
    private static WeaponEntry CloneWeaponEntry(WeaponEntry entry) => entry == null ? null : new WeaponEntry
    {
        AttackSlot = entry.AttackSlot,
        BoneName = entry.BoneName,
        Prefab = entry.Prefab,
        PositionOffset = entry.PositionOffset,
        RotationOffset = entry.RotationOffset,
        HasFirePhaseOverride = entry.HasFirePhaseOverride,
        FirePositionOffset = entry.FirePositionOffset,
        FireRotationOffset = entry.FireRotationOffset,
        HideAfterTicks = entry.HideAfterTicks,
        TrailStylePrefab = entry.TrailStylePrefab,
        HitboxMotionTime = entry.HitboxMotionTime,
        TrailBladeWidth = entry.TrailBladeWidth,
        TrailHiltAnchor = entry.TrailHiltAnchor,
        TrailTipAnchor = entry.TrailTipAnchor,
    };

    private bool ChangeAttachmentDraft(Func<AttachmentValues, AttachmentValues> change)
    {
        if (_attachmentDraft == null) return SetAttachmentDraftError("Select a weapon attachment before editing.");
        var next = change(_attachmentDraft.Value);
        if (!next.HasFiniteOffsets) return SetAttachmentDraftError("Attachment offsets must be finite numbers.");
        if (next == _attachmentDraft.Value) return false;
        _attachmentUndo.Push(_attachmentDraft.Value);
        if (_attachmentUndo.Count > MaxUndoDepth) TrimOldest(_attachmentUndo);
        _attachmentRedo.Clear();
        _attachmentDraft.Value = next;
        AttachmentDraftError = "";
        SceneView.RepaintAll();
        return true;
    }

    private static void TrimOldest<T>(Stack<T> stack)
    {
        var retained = stack.Take(MaxUndoDepth).Reverse().ToArray();
        stack.Clear();
        foreach (var item in retained) stack.Push(item);
    }

    private bool SetAttachmentDraftError(string message)
    {
        AttachmentDraftError = message;
        return false;
    }

    private static AttachmentValues CaptureAttachmentValues(WeaponEntry entry) => new()
    {
        PositionOffset = entry.PositionOffset,
        RotationOffset = entry.RotationOffset,
        HasFirePhaseOverride = entry.HasFirePhaseOverride,
        FirePositionOffset = entry.FirePositionOffset,
        FireRotationOffset = entry.FireRotationOffset,
    };

    private static void ApplyAttachmentValues(WeaponEntry entry, AttachmentValues values)
    {
        entry.PositionOffset = values.PositionOffset;
        entry.RotationOffset = values.RotationOffset;
        entry.HasFirePhaseOverride = values.HasFirePhaseOverride;
        entry.FirePositionOffset = values.FirePositionOffset;
        entry.FireRotationOffset = values.FireRotationOffset;
    }

    public bool ReplaceTrailBladeWidth(int entryIndex, float width)
    {
        var config = Catalog?.WeaponConfig;
        if (!HasPackage || config?.Entries == null || entryIndex < 0
            || entryIndex >= config.Entries.Length || config.Entries[entryIndex]?.TrailStylePrefab == null)
            return false;
        width = Mathf.Clamp(width, 0.01f, 1f);
        var entry = config.Entries[entryIndex];
        if (entry.TrailBladeWidth == width) return false;
        var prior = CaptureSnapshot(entryIndex);
        UnityEditor.Undo.RecordObject(config, "Change blade trail width");
        entry.TrailBladeWidth = width;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        PushUndo(prior);
        IsDirty = true;
        Status = "Stale";
        return true;
    }

    public bool ReplaceTrailStyle(int entryIndex, GameObject style)
    {
        var config = Catalog?.WeaponConfig;
        if (!HasPackage || config?.Entries == null || entryIndex < 0
            || entryIndex >= config.Entries.Length || config.Entries[entryIndex] == null)
            return false;
        if (style != null)
        {
            var system = style.GetComponentInChildren<ParticleSystem>(true);
            if (system == null || system.GetComponent<ParticleSystemRenderer>()?.sharedMaterial == null)
                return false;
        }
        var entry = config.Entries[entryIndex];
        if (entry.TrailStylePrefab == style) return false;
        var prior = CaptureSnapshot(entryIndex);
        UnityEditor.Undo.RecordObject(config, "Change blade trail style");
        entry.TrailStylePrefab = style;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        PushUndo(prior);
        IsDirty = true;
        Status = "Stale";
        return true;
    }

    public bool ReplaceCatalogRig(GameObject rig)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Replace character catalog rig");
            Catalog.Rig = rig;
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            MarkCatalogEdited();
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.failed", "catalog.rig", ex.Message);
        }
    }

    public bool ReplaceCatalogBinding(string semanticId, AnimationClip clip, ExtrapolationMode extrapolation)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        CharacterPackageAuthoringService service = new(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageBindingResult result = clip == null
            ? service.Unbind(PackageRoot, semanticId)
            : service.Bind(PackageRoot, semanticId, AssetDatabase.GetAssetPath(clip), extrapolation);
        if (!result.Success)
        {
            SetDiagnostics((result.Diagnostics ?? Array.Empty<CharacterPackageDiagnosticResult>())
                .Select(x => new CharacterDiagnostic(
                    x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                    x.Code, x.Path, x.Message)), "Failed");
            return false;
        }
        Catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(PackageRoot + "/CharacterAssetCatalog.asset");
        PushUndo(prior);
        MarkCatalogEdited();
        SetDiagnosticsWithoutNotify(Array.Empty<CharacterDiagnostic>(), "Stale");
        SceneView.RepaintAll();
        return true;
    }
    public bool AddPresentationAsset(string semanticId, GameObject prefab)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        semanticId = semanticId?.Trim() ?? "";
        if (string.IsNullOrEmpty(semanticId))
            return Fail("asset-catalog.presentation-id.missing", "presentationId", "Presentation ID is required.");
        if (prefab == null)
            return Fail("asset-catalog.presentation.prefab.missing", semanticId, "Presentation prefab is required.");
        if ((Draft.PresentationIds ?? Array.Empty<string>()).Contains(semanticId, StringComparer.Ordinal) ||
            (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Any(binding => binding != null && binding.SemanticId == semanticId))
            return Fail("asset-catalog.presentation.duplicate", semanticId, "Presentation ID already exists.");

        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Add presentation asset");
            Draft = Draft with
            {
                PresentationIds = (Draft.PresentationIds ?? Array.Empty<string>()).Concat(new[] { semanticId }).ToArray(),
            };
            Catalog.Presentations = (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Concat(new[]
                {
                    new CharacterAssetCatalog.PresentationBinding { SemanticId = semanticId, Prefab = prefab },
                }).ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            IsDirty = true;
            AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
            RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.presentation.failed", semanticId, ex.Message);
        }
    }

    public bool ReplaceCatalogPresentation(string semanticId, GameObject prefab)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        var existing = (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .FirstOrDefault(binding => binding != null && binding.SemanticId == semanticId);
        if (existing == null)
            return Fail("asset-catalog.presentation.missing", semanticId, "Presentation binding does not exist.");

        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Replace presentation asset");
            Catalog.Presentations = ClonePresentations(Catalog.Presentations)
                .Select(binding => binding != null && binding.SemanticId == semanticId
                    ? new CharacterAssetCatalog.PresentationBinding { SemanticId = semanticId, Prefab = prefab }
                    : binding)
                .ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            MarkCatalogEdited();
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.presentation.failed", semanticId, ex.Message);
        }
    }


    private void MarkCatalogEdited()
    {
        LoadedCatalogFingerprint = CharacterPackageAuthoringService.ComputeCatalogFingerprint(Catalog);
        IsDirty = true;
        SetDiagnosticsWithoutNotify(_diagnostics, "Stale");
        AbilityLab.Instance?.SetPresentationBindings(Catalog?.Presentations);
    }
    public bool TryResolveCanonicalSlot(string canonicalSlotId, out int explicitSourceSlotIndex, out CharacterSlotSource sourceSlot)
    {
        explicitSourceSlotIndex = -1;
        sourceSlot = null!;
        if (!HasPackage || !CanonicalSlotProjection.TryGet(canonicalSlotId, out _)) return false;

        var explicitById = Draft.Slots
            .Select((slot, index) => (slot, index))
            .Where(item => item.slot != null)
            .ToDictionary(item => item.slot.Id, item => item.index, StringComparer.Ordinal);
        var aliases = (Draft.Aliases ?? Array.Empty<CharacterAliasSource>())
            .Where(alias => alias != null)
            .ToDictionary(alias => alias.From, alias => alias.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = canonicalSlotId;
        while (true)
        {
            if (!visited.Add(current)) return false;
            if (explicitById.TryGetValue(current, out explicitSourceSlotIndex))
            {
                sourceSlot = Draft.Slots[explicitSourceSlotIndex];
                return true;
            }
            if (!aliases.TryGetValue(current, out string target)) return false;
            current = target;
        }
    }

    public bool ReplaceGeneral(string displayName, float weight, float capsuleRadius, float capsuleHeight, float hipHeight, float hurtboxRadius)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplaceGeneral(
                new CharacterPackageSource(Manifest, Draft), displayName, weight, capsuleRadius, capsuleHeight, hipHeight, hurtboxRadius));

    public bool ReplaceMovement(CharacterMovementSource value)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplaceMovement(new CharacterPackageSource(Manifest, Draft), value));
    public bool ReplaceCaptureGeometry(CharacterCaptureGeometrySource value)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplaceCaptureGeometry(
                new CharacterPackageSource(Manifest, Draft), value));

    public bool ReplacePresentation(CharacterPresentationSource value)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplacePresentation(new CharacterPackageSource(Manifest, Draft), value));

    public bool ReplaceStage(string canonicalSlotId, int stageIndex, CharacterStageSource stage)
    {
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ReplaceStage(slotIndex, stageIndex, stage);
    }
    public bool ReplaceOperationTick(string canonicalSlotId, int stageIndex, int operationIndex, int tick)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyEdit(CharacterPackageSourceCodec.ReplaceOperationTick(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, tick));
    }

    public bool ReplaceHitboxDuration(string canonicalSlotId, int stageIndex, int operationIndex, int durationTicks)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyEdit(CharacterPackageSourceCodec.ReplaceHitboxDuration(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, durationTicks));
    }
    public bool ReplacePresentationPlacement(string canonicalSlotId, int stageIndex, int operationIndex, PresentationPlacement placement)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyEdit(CharacterPackageSourceCodec.ReplacePresentationPlacement(
            new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, placement));
    }



    public bool ReplaceHitbox(string canonicalSlotId, int stageIndex, int operationIndex, HitboxSource hitbox)
    {
        if (hitbox == null) return Fail("edit.source.missing", "hitbox", "Hitbox is required.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        var operations = sourceSlot.Timeline.Stages[stageIndex].Operations;
        if (operationIndex < 0 || operationIndex >= operations.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}].operations[{operationIndex}]", "Operation index is out of range.");
        if (operations[operationIndex] is not SpawnHitboxOperationSource original)
            return Fail("edit.operation.type", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}].operations[{operationIndex}]", "Selected operation is not a hitbox operation.");
        return ReplaceOperation(slotIndex, stageIndex, operationIndex, original with { Hitbox = hitbox });
    }
    public bool AddHitbox(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        var hitbox = new HitboxSource(
            AuthoringHitboxShape.Sphere, 0.5f,
            0f, 0f, 0f, 0f, 0f, 0f,
            "bone.hips", null,
            1f, 45f, 5f, 80f, 8, 1, false, 0);
        return AddOperation(slotIndex, stageIndex, new SpawnHitboxOperationSource(0, AuthoringUnit.Meters, hitbox));
    }
    public bool AddForwardLunge(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        return AddOperation(slotIndex, stageIndex,
            new ForwardLungeOperationSource(0, AuthoringUnit.MetersPerSecond, 12f, 6));
    }

    public bool AddGravityWindow(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        ushort duration = (ushort)Math.Min(30, (int)sourceSlot.Timeline.Stages[stageIndex].DurationTicks);
        return AddOperation(slotIndex, stageIndex,
            new GravityWindowOperationSource(0, AuthoringUnit.Normalized, 0.5f, duration));
    }

    public bool AddTargetedLeap(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        if (sourceSlot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(operation => operation.CapabilityId == CharacterPackageCompiler.TargetedLeapCapabilityId))
            return Fail("edit.operation.duplicate", canonicalSlotId, "This move already has a targeted leap.");

        var requirements = Draft.CapabilityRequirements.ToList();
        if (requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.TargetedLeapCapabilityId &&
            item.CapabilityVersion != CharacterPackageCompiler.TargetedLeapCapabilityVersion))
            return Fail("edit.capability.version", canonicalSlotId, "Targeted leap requires a different capability version.");
        if (!requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.TargetedLeapCapabilityId))
            requirements.Add(new CapabilityRequirementSource(
                CharacterPackageCompiler.TargetedLeapCapabilityId,
                CharacterPackageCompiler.TargetedLeapCapabilityVersion));

        ushort duration = sourceSlot.Timeline.Stages[stageIndex].DurationTicks;
        ushort recoveryTicks = (ushort)Math.Max(1, Math.Min(20, (int)duration));
        var hitbox = new HitboxSource(
            AuthoringHitboxShape.Sphere, 0.5f,
            0f, 0f, 0f, 0f, 0f, 0f,
            "bone.hips", null,
            1f, 45f, 5f, 80f, 8, (ushort)Math.Min(6, (int)recoveryTicks), true, 0);
        var parameters = new TargetedLeapCapabilityParameters(
            0, 120, 1f, 12f, 10f, 0, recoveryTicks, hitbox);
        var source = new CharacterPackageSource(Manifest,
            Draft with { CapabilityRequirements = requirements });
        return ApplyEdit(CharacterPackageSourceCodec.AddOperation(source, slotIndex, stageIndex,
            new StartCapabilityOperationSource(0, AuthoringUnit.Ticks,
                CharacterPackageCompiler.TargetedLeapCapabilityId,
                CharacterPackageCompiler.TargetedLeapCapabilityVersion, parameters)));
    }

    public bool AddChargedDirectionalDash(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        if (stageIndex != 0 || sourceSlot.Timeline.Stages.Count != 1)
            return Fail("edit.charged-dash.stage", canonicalSlotId, "Charged directional dash requires one stage starting at tick zero.");
        var stage = sourceSlot.Timeline.Stages[stageIndex];
        if (stage.Operations.Any(operation => operation is SpawnHitboxOperationSource
            or ForwardLungeOperationSource or SetVelocityOperationSource or StartCapabilityOperationSource
            or StartupAimCorrectionOperationSource))
            return Fail("edit.charged-dash.operation-conflict", canonicalSlotId,
                "Remove authored hitboxes, motion, startup correction, and other lifecycles before adding this primitive.");
        if (sourceSlot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(operation => operation.CapabilityId == CharacterPackageCompiler.ChargedDirectionalDashCapabilityId))
            return Fail("edit.operation.duplicate", canonicalSlotId, "This move already has a charged directional dash.");

        var requirements = Draft.CapabilityRequirements.ToList();
        if (requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.ChargedDirectionalDashCapabilityId &&
            item.CapabilityVersion != CharacterPackageCompiler.ChargedDirectionalDashCapabilityVersion))
            return Fail("edit.capability.version", canonicalSlotId, "Charged directional dash requires a different capability version.");
        if (!requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.ChargedDirectionalDashCapabilityId))
            requirements.Add(new CapabilityRequirementSource(
                CharacterPackageCompiler.ChargedDirectionalDashCapabilityId,
                CharacterPackageCompiler.ChargedDirectionalDashCapabilityVersion));

        var traversal = new HitboxSource(AuthoringHitboxShape.Sphere, 0.45f,
            0f, 0f, 0f, 0f, 0f, 0f, "bone.hips", null,
            1f, 0f, 0f, 0f, 0, 1, true, 0);
        bool hasSwordBones = Draft.AttachmentBoneIds?.Contains("_weapon_hilt", StringComparer.Ordinal) == true &&
            Draft.AttachmentBoneIds.Contains("_weapon_tip", StringComparer.Ordinal);
        var finisher = hasSwordBones
            ? new HitboxSource(AuthoringHitboxShape.Capsule, 0.5f,
                0f, 0f, 0f, 0f, 0f, 0f, "_weapon_hilt", "_weapon_tip",
                6f, 45f, 5f, 80f, 8, 9, true, 0)
            : new HitboxSource(AuthoringHitboxShape.Sphere, 0.5f,
                0f, 0f, 0f, 0f, 0f, 0f, "bone.hips", null,
                6f, 45f, 5f, 80f, 8, 9, true, 0);
        var parameters = new ChargedDirectionalDashCapabilityParameters(
            60, 20, 45, 1.2f, 6.4f, 10.5f, 4, 31, 24, 9f, 12f, traversal, finisher);
        int minimumStageTicks = Math.Max(
            (int)Math.Ceiling(parameters.MaxDistance / (parameters.DashSpeed / 60d)) + parameters.RecoveryTicks,
            parameters.FinisherSeekTick + parameters.FinisherLeadTicks + parameters.RecoveryTicks);
        var slots = Draft.Slots.ToArray();
        slots[slotIndex] = sourceSlot with
        {
            Behavior = AuthoringAbilityBehavior.DirectionalDash,
            AimMode = AuthoringAimMode.GroundVector,
            AimMovement = AuthoringAimMovementMode.Mobile,
            AllowSlideCarry = false,
            PreserveMomentumOnStart = false,
            Timeline = new CharacterTimelineSource(new[]
            {
                stage with { DurationTicks = (ushort)Math.Max(stage.DurationTicks, minimumStageTicks), IasaTicks = 0 },
            }),
        };
        var source = new CharacterPackageSource(Manifest,
            Draft with { CapabilityRequirements = requirements, Slots = slots });
        return ApplyEdit(CharacterPackageSourceCodec.AddOperation(source, slotIndex, stageIndex,
            new StartCapabilityOperationSource(0, AuthoringUnit.Ticks,
                CharacterPackageCompiler.ChargedDirectionalDashCapabilityId,
                CharacterPackageCompiler.ChargedDirectionalDashCapabilityVersion, parameters)));
    }



    public bool ReplaceStage(int slotIndex, int stageIndex, CharacterStageSource stage)
    {
        var result = CharacterPackageSourceCodec.ReplaceStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, stage);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    public bool ReplaceOperation(int slotIndex, int stageIndex, int operationIndex, CharacterTimelineOperationSource operation)
    {
        var result = CharacterPackageSourceCodec.ReplaceOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, operation);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    public bool AddStage(int slotIndex, CharacterStageSource stage)
        => ApplyEdit(CharacterPackageSourceCodec.AddStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stage));
    public bool RemoveStage(int slotIndex, int stageIndex)
        => ApplyEdit(CharacterPackageSourceCodec.RemoveStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex));

    public bool AddOperation(int slotIndex, int stageIndex, CharacterTimelineOperationSource operation)
        => ApplyEdit(CharacterPackageSourceCodec.AddOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operation));
    public bool RemoveOperation(int slotIndex, int stageIndex, int operationIndex)
        => ApplyEdit(CharacterPackageSourceCodec.RemoveOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex));
    public bool MoveOperation(int slotIndex, int stageIndex, int operationIndex, int destinationIndex)
        => ApplyEdit(CharacterPackageSourceCodec.MoveOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, destinationIndex));
    private bool ApplyEdit(CharacterSourceEditResult result)
    {
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    /// <summary>Prepare the current workspace for testing without saving, cooking, or adding Undo entries.</summary>
    public bool PrepareScenarioPreview()
    {
        if (!HasPackage || Preview?.IsAvailable != true) return false;
        if (LiveDraftPackage == null && !LiveDraftInvalid)
            RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
        return !LiveDraftInvalid && LiveDraftPackage != null;
    }

    private void RefreshLiveDraft(CharacterPackageSource source, IReadOnlyList<CharacterDiagnostic> sourceDiagnostics)
    {
        AbilityLab.Instance?.SetSourceDocument(source);
        var compiled = CharacterPackageCompiler.Compile(source, CharacterPackageAuthoringService.ProfileFor(PackageId));
        var diagnostics = new List<CharacterDiagnostic>(sourceDiagnostics ?? Array.Empty<CharacterDiagnostic>());
        diagnostics.AddRange(compiled.Diagnostics);
        if (!compiled.HasErrors && compiled.CookedPackage != null)
        {
            LiveDraftPackage = compiled.CookedPackage;
            LiveDraftInvalid = false;
            SetDiagnostics(diagnostics, !IsDirty && _persistedSourceCurrent ? "Valid" : "Stale");
            return;
        }

        LiveDraftPackage = null;
        LiveDraftInvalid = true;
        if (diagnostics.Count == 0)
            diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "preview.compile.failed", "character", "Live package preview compilation failed."));
        AbilityLab.Instance?.MarkPackageDraftInvalid();
        SetDiagnostics(diagnostics, "Failed");
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Undo()
    {
        if (_undo.Count == 0) return;
        var snapshot = _undo.Pop();
        _redo.Push(CaptureSnapshot(snapshot.TrailEntryIndex));
        RestoreSnapshot(snapshot, true);
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        var snapshot = _redo.Pop();
        _undo.Push(CaptureSnapshot(snapshot.TrailEntryIndex));
        RestoreSnapshot(snapshot, true);
    }

    public void SetDraft(CharacterAuthoringDocument draft)
    {
        PushUndo();
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        IsDirty = true;
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }

    public void SetManifest(PackageManifestSource manifest)
    {
        PushUndo();
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        IsDirty = true;
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }
    public void RevertDraft() => ReloadPackage();

    private void RestoreSnapshot(WorkspaceSnapshot snapshot, bool recordUnityUndo)
    {
        if (snapshot.WeaponConfig?.Entries != null && snapshot.TrailEntryIndex >= 0
            && snapshot.TrailEntryIndex < snapshot.WeaponConfig.Entries.Length)
        {
            var config = snapshot.WeaponConfig;
            if (recordUnityUndo) UnityEditor.Undo.RecordObject(config, "Ability Lab weapon trail undo");
            config.Entries[snapshot.TrailEntryIndex].TrailBladeWidth = snapshot.TrailWidth;
            config.Entries[snapshot.TrailEntryIndex].TrailStylePrefab = snapshot.TrailStyle;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            IsDirty = true;
            Status = "Stale";
            SceneView.RepaintAll();
            return;
        }
        if (recordUnityUndo && Catalog != null)
            UnityEditor.Undo.RecordObject(Catalog, "Ability Lab workspace undo");
        Manifest = snapshot.Source.Manifest;
        Draft = snapshot.Source.Character;
        if (Catalog != null && snapshot.Catalog != null)
        {
            Catalog.Rig = snapshot.Catalog.Rig;
            Catalog.Bindings = CloneBindings(snapshot.Catalog.Bindings);
            Catalog.Presentations = ClonePresentations(snapshot.Catalog.Presentations);
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
        }
        IsDirty = true;
        RefreshLiveDraft(snapshot.Source, Array.Empty<CharacterDiagnostic>());
        SceneView.RepaintAll();
    }

    private WorkspaceSnapshot CaptureSnapshot(int trailEntryIndex = -1)
    {
        var config = trailEntryIndex >= 0 ? Catalog?.WeaponConfig : null;
        return new WorkspaceSnapshot(new CharacterPackageSource(Manifest, Draft), Catalog == null
            ? null
            : new CatalogSnapshot(Catalog.Rig, CloneBindings(Catalog.Bindings), ClonePresentations(Catalog.Presentations)),
            config, trailEntryIndex, config != null ? config.Entries[trailEntryIndex].TrailBladeWidth : 0f,
            config != null ? config.Entries[trailEntryIndex].TrailStylePrefab : null);
    }

    private void PushUndo() => PushUndo(CaptureSnapshot());

    private void PushUndo(WorkspaceSnapshot snapshot)
    {
        _undo.Push(snapshot);
        if (_undo.Count > MaxUndoDepth) _undo.Pop();
        _redo.Clear();
    }

    private static CharacterAssetCatalog.AnimationBinding[] CloneBindings(IEnumerable<CharacterAssetCatalog.AnimationBinding> bindings)
        => (bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Select(x => x == null ? null : new CharacterAssetCatalog.AnimationBinding
            {
                SemanticId = x.SemanticId,
                PoseTrackId = x.PoseTrackId,
                Clip = x.Clip,
                Extrapolation = x.Extrapolation,
            }).ToArray();
    private static CharacterAssetCatalog.PresentationBinding[] ClonePresentations(IEnumerable<CharacterAssetCatalog.PresentationBinding> bindings)
        => (bindings ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .Select(x => x == null ? null : new CharacterAssetCatalog.PresentationBinding
            {
                SemanticId = x.SemanticId,
                Prefab = x.Prefab,
            }).ToArray();


    private static bool SamePackage(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
        try
        {
            string root = UnityCharacterAssetCooker.ProjectRoot();
            string a = Path.GetFullPath(Path.IsPathRooted(left) ? left : Path.Combine(root, left));
            string b = Path.GetFullPath(Path.IsPathRooted(right) ? right : Path.Combine(root, right));
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private sealed class AttachmentDraftState
    {
        public AttachmentDraftState(WeaponAttachConfig config, int entryIndex, AttachmentValues values)
        {
            Config = config;
            EntryIndex = entryIndex;
            Value = values;
            Saved = values;
        }

        public WeaponAttachConfig Config { get; }
        public int EntryIndex { get; }
        public AttachmentValues Value { get; set; }
        public AttachmentValues Saved { get; set; }
    }

    private struct AttachmentValues : IEquatable<AttachmentValues>
    {
        public Vector3 PositionOffset;
        public Vector3 RotationOffset;
        public bool HasFirePhaseOverride;
        public Vector3 FirePositionOffset;
        public Vector3 FireRotationOffset;
        public bool HasFiniteOffsets =>
            Finite(PositionOffset) && Finite(RotationOffset) && Finite(FirePositionOffset) && Finite(FireRotationOffset);

        public bool Equals(AttachmentValues other) =>
            PositionOffset.Equals(other.PositionOffset) && RotationOffset.Equals(other.RotationOffset) &&
            HasFirePhaseOverride == other.HasFirePhaseOverride &&
            FirePositionOffset.Equals(other.FirePositionOffset) && FireRotationOffset.Equals(other.FireRotationOffset);
        public override bool Equals(object obj) => obj is AttachmentValues other && Equals(other);
        public override int GetHashCode() => PositionOffset.GetHashCode();
        public static bool operator ==(AttachmentValues left, AttachmentValues right) => left.Equals(right);
        public static bool operator !=(AttachmentValues left, AttachmentValues right) => !left.Equals(right);
        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private sealed class WorkspaceSnapshot
    {
        public WorkspaceSnapshot(CharacterPackageSource source, CatalogSnapshot catalog,
            WeaponAttachConfig weaponConfig, int trailEntryIndex, float trailWidth, GameObject trailStyle)
        {
            Source = source;
            Catalog = catalog;
            WeaponConfig = weaponConfig;
            TrailEntryIndex = trailEntryIndex;
            TrailWidth = trailWidth;
            TrailStyle = trailStyle;
        }

        public CharacterPackageSource Source { get; }
        public CatalogSnapshot Catalog { get; }
        public WeaponAttachConfig WeaponConfig { get; }
        public int TrailEntryIndex { get; }
        public float TrailWidth { get; }
        public GameObject TrailStyle { get; }
    }

    private sealed class CatalogSnapshot
    {
        public CatalogSnapshot(
            GameObject rig,
            CharacterAssetCatalog.AnimationBinding[] bindings,
            CharacterAssetCatalog.PresentationBinding[] presentations)
        {
            Rig = rig;
            Bindings = bindings;
            Presentations = presentations;
        }

        public GameObject Rig { get; }
        public CharacterAssetCatalog.AnimationBinding[] Bindings { get; }
        public CharacterAssetCatalog.PresentationBinding[] Presentations { get; }
    }

    private bool Fail(string code, string path, string message) => SetFailure(code, path, message);
    private bool SetFailure(string code, string path, string message)
    {
        SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message) }, "Failed");
        return false;
    }
    private void SetDiagnostics(IEnumerable<CharacterDiagnostic> diagnostics, string status)
    {
        var values = (diagnostics ?? Array.Empty<CharacterDiagnostic>()).ToArray();
        _diagnostics.Clear();
        _diagnostics.AddRange(values);
        Status = status;
        StatusChanged?.Invoke();
    }
    private void SetDiagnosticsWithoutNotify(IEnumerable<CharacterDiagnostic> diagnostics, string status)
    {
        var values = (diagnostics ?? Array.Empty<CharacterDiagnostic>()).ToArray();
        _diagnostics.Clear();
        _diagnostics.AddRange(values);
        Status = status;
    }
    private string ToFull(string path) => Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path);
    private string ComputeDiskHash() => HashFiles(File.ReadAllBytes(ToFull(PackageRoot + "/package.json")), File.ReadAllBytes(ToFull(PackageRoot + "/character.json")));
    private string ComputeCatalogFingerprint() => CharacterPackageAuthoringService.ComputeCatalogFingerprint(Catalog);
    internal static string HashFiles(byte[] package, byte[] character)
    {
        using var hash = SHA256.Create();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(package.Length);
        writer.Write(package);
        writer.Write(character.Length);
        writer.Write(character);
        writer.Flush();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
    private static void ReplaceSources(string packagePath, string characterPath, byte[] package, byte[] character)
    {
        string packageTemp = packagePath + ".tmp-" + Guid.NewGuid().ToString("N");
        string characterTemp = characterPath + ".tmp-" + Guid.NewGuid().ToString("N");
        byte[] priorPackage = File.Exists(packagePath) ? File.ReadAllBytes(packagePath) : null;
        byte[] priorCharacter = File.Exists(characterPath) ? File.ReadAllBytes(characterPath) : null;
        File.WriteAllBytes(packageTemp, package); File.WriteAllBytes(characterTemp, character);
        try
        {
            if (File.Exists(packagePath)) File.Replace(packageTemp, packagePath, null, true); else File.Move(packageTemp, packagePath);
            if (File.Exists(characterPath)) File.Replace(characterTemp, characterPath, null, true); else File.Move(characterTemp, characterPath);
        }
        catch
        {
            if (priorPackage == null) { if (File.Exists(packagePath)) File.Delete(packagePath); } else File.WriteAllBytes(packagePath, priorPackage);
            if (priorCharacter == null) { if (File.Exists(characterPath)) File.Delete(characterPath); } else File.WriteAllBytes(characterPath, priorCharacter);
            throw;
        }
        finally
        {
            if (File.Exists(packageTemp)) File.Delete(packageTemp);
            if (File.Exists(characterTemp)) File.Delete(characterTemp);
        }
    }
}
