using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for the opt-in Adaptive auto-angle (Melee's 361°): a hitbox flagged with
/// KnockbackProfile.Adaptive derives its launch pitch at hit time from the
/// hitbox→victim displacement instead of a fixed constant. Spikes stay authored
/// with the fixed KnockbackProfile.Spike; Adaptive is for combo/juggle tools.
///
/// Three layers:
///   1. Pure math — SpellResolver.ComputeAdaptiveAngle.
///   2. Profile plumbing — KnockbackData.Resolve() emits the sentinel + keeps base/growth.
///   3. Integration — a raw Adaptive hitbox through the sim adapts its launch to the
///      victim's height and never buries a grounded/level victim.
/// </summary>
public class AdaptiveAngleTests
{
    // ── 1. Pure math ──

    [Fact]
    public void ComputeAdaptiveAngle_LevelVictim_IsFlat()
    {
        // Victim level with the hitbox (dy=0) → 0° flat send.
        Assert.Equal(0, SpellResolver.ComputeAdaptiveAngle(1f, 0f, 0f));
    }

    [Fact]
    public void ComputeAdaptiveAngle_AirborneVictim_PopsUp()
    {
        // Victim 1m above, 1m in front → 45°.
        Assert.Equal(45, SpellResolver.ComputeAdaptiveAngle(1f, 1f, 0f));
        // Higher victim (dy=1 over horiz=0.5) → steeper: atan2(1, 0.5) ≈ 63°.
        Assert.Equal(63, SpellResolver.ComputeAdaptiveAngle(0.5f, 1f, 0f));
    }

    [Fact]
    public void ComputeAdaptiveAngle_BelowVictim_NeverBuries()
    {
        // Victim below the hitbox → raw angle is negative, clamped to 0 (never down).
        Assert.Equal(0, SpellResolver.ComputeAdaptiveAngle(1f, -1f, 0f));
    }

    [Fact]
    public void ComputeAdaptiveAngle_StraightUp_ClampedTo90()
    {
        // No horizontal component → full upward launch.
        Assert.Equal(90, SpellResolver.ComputeAdaptiveAngle(0f, 5f, 0f));
    }

    // ── 2. Profile plumbing ──

    [Fact]
    public void AdaptiveProfile_ResolvesToSentinel_KeepsBaseGrowth()
    {
        var kb = new KnockbackData
        {
            Profile = KnockbackProfile.Adaptive,
            Angle = 30,              // authored as documentation, ignored at runtime
            BaseKnockback = 4f,
            KnockbackGrowth = 20f,
        };
        var (angle, baseKB, growth) = kb.Resolve();
        Assert.Equal(KnockbackData.AdaptiveAngle, angle);
        Assert.Equal(4f, baseKB);
        Assert.Equal(20f, growth);
    }

    [Fact]
    public void CustomProfile_StillUsesItsOwnAngle()
    {
        var kb = new KnockbackData
        {
            Profile = KnockbackProfile.Custom,
            Angle = 30,
            BaseKnockback = 4f,
            KnockbackGrowth = 20f,
        };
        var (angle, baseKB, _) = kb.Resolve();
        Assert.Equal((sbyte)30, angle);
        Assert.Equal(4f, baseKB);
    }

    // ── 3. Integration through the sim ──

    /// <summary>
    /// Drop a raw Adaptive hitbox at (0, 1.0, 0.5) — radius 2 reaches a victim anywhere
    /// from PY 0.5 to 2.5 — against a victim at <paramref name="victimY"/>, tick past
    /// hitstop (1 + 1.5·5 = 8 for damage 5), and return the applied vertical launch.
    /// </summary>
    private static float LaunchKvy(float victimY)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var def = TestHelpers.EngineDef;

        // Attacker so the hitstop/queued-launch path is the real one.
        sim.RegisterEntity(1, def, TestHelpers.PlayerState());

        var npc = TestHelpers.NpcState(0f, 0f);
        npc.PY = victimY;
        sim.RegisterEntity(100, def, npc);

        sim.Resolver.Spawn(new Hitbox
        {
            X = 0f, Y = 1.0f, Z = 0.5f,
            EndX = 0f, EndY = 1.0f, EndZ = 0.5f,
            Radius = 2f,
            Damage = 5f,
            BaseKnockback = 6f,
            KnockbackGrowth = 6f,
            KnockbackAngle = KnockbackData.AdaptiveAngle,
            DurationTicks = 10,
            StunTicks = 30,
            OwnerId = 1,
        });

        for (int i = 0; i < 12; i++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            var launched = sim.GetState(100);
            // Inspect the launch angle before flight gravity bends the trajectory.
            if (launched.State == ActionState.Hitstun && launched.HitstopTicks == 0)
                return launched.KVY;
        }

        Assert.Fail("Adaptive hitbox did not launch the victim.");
        return 0f;
    }

    [Fact]
    public void AdaptiveHitbox_AdaptsToVictimHeight()
    {
        // Victim slightly above the hitbox (dy=0.5, horiz=0.5) → ~45° pop.
        float low = LaunchKvy(1.5f);
        // Victim well above the hitbox (dy=1.5, horiz=0.5) → ~71° steeper pop.
        float high = LaunchKvy(2.5f);

        Assert.True(high > low,
            $"higher victim should launch steeper: low.KVY={low:F3}, high.KVY={high:F3}");
        Assert.True(low > 0f, $"both should send up: low.KVY={low:F3}");
    }

    [Fact]
    public void AdaptiveHitbox_NeverBuriesGroundVictim()
    {
        // Hitbox above the victim (dy negative) → auto-angle clamps to 0 → no downward KVY.
        float kvy = LaunchKvy(0.5f);
        Assert.True(kvy >= 0f, $"Adaptive must never send down: KVY={kvy:F3}");
    }
}
