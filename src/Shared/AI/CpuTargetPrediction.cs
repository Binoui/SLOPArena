using System;

namespace SlopArena.Shared.AI;

internal readonly struct CpuMoveTiming
{
    public readonly int StartupTicks, TravelTicks;
    public readonly float TravelSpeed, TravelOffset;

    public CpuMoveTiming(int startupTicks, int travelTicks = 0, float travelSpeed = 0f, float travelOffset = 0f)
    {
        StartupTicks = startupTicks;
        TravelTicks = travelTicks;
        TravelSpeed = MathF.Max(0f, travelSpeed);
        TravelOffset = MathF.Max(0f, travelOffset);
    }
}

internal readonly struct CpuTargetPoint
{
    public readonly float X, Y, Z;
    public readonly int ContactTicks;

    public CpuTargetPoint(float x, float y, float z, int contactTicks)
    {
        X = x;
        Y = y;
        Z = z;
        ContactTicks = contactTicks;
    }

    public float DistanceFrom(in CharacterState self)
    {
        float dx = X - self.PX, dz = Z - self.PZ;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>Forecasts only delayed simulation observations, bounded by known motion and stage support.</summary>
internal static class CpuTargetPrediction
{
    internal const int MaxHorizonTicks = 36;
    internal const float MaxLeadDistance = 3f;

    public static CpuTargetPoint AtActivation(in CharacterState self, in CpuObservation observed,
        in ArenaDefinition arena, in CpuMoveTiming move, int ownWaitTicks = 0,
        int horizonCap = MaxHorizonTicks, float leadCap = MaxLeadDistance)
    {
        int contactTicks = ownWaitTicks + move.StartupTicks;
        if (move.TravelSpeed <= 0f)
            return Project(observed, arena, observed.AgeTicks + contactTicks + move.TravelTicks,
                contactTicks + move.TravelTicks, horizonCap, leadCap);
        var startup = Project(observed, arena, observed.AgeTicks + contactTicks, contactTicks, horizonCap, leadCap);
        if (move.TravelSpeed > 0f)
        {
            float distance = MathF.Max(0f, startup.DistanceFrom(self) - move.TravelOffset);
            int flight = (int)MathF.Ceiling(distance / (move.TravelSpeed * Simulation.TickDt));
            contactTicks += Math.Min(flight, move.TravelTicks);
        }
        return Project(observed, arena, observed.AgeTicks + contactTicks, contactTicks, horizonCap, leadCap);
    }

    private static CpuTargetPoint Project(in CpuObservation observed, in ArenaDefinition arena,
        int totalTicks, int contactTicks, int horizonCap, float leadCap)
    {
        int motionTicks = Math.Max(0, Math.Min(horizonCap, totalTicks) - observed.HitstopTicks);
        if (observed.State == ActionState.Hitstun || observed.HitstunTicks > 0)
            motionTicks = Math.Min(motionTicks, observed.HitstunTicks);
        else if (observed.InPostHitstunFlight)
            motionTicks = 0;
        float seconds = motionTicks * Simulation.TickDt;
        float dx = observed.VX * seconds, dz = observed.VZ * seconds;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance > leadCap)
        {
            dx *= leadCap / distance;
            dz *= leadCap / distance;
            distance = leadCap;
        }
        float dy = observed.IsGrounded ? 0f : Math.Clamp(observed.VY * seconds, -2f, 2f);
        bool bounded = arena.MaxX > arena.MinX && arena.MaxZ > arena.MinZ;
        float floor = arena.Heightmap.Data != null ? arena.Heightmap.Sample(observed.PX, observed.PZ) : float.MinValue;
        float x = observed.PX, y = observed.PY, z = observed.PZ;
        if (observed.IsGrounded && arena.Heightmap.Data != null && floor == float.MinValue)
            return new CpuTargetPoint(x, y, z, contactTicks);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / 0.25f));
        for (int step = 1; step <= steps; step++)
        {
            float fraction = step / (float)steps;
            float nextX = observed.PX + dx * fraction, nextZ = observed.PZ + dz * fraction;
            if (bounded && (nextX < arena.MinX || nextX > arena.MaxX || nextZ < arena.MinZ || nextZ > arena.MaxZ))
                break;
            float nextY = observed.PY + dy * fraction;
            if (observed.IsGrounded && floor > float.MinValue)
            {
                float nextFloor = arena.Heightmap.Sample(nextX, nextZ);
                if (nextFloor == float.MinValue || MathF.Abs(nextFloor - floor) > Simulation.PlatformSnapTolerance)
                    break;
                nextY = observed.PY + nextFloor - floor;
            }
            x = nextX;
            y = nextY;
            z = nextZ;
        }
        return new CpuTargetPoint(x, y, z, contactTicks);
    }
}
