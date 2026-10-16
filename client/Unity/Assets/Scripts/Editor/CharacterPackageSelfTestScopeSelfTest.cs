using System;
using System.IO;
using System.Linq;
using SlopArena.Client.Animation;
using SlopArena.EditorTools;
using UnityEditor;
using UnityEngine;

public static class CharacterPackageSelfTestScopeSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Package Fixture Restoration")]
    public static void Run()
    {
        CharacterPackageSelfTestScope.RequireSavedAssets();
        using (CharacterCookAssetPostprocessor.PreserveQueueForSelfTest()) RunChecks();
        Debug.Log("[PackageFixtureRestoration] Success, injected failure, queue state, unrelated draft preservation and unsaved-asset refusal passed.");
    }

    private static void RunChecks()
    {
        string id = "restore-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string project = UnityCharacterAssetCooker.ProjectRoot();
        string assetRoot = "Assets/CharacterPackages/" + id;
        string source = Path.Combine(project, assetRoot, "character.json");
        string catalogPath = assetRoot + "/CharacterAssetCatalog.asset";
        string cooked = Path.GetFullPath(Path.Combine(project, "../../content-cooked", id));
        string cache = Path.Combine(project, CharacterCookOutput.For(id).IntermediateDirectory);
        string generated = CharacterCookOutput.For(id).GeneratedAssetPath;
        string draftRoot = "Assets/SlopArenaSelfTest-" + id;
        try
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(draftRoot));
            string draftPath = draftRoot + "/UnrelatedDraft.asset";
            var unrelated = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
            unrelated.SampleRate = 60;
            AssetDatabase.CreateAsset(unrelated, draftPath);
            byte[] draftBefore = File.ReadAllBytes(Path.Combine(project, draftPath));
            unrelated.SampleRate = 23;
            EditorUtility.SetDirty(unrelated);
            void CheckUnrelatedDraft() => Check(EditorUtility.IsDirty(unrelated) && unrelated.SampleRate == 23 &&
                File.ReadAllBytes(Path.Combine(project, draftPath)).SequenceEqual(draftBefore),
                "Package operation saved or discarded an unrelated asset draft.");

            var service = new CharacterPackageAuthoringService(project);
            var created = service.NewPackage(id, "Restoration Test", "Self-test", "MIT", "Self-test fixture");
            Check(created.Success, "Temporary package creation failed.");
            CheckUnrelatedDraft();
            var originalClip = new AnimationClip { name = "original" };
            var replacement = new AnimationClip { name = "replacement" };
            AssetDatabase.CreateAsset(originalClip, assetRoot + "/original.anim");
            AssetDatabase.CreateAsset(replacement, assetRoot + "/replacement.anim");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
            Check(service.Bind(id, catalog.Bindings[0].SemanticId, assetRoot + "/original.anim").Success,
                "Temporary package binding failed.");
            CheckUnrelatedDraft();
            var workspace = new AbilityLabPackageWorkspace();
            Check(workspace.OpenPackage(id) && workspace.ReplaceCatalogRig(null), "Temporary workspace edit failed.");
            CheckUnrelatedDraft();
            Directory.CreateDirectory(cooked);
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(Path.Combine(cooked, "original.bin"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(cache, "original.bin"), new byte[] { 4, 5, 6 });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(project, generated))!);
            var generatedCatalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
            generatedCatalog.PackageId = id;
            string temporaryGenerated = CharacterAnimationCatalogGenerator.TemporaryPath(generated);
            AssetDatabase.CreateAsset(generatedCatalog, temporaryGenerated);
            CharacterAnimationCatalogGenerator.ReplaceTemporary(temporaryGenerated, generated);
            CheckUnrelatedDraft();
            byte[] sourceBefore = File.ReadAllBytes(source);
            byte[] catalogBefore = File.ReadAllBytes(Path.Combine(project, catalogPath));
            byte[] metaBefore = File.ReadAllBytes(Path.Combine(project, catalogPath + ".meta"));
            byte[] generatedBefore = File.ReadAllBytes(Path.Combine(project, generated));

            CharacterCookAssetPostprocessor.ResetQueueRequestCount();
            CharacterCookAssetPostprocessor.QueueRecook(new[] { id });
            string[] pending = CharacterCookAssetPostprocessor.PendingPackages.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            int requests = CharacterCookAssetPostprocessor.QueueRequestCount;
            foreach (bool fail in new[] { false, true })
            {
                bool caught = false;
                try
                {
                    using var fixture = new CharacterPackageSelfTestScope(id);
                    var changing = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
                    changing.Bindings[0].Clip = replacement;
                    changing.SampleRate = 37;
                    EditorUtility.SetDirty(changing);
                    AssetDatabase.SaveAssetIfDirty(changing);
                    File.WriteAllText(source, "deliberately invalid test source");
                    File.WriteAllBytes(Path.Combine(cooked, "original.bin"), new byte[] { 9 });
                    File.Delete(Path.Combine(cache, "original.bin"));
                    Directory.CreateDirectory(Path.Combine(cooked, "extra"));
                    File.WriteAllText(Path.Combine(cooked, "extra/new.txt"), "test output");
                    File.WriteAllText(Path.Combine(cache, "new.txt"), "test output");
                    var changingGenerated = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(generated);
                    changingGenerated.PackageId = "changed-by-test";
                    EditorUtility.SetDirty(changingGenerated);
                    AssetDatabase.SaveAssetIfDirty(changingGenerated);
                    CharacterCookAssetPostprocessor.ResetQueueRequestCount();
                    CharacterCookAssetPostprocessor.QueueRecook(new[] { id + "-other" });
                    if (fail) throw new InjectedFailure();
                }
                catch (InjectedFailure) { caught = true; }
                Check(caught == fail, "Injected failure was not propagated.");
                Check(File.ReadAllBytes(source).SequenceEqual(sourceBefore), "Source bytes were not restored.");
                Check(File.ReadAllBytes(Path.Combine(project, catalogPath)).SequenceEqual(catalogBefore), "Catalog bytes were not restored.");
                Check(File.ReadAllBytes(Path.Combine(project, catalogPath + ".meta")).SequenceEqual(metaBefore), "Catalog metadata was not restored.");
                Check(File.ReadAllBytes(Path.Combine(project, generated)).SequenceEqual(generatedBefore), "Generated catalog was not restored.");
                Check(AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath).Bindings[0].Clip == originalClip,
                    "Loaded binding did not match the restored catalog.");
                Check(File.ReadAllBytes(Path.Combine(cooked, "original.bin")).SequenceEqual(new byte[] { 1, 2, 3 }) &&
                    File.ReadAllBytes(Path.Combine(cache, "original.bin")).SequenceEqual(new byte[] { 4, 5, 6 }), "Output bytes were not restored.");
                Check(!Directory.Exists(Path.Combine(cooked, "extra")) && !File.Exists(Path.Combine(cache, "new.txt")),
                    "New test outputs leaked.");
                Check(CharacterCookAssetPostprocessor.Pending && CharacterCookAssetPostprocessor.QueueRequestCount == requests &&
                    pending.SequenceEqual(CharacterCookAssetPostprocessor.PendingPackages.OrderBy(x => x, StringComparer.Ordinal)),
                    "Preexisting cook queue was not restored.");
                CheckUnrelatedDraft();
            }

            var dirty = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
            dirty.SampleRate = 19;
            EditorUtility.SetDirty(dirty);
            bool refused = false;
            try { using var unexpected = new CharacterPackageSelfTestScope(id); }
            catch (InvalidOperationException error) { refused = error.Message.Contains("unsaved assets", StringComparison.Ordinal); }
            finally { AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
            Check(refused && File.ReadAllBytes(Path.Combine(project, catalogPath)).SequenceEqual(catalogBefore),
                "Unsaved asset was not refused without saving it.");
            CheckUnrelatedDraft();
        }
        finally
        {
            AssetDatabase.DeleteAsset(assetRoot);
            AssetDatabase.DeleteAsset(draftRoot);
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(generated)!.Replace('\\', '/'));
            if (Directory.Exists(cooked)) Directory.Delete(cooked, true);
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }

    private sealed class InjectedFailure : Exception { }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
