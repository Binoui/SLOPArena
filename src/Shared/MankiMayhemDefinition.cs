using System;

namespace SlopArena.Shared;

/// <summary>Immutable placement bounds and launch origin, compiled from stage markers.</summary>
public sealed class MankiMayhemDefinition
{
    public ArcadePoint HostOrigin { get; }
    public float MinX { get; }
    public float MaxX { get; }
    public float MinZ { get; }
    public float MaxZ { get; }
    public float DeckTop { get; }

    public MankiMayhemDefinition(ArcadePoint hostOrigin, float minX, float maxX, float minZ, float maxZ, float deckTop)
    {
        if (!hostOrigin.IsFinite || !float.IsFinite(minX) || !float.IsFinite(maxX)
            || !float.IsFinite(minZ) || !float.IsFinite(maxZ) || !float.IsFinite(deckTop)
            || maxX - minX <= 2f * MankiMayhemTuning.PlacementMargin
            || maxZ - minZ <= 2f * MankiMayhemTuning.PlacementMargin || hostOrigin.Y <= deckTop)
            throw new ArgumentException("Mayhem needs finite play bounds, a deck top and an elevated host origin.");
        HostOrigin = hostOrigin; MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; DeckTop = deckTop;
    }

    internal void ValidateArena(in ArenaDefinition arena)
    {
        if (arena.SpawnPoints == null || arena.SpawnPoints.Length != 4
            || MinX < arena.MinX || MaxX > arena.MaxX || MinZ < arena.MinZ || MaxZ > arena.MaxZ)
            throw new ArgumentException("Mayhem needs four starts and play bounds inside the deck.");
        foreach (var start in arena.SpawnPoints)
            if (start.X < MinX || start.X > MaxX || start.Z < MinZ || start.Z > MaxZ)
                throw new ArgumentException("Mayhem starts must be inside its play area.");
    }
}
