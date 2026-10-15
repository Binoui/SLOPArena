using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SlopArena.Shared
{
    public enum ArenaPurpose : byte { PvP, Arcade }

    public readonly struct ArcadePoint
    {
        public readonly float X, Y, Z;
        public ArcadePoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        internal bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    }

    /// <summary>Cooked ball tuning. Shared uses these values, never Unity physics.</summary>
    public sealed class ArcadeBallDefinition
    {
        public ArcadePoint Spawn { get; }
        public ArcadePoint InitialVelocity { get; }
        public float Radius { get; }
        public float Gravity { get; }
        public float Restitution { get; }
        public float LaunchScale { get; }
        public float LinearDrag { get; }
        internal float VelocityRetention => 1f / (1f + LinearDrag * Simulation.TickDt);

        public ArcadeBallDefinition(ArcadePoint spawn, ArcadePoint initialVelocity,
            float radius, float gravity, float restitution, float launchScale, float linearDrag = 0f)
        {
            if (!spawn.IsFinite || !initialVelocity.IsFinite
                || !float.IsFinite(radius) || radius <= 0f || radius > 2f
                || !float.IsFinite(gravity) || gravity < 0f || gravity > 200f
                || !float.IsFinite(restitution) || restitution < 0f || restitution > 1f
                || !float.IsFinite(launchScale) || launchScale <= 0f || launchScale > 10f
                || !float.IsFinite(linearDrag) || linearDrag < 0f || linearDrag > 5f)
                throw new ArgumentException("Invalid Arcade ball geometry or tuning.");
            Spawn = spawn; InitialVelocity = initialVelocity;
            Radius = radius; Gravity = gravity; Restitution = restitution; LaunchScale = launchScale; LinearDrag = linearDrag;
        }
    }

    /// <summary>
    /// Immutable focused Arcade metadata embedded in the baked arena. Phase 1 owns
    /// the ball playground; challenge scoring and Course markers are later slices.
    /// </summary>
    public sealed class ArcadeStageDefinition
    {
        public string ProfileId { get; }
        public uint GameplayRevision { get; }
        public IReadOnlyList<string> AllowedPackageIds { get; }
        public ArcadeBallDefinition Ball { get; }
        public SlopballDefinition? Slopball { get; }

        public ArcadeStageDefinition(string profileId, uint gameplayRevision,
            IReadOnlyList<string> allowedPackageIds, ArcadeBallDefinition ball, SlopballDefinition? slopball = null)
        {
            if (string.IsNullOrWhiteSpace(profileId) || Encoding.UTF8.GetByteCount(profileId) > 256
                || gameplayRevision == 0 || ball == null
                || allowedPackageIds == null || allowedPackageIds.Count == 0 || allowedPackageIds.Count > 16)
                throw new ArgumentException("Arcade needs a profile, gameplay revision, ball and allowed packages.");
            var packages = new string[allowedPackageIds.Count];
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < packages.Length; i++)
            {
                string id = allowedPackageIds[i];
                if (string.IsNullOrWhiteSpace(id) || Encoding.UTF8.GetByteCount(id) > 256 || !unique.Add(id))
                    throw new ArgumentException("Arcade package identities must be nonempty and unique.");
                packages[i] = id;
            }
            ProfileId = profileId; GameplayRevision = gameplayRevision;
            AllowedPackageIds = Array.AsReadOnly(packages); Ball = ball; Slopball = slopball;
        }

        public bool AllowsPackage(string packageId)
        {
            foreach (string id in AllowedPackageIds)
                if (string.Equals(id, packageId, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>Bounded typed section inside arena v6; not a separate runtime artifact.</summary>
    internal static class ArcadeStageBinaryFormat
    {
        private const ushort SectionVersion = 4;

        public static byte[] Serialize(ArcadeStageDefinition stage)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            ushort version = stage.Slopball is { } varied && (varied.SpawnHalfWidth > 0f || varied.SpawnHalfDepth > 0f)
                ? SectionVersion : stage.Ball.LinearDrag > 0f ? (ushort)3 : stage.Slopball == null ? (ushort)1 : (ushort)2;
            writer.Write(version);
            WriteString(writer, stage.ProfileId); writer.Write(stage.GameplayRevision);
            writer.Write((byte)stage.AllowedPackageIds.Count);
            foreach (string package in stage.AllowedPackageIds) WriteString(writer, package);
            WritePoint(writer, stage.Ball.Spawn); WritePoint(writer, stage.Ball.InitialVelocity);
            writer.Write(stage.Ball.Radius); writer.Write(stage.Ball.Gravity);
            writer.Write(stage.Ball.Restitution); writer.Write(stage.Ball.LaunchScale);
            if (version >= 3) { writer.Write(stage.Ball.LinearDrag); writer.Write(stage.Slopball != null); }
            if (stage.Slopball is SlopballDefinition rules)
            {
                writer.Write(rules.NetZ); writer.Write(rules.NetHalfThickness); writer.Write(rules.NetTop); writer.Write(rules.FloorY);
                writer.Write(rules.Seed); writer.Write(rules.ServeGapTicks); writer.Write(rules.EasyFlightTicks);
                writer.Write(rules.HardFlightTicks); writer.Write(rules.FlightJitterTicks); writer.Write(rules.RampReturns);
                writer.Write(rules.LandingHalfWidth); writer.Write(rules.LandingMinZ); writer.Write(rules.LandingMaxZ);
                if (version >= 4) { writer.Write(rules.SpawnHalfWidth); writer.Write(rules.SpawnHalfDepth); }
            }
            return stream.ToArray();
        }

        public static ArcadeStageDefinition? Deserialize(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                ushort version = reader.ReadUInt16();
                if (version != 1 && version != 2 && version != 3 && version != SectionVersion) return null;
                string profile = ReadString(reader);
                uint revision = reader.ReadUInt32();
                int count = reader.ReadByte();
                if (count == 0 || count > 16) return null;
                var packages = new string[count];
                for (int i = 0; i < count; i++) packages[i] = ReadString(reader);
                var spawn = ReadPoint(reader); var velocity = ReadPoint(reader);
                float radius = reader.ReadSingle(), gravity = reader.ReadSingle();
                float restitution = reader.ReadSingle(), launchScale = reader.ReadSingle();
                var ball = new ArcadeBallDefinition(spawn, velocity, radius, gravity, restitution, launchScale,
                    version >= 3 ? reader.ReadSingle() : 0f);
                bool hasRules = version == 2;
                if (version >= 3)
                {
                    byte marker = reader.ReadByte();
                    if (marker > 1) return null;
                    hasRules = marker == 1;
                }
                SlopballDefinition? rules = hasRules ? new SlopballDefinition(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(),
                    reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    version >= 4 ? reader.ReadSingle() : 0f, version >= 4 ? reader.ReadSingle() : 0f) : null;
                if (stream.Position != stream.Length) return null;
                return new ArcadeStageDefinition(profile, revision, packages, ball, rules);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                return null;
            }
        }

        private static void WritePoint(BinaryWriter writer, ArcadePoint point)
        { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
        private static ArcadePoint ReadPoint(BinaryReader reader)
            => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length == 0 || length > 256) throw new IOException("Invalid Arcade string length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(bytes);
        }
    }
}
