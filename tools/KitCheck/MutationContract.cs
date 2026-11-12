using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;

namespace SlopArena.KitCheck;

public sealed record MutationInput(string Name, string Manifest, string Character);
public sealed record MutationDigest(string Fighter, string Profile, string Family, int Cases, int Successful,
    int RoundTrips, int UnusedWarnings, string ContractHash, string DiagnosticHash);

public static class MutationContract
{
    public static IReadOnlyList<MutationDigest> Run(string inputsPath, string edgesPath, Action<string>? progress = null, TextWriter? trace = null)
    {
        var inputs = JsonSerializer.Deserialize<MutationInput[]>(File.ReadAllText(inputsPath))!;
        var edges = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(edgesPath))!;
        var results = new List<MutationDigest>();
        foreach (var input in inputs)
        foreach (var profile in new[] { CharacterCookProfile.Workshop, CharacterCookProfile.TrustedBuiltIn })
        {
            var families = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            void Check(string family, string label, string manifest, string character)
            {
                if (!families.TryGetValue(family, out var acc)) families.Add(family, acc = new Accumulator());
                acc.Cases++;
                CharacterCompileResult result;
                try { result = CharacterPackageCompiler.Compile(manifest, character, profile); }
                catch (Exception error)
                {
                    trace?.WriteLine(input.Name + "/" + profile + "/" + label + " exception:" + error.GetType().FullName);
                    acc.Append(acc.Contract, label, "exception:" + error.GetType().FullName);
                    acc.Append(acc.Diagnostics, label, "exception:" + error.GetType().FullName);
                    return;
                }
                string bytes = result.CookedPackage == null ? "failed" : Convert.ToBase64String(result.CookedPackage.CanonicalBytes);
                if (result.CookedPackage != null) acc.Successful++;
                var diagnostics = result.Diagnostics.Select(d => d.Severity + ":" + d.Code).ToArray();
                trace?.WriteLine(input.Name + "/" + profile + "/" + label + " " + string.Join(",", diagnostics));
                acc.UnusedWarnings += result.Diagnostics.Count(d => d.Code == "presentation.unused-id");
                acc.Append(acc.Diagnostics, label, string.Join("\n", diagnostics));
                acc.Append(acc.Contract, label, bytes, string.Join("\n", result.Diagnostics
                    .Where(d => d.Code != "presentation.unused-id").Select(d => d.Severity + ":" + d.Code)));
                if (result.CookedPackage != null)
                {
                    var loaded = CharacterPackageSourceCodec.Load(manifest, character);
                    if (!loaded.IsValid) throw new InvalidOperationException("Successful source cannot round-trip: " + label);
                    var again = CharacterPackageCompiler.Compile(CharacterPackageSourceCodec.SerializeManifest(loaded.Source!.Manifest),
                        CharacterPackageSourceCodec.SerializeCharacter(loaded.Source.Character), profile);
                    acc.RoundTrips++;
                    acc.Append(acc.Contract, again.CookedPackage == null ? "roundtrip-failed" : Convert.ToBase64String(again.CookedPackage.CanonicalBytes),
                        string.Join("\n", again.Diagnostics.Where(d => d.Code != "presentation.unused-id").Select(d => d.Severity + ":" + d.Code)));
                }
            }
            Check("original", "original", input.Manifest, input.Character);
            foreach (bool isManifest in new[] { true, false })
            {
                var root = JsonNode.Parse(isManifest ? input.Manifest : input.Character)!;
                foreach (var mutation in Mutations(root, edges))
                    Check(mutation.Family, (isManifest ? "manifest." : "character.") + mutation.Label,
                        isManifest ? mutation.Json : input.Manifest, isManifest ? input.Character : mutation.Json);
            }
            foreach (var pair in families.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var a = pair.Value;
                results.Add(new MutationDigest(input.Name, profile.ToString(), pair.Key, a.Cases, a.Successful,
                    a.RoundTrips, a.UnusedWarnings, Convert.ToHexString(a.Contract.GetHashAndReset()).ToLowerInvariant(),
                    Convert.ToHexString(a.Diagnostics.GetHashAndReset()).ToLowerInvariant()));
                a.Dispose();
            }
            progress?.Invoke(input.Name + " " + profile + ": " + families.Values.Sum(x => x.Cases) + " cases");
        }
        return results;
    }

    private sealed class Accumulator : IDisposable
    {
        internal readonly IncrementalHash Contract = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal readonly IncrementalHash Diagnostics = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal int Cases, Successful, RoundTrips, UnusedWarnings;
        internal void Append(IncrementalHash hash, params string[] parts)
        {
            foreach (var part in parts)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(part);
                hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
            }
        }
        public void Dispose() { Contract.Dispose(); Diagnostics.Dispose(); }
    }

    private sealed record Mutation(string Family, string Label, string Json);
    private static IEnumerable<Mutation> Mutations(JsonNode root, Dictionary<string, double[]> edges)
    {
        var paths = new List<string[]>();
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? node, string[] path, string shape)
        {
            if (node is JsonObject obj)
            {
                string discriminator = obj["kind"]?.ToJsonString() ?? obj["capabilityId"]?.ToJsonString() ?? "";
                foreach (var pair in obj)
                {
                    string key = shape + discriminator + "." + pair.Key;
                    if (shapes.Add(key)) paths.Add(path.Append(pair.Key).ToArray());
                    Walk(pair.Value, path.Append(pair.Key).ToArray(), key);
                }
            }
            if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++) Walk(array[i], path.Append(i.ToString()).ToArray(), shape + "[]");
        }
        Walk(root, Array.Empty<string>(), "");
        foreach (var path in paths)
        {
            JsonNode? Value(JsonNode node)
            {
                foreach (var part in path) node = node is JsonArray a ? a[int.Parse(part)]! : node[part]!;
                return node;
            }
            var original = Value(root);
            var candidates = new List<(string Family, string? Json)> { ("removal", null), ("types", "null"), ("types", "{}"), ("types", "[]"), ("types", "\"not-known\""), ("types", "false") };
            if (original is JsonValue v && v.TryGetValue<double>(out var number))
            {
                var limits = new SortedSet<double> { -1, 0, 1, number - 1, number + 1, 1e100 };
                if (edges.TryGetValue(path[^1], out var known)) foreach (var edge in known) limits.Add(edge);
                foreach (double limit in limits) candidates.Add(("range-edges", JsonSerializer.Serialize(limit)));
            }
            foreach (var (family, json) in candidates)
            {
                var clone = root.DeepClone(); JsonNode parent = clone;
                foreach (var part in path.Take(path.Length - 1)) parent = parent is JsonArray a ? a[int.Parse(part)]! : parent[part]!;
                if (json == null) parent.AsObject().Remove(path[^1]); else parent[path[^1]] = JsonNode.Parse(json);
                yield return new Mutation(family, string.Join("/", path) + "=" + (json ?? "REMOVE"), clone.ToJsonString());
            }
            if (original is JsonObject)
            {
                var clone = root.DeepClone();
                var target = Value(clone)!.AsObject();
                target["unknownField"] = true;
                yield return new Mutation("field-insertion", string.Join("/", path) + "/unknownField", clone.ToJsonString());
            }
        }
        var rootInsertion = root.DeepClone();
        rootInsertion["unknownField"] = true;
        yield return new Mutation("field-insertion", "unknownField", rootInsertion.ToJsonString());
    }
}
