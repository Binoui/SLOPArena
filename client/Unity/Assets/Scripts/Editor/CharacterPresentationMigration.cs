using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Text.Json;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools;

/// <summary>One-time native converter. The caller supplies the captured JSON baseline explicitly.</summary>
public static class CharacterPresentationMigration
{
    private static readonly string[] Packages = { "manki", "fightguy", "wibou", "bonk", "bashie" };

    public static object Run(string baselineJson)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Migration requires stopped, compiled Edit Mode.");
        using var baseline = JsonDocument.Parse(baselineJson);
        var targets = Prepare(baseline.RootElement);
        var mediaBefore = CaptureMedia(targets);
        int changed = 0;
        // Keep this converter separate from the later explicit all-roster cook transaction.
        using var queue = CharacterCookAssetPostprocessor.PreserveQueueForSelfTest();
        try
        {
            foreach (var target in targets)
            {
                if (target.Catalog.CatalogSchemaVersion == CharacterAssetCatalog.SchemaVersion) continue;
                Undo.RecordObject(target.Catalog, "Migrate package presentation profile");
                target.Catalog.SetPresentationProfile(target.Profile);
                target.Catalog.CatalogSchemaVersion = CharacterAssetCatalog.SchemaVersion;
                EditorUtility.SetDirty(target.Catalog);
                AssetDatabase.SaveAssetIfDirty(target.Catalog);
                changed++;
            }
            Validate(baselineJson);
            var mediaAfter = CaptureMedia(targets);
            if (mediaBefore.Count != mediaAfter.Count || mediaBefore.Any(pair => !mediaAfter.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
                throw new InvalidOperationException("Migration changed media, prefab components or importer/meta bytes.");
            if (Prepare(baseline.RootElement).Any(t => t.Catalog.CatalogSchemaVersion != 2))
                throw new InvalidOperationException("Migration did not converge.");
        }
        catch
        {
            foreach (var target in targets)
            {
                target.Catalog.CatalogSchemaVersion = target.Schema;
                target.Catalog.SetPresentationProfile(target.Previous);
                EditorUtility.SetDirty(target.Catalog);
                AssetDatabase.SaveAssetIfDirty(target.Catalog);
            }
            throw;
        }
        return new { packages = targets.Count, changed, unchanged = targets.Count - changed };
    }

    public static object Validate(string baselineJson)
    {
        using var baseline = JsonDocument.Parse(baselineJson);
        var targets = Prepare(baseline.RootElement);
        foreach (var target in targets)
            if (target.Catalog.CatalogSchemaVersion != 2 ||
                CharacterBindingWriter.ProfileJson(target.Catalog.PresentationProfile) != CharacterBindingWriter.ProfileJson(target.Profile))
                throw new InvalidOperationException("Package profile does not match native baseline: " + target.Catalog.PackageId);
        return new { packages = targets.Count, projectileRows = targets.Sum(t => t.Profile.Projectiles.Length), baselineMediaPreserved = true };
    }

    private sealed class Target
    {
        internal CharacterAssetCatalog Catalog;
        internal CharacterPresentationProfile Profile;
        internal CharacterPresentationProfile Previous;
        internal ushort Schema;
    }

    private static List<Target> Prepare(JsonElement baseline)
    {
        var rows = baseline.GetProperty("rows").EnumerateArray().ToArray();
        if (rows.Length != Packages.Length || !rows.Select(r => r.GetProperty("package").GetString()).OrderBy(x => x)
            .SequenceEqual(Packages.OrderBy(x => x))) throw new InvalidOperationException("Expected all five captured roster packages.");
        if (baseline.GetProperty("projectiles").GetArrayLength() != 12) throw new InvalidOperationException("Expected twelve captured projectile rows.");
        var targets = new List<Target>();
        foreach (string package in Packages)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>($"Assets/CharacterPackages/{package}/CharacterAssetCatalog.asset")
                ?? throw new InvalidOperationException("Missing catalog: " + package);
            if (catalog.PackageId != package || catalog.CatalogSchemaVersion is not (1 or 2))
                throw new InvalidOperationException("Unsupported catalog identity/schema: " + package);
            var captured = rows.Single(r => r.GetProperty("package").GetString() == package);
            RequireId(catalog, captured.GetProperty("catalog").GetString());
            RequireId(catalog.Rig, captured.GetProperty("rig").GetString());
            RequireId(catalog.WeaponConfig, captured.GetProperty("weaponConfig").GetString());
            ValidateEffects(catalog, captured.GetProperty("effects"));
            var expected = ExpectedProfile(package, baseline);
            bool legacyDefault = catalog.CatalogSchemaVersion == 1 &&
                CharacterBindingWriter.ProfileJson(catalog.PresentationProfile) == CharacterBindingWriter.ProfileJson(new CharacterPresentationProfile());
            if (catalog.PresentationProfile != null && !legacyDefault && CharacterBindingWriter.ProfileJson(catalog.PresentationProfile) != CharacterBindingWriter.ProfileJson(expected))
                throw new InvalidOperationException("Conflicting existing presentation profile: " + package);
            if (catalog.CatalogSchemaVersion == 2 && catalog.PresentationProfile == null)
                throw new InvalidOperationException("Schema 2 catalog has no presentation profile: " + package);
            targets.Add(new Target { Catalog = catalog, Profile = expected, Previous = catalog.PresentationProfile?.DeepClone(), Schema = catalog.CatalogSchemaVersion });
        }
        var config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals")
            ?? throw new InvalidOperationException("Generic explosion config is missing.");
        RequireId(config.ExplosionPrefab, baseline.GetProperty("explosion").GetProperty("prefab").GetString());
        if (BitConverter.SingleToInt32Bits(config.ExplosionScale) != BitConverter.SingleToInt32Bits(baseline.GetProperty("explosion").GetProperty("scale").GetSingle())
            || baseline.GetProperty("explosion").GetProperty("overrides").GetInt32() != 0)
            throw new InvalidOperationException("Generic explosion baseline changed.");
        return targets;
    }

    private static CharacterPresentationProfile ExpectedProfile(string package, JsonElement baseline)
    {
        var pools = baseline.GetProperty("pools");
        AudioPool Pool(string name) => new(AudioPoolSelection.Cycle, pools.GetProperty(name).EnumerateArray()
            .Select(clip => Resolve<AudioClip>(clip.GetProperty("objectId").GetString(), clip.GetProperty("path").GetString())).ToArray());
        AudioPool Impact(string tier)
        {
            if (package is not ("wibou" or "bonk")) return Pool(tier + "Impact");
            var clip = pools.GetProperty("overrides").EnumerateArray().Single(c =>
                string.Equals(c.GetProperty("character").GetString(), package, StringComparison.OrdinalIgnoreCase) && c.GetProperty("tier").GetString() == tier);
            return new AudioPool(AudioPoolSelection.Fixed, new[] { Resolve<AudioClip>(clip.GetProperty("objectId").GetString(), clip.GetProperty("path").GetString()) });
        }
        var swing = package switch
        {
            "bashie" => new AudioPool(AudioPoolSelection.Fixed, Array.Empty<AudioClip>()),
            "wibou" => Pool("wibouSwing"), "bonk" => Pool("bonkSwing"), _ => Pool("commonSwing"),
        };
        Color tint = package switch
        {
            "manki" => new Color(1f, .36f, .08f, .95f), "fightguy" => new Color(.28f, .72f, 1f, .95f),
            "wibou" => new Color(.76f, .9f, 1f, .95f), "bonk" => new Color(.96f, .58f, .18f, .95f),
            "bashie" => new Color(.98f, .3f, .27f, .95f), _ => throw new InvalidOperationException("Unknown migration package."),
        };
        var projectiles = new List<ProjectilePresentation>();
        foreach (var row in baseline.GetProperty("projectiles").EnumerateArray())
        {
            if (!string.Equals(row.GetProperty("character").GetString(), package, StringComparison.OrdinalIgnoreCase)) continue;
            byte wire = row.GetProperty("slot").GetByte();
            if (!CanonicalSlotProjection.TryGet(wire, row.GetProperty("airborne").GetBoolean(), out var slot))
                throw new InvalidOperationException("Legacy projectile has an invalid slot.");
            bool orient = package == "wibou" || package == "bashie" || (package == "manki" && slot.InputLabel == "R");
            projectiles.Add(new ProjectilePresentation(slot.Id,
                Resolve<GameObject>(row.GetProperty("prefab").GetString(), row.GetProperty("path").GetString()), row.GetProperty("scale").GetSingle(),
                orient ? ProjectileOrientation.FlightPositiveY : ProjectileOrientation.PreservePrefab,
                package == "fightguy" && slot.InputLabel == "A" ? "bone.right-hand" : ""));
        }
        int expected = package == "manki" ? 4 : 2;
        if (projectiles.Count != expected || projectiles.Select(p => p.SlotId).Distinct().Count() != expected)
            throw new InvalidOperationException("Legacy projectile rows are incomplete: " + package);
        return new CharacterPresentationProfile(tint, swing, Impact("light"), Impact("medium"), Impact("heavy"), package == "manki", projectiles.ToArray());
    }

    private static void ValidateEffects(CharacterAssetCatalog catalog, JsonElement captured)
    {
        var bindings = catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>();
        var capturedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in captured.EnumerateArray())
        {
            string id = effect.GetProperty("semanticId").GetString();
            capturedIds.Add(id);
            var binding = bindings.SingleOrDefault(b => b?.SemanticId == id)
                ?? throw new InvalidOperationException("Captured semantic effect disappeared: " + id);
            RequireId(binding.Prefab, effect.GetProperty("prefab").GetString());
            var actual = binding.Prefab.GetComponentsInChildren<AudioSource>(true);
            var sounds = effect.GetProperty("sounds").EnumerateArray().ToArray();
            if (actual.Length != sounds.Length) throw new InvalidOperationException("Prefab AudioSource count changed: " + id);
            for (int i = 0; i < actual.Length; i++)
            {
                var source = actual[i]; var sound = sounds[i];
                RequireId(source.clip, sound.GetProperty("objectId").GetString());
                if (source.gameObject.name != sound.GetProperty("name").GetString()
                    || BitConverter.SingleToInt32Bits(source.volume) != BitConverter.SingleToInt32Bits(sound.GetProperty("volume").GetSingle())
                    || BitConverter.SingleToInt32Bits(source.pitch) != BitConverter.SingleToInt32Bits(sound.GetProperty("pitch").GetSingle())
                    || source.loop != sound.GetProperty("loop").GetBoolean() || source.playOnAwake != sound.GetProperty("playOnAwake").GetBoolean()
                    || source.gameObject.activeSelf != sound.GetProperty("active").GetBoolean())
                    throw new InvalidOperationException("Prefab AudioSource settings changed: " + id);
            }
        }
        foreach (var binding in bindings)
            if (binding == null || (!capturedIds.Contains(binding.SemanticId) &&
                !(catalog.PackageId == "bashie" && binding.SemanticId == "presentation.bashie.shell.flip-tuck")))
                throw new InvalidOperationException("Unexpected semantic effect addition during migration.");
    }

    private static Dictionary<string, string> CaptureMedia(IEnumerable<Target> targets)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var assets = CharacterBindingWriter.ProfileMedia(target.Profile).Select(media => media.Asset)
                .Concat((target.Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>()).Select(binding => binding?.Prefab));
            foreach (var asset in assets)
            {
                if (asset == null) continue;
                foreach (string path in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset), true))
                {
                    paths.Add(path);
                    if (File.Exists(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path + ".meta"))) paths.Add(path + ".meta");
                }
            }
        }
        using var hash = SHA256.Create();
        return paths.Where(path => File.Exists(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path)))
            .ToDictionary(path => path, path => BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path)))), StringComparer.Ordinal);
    }

    private static T Resolve<T>(string id, string path) where T : UnityEngine.Object
    {
        if (!GlobalObjectId.TryParse(id, out var objectId)) throw new InvalidOperationException("Invalid baseline media identity.");
        var asset = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId) as T;
        if (asset == null || AssetDatabase.GetAssetPath(asset) != path) throw new InvalidOperationException("Baseline media no longer resolves: " + path);
        RequireId(asset, id);
        return asset;
    }
    private static void RequireId(UnityEngine.Object asset, string expected)
    {
        if (CharacterBindingWriter.ObjectId(asset) != expected) throw new InvalidOperationException("Native baseline media identity changed: " + expected);
    }
}
