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
        if (_selfTestDepth > 0) return;
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
        if (_processing) return;
        foreach (var packageId in packageIds ?? Array.Empty<string>())
            if (MatchContentCatalogBuilder.IsStablePackageId(packageId))
                _pendingPackages.Add(packageId);
        if (_pendingPackages.Count == 0) return;
        if (!_pending) _queueRequestCount++;
        _pending = true;
        _dueTime = UnityEditor.EditorApplication.timeSinceStartup + 0.25;
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        foreach (var packageId in _pendingPackages.ToArray())
            service.MarkStale(packageId);
        EditorApplication.update -= ProcessQueue;
        if (_selfTestDepth == 0) EditorApplication.update += ProcessQueue;
    }

    internal static IReadOnlyList<string> FindAffectedPackages(IEnumerable<string> paths)
    {
        var changed = new HashSet<string>(
            (paths ?? Array.Empty<string>())
                .Select(UnityCharacterAssetCooker.NormalizeProjectPath),
            StringComparer.Ordinal);
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
        _processing = true;
        try
        {
            foreach (string packageId in DiscoverPackageIds())
            {
                string packageRoot = "Assets/CharacterPackages/" + packageId;
                new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(packageRoot);
            }
        }
        finally
        {
            _pendingPackages.Clear();
            _pending = false;
            _processing = false;
            EditorApplication.update -= ProcessQueue;
        }
    }
}
