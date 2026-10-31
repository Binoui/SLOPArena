using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopArena.Shared;

public sealed class HitReliabilityTargets
{
    public static HitReliabilityTargets Load(string cookedRoot = "content-cooked")
        => new(BuiltInContentResolver.Resolve(CharacterClass.FightGuy, cookedRoot),
            BuiltInContentResolver.Resolve(CharacterClass.Manki, cookedRoot));

    internal HitReliabilityTargets(MatchContentEntry medium, MatchContentEntry small)
    {
        if (medium.Definition.Class != CharacterClass.FightGuy || small.Definition.Class != CharacterClass.Manki)
            throw new ArgumentException("Reliability targets must be FightGuy (medium) and Manki (small).");
        Medium = new HitReliabilityTarget(medium);
        Small = new HitReliabilityTarget(small);
        All = Array.AsReadOnly(new[] { Medium, Small });
    }

    public HitReliabilityTarget Medium { get; }
    public HitReliabilityTarget Small { get; }
    public IReadOnlyList<HitReliabilityTarget> All { get; }
}

public sealed class HitReliabilityTarget
{
    internal HitReliabilityTarget(MatchContentEntry entry)
    {
        Name = entry.Definition.Class.ToString();
        Identity = entry.Identity;
        Definition = entry.Definition;
        Poses = entry.BakedAnimation;
        Volumes = HitReliability.Volumes(entry, HitReliability.FlatArena()).AsReadOnly();
    }

    public string Name { get; }
    public MatchContentIdentity Identity { get; }
    internal CharacterDefinition Definition { get; }
    internal BakedAnimationData? Poses { get; }
    internal IReadOnlyList<SpellResolver.EntityData> Volumes { get; }
}

public sealed class HitReliabilityDocument
{
    public HitReliabilityDocument(MatchContentIdentity identity, IReadOnlyList<MoveReliability> moves,
        HitReliabilityTargets targets, DateTime generatedUtc, HitReliabilityDraft? draft = null)
    {
        Identity = identity;
        GeneratedUtc = generatedUtc.ToString("u", CultureInfo.InvariantCulture);
        Moves = moves;
        Targets = targets.All.ToDictionary(t => t.Name, t => (IReadOnlyList<float[]>)t.Volumes.Select(v => new[]
        {
            v.PosX - HitReliability.OriginMeters, v.PosY, v.PosZ - HitReliability.OriginMeters,
            v.EndX - HitReliability.OriginMeters, v.EndY, v.EndZ - HitReliability.OriginMeters, v.Radius,
        }).ToList());
        Draft = draft;
    }

    public MatchContentIdentity Identity { get; }
    [JsonPropertyName("generatedUtc")] public string GeneratedUtc { get; }
    [JsonPropertyName("moves")] public IReadOnlyList<MoveReliability> Moves { get; }
    [JsonPropertyName("targets")] public IReadOnlyDictionary<string, IReadOnlyList<float[]>> Targets { get; }
    [JsonPropertyName("draft"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HitReliabilityDraft? Draft { get; }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        if (Type.GetType("Mono.Runtime") != null)
        {
            options.Converters.Add(new HitReliabilitySingleConverter());
            options.Converters.Add(new HitReliabilityDoubleConverter());
        }
        return options;
    }
}

public sealed class HitReliabilityDraft
{
    private HitReliabilityDraft(string runtimeSha256, bool unsaved, byte form, HitReliabilityTargets targets)
    {
        RuntimeSha256 = runtimeSha256;
        Unsaved = unsaved;
        Form = form;
        TargetCookedContentHashes = targets.All.ToDictionary(t => t.Name, t => t.Identity.CookedContentHash);
    }

    public static HitReliabilityDraft Of(CookedCharacterPackage draft, bool unsaved, byte form, HitReliabilityTargets targets)
    {
        using var sha = SHA256.Create();
        string hash = string.Concat(sha.ComputeHash(draft.CanonicalBytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        return new HitReliabilityDraft(hash, unsaved, form, targets);
    }

    [JsonPropertyName("runtimeSha256")] public string RuntimeSha256 { get; }
    [JsonPropertyName("unsaved")] public bool Unsaved { get; }
    [JsonPropertyName("form")] public byte Form { get; }
    [JsonPropertyName("targetCookedContentHashes")] public IReadOnlyDictionary<string, string> TargetCookedContentHashes { get; }
}
