using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public static class AbilityLabRecoverySelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Draft Recovery")]
    public static void RunDraftRecovery()
    {
        using var fixture = new Fixture();
        VerifyUndoAndClone(fixture);
        VerifyCommandRecovery(fixture);
        VerifyAttachmentBindingConflict(fixture);
        VerifyTrailHistoryTargets(fixture);
        VerifyNavigationAndReload(fixture);
        VerifySemanticRenameRecovery(fixture);
        Debug.Log("[AbilityLabRecoverySelfTest] Draft recovery passed. Undo, visibility, command recovery, binding conflicts, navigation cancellation, per-package checkpoints, reload conflicts and close recovery preserved isolated drafts.");
    }

    public static void RunPresentationProfileRecovery()
    {
        using var fixture = new Fixture();
        var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
        var clip = source?.PresentationProfile?.LightImpact.Clips[0];
        Require(clip != null, "Migrate the admitted presentation profiles before this check.");
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(fixture.Root(fixture.First) + "/CharacterAssetCatalog.asset");
        var before = catalog.PresentationProfile.DeepClone();
        var workspace = new AbilityLabPackageWorkspace();
        var restored = new AbilityLabPackageWorkspace();
        try
        {
            var pool = new AudioPool(AudioPoolSelection.Fixed, new[] { clip });
            catalog.SetPresentationProfile(new CharacterPresentationProfile(Color.cyan,
                new AudioPool(AudioPoolSelection.Fixed, Array.Empty<AudioClip>()), pool, pool, pool, false,
                new[] { new ProjectilePresentation("ground.3", fixture.Prop, 1f, ProjectileOrientation.PreservePrefab) }));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Require(workspace.OpenPackage(fixture.Root(fixture.First)), "Profile recovery fixture did not open.");
            string recovery = workspace.CaptureRecovery();
            Require(restored.RestoreRecovery(recovery), "Profile recovery did not restore.");
            Require(ReferenceEquals(restored.Catalog.PresentationProfile, catalog.PresentationProfile)
                && catalog.PresentationProfile.LightImpact.ClipObjectIds.Length == 0
                && string.IsNullOrEmpty(catalog.PresentationProfile.Projectiles[0].PrefabObjectId),
                "Recovery injected committed fallback IDs into the authored profile.");
            string fingerprint = CharacterPackageAuthoringService.ComputeCatalogFingerprint(catalog);
            using (var serialized = new SerializedObject(catalog))
            {
                serialized.Update();
                var profile = serialized.FindProperty("_presentationProfile");
                profile.FindPropertyRelative("_lightImpact").FindPropertyRelative("_clips").GetArrayElementAtIndex(0).objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var parsed = System.Text.Json.JsonDocument.Parse(CharacterBindingWriter.ProfileJson(catalog.PresentationProfile)))
                Require(parsed.RootElement.GetProperty("lightImpact").GetProperty("clipGlobalObjectIds")[0].GetString() == "",
                    "An authored audio clear retained the recovered clip identity.");
            Require(CharacterPackageAuthoringService.ComputeCatalogFingerprint(catalog) != fingerprint,
                "An authored audio clear did not invalidate the catalog fingerprint.");
            fingerprint = CharacterPackageAuthoringService.ComputeCatalogFingerprint(catalog);
            using (var serialized = new SerializedObject(catalog))
            {
                serialized.Update();
                serialized.FindProperty("_presentationProfile").FindPropertyRelative("_projectiles")
                    .GetArrayElementAtIndex(0).FindPropertyRelative("_prefab").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var parsed = System.Text.Json.JsonDocument.Parse(CharacterBindingWriter.ProfileJson(catalog.PresentationProfile)))
                Require(parsed.RootElement.GetProperty("projectiles")[0].GetProperty("prefabGlobalObjectId").GetString() == "",
                    "An authored projectile clear retained the recovered prefab identity.");
            Require(CharacterPackageAuthoringService.ComputeCatalogFingerprint(catalog) != fingerprint,
                "An authored projectile clear did not invalidate the catalog fingerprint.");
        }
        finally
        {
            workspace.ReleasePreview(); restored.ReleasePreview();
            catalog.SetPresentationProfile(before);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
        Debug.Log("[AbilityLabRecoverySelfTest] Profile recovery retained authored authority; serialized audio/projectile clears removed identities and changed fingerprints.");
    }

    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Preview Refresh")]
    public static void RunPreviewRefresh()
    {
        using var fixture = new Fixture();
        AnimationClip clip = fixture.MakeCookable();
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        Cook();
        var window = fixture.Window();
        try
        {
            window.CreateGUI();
            using var panel = (IPanel)typeof(AbilityLabFrontendSelfTest)
                .GetMethod("CreateHiddenEditorPanel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { window });
            Require(window.TryOpenPackage(fixture.First), "Cooked isolated package did not open.");
            var workspace = window.CommandWorkspace;
            var lab = window.CommandLab ?? throw new InvalidOperationException("Window preview is unavailable.");
            var originalDraft = workspace.Draft;
            workspace.SetDraft(originalDraft with { DisplayName = "Transient preview name" });
            workspace.Undo();
            Require(!workspace.IsDirty && workspace.CanRedo, "Undo to the saved source still reports unsaved source.");
            var draft = workspace.Draft;
            var oldPreview = workspace.Preview;
            var oldCatalog = oldPreview.AnimationCatalog;
            lab.SetSlot(CanonicalSlotProjection.All.First(slot => slot.Id == "ground.1"));
            lab.SetStage(0);
            lab.SetTick(3);
            lab.Playing = false;

            RecookClip(30f);
            workspace.RefreshPersistedStatus(true);
            Invoke(window, "RefreshAll");
            Require(workspace.PackageHash != oldPreview.Identity.PackageHash &&
                workspace.Preview.Identity.PackageHash == workspace.PackageHash &&
                !ReferenceEquals(oldPreview.BakedPoses, workspace.Preview.BakedPoses) && oldCatalog == null,
                "External recook retained old poses or leaked the replaced owned catalog.");
            Require(ReferenceEquals(draft, workspace.Draft) && !workspace.IsDirty && workspace.CanRedo &&
                !workspace.PreviewPrerequisitesStale && workspace.Status == "Valid",
                "Clean recook refresh replaced the source, Undo, or freshness state.");
            Require(lab.SelectedSlotId == "ground.1" && lab.StageIndex == 0 && lab.Tick == 3 && !lab.Playing,
                "Prerequisite refresh lost the selected timeline cursor.");

            var retained = workspace.Preview;
            RecookClip(24f);
            lab.Playing = true;
            window.rootVisualElement.Q<TextField>("character-display-name").value = "First observation through gameplay UI";
            Require(workspace.Draft.DisplayName == "First observation through gameplay UI" && workspace.IsDirty && workspace.CanUndo &&
                ReferenceEquals(retained, workspace.Preview) && workspace.PreviewPrerequisitesStale &&
                workspace.PackageHash != retained.Identity.PackageHash,
                "Dirty recook refresh discarded source or paired a new identity with old authoritative poses.");
            Require(!lab.Playing && !lab.AuthoritativePreview && !lab.CanRunScenario && !lab.IsPackagePreview &&
                !window.rootVisualElement.Q<Button>("scenario-run").enabledSelf &&
                workspace.Diagnostics.Any(diagnostic => diagnostic.Code == "preview.prerequisites.stale"),
                "A stale prerequisite set still admits authoritative preview or scenarios.");

            Require(workspace.DiscardUnsavedChanges(), "Could not discard the isolated stale gameplay edit.");
            Invoke(window, "RefreshAll");
            Require(lab.CanRunScenario, "Clean preview did not recover after discarding the stale draft.");
            retained = workspace.Preview;
            RecookClip(20f);
            lab.Playing = true;
            SetField(window, "_uiReady", false);
            try { workspace.SetDraft(workspace.Draft with { DisplayName = "Admission before status polling" }); }
            finally { SetField(window, "_uiReady", true); }
            Require(!workspace.PreviewPrerequisitesStale && lab.CanRunScenario,
                "Admission fixture observed persisted status before invoking the scenario handler.");
            Invoke(window, "RunScenarioFromControls");
            Require(workspace.PreviewPrerequisitesStale && ReferenceEquals(retained, workspace.Preview) &&
                !lab.CanRunScenario && !lab.Playing && lab.Scenario == null,
                "Scenario admission accepted stale prerequisites before the status poll.");
            Require(workspace.DiscardUnsavedChanges(), "Could not discard the admission fixture edit.");
            Invoke(window, "RefreshAll");
            VerifyCleanTransitions(fixture, window, RecookClip, Cook);
            AbilityLabScenarioCommandSelfTest.AssertRunRevalidatesPreview(window, () => RecookClip(12f));
            VerifySavedSemanticRename(workspace, fixture.Prop);
            Invoke(window, "RefreshAll");
            window = VerifyRepeatedReloadCallbacks(fixture, window);
        }
        finally { fixture.Close(window); }
        Debug.Log("[AbilityLabRecoverySelfTest] Preview refresh passed. Isolated recooks refreshed clean poses, blocked stale UI refresh and admission, retained compatible saved Undo, and preserved phase cursor through repeated reload callbacks.");

        void Cook()
        {
            var result = service.Cook(fixture.First);
            Require(result.Success, "Isolated preview cook failed. " + string.Join("; ", result.Diagnostics.Select(value => value.Message)));
        }

        void RecookClip(float frameRate)
        {
            clip.frameRate = frameRate;
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(clip), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Cook();
        }
    }

    private static void VerifyCleanTransitions(Fixture fixture, AbilityLabWindow window, Action<float> recook, Action cook)
    {
        var workspace = window.CommandWorkspace;
        var lab = window.CommandLab ?? throw new InvalidOperationException("Clean-transition fixture has no Lab.");
        var saved = workspace.Draft;
        foreach (bool redo in new[] { false, true })
        {
            workspace.SetDraft(saved with { DisplayName = redo ? "Redo-to-clean draft" : "Undo-to-clean draft" });
            if (redo)
            {
                workspace.SetDraft(saved);
                workspace.Undo();
            }
            var prior = workspace.Preview;
            recook(redo ? 18f : 19f);
            Invoke(window, "RefreshAll");
            Require(workspace.IsDirty && workspace.PreviewPrerequisitesStale && !lab.CanRunScenario,
                "Dirty source did not block changed prerequisites before history cleanup.");
            if (redo) workspace.Redo();
            else workspace.Undo();
            Invoke(window, "RefreshAll");
            Require(workspace.Draft.DisplayName == saved.DisplayName && !workspace.IsDirty,
                "Source history did not restore saved values.");
            RequireFresh(prior, redo ? "source Redo" : "source Undo");
        }

        var config = AssetDatabase.LoadAssetAtPath<WeaponAttachConfig>(fixture.Root(fixture.First) + "/attachments.asset");
        var rig = UnityEngine.Object.Instantiate(workspace.Catalog.Rig);
        try
        {
            config.Entries[0].BoneName = rig.GetComponentInChildren<Animator>(true)
                .GetBoneTransform(HumanBodyBones.RightHand).name;
        }
        finally { UnityEngine.Object.DestroyImmediate(rig); }
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        workspace.Catalog.WeaponConfig = config;
        EditorUtility.SetDirty(workspace.Catalog);
        AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
        cook();
        Invoke(window, "RefreshAll");
        Require(workspace.PreviewPrerequisitesStale && !lab.CanRunScenario,
            "An externally replaced weapon binding did not block the old workspace.");
        Require(workspace.ReloadPackage(), "Could not load the explicitly saved attachment fixture binding.");
        Invoke(window, "RefreshAll");
        Require(lab.CanRunScenario && !workspace.HasUnsavedChanges, "Cookable attachment fixture did not refresh cleanly.");
        foreach (bool undo in new[] { false, true })
        {
            Require(workspace.BeginAttachmentDraft(0) && workspace.SetAttachmentPositionOffset(new Vector3(1, 2, 3)),
                "Clean-transition attachment draft did not start.");
            var prior = workspace.Preview;
            recook(undo ? 16f : 17f);
            Invoke(window, "RefreshAll");
            Require(workspace.AttachmentDraftDirty && workspace.PreviewPrerequisitesStale && !lab.CanRunScenario,
                "Dirty attachment did not block changed prerequisites.");
            Require(undo ? workspace.UndoAttachmentDraft() : workspace.RevertAttachmentDraft(),
                "Attachment cleanup was unavailable.");
            Invoke(window, "RefreshAll");
            Require(!workspace.HasUnsavedChanges && config.Entries[0].PositionOffset == Vector3.zero &&
                (!undo || workspace.AttachmentPositionOffset == Vector3.zero),
                "Attachment cleanup changed persisted placement or retained dirty values.");
            RequireFresh(prior, undo ? "attachment Undo" : "attachment Revert");
            workspace.RevertAttachmentDraft();
        }

        workspace.SetDraft(saved with { DisplayName = "Clean transition with an external conflict" });
        var conflictedPreview = workspace.Preview;
        recook(15f);
        Invoke(window, "RefreshAll");
        string path = Path.Combine(fixture.Full(fixture.First), "package.json");
        byte[] original = File.ReadAllBytes(path);
        byte[] external = original.Concat(new byte[] { (byte)'\n' }).ToArray();
        try
        {
            File.WriteAllBytes(path, external);
            workspace.Undo();
            Invoke(window, "RefreshAll");
            Require(!workspace.HasUnsavedChanges && workspace.CanRedo && workspace.PreviewPrerequisitesStale &&
                ReferenceEquals(conflictedPreview, workspace.Preview) && !workspace.PrepareScenarioPreview() &&
                !lab.CanRunScenario && File.ReadAllBytes(path).SequenceEqual(external),
                "Returning to clean adopted or overwrote an external source conflict.");
        }
        finally { File.WriteAllBytes(path, original); }
        Invoke(window, "RefreshAll");
        RequireFresh(conflictedPreview, "restored external fixture source");

        void RequireFresh(AbilityLabPackagePreviewResult prior, string transition)
        {
            Require(!workspace.PreviewPrerequisitesStale && workspace.Preview.Identity.PackageHash == workspace.PackageHash &&
                !ReferenceEquals(prior, workspace.Preview) && workspace.PrepareScenarioPreview() && lab.CanRunScenario,
                "Current prerequisites did not recover after " + transition + ".");
        }
    }

    private static void VerifyUndoAndClone(Fixture fixture)
    {
        var workspace = new AbilityLabPackageWorkspace();
        try
        {
            Require(workspace.OpenPackage(fixture.Root(fixture.First)), "Isolated Undo workspace did not open.");
            var original = workspace.Draft;
            for (int edit = 1; edit <= 51; edit++) workspace.SetDraft(original with { DisplayName = "Edit " + edit });
            workspace.Undo();
            Require(workspace.Draft.DisplayName == "Edit 50", "Undo after edit 51 skipped edit 50.");
            int undoCount = 1;
            while (workspace.CanUndo) { workspace.Undo(); undoCount++; }
            Require(undoCount == 50 && workspace.Draft.DisplayName == "Edit 1", "Bounded Undo did not retain the newest 50 snapshots.");
            int redoCount = 0;
            while (workspace.CanRedo) { workspace.Redo(); redoCount++; }
            Require(redoCount == 50 && workspace.Draft.DisplayName == "Edit 51", "Redo did not replay all 50 retained edits.");
            Require(workspace.DiscardUnsavedChanges() && !workspace.IsDirty, "Discard did not restore saved-source cleanliness.");
            Require(workspace.BeginAttachmentDraft(0) && workspace.SetAttachmentPositionOffset(new Vector3(4, 5, 6)),
                "Attachment fixture did not create a draft.");
            var source = workspace.Catalog.WeaponConfig.Entries[0];
            var preview = workspace.CreateAttachmentPreviewConfig();
            try
            {
                var clone = preview.Entries[0];
                Require(!ReferenceEquals(source, clone) && clone.ShowFromTicks == 6 && clone.HiddenBySlot == 11 &&
                    clone.HiddenBySlotFromTicks == 6 && clone.PositionOffset == new Vector3(4, 5, 6) && source.PositionOffset == Vector3.zero,
                    "Attachment clone changed visibility or wrote draft placement to the source.");
            }
            finally { UnityEngine.Object.DestroyImmediate(preview); }
        }
        finally { workspace.ReleasePreview(); }
    }

    private static void VerifyCommandRecovery(Fixture fixture)
    {
        var window = fixture.Window();
        try
        {
            Require(window.TryOpenPackage(fixture.First), "Command recovery fixture did not open.");
            var workspace = window.CommandWorkspace;
            workspace.SetDraft(workspace.Draft with { DisplayName = "Command recovered draft" });
            Require(workspace.BeginAttachmentDraft(0) && workspace.SetAttachmentPositionOffset(new Vector3(2, 3, 4)),
                "Command recovery attachment draft did not start.");
            Require(window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery),
                "Command recovery fixture did not switch to a clean package.");
            var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
            var first = service.Inspect(fixture.First);
            var second = service.Inspect(fixture.Second);
            Require(window.OpenPackageForCommand(first, out _, out _) &&
                window.CommandWorkspace.Draft.DisplayName == "Command recovered draft" &&
                window.CommandWorkspace.AttachmentPositionOffset == new Vector3(2, 3, 4),
                "Command navigation opened saved source instead of the retained package recovery.");
            workspace = window.CommandWorkspace;
            Require(window.OpenPackageForCommand(first, out _, out _) && ReferenceEquals(workspace, window.CommandWorkspace),
                "Same-package command replaced the recovered workspace.");
            Require(!window.OpenPackageForCommand(second, out string code, out _) && code == "workspace.dirty" &&
                ReferenceEquals(workspace, window.CommandWorkspace), "Command navigation bypassed the noninteractive dirty guard.");
            Require(window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery) &&
                window.OpenPackageForCommand(first, out _, out _) && window.CommandWorkspace.Draft.DisplayName == "Command recovered draft" &&
                window.CommandWorkspace.AttachmentPositionOffset == new Vector3(2, 3, 4),
                "A second command round trip deleted the retained source or placement.");
            Require(window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery),
                "Could not prepare failed command recovery.");
            workspace = window.CommandWorkspace;
            string path = Path.Combine(window.RecoveryDirectory, fixture.First + ".json");
            string checkpoint = File.ReadAllText(path);
            SetField(window, "_serializedRecovery", "");
            File.WriteAllText(path, "invalid command recovery");
            Require(!window.OpenPackageForCommand(first, out _, out _) && ReferenceEquals(workspace, window.CommandWorkspace) &&
                workspace.PackageId == fixture.Second && File.ReadAllText(path) == "invalid command recovery",
                "Failed command recovery replaced the outgoing workspace or erased the checkpoint.");
            File.WriteAllText(path, checkpoint);
        }
        finally { fixture.Close(window); }
    }

    private static void VerifyAttachmentBindingConflict(Fixture fixture)
    {
        var workspace = new AbilityLabPackageWorkspace();
        WeaponAttachConfig original = null;
        try
        {
            Require(workspace.OpenPackage(fixture.Root(fixture.First)), "Attachment binding fixture did not open.");
            original = workspace.Catalog.WeaponConfig;
            var replacement = ScriptableObject.CreateInstance<WeaponAttachConfig>();
            replacement.Entries = new[] { new WeaponEntry { BoneName = "replacement", PositionOffset = new Vector3(10, 11, 12) } };
            AssetDatabase.CreateAsset(replacement, fixture.Root(fixture.First) + "/replacement-attachments.asset");
            Require(workspace.BeginAttachmentDraft(0) && workspace.SetAttachmentPositionOffset(new Vector3(5, 6, 7)),
                "Attachment binding fixture did not create a placement draft.");
            string recovery = workspace.CaptureRecovery();
            workspace.Catalog.WeaponConfig = replacement;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
            string[] paths = { AssetDatabase.GetAssetPath(original), AssetDatabase.GetAssetPath(replacement),
                AssetDatabase.GetAssetPath(workspace.Catalog), fixture.Root(fixture.First) + "/character.json",
                fixture.Root(fixture.First) + "/package.json" };
            byte[][] saved = paths.Select(path => File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path))).ToArray();
            VerifyBlocked();
            Require(workspace.RestoreRecovery(recovery) && workspace.AttachmentDraftDirty &&
                workspace.AttachmentPositionOffset == new Vector3(5, 6, 7) && !string.IsNullOrEmpty(workspace.AttachmentDraftError),
                "Recovery lost placement values or accepted a config no longer bound to the package.");
            VerifyBlocked();

            void VerifyBlocked()
            {
                Require(!workspace.BeginAttachmentDraft(0) && workspace.AttachmentDraftSourceConfig == original &&
                    workspace.AttachmentPositionOffset == new Vector3(5, 6, 7),
                    "Refreshing attachment authoring replaced a conflicted placement draft.");
                var preview = workspace.CreateAttachmentPreviewConfig();
                try { Require(preview == null, "Attachment preview applied an obsolete package config."); }
                finally { if (preview != null) UnityEngine.Object.DestroyImmediate(preview); }
                Require(!workspace.SaveAttachmentDraft() && !workspace.SavePackage() && workspace.AttachmentDraftDirty &&
                    workspace.AttachmentPositionOffset == new Vector3(5, 6, 7) && !workspace.PrepareScenarioPreview(),
                    "An obsolete package attachment binding admitted preview or persistence.");
                for (int index = 0; index < paths.Length; index++)
                    Require(File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), paths[index])).SequenceEqual(saved[index]),
                        "Attachment binding conflict changed persisted bytes at " + paths[index]);
            }
        }
        finally
        {
            if (workspace.Catalog != null && original != null)
            {
                workspace.Catalog.WeaponConfig = original;
                EditorUtility.SetDirty(workspace.Catalog);
                AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
            }
            workspace.ReleasePreview();
        }
    }

    private static void VerifyTrailHistoryTargets(Fixture fixture)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(fixture.Root(fixture.First) + "/CharacterAssetCatalog.asset");
        var config = catalog.WeaponConfig;
        string original = EditorJsonUtility.ToJson(config);
        var replacement = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        replacement.Entries = new[] { new WeaponEntry { BoneName = "replacement", TrailBladeWidth = 0.3f } };
        AssetDatabase.CreateAsset(replacement, fixture.Root(fixture.First) + "/trail-replacement.asset");
        try
        {
            foreach (string conflict in new[] { "binding", "reorder", "value" })
            foreach (bool redo in new[] { false, true })
            {
                EditorJsonUtility.FromJsonOverwrite(original, config);
                config.Entries[0].TrailStylePrefab = fixture.Prop;
                config.Entries = config.Entries.Concat(new[]
                {
                    new WeaponEntry { BoneName = "other", Prefab = fixture.Prop, TrailBladeWidth = 0.2f },
                }).ToArray();
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
                var window = fixture.Window();
                try
                {
                    window.CreateGUI();
                    using var panel = (IPanel)typeof(AbilityLabFrontendSelfTest)
                        .GetMethod("CreateHiddenEditorPanel", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, new object[] { window });
                    Require(window.TryOpenPackage(fixture.First), "Trail history fixture did not open.");
                    var workspace = window.CommandWorkspace;
                    window.rootVisualElement.Q<Slider>("trail-blade-width-0").value = 0.6f;
                    window.rootVisualElement.Q<Slider>("trail-blade-width-0").value = 0.4f;
                    Require(config.Entries[0].TrailBladeWidth == 0.4f && workspace.CanUndo,
                        "Actual trail control did not persist its edits.");
                    Click("toolbar-undo");
                    Require(config.Entries[0].TrailBladeWidth == 0.6f, "Normal trail Undo lost the preceding workspace write.");
                    Click("toolbar-undo");
                    Require(config.Entries[0].TrailBladeWidth == 0.8f, "Second trail Undo did not reach the initial width.");
                    Click("toolbar-redo");
                    Require(config.Entries[0].TrailBladeWidth == 0.6f, "Normal trail Redo did not restore the first edit.");
                    Click("toolbar-redo");
                    Require(config.Entries[0].TrailBladeWidth == 0.4f, "Second trail Redo lost the later edit.");
                    if (redo) Click("toolbar-undo");
                    string expected = EditorJsonUtility.ToJson(config);
                    if (conflict == "binding") catalog.WeaponConfig = replacement;
                    else if (conflict == "reorder") config.Entries = config.Entries.Reverse().ToArray();
                    else config.Entries[0].TrailBladeWidth = 0.9f;
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                    string[] paths = { AssetDatabase.GetAssetPath(config), AssetDatabase.GetAssetPath(replacement),
                        AssetDatabase.GetAssetPath(catalog), fixture.Root(fixture.First) + "/package.json",
                        fixture.Root(fixture.First) + "/character.json" };
                    var bytes = paths.Select(path => File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path))).ToArray();
                    bool canUndo = workspace.CanUndo, canRedo = workspace.CanRedo;
                    Click(redo ? "toolbar-redo" : "toolbar-undo");
                    Require(workspace.CanUndo == canUndo && workspace.CanRedo == canRedo,
                        "Trail conflict consumed history for " + conflict + ".");
                    for (int index = 0; index < paths.Length; index++)
                        Require(File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), paths[index])).SequenceEqual(bytes[index]),
                            "Trail conflict changed persisted bytes at " + paths[index]);
                    EditorJsonUtility.FromJsonOverwrite(expected, config);
                    catalog.WeaponConfig = config;
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                    Click(redo ? "toolbar-redo" : "toolbar-undo");
                    Require(config.Entries[0].TrailBladeWidth == (redo ? 0.4f : 0.6f),
                        "Retained trail history did not apply to its restored intended entry.");

                    void Click(string name)
                    {
                        var button = window.rootVisualElement.Q<Button>(name);
                        Require(button != null && button.enabledSelf, "Required trail history control is unavailable.");
                        typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                            .Invoke(button.clickable, new object[] { null, 0 });
                    }
                }
                finally
                {
                    catalog.WeaponConfig = config;
                    EditorJsonUtility.FromJsonOverwrite(original, config);
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                    fixture.Close(window);
                }
            }
        }
        finally
        {
            catalog.WeaponConfig = config;
            EditorJsonUtility.FromJsonOverwrite(original, config);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
    }

    private static void VerifyNavigationAndReload(Fixture fixture)
    {
        var window = fixture.Window();
        try
        {
            Require(window.TryOpenPackage(fixture.First), "Isolated window did not open its first package.");
            var workspace = window.CommandWorkspace;
            string diskHash = workspace.LoadedDiskHash;
            workspace.SetDraft(workspace.Draft with { DisplayName = "Recovered first draft" });
            Require(workspace.BeginAttachmentDraft(0) && workspace.SetAttachmentPositionOffset(new Vector3(7, 8, 9)),
                "Window attachment draft did not start.");
            Require(window.hasUnsavedChanges, "Gameplay and attachment changes did not protect window close.");
            Require(!window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.Cancel) &&
                ReferenceEquals(workspace, window.CommandWorkspace) && workspace.Draft.DisplayName == "Recovered first draft",
                "Cancel replaced the selected package or source draft.");
            Require(window.TryOpenPackage(fixture.First, () => throw new InvalidOperationException("Same-package selection asked to leave.")) &&
                ReferenceEquals(workspace, window.CommandWorkspace) && workspace.CanUndo,
                "Same-package selection replaced the draft or Undo history.");
            Require(!window.TryNewPackage(fixture.Third, "Cancelled creation", () => AbilityLabWindow.WorkspaceLeaveChoice.Cancel) &&
                !Directory.Exists(fixture.Full(fixture.Third)), "Cancelled New wrote a package or lost the selected draft.");

            string blockedIdentity = Guid.NewGuid().ToString("N");
            string normalIdentity = Field<string>(window, "_recoveryWindowId");
            SetField(window, "_recoveryWindowId", blockedIdentity);
            string blockedDirectory = window.RecoveryDirectory;
            Directory.CreateDirectory(Path.GetDirectoryName(blockedDirectory)!);
            File.WriteAllText(blockedDirectory, "Checkpoint failure fixture");
            try
            {
                Require(!window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery) &&
                    ReferenceEquals(workspace, window.CommandWorkspace) && !string.IsNullOrEmpty(window.RecoveryError),
                    "Checkpoint failure accepted a package departure.");
            }
            finally
            {
                File.Delete(blockedDirectory);
                SetField(window, "_recoveryWindowId", normalIdentity);
            }

            Require(window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery),
                "Checkpointed navigation did not open the second package.");
            window.CommandWorkspace.SetDraft(window.CommandWorkspace.Draft with { DisplayName = "Recovered second draft" });
            Require(window.TryOpenPackage(fixture.First, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery),
                "Returning to the first package failed.");
            workspace = window.CommandWorkspace;
            Require(workspace.Draft.DisplayName == "Recovered first draft" && workspace.AttachmentDraftDirty &&
                workspace.AttachmentPositionOffset == new Vector3(7, 8, 9) && workspace.LoadedDiskHash == diskHash &&
                File.Exists(Path.Combine(window.RecoveryDirectory, fixture.First + ".json")) &&
                File.Exists(Path.Combine(window.RecoveryDirectory, fixture.Second + ".json")),
                "Switching twice overwrote a package checkpoint or its saved baseline.");
            Require(window.TryReloadPackage(() => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery) &&
                window.CommandWorkspace.Draft.DisplayName == "Recovered first draft" && window.CommandWorkspace.AttachmentDraftDirty,
                "Accepted reload lost the checkpointed source or attachment draft.");

            window.SaveChanges();
            Require(!window.hasUnsavedChanges, "Close-save did not complete EditorWindow's save protocol.");
            string identity = Field<string>(window, "_recoveryWindowId");
            UnityEngine.Object.DestroyImmediate(window);
            window = fixture.Window(identity);
            SetField(window, "_reloadPackageId", fixture.First);
            SetField(window, "_suppressInitialPackage", false);
            string checkpointPath = Path.Combine(window.RecoveryDirectory, fixture.First + ".json");
            string checkpoint = File.ReadAllText(checkpointPath);
            File.WriteAllText(checkpointPath, "invalid recovery");
            Invoke(window, "RestoreWindowRecovery");
            Require(!window.CommandWorkspace.HasPackage && !string.IsNullOrEmpty(window.RecoveryError) &&
                File.ReadAllText(checkpointPath) == "invalid recovery", "Malformed recovery was overwritten or silently opened.");
            File.WriteAllText(checkpointPath, checkpoint);
            Invoke(window, "RestoreWindowRecovery");
            workspace = window.CommandWorkspace;
            Require(workspace.HasPackage && workspace.Draft.DisplayName == "Recovered first draft" &&
                workspace.AttachmentPositionOffset == new Vector3(7, 8, 9) && workspace.IsDirty && !workspace.CanUndo && window.CommandLab == null,
                "Close or serialization restoration lost values or created a preview before recovery.");

            string recovery = workspace.CaptureRecovery();
            string characterPath = Path.Combine(fixture.Full(fixture.First), "character.json");
            File.AppendAllText(characterPath, "\n");
            workspace.Catalog.SampleRate = 30;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
            var entry = workspace.Catalog.WeaponConfig.Entries[0];
            entry.BoneName = "External entry replacement";
            EditorUtility.SetDirty(workspace.Catalog.WeaponConfig);
            AssetDatabase.SaveAssetIfDirty(workspace.Catalog.WeaponConfig);
            string externalSource = File.ReadAllText(characterPath);
            Require(workspace.RestoreRecovery(recovery) && workspace.IsDirty && workspace.PreviewPrerequisitesStale &&
                workspace.AttachmentDraftDirty && workspace.AttachmentPositionOffset == new Vector3(7, 8, 9) &&
                !string.IsNullOrEmpty(workspace.AttachmentDraftError), "Reload conflict discarded recovered data or attachment identity.");
            Require(!workspace.SavePackage() && !workspace.SaveAttachmentDraft() && !workspace.ReplaceCatalogRig(null) &&
                File.ReadAllText(characterPath) == externalSource && entry.BoneName == "External entry replacement" &&
                !workspace.PrepareScenarioPreview(), "Recovered conflict wrote over external data or admitted stale scenarios.");
            Require(window.CheckpointWorkspace() && workspace.CaptureRecovery().Contains("Recovered first draft"),
                "A conflicted draft could not be kept in local recovery.");

            Require(window.TryOpenPackage(fixture.Second, () => AbilityLabWindow.WorkspaceLeaveChoice.KeepRecovery) &&
                window.CommandWorkspace.Draft.DisplayName == "Recovered second draft", "Second package recovery was lost by close or conflict recovery.");
            Require(window.TryReloadPackage(() => AbilityLabWindow.WorkspaceLeaveChoice.Discard) &&
                !window.CommandWorkspace.IsDirty && window.CommandWorkspace.Draft.DisplayName != "Recovered second draft",
                "Explicit discard did not load the saved source.");
        }
        finally { fixture.Close(window); }
    }

    private static void VerifySemanticRenameRecovery(Fixture fixture)
    {
        var workspace = new AbilityLabPackageWorkspace();
        try
        {
            Require(workspace.OpenPackage(fixture.Root(fixture.Second)), "Semantic rename fixture did not open.");
            string originalId = workspace.Draft.Presentation.Idle;
            Require(workspace.RenameSemanticId(originalId, "anim.recovery-id"), "Semantic rename did not edit source and catalog together.");
            string recovery = workspace.CaptureRecovery();
            Require(workspace.RestoreRecovery(recovery) && workspace.Draft.Presentation.Idle == "anim.recovery-id" &&
                workspace.Catalog.Bindings.Any(binding => binding.SemanticId == "anim.recovery-id"), "Coupled rename did not survive recovery.");
            workspace.Catalog.SampleRate = 30;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
            Require(!workspace.DiscardUnsavedChanges() && workspace.Catalog.SampleRate == 30 &&
                workspace.Draft.Presentation.Idle == "anim.recovery-id", "Discard restored a stale catalog over another writer.");
            workspace.Catalog.SampleRate = 60;
            EditorUtility.SetDirty(workspace.Catalog);
            AssetDatabase.SaveAssetIfDirty(workspace.Catalog);
            Require(workspace.ReplaceCatalogRig(fixture.Prop) && workspace.IsDirty && workspace.DiscardUnsavedChanges() &&
                !workspace.IsDirty && workspace.Draft.Presentation.Idle == originalId && workspace.Catalog.Rig == fixture.Prop &&
                workspace.Catalog.Bindings.Any(binding => binding.SemanticId == originalId),
                "Discard lost persisted rig edits or failed to reverse the recovered coupled semantic IDs.");
            Require(workspace.ReplaceCatalogRig(null) && !workspace.IsDirty,
                "A persisted catalog edit falsely reported unsaved gameplay source.");
        }
        finally { workspace.ReleasePreview(); }
    }

    private static void VerifySavedSemanticRename(AbilityLabPackageWorkspace workspace, GameObject savedRigEdit)
    {
        string original = workspace.Draft.Presentation.Idle;
        Require(workspace.RenameSemanticId(original, "anim.saved-rename") && workspace.SavePackage(),
            "Isolated semantic rename Save + Cook failed.");
        workspace.Undo();
        Require(workspace.DiscardUnsavedChanges() && !workspace.IsDirty &&
            workspace.Draft.Presentation.Idle == "anim.saved-rename" &&
            workspace.Catalog.Bindings.Any(binding => binding.SemanticId == "anim.saved-rename") &&
            !workspace.Catalog.Bindings.Any(binding => binding.SemanticId == original),
            "Undo then Discard after Save split the saved source and catalog semantic IDs.");

        string savedName = workspace.Draft.DisplayName;
        workspace.SetDraft(workspace.Draft with { DisplayName = "Ordinary source save" });
        Require(workspace.SavePackage() && workspace.CanUndo, "Ordinary source Save cleared compatible Undo history.");
        workspace.Undo();
        Require(workspace.Draft.DisplayName == savedName && workspace.IsDirty && workspace.DiscardUnsavedChanges() &&
            workspace.Draft.DisplayName == "Ordinary source save", "Ordinary edit Undo or Discard failed across Save.");

        GameObject originalRig = workspace.Catalog.Rig;
        Require(workspace.RenameSemanticId("anim.saved-rename", "anim.saved-rename-with-edit"),
            "Second semantic rename failed.");
        workspace.SetDraft(workspace.Draft with { DisplayName = "Ordinary edit after rename" });
        Require(workspace.SavePackage() && workspace.CanUndo, "Saving a rename removed the ordinary edit made after it.");
        workspace.Undo();
        Require(workspace.Draft.DisplayName == "Ordinary source save" && workspace.IsDirty &&
            workspace.Draft.Presentation.Idle == "anim.saved-rename-with-edit" &&
            workspace.Catalog.Bindings.Any(binding => binding.SemanticId == "anim.saved-rename-with-edit"),
            "Undo of the ordinary edit crossed the saved semantic rename boundary.");
        Require(workspace.ReplaceCatalogRig(savedRigEdit) && workspace.DiscardUnsavedChanges() && !workspace.IsDirty &&
            workspace.Draft.DisplayName == "Ordinary edit after rename" &&
            workspace.Draft.Presentation.Idle == "anim.saved-rename-with-edit" && workspace.Catalog.Rig == savedRigEdit &&
            workspace.Catalog.Bindings.Any(binding => binding.SemanticId == "anim.saved-rename-with-edit"),
            "Discard after rename Save lost unrelated persisted catalog edits or split semantic IDs.");
        Require(workspace.ReplaceCatalogRig(originalRig), "Could not restore the isolated cookable rig after the saved catalog edit.");
    }

    private static AbilityLabWindow VerifyRepeatedReloadCallbacks(Fixture fixture, AbilityLabWindow window)
    {
        var workspace = window.CommandWorkspace;
        workspace.SetDraft(workspace.Draft with { DisplayName = "Repeated reload phase draft" });
        var lab = window.CommandLab ?? throw new InvalidOperationException("Reload fixture Lab is unavailable.");
        lab.SetSlot(CanonicalSlotProjection.All.First(slot => slot.Id == "ground.3"));
        lab.SetStage(0);
        lab.SetTick(2);
        lab.SetAuthoringPhase(AbilityLab.AuthoringPhase.Fire);
        Require(lab.SetPhasePreview(true) && lab.PhaseDurationTicks > 4, "Reload fixture has no nonzero release phase cursor.");
        lab.SetPhaseTick(4);
        lab.Playing = true;
        string identity = Field<string>(window, "_recoveryWindowId");
        string checkpointPath = Path.Combine(window.RecoveryDirectory, fixture.First + ".json");
        Invoke(window, "ReleaseLabBeforeCompilation", (object)null);
        Require(window.CommandLab == null, "Compilation callback retained its Lab.");
        string firstCapture = File.ReadAllText(checkpointPath);
        Invoke(window, "ReleaseLabBeforeReload");
        Require(File.ReadAllText(checkpointPath) == firstCapture, "Before-reload callback overwrote the captured phase cursor.");
        UnityEngine.Object.DestroyImmediate(window);
        Require(File.ReadAllText(checkpointPath) == firstCapture, "OnDisable overwrote the captured phase cursor after Lab destruction.");
        var restored = fixture.Window(identity);
        SetField(restored, "_reloadPackageId", fixture.First);
        SetField(restored, "_suppressInitialPackage", false);
        restored.CreateGUI();
        Invoke(restored, "Update");
        lab = restored.CommandLab ?? throw new InvalidOperationException("Recovered phase Lab is unavailable.");
        Require(restored.CommandWorkspace.Draft.DisplayName == "Repeated reload phase draft" &&
            lab.SelectedSlotId == "ground.3" && lab.StageIndex == 0 && lab.Tick == 2 &&
            lab.PhasePreviewActive && lab.Phase == AbilityLab.AuthoringPhase.Fire && lab.PhaseTick == 4 && lab.Playing,
            "Compilation, before-reload and disable lost recovered slot, tick, phase or playback.");
        return restored;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable _queue;
        private readonly List<AbilityLabWindow> _windows = new();
        private readonly HashSet<string> _recoveryDirectories = new(StringComparer.Ordinal);
        private readonly string _preference = "SlopArena.AbilityLab.Recovery." + Application.dataPath;
        private readonly string _previousPreference;
        private readonly UnityEngine.Object _selection;
        private readonly EditorWindow _focusedWindow;
        private readonly byte[] _roster;
        internal readonly string First = "lab-recovery-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        internal readonly string Second = "lab-recovery-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        internal readonly string Third = "lab-recovery-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        internal GameObject Prop { get; private set; }

        internal Fixture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run Lab recovery tests in Edit Mode.");
            _queue = CharacterCookAssetPostprocessor.PreserveQueueForSelfTest();
            _previousPreference = EditorPrefs.GetString(_preference, "");
            EditorPrefs.DeleteKey(_preference);
            _selection = Selection.activeObject;
            _focusedWindow = EditorWindow.focusedWindow;
            _roster = File.ReadAllBytes(RosterPath);
            var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
            try
            {
                Require(service.NewPackage(First, "Recovery fixture one").Success && service.NewPackage(Second, "Recovery fixture two").Success,
                    "Could not create isolated Lab source fixtures.");
                var prop = new GameObject("Recovery prop");
                try { Prop = PrefabUtility.SaveAsPrefabAsset(prop, Root(First) + "/prop.prefab"); }
                finally { UnityEngine.Object.DestroyImmediate(prop); }
                var config = ScriptableObject.CreateInstance<WeaponAttachConfig>();
                config.Entries = new[] { new WeaponEntry { ShowFromTicks = 6, HiddenBySlot = 11, HiddenBySlotFromTicks = 6, BoneName = "hand", Prefab = Prop } };
                AssetDatabase.CreateAsset(config, Root(First) + "/attachments.asset");
                var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root(First) + "/CharacterAssetCatalog.asset");
                catalog.WeaponConfig = config;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            catch { Dispose(); throw; }
        }

        internal string Root(string packageId) => "Assets/CharacterPackages/" + packageId;
        internal string Full(string packageId) => Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), Root(packageId));
        private string RosterPath => Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "../../content-cooked/roster/manifest.json"));

        internal AbilityLabWindow Window(string identity = null)
        {
            var window = ScriptableObject.CreateInstance<AbilityLabWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            SetField(window, "_suppressInitialPackage", true);
            SetField(window, "_recoveryWindowId", identity ?? Guid.NewGuid().ToString("N"));
            SetField(window, "_reloadPackageId", "");
            SetField(window, "_offerClosedRecovery", false);
            _windows.Add(window);
            _recoveryDirectories.Add(window.RecoveryDirectory);
            return window;
        }

        internal void Close(AbilityLabWindow window)
        {
            if (window == null) return;
            SetField(window, "_savedForClose", true);
            typeof(EditorWindow).GetProperty(nameof(EditorWindow.hasUnsavedChanges))!.SetValue(window, false);
            UnityEngine.Object.DestroyImmediate(window);
        }

        internal AnimationClip MakeCookable()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
            Require(source?.Rig != null, "Installed FightGuy Humanoid rig is required as a read-only fixture source.");
            var originalClip = source.Bindings.FirstOrDefault(binding => binding?.Clip != null)?.Clip;
            Require(originalClip != null, "An installed FightGuy clip is required as a read-only fixture source.");
            var rigInstance = UnityEngine.Object.Instantiate(source.Rig);
            GameObject rig;
            try { rig = PrefabUtility.SaveAsPrefabAsset(rigInstance, Root(First) + "/rig.prefab"); }
            finally { UnityEngine.Object.DestroyImmediate(rigInstance); }
            var clip = UnityEngine.Object.Instantiate(originalClip);
            clip.name = "Recovery clip";
            clip.frameRate = 60f;
            AssetDatabase.CreateAsset(clip, Root(First) + "/clip.anim");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root(First) + "/CharacterAssetCatalog.asset");
            catalog.Rig = rig;
            catalog.WeaponConfig = null;
            foreach (var binding in catalog.Bindings) binding.Clip = clip;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return clip;
        }

        public void Dispose()
        {
            try
            {
                foreach (var window in _windows) Close(window);
                if (_focusedWindow != null) _focusedWindow.Focus();
                Selection.activeObject = _selection;
                foreach (string packageId in new[] { First, Second, Third })
                {
                    AssetDatabase.DeleteAsset(Root(packageId));
                    AssetDatabase.DeleteAsset("Assets/Resources/Generated/CharacterPackages/" + packageId);
                    foreach (string path in new[]
                    {
                        Full(packageId),
                        Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "../../content-cooked", packageId)),
                        Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), CharacterCookOutput.For(packageId).IntermediateDirectory),
                    })
                        if (Directory.Exists(path)) Directory.Delete(path, true);
                }
                foreach (string directory in _recoveryDirectories)
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                if (string.IsNullOrEmpty(_previousPreference)) EditorPrefs.DeleteKey(_preference);
                else EditorPrefs.SetString(_preference, _previousPreference);
                Require(File.ReadAllBytes(RosterPath).SequenceEqual(_roster), "An isolated Lab self-test changed the cooked roster.");
            }
            finally { _queue.Dispose(); }
        }
    }

    private static T Field<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static void SetField(object instance, string name, object value)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void Invoke(object instance, string name, params object[] arguments)
        => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
