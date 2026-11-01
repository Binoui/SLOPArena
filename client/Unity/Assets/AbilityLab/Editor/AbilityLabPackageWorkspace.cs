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

public sealed partial class AbilityLabPackageWorkspace
{
    internal AbilityLab? PreviewLab { get; private set; }

    internal void BindPreviewLab(AbilityLab? lab)
    {
        PreviewLab = lab;
        // A disposed rig releases its catalog snapshot. Recreate only presentation
        // prerequisites when returning from Play Mode, retaining the draft and Undo.
        if (lab != null && HasPackage)
            RefreshPersistedStatus(true);
        if (lab != null && HasPackage)
        {
            lab.SetForm(SelectedForm);
            lab.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
            lab.SetPresentationBindings(Catalog.Presentations);
        }
    }

    public byte SelectedForm { get; private set; }
    public IReadOnlyList<CharacterSlotSource> SelectedSlots => SelectedForm == 1 && Draft?.AlternateForm != null
        ? Draft.AlternateForm.Slots : Draft.Slots;
    public IReadOnlyList<CharacterAliasSource> SelectedAliases => SelectedForm == 1 && Draft?.AlternateForm != null
        ? Draft.AlternateForm.Aliases : Draft.Aliases;

    public bool SelectForm(byte form)
    {
        if (!HasPackage || form > 1 || form == 1 && Draft.AlternateForm == null) return false;
        SelectedForm = form;
        PreviewLab?.SetForm(form);
        return true;
    }

    private CharacterPackageSource GridSource()
        => new(Manifest, Draft with { Slots = SelectedSlots, Aliases = SelectedAliases });

    private CharacterSourceEditResult CompleteGridEdit(CharacterSourceEditResult result)
    {
        if (SelectedForm == 0 || !result.IsValid || result.Source == null) return result;
        var edited = result.Source.Character;
        return new CharacterSourceEditResult(new CharacterPackageSource(result.Source.Manifest, edited with
        {
            Slots = Draft.Slots, Aliases = Draft.Aliases,
            AlternateForm = Draft.AlternateForm with { Slots = edited.Slots, Aliases = edited.Aliases },
        }), result.Diagnostics);
    }

    private bool ApplyGridEdit(CharacterSourceEditResult result) => ApplyEdit(CompleteGridEdit(result));

    public string PackageRoot { get; private set; } = "";
    public PackageManifestSource Manifest { get; private set; } = null!;
    public CharacterAuthoringDocument Draft { get; private set; } = null!;
    public string LoadedDiskHash { get; private set; } = "";
    private string LoadedCatalogFingerprint { get; set; } = "";
    public bool IsDirty { get; private set; }
    public bool HasUnsavedChanges => IsDirty || AttachmentDraftDirty;
    public bool PreviewPrerequisitesStale { get; private set; }
    private string _savedManifestJson = "";
    private string _savedCharacterJson = "";
    private string _previewDependencyHash = "";
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
        if (HasUnsavedChanges)
            return Fail("workspace.dirty", PackageRoot, "Keep a local recovery or discard unsaved changes before creating another package.");
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
        if (HasPackage && (packageRoot == PackageId || SamePackage(PackageRoot, packageRoot))) return true;
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
        if (inspection != null && HasPackage && SamePackage(PackageRoot, inspection.SourcePath)) return true;
        return LoadPackage(inspection);
    }

    private bool LoadPackage(CharacterPackageInspectionResult inspection)
    {
        if (inspection == null || !inspection.Success || inspection.Source == null || inspection.Catalog == null)
            return false;
        if (AttachmentDraftDirty)
            return SamePackage(PackageRoot, inspection.SourcePath)
                ? true
                : SetAttachmentDraftError("Save or revert the attachment draft before opening another package.");

        _attachmentDraft = null;
        _semanticRenames.Clear();
        _semanticRenameBaseline = "";
        _recoveredPresentationProfileJson = "";
        _attachmentUndo.Clear();
        _attachmentRedo.Clear();
        AttachmentDraftError = "";
        LiveDraftPackage = null;
        LiveDraftInvalid = false;
        PackageRoot = inspection.SourcePath;
        Manifest = inspection.Source.Manifest;
        Draft = inspection.Source.Character;
        SelectedForm = 0;
        PreviewLab?.SetForm(0);
        Catalog = inspection.Catalog;
        _undo.Clear();
        _redo.Clear();
        PreviewLab?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
        PreviewLab?.SetPresentationBindings(Catalog.Presentations);
        LoadedDiskHash = ComputeDiskHash();
        LoadedCatalogFingerprint = ComputeCatalogFingerprint();
        RememberSavedSource();
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
        PreviewPrerequisitesStale = false;
        var candidate = AbilityLabPackagePreviewLoader.Load(PackageId);
        ReplacePreview(candidate);
        if (!candidate.IsAvailable)
            SetDiagnostics(_diagnostics.Concat(candidate.Diagnostics).ToArray(), Status);
        return true;
    }

    public bool ReloadPackage() => HasPackage && LoadPackage(
        new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(PackageRoot));

    /// <summary>Refresh prerequisites without replacing the source draft or its Undo history.</summary>
    public bool RefreshPersistedStatus(bool force = false)
    {
        if (!HasPackage || Status == "Cooking") return false;
        string stamp = PersistedStatusStamp();
        if (!force && stamp == _persistedStatusStamp) return false;
        _persistedStatusStamp = stamp;
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(PackageRoot);
        bool sourceMatches = SourcesMatchBaseline();
        _persistedSourceCurrent = sourceMatches && inspection.Status == "valid";
        var diagnostics = LiveDraftInvalid ? _diagnostics.ToList() : new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Status == "stale")
            diagnostics.AddRange((inspection.StaleReasons ?? Array.Empty<CharacterPackageStaleReason>())
                .Select(reason => new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, reason.Code, reason.Path, reason.Message)));
        if (_attachmentDraft != null && !AttachmentTargetMatches(_attachmentDraft))
            AttachmentDraftError = "Attachment source changed externally. Draft placement is retained; saving is blocked.";
        if (!sourceMatches)
            diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "workspace.external.changed", PackageRoot,
                "Source or bindings changed outside this workspace. The draft is retained. Keep a local recovery before reloading."));

        CookedSourceHash = inspection.CookedSourceHash ?? "";
        CookedContentHash = inspection.CookedContentHash ?? "";
        PackageHash = inspection.PackageHash ?? "";
        bool prerequisitesChanged = !PreviewMatches(Preview, inspection) || _previewDependencyHash != PreviewDependencyHash();
        bool needsRefresh = prerequisitesChanged || Preview?.AnimationCatalog == null;
        if (!sourceMatches || (_attachmentDraft != null && !AttachmentTargetMatches(_attachmentDraft)) ||
            (prerequisitesChanged && (HasUnsavedChanges || !_persistedSourceCurrent)))
        {
            PreviewPrerequisitesStale = true;
        }
        else if (needsRefresh || PreviewPrerequisitesStale)
        {
            var candidate = AbilityLabPackagePreviewLoader.Load(PackageId);
            if (PreviewMatches(candidate, inspection) && candidate.BakedPoses != null &&
                candidate.AnimationCatalog != null && candidate.Rig != null)
            {
                ReplacePreview(candidate);
                LiveDraftPackage = null;
            }
            else
            {
                ReleasePreview(candidate);
                PreviewPrerequisitesStale = true;
                diagnostics.AddRange(candidate.Diagnostics);
            }
        }
        if (PreviewPrerequisitesStale)
        {
            diagnostics.Add(StalePreviewDiagnostic());
            PreviewLab?.MarkPackageDraftInvalid();
        }
        string status = LiveDraftInvalid ? "Failed" : _persistedSourceCurrent && !IsDirty && !PreviewPrerequisitesStale ? "Valid" : "Stale";
        SetDiagnosticsWithoutNotify(diagnostics, status);
        return true;
    }

    private CharacterDiagnostic StalePreviewDiagnostic() => new(CharacterDiagnosticSeverity.Warning,
        "preview.prerequisites.stale", PackageRoot,
        "Preview poses or bindings are stale. Keep a local recovery or discard the draft, then reload or Save + Cook before running scenarios.");

    private bool SourcesMatchBaseline()
    {
        try { return Catalog != null && ComputeDiskHash() == LoadedDiskHash && ComputeCatalogFingerprint() == LoadedCatalogFingerprint; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool PreviewMatches(AbilityLabPackagePreviewResult preview, CharacterPackageInspectionResult inspection)
        => preview?.IsAvailable == true && preview.Identity != null &&
           preview.Identity.PackageId == inspection.PackageId && preview.Identity.PackageHash == inspection.PackageHash &&
           preview.Identity.SourceHash == inspection.CookedSourceHash && preview.Identity.CookedContentHash == inspection.CookedContentHash;

    private string PreviewDependencyHash() => AssetDatabase.GetAssetDependencyHash(PackageRoot + "/CharacterAssetCatalog.asset").ToString();

    private void ReplacePreview(AbilityLabPackagePreviewResult candidate)
    {
        var previous = Preview;
        Preview = candidate;
        _previewDependencyHash = PreviewDependencyHash();
        PreviewPrerequisitesStale = !candidate.IsAvailable;
        if (PreviewLab != null)
        {
            var cursor = PreviewLab.CaptureTimelineCursor();
            bool hadScenario = PreviewLab.IsScenarioPreview;
            string slotId = PreviewLab.SelectedSlotId;
            int stage = PreviewLab.StageIndex;
            ushort tick = PreviewLab.Tick;
            if (candidate.IsAvailable) PreviewLab.ApplyPackagePreview(candidate);
            else PreviewLab.ApplyPreviewUnavailable(candidate.Diagnostics);
            if (!hadScenario) PreviewLab.RestoreTimelineCursor(cursor);
            else if (CanonicalSlotProjection.TryGet(slotId, out var address))
            {
                PreviewLab.SetSlot(address);
                PreviewLab.SetStage(stage);
                PreviewLab.SetTick(tick);
                PreviewLab.Playing = false;
            }
        }
        if (!ReferenceEquals(previous?.AnimationCatalog, candidate.AnimationCatalog)) ReleasePreview(previous);
    }

    internal void ReleasePreview()
    {
        if (PreviewLab == null) ReleasePreview(Preview);
    }

    private static void ReleasePreview(AbilityLabPackagePreviewResult preview)
    {
        if (preview?.AnimationCatalog != null && !EditorUtility.IsPersistent(preview.AnimationCatalog))
            UnityEngine.Object.DestroyImmediate(preview.AnimationCatalog);
    }

    private string PersistedStatusStamp()
    {
        string project = UnityCharacterAssetCooker.ProjectRoot();
        string[] paths =
        {
            ToFull(PackageRoot + "/package.json"), ToFull(PackageRoot + "/character.json"),
            ToFull(PackageRoot + "/CharacterAssetCatalog.asset"),
            Path.Combine(project, CharacterCookOutput.For(PackageId).IntermediateDirectory, "cook-status.json"),
            Path.GetFullPath(Path.Combine(project, "../../content-cooked", PackageId, CharacterPackageAssembler.ManifestPath)),
        };
        return string.Join("|", paths.Select(path => File.Exists(path)
            ? $"{new FileInfo(path).LastWriteTimeUtc.Ticks}:{new FileInfo(path).Length}" : "missing"))
            + "|" + AssetDatabase.GetAssetDependencyHash(PackageRoot + "/CharacterAssetCatalog.asset")
            + "|" + HasUnsavedChanges + "|" + (_attachmentDraft == null || AttachmentTargetMatches(_attachmentDraft));
    }
    public bool SavePackage()
    {
        if (!HasPackage || !File.Exists(ToFull(PackageRoot + "/package.json")) ||
            !File.Exists(ToFull(PackageRoot + "/character.json")) || ComputeDiskHash() != LoadedDiskHash)
            return Fail("workspace.conflict", PackageRoot, "Package source changed externally; reload before saving.");
        if (Catalog == null || ComputeCatalogFingerprint() != LoadedCatalogFingerprint)
            return Fail("workspace.conflict", PackageRoot + "/CharacterAssetCatalog.asset", "Character asset catalog changed externally; reload before saving.");
        if (_attachmentDraft != null && !AttachmentTargetMatches(_attachmentDraft))
            return SetAttachmentDraftError("Attachment source changed externally; recovered placement is retained and saving is blocked.");
        if (AttachmentDraftDirty && !SaveAttachmentDraft()) return false;
        string packagePath = ToFull(PackageRoot + "/package.json"); string characterPath = ToFull(PackageRoot + "/character.json");
        try
        {
            string packageJson = CharacterPackageSourceCodec.SerializeManifest(Manifest);
            string characterJson = CharacterPackageSourceCodec.SerializeCharacter(Draft);
            ReplaceSources(packagePath, characterPath, Encoding.UTF8.GetBytes(packageJson), Encoding.UTF8.GetBytes(characterJson));
            LoadedDiskHash = ComputeDiskHash();
            RememberSavedSource();
            RebaseSavedSemanticHistory(_undo);
            RebaseSavedSemanticHistory(_redo);
            _semanticRenames.Clear();
            _semanticRenameBaseline = "";
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
            ReplacePreview(candidatePreview);
            LiveDraftPackage = null;
            LiveDraftInvalid = false;
            SetDiagnostics(loadedAssembly.Diagnostics.Concat(candidatePreview.Diagnostics), "Valid");
            PreviewLab?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
            PreviewLab?.SetPresentationBindings(Catalog.Presentations);
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
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
            if (_semanticRenames.Count == 0) _semanticRenameBaseline = EditorJsonUtility.ToJson(Catalog);
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
            AssetDatabase.SaveAssetIfDirty(Catalog);
            _semanticRenames.Add(new SemanticRename { Before = oldId, After = newId });
            PushUndo(prior);
            PreviewLab?.SetSourceDocument(result.Source);
            PreviewLab?.SetPresentationBindings(Catalog.Presentations);
            UpdateSourceDirty();
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
        if (AttachmentDraftDirty && (AttachmentDraftEntryIndex != entryIndex || _attachmentDraft.Config != Catalog?.WeaponConfig))
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
        if (_attachmentDraft == null) return SetAttachmentDraftError("No attachment draft is selected.");
        var draft = _attachmentDraft;
        if (draft.Config == null || draft.Config.Entries == null || draft.EntryIndex < 0
            || draft.EntryIndex >= draft.Config.Entries.Length || draft.Config.Entries[draft.EntryIndex] == null)
            return SetAttachmentDraftError("The attachment source entry no longer exists.");
        var entry = draft.Config.Entries[draft.EntryIndex];
        if (!AttachmentTargetMatches(draft) || CaptureAttachmentValues(entry) != draft.Saved)
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
        RefreshOwnedAttachmentFingerprint(draft.ConfigFingerprint);
        draft.Saved = draft.Value;
        UpdateSourceDirty();
        Status = "Stale";
        AttachmentDraftError = "";
        StatusChanged?.Invoke();
        SceneView.RepaintAll();
        return true;
    }

    public WeaponAttachConfig CreateAttachmentPreviewConfig()
    {
        if (_attachmentDraft != null && !AttachmentTargetMatches(_attachmentDraft))
        {
            SetAttachmentDraftError("Attachment source changed externally. Draft placement is retained; preview and saving are blocked.");
            return null;
        }
        var source = _attachmentDraft?.Config ?? Catalog?.WeaponConfig;
        if (source == null) return null;
        var preview = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        preview.hideFlags = HideFlags.HideAndDontSave;
        preview.Entries = (source.Entries ?? Array.Empty<WeaponEntry>())
            .Select(CloneWeaponEntry).ToArray();
        if (_attachmentDraft != null && AttachmentTargetMatches(_attachmentDraft) && _attachmentDraft.EntryIndex >= 0
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
        ShowFromTicks = entry.ShowFromTicks,
        HiddenBySlot = entry.HiddenBySlot,
        HiddenBySlotFromTicks = entry.HiddenBySlotFromTicks,
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
        var config = Catalog?.WeaponConfig;
        if (!HasPackage || config?.Entries == null || entryIndex < 0
            || entryIndex >= config.Entries.Length || config.Entries[entryIndex]?.TrailStylePrefab == null)
            return false;
        width = Mathf.Clamp(width, 0.01f, 1f);
        var entry = config.Entries[entryIndex];
        if (entry.TrailBladeWidth == width) return false;
        var prior = CaptureSnapshot(entryIndex);
        string attachmentBefore = AttachmentFingerprint(config);
        UnityEditor.Undo.RecordObject(config, "Change blade trail width");
        entry.TrailBladeWidth = width;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        PushUndo(prior);
        RefreshOwnedAttachmentFingerprint(attachmentBefore);
        _persistedSourceCurrent = false;
        UpdateSourceDirty();
        Status = "Stale";
        return true;
    }

    public bool ReplaceTrailStyle(int entryIndex, GameObject style)
    {
        if (HasPackage && !CanWritePersistedAssets()) return false;
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
        string attachmentBefore = AttachmentFingerprint(config);
        UnityEditor.Undo.RecordObject(config, "Change blade trail style");
        entry.TrailStylePrefab = style;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        PushUndo(prior);
        RefreshOwnedAttachmentFingerprint(attachmentBefore);
        _persistedSourceCurrent = false;
        UpdateSourceDirty();
        Status = "Stale";
        return true;
    }

    public bool ReplaceCatalogRig(GameObject rig)
    {
        if (HasPackage && !CanWritePersistedAssets()) return false;
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Replace character catalog rig");
            Catalog.Rig = rig;
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssetIfDirty(Catalog);
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        CharacterPackageAuthoringService service = new(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageBindingResult result = clip == null
            ? service.Unbind(PackageRoot, semanticId)
            : service.Bind(PackageRoot, semanticId, clip, extrapolation);
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
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
            AssetDatabase.SaveAssetIfDirty(Catalog);
            PushUndo(prior);
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            UpdateSourceDirty();
            PreviewLab?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
            PreviewLab?.SetPresentationBindings(Catalog.Presentations);
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
        if (HasPackage && !CanWritePersistedAssets()) return false;
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
            AssetDatabase.SaveAssetIfDirty(Catalog);
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
        UpdateSourceDirty();
        _persistedSourceCurrent = false;
        SetDiagnosticsWithoutNotify(_diagnostics, "Stale");
        PreviewLab?.SetPresentationBindings(Catalog?.Presentations);
    }
    public bool TryResolveCanonicalSlot(string canonicalSlotId, out int explicitSourceSlotIndex, out CharacterSlotSource sourceSlot)
    {
        explicitSourceSlotIndex = -1;
        sourceSlot = null!;
        if (!HasPackage || !CanonicalSlotProjection.TryGet(canonicalSlotId, out _)) return false;

        var explicitById = SelectedSlots
            .Select((slot, index) => (slot, index))
            .Where(item => item.slot != null)
            .ToDictionary(item => item.slot.Id, item => item.index, StringComparer.Ordinal);
        var aliases = (SelectedAliases ?? Array.Empty<CharacterAliasSource>())
            .Where(alias => alias != null)
            .ToDictionary(alias => alias.From, alias => alias.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = canonicalSlotId;
        while (true)
        {
            if (!visited.Add(current)) return false;
            if (explicitById.TryGetValue(current, out explicitSourceSlotIndex))
            {
                sourceSlot = SelectedSlots[explicitSourceSlotIndex];
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
        return ApplyGridEdit(CharacterPackageSourceCodec.ReplaceOperationTick(GridSource(), slotIndex, stageIndex, operationIndex, tick));
    }

    public bool ReplaceHitboxDuration(string canonicalSlotId, int stageIndex, int operationIndex, int durationTicks)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyGridEdit(CharacterPackageSourceCodec.ReplaceHitboxDuration(GridSource(), slotIndex, stageIndex, operationIndex, durationTicks));
    }
    public bool ReplacePresentationPlacement(string canonicalSlotId, int stageIndex, int operationIndex, PresentationPlacement placement)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyGridEdit(CharacterPackageSourceCodec.ReplacePresentationPlacement(
            GridSource(), slotIndex, stageIndex, operationIndex, placement));
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
        if (stageIndex != 0 || sourceSlot.Timeline.Stages.Count != 1 ||
            sourceSlot.Timeline.Stages[0].Operations.Any(operation => operation is not EmitPresentationOperationSource))
            return Fail("edit.targeted-leap.stage", canonicalSlotId,
                "Targeted leap needs one stage with only presentation operations.");
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
            20, 4f, 20, 12, 12f, 4f, 120, recoveryTicks, recoveryTicks, hitbox);
        var slots = SelectedSlots.ToArray();
        slots[slotIndex] = sourceSlot with
        {
            Behavior = AuthoringAbilityBehavior.MeleeCombo,
            AimMode = AuthoringAimMode.None,
            AllowSlideCarry = false,
            ChargePool = null,
            Timeline = new CharacterTimelineSource(new[]
            {
                sourceSlot.Timeline.Stages[0] with { IasaTicks = 0 },
            }),
        };
        var source = new CharacterPackageSource(Manifest,
            GridSource().Character with { CapabilityRequirements = requirements, Slots = slots });
        return ApplyGridEdit(CharacterPackageSourceCodec.AddOperation(source, slotIndex, stageIndex,
            new StartCapabilityOperationSource(0, AuthoringUnit.Ticks,
                CharacterPackageCompiler.TargetedLeapCapabilityId,
                CharacterPackageCompiler.TargetedLeapCapabilityVersion, parameters)));
    }

    public bool AddDashStrike(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        if (stageIndex != 0 || sourceSlot.Timeline.Stages.Count != 1)
            return Fail("edit.dash-strike.stage", canonicalSlotId, "Dash strike requires one stage starting at tick zero.");
        var stage = sourceSlot.Timeline.Stages[stageIndex];
        if (stage.Operations.Any(operation => operation is SpawnHitboxOperationSource
            or ForwardLungeOperationSource or SetVelocityOperationSource or StartCapabilityOperationSource
            or StartupAimCorrectionOperationSource))
            return Fail("edit.dash-strike.operation-conflict", canonicalSlotId,
                "Remove authored hitboxes, motion, startup correction, and other lifecycles before adding this primitive.");
        if (sourceSlot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(operation => operation.CapabilityId == CharacterPackageCompiler.DashStrikeCapabilityId))
            return Fail("edit.operation.duplicate", canonicalSlotId, "This move already has a dash strike.");

        var requirements = Draft.CapabilityRequirements.ToList();
        if (requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.DashStrikeCapabilityId &&
            item.CapabilityVersion != CharacterPackageCompiler.DashStrikeCapabilityVersion))
            return Fail("edit.capability.version", canonicalSlotId, "Dash strike requires a different capability version.");
        if (!requirements.Any(item => item.CapabilityId == CharacterPackageCompiler.DashStrikeCapabilityId))
            requirements.Add(new CapabilityRequirementSource(
                CharacterPackageCompiler.DashStrikeCapabilityId,
                CharacterPackageCompiler.DashStrikeCapabilityVersion));

        var light = new HitboxSource(AuthoringHitboxShape.Sphere, .6f,
            0f, 0f, 0f, 0f, 0f, 0f, null, null, 3f, 0f, 0f, 0f, 12, 1, true, 0,
            FixedHitstunTicks: 38);
        var tip = new HitboxSource(AuthoringHitboxShape.Capsule, .5f,
            0f, .15f, .5f, 0f, 0f, 1.7f, null, null, 13f, 35f, 10f, 42f, 12, 4, true, 0);
        var parameters = new DashStrikeCapabilityParameters(3.5f, 28f, 33, 16, light, tip,
            Draft.PresentationIds.FirstOrDefault() ?? "");
        int minimumStageTicks = 60;
        var slots = SelectedSlots.ToArray();
        slots[slotIndex] = sourceSlot with
        {
            Behavior = AuthoringAbilityBehavior.MeleeCombo,
            AimMode = AuthoringAimMode.None,
            AimMovement = AuthoringAimMovementMode.Fixed,
            AllowSlideCarry = false,
            PreserveMomentumOnStart = false,
            Timeline = new CharacterTimelineSource(new[]
            {
                stage with { DurationTicks = (ushort)Math.Max(stage.DurationTicks, minimumStageTicks), IasaTicks = 0 },
            }),
        };
        var source = new CharacterPackageSource(Manifest,
            GridSource().Character with { CapabilityRequirements = requirements, Slots = slots });
        return ApplyGridEdit(CharacterPackageSourceCodec.AddOperation(source, slotIndex, stageIndex,
            new StartCapabilityOperationSource(0, AuthoringUnit.Ticks,
                CharacterPackageCompiler.DashStrikeCapabilityId,
                CharacterPackageCompiler.DashStrikeCapabilityVersion, parameters)));
    }



    public bool ReplaceStage(int slotIndex, int stageIndex, CharacterStageSource stage)
    {
        var result = CompleteGridEdit(CharacterPackageSourceCodec.ReplaceStage(GridSource(), slotIndex, stageIndex, stage));
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        UpdateSourceDirty();
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    public bool ReplaceOperation(int slotIndex, int stageIndex, int operationIndex, CharacterTimelineOperationSource operation)
    {
        var result = CompleteGridEdit(CharacterPackageSourceCodec.ReplaceOperation(GridSource(), slotIndex, stageIndex, operationIndex, operation));
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        UpdateSourceDirty();
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    public bool AddStage(int slotIndex, CharacterStageSource stage)
        => ApplyGridEdit(CharacterPackageSourceCodec.AddStage(GridSource(), slotIndex, stage));
    public bool RemoveStage(int slotIndex, int stageIndex)
        => ApplyGridEdit(CharacterPackageSourceCodec.RemoveStage(GridSource(), slotIndex, stageIndex));

    public bool AddOperation(int slotIndex, int stageIndex, CharacterTimelineOperationSource operation)
        => ApplyGridEdit(CharacterPackageSourceCodec.AddOperation(GridSource(), slotIndex, stageIndex, operation));
    public bool RemoveOperation(int slotIndex, int stageIndex, int operationIndex)
        => ApplyGridEdit(CharacterPackageSourceCodec.RemoveOperation(GridSource(), slotIndex, stageIndex, operationIndex));
    public bool MoveOperation(int slotIndex, int stageIndex, int operationIndex, int destinationIndex)
        => ApplyGridEdit(CharacterPackageSourceCodec.MoveOperation(GridSource(), slotIndex, stageIndex, operationIndex, destinationIndex));
    private bool ApplyEdit(CharacterSourceEditResult result)
    {
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        UpdateSourceDirty();
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    /// <summary>Prepare the current workspace for testing without saving, cooking, or adding Undo entries.</summary>
    public bool PrepareScenarioPreview()
    {
        RefreshPersistedStatus();
        if (!HasPackage || Preview?.IsAvailable != true || PreviewPrerequisitesStale) return false;
        if (LiveDraftPackage == null && !LiveDraftInvalid)
            RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
        return !LiveDraftInvalid && LiveDraftPackage != null;
    }

    private void RefreshLiveDraft(CharacterPackageSource source, IReadOnlyList<CharacterDiagnostic> sourceDiagnostics)
    {
        PreviewLab?.SetSourceDocument(source);
        var compiled = CharacterPackageCompiler.Compile(source, CharacterPackageAuthoringService.ProfileFor(PackageId));
        var diagnostics = new List<CharacterDiagnostic>(sourceDiagnostics ?? Array.Empty<CharacterDiagnostic>());
        diagnostics.AddRange(compiled.Diagnostics);
        diagnostics.AddRange(_diagnostics.Where(diagnostic => diagnostic.Code == "workspace.external.changed"));
        if (PreviewPrerequisitesStale) diagnostics.Add(StalePreviewDiagnostic());
        if (!compiled.HasErrors && compiled.CookedPackage != null)
        {
            LiveDraftPackage = compiled.CookedPackage;
            LiveDraftInvalid = false;
            SetDiagnostics(diagnostics, !IsDirty && _persistedSourceCurrent && !PreviewPrerequisitesStale ? "Valid" : "Stale");
            return;
        }

        LiveDraftPackage = null;
        LiveDraftInvalid = true;
        if (diagnostics.Count == 0)
            diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "preview.compile.failed", "character", "Live package preview compilation failed."));
        PreviewLab?.MarkPackageDraftInvalid();
        SetDiagnostics(diagnostics, "Failed");
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Undo()
    {
        if (_undo.Count == 0 || !CanRestoreSnapshot(_undo.Peek())) return;
        var snapshot = _undo.Pop();
        _redo.Push(CaptureSnapshot(snapshot.TrailEntryIndex));
        RestoreSnapshot(snapshot, true);
    }
    public void Redo()
    {
        if (_redo.Count == 0 || !CanRestoreSnapshot(_redo.Peek())) return;
        var snapshot = _redo.Pop();
        _undo.Push(CaptureSnapshot(snapshot.TrailEntryIndex));
        RestoreSnapshot(snapshot, true);
    }

    public void SetDraft(CharacterAuthoringDocument draft)
    {
        PushUndo();
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        UpdateSourceDirty();
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }

    public void SetManifest(PackageManifestSource manifest)
    {
        PushUndo();
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        UpdateSourceDirty();
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }
    public void RevertDraft() => DiscardUnsavedChanges();

    private void RestoreSnapshot(WorkspaceSnapshot snapshot, bool recordUnityUndo)
    {
        if (snapshot.WeaponConfig?.Entries != null && snapshot.TrailEntryIndex >= 0
            && snapshot.TrailEntryIndex < snapshot.WeaponConfig.Entries.Length)
        {
            var config = snapshot.WeaponConfig;
            string attachmentBefore = AttachmentFingerprint(config);
            if (recordUnityUndo) UnityEditor.Undo.RecordObject(config, "Ability Lab weapon trail undo");
            config.Entries[snapshot.TrailEntryIndex].TrailBladeWidth = snapshot.TrailWidth;
            config.Entries[snapshot.TrailEntryIndex].TrailStylePrefab = snapshot.TrailStyle;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            RefreshOwnedAttachmentFingerprint(attachmentBefore);
            _persistedSourceCurrent = false;
            UpdateSourceDirty();
            Status = "Stale";
            SceneView.RepaintAll();
            return;
        }
        Manifest = snapshot.Source.Manifest;
        Draft = snapshot.Source.Character;
        _semanticRenameBaseline = snapshot.SemanticRenameBaseline;
        _semanticRenames.Clear();
        _semanticRenames.AddRange(snapshot.SemanticRenames);
        if (Catalog != null && snapshot.Catalog != null && !snapshot.Catalog.Matches(Catalog))
        {
            if (recordUnityUndo) UnityEditor.Undo.RecordObject(Catalog, "Ability Lab workspace undo");
            Catalog.Rig = snapshot.Catalog.Rig;
            Catalog.Bindings = CloneBindings(snapshot.Catalog.Bindings);
            Catalog.Presentations = ClonePresentations(snapshot.Catalog.Presentations);
            Catalog.SetPresentationProfile(snapshot.Catalog.Profile);
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssetIfDirty(Catalog);
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            PreviewLab?.SetPresentationBindings(Catalog.Presentations);
        }
        UpdateSourceDirty();
        RefreshLiveDraft(snapshot.Source, Array.Empty<CharacterDiagnostic>());
        SceneView.RepaintAll();
    }

    private WorkspaceSnapshot CaptureSnapshot(int trailEntryIndex = -1)
    {
        var config = trailEntryIndex >= 0 ? Catalog?.WeaponConfig : null;
        return new WorkspaceSnapshot(new CharacterPackageSource(Manifest, Draft), Catalog == null
            ? null
            : new CatalogSnapshot(Catalog.Rig, CloneBindings(Catalog.Bindings), ClonePresentations(Catalog.Presentations), Catalog.PresentationProfile?.DeepClone()),
            config, trailEntryIndex, config != null ? config.Entries[trailEntryIndex].TrailBladeWidth : 0f,
            config != null ? config.Entries[trailEntryIndex].TrailStylePrefab : null,
            _semanticRenameBaseline, _semanticRenames.ToArray());
    }

    private void PushUndo() => PushUndo(CaptureSnapshot());

    private void RebaseSavedSemanticHistory(Stack<WorkspaceSnapshot> history)
    {
        var retained = history.TakeWhile(snapshot => snapshot.SemanticRenames
            .Select(rename => (rename.Before, rename.After))
            .SequenceEqual(_semanticRenames.Select(rename => (rename.Before, rename.After)))).ToArray();
        history.Clear();
        foreach (var snapshot in retained.Reverse())
            history.Push(new WorkspaceSnapshot(snapshot.Source, snapshot.Catalog,
                snapshot.WeaponConfig, snapshot.TrailEntryIndex, snapshot.TrailWidth, snapshot.TrailStyle,
                "", Array.Empty<SemanticRename>(), snapshot.TrailConfigIdentity,
                snapshot.TrailEntryIdentity, snapshot.TrailConfigFingerprint));
    }

    private void PushUndo(WorkspaceSnapshot snapshot)
    {
        _undo.Push(snapshot);
        if (_undo.Count > MaxUndoDepth) TrimOldest(_undo);
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
        public AttachmentDraftState(WeaponAttachConfig config, int entryIndex, AttachmentValues values,
            string identity = null, string fingerprint = null, string entryIdentity = null)
        {
            Config = config;
            EntryIndex = entryIndex;
            Value = values;
            Saved = values;
            ConfigIdentity = identity ?? ObjectIdentity(config);
            ConfigFingerprint = fingerprint ?? AttachmentFingerprint(config);
            EntryIdentity = entryIdentity ?? AttachmentEntryIdentity(config.Entries[entryIndex]);
        }

        public string ConfigIdentity { get; set; }
        public string ConfigFingerprint { get; set; }
        public string EntryIdentity { get; set; }
        public WeaponAttachConfig Config { get; }
        public int EntryIndex { get; }
        public AttachmentValues Value { get; set; }
        public AttachmentValues Saved { get; set; }
    }

    [Serializable]
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
            WeaponAttachConfig weaponConfig, int trailEntryIndex, float trailWidth, GameObject trailStyle,
            string semanticRenameBaseline, SemanticRename[] semanticRenames,
            string trailConfigIdentity = null, string trailEntryIdentity = null, string trailConfigFingerprint = null)
        {
            Source = source;
            Catalog = catalog;
            WeaponConfig = weaponConfig;
            TrailEntryIndex = trailEntryIndex;
            TrailWidth = trailWidth;
            TrailStyle = trailStyle;
            TrailConfigIdentity = trailConfigIdentity ?? ObjectIdentity(weaponConfig);
            TrailEntryIdentity = trailEntryIdentity ?? (weaponConfig == null ? "" : AttachmentEntryIdentity(weaponConfig.Entries[trailEntryIndex]));
            TrailConfigFingerprint = trailConfigFingerprint ?? AttachmentFingerprint(weaponConfig);
            SemanticRenameBaseline = semanticRenameBaseline;
            SemanticRenames = semanticRenames;
        }

        public CharacterPackageSource Source { get; }
        public CatalogSnapshot Catalog { get; }
        public WeaponAttachConfig WeaponConfig { get; }
        public int TrailEntryIndex { get; }
        public float TrailWidth { get; }
        public GameObject TrailStyle { get; }
        public string TrailConfigIdentity { get; }
        public string TrailEntryIdentity { get; }
        public string TrailConfigFingerprint { get; set; }
        public string SemanticRenameBaseline { get; }
        public SemanticRename[] SemanticRenames { get; }
    }

    private sealed class CatalogSnapshot
    {
        public CatalogSnapshot(
            GameObject rig,
            CharacterAssetCatalog.AnimationBinding[] bindings,
            CharacterAssetCatalog.PresentationBinding[] presentations, CharacterPresentationProfile profile)
        {
            Rig = rig;
            Bindings = bindings;
            Presentations = presentations;
            Profile = profile;
        }

        public GameObject Rig { get; }
        public CharacterAssetCatalog.AnimationBinding[] Bindings { get; }
        public CharacterAssetCatalog.PresentationBinding[] Presentations { get; }
        public CharacterPresentationProfile Profile { get; }

        public bool Matches(CharacterAssetCatalog catalog)
            => Rig == catalog.Rig &&
               CharacterBindingWriter.ProfileJson(Profile) == CharacterBindingWriter.ProfileJson(catalog.PresentationProfile) &&
               Bindings.Select(binding => (binding?.SemanticId, binding?.PoseTrackId, binding?.Clip, binding?.Extrapolation))
                   .SequenceEqual((catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
                       .Select(binding => (binding?.SemanticId, binding?.PoseTrackId, binding?.Clip, binding?.Extrapolation))) &&
               Presentations.Select(binding => (binding?.SemanticId, binding?.Prefab))
                   .SequenceEqual((catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                       .Select(binding => (binding?.SemanticId, binding?.Prefab)));
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
