using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkKitTests
{
    private const string CapabilityId = CharacterPackageCompiler.TargetedLeapCapabilityId;

    [Fact]
    public void Bonk_TrustedPackage_CooksCanonicalKit()
    {
        var first = CompileBonk();
        var second = CompileBonk();
        Assert.NotNull(first.CookedPackage);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
        Assert.Equal(first.CookedPackage!.CanonicalBytes, second.CookedPackage!.CanonicalBytes);

        var package = first.CookedPackage;
        Assert.Equal(14, package.Definition.Slots.Count);
        Assert.Equal(14, package.Budget.SlotCount);
        Assert.Equal(new[] { CharacterPackageCompiler.SteeredChargeCapabilityId, CapabilityId },
            package.Definition.CapabilityRequirements.Select(x => x.CapabilityId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(package.Definition.CapabilityRequirements, x => Assert.Equal("2", x.CapabilityVersion));

        var expectedIds = new[]
        {
            "ground.1", "ground.3", "ground.4",
            "ground.A", "ground.E", "ground.R", "ground.F",
            "air.1", "air.3", "air.4",
            "air.A", "air.E", "air.R", "air.F"
        };
        Assert.Equal(expectedIds.OrderBy(x => x), package.Definition.Slots.Select(x => x.Id).OrderBy(x => x));
    }


    [Fact]
    public void BonkF_LandsOneHoldHitPerTurnThenTheFinisher()
    {
        var def = BonkDefinition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 100f, z: 100f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        var targetDef = TestHelpers.CombatDef;
        // Oversized hurtbox isolates per-hit identity from blade reach and body separation.
        targetDef = targetDef with { HurtboxCapsules = new[]
        {
            new HurtboxCapsule(0f, -0.65f, 0f, 0f, 0.65f, 0f, 1f),
        } };
        var target = TestHelpers.NpcState(x: 100f, z: 100.1f);
        target.PY = TestHelpers.GroundPY(targetDef);
        sim.RegisterEntity(100, targetDef, target);
        var damage = new List<float>();
        for (var i = 0; i < 300; i++)
        {
            // Hold a damageable dummy in contact to test hit identity, not knockback escape.
            sim.SetState(100, target with { DamagePercent = sim.GetState(100).DamagePercent });
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = i == 0 ? TestHelpers.Input(activeSlot: AbilitySlots.F) : default,
                [100] = default,
            });
            damage.AddRange(sim.LastTickHits
                .Where(x => x.OwnerEntityId == 1 && x.TargetEntityId == 100)
                .Select(x => x.Damage));
        }
        Assert.Equal(new[] { 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 12f }, damage);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void BonkF_WalksSlowlyDuringTheSpinAndStopsForTheRecovery()
    {
        // The storm can be steered at a fraction of his speed while it spins. When the spin
        // ends, so does the walking: no slide through the recovery, on the ground or after
        // landing from the air version.
        var def = BonkDefinition();
        foreach (bool airborne in new[] { false, true })
        {
            var sim = TestHelpers.MakeSim();
            var state = TestHelpers.PlayerState(x: 20f, z: 10f);
            state.PY = TestHelpers.GroundPY(def) + (airborne ? 3f : 0f);
            state.IsGrounded = !airborne;
            sim.RegisterEntity(1, def, state);
            float scale = (def.GetCookedSlotAbility(AbilitySlots.F, airborne)?.AimMovementSpeedScale ?? 1f);
            Assert.Equal(0.7f, scale);

            var heavySeen = false;
            float fastest = 0f;
            CharacterState? spinEnd = null;
            for (var i = 0; i < 300 && (i == 0 || sim.GetActiveAbility(1) != null); i++)
            {
                sim.Tick(new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(activeSlot: i == 0 ? AbilitySlots.F : (byte)0, moveY: 1f) });
                var st = sim.GetState(1);
                heavySeen |= sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1 && x.Damage == 12f);
                if (st.State == ActionState.Aiming)
                    fastest = MathF.Max(fastest, MathF.Sqrt(st.VX * st.VX + st.VZ * st.VZ));
                else if (spinEnd == null && st.AttackSlot != 0)
                    spinEnd = st;
                if (spinEnd is { } end && st.AttackSlot != 0)
                {
                    TestHelpers.AssertNear(end.PX, st.PX, 1e-4f);
                    TestHelpers.AssertNear(end.PZ, st.PZ, 1e-4f);
                }
            }
            float cap = scale * (airborne ? def.Movement.AirSpeedMax : def.Movement.RunSpeed);
            Assert.InRange(fastest, 0.5f * cap, Math.Max(cap, scale * def.Movement.RunSpeed) + 1e-4f);
            Assert.True(sim.GetState(1).PZ > state.PZ, "F must let him walk while spinning");
            Assert.NotNull(spinEnd);
            Assert.True(heavySeen);
            Assert.Null(sim.GetActiveAbility(1));
        }
    }

    [Theory]
    [InlineData(AbilitySlots.Slot1, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot1, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot1, 8f, false, false)]
    [InlineData(AbilitySlots.Slot3, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot3, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot3, 8f, false, false)]
    [InlineData(AbilitySlots.Slot4, 2.2f, false, true)]
    [InlineData(AbilitySlots.Slot4, 2.2f, true, true)]
    [InlineData(AbilitySlots.Slot4, 8f, false, false)]
    public void BonkAerialNormals_CookedSwordPathHitsBlocksAndMisses(byte slot, float distance, bool shielding, bool expectContact)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
        var def = content.Definition;
        var baked = content.BakedAnimation;
        Assert.NotNull(baked);
        var sim = TestHelpers.MakeSim();
        // A low aerial should meet a standing opponent. The Lab's default aerial
        // starts a full capsule height above its grounded dummy and passes over it.
        var actor = TestHelpers.PlayerState(x: 20f, z: 10f) with
        {
            PY = TestHelpers.GroundPY(def) + 0.25f,
            IsGrounded = false,
        };
        var target = TestHelpers.NpcState(x: 20f, z: 10f + distance) with
        {
            PY = TestHelpers.GroundPY(def),
            FacingYaw = MathF.PI,
        };
        sim.RegisterEntity(1, def, actor, baked);
        sim.RegisterEntity(100, def, target, baked);
        var hits = new List<SpellResolver.HitResult>();
        ushort maxBlockStun = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = tick == 0 ? slot : AbilitySlots.None },
                [100] = new()
                {
                    FacingYaw = -18000,
                    ShieldHeld = shielding,
                    ShieldPressed = shielding && tick == 0,
                },
            });
            hits.AddRange(sim.LastTickHits.Where(hit => hit.OwnerEntityId == 1 && hit.TargetEntityId == 100));
            maxBlockStun = Math.Max(maxBlockStun, sim.GetState(100).BlockStunTicks);
        }

        if (expectContact)
        {
            var hit = Assert.Single(hits);
            Assert.Equal(slot, hit.AttackSlot);
            Assert.Equal(shielding, hit.Blocked);
            if (shielding)
            {
                Assert.Equal(0f, hit.Damage);
                Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
                Assert.True(maxBlockStun > 0);
            }
            else
            {
                Assert.True(hit.Damage > 0);
                Assert.True(sim.GetState(100).DamagePercent > 0);
            }
        }
        else
        {
            Assert.Empty(hits);
            Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        }
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static CharacterCompileResult CompileBonk()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null,
            string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        return result;

    }
    private static CharacterDefinition BonkDefinition()
        => CookedCharacterRuntimeAdapter.ToCharacterDefinition(CompileBonk().CookedPackage!);

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
