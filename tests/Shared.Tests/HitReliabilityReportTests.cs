using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class HitReliabilityReportTests
{
    private static readonly List<SpellResolver.EntityData> Pillar = new()
    {
        new SpellResolver.EntityData
        {
            Id = 2, Shape = HitboxShape.Capsule, Active = true, Radius = 0.3f,
            PosX = 0f, PosY = 0.2f, PosZ = 2f, EndX = 0f, EndY = 1.4f, EndZ = 2f,
        },
    };

    private static Hitbox Blade(float tipX) => new()
    {
        Shape = HitboxShape.Capsule, Radius = 0.1f,
        X = 0f, Y = 1f, Z = 0f, EndX = tipX, EndY = 1f, EndZ = 2f,
    };

    [Fact]
    public void Intervals_MergeContiguousStepsAndKeepGaps()
    {
        var bands = HitReliability.Intervals(new[] { 0.30f, 0.35f, 0.40f, 1.00f, 1.05f }, 0.05f);

        Assert.Equal(2, bands.Count);
        Assert.Equal((0.30f, 0.40f), bands[0]);
        Assert.Equal((1.00f, 1.05f), bands[1]);
    }

    [Fact]
    public void ArcAgainst_MapsWhereAFlatBladeTouchesAStandingButNotAHighTarget()
    {
        // A target pillar at the origin (0.2-1.4 m up) and a flat blade 1 m up from 0.5 to 2 m ahead.
        var target = new List<SpellResolver.EntityData>
        {
            new() { Id = 2, Shape = HitboxShape.Capsule, Active = true, Radius = 0.3f, PosY = 0.2f, EndY = 1.4f },
        };
        var blade = new Hitbox { Shape = HitboxShape.Capsule, Radius = 0.1f, Y = 1f, Z = 0.5f, EndY = 1f, EndZ = 2f };

        var arc = HitReliability.ArcAgainst(new List<List<Hitbox>> { new() { blade } }, "T", target);

        string standing = arc.Rows[arc.Heights.IndexOf(0f)];
        Assert.Equal('#', standing[arc.Distances.IndexOf(1f)]);
        Assert.Equal('.', standing[arc.Distances.IndexOf(-1f)]);
        Assert.DoesNotContain('#', arc.Rows[arc.Heights.IndexOf(3f)]);
    }

    [Fact]
    public void Flag_ReportsFastClipsPointBlankGapsLowReachAndSamplingLoss()
    {
        var move = new MoveReliability { Slot = "air.2", Air = true, PlaybackSpeed = 1.8f, ActiveTicks = 8 };
        move.Vertical.Add(new VerticalBand { Target = "Manki", Lowest = 0.2f, Highest = 2f });
        move.Vertical.Add(new VerticalBand { Target = "FightGuy", Lowest = -0.3f, Highest = 2f });
        move.Sampling.Add(new SamplingRow { SweptPositions = 100, TestedPositions = 40, TargetFeetHeight = -1f });
        move.Sampling.Add(new SamplingRow { SweptPositions = 3, TestedPositions = 0, TargetFeetHeight = -1.5f });

        HitReliability.Flag(move);

        Assert.Contains(move.Flags, x => x.Contains("1.80×"));
        Assert.Contains(move.Flags, x => x.StartsWith("cannot reach below its feet vs Manki"));
        Assert.Contains(move.Flags, x => x == "reaches only 0.3 m below its feet vs FightGuy");
        Assert.Contains(move.Flags, x => x.StartsWith("60% of swept positions untested"));
        Assert.DoesNotContain(move.Flags, x => x.Contains("-1.5 m"));
    }

    [Fact]
    public void ParseSlots_AcceptsCanonicalKeysAndRejectsUnknownOnes()
    {
        Assert.Equal(new[] { AbilitySlots.Normal1, AbilitySlots.Normal3, AbilitySlots.F }, SlopArena.HitReliabilityReport.Program.ParseSlots("1, 3,f,F"));
        Assert.Null(SlopArena.HitReliabilityReport.Program.ParseSlots("1,4"));
        Assert.Null(SlopArena.HitReliabilityReport.Program.ParseSlots(""));
    }

    [Fact]
    public void BearingsTouched_CountsOnlyTheBearingsTheBladeReaches()
    {
        // The pillar stands 1.5 m away on each of 24 bearings; the blade points straight ahead, so
        // it reaches the pillar straight ahead and 15° to either side, not 30° or behind.
        var centred = Pillar[0];
        centred.PosZ = 0f;
        centred.EndZ = 0f;

        Assert.Equal(3, HitReliability.BearingsTouched(new[] { Blade(0f) }, new[] { centred }, 1.5f));
    }

    [Fact]
    public void Flag_ReportsHitsThatNeverTouchAStandingTarget()
    {
        var move = new MoveReliability { Slot = "ground.F", ActiveTicks = 74, ContactTicksAtProbe = 10 };
        move.Hits.Add(new HitCoverage { FirstTick = 2, Touched = { ("Manki", 1f, 8), ("Manki", 1.5f, 5) } });
        move.Hits.Add(new HitCoverage { FirstTick = 16, Touched = { ("Manki", 1f, 0), ("Manki", 1.5f, 0) } });
        move.Hits.Add(new HitCoverage { FirstTick = 44, Touched = { ("Manki", 1f, 0), ("Manki", 1.5f, 0) } });

        HitReliability.Flag(move);

        Assert.Equal("2 of 3 hits (t16, t44) never touch a standing Manki 1–1.5 m away", Assert.Single(move.Flags));
    }

    [Fact]
    public void Flag_ReportsPointBlankWhiffOnlyAtTheDirectBearing()
    {
        var move = new MoveReliability { Slot = "ground.2", ActiveTicks = 10, ContactTicksAtProbe = 3 };
        move.Reach.Add(new ReachBand { Target = "Manki", BearingDegrees = 0f, Intervals = { (0.75f, 2.15f) } });
        move.Reach.Add(new ReachBand { Target = "Manki", BearingDegrees = 30f, Intervals = { (0.80f, 2.0f) } });

        HitReliability.Flag(move);

        Assert.Equal("misses Manki closer than 0.75 m", Assert.Single(move.Flags));
    }

    [Fact]
    public void Swing_FlagsTheUnsweptTurnIntoTheFirstActiveTick()
    {
        var (move, _) = SwingMove(t => t < 10 ? 0 : 90 + (t - 10) * 10);
        var swing = Assert.Single(move.Swing);
        Assert.Equal(10, swing.PrePeakTick);
        Assert.InRange(swing.PrePeakDegrees, 89.9f, 90.1f);
        Assert.InRange(swing.InPeakDegrees, 9.9f, 10.1f);
        Assert.InRange(swing.InsideShare, 0.076, 0.078); // 20 degrees inside / 260 degrees through tick 27.
        Assert.Contains(move.Flags, x => x.StartsWith("swing peaks before the hitbox (t10,"));
        Assert.Equal(0, swing.ParkedTicks);
    }

    [Fact]
    public void Swing_EvenTurnThroughTheWindowHasNoSwingFlagsAndSerializesNamedFields()
    {
        var (move, identity) = SwingMove(t => t * 10);
        var swing = Assert.Single(move.Swing);
        Assert.InRange(swing.InsideShare, 2.0 / 27 - 0.001, 2.0 / 27 + 0.001);
        Assert.Empty(swing.Flags);
        Assert.DoesNotContain(move.Flags, x => x.Contains("parked") || x.StartsWith("swing peaks"));
        using var json = JsonDocument.Parse(new HitReliabilityDocument(identity, new[] { move },
            HitReliabilityTargets.Load(), DateTime.UnixEpoch).ToJson());
        var row = json.RootElement.GetProperty("moves")[0].GetProperty("Swing")[0];
        Assert.InRange(row.GetProperty("InPeakDegrees").GetSingle(), 9.9f, 10.1f);
        Assert.True(row.TryGetProperty("InsideShare", out _));
    }

    [Fact]
    public void Swing_StillPlankReportsParkedTicksExcludingTheFirstActiveTick()
    {
        var (move, _) = SwingMove(_ => 0);
        var swing = Assert.Single(move.Swing);
        Assert.Equal(2, swing.ParkedTicks);
        Assert.Equal(0, swing.InsideShare);
        Assert.Contains("2 of 3 active ticks parked", move.Flags);
    }

    [Fact]
    public void Swing_TranslatingSingleBoneCapsuleUsesTipTravelToAvoidParkedFlag()
    {
        var (move, _) = SwingMove(_ => 0, singleBone: true);
        var swing = Assert.Single(move.Swing);
        Assert.Equal("_hilt", swing.Anchor);
        Assert.Equal(0, swing.ParkedTicks);
        Assert.Empty(swing.Flags);
    }

    private static (MoveReliability Move, MatchContentIdentity Identity) SwingMove(Func<int, float> angle, bool singleBone = false)
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var slots = entry.Definition.CookedSlots!.ToArray();
        slots[7] = new CookedSlotDefinition(7, "air.1", true, "Synthetic swing", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(40, 0, 0, 0, 0, new[] { "swing" }, new CookedTimelineOperation[]
                {
                    new CookedSpawnHitboxOperation(10, AuthoringUnit.Meters,
                        new CookedHitbox(AuthoringHitboxShape.Capsule, .1f, 0, 0, 0, 0, 0, singleBone ? 1 : 0,
                            "_hilt", singleBone ? null : "_tip", 1, 0, 0, 0, 0, 3, true, 0)),
                }),
            }));
        var poses = new BakedAnimationData
        {
            BoneNames = new[] { "_hilt", "_tip" },
            Animations = new[] { new BakedAnimationData.BakedAnim
            {
                Name = "swing", Frames = Enumerable.Range(0, 40).Select(t =>
                {
                    float radians = angle(t) * MathF.PI / 180;
                    float x = singleBone ? t * 0.2f : 0;
                    return (IReadOnlyList<float>)new[] { x, 0, 0, x + MathF.Sin(radians), 0, MathF.Cos(radians) };
                }).ToArray(),
            } },
        };
        CanonicalSlotProjection.TryGet(true, "1", out var slot);
        return (HitReliability.Measure(entry.Definition with { CookedSlots = slots }, poses, slot,
            HitReliabilityTargets.Load())!, entry.Identity);
    }
}
