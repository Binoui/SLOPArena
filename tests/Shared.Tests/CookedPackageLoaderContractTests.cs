using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Workshop roadmap step 0.4: how the cooked package loader behaves on values its own compiler never writes.
/// Contract: LoadFiles never throws, and a package that loads plays a short scripted match cleanly.
/// Admission limits use deterministic counts; the timeout detects a hung load or match.
/// </summary>
public sealed class CookedPackageLoaderContractTests
{
    private const int StepTicks = 80;
    private static readonly TimeSpan MatchWallClock = TimeSpan.FromSeconds(60);
    private static readonly byte[] ScriptedSlots =
    {
        AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3,
        AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
    };
    private static readonly int MatchTicks = ScriptedSlots.Length * 2 * StepTicks;

    private static readonly Lazy<ArenaDefinition> Arena = new(() =>
        ArenaBinaryFormat.LoadFromFile(Path.Combine(CookedPackageEdits.Root, "data/arenas/slop_pit.arena"))!.Value);
    private static readonly Lazy<Dictionary<string, CookedCharacterPackageLoadResult>> ShippedLoads = new(() =>
        CookedPackageEdits.Roster.Entries.ToDictionary(e => e.PackageId,
            e => CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped(e.PackageId), e.Requirement), StringComparer.Ordinal));

    private readonly ITestOutputHelper _output;
    public CookedPackageLoaderContractTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> Packages()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CookedPackageEdits.Roster.Entries) data.Add(entry.PackageId);
        return data;
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Helper_WithNoEdit_ReturnsTheShippedBytes(string packageId)
    {
        var shipped = CookedPackageEdits.Shipped(packageId);
        var copy = CookedPackageEdits.Edit(packageId, _ => { });
        foreach (var (path, bytes) in shipped)
            Assert.True(bytes.AsSpan().SequenceEqual(copy[path]), $"{packageId}/{path} changed with no edit");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Helper_WithAnEdit_PassesTheHashCheck(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p => p.Slot("ground.1")["name"] = "Edited");
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files, CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics));
        Assert.Equal("Edited", result.Package!.Definition.AllSlots.Single(s => s.Id == "ground.1" && s.Form == 0).Name);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void UnmodifiedPackage_PlaysTheScriptedMatchInsideItsBudget(string packageId)
    {
        AssertPlays(CookedPackageEdits.Shipped(packageId));
    }

    // A. Five things code reading suggested.

    [Fact]
    public void A1_UndeclaredInternalCapability_IsRefused()
    {
        const string capability = "slop.internal.fightguy.cyclone-kick.v1";
        var files = CookedPackageEdits.Edit("bonk", p =>
        {
            p.Runtime["character"]!["capabilityRequirements"]!.AsArray().Add(Capability(capability));
            p.Manifest["capabilityRequirements"]!.AsArray().Add(Capability(capability));
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files, CookedPackageEdits.RequirementOf(files), CharacterCookProfile.Workshop));
        Assert.Contains(result.Diagnostics, d => d.Code == "capability.untrusted");
    }

    [Fact]
    public void A2_BudgetThatDisagreesWithContent_IsNotTrusted()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            p.Runtime["budget"]!["hitboxCount"] = 0;
            p.Runtime["budget"]!["operationCount"] = 1;
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files, CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Code == "package.budget.mismatch");
    }

    [Fact]
    public void A3_TimelineWithZeroStages_IsRefusedOrPlays()
    {
        // air.F keeps every animation and presentation bound, so no orphan-binding check refuses the edit first.
        var files = CookedPackageEdits.Edit("bonk", p => p.Slot("ground.F")["timeline"]!["stages"] = new JsonArray());
        AssertRefusedOrPlays(files);
    }

    [Theory]
    [InlineData("fightguy")]
    [InlineData("bonk")]
    public void A4a_OperationListFarPastTheCompilerLimit_IsRefused(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            var operations = p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            var presentation = operations.Last(op => op!["kind"]!.GetValue<int>() == (int)CookedOperationKind.EmitPresentation)!;
            for (int i = 0; i < 20_000; i++) operations.Add(presentation.DeepClone());
        });
        AssertRefused(files, _ => true);
    }

    [Theory]
    [InlineData("fightguy")]
    [InlineData("bonk")]
    public void A4b_ThousandsOfHitboxesOnOneTick_AreRefused(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            var operations = p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray();
            int at = operations.ToList().FindIndex(op => op!["kind"]!.GetValue<int>() == (int)CookedOperationKind.SpawnHitbox);
            var hitbox = operations[at]!;
            for (int i = 0; i < 2_000; i++) operations.Insert(at, hitbox.DeepClone());
        });
        AssertRefused(files, _ => true);
    }

    [Fact]
    public void A4c_PoseHeaderClaimingFramesItDoesNotHold_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            var poses = (byte[])p.Poses.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                poses.AsSpan(CookedPackageEdits.PoseOffsets(poses).FrameCountOffset), 1_000_000);
            p.Poses = poses;
        });
        AssertRefused(files, code => code == "package.pose.malformed");
    }

    [Fact]
    public void A5_ProjectileWithHugeLifetimeAndSpeed_IsRefused()
    {
        var files = CookedPackageEdits.Edit("salamander", p =>
        {
            foreach (var id in new[] { "ground.R", "air.R" })
            {
                var projectile = p.Slot(id)["timeline"]!["stages"]![0]!["operations"]!.AsArray()
                    .Single(op => op!["kind"]!.GetValue<int>() == (int)CookedOperationKind.SpawnProjectile)!["projectile"]!;
                projectile["maxFlightTicks"] = ushort.MaxValue;
                projectile["speed"] = 100_000f;
            }
        });
        AssertRefused(files, _ => true);
    }

    // B. One representative per field kind in character.runtime.json.

    [Fact]
    public void B1_CountSetToZero_MaxJumps_Plays()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Runtime["character"]!["movement"]!["maxJumps"] = 0);
        AssertPlays(files);
    }

    [Fact]
    public void B2_NegativeDuration_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Slot("ground.1")["timeline"]!["stages"]![0]!["durationTicks"] = -1);
        AssertRefused(files, code => code is "package.runtime.malformed" or "package.operation.tick");
    }

    [Fact]
    public void B3_VeryLargeNumber_HitboxKnockback_IsRefusedOrPlays()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            foreach (var op in p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray())
                if (op!["kind"]!.GetValue<int>() == (int)CookedOperationKind.SpawnHitbox)
                {
                    op["hitbox"]!["baseKnockback"] = 3e38f;
                    op["hitbox"]!["knockbackGrowth"] = 3e38f;
                }
        });
        AssertRefusedOrPlays(files);
    }

    [Fact]
    public void B4_UnknownEnumValue_SlotBehavior_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Slot("ground.1")["behavior"] = 200);
        AssertRefused(files, _ => true);
    }

    [Fact]
    public void B5_VeryLongString_SlotName_Plays()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Slot("ground.1")["name"] = new string('x', 1_000_000));
        AssertPlays(files);
    }

    [Fact]
    public void B6_RemovedSlot_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Runtime["character"]!["slots"]!.AsArray().Remove(p.Slot("ground.E")));
        AssertRefused(files, _ => true);
    }

    // C. poses.bin.

    [Fact]
    public void C1_ShortenedPoseData_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Poses = p.Poses[..^12]);
        AssertRefused(files, code => code == "package.pose.malformed");
    }

    [Fact]
    public void C2_ZeroPoseCounts_AreRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            var poses = (byte[])p.Poses.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(poses.AsSpan(12), 0);
            p.Poses = poses;
        });
        AssertRefused(files, code => code == "package.pose.malformed");
    }

    [Fact]
    public void C3_NaNPoseSample_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            var poses = (byte[])p.Poses.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(
                poses.AsSpan(CookedPackageEdits.PoseOffsets(poses).FirstSampleOffset), float.NaN);
            p.Poses = poses;
        });
        AssertRefused(files, code => code == "package.pose.malformed");
    }

    [Fact]
    public void C4_ExtraBytesAfterPoseData_AreRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p => p.Poses = p.Poses.Concat(new byte[4]).ToArray());
        AssertRefused(files, code => code == "package.pose.malformed");
    }

    // D. client.bindings.

    [Fact]
    public void D1_MissingPoseTrackTheRuntimeUses_IsRefused()
    {
        var files = CookedPackageEdits.Edit("fightguy", p =>
        {
            var animations = p.Bindings["animations"]!.AsArray();
            animations.Remove(animations.Single(a => a!["semanticId"]!.GetValue<string>() == "anim.straight-punch"));
        });
        AssertRefused(files, code => code == "package.binding.missing");
    }

    // E. manifest.json.

    [Fact]
    public void E1_NonEmptyDependencyList_IsRefused()
    {
        var files = CookedPackageEdits.Edit("bonk", p => p.Manifest["dependencies"]!.AsArray().Add(new JsonObject
        {
            ["packageId"] = "fightguy",
            ["version"] = "0.0.0-dev",
            ["cookedHash"] = new string('a', 64),
        }));
        AssertRefused(files, code => code == "package.dependencies.unsupported");
    }

    [Fact]
    public void E2_UnsupportedCookedSchemaVersion_IsRefused()
    {
        var files = CookedPackageEdits.Edit("bonk", p =>
        {
            p.Manifest["cookedSchemaVersion"] = 6;
            p.Runtime["metadata"]!["cookedSchemaVersion"] = 6;
        });
        AssertRefused(files, code => code == "package.compatibility.unsupported");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void StructuralAndWorkLimits_ApplyAcrossRoster(string packageId)
    {
        foreach (var edit in new Action<CookedPackageEdits.PackageDraft>[]
        {
            p => p.Slot("ground.1")["behavior"] = 200,
            p => p.Runtime["character"]!["slots"]!.AsArray().Remove(p.Slot("ground.E")),
            p => p.Slot("ground.F")["timeline"]!["stages"] = new JsonArray(),
            p => p.Runtime["budget"]!["operationCount"] = -1,
            p =>
            {
                var ops = p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray();
                var sample = ops[0]!.DeepClone();
                for (int i = 0; i < CookedPackageLimits.OperationsPerStage + 1; i++) ops.Add(sample.DeepClone());
            },
        })
        {
            var files = CookedPackageEdits.Edit(packageId, edit);
            var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
                CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
            Assert.False(result.IsValid);
        }
    }

    [Theory]
    [InlineData("bonk")]
    [InlineData("bashie")]
    [InlineData("nyx")]
    public void WorkshopCannotDeclareInternalCapability(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            p.Runtime["character"]!["capabilityRequirements"]!.AsArray().Add(Capability("slop.internal.fightguy.cyclone-kick.v1"));
            p.Manifest["capabilityRequirements"]!.AsArray().Add(Capability("slop.internal.fightguy.cyclone-kick.v1"));
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.RequirementOf(files), CharacterCookProfile.Workshop));
        Assert.Contains(result.Diagnostics, d => d.Code == "capability.untrusted");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void OversizedFileIsRefusedBeforeParsing(string packageId)
    {
        var files = CookedPackageEdits.Shipped(packageId);
        files[CharacterPackageAssembler.RuntimePath] = new byte[CookedPackageLimits.RuntimeBytes + 1];
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.Roster.ResolvePackage(packageId)!.Requirement));
        Assert.Contains(result.Diagnostics, d => d.Code == "package.file.limit");
    }

    [Theory]
    [InlineData("manki", "683B1CDE1F803D2375E543AB19AC7F989D80E65B6FF29A6A0ECCD2D0B8CDB8EA", "46067F39C401DE36923191BC90DEB59368200903161AC0AF982DC24091291F3C")]
    [InlineData("fightguy", "85CC8C42FA4DD0592D42179FCDA97207532EF22F503777D652D808F12A7EC2D3", "AC094CB816FF4ABBA0BBC117C9DC28EB6FD84263DF19789ED354BC9C50C6FE9E")]
    [InlineData("wibou", "A549D7BD1CC6D7A3665483FF231D1077839DD0A449E49E8AD00B2A8DCCA8572B", "06C442141EE6D172A23ED2916F674173DDE64D282E1F09CD4A0E0326D147957D")]
    [InlineData("bonk", "F143529388855C15422964C80DF184E99C4FD8C2BDB33682ED39399E7417B2FB", "EB1DC68F62B0F68BF4CC07697A8B9C18885A59BC115312920A222E8233378314")]
    [InlineData("bashie", "BF56195A820DD1DB0989E61E72EE4ED1A8820CB49991EA2BB26C7CF97DD86CC4", "983FA3FBB9B0B008A1B21983C3774F19FBAD7C9E263F8CF8F72DF8D963119E47")]
    [InlineData("salamander", "1AADD5734E0F27344D254D1ED718FEF50E1BC395A654D135CE62318F5473A422", "4B9C4261B292FDE7104F6AE54A60CAF6327EF48460F5D3D87F0A0A5DBD8E5435")]
    [InlineData("nyx", "D25E07263BF4519B4F9E064ADEC7106383E3F11E0BEF9D480CC2F995A0774CAC", "4348694F49A668E32FA38974FBA2CC489CD7DA2D9C09CDE4FD4970BAE567BB1B")]
    public void TrustedRosterMatchesOriginMainRuntimeAndPoses(string packageId, string definitionHash, string poseHash)
    {
        var loaded = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped(packageId),
            CookedPackageEdits.Roster.ResolvePackage(packageId)!.Requirement, CharacterCookProfile.TrustedBuiltIn));
        Assert.True(loaded.IsValid, string.Join(";", loaded.Diagnostics));
        string Hash(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)));
        Assert.Equal(definitionHash, RuntimeFingerprint(loaded.Package!));
        Assert.Equal(poseHash, Hash(loaded.BakedAnimation!));
    }

    internal static string RuntimeFingerprint(CookedCharacterPackage package)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            CharacterPackageCompiler.WriteCanonical(package.Metadata, package.Definition, package.Budget)));

    [Fact]
    public void RuntimeFingerprintIncludesConcreteOperationPayloadsAndBudgets()
    {
        var original = CookedPackageEdits.Shipped("fightguy");
        var edited = CookedPackageEdits.Edit("fightguy", p =>
            p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray()
                .First(o => o!["hitbox"] != null)!["hitbox"]!["damage"] = 99);
        CookedCharacterPackage Load(Dictionary<string, byte[]> files)
        {
            var result = CookedCharacterPackageLoader.LoadFiles(files, CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn);
            Assert.True(result.IsValid, string.Join(";",result.Diagnostics)); return result.Package!;
        }
        var package = Load(original);
        Assert.NotEqual(RuntimeFingerprint(package), RuntimeFingerprint(Load(edited)));
        Assert.NotEqual(RuntimeFingerprint(package), RuntimeFingerprint(new CookedCharacterPackage(package.Metadata, package.Definition, package.Budget with { OperationCount = package.Budget.OperationCount + 1 }, package.Diagnostics, package.CanonicalBytes)));
    }

    [Theory]
    [InlineData("bonk", "ground.A")]
    [InlineData("nyx", "ground.A")]
    [InlineData("salamander", "ground.R")]
    public void ProjectileAndLiveLimitsRejectRehashedData(string packageId, string slotId)
    {
        foreach (string field in new[] { "speed", "maxFlightTicks" })
        {
            var files = CookedPackageEdits.Edit(packageId, p =>
            {
                var projectile = p.Slot(slotId)["timeline"]!["stages"]![0]!["operations"]!.AsArray()
                    .First(o => o!["projectile"] != null)!["projectile"]!;
                projectile[field] = 60_000;
            });
            var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
                CookedPackageEdits.RequirementOf(files), CharacterCookProfile.Workshop));
            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics, d => d.Code is "package.projectile.limit" or "package.live.limit");
        }
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void LiveHitboxLifetimeLimitRejectsSmallOperationLists(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            var hitbox = p.Slot("ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray()
                .First(o => o!["hitbox"] != null)!["hitbox"]!;
            hitbox["durationTicks"] = ushort.MaxValue;
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.Contains(result.Diagnostics, d => d.Code == "package.live.limit");
    }

    [Fact]
    public void TrustedLoadCannotBecomeAnExternalAdmission()
    {
        var loaded = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped("bonk"),
            CookedPackageEdits.Roster.ResolvePackage("bonk")!.Requirement, CharacterCookProfile.TrustedBuiltIn));
        Assert.True(loaded.IsValid);
        Assert.Throws<ArgumentException>(() => new AdmittedFighter(loaded, null, null));
    }

    [Fact]
    public void DirectoryOversizedFileIsRefusedWithNamedLimit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sloparena-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var file = File.Create(Path.Combine(directory, CharacterPackageAssembler.RuntimePath)))
                file.SetLength(CookedPackageLimits.RuntimeBytes + 1L);
            var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadDirectory(directory,
                CookedPackageEdits.Roster.ResolvePackage("bonk")!.Requirement));
            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics, d => d.Code == "package.file.limit");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("bonk")]
    [InlineData("manki")]
    public void BodyHurtboxesCannotMultiplyCollisionWork(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            var bodies = p.Runtime["character"]!["hurtboxBoneDefs"]!.AsArray();
            var sample = bodies[0]!.DeepClone();
            for (int i = 0; i < 2_000; i++) bodies.Add(sample.DeepClone());
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.Contains(result.Diagnostics, d => d.Code == "package.hurtboxes.limit");
    }

    [Theory]
    [InlineData("bonk")]
    [InlineData("manki")]
    public void OversizedLookupIdentifiersAreRefused(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p => p.Slot("ground.1")["iconId"] =
            new string('x', CookedPackageLimits.IdentifierCharacters + 1));
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.Contains(result.Diagnostics, d => d.Code == "package.identifier.limit");
    }

    [Theory]
    [InlineData("bonk")]
    [InlineData("manki")]
    public void OversizedPoseLookupNamesAreRefused(string packageId)
    {
        var files = CookedPackageEdits.Edit(packageId, p =>
        {
            int oldLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.Poses.AsSpan(16));
            string oldName = System.Text.Encoding.UTF8.GetString(p.Poses, 20, oldLength);
            string name = new('x', CookedPackageLimits.PoseIdentifierBytes + 1);
            var bytes = System.Text.Encoding.UTF8.GetBytes(name);
            var poses = new byte[p.Poses.Length - oldLength + bytes.Length];
            p.Poses.AsSpan(0, 16).CopyTo(poses);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(poses.AsSpan(16), (uint)bytes.Length);
            bytes.CopyTo(poses, 20);
            p.Poses.AsSpan(20 + oldLength).CopyTo(poses.AsSpan(20 + bytes.Length));
            p.Poses = poses;
            void Rename(JsonNode? node)
            {
                if (node is JsonObject obj)
                    foreach (var pair in obj.ToArray())
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && text == oldName)
                            obj[pair.Key] = name;
                        else Rename(pair.Value);
                if (node is JsonArray array)
                    for (int j = 0; j < array.Count; j++)
                        if (array[j] is JsonValue value && value.TryGetValue<string>(out var text) && text == oldName)
                            array[j] = name;
                        else Rename(array[j]);
            }
            Rename(p.Runtime);
            Rename(p.Bindings);
        });
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files,
            CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        Assert.Contains(result.Diagnostics, d => d.Code == "package.pose.malformed");
    }

    private static JsonObject Capability(string id) => new() { ["capabilityId"] = id, ["capabilityVersion"] = "1" };

    private CookedCharacterPackageLoadResult Load(IReadOnlyDictionary<string, byte[]> files)
    {
        var result = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(files, CookedPackageEdits.RequirementOf(files), CharacterCookProfile.TrustedBuiltIn));
        _output.WriteLine(result.IsValid ? "loaded" : "refused: " + string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        return result;
    }

    private void AssertRefused(IReadOnlyDictionary<string, byte[]> files, Func<string, bool> expectedCode)
    {
        var result = Load(files);
        if (result.IsValid)
            Assert.Fail("Loaded, expected refusal. Scripted match: " + Describe(() => Play(files, result)));
        Assert.Contains(result.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error && expectedCode(d.Code));
    }

    private void AssertPlays(IReadOnlyDictionary<string, byte[]> files)
    {
        var result = Load(files);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics));
        _output.WriteLine(Play(files, result).ToString());
    }

    private void AssertRefusedOrPlays(IReadOnlyDictionary<string, byte[]> files)
    {
        var result = Load(files);
        if (result.IsValid) _output.WriteLine(Play(files, result).ToString());
        else Assert.Contains(result.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
    }

    private static string Describe(Func<MatchReport> play)
    {
        try { return play().ToString(); }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message} at {ex.StackTrace?.Split('\n')[0].Trim()}"; }
    }

    /// <summary>Runs a scripted match with a hang guard and checks live effect counts and finite state.</summary>
    private MatchReport Play(IReadOnlyDictionary<string, byte[]> files, CookedCharacterPackageLoadResult loaded)
    {
        string packageId = loaded.Identity.PackageId;
        var report = RunWithWallClock(() => RunMatch(packageId, loaded, CookedPackageEdits.RequirementOf(files)));
        Assert.True(report.TicksRun == MatchTicks, $"Match stopped at tick {report.TicksRun} of {MatchTicks}.");
        Assert.True(report.AllFinite, $"A fighter left finite space by tick {report.FirstNonFiniteTick}.");
        Assert.True(report.PeakHitboxes <= CookedPackageLimits.LiveVolumesPerWorld, report.ToString());
        return report;
    }

    private static T RunWithWallClock<T>(Func<T> run)
    {
        var task = Task.Factory.StartNew(run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (!task.Wait(MatchWallClock))
                throw new TimeoutException($"Scripted match did not finish within {MatchWallClock.TotalSeconds} s.");
        }
        catch (AggregateException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
        return task.Result;
    }

    private static MatchReport RunMatch(string packageId, CookedCharacterPackageLoadResult loaded, MatchContentPackageRequirement requirement)
    {
        var roster = new BuiltInRosterManifest(BuiltInRosterManifest.CurrentSchemaVersion, CookedPackageEdits.Roster.Entries
            .Select(e => e.PackageId == packageId ? e with { Requirement = requirement } : e).ToList());
        var loads = new Dictionary<string, CookedCharacterPackageLoadResult>(ShippedLoads.Value, StringComparer.Ordinal) { [packageId] = loaded };
        var build = new MatchContentCatalogBuilder().Build(roster, loads);
        if (build.Catalog == null) throw new InvalidOperationException("Catalog refused a loaded package: " + string.Join("; ", build.Diagnostics));
        var fighter = build.Catalog.ResolvePackage(packageId)!;
        var opponent = build.Catalog.ResolvePackage(packageId == "fightguy" ? "bonk" : "fightguy")!;

        var arena = Arena.Value;
        var spawn = arena.SpawnPoints[0];
        var across = arena.SpawnPoints[1];
        float dx = across.X - spawn.X, dz = across.Z - spawn.Z, length = MathF.Sqrt(dx * dx + dz * dz);
        dx /= length; dz /= length;
        float yaw = MathF.Atan2(dx, dz);
        short aimYaw = (short)MathF.Round(yaw * 180f / MathF.PI * 100f);

        var sim = new ServerSimulation(arena, new StockMatchRule(250));
        sim.RegisterEntity(1, fighter.Definition, Spawned(1, spawn.X, spawn.Y, spawn.Z, yaw, fighter.Definition), fighter.BakedAnimation);
        sim.RegisterEntity(2, opponent.Definition, Spawned(2, spawn.X + dx * 1.5f, spawn.Y, spawn.Z + dz * 1.5f, yaw + MathF.PI, opponent.Definition), opponent.BakedAnimation);

        int peakHitboxes = 0, firstNonFinite = -1, tick = 0;
        for (; tick < MatchTicks; tick++)
        {
            var inputs = new Dictionary<ulong, InputState> { [1] = ScriptedInput(tick, aimYaw), [2] = default };
            sim.Tick(inputs);
            peakHitboxes = Math.Max(peakHitboxes, sim.Resolver.GetActiveHitboxes().Count);
            if (firstNonFinite < 0 && !(Finite(sim.GetState(1)) && Finite(sim.GetState(2)))) firstNonFinite = tick;
        }
        return new MatchReport(tick, peakHitboxes, firstNonFinite);
    }

    private static CharacterState Spawned(ulong id, float x, float y, float z, float yaw, CharacterDefinition definition) => new()
    {
        EntityId = id, PX = x, PY = y, PZ = z, FacingYaw = yaw, IsGrounded = true,
        JumpsLeft = definition.Movement.MaxJumps, AirDodgesLeft = 1,
    };

    private static bool Finite(CharacterState s) =>
        float.IsFinite(s.PX) && float.IsFinite(s.PY) && float.IsFinite(s.PZ) &&
        float.IsFinite(s.VX) && float.IsFinite(s.VY) && float.IsFinite(s.VZ);

    /// <summary>Each scripted slot is pressed once grounded, then once airborne eight ticks after a jump.</summary>
    private static InputState ScriptedInput(int tick, short aimYaw)
    {
        int step = tick / StepTicks, t = tick % StepTicks;
        var input = new InputState { AimYaw = aimYaw, FacingYaw = aimYaw };
        byte slot = ScriptedSlots[step % ScriptedSlots.Length];
        if (step < ScriptedSlots.Length)
        {
            if (t == 0) input.ActiveSlot = slot;
        }
        else
        {
            input.Jump = t == 0;
            input.JumpHeld = t < 10;
            if (t == 8) input.ActiveSlot = slot;
        }
        return input;
    }

    private sealed record MatchReport(int TicksRun, int PeakHitboxes, int FirstNonFiniteTick)
    {
        public bool AllFinite => FirstNonFiniteTick < 0;
        public override string ToString() =>
            $"ran {TicksRun} ticks, peak live hitboxes {PeakHitboxes}" +
            (AllFinite ? "" : $", non-finite fighter state from tick {FirstNonFiniteTick}");
    }
}
