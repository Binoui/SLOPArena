using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;

namespace SlopArena.KitCheck;

public sealed record CorpusDiagnostic(string Severity, string Code, string Path, string Message);
public sealed record CorpusCase(string Name, CharacterCookProfile Profile, string Manifest, string Character,
    string? Bytes, CorpusDiagnostic[] Diagnostics);

public static class ContractCorpus
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CorpusCase Capture(string name, CharacterCookProfile profile, string manifest, string character)
    {
        var result = CharacterPackageCompiler.Compile(manifest, character, profile);
        return new CorpusCase(name, profile, manifest, character,
            result.CookedPackage == null ? null : Convert.ToBase64String(result.CookedPackage.CanonicalBytes),
            result.Diagnostics.Select(d => new CorpusDiagnostic(d.Severity.ToString(), d.Code, d.Path, d.Message)).ToArray());
    }

    public static void Record(string root, string proof, string knight, string output)
    {
        var cases = new List<CorpusCase>();
        void Folder(string name, string folder, CharacterCookProfile profile) => cases.Add(Capture(name, profile,
            File.ReadAllText(Path.Combine(folder, "package.json")), File.ReadAllText(Path.Combine(folder, "character.json"))));
        foreach (var id in new[] { "fightguy", "wibou", "manki", "bonk", "bashie", "salamander", "nyx" })
            Folder(id, Path.Combine(root, "client/Unity/Assets/CharacterPackages", id), CharacterCookProfile.TrustedBuiltIn);
        Folder("example", Path.Combine(root, "docs/characters/examples/workshop-kit"), CharacterCookProfile.Workshop);
        Folder("proof", proof, CharacterCookProfile.Workshop);
        Folder("knight", knight, CharacterCookProfile.Workshop);
        var original = cases.Single(x => x.Name == "example");
        var observed = new HashSet<string>(StringComparer.Ordinal);
        void Candidate(string name, string manifest, string character)
        {
            var capture = Capture(name, CharacterCookProfile.Workshop, manifest, character);
            if (capture.Diagnostics.Any(d => !observed.Contains(d.Code)))
            {
                cases.Add(capture);
                foreach (var d in capture.Diagnostics) observed.Add(d.Code);
            }
        }
        Candidate("invalid-json", original.Manifest, "{");
        Candidate("duplicate", original.Manifest, original.Character.Insert(1, "\"weight\":100,"));
        foreach (bool manifest in new[] { true, false })
        {
            var node = JsonNode.Parse(manifest ? original.Manifest : original.Character)!;
            foreach (var mutation in Mutations(node))
                Candidate((manifest ? "manifest-" : "character-") + mutation.Name,
                    manifest ? mutation.Json : original.Manifest, manifest ? original.Character : mutation.Json);
        }
        var document = JsonNode.Parse(original.Character)!.AsObject();
        var slot = document["slots"]![0]!;
        var stage = slot["timeline"]!["stages"]![0]!;
        var op = stage["operations"]![0]!;
        var edits = new (string Path, string Value)[]
        {
            ("capabilityRequirements", "[{\"capabilityId\":\"slop.internal.fightguy.rising-dragon.v1\",\"capabilityVersion\":\"1\"}]"),
            ("capabilityRequirements", "[{\"capabilityId\":\"slop.ability.aimed-recovery.v1\",\"capabilityVersion\":\"99\"}]"),
            ("capabilityRequirements", "[{\"capabilityId\":\"slop.ability.clinch.v1\",\"capabilityVersion\":\"1\"}]"),
            ("aliases", "[{\"from\":\"ground.1\",\"to\":\"ground.1\"}]"),
            ("aliases", "[{\"from\":\"ground.F\",\"to\":\"air.F\"},{\"from\":\"air.F\",\"to\":\"ground.F\"}]"),
            ("slots", "[]"),
        };
        foreach (var edit in edits)
        {
            var copy = document.DeepClone(); copy[edit.Path] = JsonNode.Parse(edit.Value);
            Candidate(edit.Path + edit.Value, original.Manifest, copy.ToJsonString());
        }
        foreach (var entry in cases.ToArray().Where(x => x.Profile == CharacterCookProfile.Workshop && x.Bytes != null))
        {
            JsonNode? c;
            try { c = JsonNode.Parse(entry.Character); } catch (JsonException) { continue; }
            if (c is not JsonObject obj || obj["slots"] is not JsonArray slots) continue;
            foreach (var s in slots.Where(x => x != null).ToArray())
            {
                if (s!["timeline"]?["stages"] is not JsonArray stages) continue;
                foreach (var st in stages.Where(x => x != null).ToArray())
                {
                    if (st!["operations"] is not JsonArray ops) continue;
                    foreach (var operation in ops.Where(x => x != null).ToArray())
                    foreach (var field in new[] { "kind", "unit", "capabilityId", "capabilityVersion" })
                    {
                        var previous = operation![field]?.DeepClone();
                        operation[field] = "not-known";
                        Candidate(field + "-unknown", original.Manifest, c.ToJsonString());
                        operation[field] = previous;
                    }
                }
            }
        }
        void Edit(string name, Action<JsonObject> edit)
        {
            var copy = JsonNode.Parse(original.Character)!.AsObject(); edit(copy);
            Candidate(name, original.Manifest, copy.ToJsonString());
        }
        Edit("unused-presentation", c => c["presentationIds"] = JsonNode.Parse("[\"presentation.unused\"]"));
        Edit("identity-forbidden", c => c["id"] = "forbidden");
        Edit("class-forbidden", c => c["class"] = "FightGuy");
        Edit("alias-cycle", c => { c["slots"]!.AsArray().RemoveAt(0); c["aliases"] = JsonNode.Parse("[{\"from\":\"ground.1\",\"to\":\"ground.1\"}]"); });
        Edit("capability-ambiguous", c => c["slots"]![4]!["isRecoveryMove"] = false);
        Edit("aim-speed-requires-mobile", c => c["slots"]![0]!["aimMovementSpeedScale"] = 0.5);
        Edit("slide-motion-conflict", c => { c["slots"]![0]!["allowSlideCarry"] = true; c["slots"]![0]!["aimMode"] = "groundVector"; });
        Candidate("non-finite", original.Manifest, original.Character.Replace("\"weight\": 100", "\"weight\": 1e100"));
        Candidate("invalid-unchecked-float", original.Manifest, original.Character.Replace("\"runSpeed\": 14", "\"runSpeed\": 1e100"));
        JsonObject Correction() => JsonNode.Parse("{\"kind\":\"startupAimCorrection\",\"tick\":0,\"unit\":\"ticks\",\"endTick\":5,\"acquisitionRange\":4,\"acquisitionHalfAngleDegrees\":90,\"maxYawDegrees\":45,\"maxPitchDegrees\":0,\"yawDegreesPerSecond\":720,\"pitchDegreesPerSecond\":0}")!.AsObject();
        Edit("duplicate-correction", c => { var ops = c["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray(); ops.Insert(0, Correction()); ops.Insert(0, Correction()); });
        Edit("correction-after-commitment", c => { var op = Correction(); op["tick"] = 7; op["endTick"] = 9; c["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray().Insert(0, op); });
        Edit("correction-cutoff-after-commitment", c => { var op = Correction(); op["endTick"] = 9; c["slots"]![0]!["timeline"]!["stages"]![0]!["operations"]!.AsArray().Insert(0, op); });
        Edit("lunge-no-contact", c => { var ops = c["slots"]![2]!["timeline"]!["stages"]![0]!["operations"]!.AsArray(); ops[0]!["stopInAttackRange"] = true; ops.RemoveAt(1); });
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(cases, JsonOptions) + "\n");
        Console.WriteLine($"Captured {cases.Count} cases, {cases.Count(x => x.Bytes != null)} successful outputs, {observed.Count} diagnostic codes.");
        Console.WriteLine(string.Join(", ", observed.OrderBy(x => x, StringComparer.Ordinal)));
    }

    private sealed record Mutation(string Name, string Json);
    private static IEnumerable<Mutation> Mutations(JsonNode root)
    {
        var paths = new List<string[]>();
        void Walk(JsonNode? node, string[] path)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj) { var p = path.Append(pair.Key).ToArray(); paths.Add(p); Walk(pair.Value, p); }
            if (node is JsonArray array && array.Count > 0) Walk(array[0], path.Append("0").ToArray());
        }
        Walk(root, Array.Empty<string>());
        foreach (var path in paths)
        foreach (var replacement in new[] { "REMOVE", "null", "{}", "[]", "\"not-known\"", "-1", "0", "65536", "false" })
        {
            var clone = root.DeepClone(); JsonNode parent = clone;
            foreach (var segment in path.Take(path.Length - 1)) parent = parent is JsonArray a ? a[int.Parse(segment)]! : parent[segment]!;
            if (parent is not JsonObject obj) continue;
            if (replacement == "REMOVE") obj.Remove(path[^1]); else obj[path[^1]] = JsonNode.Parse(replacement);
            yield return new Mutation(string.Join(".", path) + "=" + replacement, clone.ToJsonString());
        }
        var extra = root.DeepClone(); extra["unknownField"] = true;
        yield return new Mutation("unknown-field", extra.ToJsonString());
    }
}
