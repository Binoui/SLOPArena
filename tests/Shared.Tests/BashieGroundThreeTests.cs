using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class BashieGroundThreeTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void SavedShellFlipLaunchesOnceInEveryFacing(float yaw)
    {
        var duel = new Duel(yaw, 1f);
        duel.Run(90);
        var hit = Assert.Single(duel.Hits);
        Assert.False(hit.Blocked);
        Assert.Equal(7f, hit.Damage);
        Assert.Equal((sbyte)75, hit.KnockbackAngle);
        Assert.Equal(5f, hit.BaseKnockback);
        Assert.Equal(26f, hit.KnockbackGrowth);
        Assert.Equal((ushort)18, hit.StunTicks);
        Assert.InRange(duel.ContactTicks.Single(), 24, 29);
        Assert.True(duel.MaxVictimKnockbackY > 0f);
        Assert.Equal((ushort)7, duel.Sim.GetState(100).DamagePercent);
        duel.AssertClean();
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    public void EveryActiveTickCanAcceptOneFreshContact(int attackTick)
    {
        var duel = new Duel(0f, 4f);
        duel.AdvanceTo(attackTick - 1);
        duel.PlaceVictim(1f);
        duel.Step();
        Assert.Equal(7f, Assert.Single(duel.Hits).Damage);
        Assert.Equal(attackTick, duel.ContactTicks.Single());
        duel.Run(90);
        Assert.Single(duel.Hits);
        duel.AssertClean();
    }

    [Theory]
    [InlineData(23)]
    [InlineData(30)]
    public void OutsideActiveWindowCannotHit(int attackTick)
    {
        var duel = new Duel(0f, 4f);
        duel.AdvanceTo(attackTick - 1);
        duel.PlaceVictim(1f);
        duel.Step();
        Assert.Empty(duel.Hits);
        duel.PlaceVictim(4f);
        duel.Run(90);
        Assert.Empty(duel.Hits);
        duel.AssertClean();
    }

    [Fact]
    public void SavedFlipReachesAnOverheadTarget()
    {
        var duel = new Duel(0f, .65f);
        duel.PlaceVictim(.65f, 1.1f);
        duel.Sim.NoGravityEntityId = 100;
        duel.Run(90);
        Assert.Equal(7f, Assert.Single(duel.Hits).Damage);
        Assert.True(duel.MaxVictimKnockbackY > 0f);
        duel.AssertClean();
    }

    [Fact]
    public void ShieldBlocksDamageAndFarTargetWhiffs()
    {
        var shielded = new Duel(0f, 1f, shield: true);
        shielded.Run(90);
        Assert.True(Assert.Single(shielded.Hits).Blocked);
        Assert.Equal((ushort)0, shielded.Sim.GetState(100).DamagePercent);
        shielded.AssertClean();
        var whiff = new Duel(0f, 4f);
        whiff.Run(90);
        Assert.Empty(whiff.Hits);
        whiff.AssertClean();
    }

    [Theory]
    [InlineData(18)]
    [InlineData(25)]
    public void InterruptionRemovesTheAttackAndAllRemainingContacts(int interruptTick)
    {
        var duel = new Duel(0f, 4f);
        duel.AdvanceTo(interruptTick);
        duel.Sim.SetState(1, duel.Sim.GetState(1) with
        {
            State = ActionState.Hitstun, HitstunTicks = 10, HitstopTicks = 0,
        });
        duel.Step();
        Assert.Null(duel.Sim.GetActiveAbility(1));
        Assert.Empty(duel.Sim.Resolver.GetActiveHitboxes());
        duel.PlaceVictim(1f);
        duel.Run(90);
        Assert.Empty(duel.Hits);
        duel.AssertClean();
    }

    [Fact]
    public void RecoveryCompletesAt42WithoutRootDriftHopOrSlideCarry()
    {
        var duel = new Duel(0f, 4f, moving: true);
        duel.AdvanceTo(41);
        Assert.NotNull(duel.Sim.GetActiveAbility(1));
        duel.Step();
        Assert.Null(duel.Sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, duel.Sim.GetState(1).State);
        Assert.InRange(duel.MaxRootDelta, 0f, .001f);
        Assert.False(duel.WasAirborne);
        duel.Step(AbilitySlots.Normal1);
        Assert.Equal(AbilitySlots.Normal1, duel.Sim.GetState(1).AttackSlot);
        Assert.NotNull(duel.Sim.GetActiveAbility(1));
    }

    private sealed class Duel
    {
        internal readonly ServerSimulation Sim;
        internal readonly List<SpellResolver.HitResult> Hits = new();
        internal readonly List<int> ContactTicks = new();
        internal float MaxVictimKnockbackY, MaxRootDelta;
        internal bool WasAirborne;
        private readonly MatchContentEntry _victim;
        private readonly float _yaw;
        private readonly bool _shield;
        private readonly float _floor;
        private int _frames;

        internal Duel(float yaw, float distance, bool shield = false, bool moving = false)
        {
            string root = CookedRoot();
            var content = BuiltInContentResolver.Resolve(CharacterClass.Bashie, root);
            _victim = BuiltInContentResolver.Resolve(CharacterClass.Manki, root);
            Sim = new ServerSimulation(TestHelpers.TestArena());
            _yaw = yaw * MathF.PI / 180f;
            _shield = shield;
            _floor = TestHelpers.GroundPY(content.Definition);
            var attacker = TestHelpers.PlayerState(100f, 100f);
            attacker.PY = _floor;
            attacker.FacingYaw = _yaw;
            if (moving) { attacker.VX = 3f; attacker.VZ = 2f; }
            Sim.RegisterEntity(1, content.Definition, attacker, content.BakedAnimation);
            var victim = TestHelpers.NpcState(100f + MathF.Sin(_yaw) * distance, 100f + MathF.Cos(_yaw) * distance);
            victim.PY = TestHelpers.GroundPY(_victim.Definition);
            victim.FacingYaw = _yaw + MathF.PI;
            Sim.RegisterEntity(100, _victim.Definition, victim, _victim.BakedAnimation);
        }

        internal void PlaceVictim(float distance, float height = 0f)
        {
            var victim = Sim.GetState(100);
            victim.PX = 100f + MathF.Sin(_yaw) * distance;
            victim.PZ = 100f + MathF.Cos(_yaw) * distance;
            victim.PY = TestHelpers.GroundPY(_victim.Definition) + height;
            victim.IsGrounded = height == 0f;
            Sim.SetState(100, victim);
        }

        internal void Step(byte slot = 0)
        {
            short wireYaw = (short)MathF.Round(_yaw * 180f / MathF.PI * 100f);
            Sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = _frames == 0 ? AbilitySlots.Normal2 : slot, FacingYaw = wireYaw, AimYaw = wireYaw },
                [100] = _shield ? new InputState { ShieldHeld = true } : default,
            });
            _frames++;
            var attacker = Sim.GetState(1);
            var victim = Sim.GetState(100);
            MaxVictimKnockbackY = Math.Max(MaxVictimKnockbackY, victim.KVY);
            MaxRootDelta = Math.Max(MaxRootDelta, Math.Max(Math.Abs(attacker.PX - 100f),
                Math.Max(Math.Abs(attacker.PY - _floor), Math.Abs(attacker.PZ - 100f))));
            WasAirborne |= !attacker.IsGrounded;
            foreach (var hit in Sim.LastTickHits.Where(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100))
            {
                Hits.Add(hit);
                ContactTicks.Add(attacker.AttackElapsedTicks);
            }
        }

        internal void AdvanceTo(int attackTick)
        {
            for (int guard = 0; guard < 120; guard++)
            {
                if (_frames > 0 && Sim.GetState(1).AttackElapsedTicks >= attackTick) return;
                Step();
            }
            throw new InvalidOperationException("Shell Flip did not reach attack tick " + attackTick);
        }

        internal void Run(int frames) { for (int i = 0; i < frames; i++) Step(); }

        internal void AssertClean()
        {
            Assert.Null(Sim.GetActiveAbility(1));
            Assert.Empty(Sim.Resolver.GetActiveHitboxes());
            Assert.InRange(MaxRootDelta, 0f, .001f);
            Assert.False(WasAirborne);
        }
    }

    private static string CookedRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "content-cooked")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("SLOPArena root"), "content-cooked");
    }
}
