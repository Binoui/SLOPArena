using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;

namespace SlopArena.KitCheck;

public sealed record FieldDigest(string Schema, int Objects, int Removals, int DefaultInsertions, string Hash);
public sealed record SourceObject(JsonObject Value, CharacterSourceObject Schema, string[] Path);

public static class FieldContract
{
    public static IEnumerable<SourceObject> Walk(JsonNode node, CharacterSourceObject schema, string[] path)
    {
        var obj = node.AsObject();
        yield return new SourceObject(obj, schema, path);
        foreach (var field in schema.Fields.Where(x => x.ChildSchemaId != null))
        {
            if (obj[field.Name] is not { } child) continue;
            CharacterSourceObject? Resolve(JsonNode value) => field.ChildSchemaId switch
            {
                "operation" => CharacterSourceSchema.Operation(value["kind"]!.GetValue<string>()),
                "capability" => CharacterSourceSchema.Capability(obj["capabilityId"]!.GetValue<string>()),
                _ => CharacterSourceSchema.Object(field.ChildSchemaId!),
            };
            var children = child is JsonArray array
                ? array.Select((value, i) => (Value: value!, Path: path.Append(field.Name).Append(i.ToString()).ToArray()))
                : new[] { (Value: child, Path: path.Append(field.Name).ToArray()) };
            foreach (var item in children)
                if (Resolve(item.Value) is { } resolved)
                    foreach (var entry in Walk(item.Value, resolved, item.Path)) yield return entry;
        }
    }

    public static IReadOnlyList<FieldDigest> Run(IEnumerable<MutationInput> inputs, Action<string>? progress = null)
    {
        var allInputs = inputs.ToList();
        void WithoutPhase(string source, string capability, params string[] fields)
        {
            var input = allInputs.Single(x => x.Name == source);
            var character = JsonNode.Parse(input.Character)!;
            foreach (var entry in Walk(character, CharacterSourceSchema.Object("character"), Array.Empty<string>() ))
                if (entry.Schema.CapabilityId == capability)
                    foreach (string field in fields) entry.Value.Remove(field);
            allInputs.Add(input with { Name = source + "-no-phase", Character = character.ToJsonString() });
        }
        WithoutPhase("coverage", "slop.ability.charged-stream.v1", "sprayAnimationId", "sprayLoopTicks");
        WithoutPhase("bonk", "slop.ability.steered-charge.v2", "gallopAnimationId", "gallopLoopTicks");
        var output = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var counts = new Dictionary<string, (int Objects, int Removals, int Defaults)>(StringComparer.Ordinal);
        var defaultFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in allInputs)
        {
            var profile = input.Name is "example" or "grappler" or "coverage" ? CharacterCookProfile.Workshop : CharacterCookProfile.TrustedBuiltIn;
            foreach (bool manifest in new[] { true, false })
            {
                var source = JsonNode.Parse(manifest ? input.Manifest : input.Character)!;
                foreach (var entry in Walk(source, CharacterSourceSchema.Object(manifest ? "manifest" : "character"), Array.Empty<string>()))
                {
                    string id = entry.Schema.Id;
                    if (!output.TryGetValue(id, out var text)) output.Add(id, text = new StringBuilder());
                    var count = counts.TryGetValue(id, out var previous) ? previous : default;
                    count.Objects++;
                    using var document = JsonDocument.Parse(entry.Value.ToJsonString());
                    foreach (var field in entry.Schema.Fields)
                    {
                        if (!field.IsAllowed(document.RootElement)) continue;
                        var clone = source.DeepClone(); JsonNode parent = clone;
                        foreach (var part in entry.Path) parent = parent is JsonArray a ? a[int.Parse(part)]! : parent[part]!;
                        parent.AsObject().Remove(field.Name);
                        CharacterCompileResult Compile(JsonNode changed) => CharacterPackageCompiler.Compile(
                            manifest ? changed.ToJsonString() : input.Manifest, manifest ? input.Character : changed.ToJsonString(), profile);
                        var omitted = Compile(clone);
                        count.Removals++;
                        string label = input.Name + "/" + string.Join("/", entry.Path) + "/" + field.Name;
                        text.AppendLine(label + ":" + string.Join(",", omitted.Diagnostics.Where(d => d.Code != "presentation.unused-id").Select(d => d.Severity + ":" + d.Code)));
                        text.AppendLine(omitted.CookedPackage == null ? "failed" : Convert.ToBase64String(omitted.CookedPackage.CanonicalBytes));
                        if (field.IsRequired(document.RootElement) && !omitted.HasErrors)
                            throw new InvalidOperationException("Required field removal compiled: " + label);
                        if (omitted.CookedPackage != null && field.HasWritableDefault)
                        {
                            parent[field.Name] = JsonNode.Parse(field.DefaultJson!);
                            var inserted = Compile(clone);
                            if (inserted.CookedPackage == null || !omitted.CookedPackage.CanonicalBytes.SequenceEqual(inserted.CookedPackage.CanonicalBytes))
                                throw new InvalidOperationException("Documented default changes output: " + label + "\n" + string.Join("\n", inserted.Diagnostics));
                            defaultFields.Add(id + "." + field.Name);
                            count.Defaults++;
                        }
                    }
                    counts[id] = count;
                }
            }
            progress?.Invoke(input.Name + " fields checked");
        }
        foreach (var schema in CharacterSourceSchema.Objects)
        foreach (var field in schema.Fields.Where(x => x.HasWritableDefault))
            if (!defaultFields.Contains(schema.Id + "." + field.Name)) throw new InvalidOperationException("No valid default context: " + schema.Id + "." + field.Name);
        return output.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new FieldDigest(x.Key,
            counts[x.Key].Objects, counts[x.Key].Removals, counts[x.Key].Defaults,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Value.ToString()))).ToLowerInvariant())).ToArray();
    }
}
