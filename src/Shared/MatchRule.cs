using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>
/// Outcome of a match win check.
/// </summary>
/// <param name="IsEnded">True when the match should end (single winner or shared victory).</param>
/// <param name="WinnerEntityId">Sole winner's entity ID; 0 for shared victory.</param>
/// <param name="IsSharedVictory">True when no single winner (simultaneous last-stock trade).</param>
public readonly record struct MatchOutcome(bool IsEnded, ulong WinnerEntityId, bool IsSharedVictory);

/// <summary>
/// Win-condition rule for a match mode (ADR-0007, issue #37).
/// Owns elimination (whether a lost entity becomes a frozen spectator) and the
/// match-end decision. The simulation and MatchInstance ask the rule; they
/// never hardcode one mode's stock semantics — new modes (timed, first-to-N-KOs)
/// implement this interface instead of touching sim/match plumbing.
/// </summary>
public interface IMatchRule
{
    /// <summary>True once the entity has lost and is spectating (frozen, untargetable, no input).</summary>
    bool IsEliminated(CharacterState state);

    /// <summary>Match outcome from current entity states (running, winner, or shared victory).</summary>
    MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states);
}

/// <summary>
/// Stock mode: each player starts with <see cref="MaxStocks"/> stocks; a KO
/// (void death / blast zone) costs one; 0 stocks = eliminated. Last player
/// with stocks remaining wins; a simultaneous last-stock trade goes to the
/// player with most remaining stocks, ties → shared victory (ADR-0007, issue #37).
/// </summary>
public sealed class StockMatchRule : IMatchRule
{
    public byte MaxStocks { get; }

    public StockMatchRule(byte maxStocks) => MaxStocks = maxStocks;

    public bool IsEliminated(CharacterState state) => state.Deaths >= MaxStocks;

    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states)
    {
        ulong? soleSurvivor = null;
        int alive = 0;
        foreach (var (id, st) in states)
        {
            if (!IsEliminated(st))
            {
                alive++;
                soleSurvivor = id;
            }
        }
        if (alive == 1) return new MatchOutcome(true, soleSurvivor!.Value, false);
        if (alive > 1) return default;

        // Zero players with stocks left: most stocks (fewest deaths) wins;
        // equal deaths → shared victory.
        ulong best = ulong.MaxValue;
        byte bestDeaths = byte.MaxValue;
        bool bestUnique = false;
        foreach (var (id, st) in states)
        {
            if (st.Deaths < bestDeaths)
            {
                bestDeaths = st.Deaths;
                best = id;
                bestUnique = true;
            }
            else if (st.Deaths == bestDeaths)
            {
                bestUnique = false;
            }
        }
        if (best == ulong.MaxValue) return default; // no states → nothing to decide
        return bestUnique
            ? new MatchOutcome(true, best, false)
            : new MatchOutcome(true, 0, true);
    }
}

/// <summary>
/// Training/practice mode: no elimination and no win condition — the match
/// runs indefinitely until the player leaves (Esc exit on the client).
/// </summary>
public sealed class NoWinMatchRule : IMatchRule
{
    public static readonly NoWinMatchRule Instance = new();

    private NoWinMatchRule() { }

    public bool IsEliminated(CharacterState state) => false;

    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states) => default;
}

/// <summary>
/// Co-op Slopball against the Launcher (ADR-0040): nobody is eliminated and play continues
/// until the leader returns the group to the lobby. An explicit nonzero miss budget can limit a run.
/// The simulation decides the end
/// (<see cref="ServerSimulation.EvaluateMatch"/>) because it owns the Slopball state.
/// </summary>
public sealed class SlopballCoopMatchRule : IMatchRule
{
    /// <summary>Zero means unlimited misses.</summary>
    public const byte DefaultMissBudget = 0;

    public byte MissBudget { get; }
    public FriendlyFireMode FriendlyFire { get; }

    public SlopballCoopMatchRule(byte missBudget = DefaultMissBudget, FriendlyFireMode friendlyFire = FriendlyFireMode.Off)
    {
        if (!MatchFormat.SlopballCoop.AdmitsFriendlyFire(friendlyFire)) throw new ArgumentOutOfRangeException(nameof(friendlyFire));
        MissBudget = missBudget;
        FriendlyFire = friendlyFire;
    }

    public bool IsEliminated(CharacterState state) => false;

    /// <summary>Fighter states alone never end a run; see <see cref="ServerSimulation.EvaluateMatch"/>.</summary>
    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states) => default;
}


public enum FriendlyFireMode : byte { Off, Shoves, On }

/// <summary>Competitive survival; the director owns scoring, benching and the Match outcome.</summary>
public sealed class MankiMayhemMatchRule : IMatchRule
{
    public FriendlyFireMode FriendlyFire { get; }
    public MankiMayhemMatchRule(FriendlyFireMode friendlyFire = FriendlyFireMode.Shoves)
    {
        if (friendlyFire > FriendlyFireMode.On) throw new ArgumentOutOfRangeException(nameof(friendlyFire));
        FriendlyFire = friendlyFire;
    }
    public bool IsEliminated(CharacterState state) => false;
    public MatchOutcome Evaluate(IReadOnlyDictionary<ulong, CharacterState> states) => default;
}
