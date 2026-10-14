using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Isolated, manually-invoked CI producer for the Steam Playtest client.</summary>
public static class PlaytestReleaseBuilder
{
    private const string VersionPattern = @"\A[0-9]+\.[0-9]+\.[0-9]+-playtest\.[0-9]+\z";
    private const string RevisionPattern = @"\A[0-9a-f]{40}\z";
    private const string ExecutableName = "SlopArena.exe";
    private static readonly string[] StagedRoots = { "Server", "arenas", "data", "content", "content-cooked" };
    private static readonly string[] PayloadNames =
    {
        CharacterPackageAssembler.ManifestPath,
        CharacterPackageAssembler.RuntimePath,
        CharacterPackageAssembler.PosePath,
        CharacterPackageAssembler.BindingPath,
    };

    /// <summary>Unity -executeMethod entrypoint. Throws on every incomplete or invalid release build.</summary>
    public static void Build()
    {
        if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            throw new InvalidOperationException("Playtest release builds require an isolated batchmode Win64 import.");

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        using var timing = new NativeTimings(repositoryRoot);
        string version = RequiredArgument("-releaseVersion");
        string sourceRevision = RequiredArgument("-releaseSourceRevision");
        string masterEndpoint = RequiredArgument("-releaseMasterEndpoint");
        if (version.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(version, VersionPattern))
            throw new InvalidOperationException("Release version must be X.Y.Z-playtest.N.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(sourceRevision, RevisionPattern))
            throw new InvalidOperationException("Release source revision must be a full lowercase 40-character commit SHA.");
        if (!Uri.TryCreate(masterEndpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(masterEndpoint, SlopArena.Client.ClientSession.DefaultMasterServerUrl, StringComparison.Ordinal))
            throw new InvalidOperationException("Release Master endpoint must exactly match the existing HTTPS ClientSession.DefaultMasterServerUrl.");

        string expectedOutput = Path.Combine(repositoryRoot, "build", "release", "SlopArena-" + version);
        string outputPath = ResolveBoundedArgument(repositoryRoot, OptionalArgument("-releaseOutput") ?? Path.Combine("build", "release", "SlopArena-" + version));
        if (!PathsEqual(outputPath, expectedOutput))
            throw new InvalidOperationException("Release output must be build/release/SlopArena-<version> inside the isolated checkout.");
        string expectedReceipt = Path.Combine(repositoryRoot, "build", "playtest", "client.json");
        string receiptPath = ResolveBoundedArgument(repositoryRoot, OptionalArgument("-releaseReceipt") ?? Path.Combine("build", "playtest", "client.json"));
        if (!PathsEqual(receiptPath, expectedReceipt))
            throw new InvalidOperationException("Release receipt must be build/playtest/client.json inside the isolated checkout.");
        if (File.Exists(outputPath) || Directory.Exists(outputPath) || File.Exists(receiptPath))
            throw new InvalidOperationException("Release output and receipt paths must not exist before this immutable build attempt.");
        RequireSourceRevision(repositoryRoot, sourceRevision);

        string streamingAssets = Path.Combine(projectRoot, "Assets", "StreamingAssets");
        bool hadStreamingAssets = Directory.Exists(streamingAssets);
        bool hadStreamingMeta = File.Exists(streamingAssets + ".meta");
        Directory.CreateDirectory(streamingAssets);
        string tempRoot = Path.Combine(repositoryRoot, "build", ".playtest-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var snapshots = new List<PathSnapshot>();
        string oldBundleVersion = PlayerSettings.bundleVersion;
        BuildTarget target = BuildTarget.StandaloneWindows64;
        var namedTarget = UnityEditor.Build.NamedBuildTarget.Standalone;
        ScriptingImplementation oldBackend = PlayerSettings.GetScriptingBackend(namedTarget);
        BuildReport report = null;
        try
        {
            using (timing.Measure("content-staging"))
            {
                SnapshotAndClearStaging(streamingAssets, tempRoot, snapshots);
                StageContent(repositoryRoot, streamingAssets);
            }
            // Check exact payloads before Unity generates import-only .meta files.
            PackageVerification staged = VerifyPackagedContent(Path.Combine(streamingAssets, "content-cooked"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            BuiltInRosterManifest roster = ReadRoster(repositoryRoot);
            using (timing.Measure("cook-verification"))
            {
                IReadOnlyList<PackageIdentityRow> verifiedRows = VerifyAuthoringRoster(projectRoot, roster);
                RequireSameIdentities(verifiedRows, staged.Identities, "staged cooked content");
                GenerateClientCatalogs(Path.Combine(streamingAssets, "content-cooked"), staged);
            }
            string sharedSourceHash = Sha256(Path.Combine(projectRoot, "Assets", "Plugins", "SlopArena.Shared", "SlopArena.Shared.dll"));
            string steamSourceHash = Sha256(ResolveSteamNativeSource(projectRoot, target));

            string[] scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            if (scenes.Length == 0 || scenes.Any(x => !File.Exists(Path.Combine(projectRoot, x))))
                throw new InvalidOperationException("At least one enabled, existing Unity build scene is required.");

            PlayerSettings.bundleVersion = version;
            PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.Mono2x);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputPath, ExecutableName),
                target = target,
                options = BuildOptions.None,
            };
            using (timing.Measure("player-build"))
                report = BuildPipeline.BuildPlayer(options);
            timing.CaptureBuildSteps(report);
            if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new InvalidOperationException("Windows player build failed: result=" + (report == null ? "no report" : report.summary.result.ToString()) +
                    ", errors=" + (report == null ? "unknown" : report.summary.totalErrors.ToString()) + ".");

            string dataPath = Path.Combine(outputPath, "SlopArena_Data");
            RemoveDeveloperArtifacts(outputPath);
            VerifyStreamingTree(dataPath);
            string packagedContentRoot = Path.Combine(dataPath, "StreamingAssets", "content-cooked");
            PackageVerification packaged = VerifyPackagedContent(packagedContentRoot);
            RequireSameIdentities(staged.Identities, packaged.Identities, "packaged cooked content");
            VerifyExactContentBytes(repositoryRoot, packagedContentRoot);
            VerifyExactArenas(repositoryRoot, Path.Combine(dataPath, "StreamingAssets", "arenas"));
            VerifyEndpointInAssembly(Path.Combine(dataPath, "Managed", "Assembly-CSharp.dll"), masterEndpoint);
            if (!ContainsBytes(File.ReadAllBytes(Path.Combine(dataPath, "globalgamemanagers")), Encoding.UTF8.GetBytes(version)))
                throw new InvalidDataException("Packaged application version does not match the requested release.");
            RequireNonEmpty(Path.Combine(dataPath, "Plugins", "x86_64", "steam_api64.dll"), "Steam native runtime");
            RequireNonEmpty(Path.Combine(dataPath, "Managed", "SlopArena.Shared.dll"), "Shared runtime");
            string sharedHash = Sha256(Path.Combine(dataPath, "Managed", "SlopArena.Shared.dll"));
            string steamHash = Sha256(Path.Combine(dataPath, "Plugins", "x86_64", "steam_api64.dll"));
            if (sharedHash != sharedSourceHash || steamHash != steamSourceHash)
                throw new InvalidDataException("Packaged Shared or Steam native bytes differ from their selected source binaries.");
            string licenses = Path.Combine(outputPath, "LICENSES");
            Directory.CreateDirectory(licenses);
            RequireNonEmpty(Path.Combine(dataPath, "Plugins", "Steamworks.NET.txt"), "Steamworks.NET attribution");
            RequireNonEmpty(Path.Combine(outputPath, ExecutableName), "Windows player executable");
            File.Copy(Path.Combine(repositoryRoot, "CREDITS.md"), Path.Combine(outputPath, "CREDITS.txt"));
            File.Copy(Path.Combine(repositoryRoot, "LICENSE"), Path.Combine(outputPath, "LICENSE.txt"));
            foreach (string name in new[] { "ArchivoBlack-OFL.txt", "SpaceMono-OFL.txt" })
                File.Copy(Path.Combine(projectRoot, "Assets", "Fonts", name), Path.Combine(licenses, name));

            string receipt = SerializeReceipt(version, sourceRevision, masterEndpoint, packaged, sharedHash, steamHash,
                report.summary.result.ToString(), checked((int)report.summary.totalErrors), checked((int)report.summary.totalWarnings));
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
            File.WriteAllText(receiptPath, receipt + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine("PLAYTEST_RELEASE_BUILD_OK version={0} source={1} catalog={2} result={3} errors={4} warnings={5}",
                version, sourceRevision, packaged.CatalogHash, report.summary.result, report.summary.totalErrors, report.summary.totalWarnings);
        }
        catch
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            if (File.Exists(receiptPath)) File.Delete(receiptPath);
            throw;
        }
        finally
        {
            PlayerSettings.bundleVersion = oldBundleVersion;
            PlayerSettings.SetScriptingBackend(namedTarget, oldBackend);
            RestoreStaging(snapshots);
            if (!hadStreamingAssets) DeletePath(streamingAssets);
            if (!hadStreamingMeta) DeletePath(streamingAssets + ".meta");
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }

    /// <summary>Certify committed cooks in an isolated Win64 project without building a player.</summary>
    public static void CertifyCook()
    {
        if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            throw new InvalidOperationException("Cook certification requires an isolated batchmode Win64 import.");
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repository = Path.GetFullPath(Path.Combine(project, "../.."));
        using var timing = new NativeTimings(repository);
        using var phase = timing.Measure("cook-certification");
        RequireSourceRevision(repository, RequiredArgument("-releaseSourceRevision"));
        VerifyAuthoringRoster(project, ReadRoster(repository));
        Debug.Log("PLAYTEST_COOK_CERTIFIED");
    }

    /// <summary>Fresh Win64 diagnostic cook in a disposable project; no player or release archive.</summary>
    public static void DiagnoseCook()
    {
        if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            throw new InvalidOperationException("Cook certification requires an isolated batchmode Win64 import.");
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        using var timing = new NativeTimings(repositoryRoot);
        using var cookTiming = timing.Measure("diagnostic-cook");
        RequireSourceRevision(repositoryRoot, RequiredArgument("-releaseSourceRevision"));
        BuiltInRosterManifest roster = ReadRoster(repositoryRoot);
        var authoring = new CharacterPackageAuthoringService(projectRoot);
        var rows = new List<object>();
        bool failed = false;
        foreach (BuiltInRosterEntry entry in roster.Entries)
        {
            string packageRoot = Path.Combine(repositoryRoot, "content-cooked", entry.PackageId);
            var previous = PayloadNames.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(packageRoot, name)));
            CharacterPackageCookResult cooked = authoring.Cook(entry.PackageId);
            CharacterPackageVerificationResult verified = authoring.Verify(entry.PackageId);
            failed |= !cooked.Success || !verified.Success;
            string copy = Path.Combine(repositoryRoot, "build/playtest/cook-diagnostics/content-cooked", entry.PackageId);
            Directory.CreateDirectory(copy);
            foreach (string name in PayloadNames)
                File.Copy(Path.Combine(packageRoot, name), Path.Combine(copy, name), false);
            rows.Add(new
            {
                packageId = entry.PackageId,
                success = cooked.Success && verified.Success,
                sourceHash = cooked.SourceHash,
                runtimeAndPosesUnchanged = previous[CharacterPackageAssembler.RuntimePath].SequenceEqual(File.ReadAllBytes(Path.Combine(copy, CharacterPackageAssembler.RuntimePath))) &&
                                          previous[CharacterPackageAssembler.PosePath].SequenceEqual(File.ReadAllBytes(Path.Combine(copy, CharacterPackageAssembler.PosePath))),
                payloads = PayloadNames.Select(name => new { path = name, unchanged = previous[name].SequenceEqual(File.ReadAllBytes(Path.Combine(copy, name))) }).ToArray(),
                dependencies = DependencyReport(projectRoot, entry.PackageId,
                    (string)JObject.Parse(Encoding.UTF8.GetString(previous[CharacterPackageAssembler.ManifestPath]))["sourceHash"]),
                diagnostics = cooked.Diagnostics.Concat(verified.Diagnostics).ToArray(),
            });
        }
        string rosterCopy = Path.Combine(repositoryRoot, "build/playtest/cook-diagnostics/content-cooked/roster");
        Directory.CreateDirectory(rosterCopy);
        File.Copy(Path.Combine(repositoryRoot, "content-cooked/roster/manifest.json"), Path.Combine(rosterCopy, "manifest.json"));
        WriteCookReport(projectRoot, "cook-diagnostics/cook.json", rows, !failed);
        if (failed) throw new InvalidDataException("Cold Win64 cook certification failed; inspect the complete roster report.");
        Debug.Log("PLAYTEST_COLD_COOK_OK");
    }

    private static object DependencyReport(string projectRoot, string packageId, string acceptedSourceHash = null)
    {
        string packageRoot = "Assets/CharacterPackages/" + packageId;
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(packageRoot + "/CharacterAssetCatalog.asset");
        var current = catalog == null ? Array.Empty<CharacterCookDependencyRecord>() :
            CharacterCookDependencyTracker.Collect(packageRoot, catalog, Array.Empty<CharacterCookAnimationDefinition>()).ToArray();
        string baseline = Path.GetFullPath(Path.Combine(projectRoot, "../../build/playtest/accepted-cook-status", packageId + ".json"));
        var status = File.Exists(baseline) ? JObject.Parse(File.ReadAllText(baseline)) : null;
        if (acceptedSourceHash == null)
            acceptedSourceHash = (string)JObject.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(projectRoot,
                "../../content-cooked", packageId, "manifest.json"))))["sourceHash"];
        var prior = status != null && (string)status["CookedSourceHash"] == acceptedSourceHash
            ? status["Dependencies"] as JArray : null;
        var observed = JArray.FromObject(current);
        return new { baselineAvailable = prior != null, changes = prior == null ? new JArray() : CompareDependencies(prior, observed), current = observed };
    }

    internal static JArray CompareDependencies(JArray expected, JArray observed)
    {
        var allowed = new HashSet<string>(new[] { "source", "catalog", "catalog-meta", "asset", "importer", "toolchain" }, StringComparer.Ordinal);
        Dictionary<string, JObject> Rows(JArray items) => items.OfType<JObject>()
            .Where(item => allowed.Contains((string)item["Kind"]))
            .ToDictionary(item => (string)item["Kind"] + "|" + (string)item["Identity"] + "|" + (string)item["Guid"], item => item, StringComparer.Ordinal);
        var left = Rows(expected);
        var right = Rows(observed);
        var changes = new JArray();
        foreach (string key in left.Keys.Union(right.Keys).OrderBy(value => value, StringComparer.Ordinal))
        {
            left.TryGetValue(key, out var before);
            right.TryGetValue(key, out var after);
            var fields = new JObject();
            foreach (string field in new[] { "DependencyHash", "MetaHash", "ImporterSettings" })
                if (!JToken.DeepEquals(before?[field], after?[field]))
                    fields[field] = new JObject { ["expected"] = before?[field]?.DeepClone(), ["observed"] = after?[field]?.DeepClone() };
            if (fields.Count > 0)
                changes.Add(new JObject { ["identity"] = key, ["fields"] = fields });
        }
        return changes;
    }

    private static void WriteCookReport(string projectRoot, string relative, IEnumerable<object> rows, bool success)
    {
        string path = Path.GetFullPath(Path.Combine(projectRoot, "../../build/playtest", relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonConvert.SerializeObject(new
        {
            success,
            verifiedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            sourceRevision = OptionalArgument("-releaseSourceRevision"),
            packagesManifestSha256 = Sha256(Path.Combine(projectRoot, "Packages/manifest.json")),
            packageLockSha256 = Sha256(Path.Combine(projectRoot, "Packages/packages-lock.json")),
            packages = rows.ToArray(),
            baselineAuthority = "Local cook status is advisory; committed payloads and native verification decide acceptance.",
        }, Formatting.Indented), new UTF8Encoding(false));
    }

    private sealed class NativeTimings : IDisposable
    {
        private readonly string _path;
        private readonly string _enteredAtUtc = DateTime.UtcNow.ToString("O");
        private readonly List<Phase> _phases = new List<Phase>();
        private object[] _buildSteps = Array.Empty<object>();

        public NativeTimings(string repositoryRoot)
        {
            _path = Path.Combine(repositoryRoot, "build/playtest/native-timings.json");
            Save();
        }

        public IDisposable Measure(string name)
        {
            var phase = new Phase(name, Save);
            _phases.Add(phase);
            Save();
            return phase;
        }

        public void CaptureBuildSteps(BuildReport report)
        {
            _buildSteps = report == null ? Array.Empty<object>() : report.steps.Select(step => (object)new
            {
                phase = step.name,
                durationSeconds = step.duration.TotalSeconds,
            }).ToArray();
            Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new
            {
                schemaVersion = 1, enteredAtUtc = _enteredAtUtc, phases = _phases,
                buildSteps = _buildSteps, buildStepsMayOverlap = true,
            }, Formatting.Indented));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }

        public void Dispose() => Save();

        private sealed class Phase : IDisposable
        {
            public string phase;
            public string startedAtUtc = DateTime.UtcNow.ToString("O");
            public string finishedAtUtc;
            public double? durationSeconds;
            private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
            private readonly Action _save;

            public Phase(string name, Action save) { phase = name; _save = save; }
            public void Dispose()
            {
                durationSeconds = _watch.Elapsed.TotalSeconds;
                finishedAtUtc = DateTime.UtcNow.ToString("O");
                _save();
            }
        }
    }

    internal static string SerializeReceipt(string version, string sourceRevision, string masterEndpoint,
        PackageVerification packaged, string sharedHash, string steamHash, string buildResult, int errors, int warnings)
    {
        return JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["version"] = version,
            ["sourceRevision"] = sourceRevision,
            ["catalogHash"] = packaged.CatalogHash,
            ["masterEndpoint"] = masterEndpoint,
            ["outputRelativePath"] = "build/release/SlopArena-" + version,
            ["packageIdentities"] = packaged.Identities,
            ["sharedSha256"] = sharedHash,
            ["steamApi64Sha256"] = steamHash,
            ["buildResult"] = buildResult,
            ["errors"] = errors,
            ["warnings"] = warnings,
        }, Formatting.Indented);
    }

    /// <summary>Pure packaging/admission check used both before the player build and on its actual copied bytes.</summary>
    internal static PackageVerification VerifyPackagedContent(string contentRoot)
    {
        string root = Path.GetFullPath(contentRoot);
        string rosterPath = Path.Combine(root, "roster", CharacterPackageAssembler.ManifestPath);
        if (!File.Exists(rosterPath)) throw new InvalidDataException("Packaged cooked roster manifest is missing.");
        byte[] rosterBytes = File.ReadAllBytes(rosterPath);
        BuiltInRosterManifest roster = BuiltInRosterManifestCodec.ParseCooked(Encoding.UTF8.GetString(rosterBytes));
        if (roster.SchemaVersion != BuiltInRosterManifest.CurrentSchemaVersion || roster.Entries.Count == 0)
            throw new InvalidDataException("Packaged cooked roster schema or entries are invalid.");

        var expectedRoots = new HashSet<string>(roster.Entries.Select(x => x.PackageId).Concat(new[] { "roster" }), StringComparer.Ordinal);
        foreach (string dir in Directory.GetDirectories(root))
            if (!expectedRoots.Contains(Path.GetFileName(dir)))
                throw new InvalidDataException("Unexpected cooked-content directory: " + Path.GetFileName(dir));
        foreach (string file in Directory.GetFiles(root))
            throw new InvalidDataException("Unexpected cooked-content root file: " + Path.GetFileName(file));
        if (!Directory.GetDirectories(root).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(expectedRoots))
            throw new InvalidDataException("Cooked content is missing one or more roster package directories.");
        if (Directory.GetDirectories(Path.Combine(root, "roster")).Length != 0 ||
            !Directory.GetFiles(Path.Combine(root, "roster")).Select(Path.GetFileName).SequenceEqual(new[] { CharacterPackageAssembler.ManifestPath }, StringComparer.Ordinal))
            throw new InvalidDataException("Roster staging must contain only manifest.json.");

        var loaded = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
        foreach (BuiltInRosterEntry entry in roster.Entries)
        {
            if (!MatchContentCatalogBuilder.IsStablePackageId(entry.PackageId) || entry.Requirement == null ||
                !string.Equals(entry.PackageId, entry.Requirement.PackageId, StringComparison.Ordinal))
                throw new InvalidDataException("Cooked roster contains an invalid package identity.");
            string packageRoot = Path.Combine(root, entry.PackageId);
            if (Directory.GetDirectories(packageRoot).Length != 0 ||
                !Directory.GetFiles(packageRoot).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(PayloadNames))
                throw new InvalidDataException("Cooked package must contain exactly the four canonical payload files: " + entry.PackageId);
            var package = CookedCharacterPackageLoader.LoadDirectory(packageRoot, entry.Requirement);
            if (!package.IsValid) throw new InvalidDataException("Cooked package failed Shared admission: " + entry.PackageId + ": " + Diagnostics(package.Diagnostics));
            loaded.Add(entry.PackageId, package);
        }

        MatchContentCatalogBuildResult built = new MatchContentCatalogBuilder().Build(roster, loaded);
        if (!built.IsValid || built.Catalog == null)
            throw new InvalidDataException("Cooked roster failed canonical Shared catalog admission: " + Diagnostics(built.Diagnostics));
        var identities = built.Catalog.Entries.Select(entry =>
        {
            if (!entry.LegacySelector.HasValue) throw new InvalidDataException("Canonical roster entry has no selector.");
            return new PackageIdentityRow(entry.LegacySelector.Value.ToString(), entry.Identity.PackageId, entry.Identity.Version,
                entry.Identity.SourceHash, entry.Identity.CookedContentHash, entry.Identity.PackageHash);
        }).OrderBy(x => x.packageId, StringComparer.Ordinal).ToArray();
        var mapRows = built.Catalog.Entries.Select(entry => new MatchContentHandleRecord(
            entry.Handle, entry.LegacySelector ?? CharacterClass.None, entry.Identity, entry.DisplayName)).ToArray();
        var handleMap = new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, mapRows);
        return new PackageVerification(identities, SteamMatchDescriptor.HashContent(handleMap));
    }

    internal static void GenerateClientCatalogs(string contentRoot, PackageVerification verified)
    {
        foreach (PackageIdentityRow identity in verified.Identities)
        {
            string bindingPath = Path.Combine(contentRoot, identity.packageId, CharacterPackageAssembler.BindingPath);
            string assetPath = CharacterCookOutput.For(identity.packageId).GeneratedAssetPath;
            string temporary = CharacterAnimationCatalogGenerator.Generate(File.ReadAllBytes(bindingPath), assetPath);
            CharacterAnimationCatalogGenerator.ReplaceTemporary(temporary, assetPath);
        }
        VerifyClientCatalogs(verified);
    }

    internal static void VerifyClientCatalogs(PackageVerification verified)
    {
        foreach (PackageIdentityRow identity in verified.Identities)
        {
            // Persisted Resources only: Editor development catalogs must not satisfy a release gate.
            var catalogs = Resources.LoadAll<CharacterAnimationCatalog>("Generated/CharacterPackages/" + identity.packageId);
            if (catalogs.Length != 1 || catalogs[0] == null ||
                catalogs[0].PackageId != identity.packageId || catalogs[0].SourceHash != identity.sourceHash ||
                catalogs[0].Rig == null)
                throw new InvalidDataException("Generated client catalog is missing, ambiguous, stale or rigless: " + identity.packageId);
        }
    }

    private static IReadOnlyList<PackageIdentityRow> VerifyAuthoringRoster(string projectRoot, BuiltInRosterManifest roster)
    {
        var authoring = new CharacterPackageAuthoringService(projectRoot);
        var rows = new List<PackageIdentityRow>(roster.Entries.Count);
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (BuiltInRosterEntry entry in roster.Entries)
        {
            CharacterPackageVerificationResult verified = authoring.Verify(entry.PackageId);
            reports.Add(new
            {
                packageId = entry.PackageId,
                success = verified.Success,
                expectedSourceHash = verified.Inspection?.CookedSourceHash,
                observedSourceHash = verified.Plan?.SourceHash,
                expectedPackageHash = entry.Requirement.PackageHash,
                observedPackageHash = verified.Plan?.PackageHash,
                dependencies = DependencyReport(projectRoot, entry.PackageId),
                diagnostics = verified.Diagnostics,
            });
            if (!verified.Success || verified.Inspection == null || verified.Plan == null || !verified.Plan.DryRun ||
                !verified.Inspection.Rostered || !string.Equals(verified.Inspection.RosterSelector, entry.Selector.ToString(), StringComparison.Ordinal) ||
                !string.Equals(verified.Plan.PackageId, entry.PackageId, StringComparison.Ordinal) ||
                !string.Equals(verified.Plan.CookedContentHash, entry.Requirement.CookedContentHash, StringComparison.Ordinal) ||
                !string.Equals(verified.Plan.PackageHash, entry.Requirement.PackageHash, StringComparison.Ordinal) ||
                !string.Equals(verified.Inspection.Provenance?.Version, entry.Requirement.Version, StringComparison.Ordinal))
                failures.Add(entry.PackageId + ": " + string.Join("; ", verified.Diagnostics.Select(x => x.Code + " " + x.Path + ": " + x.Message)));
            else
                rows.Add(new PackageIdentityRow(entry.Selector.ToString(), entry.PackageId, entry.Requirement.Version,
                    verified.Plan.SourceHash, verified.Plan.CookedContentHash, verified.Plan.PackageHash));
        }
        WriteCookReport(projectRoot, "cook-verification.json", reports, failures.Count == 0);
        if (failures.Count != 0)
            throw new InvalidDataException("Authoring freshness/deterministic-cook verification failed: " + string.Join("; ", failures));
        return rows.OrderBy(x => x.packageId, StringComparer.Ordinal).ToArray();
    }

    private static BuiltInRosterManifest ReadRoster(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        if (!File.Exists(path)) throw new InvalidDataException("Authoritative cooked roster is missing: " + path);
        return BuiltInRosterManifestCodec.ParseCooked(File.ReadAllText(path, Encoding.UTF8));
    }

    private static void RequireSameIdentities(IReadOnlyList<PackageIdentityRow> expected, IReadOnlyList<PackageIdentityRow> actual, string context)
    {
        string left = JsonConvert.SerializeObject(expected);
        string right = JsonConvert.SerializeObject(actual);
        if (!string.Equals(left, right, StringComparison.Ordinal))
            throw new InvalidDataException("Package identities differ in " + context + ".");
    }

    private static void StageContent(string repositoryRoot, string streamingAssets)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "content-cooked");
        BuiltInRosterManifest roster = ReadRoster(repositoryRoot);
        string targetRoot = Path.Combine(streamingAssets, "content-cooked");
        Directory.CreateDirectory(Path.Combine(targetRoot, "roster"));
        File.Copy(Path.Combine(sourceRoot, "roster", CharacterPackageAssembler.ManifestPath),
            Path.Combine(targetRoot, "roster", CharacterPackageAssembler.ManifestPath));
        foreach (BuiltInRosterEntry entry in roster.Entries)
        {
            if (!MatchContentCatalogBuilder.IsStablePackageId(entry.PackageId))
                throw new InvalidDataException("Cooked roster contains an unsafe package ID.");
            string sourcePackage = Path.Combine(sourceRoot, entry.PackageId);
            if (!Directory.Exists(sourcePackage)) throw new InvalidDataException("Cooked roster package directory is missing: " + entry.PackageId);
            string destination = Path.Combine(targetRoot, entry.PackageId);
            Directory.CreateDirectory(destination);
            foreach (string name in PayloadNames)
            {
                string source = Path.Combine(sourcePackage, name);
                if (!File.Exists(source)) throw new InvalidDataException("Cooked package payload is missing: " + entry.PackageId + "/" + name);
                File.Copy(source, Path.Combine(destination, name));
            }
        }
        string arenaSource = Path.Combine(repositoryRoot, "data", "arenas");
        string[] arenas = Directory.Exists(arenaSource) ? Directory.GetFiles(arenaSource, "*.arena", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
        if (arenas.Length == 0) throw new InvalidDataException("No cooked .arena files are available for the Windows client.");
        string arenaDestination = Path.Combine(streamingAssets, "arenas");
        Directory.CreateDirectory(arenaDestination);
        foreach (string arena in arenas) File.Copy(arena, Path.Combine(arenaDestination, Path.GetFileName(arena)));
    }

    private static void VerifyExactContentBytes(string repositoryRoot, string packagedRoot)
    {
        string source = Path.Combine(repositoryRoot, "content-cooked");
        RequireEqualFiles(Path.Combine(source, "roster", CharacterPackageAssembler.ManifestPath),
            Path.Combine(packagedRoot, "roster", CharacterPackageAssembler.ManifestPath));
        BuiltInRosterManifest roster = ReadRoster(repositoryRoot);
        foreach (BuiltInRosterEntry entry in roster.Entries)
            foreach (string name in PayloadNames)
                RequireEqualFiles(Path.Combine(source, entry.PackageId, name), Path.Combine(packagedRoot, entry.PackageId, name));
    }

    private static void VerifyExactArenas(string repositoryRoot, string packagedArenas)
    {
        string source = Path.Combine(repositoryRoot, "data", "arenas");
        string[] expected = Directory.GetFiles(source, "*.arena", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] actual = Directory.GetFiles(packagedArenas, "*.arena", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal)) throw new InvalidDataException("Packaged arena roster differs from the cooked source roster.");
        foreach (string name in expected) RequireEqualFiles(Path.Combine(source, name), Path.Combine(packagedArenas, name));
        if (Directory.GetDirectories(packagedArenas).Length != 0 || Directory.GetFiles(packagedArenas).Length != expected.Length)
            throw new InvalidDataException("Arena staging contains unexpected files or directories.");
    }

    private static void VerifyStreamingTree(string dataPath)
    {
        string streaming = Path.Combine(dataPath, "StreamingAssets");
        if (!Directory.Exists(streaming)) throw new InvalidDataException("Player has no StreamingAssets content.");
        string[] dirs = Directory.GetDirectories(streaming).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!dirs.SequenceEqual(new[] { "arenas", "content-cooked" }, StringComparer.Ordinal) || Directory.GetFiles(streaming).Length != 0)
            throw new InvalidDataException("Packaged StreamingAssets contains developer/server/local content or lacks required cooked content.");
    }

    private static void VerifyEndpointInAssembly(string assemblyPath, string endpoint)
    {
        byte[] bytes = File.ReadAllBytes(assemblyPath);
        if (!ContainsBytes(bytes, Encoding.Unicode.GetBytes(endpoint)))
            throw new InvalidDataException("Built Assembly-CSharp.dll does not contain the approved HTTPS Master endpoint.");
    }

    private static string ResolveSteamNativeSource(string projectRoot, BuildTarget target)
    {
        PluginImporter[] matches = PluginImporter.GetAllImporters().Where(x =>
            x.assetPath.EndsWith("/steam_api64.dll", StringComparison.Ordinal) && x.GetCompatibleWithPlatform(target)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Exactly one Windows64 Steam native plugin must be selected.");
        string assetPath = matches[0].assetPath;
        if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            return Path.Combine(projectRoot, assetPath);
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        if (package == null)
            throw new InvalidDataException("Steam native plugin package cannot be resolved.");
        return Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length + 1));
    }

    private static void RemoveDeveloperArtifacts(string outputPath)
    {
        foreach (string pdb in Directory.GetFiles(outputPath, "*.pdb", SearchOption.AllDirectories)) File.Delete(pdb);
        string[] burstDirs = Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories)
            .Where(x => Path.GetFileName(x).Contains("BurstDebugInformation") || Path.GetFileName(x).EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.Ordinal)).ToArray();
        foreach (string path in burstDirs.OrderByDescending(x => x.Length))
            if (Directory.Exists(path)) Directory.Delete(path, true);
        if (Directory.GetFiles(outputPath, "*.pdb", SearchOption.AllDirectories).Length != 0 ||
            Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories).Any(x =>
                Path.GetFileName(x).Contains("BurstDebugInformation") || Path.GetFileName(x).EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.Ordinal)))
            throw new InvalidDataException("Developer symbols or Burst debug data remain in the release output.");
    }

    private static void RequireSourceRevision(string repositoryRoot, string sourceRevision)
    {
        string head = RunGit(repositoryRoot, "rev-parse HEAD").Trim();
        if (!string.Equals(head, sourceRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("Checked-out source revision does not equal -releaseSourceRevision.");
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using (var process = System.Diagnostics.Process.Start(start))
        {
            if (process == null) throw new InvalidOperationException("Could not start git.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("Git source validation failed: " + error.Trim());
            return output;
        }
    }

    private static void SnapshotAndClearStaging(string streamingAssets, string tempRoot, List<PathSnapshot> snapshots)
    {
        foreach (string name in StagedRoots)
        foreach (string suffix in new[] { "", ".meta" })
        {
            string original = Path.Combine(streamingAssets, name + suffix);
            string backup = Path.Combine(tempRoot, name + suffix);
            bool existed = File.Exists(original) || Directory.Exists(original);
            if (existed)
            {
                if (Directory.Exists(original)) Directory.Move(original, backup);
                else File.Move(original, backup);
            }
            snapshots.Add(new PathSnapshot(original, backup, existed));
        }
    }

    private static void RestoreStaging(IEnumerable<PathSnapshot> snapshots)
    {
        foreach (PathSnapshot snapshot in snapshots.Reverse())
        {
            DeletePath(snapshot.Original);
            if (!snapshot.Existed) continue;
            if (Directory.Exists(snapshot.Backup)) Directory.Move(snapshot.Backup, snapshot.Original);
            else if (File.Exists(snapshot.Backup)) File.Move(snapshot.Backup, snapshot.Original);
        }
    }


    private static void DeletePath(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private static string RequiredArgument(string name)
        => OptionalArgument(name) ?? throw new ArgumentException("Missing required Unity command-line argument " + name + ".");

    private static string OptionalArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        string value = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.Ordinal)) continue;
            if (value != null || i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Invalid or duplicate Unity command-line argument " + name + ".");
            value = args[++i];
        }
        return value;
    }

    private static string ResolveBoundedArgument(string repositoryRoot, string value)
    {
        if (Path.IsPathRooted(value) || value.Contains("\\") || value.Split('/').Any(x => x == ".." || x == "."))
            throw new InvalidOperationException("Release paths must be safe repository-relative paths.");
        string full = Path.GetFullPath(Path.Combine(repositoryRoot, value));
        string root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Release path must remain inside the isolated game checkout.");
        return full;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);

    private static void RequireEqualFiles(string expected, string actual)
    {
        if (!File.Exists(expected) || !File.Exists(actual) || new FileInfo(expected).Length != new FileInfo(actual).Length || Sha256(expected) != Sha256(actual))
            throw new InvalidDataException("Packaged cooked file differs from its validated source: " + Path.GetFileName(expected));
    }

    private static void RequireNonEmpty(string path, string description)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException(description + " is missing or empty: " + path);
    }


    private static string Sha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return string.Concat(sha.ComputeHash(stream).Select(x => x.ToString("x2")));
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            for (; j < needle.Length && haystack[i + j] == needle[j]; j++) { }
            if (j == needle.Length) return true;
        }
        return false;
    }

    private static string Diagnostics(IEnumerable<CharacterDiagnostic> diagnostics)
        => string.Join("; ", diagnostics.Select(x => x.Code + " " + x.Path + ": " + x.Message));


    internal sealed class PackageVerification
    {
        internal readonly IReadOnlyList<PackageIdentityRow> Identities;
        internal readonly string CatalogHash;
        internal PackageVerification(IReadOnlyList<PackageIdentityRow> identities, string catalogHash)
        {
            Identities = identities;
            CatalogHash = catalogHash;
        }
    }

    internal sealed class PackageIdentityRow
    {
        public readonly string selector;
        public readonly string packageId;
        public readonly string version;
        public readonly string sourceHash;
        public readonly string cookedContentHash;
        public readonly string packageHash;
        internal PackageIdentityRow(string selector, string packageId, string version, string sourceHash, string cookedContentHash, string packageHash)
        {
            this.selector = selector;
            this.packageId = packageId;
            this.version = version;
            this.sourceHash = sourceHash;
            this.cookedContentHash = cookedContentHash;
            this.packageHash = packageHash;
        }
    }

    private sealed class PathSnapshot
    {
        internal readonly string Original;
        internal readonly string Backup;
        internal readonly bool Existed;
        internal PathSnapshot(string original, string backup, bool existed) { Original = original; Backup = backup; Existed = existed; }
    }
}
