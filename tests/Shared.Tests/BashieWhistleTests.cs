using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BashieWhistleTests
{
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(false, 90f)]
    [InlineData(false, 180f)]
    [InlineData(false, 270f)]
    [InlineData(true, 0f)]
    [InlineData(true, 90f)]
    [InlineData(true, 180f)]
    [InlineData(true, 270f)]
    public void NearbyFighterIsStunnedFor120TicksWithoutLaunchOrSlide(bool victimAirborne, float direction)
    {
        var duel = new Duel(4f, direction: direction, victimAirborne: victimAirborne);
        duel.AdvanceToBlow();
        var hit = Assert.Single(duel.WhistleHits);
        Assert.False(hit.Blocked);
        Assert.Equal(0f, hit.Damage);
        Assert.Equal((byte)6, duel.Victim.HitstopTicks);
        Assert.Equal((ushort)0, duel.Victim.HitstunTicks);
        float x = duel.Victim.PX, z = duel.Victim.PZ, y = duel.Victim.PY;
        for (int tick = 0; tick < 6; tick++)
        {
            Assert.Equal((byte)(6 - tick), duel.Victim.HitstopTicks);
            duel.Step();
        }
        Assert.Equal((byte)0, duel.Victim.HitstopTicks);
        bool fell = false, landedInStun = false;
        for (int tick = 0; tick < 120; tick++)
        {
            var victim = duel.Victim;
            Assert.Equal(ActionState.Hitstun, victim.State);
            Assert.Equal((ushort)(120 - tick), victim.HitstunTicks);
            Assert.InRange(MathF.Abs(victim.PX - x), 0f, .0099f);
            Assert.InRange(MathF.Abs(victim.PZ - z), 0f, .0099f);
            Assert.True(victim.PY <= y + .001f);
            Assert.True(victim.VY <= 0f);
            if (!victimAirborne) Assert.True(victim.IsGrounded);
            fell |= victim.PY < y - .01f;
            landedInStun |= victim.IsGrounded;
            duel.Step();
        }
        Assert.NotEqual(ActionState.Hitstun, duel.Victim.State);
        Assert.Equal((ushort)0, duel.Victim.HitstunTicks);
        Assert.True(duel.Victim.IsGrounded);
        Assert.True(landedInStun);
        if (victimAirborne) Assert.True(fell);
        Assert.Single(duel.WhistleHits);
        duel.AssertNoDamageOrSelfHit();
    }

    [Fact]
    public void FighterSixMetresAwayIsUntouched()
    {
        var duel = new Duel(6f);
        duel.Run(210);
        Assert.Empty(duel.WhistleHits);
        Assert.False(duel.WasStunned);
        Assert.Equal(106f, duel.Victim.PZ);
        duel.AssertNoDamageOrSelfHit();
    }

    [Fact]
    public void ShieldBlocksWhistleWithoutStun()
    {
        var duel = new Duel(4f, shield: true);
        duel.Run(210);
        Assert.NotEmpty(duel.WhistleHits);
        Assert.All(duel.WhistleHits, hit => Assert.True(hit.Blocked));
        Assert.False(duel.WasStunned);
        duel.AssertNoDamageOrSelfHit();
    }

    [Fact]
    public void InvincibleFighterIsUntouched()
    {
        var duel = new Duel(4f, invincible: true);
        duel.Run(210);
        Assert.Empty(duel.WhistleHits);
        Assert.False(duel.WasStunned);
        duel.AssertNoDamageOrSelfHit();
    }

    [Fact]
    public void HitDuringInhaleCancelsTheBlowAndStartsCooldown()
    {
        var duel = new Duel(4f);
        var interrupter = TestHelpers.NpcState(100f, 99.1f);
        interrupter.EntityId = 200;
        interrupter.PY = TestHelpers.GroundPY(duel.Content.Definition);
        duel.Sim.RegisterEntity(200, duel.Content.Definition, interrupter, duel.Content.BakedAnimation);
        for (int tick = 0; tick < 100; tick++) duel.Step(interrupt: tick == 15);
        Assert.Contains(duel.AllHits, hit => hit.OwnerEntityId == 200 && hit.TargetEntityId == 1 && !hit.Blocked);
        Assert.Empty(duel.WhistleHits);
        Assert.False(duel.WasStunned);
        Assert.True(duel.Attacker.GetCooldown(AbilitySlots.F) > 0);
        Assert.Null(duel.Sim.GetActiveAbility(1));
    }

    [Fact]
    public void AirVariantBlowsAndResumesFallingAfterInhale()
    {
        var duel = new Duel(4f, attackerAirborne: true);
        duel.AdvanceToBlow();
        Assert.False(duel.Attacker.IsGrounded);
        Assert.Single(duel.WhistleHits);
        duel.Run(6);
        Assert.Equal(ActionState.Hitstun, duel.Victim.State);
        Assert.Equal((ushort)120, duel.Victim.HitstunTicks);
        float blowHeight = duel.Attacker.PY;
        duel.Run(150);
        Assert.True(duel.Attacker.PY < blowHeight);
        Assert.True(duel.Attacker.IsGrounded);
        duel.AssertNoDamageOrSelfHit();
    }

    [Fact]
    public void FinishingWhistleStarts720TickSharedSlotCooldown()
    {
        var duel = new Duel(6f);
        for (int tick = 0; tick < 71; tick++)
        {
            duel.Step();
            Assert.Equal((ushort)0, duel.Attacker.GetCooldown(AbilitySlots.F));
            Assert.NotNull(duel.Sim.GetActiveAbility(1));
        }
        duel.Step();
        Assert.Null(duel.Sim.GetActiveAbility(1));
        Assert.Equal((ushort)720, duel.Attacker.GetCooldown(AbilitySlots.F));
        duel.Step(press: true);
        Assert.Null(duel.Sim.GetActiveAbility(1));
        Assert.Equal((ushort)719, duel.Attacker.GetCooldown(AbilitySlots.F));
        duel.AssertNoDamageOrSelfHit();
    }

    private sealed class Duel
    {
        internal readonly MatchContentEntry Content;
        internal readonly ServerSimulation Sim = TestHelpers.MakeSim();
        internal readonly List<SpellResolver.HitResult> AllHits = new();
        internal IEnumerable<SpellResolver.HitResult> WhistleHits => AllHits.Where(hit => hit.OwnerEntityId == 1);
        internal CharacterState Attacker => Sim.GetState(1);
        internal CharacterState Victim => Sim.GetState(100);
        internal bool WasStunned;
        private readonly bool _shield;
        private readonly short _victimYaw;
        private int _tick;

        internal Duel(float distance, float direction = 0f, bool victimAirborne = false,
            bool attackerAirborne = false, bool shield = false, bool invincible = false)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "content-cooked"))) directory = directory.Parent;
            Assert.NotNull(directory);
            Content = BuiltInContentResolver.Resolve(CharacterClass.Bashie, Path.Combine(directory!.FullName, "content-cooked"));
            var attacker = TestHelpers.PlayerState(100f, 100f);
            attacker.PY = TestHelpers.GroundPY(Content.Definition) + (attackerAirborne ? 3f : 0f);
            attacker.IsGrounded = !attackerAirborne;
            Sim.RegisterEntity(1, Content.Definition, attacker, Content.BakedAnimation);
            float radians = direction * MathF.PI / 180f;
            var victim = TestHelpers.NpcState(100f + MathF.Sin(radians) * distance, 100f + MathF.Cos(radians) * distance);
            victim.PY = TestHelpers.GroundPY(Content.Definition) + (victimAirborne || attackerAirborne ? 3f : 0f);
            victim.IsGrounded = !victimAirborne && !attackerAirborne;
            victim.FacingYaw = radians + MathF.PI;
            victim.InvincibilityTicks = invincible ? (ushort)240 : (ushort)0;
            _victimYaw = (short)((direction >= 180 ? direction - 180 : direction + 180) * 100);
            _shield = shield;
            Sim.RegisterEntity(100, Content.Definition, victim, Content.BakedAnimation);
        }

        internal void Step(bool press = false, bool interrupt = false)
        {
            Sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = _tick == 0 || press ? AbilitySlots.F : (byte)0 },
                [100] = new() { ShieldHeld = _shield, FacingYaw = _victimYaw },
                [200] = new() { ActiveSlot = interrupt ? AbilitySlots.Slot1 : (byte)0 },
            });
            _tick++;
            AllHits.AddRange(Sim.LastTickHits);
            WasStunned |= Victim.State == ActionState.Hitstun;
        }

        internal void AdvanceToBlow()
        {
            Run(47);
            Assert.Empty(WhistleHits);
            Assert.False(WasStunned);
            Step();
            Assert.Equal((ushort)48, Attacker.AttackElapsedTicks);
        }

        internal void Run(int ticks) { for (int tick = 0; tick < ticks; tick++) Step(); }

        internal void AssertNoDamageOrSelfHit()
        {
            Assert.Equal((ushort)0, Attacker.DamagePercent);
            Assert.Equal((ushort)0, Victim.DamagePercent);
            Assert.DoesNotContain(AllHits, hit => hit.OwnerEntityId == hit.TargetEntityId);
        }
    }
}
