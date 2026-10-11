using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tuning profile isolation and deterministic self-play contracts.
/// </summary>
public class TuningProfileTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    private static ArenaDefinition KillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }

    private static MatchRecord Run(string profile, int seed = 20260817, int maxTicks = 1500)
        => SelfPlayMatch.Run(Def, KillArena(), seed, TestHelpers.LoadBakedData(Def), maxTicks,
            combatTuning: TuningProfiles.Get(profile));

    private static string Trace(MatchRecord record)
        => JsonSerializer.Serialize(record, new JsonSerializerOptions { IncludeFields = true });

    [Fact]
    public void UnknownProfileFailsWithoutChangingExistingTuning()
    {
        var candidate = TuningProfiles.Get("floor30");
        Assert.False(TuningProfiles.TryGet("nope", out var fallback));
        Assert.Same(CombatTuning.Default, fallback);
        Assert.Same(candidate, TuningProfiles.Get("FLOOR30"));
        Assert.Throws<ArgumentException>(() => TuningProfiles.Get("nope"));
    }

    [Theory]
    [InlineData("base")]
    [InlineData("floor30")]
    public void SameSeed_SameProfile_ReproducesMatchBitForBit(string profile)
    {
        Assert.Equal(Trace(Run(profile)), Trace(Run(profile)));
    }

    [Fact]
    public async Task DifferentProfilesRunConcurrentlyWithoutChangingTheirTraces()
    {
        var baseline = Run("base");
        var candidate = Run("floor30");
        Assert.NotEmpty(baseline.Hits);
        Assert.NotEmpty(candidate.Hits);
        Assert.NotEqual(Trace(baseline), Trace(candidate));

        var concurrent = await Task.WhenAll(
            Task.Run(() => Run("base")),
            Task.Run(() => Run("floor30")));

        Assert.Equal(Trace(baseline), Trace(concurrent[0]));
        Assert.Equal(Trace(candidate), Trace(concurrent[1]));
    }

}
