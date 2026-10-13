using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;

internal static class CharacterAnimationCatalogGenerator
{
    internal static CharacterAnimationCatalog Create(byte[] bindingBytes)
    {
        if (bindingBytes == null || bindingBytes.Length == 0) throw new InvalidOperationException("Binding payload is empty.");
        using JsonDocument document = JsonDocument.Parse(bindingBytes);
        JsonElement root = document.RootElement;
        string rigObjectIdText = root.GetProperty("rigGlobalObjectId").GetString() ?? "";
        if (!GlobalObjectId.TryParse(rigObjectIdText, out GlobalObjectId rigObjectId))
            throw new InvalidOperationException($"Invalid rig global object ID: {rigObjectIdText}");
        var rig = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(rigObjectId) as GameObject;
        if (rig == null) throw new InvalidOperationException($"Could not resolve rig global object ID: {rigObjectIdText}");
        var weaponConfig = ResolveWeaponConfig(root);
        var entries = new List<CharacterAnimationCatalog.AnimationEntry>();
        foreach (JsonElement element in root.GetProperty("animations").EnumerateArray())
        {
            string objectIdText = element.GetProperty("clipGlobalObjectId").GetString() ?? "";
            if (!GlobalObjectId.TryParse(objectIdText, out GlobalObjectId objectId))
                throw new InvalidOperationException($"Invalid clip global object ID: {objectIdText}");
            var clip = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId) as AnimationClip;
            if (clip == null) throw new InvalidOperationException($"Could not resolve clip global object ID: {objectIdText}");
            entries.Add(new CharacterAnimationCatalog.AnimationEntry
            {
                SemanticId = element.GetProperty("semanticId").GetString() ?? "",
                PoseTrackId = element.GetProperty("poseTrackId").GetString() ?? "",
                Clip = clip,
                FrameCount = element.GetProperty("frameCount").GetInt32(),
                SampleRate = element.GetProperty("sampleRate").GetInt32(),
                Extrapolation = (SlopArena.Shared.ExtrapolationMode)element.GetProperty("extrapolation").GetInt32(),
            });
        }
        var presentationEntries = new List<CharacterAnimationCatalog.PresentationEntry>();
        if (root.TryGetProperty("presentations", out JsonElement presentations))
        {
            foreach (JsonElement element in presentations.EnumerateArray())
            {
                string objectIdText = element.GetProperty("prefabGlobalObjectId").GetString() ?? "";
                if (!GlobalObjectId.TryParse(objectIdText, out GlobalObjectId objectId))
                    throw new InvalidOperationException($"Invalid presentation prefab global object ID: {objectIdText}");
                var prefab = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId) as GameObject;
                if (prefab == null)
                    throw new InvalidOperationException($"Could not resolve presentation prefab global object ID: {objectIdText}");
                presentationEntries.Add(new CharacterAnimationCatalog.PresentationEntry
                {
                    SemanticId = element.GetProperty("semanticId").GetString() ?? "",
                    Prefab = prefab,
                });
            }
        }
        presentationEntries.Sort((a, b) => StringComparer.Ordinal.Compare(a.SemanticId, b.SemanticId));
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.PackageId = root.GetProperty("packageId").GetString() ?? "";
        catalog.CatalogSchemaVersion = root.GetProperty("catalogSchemaVersion").GetUInt16();
        catalog.BindingSchemaVersion = root.GetProperty("bindingSchemaVersion").GetUInt16();
        catalog.SampleRate = root.GetProperty("sampleRate").GetInt32();
        catalog.SourceHash = root.GetProperty("sourceHash").GetString() ?? "";
        catalog.Rig = rig;
        catalog.WeaponConfig = weaponConfig;
        catalog.Animations = entries.ToArray();
        catalog.Presentations = presentationEntries.ToArray();
        return catalog;
    }
    private static SlopArena.Client.Entities.WeaponAttachConfig ResolveWeaponConfig(JsonElement root)
    {
        if (!root.TryGetProperty("weaponConfigGlobalObjectId", out var property) ||
            string.IsNullOrEmpty(property.GetString()))
            return null;
        string text = property.GetString() ?? "";
        if (!GlobalObjectId.TryParse(text, out GlobalObjectId objectId))
            throw new InvalidOperationException($"Invalid weapon config global object ID: {text}");
        var config = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId) as SlopArena.Client.Entities.WeaponAttachConfig;
        if (config == null) throw new InvalidOperationException($"Could not resolve weapon config global object ID: {text}");
        return config;
    }

    internal static string Generate(byte[] bindingBytes, string outputAssetPath)
    {
        var generated = Create(bindingBytes);
        string projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        string normalizedOutput = outputAssetPath.Replace('\\', '/');
        string fullOutput = Path.Combine(projectRoot, normalizedOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        string tempPath = TemporaryPath(normalizedOutput);
        string fullTemp = Path.Combine(projectRoot, tempPath);
        if (File.Exists(fullTemp)) AssetDatabase.DeleteAsset(tempPath);
        AssetDatabase.CreateAsset(generated, tempPath);
        EditorUtility.SetDirty(generated);
        AssetDatabase.SaveAssetIfDirty(generated);
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);
        return tempPath;
    }

    internal static string TemporaryPath(string outputPath)
    {
        string normalized = outputPath.Replace('\\', '/');
        return normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - ".asset".Length) + ".tmp.asset"
            : normalized + ".tmp.asset";
    }

    internal static void ReplaceTemporary(string tempPath, string outputPath)
    {
        string normalized = outputPath.Replace('\\', '/');
        string backup = normalized + ".previous";
        if (AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(backup) != null)
            AssetDatabase.DeleteAsset(backup);
        bool hadPrevious = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(normalized) != null;
        if (hadPrevious)
        {
            string backupError = AssetDatabase.MoveAsset(normalized, backup);
            if (!string.IsNullOrEmpty(backupError)) throw new InvalidOperationException(backupError);
        }
        try
        {
            string error = AssetDatabase.MoveAsset(tempPath, normalized);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(normalized));
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(normalized) == null)
                throw new InvalidOperationException("Generated catalog did not import.");
            if (hadPrevious) AssetDatabase.DeleteAsset(backup);
        }
        catch
        {
            if (AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(normalized) != null)
                AssetDatabase.DeleteAsset(normalized);
            if (hadPrevious && AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(backup) != null)
                AssetDatabase.MoveAsset(backup, normalized);
            throw;
        }
    }
}
