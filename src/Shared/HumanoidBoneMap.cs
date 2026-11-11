using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>
/// Which node of a creator's rig plays each Unity Humanoid bone (ADR-0065, "Bone map"). Human bone names are the
/// identifiers of Unity's <c>HumanBodyBones</c>. The map comes from Mixamo node names, with any <c>prefix:</c>
/// ignored, unless a <c>humanoid.json</c> map file sits beside the GLB, which wins.
/// </summary>
public sealed class HumanoidBoneMap
{
    /// <summary>Unity's required human bones (<c>HumanTrait.RequiredBone</c>); an avatar without one is not humanoid.</summary>
    public static IReadOnlyList<string> RequiredHumanBones { get; } = Array.AsReadOnly(new[]
    {
        "Hips", "Spine", "Head",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
    });

    /// <summary>Each human bone and the Mixamo node name that plays it, as Tripo's and Mixamo's auto-riggers write.</summary>
    public static IReadOnlyList<(string Human, string Mixamo)> MixamoNames { get; } = BuildMixamoNames();

    private static readonly HashSet<string> KnownHumanBones =
        new(MixamoNames.Select(x => x.Human).Concat(new[] { "LeftEye", "RightEye", "Jaw" }), StringComparer.Ordinal);

    private readonly Dictionary<string, string> _nodeByHuman;

    private HumanoidBoneMap(Dictionary<string, string> nodeByHuman)
    {
        _nodeByHuman = nodeByHuman;
        MissingRequired = Array.AsReadOnly(RequiredHumanBones.Where(x => !nodeByHuman.ContainsKey(x)).ToArray());
    }

    /// <summary>Human bone name to node name.</summary>
    public IReadOnlyDictionary<string, string> NodeByHuman => _nodeByHuman;

    /// <summary>Required human bones the map leaves out, in <see cref="RequiredHumanBones"/> order.</summary>
    public IReadOnlyList<string> MissingRequired { get; }

    public string? NodeFor(string human) => _nodeByHuman.TryGetValue(human, out var node) ? node : null;

    /// <summary>A node name without any <c>prefix:</c>, lower case, as Mixamo matching compares it.</summary>
    public static string BareName(string nodeName)
    {
        int colon = nodeName.LastIndexOf(':');
        return (colon >= 0 ? nodeName.Substring(colon + 1) : nodeName).ToLowerInvariant();
    }

    /// <summary>
    /// Match Mixamo names among <paramref name="nodeNames"/>. When two nodes share a bare name the first one wins,
    /// so callers list skin joints before other nodes and a mesh node called "Head" cannot take the head bone.
    /// </summary>
    public static HumanoidBoneMap FromMixamoNames(IEnumerable<string> nodeNames)
    {
        var byBare = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in nodeNames)
            if (!string.IsNullOrEmpty(name) && !byBare.ContainsKey(BareName(name)))
                byBare.Add(BareName(name), name);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (human, mixamo) in MixamoNames)
            if (byBare.TryGetValue(mixamo.ToLowerInvariant(), out string? node))
                map[human] = node;
        return new HumanoidBoneMap(map);
    }

    /// <summary>
    /// Read a <c>humanoid.json</c> map file: <c>{"bones":[{"human":"Hips","node":"mixamorig:Hips"}, ...]}</c>.
    /// Unknown fields, unknown human bones, and a human bone or node named twice are refused with the reason.
    /// </summary>
    public static bool TryParseMapFile(string json, out HumanoidBoneMap? map, out string error)
    {
        map = null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow,
            });
            return TryRead(document.RootElement, out map, out error);
        }
        catch (JsonException exception)
        {
            error = $"humanoid.json is not valid JSON: {exception.Message}";
            return false;
        }
    }

    private static bool TryRead(JsonElement root, out HumanoidBoneMap? map, out string error)
    {
        map = null;
        if (root.ValueKind != JsonValueKind.Object)
            return Fail("humanoid.json must be an object with a \"bones\" array.", out error);
        JsonElement? bones = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "bones")
                return Fail($"humanoid.json has an unknown field \"{property.Name}\".", out error);
            bones = property.Value;
        }
        if (bones is not { ValueKind: JsonValueKind.Array } list)
            return Fail("humanoid.json must have a \"bones\" array.", out error);

        var nodeByHuman = new Dictionary<string, string>(StringComparer.Ordinal);
        var humanByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var entry in list.EnumerateArray())
        {
            string where = $"humanoid.json bones[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
                return Fail($"{where} must be an object.", out error);
            string? human = null, node = null;
            foreach (var property in entry.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return Fail($"{where}.{property.Name} must be a string.", out error);
                switch (property.Name)
                {
                    case "human": human = property.Value.GetString(); break;
                    case "node": node = property.Value.GetString(); break;
                    default: return Fail($"{where} has an unknown field \"{property.Name}\".", out error);
                }
            }
            if (string.IsNullOrEmpty(human) || string.IsNullOrEmpty(node))
                return Fail($"{where} needs both \"human\" and \"node\".", out error);
            if (!KnownHumanBones.Contains(human!))
                return Fail($"{where} names \"{human}\", which is not a Unity human bone.", out error);
            if (nodeByHuman.ContainsKey(human!))
                return Fail($"{where} maps {human} a second time.", out error);
            if (humanByNode.TryGetValue(node!, out string? other))
                return Fail($"{where} gives node \"{node}\" to {human}, but it already plays {other}.", out error);
            nodeByHuman.Add(human!, node!);
            humanByNode.Add(node!, human!);
        }
        map = new HumanoidBoneMap(nodeByHuman);
        error = "";
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static IReadOnlyList<(string Human, string Mixamo)> BuildMixamoNames()
    {
        var list = new List<(string, string)>
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Spine1"), ("UpperChest", "Spine2"), ("Neck", "Neck"), ("Head", "Head"),
        };
        foreach (string side in new[] { "Left", "Right" })
        {
            list.Add((side + "Shoulder", side + "Shoulder"));
            list.Add((side + "UpperArm", side + "Arm"));
            list.Add((side + "LowerArm", side + "ForeArm"));
            list.Add((side + "Hand", side + "Hand"));
            list.Add((side + "UpperLeg", side + "UpLeg"));
            list.Add((side + "LowerLeg", side + "Leg"));
            list.Add((side + "Foot", side + "Foot"));
            list.Add((side + "Toes", side + "ToeBase"));
            foreach (var (finger, mixamo) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") })
            {
                list.Add((side + finger + "Proximal", side + "Hand" + mixamo + "1"));
                list.Add((side + finger + "Intermediate", side + "Hand" + mixamo + "2"));
                list.Add((side + finger + "Distal", side + "Hand" + mixamo + "3"));
            }
        }
        return list.AsReadOnly();
    }
}
