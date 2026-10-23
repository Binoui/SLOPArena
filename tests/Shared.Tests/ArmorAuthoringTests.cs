using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ArmorAuthoringTests
{
    private static string PackagePath => FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy/package.json");
    private static string CharacterPath => FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy/character.json");

    [Fact]
    public void ArmorWindowAndFixedHitstunRoundTripThroughSourceCookAndRuntimeAdapter()
    {
        var character = JsonNode.Parse(File.ReadAllText(CharacterPath))!.AsObject();
        var stage = character["slots"]!.AsArray()
            .Single(slot => slot!["id"]!.GetValue<string>() == "ground.1")!["timeline"]!["stages"]![0]!;
        stage["operations"]!.AsArray().Add(new JsonObject
        {
            ["kind"] = "armorWindow",
            ["tick"] = 1,
            ["unit"] = "ticks",
            ["durationTicks"] = 12,
        });
        FirstHitbox(character)["fixedHitstunTicks"] = 24;

        var loaded = CharacterPackageSourceCodec.Load(File.ReadAllText(PackagePath), character.ToJsonString());
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics));
        var sourceSlot = loaded.Source!.Character.Slots.Single(slot => slot.Id == "ground.1");
        var sourceArmor = Assert.IsType<ArmorWindowOperationSource>(Assert.Single(
            sourceSlot.Timeline.Stages.Single().Operations.OfType<ArmorWindowOperationSource>()));
        Assert.Equal((AuthoringUnit.Ticks, (ushort)1, (ushort)12),
            (sourceArmor.Unit, sourceArmor.Tick, sourceArmor.DurationTicks));
        var sourceHitbox = sourceSlot.Timeline.Stages.Single().Operations
            .OfType<SpawnHitboxOperationSource>().First().Hitbox;
        Assert.Equal((ushort)24, sourceHitbox.FixedHitstunTicks);

        var serialized = CharacterPackageSourceCodec.SerializeCharacter(loaded.Source.Character);
        var roundTrip = CharacterPackageSourceCodec.Load(File.ReadAllText(PackagePath), serialized);
        Assert.True(roundTrip.IsValid, string.Join("; ", roundTrip.Diagnostics));
        Assert.Equal((ushort)12, Assert.Single(roundTrip.Source!.Character.Slots.Single(slot => slot.Id == "ground.1")
            .Timeline.Stages.Single().Operations.OfType<ArmorWindowOperationSource>()).DurationTicks);
        Assert.Equal((ushort)24, roundTrip.Source!.Character.Slots.Single(slot => slot.Id == "ground.1")
            .Timeline.Stages.Single().Operations.OfType<SpawnHitboxOperationSource>().First().Hitbox.FixedHitstunTicks);

        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(PackagePath), serialized, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Severity == CharacterDiagnosticSeverity.Error);
        var cookedSlot = compiled.CookedPackage.Definition.Slots.Single(slot => slot.Id == "ground.1");
        Assert.Equal((ushort)12, Assert.Single(cookedSlot.Timeline.Stages.Single().Operations
            .OfType<CookedArmorWindowOperation>()).DurationTicks);
        Assert.Equal((ushort)24, cookedSlot.Timeline.Stages.Single().Operations
            .OfType<CookedSpawnHitboxOperation>().First().Hitbox.FixedHitstunTicks);
        var runtime = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
        Assert.Equal((ushort)24, runtime.GetCookedSlotAbility(AbilitySlots.Slot1, false)!.Timeline.Stages[0].ReadHitboxes().First().FixedHitstunTicks);
        var assembly = CharacterPackageAssembler.Assemble(BuildInput(compiled.CookedPackage!));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics));
        var admitted = CookedCharacterPackageLoader.LoadAssembly(assembly);
        Assert.True(admitted.IsValid, string.Join("; ", admitted.Diagnostics));
        var loadedSlot = admitted.Package!.Definition.Slots.Single(slot => slot.Id == "ground.1");
        Assert.Single(loadedSlot.Timeline.Stages.Single().Operations.OfType<CookedArmorWindowOperation>());
        Assert.Equal((ushort)24, loadedSlot.Timeline.Stages.Single().Operations
            .OfType<CookedSpawnHitboxOperation>().First().Hitbox.FixedHitstunTicks);
    }

    [Fact]
    public void InvalidArmorDurationsAndFixedHitstunOverridesAreRejected()
    {
        AssertCompileError(character => AddArmorWindow(character, 0));
        AssertCompileError(character => AddArmorWindow(character, 241));
        AssertCompileError(character => FirstHitbox(character)["fixedHitstunTicks"] = 241);
        AssertCompileError(character =>
        {
            var hitbox = FirstHitbox(character);
            hitbox["stunTicks"] = 0;
            hitbox["fixedHitstunTicks"] = 24;
        });
        AssertCompileError(character => AddArmorWindow(character, 12, unit: "meters"));
    }

    [Fact]
    public void FixedHitstunAllowsTheBoundedMaximum()
    {
        var character = JsonNode.Parse(File.ReadAllText(CharacterPath))!.AsObject();
        FirstHitbox(character)["fixedHitstunTicks"] = 240;
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(PackagePath), character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        Assert.Equal((ushort)240, result.CookedPackage!.Definition.Slots.Single(slot => slot.Id == "ground.1")
            .Timeline.Stages.Single().Operations.OfType<CookedSpawnHitboxOperation>().First().Hitbox.FixedHitstunTicks);
    }

    [Fact]
    public void ZeroFixedHitstunKeepsOrdinaryCookedBytesUnchanged()
    {
        var packageJson = File.ReadAllText(PackagePath);
        var baseline = CharacterPackageCompiler.Compile(
            packageJson, File.ReadAllText(CharacterPath), CharacterCookProfile.TrustedBuiltIn);
        var explicitZeroCharacter = JsonNode.Parse(File.ReadAllText(CharacterPath))!.AsObject();
        FirstHitbox(explicitZeroCharacter)["fixedHitstunTicks"] = 0;
        var explicitZero = CharacterPackageCompiler.Compile(
            packageJson, explicitZeroCharacter.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);

        Assert.NotNull(baseline.CookedPackage);
        Assert.NotNull(explicitZero.CookedPackage);
        Assert.Equal(baseline.CookedPackage!.CanonicalBytes, explicitZero.CookedPackage!.CanonicalBytes);
    }

    private static void AssertCompileError(Action<JsonObject> mutate)
    {
        var character = JsonNode.Parse(File.ReadAllText(CharacterPath))!.AsObject();
        mutate(character);
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(PackagePath), character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == CharacterDiagnosticSeverity.Error);
    }

    private static void AddArmorWindow(JsonObject character, int durationTicks, string unit = "ticks")
    {
        var stage = character["slots"]!.AsArray()
            .Single(slot => slot!["id"]!.GetValue<string>() == "ground.1")!["timeline"]!["stages"]![0]!;
        stage["operations"]!.AsArray().Add(new JsonObject
        {
            ["kind"] = "armorWindow",
            ["tick"] = 1,
            ["unit"] = unit,
            ["durationTicks"] = durationTicks,
        });
    }

    private static JsonObject FirstHitbox(JsonObject character)
    {
        var slot = character["slots"]!.AsArray()
            .Single(candidate => candidate!["id"]!.GetValue<string>() == "ground.1")!;
        foreach (var stage in slot["timeline"]!["stages"]!.AsArray())
        foreach (var operation in stage!["operations"]!.AsArray())
            if (operation!["kind"]!.GetValue<string>() == "spawnHitbox")
                return operation["hitbox"]!.AsObject();
        throw new InvalidDataException("ground.1 fixture has no hitbox operation.");
    }

    private static CharacterPackageAssemblyInput BuildInput(CookedCharacterPackage package)
    {
        const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            package.Definition.Presentation.Idle, package.Definition.Presentation.Run,
            package.Definition.Presentation.Dash, package.Definition.Presentation.Jump,
            package.Definition.Presentation.Fall, package.Definition.Presentation.HitSmall,
            package.Definition.Presentation.HitMedium, package.Definition.Presentation.HitHard,
        };
        foreach (var name in new[]
        {
            package.Definition.Presentation.Tumble,
            package.Definition.Presentation.Crouch,
            package.Definition.Presentation.Slide,
            package.Definition.Presentation.Shield,
            package.Definition.Presentation.Grab,
            package.Definition.Presentation.Grabbed,
            package.Definition.Presentation.ThrowForward,
            package.Definition.Presentation.AirDodge,
        })
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        foreach (var slot in package.Definition.Slots)
        {
            if (!string.IsNullOrEmpty(slot.AimAnimationId)) names.Add(slot.AimAnimationId);
            foreach (var stage in slot.Timeline.Stages)
            foreach (var animation in stage.AnimationIds)
                names.Add(animation);
        }
        var binding = new StringBuilder("{\"packageId\":")
            .Append(JsonSerializer.Serialize(package.Metadata.PackageId))
            .Append(",\"catalogSchemaVersion\":1,\"bindingSchemaVersion\":1,\"poseFormat\":\"SKEL\",\"poseVersion\":1,\"sampleRate\":60,\"sourceHash\":\"")
            .Append(sourceHash).Append("\",\"rigGlobalObjectId\":\"rig\",\"animations\":[");
        bool first = true;
        foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!first) binding.Append(',');
            first = false;
            binding.Append("{\"semanticId\":").Append(JsonSerializer.Serialize(name))
                .Append(",\"poseTrackId\":").Append(JsonSerializer.Serialize(name))
                .Append(",\"clipGlobalObjectId\":\"clip\",\"poseName\":")
                .Append(JsonSerializer.Serialize(name))
                .Append(",\"frameCount\":1,\"clipLengthBits\":0,\"sampleRate\":60,\"extrapolation\":0}");
        }
        binding.Append("],\"presentations\":[");
        first = true;
        foreach (var id in package.Definition.PresentationIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!first) binding.Append(',');
            first = false;
            binding.Append("{\"semanticId\":").Append(JsonSerializer.Serialize(id))
                .Append(",\"prefabGlobalObjectId\":\"prefab\"}");
        }
        binding.Append("]}");

        using var poses = new MemoryStream();
        WriteUInt32(poses, 0x4C454B53); WriteUInt32(poses, 1); WriteUInt32(poses, 1);
        WriteUInt32(poses, (uint)names.Count);
        WriteString(poses, "root");
        foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
        {
            WriteString(poses, name);
            WriteUInt32(poses, 1); WriteUInt32(poses, 0); WriteUInt32(poses, 0); WriteUInt32(poses, 0);
        }
        return new CharacterPackageAssemblyInput(
            package.Metadata.PackageId, package.Metadata.Version, "Binoui", "MIT", "SlopArena", 3,
            package.Metadata.CookedSchemaVersion, package.Metadata.RuntimeApiMin, package.Metadata.RuntimeApiMax,
            sourceHash, Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            "test-cooker", "test-unity", 1, "SKEL", 1, 60, package.Diagnostics,
            package.CanonicalBytes, poses.ToArray(), Encoding.UTF8.GetBytes(binding.ToString()), package);
    }

    private static void WriteUInt32(Stream stream, uint value)
        => stream.Write(BitConverter.GetBytes(value), 0, sizeof(uint));

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }
    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
