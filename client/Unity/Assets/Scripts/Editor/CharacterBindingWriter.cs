using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Client.Animation;

internal static class CharacterBindingWriter
{
    internal static byte[] Write(
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterCookAnimationDefinition> animations,
        string sourceHash)
    {
        var json = new StringBuilder(1024);
        json.Append('{');
        Property(json, "packageId", catalog.PackageId);
        Property(json, "catalogSchemaVersion", catalog.CatalogSchemaVersion);
        Property(json, "bindingSchemaVersion", UnityCharacterAssetCooker.BindingSchemaVersion);
        Property(json, "poseFormat", "SKEL");
        Property(json, "poseVersion", UnityCharacterAssetCooker.PoseVersion);
        Property(json, "sampleRate", UnityCharacterAssetCooker.SampleRate);
        Property(json, "sourceHash", sourceHash);
        Property(json, "rigGlobalObjectId", UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(catalog.Rig).ToString());
        Property(json, "weaponConfigGlobalObjectId", catalog.WeaponConfig == null ? "" : UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(catalog.WeaponConfig).ToString());
        json.Append("\"animations\":[");
        bool first = true;
        foreach (var animation in animations.OrderBy(x => x.SemanticId, StringComparer.Ordinal))
        {
            if (!first) json.Append(',');
            first = false;
            json.Append('{');
            Property(json, "semanticId", animation.SemanticId);
            Property(json, "poseTrackId", animation.PoseTrackId);
            Property(json, "clipGlobalObjectId", animation.ClipGlobalObjectId);
            Property(json, "poseName", animation.PoseTrackId);
            Property(json, "frameCount", animation.FrameCount);
            Property(json, "clipLengthBits", animation.ClipLengthBits);
            Property(json, "sampleRate", animation.SampleRate);
            Property(json, "extrapolation", (int)animation.Extrapolation, final: true);
            json.Append('}');
        }
        json.Append(']');
        json.Append(",\"presentations\":[");
        first = true;
        foreach (var presentation in (catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>()).OrderBy(x => x.SemanticId, StringComparer.Ordinal))
        {
            if (!first) json.Append(',');
            first = false;
            json.Append('{');
            Property(json, "semanticId", presentation.SemanticId);
            Property(json, "prefabGlobalObjectId",
                UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(presentation.Prefab).ToString(), final: true);
            json.Append('}');
        }
        json.Append(']');
        json.Append(",\"presentationProfile\":").Append(ProfileJson(catalog.PresentationProfile));
        json.Append('}');
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    internal static string ObjectId(UnityEngine.Object asset, string retainedId = "")
        => asset == null ? retainedId ?? "" : UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString();

    // This exact encoding is shared by bindings, source hashing, catalog equality and recovery.
    internal static string ProfileJson(CharacterPresentationProfile profile)
    {
        if (profile == null) return "null";
        var json = new StringBuilder();
        json.Append("{\"respawnTintBits\":[");
        var tint = profile.RespawnTint;
        json.Append(BitConverter.SingleToInt32Bits(tint.r).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(BitConverter.SingleToInt32Bits(tint.g).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(BitConverter.SingleToInt32Bits(tint.b).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(BitConverter.SingleToInt32Bits(tint.a).ToString(CultureInfo.InvariantCulture)).Append(']');
        Pool("swing", profile.Swing); Pool("lightImpact", profile.LightImpact);
        Pool("mediumImpact", profile.MediumImpact); Pool("heavyImpact", profile.HeavyImpact);
        json.Append(",\"suppressRemovalExplosion\":").Append(profile.SuppressRemovalExplosion ? "true" : "false");
        json.Append(",\"projectiles\":[");
        bool first = true;
        foreach (var row in profile.Projectiles.ToArray().OrderBy(x =>
            SlopArena.Shared.CanonicalSlotProjection.TryGet(x.SlotId, out var address) ? address.Ordinal : int.MaxValue))
        {
            if (!first) json.Append(','); first = false;
            json.Append('{');
            Property(json, "slotId", row.SlotId);
            Property(json, "prefabGlobalObjectId", ObjectId(row.Prefab, row.PrefabObjectId));
            Property(json, "scaleBits", BitConverter.SingleToInt32Bits(row.Scale));
            Property(json, "orientation", (int)row.Orientation);
            Property(json, "heldBoneId", row.HeldBoneId, final: true);
            json.Append('}');
        }
        return json.Append("]}").ToString();

        void Pool(string name, AudioPool pool)
        {
            json.Append(',').Append(JsonSerializer.Serialize(name)).Append(':');
            if (pool == null) { json.Append("null"); return; }
            json.Append('{'); Property(json, "selection", (int)pool.Selection);
            json.Append("\"clipGlobalObjectIds\":[");
            for (int i = 0; i < pool.Clips.Length; i++)
            {
                if (i != 0) json.Append(',');
                json.Append(JsonSerializer.Serialize(ObjectId(pool.Clips[i],
                    i < pool.ClipObjectIds.Length ? pool.ClipObjectIds[i] : "")));
            }
            json.Append("]}");
        }
    }

    internal static CharacterPresentationProfile ReadProfile(JsonElement profile, bool allowMissing,
        List<string> missing = null)
    {
        var tint = profile.GetProperty("respawnTintBits").EnumerateArray()
            .Select(v => BitConverter.Int32BitsToSingle(v.GetInt32())).ToArray();
        var rows = new List<ProjectilePresentation>();
        foreach (var row in profile.GetProperty("projectiles").EnumerateArray())
        {
            string id = row.GetProperty("prefabGlobalObjectId").GetString();
            rows.Add(new ProjectilePresentation(row.GetProperty("slotId").GetString(),
                Resolve<UnityEngine.GameObject>(id), BitConverter.Int32BitsToSingle(row.GetProperty("scaleBits").GetInt32()),
                (ProjectileOrientation)row.GetProperty("orientation").GetInt32(), row.GetProperty("heldBoneId").GetString(), id));
        }
        return new CharacterPresentationProfile(new UnityEngine.Color(tint[0], tint[1], tint[2], tint[3]),
            Pool("swing"), Pool("lightImpact"), Pool("mediumImpact"), Pool("heavyImpact"),
            profile.GetProperty("suppressRemovalExplosion").GetBoolean(), rows.ToArray());

        AudioPool Pool(string name)
        {
            var pool = profile.GetProperty(name);
            var ids = pool.GetProperty("clipGlobalObjectIds").EnumerateArray().Select(v => v.GetString()).ToArray();
            return new AudioPool((AudioPoolSelection)pool.GetProperty("selection").GetInt32(),
                ids.Select(Resolve<UnityEngine.AudioClip>).ToArray(), ids);
        }
        T Resolve<T>(string id) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!UnityEditor.GlobalObjectId.TryParse(id, out var objectId))
                throw new InvalidOperationException("Invalid presentation media ID: " + id);
            var asset = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId);
            if (asset != null && asset is not T)
                throw new InvalidOperationException("Presentation media has the wrong asset type: " + id);
            if (asset == null)
            {
                if (!allowMissing) throw new InvalidOperationException("Presentation media is not installed: " + id);
                missing?.Add(id);
            }
            return asset as T;
        }
    }

    internal static IEnumerable<(string Identity, UnityEngine.Object Asset, string ObjectId)> ProfileMedia(CharacterPresentationProfile profile)
    {
        if (profile == null) yield break;
        foreach (var pair in new[] { ("swing", profile.Swing), ("lightImpact", profile.LightImpact),
            ("mediumImpact", profile.MediumImpact), ("heavyImpact", profile.HeavyImpact) })
        {
            var pool = pair.Item2;
            if (pool == null) continue;
            var clips = pool.Clips.ToArray();
            var ids = pool.ClipObjectIds.ToArray();
            for (int i = 0; i < clips.Length; i++)
                yield return (pair.Item1 + "[" + i + "]", clips[i], ObjectId(clips[i], i < ids.Length ? ids[i] : ""));
        }
        // Do not hold a span over a yield boundary.
        foreach (var row in profile.Projectiles.ToArray())
            if (row != null) yield return (row.SlotId, row.Prefab, ObjectId(row.Prefab, row.PrefabObjectId));
    }

    private static void Property(StringBuilder json, string name, string value, bool final = false)
    {
        json.Append(JsonSerializer.Serialize(name));
        json.Append(':');
        json.Append(JsonSerializer.Serialize(value));
        if (!final) json.Append(',');
    }

    private static void Property(StringBuilder json, string name, int value, bool final = false)
    {
        json.Append(JsonSerializer.Serialize(name));
        json.Append(':');
        json.Append(value.ToString(CultureInfo.InvariantCulture));
        if (!final) json.Append(',');
    }
}
