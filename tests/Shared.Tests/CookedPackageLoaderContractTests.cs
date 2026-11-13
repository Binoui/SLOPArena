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
    [InlineData("manki", "8BDE5BE4150CCF596884E145915EB6ACC13A80EC1C31E212861C4DD2EA72AA68", "46067F39C401DE36923191BC90DEB59368200903161AC0AF982DC24091291F3C")]
    [InlineData("fightguy", "B1B45CBEE882717850834743FF4423C14325762A27ED71030E1AC081E079782A", "AC094CB816FF4ABBA0BBC117C9DC28EB6FD84263DF19789ED354BC9C50C6FE9E")]
    [InlineData("wibou", "62855D216CC779C2D58F72D1C0211B56DFE098743F85737ECC858EE4BC9526C7", "06C442141EE6D172A23ED2916F674173DDE64D282E1F09CD4A0E0326D147957D")]
    [InlineData("bonk", "CC7C89C12D1AB5D81A04EB5265B5B7B821CFDF18D530E6A200017A6A89665DFF", "EB1DC68F62B0F68BF4CC07697A8B9C18885A59BC115312920A222E8233378314")]
    [InlineData("bashie", "77B276AEE8B32D96EC8167801666B1562C38608DB9CFDB85A353E987E7302658", "983FA3FBB9B0B008A1B21983C3774F19FBAD7C9E263F8CF8F72DF8D963119E47")]
    [InlineData("salamander", "461E510A33C1846B34EBB2A97895B7B17764041752E604806F117A2254AE1F15", "4B9C4261B292FDE7104F6AE54A60CAF6327EF48460F5D3D87F0A0A5DBD8E5435")]
    [InlineData("nyx", "7D6E8A0768E2F76310A25FC23D1143AF5766153065485DDEDF79997D282F5034", "4348694F49A668E32FA38974FBA2CC489CD7DA2D9C09CDE4FD4970BAE567BB1B")]
    public void TrustedRosterMatchesOriginMainRuntimeAndPoses(string packageId, string definitionHash, string poseHash)
    {
        var loaded = RunWithWallClock(() => CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped(packageId),
            CookedPackageEdits.Roster.ResolvePackage(packageId)!.Requirement, CharacterCookProfile.TrustedBuiltIn));
        Assert.True(loaded.IsValid, string.Join(";", loaded.Diagnostics));
        string Hash(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)));
        Assert.Equal(definitionHash, Hash(loaded.Package!.Definition));
        Assert.Equal(poseHash, Hash(loaded.BakedAnimation!));
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
        Assert.True(report.PeakHitboxes <= CookedPackageLimits.LiveHitboxes, report.ToString());
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
