using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Package-native Manki coverage: canonical kit validation, cooked capability loading,
/// real normal contacts, and special regression scenarios.
/// </summary>
public sealed class MankiKitScenarioTests : KitScenarioTests
{
    private const string RoundBombCapabilityId = "slop.internal.manki.proximity-mine.v1";
    private const string JetpackCapabilityId = "slop.internal.manki.jetpack-boost.v3";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v3";
    private const string KamikazeCapabilityId = "slop.internal.manki.kamikaze.v1";

    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static float GroundPy => TestHelpers.GroundPY(Def);

    private static CharacterState GroundedPlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        return state;
    }

    private static CharacterState AirbornePlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        state.IsGrounded = false;
        return state;
    }

    private static CharacterState AirborneNpc(float z)
    {
        var state = TestHelpers.NpcState(0f, z);
        state.PY = 3f;
        state.IsGrounded = false;
        return state;
    }

    [Fact]
    public void Manki_TrustedPackage_CooksValidCanonicalKit()
    {
        var first = CompileManki();
        var second = CompileManki();
        Assert.True(first.CookedPackage != null, string.Join("; ", first.Diagnostics));
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(4, package.Definition.CapabilityRequirements.Count);
        Assert.Equal(new[] { BazookaCapabilityId, JetpackCapabilityId, KamikazeCapabilityId, RoundBombCapabilityId }.OrderBy(x => x),
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x).ToArray());
        Assert.All(package.Definition.CapabilityRequirements, x => Assert.Equal("1", x.CapabilityVersion));
        var expectedSlots = new[]
        {
            "ground.1", "ground.3", "ground.4", "ground.A", "ground.E", "ground.R", "ground.F",
            "air.1", "air.3", "air.4", "air.A", "air.E", "air.R", "air.F",
        };
        Assert.Equal(expectedSlots.OrderBy(x => x),
            package.Definition.Slots.Select(x => x.Id).OrderBy(x => x));
        foreach (var slot in package.Definition.Slots.Where(x => x.Ordinal % CanonicalSlotProjection.VariantSlotCount < 3))
        {
            var stage = Assert.Single(slot.Timeline.Stages);
            var hitboxes = stage.Operations.OfType<CookedSpawnHitboxOperation>().ToArray();
            Assert.NotEmpty(hitboxes);
            Assert.All(hitboxes, operation =>
            {
                Assert.InRange(operation.Tick + operation.Hitbox.DurationTicks, 1, stage.DurationTicks);
                Assert.True(operation.Hitbox.Damage > 0f);
            });
        }

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", JetpackCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        var jetpackSlot = package.Definition.Slots.Single(x => x.Id == "ground.E");
        Assert.True(jetpackSlot.IsRecoveryMove);
        Assert.False(jetpackSlot.PreserveMomentumOnStart);
        AssertSpecial(package, "ground.F", KamikazeCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiKamikazeCapabilityParameters>(p));
    }

    private static void AssertSpecial(CookedCharacterPackage package, string slotId, string capabilityId,
        AuthoringAbilityBehavior behavior, AuthoringAimMode aimMode,
        Action<CookedCapabilityParameters> parameterAssert)
    {
        var slot = package.Definition.Slots.Single(x => x.Id == slotId);
        Assert.Equal(behavior, slot.Behavior);
        Assert.Equal(aimMode, slot.AimMode);
        var operation = Assert.Single(slot.Timeline.Stages.Single().Operations
            .OfType<CookedStartCapabilityOperation>());
        Assert.Equal((ushort)0, operation.Tick);
        Assert.Equal(capabilityId, operation.CapabilityId);
        Assert.Equal("1", operation.CapabilityVersion);
        parameterAssert(operation.Parameters);
    }

    [Fact]
    public void MankiCookedArtifact_EnforcesPinnedRosterIdentity()
    {
        var root = Path.GetDirectoryName(RepoFile("content-cooked/manki/manifest.json"))!;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            [CharacterPackageAssembler.RuntimePath] = File.ReadAllBytes(Path.Combine(root, "character.runtime.json")),
            [CharacterPackageAssembler.PosePath] = File.ReadAllBytes(Path.Combine(root, "poses.bin")),
            [CharacterPackageAssembler.BindingPath] = File.ReadAllBytes(Path.Combine(root, "client.bindings")),
        };
        var roster = BuiltInRosterManifestCodec.Load(RepoFile("content-cooked/roster/manifest.json"));
        var rosterEntry = roster.Resolve(CharacterClass.Manki);
        Assert.NotNull(rosterEntry);
        Assert.Equal("manki", rosterEntry!.PackageId);
        var loaded = CookedCharacterPackageLoader.LoadFiles(files, rosterEntry.Requirement);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(x => x.Message)));
        var stale = CookedCharacterPackageLoader.LoadFiles(
            files,
            rosterEntry.Requirement with { PackageHash = new string('0', 64) });
        Assert.False(stale.IsValid);
        Assert.Contains(stale.Diagnostics, x => x.Code == "package.identity.mismatch");
    }
    [Theory]
    [InlineData(AbilitySlots.Slot1, false, 1f, 80)]
    [InlineData(AbilitySlots.Slot4, false, 0.8f, 100)]
    [InlineData(AbilitySlots.Slot1, true, 0f, 90)]
    [InlineData(AbilitySlots.Slot4, true, 0f, 120)]
    public void NormalContact_TriggersVictimReaction_AndAttackerRecovers(
        byte slot, bool airborne, float distance, int totalTicks)
    {
        var sim = TestHelpers.MakeSim();
        var player = airborne ? AirbornePlayer() : GroundedPlayer();
        var npc = airborne
            ? AirborneNpc(distance) with { PY = player.PY }
            : TestHelpers.NpcState(0f, distance) with { PY = GroundPy };
        var baked = TestHelpers.LoadBakedData(Def);
        sim.RegisterEntity(1, Def, player, baked);
        sim.RegisterEntity(100, Def, npc, baked);

        bool contacted = false;
        bool victimHitstop = false;
        bool victimHitstun = false;
        for (int tick = 0; tick < totalTicks; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = slot } : default,
                [100] = default,
            });
            if (tick == 0)
            {
                Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
                Assert.Equal(slot, sim.GetState(1).AttackSlot);
            }
            foreach (var hit in sim.LastTickHits)
            {
                if (hit.OwnerEntityId != 1 || hit.TargetEntityId != 100 || hit.Blocked)
                    continue;
                Assert.Equal(slot, hit.AttackSlot);
                Assert.Equal(airborne, hit.Airborne);
                contacted = true;
            }
            var victim = sim.GetState(100);
            victimHitstop |= contacted && victim.HitstopTicks > 0;
            victimHitstun |= victimHitstop && victim.State == ActionState.Hitstun;
        }

        Assert.True(contacted, "The normal must accept contact with the nearby opponent.");
        Assert.True(victimHitstop, "Accepted contact must freeze the victim before launch.");
        Assert.True(victimHitstun, "The victim must enter Hitstun after contact.");
        var recovered = sim.GetState(1);
        Assert.Equal((byte)0, recovered.AttackSlot);
        Assert.Equal((ushort)0, recovered.AnimLockTicks);
        Assert.NotEqual(ActionState.Attacking, recovered.State);
    }



    [Fact]
    public void E_JetpackBoost_Ignition_LaunchesPlayerAndDamagesVictim()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki E Jetpack Boost Ignition",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence()
                .Set(0, new InputState { ActiveSlot = AbilitySlots.E, MoveX = 0.6f, MoveY = 0.8f })
                .Set(1, new InputState { MoveX = 0.6f, MoveY = 0.8f })
                .Set(2, new InputState { MoveX = 0.6f, MoveY = 0.8f })
                .Set(3, new InputState { MoveX = 0.6f, MoveY = 0.8f }),
            Assert = player =>
            {
                Assert.Equal((ushort)0, player.HitstopTicks);
                Assert.True(player.PY > GroundPy);
                Assert.True(MathF.Abs(player.VX) + MathF.Abs(player.VZ) > 1f);
                Assert.False(player.IsGrounded);
                Assert.True(player.Flight.Active);
            },
            NpcSetup = () => TestHelpers.NpcState(0f, 0.75f) with { PY = GroundPy },
            NpcAssert = npc =>
            {
                Assert.Equal((ushort)10, npc.DamagePercent);
            },
            TotalTicks = 18,
        });
    }

    [Fact]
    public void R_Bazooka_ChargesThenFiresAndRecovers()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, GroundedPlayer() with { PX = 20f, PZ = 20f }, TestHelpers.LoadBakedData(Def));
        int spawned = 0;
        for (int tick = 0; tick < 110; tick++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0 } });
            Assert.NotEqual(ActionState.Aiming, sim.GetState(1).State);
            foreach (var rocket in sim.Resolver.LastTickCheckedHitboxes.Where(h => h.OwnerId == 1 && h.Explosion.HasValue && h.AgeTicks == 0))
            {
                spawned++;
                Assert.Equal(40u, sim.CurrentTick);
                Assert.Equal(4f, rocket.Explosion!.Value.OwnerDamage);
            }
        }
        Assert.Equal(1, spawned);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Assert.Null(sim.GetActiveAbility(1));
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
    }





    private static InputSequence HoldRelease(byte slot, ushort aimDistance, short aimPitch = 0)
    {
        var held = new InputState { ActiveSlot = slot, IsAiming = true, AimDistance = aimDistance, AimPitch = aimPitch };
        var sequence = new InputSequence();
        for (var tick = 0; tick <= 19; tick++) sequence.Set(tick, held);
        sequence.Set(20, new InputState { IsAiming = false, AimPitch = aimPitch });
        return sequence;
    }

    private static CharacterCompileResult CompileManki()
    {
        return CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/manki/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/manki/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
    }

    private static string RepoFile(string relative)
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
