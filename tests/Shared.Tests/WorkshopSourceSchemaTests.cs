using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class WorkshopSourceSchemaTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_field_has_a_description_unit_and_unambiguous_default()
    {
        Assert.NotEmpty(CharacterSourceSchema.Objects);
        foreach (var schema in CharacterSourceSchema.Objects)
        foreach (var field in schema.Fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Description), schema.Id + "." + field.Name);
            Assert.False(string.IsNullOrWhiteSpace(field.Unit), schema.Id + "." + field.Name);
            Assert.Equal(!field.Required, field.DefaultJson != null);
            if (field.DefaultJson != null) using (JsonDocument.Parse(field.DefaultJson)) { }
            if (field.RangeRuleId != null) Assert.NotNull(CharacterSourceSchema.Range(field.RangeRuleId));
        }
    }

    [Theory]
    [InlineData("workshop-kit")]
    [InlineData("workshop-grappler")]
    public void Every_example_field_matches_documented_requiredness(string example)
    {
        var folder = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples", example);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "package.json")))!;
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "character.json")))!;
        var original = Compile(manifest, character);
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics));
        Assert.Empty(original.Diagnostics);
        int required = 0, optional = 0;
        Check(manifest, "manifest", true);
        Check(character, "character", false);
        output.WriteLine($"{example}: removed {required} required fields and {optional} optional fields.");

        void Check(JsonNode source, string schema, bool isManifest)
        {
            foreach (var entry in Walk(source, CharacterSourceSchema.Object(schema), Array.Empty<string>()))
            {
                using var document = JsonDocument.Parse(entry.Object.ToJsonString());
                foreach (var field in entry.Schema.Fields)
                {
                    bool expectedRequired = field.IsRequired(document.RootElement);
                    var clone = source.DeepClone();
                    JsonNode parent = clone;
                    foreach (var segment in entry.Path) parent = parent is JsonArray array ? array[int.Parse(segment)]! : parent[segment]!;
                    parent.AsObject().Remove(field.Name);
                    var result = isManifest ? Compile(clone, character) : Compile(manifest, clone);
                    Assert.True(result.HasErrors == expectedRequired,
                        example + " " + string.Join(".", entry.Path.Append(field.Name)) + ": documented "
                        + (expectedRequired ? "required" : "optional") + " but removal " + (result.HasErrors ? "failed" : "passed")
                        + "\n" + string.Join("\n", result.Diagnostics));
                    if (expectedRequired) required++; else optional++;
                }
            }
        }
    }

    [Fact]
    public void Animation_diagnostics_identify_each_entry_and_offending_value()
    {
        var folder = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples/workshop-kit");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "package.json")))!;
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "character.json")))!;
        character["slots"]![0]!["timeline"]!["stages"]![0]!["animationIds"]![0] = "anim.InvalidOne";
        character["slots"]![7]!["timeline"]!["stages"]![0]!["animationIds"]![0] = "anim.InvalidTwo";
        var diagnostics = Compile(manifest, character).Diagnostics.Where(x => x.Code == "id.invalid").ToArray();
        Assert.Collection(diagnostics,
            first => { Assert.Equal("character.slots[0].timeline.stages[0].animationIds[0]", first.Path); Assert.Contains("anim.InvalidOne", first.Message); },
            second => { Assert.Equal("character.slots[7].timeline.stages[0].animationIds[0]", second.Path); Assert.Contains("anim.InvalidTwo", second.Message); });
    }

    [Fact]
    public void Invalid_json_and_unchecked_movement_numbers_identify_their_source()
    {
        var folder = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples/workshop-kit");
        var manifest = File.ReadAllText(Path.Combine(folder, "package.json"));
        var character = File.ReadAllText(Path.Combine(folder, "character.json"));
        var malformed = CharacterPackageCompiler.Compile("{", character);
        Assert.Equal("manifest", Assert.Single(malformed.Diagnostics).Path);
        var infinite = CharacterPackageCompiler.Compile(manifest, character.Replace("\"runSpeed\": 14", "\"runSpeed\": 1e100"));
        var diagnostic = Assert.Single(infinite.Diagnostics);
        Assert.Equal("schema.invalid", diagnostic.Code);
        Assert.Equal("character.movement.runSpeed", diagnostic.Path);
    }

    [Fact]
    public void Duplicate_role_alias_cycle_and_budget_diagnostics_identify_real_source_entries()
    {
        var folder = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples/workshop-kit");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "package.json")))!;
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "character.json")))!;
        character["presentation"]!["tumble"] = character["presentation"]!["idle"]!.DeepClone();
        Assert.Contains(Compile(manifest, character).Diagnostics, d => d.Code == "id.duplicate" && d.Path == "character.presentation.tumble");
        character["presentation"]!.AsObject().Remove("tumble");
        var stages = character["slots"]![0]!["timeline"]!["stages"]!.AsArray();
        var stage = stages[0]!;
        for (int i = 1; i < 5; i++) stages.Add(stage.DeepClone());
        foreach (var part in stages)
        {
            var operations = part!["operations"]!.AsArray();
            while (operations.Count < 60) operations.Add(JsonNode.Parse("{\"kind\":\"emitPresentation\",\"tick\":0,\"unit\":\"ticks\",\"presentationId\":\"presentation.cue\"}"));
        }
        character["presentationIds"]!.AsArray().Add("presentation.cue");
        Assert.Contains(Compile(manifest, character).Diagnostics, d => d.Code == "budget.exceeded" && d.Path == "character.slots[0].timeline.stages");
        stages.Clear(); stages.Add(stage.DeepClone());
        character["slots"]!.AsArray().RemoveAt(0);
        character["aliases"]!.AsArray().Add(JsonNode.Parse("{\"from\":\"ground.1\",\"to\":\"ground.1\"}"));
        Assert.Contains(Compile(manifest, character).Diagnostics, d => d.Code == "alias.cycle" && d.Path == "character.aliases[0].to");
    }

    [Fact]
    public void Public_capability_admission_does_not_admit_unknown_ids_with_null_versions()
    {
        Assert.False(CharacterPackageCompiler.IsPublicCapability("not.a.capability", null!));
        Assert.False(CharacterPackageCompiler.IsPublicCapability(null!, null!));
        Assert.True(CharacterPackageCompiler.IsPublicCapability("slop.ability.command-grab.v1", "1"));
    }

    private static CharacterCompileResult Compile(JsonNode manifest, JsonNode character)
        => CharacterPackageCompiler.Compile(manifest.ToJsonString(), character.ToJsonString(), CharacterCookProfile.Workshop);

    private sealed record Entry(JsonObject Object, CharacterSourceObject Schema, string[] Path);
    private static IEnumerable<Entry> Walk(JsonNode node, CharacterSourceObject schema, string[] path)
    {
        var obj = node.AsObject();
        foreach (var key in obj.Select(x => x.Key)) Assert.NotNull(schema.FindField(key));
        yield return new Entry(obj, schema, path);
        foreach (var field in schema.Fields.Where(x => x.ChildSchemaId != null))
        {
            if (obj[field.Name] is not { } child) continue;
            var childId = field.ChildSchemaId!;
            CharacterSourceObject Resolve(JsonNode value) => childId switch
            {
                "operation" => CharacterSourceSchema.Operation(value["kind"]!.GetValue<string>())!,
                "capability" => CharacterSourceSchema.Capability(obj["capabilityId"]!.GetValue<string>())!,
                _ => CharacterSourceSchema.Object(childId),
            };
            if (child is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                    foreach (var entry in Walk(array[i]!, Resolve(array[i]!), path.Append(field.Name).Append(i.ToString()).ToArray())) yield return entry;
            }
            else
                foreach (var entry in Walk(child, Resolve(child), path.Append(field.Name).ToArray())) yield return entry;
        }
    }
}
