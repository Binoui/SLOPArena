using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

/// <summary>Run with the producer injected into an Editor assembly; never builds a player.</summary>
public static class PlaytestReleaseBuilderSelfTest
{
    public static void RunBatch()
    {
        string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        var roster = JObject.Parse(File.ReadAllText(Path.Combine(repository, "content-cooked/roster/manifest.json")));
        foreach (var entry in roster["entries"])
            Run(Path.Combine(repository, "content-cooked", (string)entry["packageId"], "client.bindings"));
        Debug.Log("PLAYTEST_RELEASE_SELFTEST_OK");
    }

    public static string[] Run(string bindingPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Catalog regression requires Edit Mode.");
        VerifyDependencyDiff();
        Refuses(() => PlaytestReleaseBuilder.Build());
        Refuses(() => PlaytestReleaseBuilder.DiagnoseCook());
        string id = "playtest-catalog-test-" + Guid.NewGuid().ToString("N");
        string contentRoot = Path.Combine(Path.GetTempPath(), id);
        string assetRoot = "Assets/Resources/Generated/CharacterPackages/" + id;
        string assetPath = CharacterCookOutput.For(id).GeneratedAssetPath;
        var bindings = JObject.Parse(File.ReadAllText(bindingPath));
        bindings["packageId"] = id;
        var identity = new PlaytestReleaseBuilder.PackageIdentityRow("", id, "", (string)bindings["sourceHash"], "", "");
        var verified = new PlaytestReleaseBuilder.PackageVerification(new[] { identity }, "");
        string fixture = Path.Combine(contentRoot, id, CharacterPackageAssembler.BindingPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture));
        try
        {
            File.WriteAllText(fixture, bindings.ToString());
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            PlaytestReleaseBuilder.GenerateClientCatalogs(contentRoot, verified);
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(assetPath);
            GlobalObjectId.TryParse((string)bindings["rigGlobalObjectId"], out var rigId);
            if (catalog.Rig != GlobalObjectId.GlobalObjectIdentifierToObjectSlow(rigId) || catalog.SourceHash != identity.sourceHash)
                throw new InvalidOperationException("Generated catalog does not resolve the bound rig/source identity.");
            catalog.SourceHash = new string('0', 64);
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            catalog.SourceHash = identity.sourceHash;
            var rig = catalog.Rig;
            catalog.Rig = null;
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            catalog.Rig = rig;
            string duplicate = assetRoot + "/duplicate.asset";
            AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(catalog), duplicate);
            Refuses(() => PlaytestReleaseBuilder.VerifyClientCatalogs(verified));
            AssetDatabase.DeleteAsset(duplicate);
            bindings["rigGlobalObjectId"] = "GlobalObjectId_V1-1-00000000000000000000000000000000-1-0";
            File.WriteAllText(fixture, bindings.ToString());
            Refuses(() => PlaytestReleaseBuilder.GenerateClientCatalogs(contentRoot, verified));
            PlaytestReleaseBuilder.VerifyClientCatalogs(verified);
            return new[] { "missing refused", "bound rig/source resolved", "stale refused", "rigless refused", "duplicate refused", "unresolved binding refused; valid catalog preserved" };
        }
        finally
        {
            AssetDatabase.DeleteAsset(assetRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.Delete(contentRoot, true);
        }
    }

    public static void VerifyDependencyDiff()
    {
        var expected = JArray.Parse(@"[{""Kind"":""asset"",""Identity"":""Assets/shared/tumble.fbx"",
            ""Guid"":""rig"",""DependencyHash"":""warm"",""MetaHash"":""same"",""ImporterSettings"":""same""}]");
        var observed = (JArray)expected.DeepClone();
        if (PlaytestReleaseBuilder.CompareDependencies(expected, observed).Count != 0)
            throw new InvalidOperationException("Identical dependencies produced a correction.");
        observed[0]["DependencyHash"] = "cold";
        var changes = PlaytestReleaseBuilder.CompareDependencies(expected, observed);
        if (changes.Count != 1 || ((JObject)changes[0]["fields"]).Count != 1 ||
            (string)changes[0]["fields"]["DependencyHash"]["expected"] != "warm" ||
            (string)changes[0]["fields"]["DependencyHash"]["observed"] != "cold")
            throw new InvalidOperationException("Cold-import dependency mismatch was not isolated.");
        observed[0]["MetaHash"] = "changed";
        if (PlaytestReleaseBuilder.CompareDependencies(expected, observed)[0]["fields"]["MetaHash"] == null)
            throw new InvalidOperationException("Changed importer metadata was omitted.");
    }

    private static void Refuses(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid client catalog was accepted.");
    }
}
