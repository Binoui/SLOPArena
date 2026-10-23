using System;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Client.Tools;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    internal enum WorkspaceLeaveChoice { KeepRecovery = 0, Cancel = 1, Discard = 2 }

    [SerializeField] private string _recoveryWindowId = "";
    [SerializeField] private string _serializedRecovery = "";
    private bool _offerClosedRecovery;
    private bool _discardForClose;
    private bool _savedForClose;
    internal string RecoveryError { get; private set; } = "";

    [Serializable]
    private sealed class WindowRecovery
    {
        public int Version = 1;
        public string PackageId = "";
        public string Workspace = "";
        public string Slot = "";
        public int Stage;
        public int Tick;
        public bool Playing;
        public string Page = "moves-page";
        public bool PhaseActive;
        public int Phase;
        public int PhaseTick;
    }

    private static string RecoveryPreference => "SlopArena.AbilityLab.Recovery." + Application.dataPath;
    internal string RecoveryDirectory => Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Library/AbilityLab", _recoveryWindowId);
    private string RecoveryPath(string packageId) => Path.Combine(RecoveryDirectory, packageId + ".json");

    private void InitializeRecoveryIdentity()
    {
        if (!string.IsNullOrEmpty(_recoveryWindowId)) return;
        string[] prior = EditorPrefs.GetString(RecoveryPreference, "").Split('|');
        if (!_openingForCommand && prior.Length == 2 && Guid.TryParseExact(prior[0], "N", out _) &&
            SlopArena.Shared.MatchContentCatalogBuilder.IsStablePackageId(prior[1]) &&
            !Resources.FindObjectsOfTypeAll<AbilityLabWindow>().Any(window => window != this && window._recoveryWindowId == prior[0]))
        {
            _recoveryWindowId = prior[0];
            _reloadPackageId = prior[1];
            _offerClosedRecovery = true;
        }
        else _recoveryWindowId = Guid.NewGuid().ToString("N");
    }

    private void OnWorkspaceStatusChanged()
    {
        _savedForClose = false;
        _discardForClose = false;
        RefreshUnsavedChanges();
        if (_uiReady) RefreshAll();
    }

    private void RefreshUnsavedChanges()
    {
        hasUnsavedChanges = _workspace.HasUnsavedChanges;
        saveChangesMessage = "Save keeps gameplay and attachment drafts in local recovery for the next opening. " +
            "Save + Cook publishes separately. Discard removes unsaved values and reverses unsaved semantic renames. Other saved catalog and trail edits remain saved.";
    }

    internal bool TryLeaveWorkspace(Func<WorkspaceLeaveChoice> choose = null)
    {
        if (!_workspace.HasUnsavedChanges) return ClearRecovery(_workspace.PackageId);
        var choice = choose != null ? choose() : (WorkspaceLeaveChoice)EditorUtility.DisplayDialogComplex(
            "Unsaved Ability Lab changes",
            "Keep gameplay and attachment edits in local recovery before leaving? Save + Cook publishes separately. " +
            "Discard removes unsaved values and reverses unsaved semantic renames. Other saved catalog and trail edits remain saved.",
            "Keep local recovery", "Cancel", "Discard");
        if (choice == WorkspaceLeaveChoice.Cancel) return false;
        if (choice == WorkspaceLeaveChoice.KeepRecovery) return CheckpointWorkspace();
        if (!_workspace.DiscardUnsavedChanges())
        {
            RecoveryError = string.Join("; ", _workspace.Diagnostics.Select(diagnostic => diagnostic.Message));
            return false;
        }
        RefreshUnsavedChanges();
        return ClearRecovery(_workspace.PackageId);
    }

    internal bool TryOpenPackage(string packageId, Func<WorkspaceLeaveChoice> choose = null, bool reload = false)
    {
        if (string.IsNullOrEmpty(packageId)) return false;
        if (!reload && _workspace.HasPackage && _workspace.PackageId == packageId) return true;
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(packageId);
        if ((!inspection.Success || inspection.Source == null || inspection.Catalog == null) &&
            !File.Exists(RecoveryPath(packageId))) return false;
        if (!TryLeaveWorkspace(choose))
        {
            if (_packageSelector != null) BindPackageSelection();
            return false;
        }
        var candidate = new AbilityLabPackageWorkspace();
        if (!LoadRecovery(candidate, packageId) && (!string.IsNullOrEmpty(RecoveryError) || !candidate.OpenPackage(inspection)))
        {
            candidate.ReleasePreview();
            if (_packageSelector != null) BindPackageSelection();
            return false;
        }
        var previous = _workspace;
        previous.StatusChanged -= OnWorkspaceStatusChanged;
        previous.BindPreviewLab(null);
        _workspace = candidate;
        _workspace.BindPreviewLab(_lab);
        _workspace.StatusChanged += OnWorkspaceStatusChanged;
        _reloadPackageId = packageId;
        if (_uiReady && _pages.ContainsKey(_activePage)) SelectTab(_activePage);
        if (_uiReady) RefreshAll();
        previous.ReleasePreview();
        RefreshUnsavedChanges();
        return true;
    }

    internal bool TryReloadPackage(Func<WorkspaceLeaveChoice> choose = null)
        => _workspace.HasPackage && TryOpenPackage(_workspace.PackageId, choose, true);

    internal bool TryNewPackage(string packageId, string displayName, Func<WorkspaceLeaveChoice> choose = null)
    {
        if (!TryLeaveWorkspace(choose)) return false;
        var result = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).NewPackage(packageId, displayName);
        return result.Success && TryOpenPackage(packageId, () => WorkspaceLeaveChoice.KeepRecovery);
    }

    internal bool CheckpointWorkspace()
    {
        if (!_workspace.HasPackage) return true;
        CaptureReloadCursor();
        if (!_workspace.HasUnsavedChanges) return ClearRecovery(_workspace.PackageId);
        string temporary = "";
        try
        {
            var recovery = new WindowRecovery
            {
                PackageId = _workspace.PackageId,
                Workspace = _workspace.CaptureRecovery(),
                Slot = _reloadSlot,
                Stage = _reloadStage,
                Tick = _reloadTick,
                Playing = _reloadPlaying,
                Page = _activePage,
                PhaseActive = _recoveredPhaseActive,
                Phase = _recoveredPhase,
                PhaseTick = _recoveredPhaseTick,
            };
            _reloadPackageId = recovery.PackageId;
            _serializedRecovery = JsonUtility.ToJson(recovery);
            Directory.CreateDirectory(RecoveryDirectory);
            string path = RecoveryPath(recovery.PackageId);
            temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(_serializedRecovery);
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            EditorPrefs.SetString(RecoveryPreference, _recoveryWindowId + "|" + recovery.PackageId);
            RecoveryError = "";
            return true;
        }
        catch (Exception ex)
        {
            RecoveryError = "Could not keep local recovery. The workspace remains open. " + ex.Message;
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporary) && File.Exists(temporary))
                try { File.Delete(temporary); } catch (IOException) { }
        }
    }

    private void CaptureReloadCursor()
    {
        if (_lab?.IsPackagePreview != true || _lab.IsScenarioPreview) return;
        _reloadSlot = _lab.SelectedSlotId;
        _reloadStage = _lab.StageIndex;
        _reloadTick = _lab.Tick;
        _reloadPlaying = _lab.Playing;
        _recoveredPhaseActive = _lab.PhasePreviewActive;
        _recoveredPhase = (int)_lab.Phase;
        _recoveredPhaseTick = _lab.PhaseTick;
        _restoreReloadCursor = true;
    }

    private bool LoadRecovery(AbilityLabPackageWorkspace candidate, string packageId)
    {
        RecoveryError = "";
        string path = RecoveryPath(packageId);
        if (!File.Exists(path) && string.IsNullOrEmpty(_serializedRecovery)) return false;
        try
        {
            WindowRecovery recovery = null;
            if (!string.IsNullOrEmpty(_serializedRecovery))
            {
                var serialized = JsonUtility.FromJson<WindowRecovery>(_serializedRecovery);
                if (serialized?.PackageId == packageId) recovery = serialized;
            }
            if (recovery == null)
            {
                if (!File.Exists(path)) return false;
                recovery = JsonUtility.FromJson<WindowRecovery>(File.ReadAllText(path));
            }
            if (recovery == null || recovery.Version != 1) throw new InvalidDataException("Unsupported window recovery version.");
            if (recovery.PackageId != packageId) return false;
            if (!candidate.RestoreRecovery(recovery.Workspace) || candidate.PackageId != packageId)
                throw new InvalidDataException(string.Join("; ", candidate.Diagnostics.Select(diagnostic => diagnostic.Message)));
            _reloadPackageId = packageId;
            _reloadSlot = recovery.Slot;
            _reloadStage = recovery.Stage;
            _reloadTick = recovery.Tick;
            _reloadPlaying = recovery.Playing;
            _activePage = recovery.Page;
            _restoreReloadCursor = true;
            _refreshAfterCompilation = true;
            _recoveredPhaseActive = recovery.PhaseActive;
            _recoveredPhase = recovery.Phase;
            _recoveredPhaseTick = recovery.PhaseTick;
            return true;
        }
        catch (Exception ex)
        {
            RecoveryError = "Local recovery was retained; opening was cancelled. " + ex.Message;
            return false;
        }
    }

    private bool _recoveredPhaseActive;
    private int _recoveredPhase, _recoveredPhaseTick;

    private void RestoreWindowRecovery()
    {
        if (_workspace.HasPackage || string.IsNullOrEmpty(_reloadPackageId) || _suppressInitialPackage) return;
        if (_offerClosedRecovery)
        {
            _offerClosedRecovery = false;
            if (!File.Exists(RecoveryPath(_reloadPackageId))) return;
            if (!EditorUtility.DisplayDialog("Recover Ability Lab draft",
                    "Recover the local gameplay and attachment draft? The original saved-source baselines are retained so external changes remain conflicts.",
                    "Recover local draft", "Cancel"))
            {
                _suppressInitialPackage = true;
                return;
            }
        }
        var candidate = new AbilityLabPackageWorkspace();
        if (LoadRecovery(candidate, _reloadPackageId))
        {
            _workspace.ReleasePreview();
            _workspace = candidate;
            if (_pages.ContainsKey(_activePage)) SelectTab(_activePage);
            RefreshUnsavedChanges();
        }
        else candidate.ReleasePreview();
    }

    private bool ClearRecovery(string packageId)
    {
        if (string.IsNullOrEmpty(packageId)) return true;
        try
        {
            string path = RecoveryPath(packageId);
            if (File.Exists(path)) File.Delete(path);
            if (!string.IsNullOrEmpty(_serializedRecovery) &&
                JsonUtility.FromJson<WindowRecovery>(_serializedRecovery)?.PackageId == packageId)
                _serializedRecovery = "";
            RecoveryError = "";
            return true;
        }
        catch (Exception ex)
        {
            RecoveryError = "Could not clear local recovery. Departure was cancelled. " + ex.Message;
            return false;
        }
    }

    private void SaveAndCook()
    {
        _workspace.SavePackage();
        if (!_workspace.HasUnsavedChanges) ClearRecovery(_workspace.PackageId);
        RefreshAll();
    }

    public override void SaveChanges()
    {
        if (!CheckpointWorkspace()) throw new InvalidOperationException(RecoveryError);
        _savedForClose = true;
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        if (_workspace.HasPackage && !_workspace.DiscardUnsavedChanges())
            throw new InvalidOperationException(string.Join("; ", _workspace.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (!ClearRecovery(_workspace.PackageId)) throw new InvalidOperationException(RecoveryError);
        _discardForClose = true;
        base.DiscardChanges();
    }
}
