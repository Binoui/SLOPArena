using System;

namespace SlopArena.Shared;

/// <summary>
/// What a Match plays (ADR-0040). The Room Pick, the Match start contract and the client
/// descriptor carry it so the GameHost and every client build the same simulation.
/// </summary>
public enum MatchFormat : byte
{
    /// <summary>A stock fight on a PvP stage.</summary>
    Stock = 0,
    /// <summary>Two to four fighters on Slopball's player half against the Launcher.</summary>
    SlopballCoop = 1,
    SlopballVersus = 2,
    CourseRace = 3,
    CourseCoop = 4,
    MankiMayhem = 5,
}

public static class MatchFormats
{
    public const string MankiMayhemWire = "manki-mayhem";
    public const string StockWire = "stock";
    public const string SlopballCoopWire = "slopball-coop";
    public const string SlopballVersusWire = "slopball-versus";
    public const string CourseRaceWire = "course-race";
    public const string CourseCoopWire = "course-coop";
    public static bool IsCourse(this MatchFormat format) => format is MatchFormat.CourseRace or MatchFormat.CourseCoop;
    public static bool IsSlopball(this MatchFormat format)
        => format is MatchFormat.SlopballCoop or MatchFormat.SlopballVersus;

    /// <summary>Lateral spacing between co-op Slopball starts, metres.</summary>
    public const float SlopballCoopStartSpacing = 2.5f;

    public static string ToWire(this MatchFormat format) => format switch
    {
        MatchFormat.MankiMayhem => MankiMayhemWire,
        MatchFormat.Stock => StockWire,
        MatchFormat.SlopballCoop => SlopballCoopWire,
        MatchFormat.SlopballVersus => SlopballVersusWire,
        MatchFormat.CourseRace => CourseRaceWire,
        MatchFormat.CourseCoop => CourseCoopWire,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Parses a wire name; a missing value is a stock fight, as before formats existed.</summary>
    public static bool TryParseWire(string? value, out MatchFormat format)
    {
        switch (value)
        {
            case null:
            case StockWire:
                format = MatchFormat.Stock;
                return true;
            case MankiMayhemWire:
                format = MatchFormat.MankiMayhem;
                return true;
            case SlopballCoopWire:
                format = MatchFormat.SlopballCoop;
                return true;
            case SlopballVersusWire:
                format = MatchFormat.SlopballVersus;
                return true;
            case CourseRaceWire:
                format = MatchFormat.CourseRace;
                return true;
            case CourseCoopWire:
                format = MatchFormat.CourseCoop;
                return true;
            default:
                format = MatchFormat.Stock;
                return false;
        }
    }

    /// <summary>The arena a format needs: stock fights use PvP stages, Slopball formats its court.</summary>
    public static bool Admits(this MatchFormat format, in ArenaDefinition arena) => format switch
    {
        MatchFormat.MankiMayhem => arena.Arcade?.Mayhem != null,
        MatchFormat.Stock => arena.Purpose == ArenaPurpose.PvP,
        MatchFormat.SlopballCoop => arena.Arcade?.Slopball != null,
        MatchFormat.SlopballVersus => arena.Arcade?.Slopball != null,
        MatchFormat.CourseRace or MatchFormat.CourseCoop => arena.Arcade?.Course != null,
        _ => false,
    };

    /// <summary>The rule a format plays under; stock fights keep the Match's stock count.</summary>
    public static bool AdmitsFriendlyFire(this MatchFormat format, FriendlyFireMode mode) => format switch
    {
        MatchFormat.MankiMayhem => mode <= FriendlyFireMode.On,
        MatchFormat.SlopballCoop => mode is FriendlyFireMode.Off or FriendlyFireMode.On,
        _ => mode == FriendlyFireMode.Off,
    };

    public static IMatchRule CreateRule(this MatchFormat format, byte maxStocks, FriendlyFireMode friendlyFire = FriendlyFireMode.Off,
        System.Collections.Generic.IReadOnlyDictionary<ulong, SlopballCamp>? camps = null, bool ballOnly = false) => format switch
    {
        MatchFormat.MankiMayhem => new MankiMayhemMatchRule(friendlyFire),
        MatchFormat.Stock => new StockMatchRule(maxStocks),
        MatchFormat.SlopballCoop => new SlopballCoopMatchRule(friendlyFire: friendlyFire),
        MatchFormat.SlopballVersus => new SlopballVersusMatchRule(camps!, ballOnly),
        MatchFormat.CourseRace => new CourseMatchRule(cooperative: false),
        MatchFormat.CourseCoop => new CourseMatchRule(cooperative: true),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Where fighter <paramref name="index"/> of <paramref name="count"/> starts. Stock fights use
    /// the arena's spawn points; co-op Slopball spreads the court's one start sideways, so the solo
    /// start, and with it solo record fingerprints, stays as authored.
    /// </summary>
    public static SpawnPoint StartFor(this MatchFormat format, in ArenaDefinition arena, int index, int count,
        System.Collections.Generic.IReadOnlyList<SlopballCamp>? camps = null)
    {
        if (index < 0 || count < 1 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (arena.SpawnPoints == null || arena.SpawnPoints.Length == 0)
            throw new ArgumentException("The arena has no spawn points.", nameof(arena));
        if (format.IsCourse())
        {
            var grid = arena.SpawnPoints[0];
            float offset = (index - (count - 1) * .5f) * .9f;
            grid.X += MathF.Cos(grid.Yaw) * offset;
            grid.Z -= MathF.Sin(grid.Yaw) * offset;
            return grid;
        }
        if (!format.IsSlopball())
            return arena.SpawnPoints[index % arena.SpawnPoints.Length];
        var start = arena.SpawnPoints[0];
        if (format == MatchFormat.SlopballVersus)
        {
            if (camps == null || camps.Count != count || camps[index] is not (SlopballCamp.Teal or SlopballCamp.Clay))
                throw new ArgumentException("Versus starts need a camp for every fighter.", nameof(camps));
            var camp = camps[index];
            int ownIndex = 0, ownCount = 0;
            for (int i = 0; i < count; i++)
                if (camps[i] == camp) { if (i < index) ownIndex++; ownCount++; }
            start.X += (ownIndex - (ownCount - 1) * .5f) * SlopballCoopStartSpacing;
            if (camp == SlopballCamp.Clay)
            {
                start.Z = 2f * arena.Arcade!.Slopball!.NetZ - start.Z;
                start.Yaw += MathF.PI;
            }
            return start;
        }
        start.X += (index - (count - 1) * .5f) * SlopballCoopStartSpacing;
        return start;
    }
}
