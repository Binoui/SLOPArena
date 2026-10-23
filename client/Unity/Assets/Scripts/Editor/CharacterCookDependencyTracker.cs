using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using System.Text.Json;
using UnityEngine;
using SlopArena.Client.Animation;

internal static class CharacterCookDependencyTracker
{
    internal static IReadOnlyList<CharacterCookDependencyRecord> Collect(
        string packageRoot,
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterCookAnimationDefinition> animations)
    {
        var records = new List<CharacterCookDependencyRecord>();
        string packagePath = UnityCharacterAssetCooker.NormalizeProjectPath(UnityCharacterAssetCooker.ResolveFile(packageRoot, "package.json"));
        string characterPath = UnityCharacterAssetCooker.NormalizeProjectPath(UnityCharacterAssetCooker.ResolveFile(packageRoot, "character.json"));
        AddFile(records, "source", packagePath);
        AddFile(records, "source", characterPath);
        string catalogPath = UnityCharacterAssetCooker.NormalizeProjectPath(AssetDatabase.GetAssetPath(catalog));
        AddFile(records, "catalog", catalogPath);
        AddFile(records, "catalog-meta", catalogPath + ".meta");

        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(catalogPath))
        {
            paths.Add(catalogPath);
            foreach (string dependency in AssetDatabase.GetDependencies(catalogPath, true)) paths.Add(UnityCharacterAssetCooker.NormalizeProjectPath(dependency));
        }
        foreach (var animation in animations)
        {
            if (!string.IsNullOrEmpty(animation.ClipAssetPath)) paths.Add(animation.ClipAssetPath);
            if (!string.IsNullOrEmpty(animation.ClipAssetPath))
                foreach (string dependency in AssetDatabase.GetDependencies(animation.ClipAssetPath, true))
                    paths.Add(UnityCharacterAssetCooker.NormalizeProjectPath(dependency));
            CharacterPackageDependencyInfo classification = CharacterPackageAuthoringService.ClassifyDependency(packageRoot, animation.ClipAssetPath);
            records.Add(new CharacterCookDependencyRecord
            {
                Kind = "clip-object",
                Identity = animation.SemanticId,
                Guid = animation.ClipGlobalObjectId,
                DependencyHash = animation.ClipAssetGuid,
                MetaHash = animation.ClipAssetPath,
                Classification = classification.Classification,
                SourcePackageId = classification.SourcePackageId,
                SourcePath = classification.SourcePath,
                ApprovalReason = classification.ApprovalReason,
                ApprovalVersion = classification.ApprovalVersion,
            });
        }
        foreach (var presentation in catalog?.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
        {
            string path = presentation?.Prefab == null
                ? ""
                : UnityCharacterAssetCooker.NormalizeProjectPath(AssetDatabase.GetAssetPath(presentation.Prefab));
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
            records.Add(new CharacterCookDependencyRecord
            {
                Kind = "presentation-object",
                Identity = presentation?.SemanticId ?? "",
                Guid = presentation?.Prefab == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(presentation.Prefab).ToString(),
                DependencyHash = presentation?.Prefab == null ? "" : AssetDatabase.AssetPathToGUID(path),
                MetaHash = path,
                Classification = presentation?.Prefab == null ? "missing" : CharacterPackageAuthoringService.ClassifyDependency(packageRoot, path).Classification,
            });
        }
        foreach (var media in CharacterBindingWriter.ProfileMedia(catalog.PresentationProfile))
        {
            string path = media.Asset == null ? "" : UnityCharacterAssetCooker.NormalizeProjectPath(AssetDatabase.GetAssetPath(media.Asset));
            if (!string.IsNullOrEmpty(path))
                foreach (string dependency in AssetDatabase.GetDependencies(path, true)) paths.Add(UnityCharacterAssetCooker.NormalizeProjectPath(dependency));
            var classification = CharacterPackageAuthoringService.ClassifyDependency(packageRoot, path);
            records.Add(new CharacterCookDependencyRecord
            {
                Kind = "profile-media", Identity = media.Identity, Guid = media.ObjectId, MetaHash = path,
                Classification = classification.Classification, SourcePackageId = classification.SourcePackageId,
                SourcePath = classification.SourcePath, ApprovalReason = classification.ApprovalReason, ApprovalVersion = classification.ApprovalVersion,
            });
        }
        foreach (string path in paths.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (path == catalogPath) continue;
            AddAsset(records, "asset", path, packageRoot);
            if (Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase) || AssetImporter.GetAtPath(path) is AudioImporter)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null)
                    records.Add(new CharacterCookDependencyRecord
                    {
                        Kind = "importer",
                        Identity = path,
                        Guid = AssetDatabase.AssetPathToGUID(path),
                        DependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                        ImporterSettings = EditorJsonUtility.ToJson(importer),
                    });
            }
        }
        AddFile(records, "toolchain", "Packages/manifest.json");
        AddFile(records, "toolchain", "Packages/packages-lock.json");
        records.Add(new CharacterCookDependencyRecord
        {
            Kind = "toolchain",
            Identity = "UnityEditor",
            DependencyHash = Application.unityVersion,
            ImporterSettings = "Animancer=" + FindAnimancerIdentity(),
        });
        return records
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Identity, StringComparer.Ordinal)
            .ThenBy(x => x.Guid, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string ComputeSourceHash(
        string packageJson,
        string characterJson,
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterCookDependencyRecord> dependencies,
        IReadOnlyList<CharacterCookAnimationDefinition> animations)
    {
        var canonical = new StringBuilder();
        canonical.Append("cookerVersion=").Append(UnityCharacterAssetCooker.CookerVersion).Append('\n');
        canonical.Append("catalogSchemaVersion=").Append(CharacterAssetCatalog.SchemaVersion).Append('\n');
        canonical.Append("bindingSchemaVersion=").Append(UnityCharacterAssetCooker.BindingSchemaVersion).Append('\n');
        canonical.Append("poseFormat=SKEL;poseVersion=").Append(UnityCharacterAssetCooker.PoseVersion)
            .Append(";sampleRate=").Append(UnityCharacterAssetCooker.SampleRate).Append('\n');
        canonical.Append("package=").Append(CanonicalJson(packageJson)).Append('\n');
        canonical.Append("character=").Append(CanonicalJson(characterJson)).Append('\n');
        canonical.Append("rig=").Append(GlobalObjectId.GetGlobalObjectIdSlow(catalog.Rig)).Append('\n');
        canonical.Append("weapon-config=").Append(catalog.WeaponConfig == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(catalog.WeaponConfig).ToString()).Append('\n');
        foreach (var animation in animations.OrderBy(x => x.SemanticId, StringComparer.Ordinal))
        {
            canonical.Append("catalog-animation=").Append(animation.SemanticId).Append('|')
                .Append(animation.PoseTrackId).Append('|').Append(animation.ClipGlobalObjectId).Append('|')
                .Append(animation.ClipAssetGuid).Append('|').Append(animation.ClipLengthBits).Append('|')
                .Append(animation.FrameCount).Append('|').Append((int)animation.Extrapolation).Append('\n');
        }
        foreach (var presentation in catalog?.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            canonical.Append("catalog-presentation=").Append(presentation?.SemanticId ?? "").Append('|')
                .Append(presentation?.Prefab == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(presentation.Prefab).ToString()).Append('\n');
        canonical.Append("catalog-profile=").Append(CharacterBindingWriter.ProfileJson(catalog.PresentationProfile)).Append('\n');
        canonical.Append("ownership-registry=").Append(CharacterPackageAssetOwnershipRegistry.RegistryVersion).Append('\n');
        foreach (var dependency in dependencies)
        {
            canonical.Append("dependency=").Append(dependency.Kind).Append('|').Append(dependency.Identity).Append('|')
                .Append(dependency.Guid).Append('|').Append(dependency.DependencyHash).Append('|')
                .Append(dependency.MetaHash).Append('|').Append(dependency.ImporterSettings).Append('|')
                .Append(dependency.Classification).Append('|').Append(dependency.SourcePackageId).Append('|')
                .Append(dependency.SourcePath).Append('|').Append(dependency.ApprovalReason).Append('|')
                .Append(dependency.ApprovalVersion).Append('\n');
        }
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    internal static void AddFile(List<CharacterCookDependencyRecord> records, string kind, string projectPath)
    {
        string normalized = projectPath.Replace('\\', '/');
        string full = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), normalized);
        string dependencyHash = "missing";
        if (File.Exists(full))
        {
            string extension = Path.GetExtension(full);
            dependencyHash = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? Sha256(Encoding.UTF8.GetBytes(CanonicalJson(File.ReadAllText(full))))
                : Sha256(File.ReadAllBytes(full));
        }
        records.Add(new CharacterCookDependencyRecord
        {
            Kind = kind,
            Identity = normalized,
            Guid = AssetDatabase.AssetPathToGUID(normalized),
            DependencyHash = dependencyHash,
            MetaHash = File.Exists(full + ".meta") ? Sha256(File.ReadAllBytes(full + ".meta")) : "",
        });
    }
    private static void AddAsset(List<CharacterCookDependencyRecord> records, string kind, string projectPath, string packageRoot)
    {
        string normalized = projectPath.Replace('\\', '/');
        string full = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), normalized);
        CharacterPackageDependencyInfo classification = CharacterPackageAuthoringService.ClassifyDependency(packageRoot, normalized);
        records.Add(new CharacterCookDependencyRecord
        {
            Kind = kind,
            Identity = normalized,
            Guid = AssetDatabase.AssetPathToGUID(normalized),
            DependencyHash = AssetDatabase.GetAssetDependencyHash(normalized).ToString(),
            MetaHash = File.Exists(full + ".meta") ? Sha256(File.ReadAllBytes(full + ".meta")) : "",
            Classification = classification.Classification,
            SourcePackageId = classification.SourcePackageId,
            SourcePath = classification.SourcePath,
            ApprovalReason = classification.ApprovalReason,
            ApprovalVersion = classification.ApprovalVersion,
        });
    }

    private static string FindAnimancerIdentity()
    {
        string manifest = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Packages/manifest.json");
        if (!File.Exists(manifest)) return "absent";
        string text = File.ReadAllText(manifest);
        int index = text.IndexOf("animancer", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? "absent" : text.Substring(index, Math.Min(160, text.Length - index));
    }

    private static string Hex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) builder.Append(value.ToString("x2"));
        return builder.ToString();
    }

    private static string CanonicalJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var builder = new StringBuilder();
        WriteCanonical(document.RootElement, builder);
        return builder.ToString();
    }

    private static void WriteCanonical(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                bool firstProperty = true;
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (!firstProperty) builder.Append(',');
                    firstProperty = false;
                    builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    WriteCanonical(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                bool firstValue = true;
                foreach (var value in element.EnumerateArray())
                {
                    if (!firstValue) builder.Append(',');
                    firstValue = false;
                    WriteCanonical(value, builder);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString() ?? ""));
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(bytes));
    }
}
