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
    public void BonkR_JoustRearsThenGallopsAndUnhorsesTheFirstFighterItMeets(bool airborne)
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        var victimDef = CompileBonk();
        var sim = TestHelpers.MakeSim();
        if (airborne)
            sim.NoGravityEntityId = VictimId;
        var attacker = TestHelpers.PlayerState(100f, 100f);
        attacker.PY = airborne ? 50f : TestHelpers.GroundPY(bonk);
        attacker.IsGrounded = !airborne;
        attacker.FacingYaw = 0f;
        var victim = TestHelpers.NpcState(100f, airborne ? 104f : 106f);
        victim.PY = airborne ? 50f : TestHelpers.GroundPY(victimDef);   // he holds his height through the air mount
        victim.IsGrounded = !airborne;
        sim.RegisterEntity(BonkId, bonk, attacker, baked);
        sim.RegisterEntity(VictimId, victimDef, victim, baked);

        var contacts = new List<(int Tick, SpellResolver.HitResult Hit)>();
        var z = new List<float>();
        var stage = new List<byte>();
        int ended = -1;
        for (int tick = 0; tick < 300 && ended < 0; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.R } : default,
            });
            contacts.AddRange(sim.LastTickHits.Where(hit => hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId)
                .Select(hit => (tick, hit)));
            z.Add(sim.GetState(BonkId).PZ);
            stage.Add(sim.GetState(BonkId).ComboStage);
            if (sim.GetActiveAbility(BonkId) == null)
                ended = tick;
        }

        var (hitTick, lance) = Assert.Single(contacts);
        Assert.True(hitTick >= 30, $"the rear is the tell: no lance before the charge (hit at tick {hitTick})");
        Assert.False(lance.Blocked);
        Assert.Equal(11f, lance.Damage);
        Assert.True(sim.GetState(VictimId).DamagePercent > 0);
        Assert.InRange(z[29] - z[0], -0.05f, 0.05f);        // he rears where he stands
        Assert.InRange(z[32] - z[30], 0.35f, 0.45f);        // 12 m/s once the charge starts
        // On the ground the hit ending (the proud rear) plays; on thin air he looks down and drops instead. The
        // stage's sorted clips: R, gallop, hit, look-down, stop.
        Assert.Equal(airborne ? (byte)3 : (byte)2, stage[ended - 1]);
        Assert.True(ended >= hitTick + (airborne ? 20 : 36), $"the ending plays out ({hitTick} -> {ended})");
        Assert.InRange(z[ended] - z[hitTick + 1], -0.05f, 0.05f);   // stopped dead at the hit
    }

    [Fact]
    public void BonkR_ShieldStopsTheJoustShortAndAWhiffGallopsOnThenSkidsWithoutTurning()
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        var victimDef = CompileBonk();

        var shielded = RunJoust(bonk, baked, victimDef, 106f, shield: true);
        Assert.Equal(0, shielded.FinalDamage);
        Assert.NotEmpty(shielded.BlockedContacts);
        Assert.Empty(shielded.DamagingContacts);
        Assert.True(shielded.EndZ < 106f, $"the shield stops him short (at {shielded.EndZ})");

        var charge = Assert.IsType<CookedSteeredChargeCapabilityParameters>(bonk
            .GetCookedSlotAbility(AbilitySlots.R, airborne: false)!.Timeline.Stages.Single().Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters);
        float gallopDistance = charge.GallopSpeed * charge.MaxGallopTicks * Simulation.TickDt;
        var missed = RunJoust(bonk, baked, victimDef, 100f + gallopDistance + 10f, shield: false);
        Assert.Equal(0, missed.FinalDamage);
        Assert.Empty(missed.BlockedContacts);
        Assert.Empty(missed.DamagingContacts);
        // The full authored gallop, then an even 18-tick skid from 12 m/s: about 1.9 m more.
        Assert.InRange(missed.EndZ - 100f, gallopDistance + 0.5f, gallopDistance + 3f);
        Assert.InRange(missed.EndYaw, -0.01f, 0.01f);       // the owner dropped the turn-round
    }

    private static (int FinalDamage, SpellResolver.HitResult[] BlockedContacts,
        SpellResolver.HitResult[] DamagingContacts, float EndZ, float EndYaw) RunJoust(
        CharacterDefinition bonk, BakedAnimationData baked, CharacterDefinition victimDef,
        float victimZ, bool shield)
    {
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState(100f, 100f);
        attacker.PY = TestHelpers.GroundPY(bonk);
        attacker.FacingYaw = 0f;
        sim.RegisterEntity(BonkId, bonk, attacker, baked);
        var victim = TestHelpers.NpcState(100f, victimZ);
        victim.PY = TestHelpers.GroundPY(victimDef);
        sim.RegisterEntity(VictimId, victimDef, victim);

        var blocked = new List<SpellResolver.HitResult>();
        var damaging = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 600 && (tick == 0 || sim.GetActiveAbility(BonkId) != null); tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [BonkId] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.R } : default,
                [VictimId] = shield ? new InputState { ShieldHeld = true } : default,
            });
            blocked.AddRange(sim.LastTickHits.Where(hit =>
                hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId && hit.Blocked));
            damaging.AddRange(sim.LastTickHits.Where(hit =>
                hit.OwnerEntityId == BonkId && hit.TargetEntityId == VictimId && !hit.Blocked));
        }
        Assert.Null(sim.GetActiveAbility(BonkId));
        var end = sim.GetState(BonkId);
        return ((int)sim.GetState(VictimId).DamagePercent, blocked.ToArray(), damaging.ToArray(), end.PZ, end.FacingYaw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BonkR_ArmourCoversTheGallopButNotTheRear(bool airborne)
    {
        var bonk = CompileBonk();
        var baked = LoadBonkPoses();
        foreach (var (challengeTick, armoured) in new[] { (12, false), (29, false), (33, true), (50, true) })
        {
            var result = RunArmorContact(bonk, baked, AbilitySlots.R, airborne, challengeTick);
            Assert.Equal(3, result.Damage);
            Assert.Equal(armoured, result.ArmorActive);
            Assert.Equal(armoured, result.AbilityContinues);
            Assert.Equal(armoured, result.RemainsAttacking);
            Assert.Equal(armoured, result.NoLaunch);
        }
    }

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
        foreach (var animation in baked.Animations)
            animation.Name = names[animation.Name];
        return baked;
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
