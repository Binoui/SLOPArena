namespace SlopArena.Shared.AI;

/// <summary>
/// Named CPU challenge tiers shared by Solo, Training, and deterministic self-play.
/// The enum values are the serialized tier indices; no numeric difficulty scale is retained.
/// </summary>
public enum CpuDifficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// Fixed, deterministic tuning for the simple heuristic CPU. Probabilities are in the 0..1
/// range; callers supply the only <see cref="System.Random"/> used to sample them.
/// </summary>
public readonly struct BotDifficultyProfile
{
    public readonly int ReactionDelayTicks;
    public readonly float RangeError, ThreatRecognition, PunishRecognition, ConfirmReliability;
    public readonly ushort HesitationMin, HesitationMax, PunishMargin;
    public readonly byte PressureBudget;
    private BotDifficultyProfile(int delay, float range, float threat, float punish, float confirm,
        ushort hesitationMin, ushort hesitationMax, ushort margin, byte budget)
    {
        ReactionDelayTicks = delay; RangeError = range; ThreatRecognition = threat;
        PunishRecognition = punish; ConfirmReliability = confirm; HesitationMin = hesitationMin;
        HesitationMax = hesitationMax; PunishMargin = margin; PressureBudget = budget;
    }

    /// <summary>
    /// Normalize a serialized tier. Unknown values use the middle tier rather than changing
    /// match semantics through an unsupported numeric value.
    /// </summary>
    public static CpuDifficulty Normalize(CpuDifficulty difficulty)
        => difficulty switch
        {
            CpuDifficulty.Easy => CpuDifficulty.Easy,
            CpuDifficulty.Hard => CpuDifficulty.Hard,
            _ => CpuDifficulty.Normal,
        };

    /// <summary>Return the player-facing name for a CPU tier.</summary>
    public static string DisplayName(CpuDifficulty difficulty)
        => Normalize(difficulty).ToString().ToUpperInvariant();

    /// <summary>
    /// Return the fixed profile for a named CPU difficulty.
    /// Reaction delays are 24, 18, and 12 simulation ticks for Easy, Normal, and Hard.
    /// </summary>
    public static BotDifficultyProfile ForDifficulty(CpuDifficulty difficulty)
    {
        return Normalize(difficulty) switch
        {
            CpuDifficulty.Easy => new(24, .45f, .35f, .20f, .10f, 6, 12, 12, 1),
            CpuDifficulty.Normal => new(18, .20f, .70f, .65f, .50f, 2, 6, 6, 2),
            _ => new(12, .05f, .90f, .90f, .85f, 0, 2, 3, 3),
        };
    }
}
