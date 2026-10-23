using SlopArena.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

public sealed class CharacterCookAssetPostprocessor : AssetPostprocessor
{
    private static readonly HashSet<string> _pendingPackages = new(StringComparer.Ordinal);
    private static bool _pending;
    private static bool _processing;
    private static double _dueTime;
    private static int _queueRequestCount;
    private static int _selfTestDepth;

    internal static bool Pending => _pending;
    internal static int QueueRequestCount => _queueRequestCount;
    internal static IReadOnlyCollection<string> PendingPackages => _pendingPackages;

    // Self-tests may exercise explicit queue requests, but imports and later
    // Editor frames must not publish those requests after the fixture is restored.
    internal static IDisposable PreserveQueueForSelfTest()
    {
        if (_processing) throw new InvalidOperationException("A package cook is already processing.");
        return new SelfTestQueueScope();
    }

    private sealed class SelfTestQueueScope : IDisposable
    {
        private readonly string[] _packages = _pendingPackages.ToArray();
        private readonly bool _wasPending = _pending;
        private readonly double _due = _dueTime;
        private readonly int _requests = _queueRequestCount;
        private bool _disposed;

        internal SelfTestQueueScope()
        {
            _selfTestDepth++;
            EditorApplication.update -= ProcessQueue;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ResetQueueRequestCount();
            foreach (string packageId in _packages) _pendingPackages.Add(packageId);
            _pending = _wasPending;
            _dueTime = _due;
            _queueRequestCount = _requests;
            _selfTestDepth--;
            if (_selfTestDepth == 0 && _pending) EditorApplication.update += ProcessQueue;
        }
    }

    internal static void ResetQueueRequestCount()
    {
        _queueRequestCount = 0;
        _pending = false;
        _pendingPackages.Clear();
        EditorApplication.update -= ProcessQueue;
    }

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // Isolated certification/release imports must only verify committed cooks.
        // Explicit batchmode Cook commands remain the publication boundary.
        if (_selfTestDepth > 0 || UnityEngine.Application.isBatchMode) return;
        var changed = new List<string>();
        changed.AddRange(importedAssets ?? Array.Empty<string>());
        changed.AddRange(deletedAssets ?? Array.Empty<string>());
        changed.AddRange(movedAssets ?? Array.Empty<string>());
        changed.AddRange(movedFromAssetPaths ?? Array.Empty<string>());
        QueueRecook(FindAffectedPackages(changed));
    }

    public static void QueueRecook() => QueueRecook(DiscoverPackageIds());

    internal static void QueueRecook(IEnumerable<string> packageIds)
    {
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packageId in packageIds ?? Array.Empty<string>())
            if (MatchContentCatalogBuilder.IsStablePackageId(packageId))
            {
                _pendingPackages.Add(packageId);
                requested.Add(packageId);
            }
        if (_pendingPackages.Count == 0) return;
        if (!_pending) _queueRequestCount++;
        _pending = true;
        _dueTime = UnityEditor.EditorApplication.timeSinceStartup + 0.25;
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        foreach (var packageId in requested)
            service.MarkStale(packageId);
        EditorApplication.update -= ProcessQueue;
        if (_selfTestDepth == 0) EditorApplication.update += ProcessQueue;
    }

    internal static IReadOnlyList<string> FindAffectedPackages(IEnumerable<string> paths)
    {
        var changed = new HashSet<string>(
            (paths ?? Array.Empty<string>())
                .Select(UnityCharacterAssetCooker.NormalizeProjectPath)
                .Where(path => !IsGeneratedCookOutput(path)),
            StringComparer.Ordinal);
        if (changed.Count == 0) return Array.Empty<string>();
        var affected = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" }))
        {
            string catalogPath = UnityCharacterAssetCooker.NormalizeProjectPath(AssetDatabase.GUIDToAssetPath(guid));
            var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(catalogPath);
            if (catalog == null || !MatchContentCatalogBuilder.IsStablePackageId(catalog.PackageId)) continue;
            string packageRoot = catalogPath.Substring(0, catalogPath.LastIndexOf('/'));
            bool packageSourceChanged = changed.Contains(packageRoot + "/package.json")
                || changed.Contains(packageRoot + "/character.json")
                || changed.Contains(packageRoot + "/CharacterAssetCatalog.asset")
                || changed.Contains(packageRoot + "/CharacterAssetCatalog.asset.meta");
            var status = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).ReadStatus(catalog.PackageId);
            bool importedDependencyChanged = status.Dependencies != null
                && status.Dependencies.Any(dependency => changed.Contains(UnityCharacterAssetCooker.NormalizeProjectPath(dependency.Identity)));
            if (packageSourceChanged || importedDependencyChanged)
                affected.Add(catalog.PackageId);
        }
        return affected;
    }

    private static IEnumerable<string> DiscoverPackageIds()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" }))
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(guid));
            if (catalog != null && MatchContentCatalogBuilder.IsStablePackageId(catalog.PackageId))
                yield return catalog.PackageId;
        }
    }

    private static void ProcessQueue()
    {
        if (_selfTestDepth > 0 || !_pending || _processing || EditorApplication.timeSinceStartup < _dueTime) return;
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        DrainQueue(packageId => service.Cook("Assets/CharacterPackages/" + packageId));
    }

    private static bool IsGeneratedCookOutput(string path)
    {
        const string root = "Assets/Resources/Generated/CharacterPackages/";
        if (!path.StartsWith(root, StringComparison.Ordinal)) return false;
        int slash = path.IndexOf('/', root.Length);
        if (slash < 0) return false;
        string packageId = path.Substring(root.Length, slash - root.Length);
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId)) return false;
        string output = CharacterCookOutput.For(packageId).GeneratedAssetPath;
        string asset = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
        return asset == output || asset == CharacterAnimationCatalogGenerator.TemporaryPath(output) || asset == output + ".previous";
    }

    private static void DrainQueue(Func<string, CharacterPackageCookResult> cook)
    {
        if (_processing) throw new InvalidOperationException("A package cook is already processing.");
        string[] requested = _pendingPackages.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        _pendingPackages.ExceptWith(requested);
        _pending = false;
        EditorApplication.update -= ProcessQueue;
        _processing = true;
        try
        {
            foreach (string packageId in requested)
            {
                try
                {
                    CharacterPackageCookResult result = cook(packageId);
                    string message = $"[SLOPArena] Automatic cook {packageId}: {(result.Success ? "succeeded" : "failed")}.";
                    foreach (var diagnostic in result.Diagnostics)
                        message += $"\n{diagnostic.Severity} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}";
                    if (!result.Success) UnityEngine.Debug.LogError(message);
                    else if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == "warning")) UnityEngine.Debug.LogWarning(message);
                    else UnityEngine.Debug.Log(message);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SLOPArena] Automatic cook {packageId} failed: {ex}");
                }
            }
        }
        finally
        {
            _processing = false;
            _pending = _pendingPackages.Count > 0;
            EditorApplication.update -= ProcessQueue;
            if (_selfTestDepth == 0 && _pending) EditorApplication.update += ProcessQueue;
        }
    }
}
