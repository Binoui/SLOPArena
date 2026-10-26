using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BashieNormalsTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(135f)]
    [InlineData(180f)]
    [InlineData(225f)]
    [InlineData(270f)]
    [InlineData(315f)]
    public void GroundOneHitsOnceAroundTheShell(float degrees)
    {
        var result = Run(degrees, 1f, shield: false);
        var hit = Assert.Single(result.Hits);
        Assert.False(hit.Blocked);
        Assert.Equal(7f, hit.Damage);
        Assert.Equal((ushort)7, result.Victim.DamagePercent);
        Assert.InRange(result.FirstContactTick, 10, 29);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.InRange(result.Attacker.PX, 99.999f, 100.001f);
        Assert.InRange(result.Attacker.PZ, 99.999f, 100.001f);
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void GroundOneRespectsShieldAndWhiffRecovery()
    {
        var blocked = Run(180f, 1f, shield: true);
        Assert.NotEmpty(blocked.Hits);
        Assert.All(blocked.Hits, hit => Assert.True(hit.Blocked));
        Assert.Equal((ushort)0, blocked.Victim.DamagePercent);
        Assert.Null(blocked.Sim.GetActiveAbility(1));

        var missed = Run(0f, 3f, shield: false);
        Assert.Empty(missed.Hits);
        Assert.Equal((ushort)0, missed.Victim.DamagePercent);
        Assert.Null(missed.Sim.GetActiveAbility(1));
        Assert.Empty(missed.Sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void AirOneHitsOnceAroundTheShell(float degrees)
    {
        var result = Run(degrees, 1f, false, airborne: true, ticks: 60);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(7f, hit.Damage);
        Assert.InRange(result.FirstContactTick, 10, 29);
        Assert.False(result.Attacker.IsGrounded);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
        Assert.True(result.Attacker.VY < 0f);
    }

    [Fact]
    public void AirOnePreservesTakeoffMomentum()
    {
        var attack = Run(0f, 20f, false, airborne: true, ticks: 1, moving: true);
        var baseline = Run(0f, 20f, false, airborne: true, ticks: 1, moving: true, press: false);
        Assert.Equal(3f, attack.Attacker.VX);
        Assert.True(attack.Attacker.VX >= baseline.Attacker.VX);
        Assert.Equal(2f, attack.Attacker.VZ);
        Assert.True(attack.Attacker.VY > 0f);
        Assert.NotNull(attack.Sim.GetActiveAbility(1));
    }

    [Fact]
    public void AirOneLandingClearsAttackAndHitboxes()
    {
        var result = Run(0f, 20f, false, airborne: true, ticks: 120);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
        Assert.Equal((ushort)0, result.Attacker.LandingLagTicks);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(4f, true)]
    public void AirOneLandingUsesItsAutoCancelWindow(float height, bool expectsLag)
    {
        var result = Run(0f, 20f, false, airborne: true, ticks: 80, height: height, falling: true);
        Assert.Equal(expectsLag, result.MaxLandingLag > 0);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
        Assert.Null(result.Sim.GetActiveAbility(1));
    }

    [Fact]
    public void GroundFourHopsAndHitsOnceThenLandsOnTheSameSpot()
    {
        var result = Run(0f, 1f, false, slot: AbilitySlots.Slot4);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(14f, hit.Damage);
        Assert.InRange(result.FirstContactTick, 14, 23);
        Assert.InRange(result.MaxHorizontalDelta, 0f, .001f);
        Assert.True(result.Attacker.IsGrounded);
        Assert.True(result.AirborneDuringAttack);
        Assert.InRange(result.MaxRise, .45f, .65f);
        Assert.InRange(result.ReturnHeightDelta, -.001f, .001f);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void GroundFourRespectsShieldAndWhiffRecovery()
    {
        var blocked = Run(0f, 1f, true, slot: AbilitySlots.Slot4);
        Assert.NotEmpty(blocked.Hits);
        Assert.All(blocked.Hits, hit => Assert.True(hit.Blocked));
        Assert.Equal((ushort)0, blocked.Victim.DamagePercent);
        Assert.InRange(blocked.MaxHorizontalDelta, 0f, .001f);
        var missed = Run(0f, 3f, false, slot: AbilitySlots.Slot4);
        Assert.Empty(missed.Hits);
        Assert.Null(missed.Sim.GetActiveAbility(1));
        Assert.Empty(missed.Sim.Resolver.GetActiveHitboxes());
        Assert.InRange(missed.MaxHorizontalDelta, 0f, .001f);
    }

    [Fact]
    public void GroundFourHitsAnOpponentAboveTheShell()
    {
        var result = Run(0f, .6f, false, slot: AbilitySlots.Slot4, victimHeight: 1.2f);
        Assert.Equal(14f, Assert.Single(result.Hits).Damage);
        Assert.InRange(result.FirstContactTick, 14, 23);
        Assert.InRange(result.MaxHorizontalDelta, 0f, .001f);
    }

    [Fact]
    public void GroundFourInterruptionClearsTheRisingHitboxes()
    {
        var result = Run(0f, 3f, false, slot: AbilitySlots.Slot4, interruptAt: 18);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Empty(result.Hits);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void GroundFourHopCannotBeSteeredOrJumpCancelled()
    {
        var result = Run(0f, 3f, false, slot: AbilitySlots.Slot4, steerDuringAttack: true);
        Assert.True(result.AirborneDuringAttack);
        Assert.InRange(result.MaxHorizontalDelta, 0f, .001f);
        Assert.InRange(result.MaxRise, .45f, .65f);
        Assert.InRange(result.ReturnHeightDelta, -.001f, .001f);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Null(result.Sim.GetActiveAbility(1));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void GroundFourDiagonalReachFollowsFacing(float degrees)
    {
        var result = Run(degrees, 1.6f, false, slot: AbilitySlots.Slot4, attackerYawDegrees: degrees);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(14f, hit.Damage);
        Assert.InRange(hit.KnockbackAngle, (sbyte)40, (sbyte)50);
        float yaw = degrees * MathF.PI / 180f;
        Assert.True(hit.DirX * MathF.Sin(yaw) + hit.DirZ * MathF.Cos(yaw) > .95f);
        Assert.InRange(result.MaxHorizontalDelta, 0f, .001f);
        Assert.InRange(result.ReturnHeightDelta, -.001f, .001f);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void AirFourHitsOnceWithDiagonalReach(float degrees)
    {
        var result = Run(degrees, 1.6f, false, airborne: true, ticks: 90,
            slot: AbilitySlots.Slot4, attackerYawDegrees: degrees);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(14f, hit.Damage);
        Assert.InRange(hit.KnockbackAngle, (sbyte)40, (sbyte)50);
        Assert.InRange(result.FirstContactTick, 14, 23);
        Assert.True(hit.Airborne);
        Assert.False(result.Attacker.IsGrounded);
        Assert.True(result.Attacker.VY < 0f);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void AirFourPreservesTakeoffMomentum()
    {
        var attack = Run(0f, 20f, false, airborne: true, ticks: 1, moving: true, slot: AbilitySlots.Slot4);
        var baseline = Run(0f, 20f, false, airborne: true, ticks: 1, moving: true, press: false);
        Assert.Equal(3f, attack.Attacker.VX);
        Assert.Equal(2f, attack.Attacker.VZ);
        Assert.Equal(baseline.Attacker.VY, attack.Attacker.VY);
        Assert.NotNull(attack.Sim.GetActiveAbility(1));
    }

    [Fact]
    public void AirFourDoesNotBoostOrResetTheFall()
    {
        var attack = Run(0f, 20f, false, airborne: true, ticks: 45, falling: true, slot: AbilitySlots.Slot4);
        var baseline = Run(0f, 20f, false, airborne: true, ticks: 45, falling: true, press: false);
        Assert.Equal(baseline.Attacker.PY, attack.Attacker.PY);
        Assert.Equal(baseline.Attacker.VY, attack.Attacker.VY);
        Assert.Equal(baseline.Attacker.JumpsLeft, attack.Attacker.JumpsLeft);
        Assert.Equal(baseline.Attacker.AirDodgesLeft, attack.Attacker.AirDodgesLeft);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(4f, true)]
    [InlineData(7f, false)]
    public void AirFourLandingCleansUpAndUsesAutoCancel(float height, bool expectsLag)
    {
        var result = Run(0f, 20f, false, airborne: true, ticks: 100, height: height,
            falling: true, slot: AbilitySlots.Slot4);
        Assert.Equal(expectsLag, result.MaxLandingLag > 0);
        Assert.True(result.Attacker.IsGrounded);
        Assert.Equal((ushort)0, result.Attacker.LandingLagTicks);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void AirFourRespectsGroundedShieldAndWhiffRecovery()
    {
        var blocked = Run(0f, 1.6f, true, airborne: true, height: .25f,
            slot: AbilitySlots.Slot4, victimGrounded: true);
        Assert.NotEmpty(blocked.Hits);
        Assert.All(blocked.Hits, hit => Assert.True(hit.Blocked));
        Assert.Equal((ushort)0, blocked.Victim.DamagePercent);
        var missed = Run(0f, 3f, false, airborne: true, slot: AbilitySlots.Slot4);
        Assert.Empty(missed.Hits);
        Assert.Null(missed.Sim.GetActiveAbility(1));
        Assert.Empty(missed.Sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void AirFourInterruptionClearsTheStrike()
    {
        var result = Run(0f, 3f, false, airborne: true, ticks: 120,
            slot: AbilitySlots.Slot4, interruptAt: 18);
        Assert.Empty(result.Hits);
        Assert.Null(result.Sim.GetActiveAbility(1));
        Assert.Empty(result.Sim.Resolver.GetActiveHitboxes());
    }

    private static (ServerSimulation Sim, CharacterState Attacker, CharacterState Victim,
        SpellResolver.HitResult[] Hits, int FirstContactTick, int MaxLandingLag, float MaxHorizontalDelta, float MaxRise, float ReturnHeightDelta, bool AirborneDuringAttack) Run(float degrees, float distance, bool shield,
        bool airborne = false, int ticks = 100, bool moving = false, bool press = true, float height = 12f, bool falling = false,
        byte slot = AbilitySlots.Slot1, float victimHeight = 0f, int interruptAt = -1, bool steerDuringAttack = false, float attackerYawDegrees = 0f, bool victimGrounded = false)
    {
        var package = Path.Combine(Root(), "client/Unity/Assets/CharacterPackages/bashie");
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(package, "package.json")),
            File.ReadAllText(Path.Combine(package, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!, CharacterClass.Bashie);
        var cooked = Path.Combine(Root(), "content-cooked/bashie");
        var poses = BakedAnimationData.LoadFromBin(File.ReadAllBytes(Path.Combine(cooked, "poses.bin")));
        using var bindings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(cooked, "client.bindings")));
        var names = bindings.RootElement.GetProperty("animations").EnumerateArray().ToDictionary(
            a => a.GetProperty("poseTrackId").GetString()!, a => a.GetProperty("semanticId").GetString()!);
        poses = new BakedAnimationData
        {
            BoneNames = poses.BoneNames,
            Animations = poses.Animations.Select(animation => animation.WithName(names[animation.Name])).ToArray(),
        };
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState(100f, 100f);
        attacker.PY = TestHelpers.GroundPY(definition);
        attacker.FacingYaw = attackerYawDegrees * MathF.PI / 180f;
        short wireYaw = (short)(attackerYawDegrees * 100f);
        if (airborne) { attacker.PY = TestHelpers.GroundPY(definition) + height; attacker.IsGrounded = false; }
        if (falling) attacker.VY = -10f;
        if (moving) { attacker.VX = 3f; attacker.VZ = 2f; attacker.VY = 4f; }
        float radians = degrees * MathF.PI / 180f;
        var victim = TestHelpers.NpcState(100f + MathF.Sin(radians) * distance, 100f + MathF.Cos(radians) * distance);
        victim.PY = TestHelpers.GroundPY(definition);
        victim.FacingYaw = radians + MathF.PI;
        if (airborne && !victimGrounded) { victim.PY = attacker.PY; victim.IsGrounded = false; }
        if (victimHeight > 0f)
        {
            victim.PY += victimHeight; victim.IsGrounded = false; sim.NoGravityEntityId = 100;
        }
        float initialX = attacker.PX, initialY = attacker.PY, initialZ = attacker.PZ, maxHorizontalDelta = 0f;
        sim.RegisterEntity(1, definition, attacker, poses);
        sim.RegisterEntity(100, definition, victim, poses);
        float maxRise = 0f;
        bool airborneDuringAttack = false;
        var hits = new List<SpellResolver.HitResult>();
        int firstContact = -1, maxLandingLag = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            if (tick == interruptAt)
                sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 10, HitstopTicks = 0 });
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 && press ? new InputState { ActiveSlot = slot, FacingYaw = wireYaw, AimYaw = wireYaw }
                    : steerDuringAttack && sim.GetActiveAbility(1) != null
                        ? new InputState { Right = true, Jump = true, JumpHeld = true, DownPressed = true, FacingYaw = wireYaw, AimYaw = wireYaw } : default,
                [100] = shield ? new InputState { ShieldHeld = true } : default,
            });
            var state = sim.GetState(1);
            maxHorizontalDelta = Math.Max(maxHorizontalDelta, Math.Max(Math.Abs(state.PX - initialX), Math.Abs(state.PZ - initialZ)));
            maxRise = Math.Max(maxRise, state.PY - initialY);
            airborneDuringAttack |= !state.IsGrounded && sim.GetActiveAbility(1) != null;
            maxLandingLag = Math.Max(maxLandingLag, sim.GetState(1).LandingLagTicks);
            foreach (var hit in sim.LastTickHits.Where(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100))
            {
                if (firstContact < 0) firstContact = sim.GetState(1).AttackElapsedTicks;
                hits.Add(hit);
            }
        }
        return (sim, sim.GetState(1), sim.GetState(100), hits.ToArray(), firstContact, maxLandingLag, maxHorizontalDelta, maxRise, sim.GetState(1).PY - initialY, airborneDuringAttack);
    }

    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !Directory.Exists(Path.Combine(path.FullName, "content-cooked"))) path = path.Parent;
        return path?.FullName ?? throw new DirectoryNotFoundException("SLOPArena root");
    }
}
