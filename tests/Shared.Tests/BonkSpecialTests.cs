using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BonkSpecialTests
{
    private const ulong BonkId = 1;
    private const ulong VictimId = 100;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BonkQ_ThrowsOneReturningLidAtTheReleaseOnGroundAndAir(bool airborne)
    {
        var bonk = CompileBonk();
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState(100f, 100f) with
        {
            PY = airborne ? 20f : TestHelpers.GroundPY(bonk),
            IsGrounded = !airborne,
            AirTimeTicks = airborne ? (ushort)100 : (ushort)0,
        };
        sim.RegisterEntity(BonkId, bonk, player, LoadBonkPoses());

        int spawned = 0;
        float? releaseZ = null;
        bool outbound = false, returning = false;
        var catches = new List<TimelinePresentationEvent>();
        for (int tick = 0; tick < 240; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.A } : default,
            });
            if (tick == 0)
            {
                Assert.Equal(AbilitySlots.A, sim.GetState(BonkId).AttackSlot);
                Assert.Equal(airborne, sim.GetActiveAbility(BonkId)!.AirborneAtStart);
            }
            spawned += sim.Resolver.LastTickCheckedHitboxes.Count(h =>
                h.OwnerId == BonkId && h.Return.HasValue && h.AgeTicks == 0);
            var lids = sim.Resolver.GetActiveHitboxes()
                .Where(h => h.OwnerId == BonkId && h.Return.HasValue).ToArray();
            Assert.InRange(lids.Length, 0, 1);
            if (lids.Length == 1)
            {
                var lid = lids[0];
                releaseZ ??= lid.Z;
                outbound |= !lid.ReturnLeg && lid.Z > releaseZ.Value;
                returning |= lid.ReturnLeg && lid.VZ < 0f;
                Assert.Equal(AbilitySlots.A, sim.GetState(BonkId).ReturningProjectileSlot);
            }
            catches.AddRange(sim.GetPresentationEvents(clear: true)
                .Where(e => e.Source == PresentationEventSource.ProjectileCatch));
        }

        Assert.Equal(1, spawned);
        Assert.True(outbound, "The package input must throw the lid away from Bonk.");
        Assert.True(returning, "The lid must turn and fly back toward Bonk.");
        Assert.Single(catches);
        Assert.Empty(sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == BonkId && h.Return.HasValue));
        var recovered = sim.GetState(BonkId);
        Assert.Equal((byte)0, recovered.ReturningProjectileSlot);
        Assert.Equal((byte)0, recovered.AttackSlot);
        Assert.Equal(ActionState.Idle, recovered.State);
        Assert.Equal((ushort)0, recovered.DamagePercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BonkE_HoldsTheNewDropAtItsEntryAndRestartsItWhenCommitting(bool airborne)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
        var bonk = content.Definition;
        Assert.NotNull(content.BakedAnimation);
        var stage = bonk.CookedSlots!.Single(s => s.Id == (airborne ? "air.E" : "ground.E")).Timeline.Stages[0];
        var phases = (CookedTargetedLeapCapabilityParameters)stage.Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters;
        Assert.Equal(new[] { "anim.bonk.ge", "anim.bonk.ge-drop", "anim.bonk.ge-land", "anim.bonk.ge-rise" }, stage.AnimationIds);
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(BonkId, bonk, TestHelpers.PlayerState(100, 100) with
        {
            PY = airborne ? 20f : TestHelpers.GroundPY(bonk),
            IsGrounded = !airborne,
            AirTimeTicks = airborne ? (ushort)100 : (ushort)0,
        }, content.BakedAnimation);
        bool hanging = false, dropping = false;
        for (int tick = 0; tick < phases.RiseTicks + phases.HangTicks + 3; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.E } : default,
            });
            var events = sim.GetPresentationEvents(clear: true);
            hanging |= events.Any(e => e.PresentationId == phases.HangPresentationId);
            bool committed = events.Any(e => e.PresentationId == phases.LockPresentationId);
            var state = sim.GetState(BonkId);
            if (!hanging)
                Assert.Equal("anim.bonk.ge-rise", stage.AnimationIds[state.ComboStage]);
            if (hanging && !dropping)
            {
                Assert.Equal("anim.bonk.ge-drop", stage.AnimationIds[state.ComboStage]);
                Assert.Equal((ushort)0, state.AttackElapsedTicks);
            }
            if (dropping)
            {
                Assert.Equal("anim.bonk.ge-drop", stage.AnimationIds[state.ComboStage]);
                Assert.True(state.AttackElapsedTicks > 0, "The committed drop must advance from its entry pose.");
            }
            dropping |= committed;
        }
        Assert.True(hanging);
        Assert.True(dropping);
    }

    private static (int Damage, float HitDamage, bool AbilityContinues,
        bool RemainsAttacking, bool NoLaunch, bool ArmorActive)
        RunArmorContact(CharacterDefinition bonk, BakedAnimationData baked, byte slot,
            bool airborne, int challengeTick)
    {
        var sim = TestHelpers.MakeSim();
        var target = TestHelpers.PlayerState(100f, 100f);
        target.PY = airborne ? 50f : TestHelpers.GroundPY(bonk);
        target.IsGrounded = !airborne;
        target.AirTimeTicks = airborne ? (ushort)100 : (ushort)0;
        sim.RegisterEntity(BonkId, bonk, target, baked);
        var hitter = TestHelpers.PlayerState(150f, 150f);
        var hitterDef = TestHelpers.EngineDef;
        hitter.PY = TestHelpers.GroundPY(hitterDef);
        sim.RegisterEntity(VictimId, hitterDef, hitter);

        SpellResolver.HitResult? incoming = null;
        // Input activation advances the first authored tick in this same simulation step.
        for (int tick = 1; tick <= challengeTick; tick++)
        {
            if (tick == challengeTick)
            {
                var current = sim.GetState(BonkId);
                sim.Resolver.Spawn(new Hitbox
                {
                    X = current.PX,
                    Y = current.PY,
                    Z = current.PZ,
                    EndX = current.PX,
                    EndY = current.PY,
                    EndZ = current.PZ,
                    Radius = 0.45f,
                    Shape = HitboxShape.Sphere,
                    Damage = 3f,
                    BaseKnockback = 4f,
                    KnockbackGrowth = 4f,
                    KnockbackAngle = 35,
                    StunTicks = 10,
                    DurationTicks = 20,
                    OwnerId = VictimId,
                    ActivationId = 1,
                    AttackSlot = 1,
                });
            }

            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 1 ? new InputState { ActiveSlot = slot } : default,
            });
            var hit = sim.LastTickHits.FirstOrDefault(contact =>
                contact.OwnerEntityId == VictimId && contact.TargetEntityId == BonkId);
            if (hit.OwnerEntityId == VictimId)
            {
                incoming = hit;
                break;
            }
        }

        Assert.True(incoming.HasValue, $"synthetic challenge missed Bonk at tick {challengeTick}");
        bool armorAtContact = sim.GetActiveAbility(BonkId)?.HasArmor ?? false;
        for (int i = 0; i <= incoming.Value.HitstopTicks + 1; i++)
            sim.Tick(new Dictionary<ulong, InputState>());
        var state = sim.GetState(BonkId);
        return (
            (int)state.DamagePercent,
            incoming.Value.Damage,
            sim.GetActiveAbility(BonkId) != null,
            state.State == ActionState.Attacking,
            MathF.Abs(state.KVX) + MathF.Abs(state.KVY) + MathF.Abs(state.KVZ) < 0.0001f,
            armorAtContact);
    }


    private static CharacterDefinition CompileBonk()
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/package.json")),
            File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/bonk/character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null,
            string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}: {x.Message} ({x.Path})")));
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage!, CharacterClass.Bonk);
    }

    private static BakedAnimationData LoadBonkPoses()
    {
        var baked = BakedAnimationData.LoadFromBin(File.ReadAllBytes(RepoFile("content-cooked/bonk/poses.bin")));
        using var bindings = JsonDocument.Parse(File.ReadAllBytes(RepoFile("content-cooked/bonk/client.bindings")));
        var names = bindings.RootElement.GetProperty("animations").EnumerateArray()
            .ToDictionary(x => x.GetProperty("poseTrackId").GetString()!,
                x => x.GetProperty("semanticId").GetString()!, StringComparer.Ordinal);
        return new BakedAnimationData
        {
            BoneNames = baked.BoneNames,
            Animations = baked.Animations.Select(animation => animation.WithName(names[animation.Name])).ToArray(),
        };
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
