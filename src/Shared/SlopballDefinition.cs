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

        public SlopballShot CreateShot(ArcadeBallDefinition ball, ulong returns)
            => CreateShot(ball, returns, returns, Seed);

        internal SlopballShot CreateShot(ArcadeBallDefinition ball, ulong shotIndex, ulong returns, uint seed)
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
                return new SlopballShot(shotIndex, spawn,
                    new((x - spawn.X) / travel, (FloorY + ball.Radius - spawn.Y - fall) / travel,
                        (z - spawn.Z) / travel), (ushort)ticks);
            }
            // Semi-implicit gravity matches ArcadeBallSimulation, including its first gravity step.
            var velocity = new ArcadePoint((x - spawn.X) / duration,
                (FloorY + ball.Radius - spawn.Y) / duration + .5f * ball.Gravity * Simulation.TickDt * (ticks + 1),
                (z - spawn.Z) / duration);
            return new SlopballShot(shotIndex, spawn, velocity, (ushort)ticks);
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
            float ceiling = float.MaxValue;
            bool net = false;
            foreach (var triangle in arena.CollisionTriangles)
            {
                if (MathF.Abs(triangle.AY - triangle.BY) < .001f && MathF.Abs(triangle.AY - triangle.CY) < .001f
                    && triangle.AY > NetTop)
                    ceiling = MathF.Min(ceiling, triangle.AY);
                if (MathF.Abs(triangle.AZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Abs(triangle.BZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Abs(triangle.CZ - NetZ) <= NetHalfThickness + .001f
                    && MathF.Max(triangle.AY, MathF.Max(triangle.BY, triangle.CY)) >= NetTop - .001f)
                    net = true;
            }
            if (!net || ceiling == float.MaxValue) throw new ArgumentException("Slopball needs baked net and ceiling geometry.");
            // Sample the whole capped profile, including its jitter extremes. These are technical
            // path checks; character reach/readability still need gameplay and human validation.
            for (uint i = 0; i <= RampReturns + 32; i++)
            {
                var shot = CreateShot(ball, i);
                var position = shot.Spawn; var velocity = shot.Velocity;
                bool cleared = false;
                for (int tick = 0; tick < shot.FlightTicks; tick++)
                {
                    velocity = new(velocity.X * ball.VelocityRetention,
                        velocity.Y * ball.VelocityRetention - ball.Gravity * Simulation.TickDt,
                        velocity.Z * ball.VelocityRetention);
                    var next = new ArcadePoint(position.X + velocity.X * Simulation.TickDt,
                        position.Y + velocity.Y * Simulation.TickDt, position.Z + velocity.Z * Simulation.TickDt);
                    if (next.Y + ball.Radius >= ceiling - .1f)
                        throw new ArgumentException("Slopball serves must remain below the ceiling.");
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

    public readonly struct SlopballShot
    {
        public readonly ulong Index;
        public readonly ArcadePoint Spawn, Velocity;
        public readonly ushort FlightTicks;
        public SlopballShot(ulong index, ArcadePoint spawn, ArcadePoint velocity, ushort flightTicks)
        { Index = index; Spawn = spawn; Velocity = velocity; FlightTicks = flightTicks; }
    }
}
