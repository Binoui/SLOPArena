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
    private const string RoundBombCapabilityId = "slop.internal.manki.round-bomb.v2";
    private const string JetpackCapabilityId = "slop.internal.manki.jetpack-boost.v2";
    private const string BazookaCapabilityId = "slop.internal.manki.bazooka.v2";
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

        AssertSpecial(package, "ground.A", RoundBombCapabilityId, AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.GroundCursor,
            p => Assert.IsType<CookedMankiRoundBombCapabilityParameters>(p));
        AssertSpecial(package, "ground.E", JetpackCapabilityId, AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiJetpackBoostCapabilityParameters>(p));
        AssertSpecial(package, "ground.R", BazookaCapabilityId, AuthoringAbilityBehavior.Projectile, AuthoringAimMode.None,
            p => Assert.IsType<CookedMankiBazookaCapabilityParameters>(p));
        var jetpackSlot = package.Definition.Slots.Single(x => x.Id == "ground.E");
        Assert.True(jetpackSlot.IsRecoveryMove);
        Assert.False(jetpackSlot.PreserveMomentumOnStart);
        var aerosol = package.Definition.Slots.Single(x => x.Id == "ground.F");
        Assert.Equal(AuthoringAbilityBehavior.AreaDenial, aerosol.Behavior);
        Assert.Equal(AuthoringAimMode.GroundVector, aerosol.AimMode);
        var aerosolStage = Assert.Single(aerosol.Timeline.Stages);
        Assert.Equal((ushort)0, aerosolStage.IasaTicks);
        var start = Assert.Single(aerosolStage.Operations.OfType<CookedStartCapabilityOperation>());
        Assert.Equal((ushort)0, start.Tick);
        var aerosolParameters = Assert.IsType<CookedMankiAerosolInfernoCapabilityParameters>(start.Parameters);
        Assert.InRange(aerosolParameters.FireTriggerTick + aerosolParameters.HitboxDurationTicks,
            1, aerosolStage.DurationTicks);
        Assert.True(aerosolParameters.Damage > 0f);
        var presentations = aerosolStage.Operations.OfType<CookedEmitPresentationOperation>().ToArray();
        // The charge cue starts with the charge, the lighter right after release, and the flame
        // stream with the hitbox.
        Assert.Equal((ushort)0, Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.charge").Tick);
        Assert.Equal((ushort)1, Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.lighter").Tick);
        Assert.Equal(aerosolParameters.FireTriggerTick,
            Assert.Single(presentations, x => x.PresentationId == "presentation.manki.aerosol-inferno.start").Tick);
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
    public void A_RoundBomb_HoldReleaseLobsOneBomb_ThatFliesAndHits()
    {
        var sim = TestHelpers.MakeSim();
        var player = GroundedPlayer() with { PX = 20f, PZ = 20f };
        sim.RegisterEntity(1, Def, player, TestHelpers.LoadBakedData(Def));
        var targetDef = TestHelpers.EngineDef;
        sim.RegisterEntity(100, targetDef,
            TestHelpers.NpcState(20f, 26f) with { PY = TestHelpers.GroundPY(targetDef) });
        var inputs = HoldRelease(AbilitySlots.A, aimDistance: 600);
        int spawned = 0;
        bool rose = false, fell = false, travelled = false, contacted = false;
        for (int tick = 0; tick < 160; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = inputs.ForTick(tick),
                [100] = default,
            });
            var bombs = sim.Resolver.LastTickCheckedHitboxes
                .Where(h => h.OwnerId == 1 && h.Explosion.HasValue).ToArray();
            if (tick < 20)
            {
                Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
                Assert.Empty(bombs);
            }
            spawned += bombs.Count(h => h.AgeTicks == 0);
            foreach (var bomb in bombs)
            {
                rose |= bomb.VY > 0f && bomb.Y > player.PY;
                fell |= rose && bomb.VY < 0f;
                travelled |= bomb.Z > player.PZ + 3f;
            }
            contacted |= sim.LastTickHits.Any(h =>
                h.OwnerEntityId == 1 && h.TargetEntityId == 100 && !h.Blocked);
        }

        Assert.Equal(1, spawned);
        Assert.True(rose && fell, "The released bomb must rise and descend along a lobbed flight.");
        Assert.True(travelled, "The bomb must travel toward the held cursor.");
        Assert.True(contacted, "The bomb must contact the target at the aimed distance.");
        Assert.True(sim.GetState(100).DamagePercent > 0);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
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
                Assert.Equal((byte)AbilitySlots.E, player.AttackSlot);
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
    public void R_Bazooka_PressFiresAndRecoversWithoutAiming()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, GroundedPlayer() with { PX = 20f, PZ = 20f }, TestHelpers.LoadBakedData(Def));
        int spawned = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0 } });
            Assert.NotEqual(ActionState.Aiming, sim.GetState(1).State);
            foreach (var rocket in sim.Resolver.LastTickCheckedHitboxes.Where(h => h.OwnerId == 1 && h.Explosion.HasValue && h.AgeTicks == 0))
            {
                spawned++;
                Assert.Equal(6u, sim.CurrentTick);
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
    public void F_AerosolInferno_DamagesVictimAndRecovers()
    {
        AssertScenario(new KitScenario
        {
            Name = "Manki F Aerosol Inferno",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, AbilitySlots.F),
            Assert = state =>
            {
                Assert.Equal(ActionState.Idle, state.State);
                Assert.Equal((byte)0, state.AttackSlot);
            },
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcAssert = npc => Assert.Equal(10f, npc.DamagePercent),
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
