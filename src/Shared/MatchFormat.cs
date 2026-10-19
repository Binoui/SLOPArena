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
}

public static class MatchFormats
{
    public const string StockWire = "stock";
    public const string SlopballCoopWire = "slopball-coop";

    /// <summary>Lateral spacing between co-op Slopball starts, metres.</summary>
    public const float SlopballCoopStartSpacing = 2.5f;

    public static string ToWire(this MatchFormat format) => format switch
    {
        MatchFormat.Stock => StockWire,
        MatchFormat.SlopballCoop => SlopballCoopWire,
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
            case SlopballCoopWire:
                format = MatchFormat.SlopballCoop;
                return true;
            default:
                format = MatchFormat.Stock;
                return false;
        }
    }

    /// <summary>The arena a format needs: stock fights use PvP stages, Slopball formats its court.</summary>
    public static bool Admits(this MatchFormat format, in ArenaDefinition arena) => format switch
    {
        MatchFormat.Stock => arena.Purpose == ArenaPurpose.PvP,
        MatchFormat.SlopballCoop => arena.Arcade?.Slopball != null,
        _ => false,
    };

    /// <summary>The rule a format plays under; stock fights keep the Match's stock count.</summary>
    public static IMatchRule CreateRule(this MatchFormat format, byte maxStocks) => format switch
    {
        MatchFormat.Stock => new StockMatchRule(maxStocks),
        MatchFormat.SlopballCoop => new SlopballCoopMatchRule(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Where fighter <paramref name="index"/> of <paramref name="count"/> starts. Stock fights use
    /// the arena's spawn points; co-op Slopball spreads the court's one start sideways, so the solo
    /// start, and with it solo record fingerprints, stays as authored.
    /// </summary>
    public static SpawnPoint StartFor(this MatchFormat format, in ArenaDefinition arena, int index, int count)
    {
        if (index < 0 || count < 1 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (arena.SpawnPoints == null || arena.SpawnPoints.Length == 0)
            throw new ArgumentException("The arena has no spawn points.", nameof(arena));
        if (format != MatchFormat.SlopballCoop)
            return arena.SpawnPoints[index % arena.SpawnPoints.Length];
        var start = arena.SpawnPoints[0];
        start.X += (index - (count - 1) * .5f) * SlopballCoopStartSpacing;
        return start;
    }
}
