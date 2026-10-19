using System;

namespace SlopArena.Shared
{
    /// <summary>Cooked deterministic receive-and-return profile in the unified stage artifact.</summary>
    public sealed class SlopballDefinition
    {
        public float NetZ { get; }
        public float NetHalfThickness { get; }
        public float NetTop { get; }
        public float FloorY { get; }
        public uint Seed { get; }
        public ushort ServeGapTicks { get; }
        public ushort EasyFlightTicks { get; }
        public ushort HardFlightTicks { get; }
        public ushort FlightJitterTicks { get; }
        public uint RampReturns { get; }
        public float LandingHalfWidth { get; }
        public float LandingMinZ { get; }
        public float LandingMaxZ { get; }
        public float SpawnHalfWidth { get; }
        public float SpawnHalfDepth { get; }

        public SlopballDefinition(float netZ, float netHalfThickness, float netTop, float floorY,
            uint seed, ushort serveGapTicks, ushort easyFlightTicks, ushort hardFlightTicks,
            ushort flightJitterTicks, uint rampReturns, float landingHalfWidth, float landingMinZ, float landingMaxZ,
            float spawnHalfWidth = 0f, float spawnHalfDepth = 0f)
        {
            if (!float.IsFinite(netZ) || !float.IsFinite(netHalfThickness) || netHalfThickness <= 0f || netHalfThickness > .5f
                || !float.IsFinite(netTop) || !float.IsFinite(floorY) || netTop <= floorY
                || seed == 0 || serveGapTicks == 0 || serveGapTicks > 600
                || hardFlightTicks < 30 || easyFlightTicks < hardFlightTicks || easyFlightTicks > 300
                || flightJitterTicks >= hardFlightTicks - 15 || rampReturns == 0 || rampReturns > 10000
                || !float.IsFinite(landingHalfWidth) || landingHalfWidth <= 0f
                || !float.IsFinite(landingMinZ) || !float.IsFinite(landingMaxZ)
                || landingMinZ >= landingMaxZ || landingMaxZ >= netZ - netHalfThickness
                || !float.IsFinite(spawnHalfWidth) || spawnHalfWidth < 0f
                || !float.IsFinite(spawnHalfDepth) || spawnHalfDepth < 0f)
                throw new ArgumentException("Invalid Slopball court or shot profile.");
            NetZ = netZ; NetHalfThickness = netHalfThickness; NetTop = netTop; FloorY = floorY;
            Seed = seed; ServeGapTicks = serveGapTicks; EasyFlightTicks = easyFlightTicks;
            HardFlightTicks = hardFlightTicks; FlightJitterTicks = flightJitterTicks; RampReturns = rampReturns;
            LandingHalfWidth = landingHalfWidth; LandingMinZ = landingMinZ; LandingMaxZ = landingMaxZ;
            SpawnHalfWidth = spawnHalfWidth; SpawnHalfDepth = spawnHalfDepth;
        }

        /// <summary>Returns per heat step: every fifth return in a combo heats the rally up.</summary>
        public const int HeatStep = 5;
        public const int MaxHeat = 4;

        /// <summary>Rally heat for a combo: 0 below five returns, then one step per five up to <see cref="MaxHeat"/>. A miss cools it with the combo.</summary>
        public static int HeatOf(ulong returns) => (int)Math.Min(returns / HeatStep, (ulong)MaxHeat);

        public SlopballShot CreateShot(ArcadeBallDefinition ball, ulong returns)
            => CreateShot(ball, returns, returns, Seed);

        /// <summary>
        /// One serve. The landing point, flight time and launcher origin are drawn as they always were; rally heat
        /// then mixes in special serves solved to land on that same point: a zinger (quicker and flatter, deeper), a
        /// moonball (slower and higher, under <paramref name="ceiling"/>) and a curveball that bends sideways. A
        /// special serve that cannot clear the net with margin stays a lob. <paramref name="kind"/> forces one.
        /// </summary>
        internal SlopballShot CreateShot(ArcadeBallDefinition ball, ulong shotIndex, ulong returns, uint seed,
            float ceiling = float.PositiveInfinity, SlopballServe? kind = null)
        {
            uint random = Mix(seed ^ (uint)shotIndex ^ Mix((uint)(shotIndex >> 32) + 0x9e3779b9u));
            float x = (Unit(ref random) * 2f - 1f) * LandingHalfWidth;
            float z = LandingMinZ + Unit(ref random) * (LandingMaxZ - LandingMinZ);
            int jitter = (int)(Unit(ref random) * (FlightJitterTicks * 2 + 1)) - FlightJitterTicks;
            int ramp = (int)Math.Min(returns, RampReturns);
            int ticks = EasyFlightTicks - ramp * (EasyFlightTicks - HardFlightTicks) / (int)RampReturns + jitter;
            // Keep old fixed-origin profiles and their random draws unchanged.
            var spawn = SpawnHalfWidth == 0f && SpawnHalfDepth == 0f ? ball.Spawn
                : new ArcadePoint(ball.Spawn.X + (Unit(ref random) * 2f - 1f) * SpawnHalfWidth,
                    ball.Spawn.Y, ball.Spawn.Z + (Unit(ref random) * 2f - 1f) * SpawnHalfDepth);
            float duration = ticks * Simulation.TickDt;
            SlopballShot lob;
            if (ball.LinearDrag > 0f)
            {
                // Solve the same discrete damped motion used by the ball. Randomize
                // the landing point/flight time, not unconstrained velocities that can
                // send a serve into the net or beyond the receiving area.
                float weight = 1f, travel = 0f, fallSpeed = 0f, fall = 0f;
                for (int tick = 0; tick < ticks; tick++)
                {
                    weight *= ball.VelocityRetention;
                    travel += weight * Simulation.TickDt;
                    fallSpeed = fallSpeed * ball.VelocityRetention - ball.Gravity * Simulation.TickDt;
                    fall += fallSpeed * Simulation.TickDt;
                }
                lob = new SlopballShot(shotIndex, spawn,
                    new((x - spawn.X) / travel, (FloorY + ball.Radius - spawn.Y - fall) / travel,
                        (z - spawn.Z) / travel), (ushort)ticks);
            }
            else
            {
                // Semi-implicit gravity matches ArcadeBallSimulation, including its first gravity step.
                var velocity = new ArcadePoint((x - spawn.X) / duration,
                    (FloorY + ball.Radius - spawn.Y) / duration + .5f * ball.Gravity * Simulation.TickDt * (ticks + 1),
                    (z - spawn.Z) / duration);
                lob = new SlopballShot(shotIndex, spawn, velocity, (ushort)ticks);
            }
            // The serve mix draws after every earlier value, so lobs stay exactly as before.
            float pick = Unit(ref random), side = Unit(ref random) < .5f ? -1f : 1f, bend = Unit(ref random);
            var serve = kind ?? PickServe(HeatOf(returns), pick);
            return serve == SlopballServe.Lob ? lob : Special(ball, shotIndex, spawn, x, z, ticks, serve, side, bend, ceiling) ?? lob;
        }

        /// <summary>Rally heat's serve mix: lobs only when cold, then zingers, moonballs and curveballs join in.</summary>
        private static SlopballServe PickServe(int heat, float pick)
        {
            int roll = (int)(pick * 100f);
            return heat switch
            {
                <= 0 => SlopballServe.Lob,
                1 => roll < 30 ? SlopballServe.Zinger : SlopballServe.Lob,
                2 => roll < 30 ? SlopballServe.Zinger : roll < 50 ? SlopballServe.Moonball : SlopballServe.Lob,
                3 => roll < 25 ? SlopballServe.Zinger : roll < 40 ? SlopballServe.Moonball : roll < 65 ? SlopballServe.Curveball : SlopballServe.Lob,
                _ => roll < 30 ? SlopballServe.Zinger : roll < 45 ? SlopballServe.Moonball : roll < 75 ? SlopballServe.Curveball : SlopballServe.Lob,
            };
        }

        private SlopballShot? Special(ArcadeBallDefinition ball, ulong index, ArcadePoint spawn, float x, float z, int ticks,
            SlopballServe serve, float side, float bend, float ceiling)
        {
            float curve = 0f;
            switch (serve)
            {
                case SlopballServe.Zinger:
                    // Quicker and flatter, so it lands in the deeper part of the receiving area to clear the net.
                    z = LandingMinZ + (z - LandingMinZ) * .45f;
                    ticks = Math.Max(30, (int)MathF.Round(ticks * .68f));
                    break;
                case SlopballServe.Moonball:
                    ticks = Math.Min(300, (int)MathF.Round(ticks * 1.38f));
                    break;
                case SlopballServe.Curveball:
                    // 2.5-4.5 m/s² sideways, but never bending more than about 3 m off the straight path.
                    float seconds = ticks * Simulation.TickDt;
                    curve = side * MathF.Min(2.5f + 2f * bend, 24f / (seconds * seconds));
                    break;
            }
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var velocity = Solve(ball, spawn, x, FloorY + ball.Radius, z, ticks, curve);
                var (netClear, apex) = Fly(ball, spawn, velocity, curve, ticks);
                if (netClear >= NetTop + ball.Radius + .1f && apex + ball.Radius < ceiling - .1f)
                    return new SlopballShot(index, spawn, velocity, (ushort)ticks, serve, curve);
                // Only a moonball under a low ceiling can recover: it comes down a little sooner.
                if (serve != SlopballServe.Moonball || ticks < 40) return null;
                ticks = (int)MathF.Round(ticks * .93f);
            }
            return null;
        }

        /// <summary>The launch velocity that reaches (x, y, z) after exactly <paramref name="ticks"/> steps of the ball's damped, curving flight.</summary>
        private static ArcadePoint Solve(ArcadeBallDefinition ball, ArcadePoint start, float x, float y, float z, int ticks, float curve)
        {
            float retention = ball.VelocityRetention, dt = Simulation.TickDt;
            float weight = 1f, travel = 0f, fallSpeed = 0f, fall = 0f, bendSpeed = 0f, bend = 0f;
            for (int tick = 0; tick < ticks; tick++)
            {
                weight *= retention;
                travel += weight * dt;
                fallSpeed = fallSpeed * retention - ball.Gravity * dt;
                fall += fallSpeed * dt;
                bendSpeed = bendSpeed * retention + curve * dt;
                bend += bendSpeed * dt;
            }
            return new((x - start.X - bend) / travel, (y - start.Y - fall) / travel, (z - start.Z) / travel);
        }

        /// <summary>
        /// Free flight as ArcadeBallSimulation steps it, ignoring collisions: the centre height where it first
        /// crosses the net plane (−∞ if it reaches the floor first) and its highest centre.
        /// </summary>
        private (float NetClear, float Apex) Fly(ArcadeBallDefinition ball, ArcadePoint start, ArcadePoint velocity, float curve, int maxTicks)
        {
            float retention = ball.VelocityRetention, dt = Simulation.TickDt, floor = FloorY + ball.Radius;
            float x = start.X, y = start.Y, z = start.Z, vx = velocity.X, vy = velocity.Y, vz = velocity.Z;
            float netClear = float.NegativeInfinity, apex = y;
            bool crossed = false;
            for (int tick = 0; tick < maxTicks; tick++)
            {
                vx = vx * retention + curve * dt; vz *= retention; vy = vy * retention - ball.Gravity * dt;
                float nx = x + vx * dt, ny = y + vy * dt, nz = z + vz * dt;
                if (!crossed && nz != z && (z - NetZ) * (nz - NetZ) <= 0f)
                {
                    crossed = true;
                    netClear = y + (ny - y) * ((NetZ - z) / (nz - z));
                }
                x = nx; y = ny; z = nz;
                apex = MathF.Max(apex, y);
                if (y <= floor && vy < 0f) break;
            }
            return (netClear, apex);
        }

        /// <summary>The cage's ceiling: the lowest flat collision above the net.</summary>
        internal float CeilingOf(in ArenaDefinition arena)
        {
            float ceiling = float.MaxValue;
            foreach (var triangle in arena.CollisionTriangles ?? Array.Empty<CollisionTriangle>())
                if (MathF.Abs(triangle.AY - triangle.BY) < .001f && MathF.Abs(triangle.AY - triangle.CY) < .001f
                    && triangle.AY > NetTop)
                    ceiling = MathF.Min(ceiling, triangle.AY);
            return ceiling;
        }

        public void ValidateCourt(in ArenaDefinition arena)
        {
            var ball = arena.Arcade!.Ball;
            if (ball.Gravity <= 0f || ball.Spawn.Z - SpawnHalfDepth <= NetZ + NetHalfThickness + ball.Radius
                || ball.Spawn.Y <= FloorY + ball.Radius || ball.Spawn.X - SpawnHalfWidth - ball.Radius <= arena.MinX
                || ball.Spawn.X + SpawnHalfWidth + ball.Radius >= arena.MaxX || ball.Spawn.Z + SpawnHalfDepth + ball.Radius >= arena.MaxZ
                || NetZ <= arena.MinZ || NetZ >= arena.MaxZ
                || -LandingHalfWidth - ball.Radius <= arena.MinX || LandingHalfWidth + ball.Radius >= arena.MaxX
                || LandingMinZ - ball.Radius <= arena.MinZ || LandingMaxZ >= NetZ - NetHalfThickness - ball.Radius
                || arena.SpawnPoints == null || arena.SpawnPoints.Length != 1 || arena.SpawnPoints[0].Z >= NetZ - NetHalfThickness
                || arena.CollisionTriangles == null || arena.CollisionTriangles.Length < 12)
                throw new ArgumentException("Slopball requires an enclosed court, sender spawn and valid player-side landing region.");
            float ceiling = CeilingOf(in arena);
            bool net = false;
            foreach (var triangle in arena.CollisionTriangles)
            {
                if (MathF.Abs(triangle.AZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Abs(triangle.BZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Abs(triangle.CZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Max(triangle.AY, MathF.Max(triangle.BY, triangle.CY)) >= NetTop - .001f)
                    net = true;
            }
            if (!net || ceiling == float.MaxValue) throw new ArgumentException("Slopball needs baked net and ceiling geometry.");
            // Sample the whole capped profile, including its jitter extremes and every serve rally heat can
            // mix in (curveballs must also stay off the side walls). These are technical path checks;
            // character reach/readability still need gameplay and human validation.
            var kinds = new SlopballServe?[] { null, SlopballServe.Zinger, SlopballServe.Moonball, SlopballServe.Curveball };
            for (uint i = 0; i <= RampReturns + 32; i++)
            foreach (var kind in kinds)
            {
                var shot = CreateShot(ball, i, i, Seed, ceiling, kind);
                var position = shot.Spawn; var velocity = shot.Velocity;
                bool cleared = false;
                for (int tick = 0; tick < shot.FlightTicks; tick++)
                {
                    velocity = new(velocity.X * ball.VelocityRetention + shot.Curve * Simulation.TickDt,
                        velocity.Y * ball.VelocityRetention - ball.Gravity * Simulation.TickDt,
                        velocity.Z * ball.VelocityRetention);
                    var next = new ArcadePoint(position.X + velocity.X * Simulation.TickDt,
                        position.Y + velocity.Y * Simulation.TickDt, position.Z + velocity.Z * Simulation.TickDt);
                    if (next.Y + ball.Radius >= ceiling - .1f)
                        throw new ArgumentException("Slopball serves must remain below the ceiling.");
                    if (next.X - ball.Radius <= arena.MinX || next.X + ball.Radius >= arena.MaxX)
                        throw new ArgumentException("Slopball serves must stay off the side walls.");
                    if (position.Z > NetZ && next.Z <= NetZ)
                    {
                        float fraction = (NetZ - position.Z) / (next.Z - position.Z);
                        float y = position.Y + (next.Y - position.Y) * fraction;
                        cleared = y >= NetTop + ball.Radius + .1f;
                    }
                    position = next;
                }
                if (!cleared)
                    throw new ArgumentException("Slopball serves must clear the net and remain below the ceiling.");
            }
        }

        private static uint Mix(uint value)
        { unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; return value ^ (value >> 16); } }
        private static float Unit(ref uint value)
        { value = Mix(value + 0x9e3779b9u); return (value >> 8) * (1f / 16777216f); }
    }

    /// <summary>The sender's serves: rally heat mixes the special ones in (<see cref="SlopballDefinition.HeatOf"/>).</summary>
    public enum SlopballServe : byte { Lob, Zinger, Moonball, Curveball }

    public readonly struct SlopballShot
    {
        public readonly ulong Index;
        public readonly ArcadePoint Spawn, Velocity;
        public readonly ushort FlightTicks;
        public readonly SlopballServe Kind;
        /// <summary>A curveball's sideways (X) acceleration until its first contact, m/s²; zero for other serves.</summary>
        public readonly float Curve;
        public SlopballShot(ulong index, ArcadePoint spawn, ArcadePoint velocity, ushort flightTicks,
            SlopballServe kind = SlopballServe.Lob, float curve = 0f)
        { Index = index; Spawn = spawn; Velocity = velocity; FlightTicks = flightTicks; Kind = kind; Curve = curve; }
    }
}
