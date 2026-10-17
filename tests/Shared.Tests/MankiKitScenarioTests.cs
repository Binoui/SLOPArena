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
    private const string RoundBombCapabilityId = "slop.internal.manki.round-bomb.v1";
    private const string JetpackCapabilityId = "slop.internal.manki.jetpack-boost.v1";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v1";
    private const string AerosolCapabilityId = "slop.internal.manki.aerosol-inferno.v1";

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
        Assert.Equal(new[] { AerosolCapabilityId, BazookaCapabilityId, JetpackCapabilityId, RoundBombCapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x).ToArray());
        Assert.Equal(16, package.Definition.Slots.Count);
        foreach (var slot in package.Definition.Slots.Where(x => x.Ordinal % 8 < 4))
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

        // Air Swing's sweet/sour windows must share hit history and the same limb sweep.
        var airSwing = package.Definition.Slots.Single(x => x.Id == "air.2").Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>().ToArray();
        Assert.Equal(2, airSwing.Length);
        Assert.NotEqual((byte)0, airSwing[0].Hitbox.HitGroup);
        Assert.All(airSwing, operation =>
        {
            Assert.Equal(airSwing[0].Hitbox.HitGroup, operation.Hitbox.HitGroup);
            Assert.Equal(AuthoringHitboxShape.Capsule, operation.Hitbox.Shape);
            Assert.Equal("bone.left-foot", operation.Hitbox.StartBoneId);
            Assert.Equal("bone.hips", operation.Hitbox.EndBoneId);
        });

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.GroundCursor,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", JetpackCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.CameraForward3D,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        var jetpackSlot = package.Definition.Slots.Single(x => x.Id == "ground.E");
        Assert.True(jetpackSlot.IsRecoveryMove);
        Assert.False(jetpackSlot.PreserveMomentumOnStart);
        var airJetpack = package.Definition.Slots.Single(x => x.Id == "air.E");
        Assert.Equal(jetpackSlot.Name, airJetpack.Name);
        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal(AuthoringAbilityBehavior.AreaDenial, aerosol.Behavior);
        Assert.Equal(AuthoringAimMode.CameraForward3D, aerosol.AimMode);
        Assert.Equal("anim.manki.gf-loop", aerosol.AimAnimationId);
        var aerosolStage = Assert.Single(aerosol.Timeline.Stages);
        Assert.Equal((ushort)0, aerosolStage.IasaTicks);
        var start = Assert.Single(aerosolStage.Operations.OfType<CookedStartCapabilityOperation>());
        Assert.Equal((ushort)0, start.Tick);
        var aerosolParameters = Assert.IsType<CookedMankiAerosolInfernoCapabilityParameters>(start.Parameters);
        Assert.InRange(aerosolParameters.FireTriggerTick + aerosolParameters.HitboxDurationTicks,
            1, aerosolStage.DurationTicks);
        Assert.True(aerosolParameters.Damage > 0f);
        var presentations = aerosolStage.Operations.OfType<CookedEmitPresentationOperation>().ToArray();
        Assert.Equal(3, presentations.Length);
        // The charge cue starts with the charge, the lighter right after release, and the flame
        // stream with the hitbox.
        Assert.Equal((ushort)0, Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.charge").Tick);
        Assert.Equal((ushort)1, Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.lighter").Tick);
        Assert.Equal(aerosolParameters.FireTriggerTick,
            Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.start").Tick);
        Assert.Empty(package.Definition.Slots.SelectMany(x => x.Timeline.Stages).SelectMany(x => x.Operations).OfType<CookedSetVelocityOperation>());
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
    [InlineData(AbilitySlots.Slot2, false, 1f, 80)]
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

    // ── Golden scenarios: specials ──

    [Fact]
    public void A_RoundBomb_HoldReleaseLobsBomb_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki A Round Bomb Hold Release Lob",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = HoldRelease(AbilitySlots.A, aimDistance: 600),
            Assert = _ => { },
            SnapshotTick = 34, // release t20 → fire t30; bomb in flight.
            TotalTicks = 120,
        });
    }

    [Fact]
    public void E_JetpackBoost_Ignition_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
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
                Assert.True(player.PY > GroundPy);
                Assert.True(player.VY > 0f);
                Assert.False(player.IsGrounded);
                Assert.Equal((byte)AbilitySlots.E, player.AttackSlot);
            },
            NpcSetup = () => TestHelpers.NpcState(0f, 0.75f) with { PY = GroundPy },
            NpcAssert = npc =>
            {
                Assert.Equal((ushort)10, npc.DamagePercent);
            },
            SnapshotTick = 10,
            TotalTicks = 11,
        });
    }

    [Fact]
    public void R_Bazooka_AimDownRocketJump_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki R Bazooka Rocket Jump",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = HoldRelease(AbilitySlots.R, aimDistance: 600, aimPitch: -9000), // -90°: straight down at the feet.
            Assert = _ => { },
            SnapshotTick = 30, // release t20 → rocket fires t26 → ground explosion near self.
            TotalTicks = 100,
        });
    }

    [Fact]
    public void F_AerosolInferno_HitsAfterTemporaryInvincibilityWithoutHittingBeyondEndpoint()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, GroundedPlayer(), TestHelpers.LoadBakedData(Def));
        sim.RegisterEntity(100, TestHelpers.CombatDef,
            TestHelpers.NpcState(0f, 1f) with { PY = GroundPy, FacingYaw = MathF.PI / 2f });
        sim.RegisterEntity(101, TestHelpers.CombatDef,
            TestHelpers.NpcState(8f, 8f) with { EntityId = 101, PY = GroundPy });

        for (var tick = 0; tick < 17; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.F } : default,
                [100] = default,
                [101] = default,
            });
            Assert.Equal(0f, sim.GetState(100).DamagePercent);
            Assert.Equal(0f, sim.GetState(101).DamagePercent);
        }

        var invincible = sim.GetState(100) with { InvincibilityTicks = 4, VX = 0f, VY = 0f, VZ = 0f };
        sim.SetState(100, invincible);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = default,
            [100] = default,
            [101] = default,
        });
        Assert.Equal(0f, sim.GetState(100).DamagePercent);
        Assert.Equal(0f, sim.GetState(101).DamagePercent);

        for (var tick = 0; tick < 12; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = default,
                [100] = default,
                [101] = default,
            });
        }

        Assert.Equal(10f, sim.GetState(100).DamagePercent);
        Assert.Equal(0f, sim.GetState(101).DamagePercent);
    }

    [Fact]
    public void F_AerosolInferno_IsGolden()
    {
        AssertGoldenScenario(new KitScenario
        {
            Name = "Manki F Aerosol Inferno",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.F),
            Assert = state => Assert.Equal(ActionState.Idle, state.State),
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcAssert = npc => Assert.Equal(10f, npc.DamagePercent),
            SnapshotTick = 30,
            TotalTicks = 100,
        });
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
