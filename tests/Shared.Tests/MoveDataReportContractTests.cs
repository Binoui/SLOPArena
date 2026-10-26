using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using SlopArena.MoveDataReport;

namespace SlopArena.Shared.Tests;

public sealed class MoveDataReportContractTests
{
    private static readonly int[] Pcts = { 0, 30, 60, 90, 120 };

    private static CharacterDefinition ReportingDef()
    {
        var def = TestHelpers.EngineDef;
        def = def with { DisplayName = "Trajectory fixture" };
        def = def with { Weight = 137f };
        def = TestHelpers.WithMove(def, AbilitySlots.Slot3, false, "Fixture launcher", 30, 20,
            new[] { new HitboxEvent
                        {
                            TriggerTick = 4,
                            DurationTicks = 3,
                            Shape = HitboxShape.Sphere,
                            Radius = 0.5f,
                            OffZ = 1f,
                            Damage = 7f,
                            StunTicks = 1,
                            Knockback = new()
                            {
                                Profile = KnockbackProfile.Custom,
                                Angle = 40,
                                BaseKnockback = 20f,
                                KnockbackGrowth = 12f,
                            },
                        } });
        return def;
    }

    private static Program.ReportData Build()
    {
        var def = ReportingDef();
        var hits = Program.CollectHits(def);
        return Program.BuildReport(def, hits, Pcts.ToArray(), null, false, false);
    }

    [Fact]
    public void RepresentativeTrajectory_ExposesLaunchExpiryApexLandingAndSamples()
    {
        var report = Build();
        var trajectory = report.Moves.Single(m => m.Label == "g3 Fixture launcher").Trajectories.Single(t => t.Pct == 0);

        Assert.Equal("Trajectory fixture", report.Metadata.Character);
        Assert.Equal(Pcts, report.Metadata.RequestedPercents);
        Assert.Equal(Pcts, report.Percents);
        Assert.Equal("Trajectory fixture", report.Metadata.VictimCharacter);
        Assert.Equal(137f, report.Metadata.VictimWeight);
        Assert.Equal(60, report.Metadata.TickRateHz);
        Assert.Equal("neutral (no DI/SDI)", report.Metadata.DiMode);
        Assert.Equal("landing", trajectory.Termination);
        Assert.True(MathF.Abs(trajectory.LaunchVelocityZ) > 0.01f);
        Assert.True(MathF.Abs(trajectory.LaunchVelocityY) > 0.01f);
        Assert.Equal(trajectory.LaunchVelocityY, trajectory.InitialVerticalVelocity);
        Assert.True(trajectory.HitstunExpiryTick.HasValue);
        Assert.True(trajectory.HitstunExpiryPositionY.HasValue);
        Assert.True(trajectory.HitstunExpiryHorizontalDistance.HasValue);
        Assert.True(trajectory.ApexTick.HasValue);
        Assert.True(trajectory.LandedTick.HasValue);

        var apex = trajectory.Points.OrderByDescending(p => p.PositionY).First();
        Assert.Equal(trajectory.ApexTick, apex.Tick);
        Assert.Equal(trajectory.Apex, apex.Height, 5);
        var expiry = trajectory.Points.Single(p => p.Tick == trajectory.HitstunExpiryTick);
        Assert.True(expiry.InHitstun == false);
        Assert.Equal(trajectory.HitstunExpiryPositionX, expiry.PositionX);
        Assert.Equal(trajectory.HitstunExpiryPositionY, expiry.PositionY);
        Assert.Equal(trajectory.HitstunExpiryPositionZ, expiry.PositionZ);
        Assert.Contains(trajectory.Points, p => p.InHitstun);
        Assert.Contains(trajectory.Points, p => p.PositionX == 0f && p.PositionZ > 0f);
    }

    [Fact]
    public void RepeatedReportGeneration_HasIdenticalSemanticJson()
    {
        var first = Build();
        var second = Build();

        Assert.Equal(WithoutTimestamp(first), WithoutTimestamp(second));
    }

    [Fact]
    public async Task DifferentProfilesProduceStableReportsWhenRunConcurrently()
    {
        static Program.ReportData BuildProfile(string profile)
        {
            var def = ReportingDef();
            var hit = Program.CollectHits(def).Single(h => !h.Slot.Air && h.Slot.Slot == 3 && h.HitIndex == 0);
            return Program.BuildReport(def, new() { hit }, new[] { 0, 60 }, null, false, true,
                combatTuning: TuningProfiles.Get(profile));
        }

        var baseline = BuildProfile("base");
        var candidate = BuildProfile("floor30");
        Assert.NotEqual(baseline.Moves[0].Trajectories[0].LaunchVelocityZ,
            candidate.Moves[0].Trajectories[0].LaunchVelocityZ);
        Assert.NotEqual(baseline.Moves[0].Trajectories[0].HitstunExpiryTick,
            candidate.Moves[0].Trajectories[0].HitstunExpiryTick);
        var concurrent = await Task.WhenAll(
            Task.Run(() => BuildProfile("base")),
            Task.Run(() => BuildProfile("floor30")));

        Assert.Equal(WithoutTimestamp(baseline), WithoutTimestamp(concurrent[0]));
        Assert.Equal(WithoutTimestamp(candidate), WithoutTimestamp(concurrent[1]));
    }

    private static string WithoutTimestamp(Program.ReportData report)
    {
        var node = JsonNode.Parse(Program.ToJson(report))!.AsObject();
        node.Remove("generatedAt");
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public void FightGuyG1_UsesApplyKnockbackAndPipelineParity()
    {
        var def = TestHelpers.FightGuyDef;
        var hits = Program.CollectHits(def);
        var baked = TestHelpers.LoadBakedData(def);
        var runs = new Dictionary<(Program.SlotRef, int, int), Program.RunResult>();
        foreach (var hit in hits)
        foreach (var pct in new[] { 0, 120 })
            runs[(hit.Slot, hit.HitIndex, pct)] = Program.RunTrajectory(def, hit, pct);

        var g1 = hits.Single(h => !h.Slot.Air && h.Slot.Slot == 1 && h.HitIndex == 0);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        state.DamagePercent = (ushort)g1.Hit.Damage;
        Simulation.ApplyKnockback(ref state, 0f, 1f, g1.LaunchAngle, g1.BaseKb, g1.GrowthKb,
            g1.Hit.Damage, g1.Hit.StunTicks, def.Weight);
        Assert.Equal(runs[(g1.Slot, 0, 0)].Kv,
            MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ), 5);

        var parity = Program.ComputeParity(def, hits, runs, new[] { 0, 120 }, baked);
        var row = parity.Single(p => p.Label == "g1 Straight Punch" && p.Pct == 0);
        Assert.True(row.Ok, $"g1 parity diverged: direct {row.DirectKv:F3}, pipeline {row.PipeKv:F3}");
    }
}
