using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

/// <summary>Restores package test inputs/outputs without a cleanup recook.</summary>
internal sealed class CharacterPackageSelfTestScope : IDisposable
{
    private readonly string _projectRoot;
    private readonly Dictionary<string, byte[]?> _files = new(StringComparer.Ordinal);
    private readonly DirectorySnapshot[] _directories;
    private readonly IDisposable _queue;
    private bool _disposed;

    internal static void RequireSavedAssets(params string[] packageIds)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run package self-tests in Edit Mode.");
        string[] dirty = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
            .Where(asset => EditorUtility.IsPersistent(asset) && AssetDatabase.IsNativeAsset(asset) && EditorUtility.IsDirty(asset))
            .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path))
            .Where(path => path.StartsWith("Assets/Resources/Generated/CharacterPackages/", StringComparison.Ordinal)
                || packageIds.Any(id => path.StartsWith("Assets/CharacterPackages/" + id + "/", StringComparison.Ordinal)))
            .Distinct().OrderBy(path => path, StringComparer.Ordinal).Take(5).ToArray();
        if (dirty.Length > 0)
            throw new InvalidOperationException("Save or revert unsaved assets before package self-tests: " + string.Join(", ", dirty));
    }

    internal CharacterPackageSelfTestScope(params string[] packageIds)
    {
        RequireSavedAssets(packageIds);
        _projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        foreach (string id in packageIds.Distinct(StringComparer.Ordinal))
        {
            if (!MatchContentCatalogBuilder.IsStablePackageId(id)) throw new ArgumentException("Invalid test package ID.");
            foreach (string name in new[] { "package.json", "character.json", "CharacterAssetCatalog.asset" })
            {
                string path = Path.Combine(_projectRoot, "Assets/CharacterPackages", id, name);
                CaptureFile(path);
                CaptureFile(path + ".meta");
            }
        }
        string generated = Path.Combine(_projectRoot, "Assets/Resources/Generated/CharacterPackages");
        CaptureFile(generated + ".meta");
        _directories = new[]
        {
            new DirectorySnapshot(Path.GetFullPath(Path.Combine(_projectRoot, "../../content-cooked"))),
            new DirectorySnapshot(Path.Combine(_projectRoot, "Library/SlopArena/CharacterCook")),
            new DirectorySnapshot(generated),
        };
        _queue = CharacterCookAssetPostprocessor.PreserveQueueForSelfTest();
    }

    private void CaptureFile(string path) => _files[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;

    internal void PrepareCookedPackage(string packageId)
    {
        if (!_files.ContainsKey(Path.Combine(_projectRoot, "Assets/CharacterPackages", packageId, "character.json")))
            throw new ArgumentException("Cook fixture must be included in the preservation scope.");
        var result = new CharacterPackageAuthoringService(_projectRoot).Cook(packageId);
        if (!result.Success)
            throw new InvalidOperationException("Could not prepare " + packageId + " test fixture: " +
                string.Join("; ", result.Diagnostics.Select(d => d.Code + " " + d.Path + ": " + d.Message)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var entry in _files) RestoreFile(entry.Key, entry.Value);
            foreach (var directory in _directories) directory.Restore();
            // Force reimport of restored catalogs, not only a timestamp refresh:
            // bindings loaded into memory must match the restored bytes too.
            foreach (string path in _files.Keys.Concat(_directories.SelectMany(d => d.Files.Keys)))
                if (path.StartsWith(_projectRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && File.Exists(path) && (path.EndsWith(".asset", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal)))
                    AssetDatabase.ImportAsset(path.Substring(_projectRoot.Length + 1).Replace('\\', '/'),
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var entry in _files)
                if (!SameFile(entry.Key, entry.Value)) throw new IOException("Package self-test did not restore " + entry.Key);
            foreach (var directory in _directories) directory.AssertRestored();
        }
        finally { _queue.Dispose(); }
    }

    private static bool SameFile(string path, byte[]? bytes)
        => bytes == null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes);

    private static void RestoreFile(string path, byte[]? bytes)
    {
        if (SameFile(path, bytes)) return;
        if (bytes == null) File.Delete(path);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    private sealed class DirectorySnapshot
    {
        private readonly string _root;
        private readonly bool _existed;
        private readonly HashSet<string> _folders;
        internal readonly Dictionary<string, byte[]> Files;

        internal DirectorySnapshot(string root)
        {
            _root = root;
            _existed = Directory.Exists(root);
            _folders = new HashSet<string>(_existed ? Directory.GetDirectories(root, "*", SearchOption.AllDirectories) : Array.Empty<string>(), StringComparer.Ordinal);
            Files = (_existed ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : Array.Empty<string>())
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        }

        internal void Restore()
        {
            if (!_existed)
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
                return;
            }
            Directory.CreateDirectory(_root);
            if (Directory.Exists(_root))
                foreach (string path in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                    if (!Files.ContainsKey(path)) File.Delete(path);
            foreach (var file in Files) RestoreFile(file.Key, file.Value);
            foreach (string folder in _folders) Directory.CreateDirectory(folder);
            foreach (string folder in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
                if (!_folders.Contains(folder)) Directory.Delete(folder);
        }

        internal void AssertRestored()
        {
            if (Directory.Exists(_root) != _existed || (_existed &&
                (Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length != Files.Count ||
                 !_folders.SetEquals(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories)))))
                throw new IOException("Package self-test output set was not restored: " + _root);
            foreach (var file in Files)
                if (!SameFile(file.Key, file.Value)) throw new IOException("Package self-test output was not restored: " + file.Key);
        }
    }
}
