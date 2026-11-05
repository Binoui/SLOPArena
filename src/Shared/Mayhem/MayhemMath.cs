using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>
/// Pure seeded sampling, schedules and deck geometry shared by channels, CPU intent and cue projection.
/// No <see cref="Random"/>, wall clock or dictionary order enters a channel.
/// </summary>
/// <summary>A disk a CPU keeps out of: a blast footprint grown by the CPU's capsule and clearance.</summary>
internal readonly record struct MayhemDanger(float X, float Z, float Radius);

internal static class MayhemMath
{
    /// <summary>Seed domains: deck shuffles, boss picks and channel setups never share a stream.</summary>
    internal const uint DeckStream = 0x100, BossStream = 0x200, ChannelStream = 0x1000;

    internal static uint Sample(uint seed, uint stream, uint ordinal)
    {
        uint x = seed ^ unchecked((stream + 1) * 0x9e3779b9u) ^ unchecked((ordinal + 1) * 0x85ebca6bu);
        x ^= x >> 16; x = unchecked(x * 0x7feb352du); x ^= x >> 15;
        x = unchecked(x * 0x846ca68bu); return x ^ (x >> 16);
    }

    /// <summary>A channel's own seed, separated by episode and lane.</summary>
    internal static uint ChannelSeed(uint showSeed, int episode, byte lane)
        => Sample(showSeed, ChannelStream + (uint)episode * 4 + lane, 0) | 1;

    /// <summary>A uniform value in [0, 1) from the low 24 bits of a sample.</summary>
    internal static float Unit(uint sample) => (sample & 0xFFFFFF) / 16777216f;

    internal static float Signed(uint sample) => Unit(sample) * 2f - 1f;

    internal static void Shuffle<T>(T[] items, uint seed, uint stream)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = (int)(Sample(seed, stream, (uint)i) % (uint)(i + 1));
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    internal static float CenterX(MankiMayhemDefinition stage) => (stage.MinX + stage.MaxX) * .5f;
    internal static float CenterZ(MankiMayhemDefinition stage) => (stage.MinZ + stage.MaxZ) * .5f;

    internal static ArcadePoint Clamp(MankiMayhemDefinition stage, float x, float z, float margin)
        => new(Math.Clamp(x, stage.MinX + margin, stage.MaxX - margin), stage.DeckTop,
            Math.Clamp(z, stage.MinZ + margin, stage.MaxZ - margin));

    internal static bool OnDeck(MankiMayhemDefinition stage, float x, float z, float margin = 0f)
        => x >= stage.MinX + margin && x <= stage.MaxX - margin && z >= stage.MinZ + margin && z <= stage.MaxZ - margin;

    internal static float DistanceSquared(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return dx * dx + dz * dz;
    }

    /// <summary>The farthest deck corner from a point: how far a ring must travel to sweep the deck.</summary>
    internal static float FarthestCorner(MankiMayhemDefinition stage, float x, float z)
    {
        float dx = MathF.Max(MathF.Abs(stage.MinX - x), MathF.Abs(stage.MaxX - x));
        float dz = MathF.Max(MathF.Abs(stage.MinZ - z), MathF.Abs(stage.MaxZ - z));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Ticks for a ring of <paramref name="speed"/> m/s to reach <paramref name="distance"/>.</summary>
    internal static ushort RingTicks(float distance, float speed)
        => (ushort)Math.Min(ushort.MaxValue, (int)MathF.Ceiling(distance / speed * 60f) + 1);

    /// <summary>Yaw (radians, atan2(dx, dz)) from one deck point toward another.</summary>
    internal static float YawTo(float fromX, float fromZ, float toX, float toZ) => MathF.Atan2(toX - fromX, toZ - fromZ);

    /// <summary>
    /// The candidate point around (<paramref name="x"/>, <paramref name="z"/>) whose nearest danger edge is farthest,
    /// in a fixed angular order so ties resolve the same way everywhere.
    /// </summary>
    internal static (float X, float Z, float Clearance) SafestPoint(MankiMayhemDefinition stage, float x, float z,
        float step, float margin, IReadOnlyList<MayhemDanger> dangers)
    {
        float bestX = x, bestZ = z, best = Clearance(x, z, dangers);
        for (int ring = 1; ring <= 3; ring++)
            for (int i = 0; i < 16; i++)
            {
                float yaw = i * (MathF.PI / 8f);
                float px = x + MathF.Sin(yaw) * step * ring, pz = z + MathF.Cos(yaw) * step * ring;
                if (!OnDeck(stage, px, pz, margin)) continue;
                float score = Clearance(px, pz, dangers) - ring * .05f;
                if (score <= best + .01f) continue;
                best = score; bestX = px; bestZ = pz;
            }
        return (bestX, bestZ, best);
    }

    /// <summary>Distance from a point to the nearest danger's edge; negative inside one.</summary>
    internal static float Clearance(float x, float z, IReadOnlyList<MayhemDanger> dangers)
    {
        float nearest = float.MaxValue;
        foreach (var d in dangers) nearest = MathF.Min(nearest, MathF.Sqrt(DistanceSquared(x, z, d.X, d.Z)) - d.Radius);
        return nearest;
    }

    internal static bool Exposed(float x, float z, IReadOnlyList<MayhemDanger> dangers) => Clearance(x, z, dangers) < 0f;
}
