using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Data-recording harness for current knockback behavior. Simulates a full knockback
/// flight (Simulation.ApplyKnockback → SimulateTick, the exact path ResolveHits uses)
/// at various damage percents and dumps velocity / hitstun / distance metrics.
///
/// Not a pass/fail suite — the point is a repeatable snapshot of how knockback feels
/// today so the profile table (KnockbackProfile.cs) can be retuned. Sanity assertions
/// only pin the engine formulas the harness relies on.
///
/// Run:  dotnet test tests/Shared.Tests --filter "FullyQualifiedName~KnockbackPhysicsDataTests" --logger "console;verbosity=detailed"
/// CSV:  tests/Shared.Tests/bin/Debug/net8.0/knockback-current-data.csv
/// </summary>
public class KnockbackPhysicsDataTests
{
    private readonly ITestOutputHelper _output;
    public KnockbackPhysicsDataTests(ITestOutputHelper output) => _output = output;

    private static readonly CharacterDefinition Def = KnockbackFixture();
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    private static CharacterDefinition KnockbackFixture()
    {
        var def = TestHelpers.EngineDef;
        def.Weight = 100f;
        return def;
    }

    private static readonly int[] Percents = { 0, 25, 50, 75, 100, 125, 150, 175, 200 };

    // Profiles + a real-world custom move (Wibou charged finisher, package source).
    // stunTicks = 60 (engine cap) so hitstun is purely magnitude-derived:
    // hitstun = clamp(8 + magnitude*0.5, 8, 60). Real moves pass lower stun values,
    // which only shorten the constant-velocity phase, never lengthen it.
    private static readonly (string Name, sbyte Angle, float Base, float Growth)[] Cases =
    {
        ("Light",     15,  2f, 1.5f),
        ("Medium",    15,  8f, 5f),
        ("Launcher",  25,  8f, 4f),
        ("Kill",      20, 18f, 10f),
        ("Spike",    -45, 12f, 4f),
        ("WibouFin",  45, 14f, 8f), // Custom: Wibou charged finisher
    };

    private sealed record Flight(
        string Profile, sbyte Angle, float Base, float Growth, int Percent,
        float LaunchSpeed, int HitstunTicks,
        float DistanceAtHitstunEnd, float SpeedAtHitstunEnd,
        int TicksUntilSettled, float TotalDistance, float MaxHeight,
        float SpeedAtLanding, bool Landed);

    [Fact]
    public void Record_CurrentKnockbackBehavior_AcrossPercents()
    {
        var rows = new List<Flight>();
        var sb = new StringBuilder();

        foreach (var (name, angle, baseKB, growthKB) in Cases)
        {
            sb.AppendLine($"## {name}  (angle={angle}°, base={baseKB}, growth={growthKB})");
            sb.AppendLine("pct | launch m/s | hitstun t | dist@hitstun m | speed@hitstun | settle t | settle s | total dist m | max h m | speed@land");
            foreach (int pct in Percents)
            {
                var f = SimulateFlight(name, angle, baseKB, growthKB, pct);
                rows.Add(f);
                sb.AppendLine(FormattableString.Invariant(
                    $"{f.Percent,3}% | {f.LaunchSpeed,7:F2} | {f.HitstunTicks,6} | {f.DistanceAtHitstunEnd,11:F2} | {f.SpeedAtHitstunEnd,9:F2} | {f.TicksUntilSettled,6} | {f.TicksUntilSettled / 60f,6:F2} | {f.TotalDistance,10:F2} | {f.MaxHeight,6:F2} | {f.SpeedAtLanding,7:F2}"));
            }
            sb.AppendLine();
        }

        string csvPath = Path.Combine(AppContext.BaseDirectory, "knockback-current-data.csv");
        var csv = new StringBuilder();
        csv.AppendLine("profile,angleDeg,baseKB,growthKB,percent,launchSpeedMps,hitstunTicks,distAtHitstunEndM,speedAtHitstunEndMps,ticksUntilSettled,totalDistM,maxHeightM,speedAtLandingMps,landed");
        foreach (var f in rows)
        {
            csv.AppendLine(FormattableString.Invariant(
                $"{f.Profile},{f.Angle},{f.Base},{f.Growth},{f.Percent},{f.LaunchSpeed:F4},{f.HitstunTicks},{f.DistanceAtHitstunEnd:F4},{f.SpeedAtHitstunEnd:F4},{f.TicksUntilSettled},{f.TotalDistance:F4},{f.MaxHeight:F4},{f.SpeedAtLanding:F4},{f.Landed}"));
        }
        File.WriteAllText(csvPath, csv.ToString());

        // Per-tick trajectories for representative flights — shows the phase structure
        // (hitstun constant velocity → float window → ramp → fall) and validates the
        // speed-at-landing readout.
        var traj = new StringBuilder();
        traj.AppendLine("profile,pct,tick,state,grounded,PX,PY,VX,VY,KVX,KVY,AirTime");
        foreach (var (name, angle, baseKB, growthKB) in new[]
        {
            ("Light", (sbyte)15, 2f, 1.5f),    // low launch: near-instant land + slide
            ("Medium", (sbyte)15, 8f, 5f),     // mid: float stall then straight drop
            ("Kill", (sbyte)20, 18f, 10f),     // high: long constant-velocity hitstun
        })
        {
            foreach (int pct in new[] { 0, 100 })
                DumpTrajectory(traj, name, angle, baseKB, growthKB, pct);
        }
        string trajPath = Path.Combine(AppContext.BaseDirectory, "knockback-trajectories.csv");
        File.WriteAllText(trajPath, traj.ToString());
        _output.WriteLine($"Trajectories written to {trajPath}");

        _output.WriteLine(sb.ToString());
        _output.WriteLine($"CSV written to {csvPath}");

        // ADR-0019 invariant: launch magnitude includes accumulated damage,
        // hit damage, and target weight.
        foreach (var f in rows)
        {
            Assert.True(f.Landed, $"{f.Profile}@{f.Percent}% never settled (flew off the 200x200 heightmap?)");

            float rawMag = (f.Base + f.Growth * (f.Percent * 0.01f + 1f)) * 200f / (Def.Weight + 100f);
            // KnockbackScale scales launch velocity only; hitstun derives from the raw magnitude.
            TestHelpers.AssertNear(rawMag * CombatTuning.Default.KnockbackScale, f.LaunchSpeed, 0.01f);

            // Hitstun = trunc(StunCoefficient · (3D magnitude + MagBonus)), same float path as
            // Simulation.ApplyKnockback (magnitude * cos/sin, then sqrt) — replicating it
            // reproduces boundary truncation exactly. Knobs read from the sim so the table
            // tracks the shipped curve (melee-shape: 0.7, +20, KV×0.11).
            float rad = f.Angle * MathF.PI / 180f;
            float mag3 = MathF.Sqrt(MathF.Pow(rawMag * MathF.Cos(rad), 2f)
                + MathF.Pow(rawMag * MathF.Sin(rad), 2f));
            int expectedHitstun = Math.Clamp(
                (int)(CombatTuning.Default.HitstunCoefficient * (mag3 + CombatTuning.Default.HitstunMagnitudeBonus)), 1, ushort.MaxValue);
            Assert.Equal(expectedHitstun, f.HitstunTicks);

        }
    }

    private static void DumpTrajectory(StringBuilder sb, string name, sbyte angle, float baseKB, float growthKB, int pct)
    {
        var s = TestHelpers.PlayerState(x: 50f, z: 50f);
        s.PY = GroundPy;
        s.DamagePercent = (ushort)pct;
        Simulation.ApplyKnockback(ref s, 1f, 0f, angle, baseKB, growthKB, 0f, 60, 100f);

        var arena = TestHelpers.TestArena();
        for (int t = 0; t < 120 && !(s.IsGrounded && s.VX == 0f && s.VZ == 0f && s.VY == 0f); t++)
        {
            Simulation.SimulateTick(ref s, Def, default, arena, out _, out _, DownActionTuning.Default, false);
            sb.AppendLine(FormattableString.Invariant(
                $"{name},{pct},{t + 1},{s.State},{s.IsGrounded},{s.PX:F3},{s.PY:F3},{s.VX:F3},{s.VY:F3},{s.KVX:F3},{s.KVY:F3},{s.AirTimeTicks}"));
        }
    }

    /// <summary>
    /// Fire one knockback and tick to rest. Start at (10,10) so up to ~190 m of
    /// flight stays inside the TestArena heightmap [0,200)².
    /// </summary>
    private static Flight SimulateFlight(string name, sbyte angle, float baseKB, float growthKB, int pct)
    {
        var s = TestHelpers.PlayerState(x: 10f, z: 10f);
        s.PY = GroundPy;
        s.DamagePercent = (ushort)pct;

        // dirX=1, dirZ=0 → pure +X launch.
        Simulation.ApplyKnockback(ref s, 1f, 0f, angle, baseKB, growthKB, 0f, 60, 100f);

        float launchSpeed = MathF.Sqrt((s.KVX * s.KVX) + (s.KVY * s.KVY) + (s.KVZ * s.KVZ));
        int hitstunTicks = s.HitstunTicks;

        var arena = TestHelpers.TestArena();
        var prev = s;
        float totalDist = 0f;
        float distAtHitstunEnd = 0f;
        float speedAtHitstunEnd = 0f;
        float maxHeight = s.PY;
        float speedAtLanding = 0f;
        bool hitstunOver = false;
        bool firstLanding = true;
        bool landed = false;
        int ticks = 0;

        for (; ticks < 3600; ticks++) // 60 s ceiling
        {
            Simulation.SimulateTick(ref s, Def, default, arena, out _, out _, DownActionTuning.Default, false);

            float dHoriz = MathF.Sqrt(((s.PX - prev.PX) * (s.PX - prev.PX)) + ((s.PZ - prev.PZ) * (s.PZ - prev.PZ)));
            totalDist += dHoriz;
            maxHeight = MathF.Max(maxHeight, s.PY);

            // Hitstun expiry: KV transferred to V, state → Idle (ProcessHitstun).
            if (!hitstunOver && s.State != ActionState.Hitstun)
            {
                hitstunOver = true;
                distAtHitstunEnd = totalDist;
                speedAtHitstunEnd = MathF.Sqrt((s.VX * s.VX) + (s.VY * s.VY) + (s.VZ * s.VZ));
            }

            if (firstLanding && s.IsGrounded)
            {
                firstLanding = false;
                speedAtLanding = MathF.Sqrt((s.VX * s.VX) + (s.VY * s.VY) + (s.VZ * s.VZ));
            }

            // Settled: grounded with all velocity dead-zoned to exact zero.
            if (s.IsGrounded && s.VX == 0f && s.VZ == 0f && s.VY == 0f)
            {
                landed = true;
                break;
            }

            prev = s;
        }

        return new Flight(name, angle, baseKB, growthKB, pct,
            launchSpeed, hitstunTicks,
            distAtHitstunEnd, speedAtHitstunEnd,
            ticks + 1, totalDist, maxHeight,
            speedAtLanding, landed);
    }
}
