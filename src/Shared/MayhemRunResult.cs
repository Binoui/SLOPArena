using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;

namespace SlopArena.Shared;

/// <summary>One contestant's finished show: a pass bit per episode. Ratings are always recomputed from it.</summary>
public readonly record struct MayhemFinishResult(ulong EntityId, uint PassMask, bool Withdrawn)
{
    public ushort Ratings => MayhemShowPlan.Ratings(PassMask);
}

public sealed record MayhemRunResult(IReadOnlyList<MayhemFinishResult> Players)
{
    /// <summary>One plus the number of eligible contestants with more ratings, so ties share a place; withdrawn is zero.</summary>
    public byte PlaceFor(ulong id)
    {
        var player = Players.Single(p => p.EntityId == id);
        return player.Withdrawn ? (byte)0 : (byte)(1 + Players.Count(p => !p.Withdrawn && p.Ratings > player.Ratings));
    }

    public static MayhemRunResult From(ServerSimulation simulation)
    {
        var show = simulation.MayhemShow;
        if (show?.Plan is not { } plan || simulation.CurrentTick < plan.EndTick)
            throw new InvalidOperationException("Mayhem has not ended.");
        var view = simulation.MayhemView!;
        return new(Array.AsReadOnly(view.Contestants
            .Select(c => new MayhemFinishResult(c.EntityId, c.PassMask, c.Withdrawn)).ToArray()));
    }
}
