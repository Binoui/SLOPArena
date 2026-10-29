using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopArena.Shared;

/// <summary>
/// Per-character hurtbox overrides authored in the Ability Lab tool (spec #119).
/// The JSON file is a FULL replacement of CharacterDefinition.HurtboxBoneDefs and
/// lives next to the baked skeleton data. It is loaded at entity registration:
/// when present and valid it wins over the C# defs; absent or malformed → C# defs
/// unchanged. The pose resolver pairs each def with its baked bone by name;
/// ValidateOrder still requires the file to list bones in bake order.
/// </summary>
public static class HurtboxOverride
{
    private sealed class BoneDefDto
    {
        [JsonPropertyName("bone")] public string Bone { get; set; } = "";
        [JsonPropertyName("ox")] public float Ox { get; set; }
        [JsonPropertyName("oy")] public float Oy { get; set; }
        [JsonPropertyName("oz")] public float Oz { get; set; }
        [JsonPropertyName("r")] public float R { get; set; }
        [JsonPropertyName("endBone")] public string? EndBone { get; set; }
        [JsonPropertyName("endOx")] public float EndOx { get; set; }
        [JsonPropertyName("endOy")] public float EndOy { get; set; }
        [JsonPropertyName("endOz")] public float EndOz { get; set; }
    }

    private sealed class OverrideDto
    {
        [JsonPropertyName("character")] public string Character { get; set; } = "";
        [JsonPropertyName("boneDefs")] public BoneDefDto[] BoneDefs { get; set; } = Array.Empty<BoneDefDto>();
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Derive the override file path from BakedDataPath, e.g.
    /// "res://data/manki_skeleton.bin" → "res://data/manki_hurtboxes.json".
    /// Returns null when the character has no baked data path (no override possible).
    /// </summary>
    public static string? OverridePathFor(CharacterDefinition def)
    {
        if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
        const string bakedSuffix = "_skeleton.bin";
        string path = def.BakedDataPath;
        if (path.EndsWith(bakedSuffix, StringComparison.Ordinal))
            return path.Substring(0, path.Length - bakedSuffix.Length) + "_hurtboxes.json";
        int dot = path.LastIndexOf('.');
        return (dot > 0 ? path.Substring(0, dot) : path) + "_hurtboxes.json";
    }

    /// <summary>Serialize the full def list to the override file format (tool-side writer).</summary>
    public static string Serialize(CharacterClass character, HurtboxBoneDef[] defs)
    {
        var dto = new OverrideDto
        {
            Character = character.ToString(),
            BoneDefs = new BoneDefDto[defs.Length],
        };
        for (int i = 0; i < defs.Length; i++)
        {
            dto.BoneDefs[i] = new BoneDefDto
            {
                Bone = defs[i].BoneName,
                Ox = defs[i].OffX,
                Oy = defs[i].OffY,
                Oz = defs[i].OffZ,
                R = defs[i].Radius,
                EndBone = defs[i].EndBoneName,
                EndOx = defs[i].EndOffX,
                EndOy = defs[i].EndOffY,
                EndOz = defs[i].EndOffZ,
            };
        }
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// Parse an override file. Returns false (defs = null) on malformed JSON, a
    /// missing character name, or an empty bone list — callers fall back to C# defs.
    /// </summary>
    public static bool TryParse(string json, out CharacterClass character, out HurtboxBoneDef[]? defs)
    {
        character = CharacterClass.None;
        defs = null;
        OverrideDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OverrideDto>(json);
        }
        catch (JsonException)
        {
            return false;
        }
        if (dto == null || string.IsNullOrEmpty(dto.Character) || dto.BoneDefs == null || dto.BoneDefs.Length == 0)
            return false;
        if (!Enum.TryParse(dto.Character, ignoreCase: true, out CharacterClass parsed))
            return false;

        var result = new HurtboxBoneDef[dto.BoneDefs.Length];
        for (int i = 0; i < dto.BoneDefs.Length; i++)
        {
            var b = dto.BoneDefs[i];
            if (string.IsNullOrEmpty(b.Bone)) return false;
            result[i] = new HurtboxBoneDef(b.Bone, b.Ox, b.Oy, b.Oz, b.R, b.EndBone, b.EndOx, b.EndOy, b.EndOz);
        }
        character = parsed;
        defs = result;
        return true;
    }

    /// <summary>
    /// True when the def list matches the baked bone array's hurtbox bones: same
    /// names in the same order for the first defs.Length entries. The bake may carry
    /// MORE named points than hurtbox defs (e.g. the synthetic _weapon_tip appended
    /// last by the baker), so extra trailing baked points are fine. Hurtboxes resolve
    /// bones by name, so this prefix match is stricter than the simulation needs.
    /// </summary>
    public static bool ValidateOrder(HurtboxBoneDef[] defs, BakedAnimationData baked)
    {
        if (baked.BoneNames == null || defs.Length > baked.BoneNames.Count) return false;
        for (int i = 0; i < defs.Length; i++)
        {
            if (!string.Equals(defs[i].BoneName, baked.BoneNames[i], StringComparison.Ordinal)
                || (defs[i].EndBoneName != null && baked.FindBoneIndex(defs[i].EndBoneName!) < 0))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Shallow-copy a definition with the hurtbox defs replaced (the override wins).
    /// The original is left untouched — defs may be shared across entities.
    /// </summary>
    public static CharacterDefinition Apply(CharacterDefinition def, HurtboxBoneDef[] defs)
        => def with { HurtboxBoneDefs = defs };
}
