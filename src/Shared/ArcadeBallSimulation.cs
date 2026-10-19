using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>One continuous portion of a moving sphere's path within a 60 Hz tick.</summary>
    public readonly struct SphereMotionSegment
    {
        public readonly float StartTime, EndTime;
        public readonly ArcadePoint Start, End;
        public SphereMotionSegment(float startTime, float endTime, ArcadePoint start, ArcadePoint end)
        { StartTime = startTime; EndTime = endTime; Start = start; End = end; }
        public ArcadePoint Sample(float time)
        {
            float t = EndTime > StartTime ? Math.Clamp((time - StartTime) / (EndTime - StartTime), 0f, 1f) : 0f;
            return new ArcadePoint(Start.X + (End.X - Start.X) * t,
                Start.Y + (End.Y - Start.Y) * t, Start.Z + (End.Z - Start.Z) * t);
        }
    }

    public readonly struct ArcadeBallState
    {
        public readonly ulong EntityId;
        public readonly ArcadePoint Position, Velocity;
        public readonly float Radius, DamagePercent;
        public readonly ushort HitstopTicks;
        public readonly bool Active;
        /// <summary>Sideways (X) acceleration of a curveball serve until its first contact, m/s²; zero otherwise.</summary>
        public readonly float Curve;
        public ArcadeBallState(ulong entityId, ArcadePoint position, ArcadePoint velocity,
            float radius, float damagePercent, ushort hitstopTicks, bool active = true, float curve = 0f)
        {
            EntityId = entityId; Position = position; Velocity = velocity;
            Radius = radius; DamagePercent = damagePercent; HitstopTicks = hitstopTicks; Active = active; Curve = curve;
        }
    }

    /// <summary>
    /// Authoritative ball physics. Slopball ignores passive fighter bodies so attacks can reach
    /// the ball; the playground keeps body rebounds. Slopball rules retire floor contacts.
    /// </summary>
    internal sealed class ArcadeBallSimulation
    {
        public const ulong FirstEntityId = 1UL << 32;
        public const byte TargetInputId = byte.MaxValue;
        private const float Skin = 0.0002f;
        private readonly ArcadeBallDefinition _definition;
        private readonly int[] _triangles;
        private readonly List<SphereMotionSegment> _path = new();
        private readonly List<ArcadeBallContact> _contacts = new();
        private ulong _entityId = FirstEntityId;
        private float _x, _y, _z, _vx, _vy, _vz, _percent, _curve;
        private ushort _hitstop;
        private bool _active;
        public ArcadeBallState State => new(_entityId, new(_x, _y, _z), new(_vx, _vy, _vz),
            _definition.Radius, _percent, _hitstop, _active, _curve);
        public IReadOnlyList<SphereMotionSegment> Path { get; }
        public IReadOnlyList<ArcadeBallContact> Contacts => _contacts;

        public ArcadeBallSimulation(ArcadeBallDefinition definition, in ArenaDefinition arena)
        {
            _definition = definition;
            _triangles = new int[arena.CollisionTriangles?.Length ?? 0];
            Path = _path.AsReadOnly();
            Reset();
        }

        public void Reset()
            => Reset(_definition.Spawn, _definition.InitialVelocity);

        /// <summary>A fresh ball; <paramref name="curve"/> bends it sideways (X, m/s²) until its first contact.</summary>
        public void Reset(ArcadePoint spawn, ArcadePoint velocity, float curve = 0f)
        {
            if (_entityId == ulong.MaxValue) throw new InvalidOperationException("Ball identity space exhausted.");
            if (!float.IsFinite(curve)) throw new ArgumentOutOfRangeException(nameof(curve));
            _entityId++;
            _x = spawn.X; _y = spawn.Y; _z = spawn.Z;
            _vx = velocity.X; _vy = velocity.Y; _vz = velocity.Z;
            _percent = 0f; _hitstop = 0; _active = true; _curve = curve;
            _path.Clear(); _contacts.Clear();
        }

        public void Retire(float time = 1f)
            => RetireAt(PositionAt(time));

        public void RetireAt(ArcadePoint point)
        {
            _x = point.X; _y = point.Y; _z = point.Z;
            _active = false;
        }

        public void ClipCollisionPath(float time)
        {
            for (int i = _path.Count - 1; i >= 0; i--)
            {
                var segment = _path[i];
                if (segment.StartTime >= time) { _path.RemoveAt(i); continue; }
                if (segment.EndTime > time) _path[i] = new(segment.StartTime, time, segment.Start, segment.Sample(time));
            }
        }

        public SpellResolver.EntityData CollisionTarget => new()
        {
            Id = _entityId, PosX = _x, PosY = _y, PosZ = _z,
            EndX = _x, EndY = _y, EndZ = _z,
            Radius = _definition.Radius, Shape = HitboxShape.Sphere, Active = _active,
            MotionPath = Path,
        };

        public ArcadePoint PositionAt(float time)
        {
            foreach (var segment in _path)
                if (time >= segment.StartTime && time <= segment.EndTime) return segment.Sample(time);
            return new(_x, _y, _z);
        }

        public void Step(in ArenaDefinition arena, IReadOnlyDictionary<ulong, CharacterState> fighters,
            IReadOnlyDictionary<ulong, CharacterDefinition> definitions)
        {
            _path.Clear(); _contacts.Clear();
            if (!_active) return;
            if (_hitstop > 0)
            {
                _hitstop--;
                _path.Add(new(0f, 1f, new(_x, _y, _z), new(_x, _y, _z)));
                return;
            }
            float retention = _definition.VelocityRetention;
            _vx *= retention; _vz *= retention;
            _vy = _vy * retention - _definition.Gravity * Simulation.TickDt;
            // A curveball's sideways pull, after drag like gravity; SlopballDefinition solves serves the same way.
            if (_curve != 0f) _vx += _curve * Simulation.TickDt;
            float time = 0f;
            for (int bounce = 0; bounce < 16 && time < 1f; bounce++)
            {
                var start = new ArcadePoint(_x, _y, _z);
                float dt = (1f - time) * Simulation.TickDt;
                var end = new ArcadePoint(_x + _vx * dt, _y + _vy * dt, _z + _vz * dt);
                int count = ArenaCollision.GetCandidateTrianglesForSweep(start.X, start.Y, start.Z,
                    end.X, end.Y, end.Z, _definition.Radius, _definition.Radius * 2f, in arena, _triangles);
                bool contact = ArenaCollision.SweepCapsule(start.X, start.Y, start.Z, end.X, end.Y, end.Z,
                    _definition.Radius, _definition.Radius * 2f, in arena, _triangles, count, out var solid);
                float fraction = contact ? solid.Time : 1f;
                float nx = solid.NormalX, ny = solid.NormalY, nz = solid.NormalZ;
                float penetration = contact ? solid.Penetration : 0f;
                bool body = false;
                ulong bodyId = ulong.MaxValue;
                // Passive bodies must not push a Slopball serve away before an attack connects.
                // Keep body rebounds in the independent ball playground.
                if (arena.Arcade?.Slopball == null)
                {
                    foreach (var pair in fighters)
                    {
                        if (!definitions.TryGetValue(pair.Key, out var def)) continue;
                        if (!SweepBody(start, end, pair.Value, def, _definition.Radius,
                                out float t, out float bx, out float by, out float bz, out float depth)) continue;
                        if (t < fraction || t == fraction && body && pair.Key < bodyId || !contact)
                        {
                            contact = body = true; bodyId = pair.Key; fraction = t;
                            nx = bx; ny = by; nz = bz; penetration = depth;
                        }
                    }
                }
                float nextTime = time + (1f - time) * fraction;
                _x = start.X + (end.X - start.X) * fraction;
                _y = start.Y + (end.Y - start.Y) * fraction;
                _z = start.Z + (end.Z - start.Z) * fraction;
                _path.Add(new(time, nextTime, start, new(_x, _y, _z)));
                if (!contact) { time = 1f; break; }
                _contacts.Add(new(nextTime, new(_x, _y, _z), body, nx, ny, nz));
                _curve = 0f;
                _x += nx * (Skin + penetration); _y += ny * (Skin + penetration); _z += nz * (Skin + penetration);
                float incoming = _vx * nx + _vy * ny + _vz * nz;
                if (incoming < 0f)
                {
                    // Bodies redirect existing velocity: no fighter momentum, damage,
                    // shield bonus or dodge-dependent shape is introduced.
                    float rebound = body ? 1f : _definition.Restitution;
                    _vx -= (1f + rebound) * incoming * nx;
                    _vy -= (1f + rebound) * incoming * ny;
                    _vz -= (1f + rebound) * incoming * nz;
                }
                time = nextTime;
            }
            if (time < 1f) _path.Add(new(time, 1f, new(_x, _y, _z), new(_x, _y, _z)));
        }

        public float Hit(in SpellResolver.HitResult hit, float damage, float force,
            float dirX, float dirZ, ushort freeze)
        {
            var position = PositionAt(hit.ContactTime);
            _x = position.X; _y = position.Y; _z = position.Z;
            _percent = Math.Clamp(_percent + MathF.Max(0f, damage), 0f, 999f);
            float speed = MathF.Max(0f, force) * _definition.LaunchScale;
            float angle = hit.KnockbackAngle * (MathF.PI / 180f);
            float horizontal = speed * MathF.Cos(angle);
            _vx = dirX * horizontal; _vy = speed * MathF.Sin(angle); _vz = dirZ * horizontal;
            _hitstop = freeze; _curve = 0f;
            return speed;
        }

        /// <summary>The ball's state for a World Snapshot (ADR-0039/0040); the path and contacts are per-tick scratch.</summary>
        internal readonly struct Snapshot
        {
            public readonly ulong EntityId;
            public readonly float X, Y, Z, VX, VY, VZ, Percent, Curve;
            public readonly ushort Hitstop;
            public readonly bool Active;

            public Snapshot(ulong entityId, float x, float y, float z, float vx, float vy, float vz,
                float percent, float curve, ushort hitstop, bool active)
            {
                EntityId = entityId; X = x; Y = y; Z = z; VX = vx; VY = vy; VZ = vz;
                Percent = percent; Curve = curve; Hitstop = hitstop; Active = active;
            }

            public void Write(SnapshotWriter w)
            {
                w.WriteULong(EntityId);
                w.WriteFloat(X); w.WriteFloat(Y); w.WriteFloat(Z);
                w.WriteFloat(VX); w.WriteFloat(VY); w.WriteFloat(VZ);
                w.WriteFloat(Percent); w.WriteFloat(Curve); w.WriteUShort(Hitstop); w.WriteBool(Active);
            }

            public static Snapshot Read(SnapshotReader r)
            {
                ulong id = r.ReadULong();
                if (id < FirstEntityId) throw SnapshotReader.Malformed("Arcade ball identity");
                return new Snapshot(id, r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(),
                    r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadUShort(), r.ReadBool());
            }
        }

        internal Snapshot Capture() => new(_entityId, _x, _y, _z, _vx, _vy, _vz, _percent, _curve, _hitstop, _active);

        internal void Apply(in Snapshot snapshot)
        {
            _entityId = snapshot.EntityId;
            _x = snapshot.X; _y = snapshot.Y; _z = snapshot.Z;
            _vx = snapshot.VX; _vy = snapshot.VY; _vz = snapshot.VZ;
            _percent = snapshot.Percent; _curve = snapshot.Curve; _hitstop = snapshot.Hitstop; _active = snapshot.Active;
            _path.Clear(); _contacts.Clear();
        }

        private static bool SweepBody(ArcadePoint start, ArcadePoint end, in CharacterState body,
            CharacterDefinition def, float radius, out float time,
            out float nx, out float ny, out float nz, out float penetration)
        {
            float dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            float travel = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            time = 0f; nx = ny = nz = penetration = 0f;
            for (int i = 0; i < 64 && time <= 1f; i++)
            {
                float gap = BodyDistance(start.X + dx * time, start.Y + dy * time, start.Z + dz * time,
                    body, def, out nx, out ny, out nz) - radius;
                float closing = -(dx * nx + dy * ny + dz * nz);
                if (gap < -0.0001f || gap <= 0.0001f && closing > 0f)
                { penetration = MathF.Max(0f, -gap); return true; }
                if (travel < 0.000001f || time == 1f) return false;
                float next = time + MathF.Max(gap, 0.0001f) / travel;
                if (next > 1f) return false;
                time = next;
            }
            return false;
        }

        // Normal pushbox dimensions, independent of animation, guard and attack immunity.
        private static float BodyDistance(float x, float y, float z, in CharacterState body,
            CharacterDefinition def, out float nx, out float ny, out float nz)
        {
            float dx = x - body.PX, dz = z - body.PZ;
            float radial = MathF.Sqrt(dx * dx + dz * dz);
            float sideGap = radial - def.CapsuleRadius;
            float vertical = y - body.PY;
            float topGap = MathF.Abs(vertical) - def.CapsuleHeight * .5f;
            float ux = radial > 0.000001f ? dx / radial : 1f;
            float uz = radial > 0.000001f ? dz / radial : 0f;
            if (sideGap <= 0f && topGap <= 0f)
            {
                bool side = sideGap >= topGap;
                nx = side ? ux : 0f; ny = side ? 0f : (vertical >= 0f ? 1f : -1f); nz = side ? uz : 0f;
                return MathF.Max(sideGap, topGap);
            }
            float h = MathF.Max(0f, sideGap), v = MathF.Max(0f, topGap);
            float distance = MathF.Sqrt(h * h + v * v);
            float inverse = distance > 0.000001f ? 1f / distance : 0f;
            nx = ux * h * inverse; ny = (vertical >= 0f ? v : -v) * inverse; nz = uz * h * inverse;
            return distance;
        }
    }

    /// <summary>
    /// One bounce of the ball within a tick: off baked collision (floor, walls, net, ceiling) or a fighter's body.
    /// The normal points away from the surface. Presentation reads these for sounds and trick callouts.
    /// </summary>
    public readonly struct ArcadeBallContact
    {
        public readonly float Time, NormalX, NormalY, NormalZ;
        public readonly ArcadePoint Position;
        public readonly bool Body;
        public ArcadeBallContact(float time, ArcadePoint position, bool body, float normalX, float normalY, float normalZ)
        { Time = time; Position = position; Body = body; NormalX = normalX; NormalY = normalY; NormalZ = normalZ; }
    }
}
