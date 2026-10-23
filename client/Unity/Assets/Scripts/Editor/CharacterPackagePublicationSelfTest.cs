using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

public static class CharacterPackagePublicationSelfTest
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/SLOPArena/Tests/Character Package Publication Regression")]
    public static void Run()
    {
        RunQueueSelfTest();
        RunPublicationSelfTest();
        RunClipIdentitySelfTest();
        RunDryRunSelfTest();
        Debug.Log("[SLOPArena] Package publication regression self-test passed; isolated fixtures removed.");
    }

    public static void RunQueueSelfTest()
    {
        using (CharacterCookAssetPostprocessor.PreserveQueueForSelfTest())
        {
            CharacterCookAssetPostprocessor.ResetQueueRequestCount();
            CharacterCookAssetPostprocessor.QueueRecook(new[] { "queue-test-b", "queue-test-a" });
            var cooked = new List<string>();
            DrainQueue(id =>
            {
                cooked.Add(id);
                if (id == "queue-test-a")
                    CharacterCookAssetPostprocessor.QueueRecook(new[] { "queue-test-a", "queue-test-c" });
                return QueueResult(id, id != "queue-test-b");
            });
            Require(cooked.SequenceEqual(new[] { "queue-test-a", "queue-test-b" }), "Queue cooked packages outside its sorted pending snapshot.");
            Require(CharacterCookAssetPostprocessor.Pending && CharacterCookAssetPostprocessor.PendingPackages.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(new[] { "queue-test-a", "queue-test-c" }), "Queue dropped a fresh request or requeued a failed cook.");
            cooked.Clear();
            DrainQueue(id => { cooked.Add(id); return QueueResult(id, true); });
            Require(cooked.SequenceEqual(new[] { "queue-test-a", "queue-test-c" }) && !CharacterCookAssetPostprocessor.Pending,
                "Fresh requests were not drained exactly once.");
            cooked.Clear();
            DrainQueue(id => { cooked.Add(id); return QueueResult(id, true); });
            Require(cooked.Count == 0 && CharacterCookAssetPostprocessor.PendingPackages.Count == 0, "Queue entered an automatic recook loop.");
            string generated = CharacterCookOutput.For("fightguy").GeneratedAssetPath;
            Require(CharacterCookAssetPostprocessor.FindAffectedPackages(new[] { generated, generated + ".meta",
                CharacterAnimationCatalogGenerator.TemporaryPath(generated), generated + ".previous" }).Count == 0,
                "Generated output imports scheduled another cook.");
            Require(CharacterCookAssetPostprocessor.FindAffectedPackages(new[] { generated, "Assets/CharacterPackages/fightguy/character.json" }).Contains("fightguy"),
                "Generated import suppression dropped a genuine source change in the same import batch.");
        }
        Debug.Log("[SLOPArena] Package queue regression passed. The queue-test-b error above is injected and expected.");
    }

    public static void RunPublicationSelfTest()
    {
        using (var fixture = new PublicationFixture(false))
        {
            string generated = Path.Combine(fixture.ProjectRoot, CharacterCookOutput.FightGuy.GeneratedAssetPath);
            string temporary = Path.Combine(fixture.ProjectRoot, CharacterAnimationCatalogGenerator.TemporaryPath(CharacterCookOutput.FightGuy.GeneratedAssetPath));
            int installations = 0;
            fixture.Checkpoint(phase =>
            {
                if (phase != "before-install" && phase != "before-roster-install") return;
                Require(!File.Exists(temporary) && !File.Exists(temporary + ".meta"),
                    "Publication retained the temporary catalog identity while installing the final catalog.");
                if (phase != "before-install") return;
                installations++;
                if (installations == 1)
                    Require(!File.Exists(generated) && !File.Exists(generated + ".meta"), "First-publication fixture already has a final catalog identity.");
            });
            Require(fixture.Publish(out _), "First catalog publication failed with absent final asset and metadata.");
            fixture.AssertCommitted();
            byte[] firstMetadata = File.ReadAllBytes(generated + ".meta");
            Require(Encoding.UTF8.GetString(firstMetadata) == "new generated metadata 1", "First publication did not transfer the staged catalog metadata.");
            Require(fixture.Publish(out _), "Recook failed after first catalog publication.");
            fixture.AssertCommitted();
            Require(installations == 2 && fixture.GeneratedCount == 2 && File.ReadAllBytes(generated + ".meta").SequenceEqual(firstMetadata),
                "Recook replaced the existing final catalog identity with its newly generated temporary identity.");
        }
        using (var fixture = new PublicationFixture(false))
        {
            var before = SnapshotTree(fixture.Root);
            fixture.Checkpoint(phase => { if (phase == "after-roster-install") throw new IOException("Injected first-publication verification failure."); });
            Require(!fixture.Publish(out var diagnostics) && !diagnostics.Any(diagnostic => diagnostic.Code == "cook.rollback.failed"),
                "First-publication failure did not roll back its new catalog and metadata.");
            AssertTree(fixture.Root, before);
            fixture.Checkpoint(null);
            Require(fixture.Publish(out _), "First publication could not retry after restoring absent final asset and metadata.");
            fixture.AssertCommitted();
        }
        using (var fixture = new PublicationFixture())
        {
            File.WriteAllText(fixture.RosterPath, "{ invalid roster");
            var before = SnapshotTree(fixture.Root);
            Require(!fixture.Publish(out _), "Malformed roster unexpectedly published.");
            AssertTree(fixture.Root, before);
            Require(fixture.GeneratedCount == 0, "Malformed roster was checked after generating outputs.");
        }
        using (var fixture = new PublicationFixture())
        {
            var before = SnapshotTree(fixture.Root);
            fixture.Checkpoint(phase =>
            {
                if (phase != "before-roster-install") return;
                foreach (string path in Directory.GetFiles(Path.GetDirectoryName(fixture.RosterPath), ".manifest.json.publish-*.tmp")) File.Delete(path);
            });
            Require(!fixture.Publish(out var diagnostics) && diagnostics.Any(diagnostic => diagnostic.Code == "cook.failed"),
                "Injected late roster replacement failure was not reported.");
            AssertTree(fixture.Root, before);
            fixture.Checkpoint(null);
            Require(fixture.Publish(out _), "Cook did not succeed after a fully rolled-back failure.");
            fixture.AssertCommitted();
        }
        using (var fixture = new PublicationFixture())
        {
            var before = SnapshotTree(fixture.Root);
            byte[] concurrentRoster = null;
            fixture.Checkpoint(phase =>
            {
                if (phase != "before-roster-install") return;
                concurrentRoster = Encoding.UTF8.GetBytes(File.ReadAllText(fixture.RosterPath).Replace("unrelated-pin", "concurrent-pin"));
                File.WriteAllBytes(fixture.RosterPath, concurrentRoster);
            });
            Require(!fixture.Publish(out _), "Concurrent roster writer did not reject the prepared preimage.");
            before[Relative(fixture.Root, fixture.RosterPath)] = concurrentRoster;
            AssertTree(fixture.Root, before);
            fixture.Checkpoint(null);
            Require(fixture.Publish(out _), "Retry failed after a roster preimage conflict.");
            fixture.AssertCommitted("concurrent-pin");
        }
        using (var fixture = new PublicationFixture())
        {
            var before = SnapshotTree(fixture.Root);
            fixture.Checkpoint(phase => { if (phase == "after-roster-install") throw new IOException("Injected post-install verification failure."); });
            Require(!fixture.Publish(out _), "Post-install failure unexpectedly committed.");
            AssertTree(fixture.Root, before);
        }
        using (var fixture = new PublicationFixture())
        {
            var before = SnapshotTree(fixture.Root);
            string pose = Path.Combine(fixture.ProjectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin");
            byte[] externalPose = Encoding.UTF8.GetBytes("external pose writer");
            byte[] externalRoster = null;
            fixture.Checkpoint(phase =>
            {
                if (phase != "after-roster-install") return;
                externalRoster = Encoding.UTF8.GetBytes(File.ReadAllText(fixture.RosterPath).Replace("unrelated-pin", "concurrent-pin"));
                File.WriteAllBytes(fixture.RosterPath, externalRoster);
                File.WriteAllBytes(pose, externalPose);
                throw new IOException("Injected competing output writers.");
            });
            Require(!fixture.Publish(out var diagnostics) && diagnostics.Count(diagnostic => diagnostic.Code == "cook.rollback.failed") >= 2,
                "Conflicting owned outputs did not retain independent rollback errors.");
            foreach (var file in before)
            {
                string path = Path.Combine(fixture.Root, file.Key);
                byte[] expected = path == pose ? externalPose : path == fixture.RosterPath ? externalRoster : file.Value;
                Require(File.ReadAllBytes(path).SequenceEqual(expected), "Rollback did not independently preserve/restore " + path);
            }
            string recovery = Path.Combine(fixture.Root, "content-cooked/.fightguy.publish-backup");
            Require(Directory.Exists(recovery) && Directory.GetFiles(recovery, "*", SearchOption.AllDirectories).Length > 0,
                "Failed rollback discarded its recovery evidence.");
            var retained = SnapshotTree(fixture.Root);
            fixture.Checkpoint(null);
            Require(!fixture.Publish(out _), "Retry overwrote unresolved recovery evidence.");
            AssertTree(fixture.Root, retained);
        }
        using (var fixture = new PublicationFixture())
        {
            File.Delete(fixture.RosterPath);
            var before = SnapshotTree(fixture.Root);
            Require(!fixture.Publish(out _), "Missing roster unexpectedly published.");
            AssertTree(fixture.Root, before);
        }
        using (var fixture = new PublicationFixture())
        {
            File.WriteAllText(fixture.RosterPath, "{ \"schemaVersion\" : 1, \"entries\" : [" + fixture.UnrelatedEntry + "] }\n");
            byte[] before = File.ReadAllBytes(fixture.RosterPath);
            Require(fixture.Publish(out _), "Unrostered package could not publish.");
            Require(File.ReadAllBytes(fixture.RosterPath).SequenceEqual(before), "Publishing admitted an unrostered package or rewrote unrelated roster bytes.");
        }
        using (var fixture = new PublicationFixture())
        {
            fixture.Checkpoint(phase =>
            {
                if (phase != "after-roster-install") return;
                string temporary = Path.Combine(fixture.ProjectRoot, CharacterAnimationCatalogGenerator.TemporaryPath(CharacterCookOutput.FightGuy.GeneratedAssetPath));
                File.Delete(temporary);
                Directory.CreateDirectory(temporary);
            });
            Require(fixture.Publish(out var diagnostics) && diagnostics.Any(diagnostic => diagnostic.Code == "cook.cleanup.failed" && diagnostic.Severity == CharacterDiagnosticSeverity.Warning),
                "Cleanup failure turned a verified commit into a failed cook.");
            fixture.AssertCommitted();
        }
        Debug.Log("[SLOPArena] Package publication rollback/conflict/retry regressions passed.");
    }

    public static void RunClipIdentitySelfTest()
    {
        using (CharacterCookAssetPostprocessor.PreserveQueueForSelfTest())
        {
            string packageId = "clip-identity-test-" + Guid.NewGuid().ToString("N");
            string packageRoot = "Assets/CharacterPackages/" + packageId;
            var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
            Require(service.NewPackage(packageId, "Clip identity fixture").Success, "Could not create clip identity fixture.");
            try
            {
                string clipPath = packageRoot + "/multiple.anim";
                var first = new AnimationClip { name = "First" };
                var selected = new AnimationClip { name = "Selected" };
                AssetDatabase.CreateAsset(first, clipPath);
                AssetDatabase.AddObjectToAsset(selected, clipPath);
                EditorUtility.SetDirty(first);
                AssetDatabase.SaveAssetIfDirty(first);
                AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceSynchronousImport);
                selected = AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>().Single(clip => clip.name == "Selected");
                string identity = GlobalObjectId.GetGlobalObjectIdSlow(selected).ToString();
                var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(packageRoot + "/CharacterAssetCatalog.asset");
                string semanticId = catalog.Bindings[0].SemanticId;
                string fullCatalog = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), packageRoot, "CharacterAssetCatalog.asset");
                byte[] before = File.ReadAllBytes(fullCatalog);
                var ambiguous = service.Bind(packageId, semanticId, clipPath);
                Require(!ambiguous.Success && ambiguous.Diagnostics.Any(diagnostic => diagnostic.Code == "binding.asset.ambiguous") &&
                    File.ReadAllBytes(fullCatalog).SequenceEqual(before), "Path-only binding chose an arbitrary clip or changed the catalog.");
                var bound = service.Bind(packageId, semanticId, selected, ExtrapolationMode.Hold);
                Require(bound.Success, "Object binding rejected the selected animation subasset.");
                catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(packageRoot + "/CharacterAssetCatalog.asset");
                Require(GlobalObjectId.GetGlobalObjectIdSlow(catalog.Bindings.Single(binding => binding.SemanticId == semanticId).Clip).ToString() == identity,
                    "Binding persistence changed the selected animation subasset identity.");
            }
            finally { Require(AssetDatabase.DeleteAsset(packageRoot), "Could not remove clip identity fixture."); }
        }
        Debug.Log("[SLOPArena] Selected animation subasset identity regression passed.");
    }

    public static void RunDryRunSelfTest()
    {
        using (CharacterCookAssetPostprocessor.PreserveQueueForSelfTest())
        {
            string project = UnityCharacterAssetCooker.ProjectRoot();
            string repository = Path.GetFullPath(Path.Combine(project, "../.."));
            string[] roots = { Path.Combine(repository, "content-cooked/fightguy"),
                Path.Combine(project, CharacterCookOutput.FightGuy.IntermediateDirectory),
                Path.Combine(project, Path.GetDirectoryName(CharacterCookOutput.FightGuy.GeneratedAssetPath)) };
            var snapshots = roots.Select(SnapshotTree).ToArray();
            string roster = Path.Combine(repository, "content-cooked/roster/manifest.json");
            byte[] rosterBefore = File.ReadAllBytes(roster);
            var dryRun = new CharacterPackageAuthoringService(project).Cook("fightguy", true);
            Require(dryRun.Success && dryRun.DryRun && dryRun.Assembly != null, "Dry-run needs a valid FightGuy rig and installed clips: " +
                string.Join("\n", dryRun.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            for (int i = 0; i < roots.Length; i++) AssertTree(roots[i], snapshots[i]);
            Require(File.ReadAllBytes(roster).SequenceEqual(rosterBefore), "Dry-run wrote roster bytes.");
        }
        Debug.Log("[SLOPArena] Package dry-run output preservation regression passed.");
    }

    private static CharacterPackageCookResult QueueResult(string packageId, bool success)
        => success
            ? CharacterPackageCookResult.CreateSuccess(packageId, "", "", "", "", "", Array.Empty<CharacterDiagnostic>(), null)
            : CharacterPackageCookResult.CreateFailure(packageId, "", "", "", "",
                new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "fixture.failure", packageId, "Expected queue regression failure.") }, null);

    private static void DrainQueue(Func<string, CharacterPackageCookResult> cook)
        => typeof(CharacterCookAssetPostprocessor).GetMethod("DrainQueue", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { cook });

    private sealed class PublicationFixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "sloparena-publication-" + Guid.NewGuid().ToString("N"));
        internal readonly string ProjectRoot;
        internal readonly string RosterPath;
        internal readonly string UnrelatedEntry;
        internal int GeneratedCount;
        private readonly CharacterPackageAuthoringService _service;
        private readonly object _context;
        private readonly CharacterPackageAssemblyResult _assembly;
        private readonly CharacterAssetCookResult _cooked;

        internal PublicationFixture(bool hasGeneratedCatalog = true)
        {
            ProjectRoot = Path.Combine(Root, "client/Unity");
            RosterPath = Path.Combine(Root, "content-cooked/roster/manifest.json");
            string committed = Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "../../content-cooked/fightguy"));
            var files = new[] { "manifest.json", "character.runtime.json", "poses.bin", "client.bindings" }
                .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(committed, name)), StringComparer.Ordinal);
            var verification = CharacterPackageAssembler.Verify(files);
            Require(verification.IsValid, "Publication regression needs a verified committed FightGuy artifact: " +
                string.Join("\n", verification.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            using var manifest = JsonDocument.Parse(files["manifest.json"]);
            JsonElement metadata = manifest.RootElement;
            var payloads = metadata.GetProperty("payloads").EnumerateArray().Select(payload => new CharacterPackagePayloadInfo(
                payload.GetProperty("path").GetString(), payload.GetProperty("sha256").GetString(), payload.GetProperty("size").GetInt64())).ToArray();
            _assembly = (CharacterPackageAssemblyResult)Activator.CreateInstance(typeof(CharacterPackageAssemblyResult), PrivateInstance, null,
                new object[] { files["manifest.json"], files["character.runtime.json"], files["poses.bin"], files["client.bindings"],
                    metadata.GetProperty("sourceHash").GetString(), metadata.GetProperty("cookedContentHash").GetString(),
                    metadata.GetProperty("packageHash").GetString(), payloads, Array.Empty<CharacterDiagnostic>() }, null);
            var source = CharacterPackageSourceCodec.CreateAuthoringReady("fightguy", "Publication fixture", "Self-test", "MIT", "Self-test");
            source = source with { Manifest = source.Manifest with { Version = metadata.GetProperty("version").GetString() } };
            string packageRoot = Path.Combine(ProjectRoot, "Assets/CharacterPackages/fightguy");
            Write(Path.Combine(packageRoot, "package.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeManifest(source.Manifest)));
            Write(Path.Combine(packageRoot, "character.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeCharacter(source.Character)));
            _context = Activator.CreateInstance(typeof(CharacterPackageAuthoringService).GetNestedType("PackageContext", BindingFlags.NonPublic), true);
            SetContext("FullRoot", packageRoot);
            SetContext("ProjectRelativeRoot", "Assets/CharacterPackages/fightguy");
            SetContext("PackageId", "fightguy");
            SetContext("Source", source);
            SetContext("PackageBytes", File.ReadAllBytes(Path.Combine(packageRoot, "package.json")));
            SetContext("CharacterBytes", File.ReadAllBytes(Path.Combine(packageRoot, "character.json")));
            _cooked = new CharacterAssetCookResult { SourceHash = _assembly.SourceHash };
            _service = new CharacterPackageAuthoringService(ProjectRoot);
            typeof(CharacterPackageAuthoringService).GetField("_generateCatalog", PrivateInstance).SetValue(_service,
                (Func<byte[], string, string>)((_, output) =>
                {
                    GeneratedCount++;
                    string temporary = CharacterAnimationCatalogGenerator.TemporaryPath(output);
                    Write(Path.Combine(ProjectRoot, temporary), Encoding.UTF8.GetBytes("new generated catalog"));
                    Write(Path.Combine(ProjectRoot, temporary) + ".meta", Encoding.UTF8.GetBytes("new generated metadata " + GeneratedCount));
                    return temporary;
                }));
            string previous = Path.Combine(Root, "content-cooked/fightguy");
            foreach (string name in files.Keys) Write(Path.Combine(previous, name), Encoding.UTF8.GetBytes("old " + name));
            Write(Path.Combine(previous, "owner-note.txt"), Encoding.UTF8.GetBytes("old package extra file"));
            CharacterCookOutput output = CharacterCookOutput.FightGuy;
            foreach (string name in new[] { output.PoseFileName, output.BindingFileName, output.StatusFileName })
                Write(Path.Combine(ProjectRoot, output.IntermediateDirectory, name), Encoding.UTF8.GetBytes("old intermediate " + name));
            if (hasGeneratedCatalog)
            {
                Write(Path.Combine(ProjectRoot, output.GeneratedAssetPath), Encoding.UTF8.GetBytes("old generated catalog"));
                Write(Path.Combine(ProjectRoot, output.GeneratedAssetPath) + ".meta", Encoding.UTF8.GetBytes("old generated metadata"));
            }
            string hash = new string('1', 64);
            UnrelatedEntry = "{ \"selector\" : \"Bonk\", \"packageId\" : \"bonk\", \"requirement\" : { \"packageId\" : \"bonk\", \"version\" : \"unrelated-pin\", \"cookedContentHash\" : \"" + hash + "\", \"packageHash\" : \"" + hash + "\" } }";
            string target = "{\"selector\":\"FightGuy\",\"packageId\":\"fightguy\",\"requirement\":{\"packageId\":\"fightguy\",\"version\":\"old\",\"cookedContentHash\":\"" + hash + "\",\"packageHash\":\"" + hash + "\"}}";
            Write(RosterPath, Encoding.UTF8.GetBytes("{ \"schemaVersion\" : 1,\n  \"entries\" : [ " + target + ",\n" + UnrelatedEntry + " ] }\n"));
            Write(Path.Combine(Root, "content-cooked/bonk/owner-note.txt"), Encoding.UTF8.GetBytes("unrelated package"));
        }

        private void SetContext(string field, object value) => _context.GetType().GetField(field).SetValue(_context, value);
        internal void Checkpoint(Action<string> action) => typeof(CharacterPackageAuthoringService).GetField("_publicationCheckpoint", PrivateInstance).SetValue(_service, action);

        internal bool Publish(out IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            object[] args = { _context, _cooked, _assembly, null };
            bool published = (bool)typeof(CharacterPackageAuthoringService).GetMethod("Publish", PrivateInstance).Invoke(_service, args);
            diagnostics = (IReadOnlyList<CharacterDiagnostic>)args[3];
            return published;
        }

        internal void AssertCommitted(string unrelatedPin = "unrelated-pin")
        {
            string json = File.ReadAllText(RosterPath);
            var roster = BuiltInRosterManifestCodec.ParseCooked(json);
            var target = roster.ResolvePackage("fightguy");
            Require(target != null && target.Requirement.PackageHash == _assembly.PackageHash && target.Requirement.CookedContentHash == _assembly.CookedContentHash,
                "Successful publication did not update the existing roster pin.");
            Require(json.Contains(UnrelatedEntry.Replace("unrelated-pin", unrelatedPin)), "Publication rewrote unrelated roster bytes.");
            Require(File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/manifest.json")).SequenceEqual(_assembly.ManifestBytes),
                "Successful retry did not install the package.");
            Require(!Directory.Exists(Path.Combine(Root, "content-cooked/.fightguy.publish-backup")), "Successful publication retained a blocking backup.");
            Require(File.ReadAllText(Path.Combine(Root, "content-cooked/bonk/owner-note.txt")) == "unrelated package", "Publication touched an unrelated package.");
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes);
    }

    private static string Relative(string root, string path) => path.Substring(root.Length + 1);
    private static Dictionary<string, byte[]> SnapshotTree(string root)
        => !Directory.Exists(root) ? new Dictionary<string, byte[]>(StringComparer.Ordinal) : Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Relative(root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertTree(string root, Dictionary<string, byte[]> before)
    {
        var after = SnapshotTree(root);
        Require(before.Count == after.Count && before.All(file => after.TryGetValue(file.Key, out var bytes) && file.Value.SequenceEqual(bytes)),
            "Publication changed the fixture's file set or bytes after rejection/rollback: " + root);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
