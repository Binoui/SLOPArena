using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public readonly record struct MayhemFinishResult(ulong EntityId, ushort Score, ushort RoundsSurvived,
    ushort Bonuses, bool Withdrawn);

public sealed record MayhemRunResult(IReadOnlyList<MayhemFinishResult> Players)
{
    public byte PlaceFor(ulong id)
    {
        var player = Players.Single(p => p.EntityId == id);
        return player.Withdrawn ? (byte)0 : (byte)(1 + Players.Count(p => !p.Withdrawn && p.Score > player.Score));
    }

    public static MayhemRunResult From(ServerSimulation simulation)
    {
        if (simulation.MankiMayhem is not { Ended: true }) throw new InvalidOperationException("Mayhem has not ended.");
        return new(Array.AsReadOnly(simulation.MayhemPlayers.Select(p =>
            new MayhemFinishResult(p.EntityId, p.Score, p.RoundsSurvived, p.Bonuses, p.Withdrawn)).ToArray()));
    }
}
