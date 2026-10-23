using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabPackageWorkspace
{
    private readonly List<SemanticRename> _semanticRenames = new();
    private string _semanticRenameBaseline = "";
    private string _recoveredPresentationProfileJson = "";

    [Serializable]
    private sealed class SemanticRename
    {
        public string Before = "";
        public string After = "";
    }

    [Serializable]
    private sealed class Recovery
    {
        public int Version = 1;
        public string PackageRoot = "";
        public string Manifest = "";
        public string Character = "";
        public string SavedManifest = "";
        public string SavedCharacter = "";
        public string DiskHash = "";
        public string CatalogFingerprint = "";
        public string PresentationProfileJson = "";
        public string SemanticRenameBaseline = "";
        public SemanticRename[] SemanticRenames = Array.Empty<SemanticRename>();
        public AttachmentRecovery Attachment;
        public string PreviewPackageHash = "";
        public string PreviewSourceHash = "";
        public string PreviewContentHash = "";
        public string PreviewDependencyHash = "";
    }

    [Serializable]
    private sealed class AttachmentRecovery
    {
        public string ConfigIdentity = "";
        public string ConfigFingerprint = "";
        public string EntryIdentity = "";
        public int EntryIndex;
        public AttachmentValues Saved;
        public AttachmentValues Value;
    }

    private void RememberSavedSource()
    {
        _savedManifestJson = CharacterPackageSourceCodec.SerializeManifest(Manifest);
        _savedCharacterJson = CharacterPackageSourceCodec.SerializeCharacter(Draft);
        UpdateSourceDirty();
    }

    private void UpdateSourceDirty()
    {
        IsDirty = HasPackage && (CharacterPackageSourceCodec.SerializeManifest(Manifest) != _savedManifestJson ||
            CharacterPackageSourceCodec.SerializeCharacter(Draft) != _savedCharacterJson);
    }

    internal string CaptureRecovery()
    {
        if (!HasPackage) throw new InvalidOperationException("No workspace is open.");
        return JsonUtility.ToJson(new Recovery
        {
            PackageRoot = PackageRoot,
            Manifest = CharacterPackageSourceCodec.SerializeManifest(Manifest),
            Character = CharacterPackageSourceCodec.SerializeCharacter(Draft),
            SavedManifest = _savedManifestJson,
            SavedCharacter = _savedCharacterJson,
            DiskHash = LoadedDiskHash,
            CatalogFingerprint = LoadedCatalogFingerprint,
            PresentationProfileJson = string.IsNullOrEmpty(_recoveredPresentationProfileJson)
                ? CharacterBindingWriter.ProfileJson(Catalog.PresentationProfile) : _recoveredPresentationProfileJson,
            SemanticRenameBaseline = _semanticRenameBaseline,
            SemanticRenames = _semanticRenames.ToArray(),
            Attachment = _attachmentDraft == null ? null : new AttachmentRecovery
            {
                ConfigIdentity = _attachmentDraft.ConfigIdentity,
                ConfigFingerprint = _attachmentDraft.ConfigFingerprint,
                EntryIdentity = _attachmentDraft.EntryIdentity,
                EntryIndex = _attachmentDraft.EntryIndex,
                Saved = _attachmentDraft.Saved,
                Value = _attachmentDraft.Value,
            },
            PreviewPackageHash = Preview?.Identity?.PackageHash ?? "",
            PreviewSourceHash = Preview?.Identity?.SourceHash ?? "",
            PreviewContentHash = Preview?.Identity?.CookedContentHash ?? "",
            PreviewDependencyHash = _previewDependencyHash,
        });
    }

    internal bool RestoreRecovery(string json)
    {
        try
        {
            var recovery = JsonUtility.FromJson<Recovery>(json);
            if (recovery == null || recovery.Version != 1) throw new InvalidDataException("Unsupported local recovery version.");
            var current = CharacterPackageSourceCodec.Load(recovery.Manifest, recovery.Character);
            var saved = CharacterPackageSourceCodec.Load(recovery.SavedManifest, recovery.SavedCharacter);
            if (current.Source == null || saved.Source == null ||
                current.Source.Manifest.PackageId != saved.Source.Manifest.PackageId ||
                !SamePackage(recovery.PackageRoot, "Assets/CharacterPackages/" + current.Source.Manifest.PackageId) ||
                string.IsNullOrEmpty(recovery.DiskHash) || string.IsNullOrEmpty(recovery.CatalogFingerprint))
                throw new InvalidDataException("Local recovery is incomplete or belongs to another package.");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(recovery.PackageRoot + "/CharacterAssetCatalog.asset");
            if (catalog == null) throw new InvalidDataException("Character asset catalog is missing. Restore it before opening this local recovery.");
            CharacterPresentationProfile recoveredProfile = null;
            if (!string.IsNullOrEmpty(recovery.PresentationProfileJson) && recovery.PresentationProfileJson != "null")
            {
                using var profileDocument = System.Text.Json.JsonDocument.Parse(recovery.PresentationProfileJson);
                recoveredProfile = CharacterBindingWriter.ReadProfile(profileDocument.RootElement, allowMissing: true);
                if (CharacterBindingWriter.ProfileJson(recoveredProfile) != recovery.PresentationProfileJson)
                    throw new InvalidDataException("Local presentation recovery has noncanonical or unknown fields.");
            }
            var attachment = recovery.Attachment;
            if (attachment != null && (!attachment.Saved.HasFiniteOffsets || !attachment.Value.HasFiniteOffsets))
                throw new InvalidDataException("Local attachment recovery has invalid offsets.");

            PackageRoot = recovery.PackageRoot;
            Manifest = current.Source.Manifest;
            Draft = current.Source.Character;
            _savedManifestJson = recovery.SavedManifest;
            _savedCharacterJson = recovery.SavedCharacter;
            LoadedDiskHash = recovery.DiskHash;
            LoadedCatalogFingerprint = recovery.CatalogFingerprint;
            Catalog = catalog;
            _recoveredPresentationProfileJson = recovery.PresentationProfileJson ?? "";
            // Keep the persisted authored profile authoritative. Committed fallback IDs remain recovery metadata.
            _semanticRenameBaseline = recovery.SemanticRenameBaseline ?? "";
            _semanticRenames.Clear();
            _semanticRenames.AddRange(recovery.SemanticRenames ?? Array.Empty<SemanticRename>());
            _undo.Clear();
            _redo.Clear();
            _attachmentUndo.Clear();
            _attachmentRedo.Clear();
            _attachmentDraft = null;
            AttachmentDraftError = "";
            if (attachment != null)
            {
                var config = ResolveObject(attachment.ConfigIdentity) as WeaponAttachConfig;
                _attachmentDraft = new AttachmentDraftState(config, attachment.EntryIndex, attachment.Saved,
                    attachment.ConfigIdentity, attachment.ConfigFingerprint, attachment.EntryIdentity)
                {
                    Value = attachment.Value,
                };
                if (!AttachmentTargetMatches(_attachmentDraft))
                    AttachmentDraftError = "Attachment source changed externally. Recovered placement is retained; saving is blocked.";
            }
            UpdateSourceDirty();
            LiveDraftPackage = null;
            LiveDraftInvalid = false;
            var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(PackageRoot);
            bool originalPrerequisites = SourcesMatchBaseline() && inspection.Status == "valid" &&
                inspection.PackageHash == recovery.PreviewPackageHash &&
                inspection.CookedSourceHash == recovery.PreviewSourceHash &&
                inspection.CookedContentHash == recovery.PreviewContentHash &&
                PreviewDependencyHash() == recovery.PreviewDependencyHash;
            ReleasePreview();
            Preview = null;
            _previewDependencyHash = recovery.PreviewDependencyHash;
            if (originalPrerequisites)
            {
                var candidate = AbilityLabPackagePreviewLoader.Load(PackageId);
                if (PreviewMatches(candidate, inspection)) ReplacePreview(candidate);
                else ReleasePreview(candidate);
            }
            PreviewPrerequisitesStale = !originalPrerequisites || Preview?.IsAvailable != true || !string.IsNullOrEmpty(AttachmentDraftError);
            RefreshPersistedStatus(true);
            RefreshLiveDraft(current.Source, current.Diagnostics);
            return true;
        }
        catch (Exception ex)
        {
            return Fail("workspace.recovery.failed", PackageRoot, "Local recovery was retained. " + ex.Message);
        }
    }

    internal bool DiscardUnsavedChanges()
    {
        if (_semanticRenames.Count > 0)
        {
            if (!SourcesMatchBaseline())
                return Fail("workspace.conflict", PackageRoot,
                    "Cannot discard a semantic rename over external source or catalog changes. Keep a local recovery first.");
            var prior = CaptureSnapshot();
            try
            {
                UnityEditor.Undo.RecordObject(Catalog, "Discard Ability Lab semantic rename");
                Catalog.Bindings = CloneBindings(Catalog.Bindings);
                Catalog.Presentations = ClonePresentations(Catalog.Presentations);
                foreach (var rename in _semanticRenames.AsEnumerable().Reverse())
                {
                    foreach (var binding in Catalog.Bindings.Where(binding => binding != null))
                    {
                        if (binding.SemanticId == rename.After) binding.SemanticId = rename.Before;
                        if (binding.PoseTrackId == rename.After) binding.PoseTrackId = rename.Before;
                    }
                    foreach (var binding in Catalog.Presentations.Where(binding => binding != null))
                        if (binding.SemanticId == rename.After) binding.SemanticId = rename.Before;
                }
                EditorUtility.SetDirty(Catalog);
                AssetDatabase.SaveAssetIfDirty(Catalog);
                LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            }
            catch (Exception ex)
            {
                RestoreSnapshot(prior, false);
                return Fail("workspace.discard.failed", PackageRoot, ex.Message);
            }
        }
        var saved = CharacterPackageSourceCodec.Load(_savedManifestJson, _savedCharacterJson);
        if (saved.Source == null) return Fail("workspace.recovery.failed", PackageRoot, "Saved source baseline is invalid.");
        Manifest = saved.Source.Manifest;
        Draft = saved.Source.Character;
        _semanticRenames.Clear();
        _semanticRenameBaseline = "";
        RevertAttachmentDraft();
        _undo.Clear();
        _redo.Clear();
        UpdateSourceDirty();
        RefreshLiveDraft(saved.Source, saved.Diagnostics);
        return true;
    }

    private bool CanWritePersistedAssets()
        => SourcesMatchBaseline() || Fail("workspace.conflict", PackageRoot,
            "Source or catalog changed externally. Keep a local recovery before reloading; writes are blocked.");

    private bool CanRestoreSnapshot(WorkspaceSnapshot snapshot)
    {
        if (snapshot.TrailEntryIndex >= 0 &&
            (snapshot.WeaponConfig == null || snapshot.WeaponConfig != Catalog?.WeaponConfig ||
             ObjectIdentity(snapshot.WeaponConfig) != snapshot.TrailConfigIdentity ||
             AttachmentFingerprint(snapshot.WeaponConfig) != snapshot.TrailConfigFingerprint ||
             snapshot.WeaponConfig.Entries == null || snapshot.TrailEntryIndex >= snapshot.WeaponConfig.Entries.Length ||
             AttachmentEntryIdentity(snapshot.WeaponConfig.Entries[snapshot.TrailEntryIndex]) != snapshot.TrailEntryIdentity))
            return Fail("workspace.conflict", PackageRoot,
                "Weapon attachment changed externally. History was retained without writing over those changes.");
        if (Catalog != null && snapshot.Catalog != null && !snapshot.Catalog.Matches(Catalog) &&
            ComputeCatalogFingerprint() != LoadedCatalogFingerprint)
            return Fail("workspace.conflict", PackageRoot, "Catalog changed externally. Undo was retained without writing over those changes.");
        return true;
    }

    private static string ObjectIdentity(UnityEngine.Object value)
        => value == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();

    private static UnityEngine.Object ResolveObject(string identity)
        => GlobalObjectId.TryParse(identity, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;

    private static string AttachmentFingerprint(WeaponAttachConfig config)
        => config == null ? "" : HashFiles(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(config)), Array.Empty<byte>());

    private static string AttachmentEntryIdentity(WeaponEntry entry)
        => entry == null ? "" : entry.AttackSlot + "|" + entry.BoneName + "|" + ObjectIdentity(entry.Prefab);

    private bool AttachmentTargetMatches(AttachmentDraftState draft)
        => draft.Config != null && draft.Config == Catalog?.WeaponConfig && ObjectIdentity(draft.Config) == draft.ConfigIdentity &&
           AttachmentFingerprint(draft.Config) == draft.ConfigFingerprint && draft.Config.Entries != null &&
           draft.EntryIndex >= 0 && draft.EntryIndex < draft.Config.Entries.Length &&
           AttachmentEntryIdentity(draft.Config.Entries[draft.EntryIndex]) == draft.EntryIdentity;

    private void RefreshOwnedAttachmentFingerprint(string before)
    {
        var config = Catalog?.WeaponConfig;
        string after = AttachmentFingerprint(config);
        if (_attachmentDraft != null && _attachmentDraft.Config == Catalog?.WeaponConfig && _attachmentDraft.ConfigFingerprint == before)
            _attachmentDraft.ConfigFingerprint = after;
        foreach (var snapshot in _undo.Concat(_redo))
            if (snapshot.WeaponConfig == config && snapshot.TrailConfigFingerprint == before)
                snapshot.TrailConfigFingerprint = after;
    }
}
