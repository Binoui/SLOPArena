using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SlopArena.KitCheck;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopFixRegressionTests
{
    [Fact]
    public void Runtime_limits_and_capability_versions_are_code_owned_and_match_authoring_metadata()
    {
        foreach (var limit in CharacterRuntimeLimits.Fields)
        {
            var schema = CharacterSourceSchema.Objects.FirstOrDefault(x => x.Id == limit.SchemaId);
            if (schema != null)
            {
                var field = schema.Field(limit.Name);
                Assert.Equal((limit.GameplayMinimum, limit.GameplayMaximum, limit.ExclusiveGameplayMinimum),
                    (field.GameplayMinimum, field.GameplayMaximum, field.ExclusiveGameplayMinimum));
            }
            else
            {
                var range = CharacterSourceSchema.Objects.SelectMany(x => x.Fields).Where(x => x.Name == limit.Name)
                    .SelectMany(x => x.ContextRanges).Single(x => x.Context == limit.SchemaId);
                Assert.Equal((limit.GameplayMinimum, limit.GameplayMaximum, limit.ExclusiveGameplayMinimum),
                    (range.Minimum, range.Maximum, range.ExclusiveMinimum));
            }
        }
        foreach (var field in CharacterSourceSchema.Objects.SelectMany(x => x.Fields))
        {
            void CheckMaximum(string? codeConstant, double? maximum)
            {
                if (codeConstant == null) return;
                int split = codeConstant.LastIndexOf('.');
                var type = typeof(CharacterPackageCompiler).Assembly.GetType("SlopArena.Shared." + codeConstant[..split], true)!;
                var constant = type.GetField(codeConstant[(split + 1)..], BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
                Assert.Equal(Convert.ToDouble(constant.GetRawConstantValue()), maximum);
            }
            CheckMaximum(field.MaximumCodeConstant, field.GameplayMaximum);
            foreach (var range in field.ContextRanges) CheckMaximum(range.MaximumCodeConstant, range.Maximum);
        }
        foreach (var field in typeof(CharacterPackageCompiler).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name.EndsWith("CapabilityId", StringComparison.Ordinal)))
        {
            var versionField = typeof(CharacterPackageCompiler).GetField(field.Name[..^2] + "Version")!;
            string id = (string)field.GetRawConstantValue()!, version = (string)versionField.GetRawConstantValue()!;
            var schema = CharacterSourceSchema.Capability(id);
            Assert.Equal(version, schema?.CapabilityVersion ?? CharacterSourceSchema.Features.Single(x => x.Id == id).Version);
        }
        Assert.Equal(7, FlashCutContract.MaxTargetsLimit);
        Assert.Equal(32, CharacterSourceSchema.Range("startupRange").Maximum);
        Assert.Equal(CharacterPackageCompiler.MaxStartupAcquisitionRange, CharacterSourceSchema.Range("startupRange").Maximum);
        Assert.Equal(CharacterPackageCompiler.MaxStartupAngleDegrees, CharacterSourceSchema.Range("startupAngle").Maximum);
        Assert.Equal(CharacterPackageCompiler.MaxStartupPitchDegrees, CharacterSourceSchema.Range("startupPitch").Maximum);
        Assert.Equal(CharacterPackageCompiler.MaxStartupRateDegreesPerSecond, CharacterSourceSchema.Range("startupRate").Maximum);
        Assert.Equal(CharacterPackageCompiler.MaxSteeredChargeTurnDegreesPerSecond,
            CharacterSourceSchema.Object("cap.steered-charge.v2").Field("turnDegreesPerSecond").GameplayMaximum);
        Assert.Equal(240, CharacterSourceSchema.Range("fixedHitstun").Maximum);
        Assert.Equal(ProjectilePerchContract.PeckUsesWholeDamage, CharacterSourceSchema.Object("peck").Field("damage").WholeNumber);
    }

    [Fact]
    public void Public_and_runtime_capability_admission_allocate_no_bytes_after_warmup()
    {
        var measurement = MeasureOnFreshThread(Check, 10000, 100000, 5);
        Assert.All(measurement.Bytes, allocated => Assert.Equal(0, allocated));
        Assert.Equal(510000 * 3, measurement.Accepted);
        static int Check() => (CharacterPackageCompiler.IsPublicCapability("slop.ability.dash-strike.v1", "1") ? 1 : 0)
            + (CharacterPackageCompiler.IsPublicCapability("slop.ability.forms.v1", "1") ? 1 : 0)
            + (CharacterPackageCompiler.IsRuntimeCapability("slop.internal.fightguy.rising-dragon.v1", "1") ? 1 : 0)
            + (CharacterPackageCompiler.IsPublicCapability("unknown", "1") ? 1 : 0);
    }

    [Fact]
    public void AllocationMeasurementDetectsAllocationsInsideTheCalls()
    {
        var measurement = MeasureOnFreshThread(() => { GC.KeepAlive(new byte[32]); return 1; }, 10, 100, 2);
        Assert.All(measurement.Bytes, allocated => Assert.True(allocated > 0));
        Assert.Equal(210, measurement.Accepted);
    }

    private static (long[] Bytes, int Accepted) MeasureOnFreshThread(Func<int> call, int warmup, int iterations, int samples)
    {
        int accepted = 0;
        for (int i = 0; i < warmup; i++) accepted += call();
        var bytes = new long[samples];
        Exception? failure = null;
        // A reused allocation context can charge its retired tail during background GC.
        var thread = new Thread(() =>
        {
            try
            {
                for (int run = 0; run < samples; run++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < iterations; i++) accepted += call();
                    bytes[run] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        using (ExecutionContext.SuppressFlow()) thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return (bytes, accepted);
    }

    [Fact]
    public void Shared_source_consumers_import_the_same_resource_items_as_the_web_demo()
    {
        string root = WorkshopContractCorpusTests.Root;
        foreach (string directory in new[] { "src", "tests", "tools", "client/Web" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(path);
            Assert.DoesNotContain(project.Descendants("Compile"), item => ((string?)item.Attribute("Include"))?.Replace('\\', '/').Contains("Shared/**/*.cs", StringComparison.Ordinal) == true);
        }
        var web = XDocument.Load(Path.Combine(root, "client/Web/sim/DemoSim.csproj"));
        Assert.Contains(web.Descendants("Import"), x => ((string?)x.Attribute("Project"))?.EndsWith("SharedSources.projitems", StringComparison.Ordinal) == true);
        var items = XDocument.Load(Path.Combine(root, "src/Shared/SharedSources.projitems"));
        Assert.Contains(items.Descendants("EmbeddedResource"), x => (string?)x.Attribute("LogicalName") == "SlopArena.Shared.CharacterSourceSchema.xml");
    }

    [Fact]
    public void Equal_record_operations_report_distinct_indices()
    {
        var input = Example(); var character = JsonNode.Parse(input.Character)!;
        var operations = character["slots"]![2]!["timeline"]!["stages"]![0]!["operations"]!.AsArray();
        var lunge = operations[0]!.DeepClone(); lunge["stopInAttackRange"] = true;
        operations.Clear(); operations.Add(lunge); operations.Add(lunge.DeepClone());
        var result = CharacterPackageCompiler.Compile(input.Manifest, character.ToJsonString());
        Assert.Equal(new[] { "character.slots[2].timeline.stages[0].operations[0].stopInAttackRange", "character.slots[2].timeline.stages[0].operations[1].stopInAttackRange" },
            result.Diagnostics.Where(x => x.Code == "operation.forward-lunge.no-subsequent-hitbox").Select(x => x.Path));
    }

    [Fact]
    public void Template_refuses_existing_files_and_invalid_ids_without_writing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kit-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<ArgumentException>(() => KitTemplate.Write(folder, "INVALID ID", "Fighter"));
            Assert.False(Directory.Exists(folder));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "character.json"), "keep me");
            Assert.Throws<IOException>(() => KitTemplate.Write(folder, "local.valid", "Fighter"));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(folder, "character.json")));
            Assert.False(File.Exists(Path.Combine(folder, "package.json")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Moves_use_the_simulation_launch_and_fixed_stun_functions()
    {
        var hit = new MoveContact("test", 10, 35, 8, 5, 1, 0);
        var result = MovesReport.Evaluate(hit, 90);
        Assert.Equal(3.23f, result.Speed, 4);
        Assert.Equal((ushort)8, result.Hitstun);
        Assert.Equal((ushort)17, MovesReport.Evaluate(hit with { FixedStun = 17 }, 90).Hitstun);
        var input = Example();
        var cooked = CharacterPackageCompiler.Compile(input.Manifest, input.Character).CookedPackage!;
        using var json = JsonDocument.Parse(cooked.CanonicalBytes);
        Assert.Contains(MovesReport.Contacts(json.RootElement), x => x.Path.Contains("ground.1", StringComparison.Ordinal));
        Assert.Contains(MovesReport.Contacts(json.RootElement), x => x.Path.Contains("air.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Capability_only_cues_are_used_and_truly_unused_cues_still_warn()
    {
        string fixture = Path.Combine(WorkshopContractCorpusTests.Root, "tests/Shared.Tests/Fixtures/WorkshopContract/mutation-inputs.json");
        var input = JsonSerializer.Deserialize<MutationInput[]>(File.ReadAllText(fixture))!.Single(x => x.Name == "wibou");
        var character = JsonNode.Parse(input.Character)!;
        var dash = FieldContract.Walk(character, CharacterSourceSchema.Object("character"), Array.Empty<string>())
            .First(x => x.Schema.CapabilityId == CharacterPackageCompiler.DashStrikeCapabilityId);
        string cue = dash.Value["tipPresentationId"]!.GetValue<string>();
        var declarations = character["presentationIds"]!.AsArray();
        int cueIndex = declarations.Select(x => x!.GetValue<string>()).ToList().IndexOf(cue);
        Assert.True(cueIndex >= 0);
        declarations.Add("presentation.truly-unused");
        var result = CharacterPackageCompiler.Compile(input.Manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "presentation.unused-id" && d.Path == $"character.presentationIds[{cueIndex}]");
        Assert.Contains(result.Diagnostics, d => d.Code == "presentation.unused-id" && d.Path == $"character.presentationIds[{declarations.Count - 1}]");
    }

    [Fact]
    public void Anchor_null_and_placed_tether_defaults_are_not_legal_JSON_defaults()
    {
        string fixture = Path.Combine(WorkshopContractCorpusTests.Root, "tests/Shared.Tests/Fixtures/WorkshopContract/mutation-inputs.json");
        var inputs = JsonSerializer.Deserialize<MutationInput[]>(File.ReadAllText(fixture))!;
        var grabInput = inputs.Single(x => x.Name == "grappler");
        var grab = JsonNode.Parse(grabInput.Character)!;
        grab["slots"]![6]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["anchorDistance"] = null;
        Assert.Contains(CharacterPackageCompiler.Compile(grabInput.Manifest, grab.ToJsonString()).Diagnostics,
            d => d.Code == "value.out-of-range" && d.Path.EndsWith(".parameters.anchorDistance", StringComparison.Ordinal));
        var nyx = inputs.Single(x => x.Name == "nyx");
        foreach (var (name, json) in new[] { ("projectile", "null"), ("retractSpeed", "0") })
        {
            var character = JsonNode.Parse(nyx.Character)!;
            var tether = FieldContract.Walk(character, CharacterSourceSchema.Object("character"), Array.Empty<string>())
                .First(x => x.Schema.CapabilityId == CharacterPackageCompiler.TetherCapabilityId && x.Value["atTarget"] != null);
            tether.Value[name] = JsonNode.Parse(json);
            Assert.Contains(CharacterPackageCompiler.Compile(nyx.Manifest, character.ToJsonString()).Diagnostics,
                d => d.Code == "operation.parameter-unknown" && d.Path.EndsWith(".parameters." + name, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("workshop-kit")]
    [InlineData("workshop-grappler")]
    public void Worked_example_fallback_capsules_fit_the_documented_center_frame(string example)
    {
        string path = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples", example);
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(path, "package.json")), File.ReadAllText(Path.Combine(path, "character.json"))).Source!.Character;
        foreach (var capsule in source.HurtboxCapsules)
        {
            Assert.InRange(capsule.StartY + capsule.Radius, -source.CapsuleHeight / 2, source.CapsuleHeight / 2);
            Assert.InRange(capsule.StartY - capsule.Radius, -source.CapsuleHeight / 2, source.CapsuleHeight / 2);
            Assert.InRange(capsule.EndY + capsule.Radius, -source.CapsuleHeight / 2, source.CapsuleHeight / 2);
            Assert.InRange(capsule.EndY - capsule.Radius, -source.CapsuleHeight / 2, source.CapsuleHeight / 2);
        }
    }

    private static MutationInput Example()
    {
        string path = Path.Combine(WorkshopContractCorpusTests.Root, "docs/characters/examples/workshop-kit");
        return new("example", File.ReadAllText(Path.Combine(path, "package.json")), File.ReadAllText(Path.Combine(path, "character.json")));
    }
}
