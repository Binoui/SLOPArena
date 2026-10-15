using System;
using System.Collections.Generic;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>A baked, nonblocking sensor, tested against the fighter's swept hip position.</summary>
    public sealed class ArcadeVolume
    {
        public ArcadePoint Center { get; }
        public ArcadePoint HalfSize { get; }
        public ArcadeVolume(ArcadePoint center, ArcadePoint halfSize)
        {
            if (!center.IsFinite || !halfSize.IsFinite || halfSize.X <= 0 || halfSize.Y <= 0 || halfSize.Z <= 0)
                throw new ArgumentException("Course sensor volumes must be finite and have positive dimensions.");
            Center = center; HalfSize = halfSize;
        }

        internal bool Crosses(ArcadePoint start, ArcadePoint end)
        {
            float enter = 0f, exit = 1f;
            return Axis(start.X, end.X, Center.X, HalfSize.X, ref enter, ref exit)
                && Axis(start.Y, end.Y, Center.Y, HalfSize.Y, ref enter, ref exit)
                && Axis(start.Z, end.Z, Center.Z, HalfSize.Z, ref enter, ref exit);
        }

        private static bool Axis(float start, float end, float center, float half, ref float enter, ref float exit)
        {
            float delta = end - start;
            if (MathF.Abs(delta) < .000001f) return start >= center - half && start <= center + half;
            float a = (center - half - start) / delta, b = (center + half - start) / delta;
            enter = MathF.Max(enter, MathF.Min(a, b)); exit = MathF.Min(exit, MathF.Max(a, b));
            return enter <= exit;
        }
    }

    public sealed class CourseTargetDefinition
    {
        public string Id { get; }
        public ArcadePoint Position { get; }
        public float Radius { get; }
        public CourseTargetDefinition(string id, ArcadePoint position, float radius)
        {
            CharacterCourseDefinition.ValidateId(id);
            if (!position.IsFinite || !float.IsFinite(radius) || radius <= 0f || radius > 2f)
                throw new ArgumentException("Course targets need finite positions and a radius in (0, 2].");
            Id = id; Position = position; Radius = radius;
        }
    }

    public sealed class CourseCheckpointDefinition
    {
        public string Id { get; }
        public ArcadeVolume Volume { get; }
        public ArcadePoint Respawn { get; }
        public float RespawnYaw { get; }
        /// <summary>All targets in the ordered prefix must be broken. Earlier marker visits are optional.</summary>
        public ushort RequiredTargetCount { get; }
        public CourseCheckpointDefinition(string id, ArcadeVolume volume, ArcadePoint respawn,
            float respawnYaw, ushort requiredTargetCount)
        {
            CharacterCourseDefinition.ValidateId(id);
            if (volume == null || !respawn.IsFinite || !float.IsFinite(respawnYaw) || requiredTargetCount == 0)
                throw new ArgumentException("Invalid course checkpoint.");
            Id = id; Volume = volume; Respawn = respawn; RespawnYaw = respawnYaw; RequiredTargetCount = requiredTargetCount;
        }
    }

    /// <summary>Immutable course markers in route order. Every target is required for completion.</summary>
    public sealed class CharacterCourseDefinition
    {
        // Byte aliases 128..254 are local object selections, never a remote protocol change.
        public const int MaxTargets = 127;
        public IReadOnlyList<CourseTargetDefinition> Targets { get; }
        public IReadOnlyList<CourseCheckpointDefinition> Checkpoints { get; }
        public ArcadeVolume Finish { get; }
        public CharacterCourseDefinition(IReadOnlyList<CourseTargetDefinition> targets,
            IReadOnlyList<CourseCheckpointDefinition> checkpoints, ArcadeVolume finish)
        {
            if (targets == null || targets.Count == 0 || targets.Count > MaxTargets
                || checkpoints == null || checkpoints.Count > 32 || finish == null)
                throw new ArgumentException("A course requires 1–127 targets, at most 32 checkpoints and a finish.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targetCopy = new CourseTargetDefinition[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null || !ids.Add(target.Id)) throw new ArgumentException("Duplicate or missing course target.");
                targetCopy[i] = target;
            }
            var checkpointCopy = new CourseCheckpointDefinition[checkpoints.Count];
            int previous = 0;
            for (int i = 0; i < checkpoints.Count; i++)
            {
                var checkpoint = checkpoints[i];
                if (checkpoint == null || !ids.Add(checkpoint.Id) || checkpoint.RequiredTargetCount < previous
                    || checkpoint.RequiredTargetCount > targets.Count)
                    throw new ArgumentException("Checkpoints need unique identities and cumulative target prerequisites.");
                previous = checkpoint.RequiredTargetCount; checkpointCopy[i] = checkpoint;
            }
            Targets = Array.AsReadOnly(targetCopy); Checkpoints = Array.AsReadOnly(checkpointCopy); Finish = finish;
        }

        internal static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || Encoding.UTF8.GetByteCount(id) > 256)
                throw new ArgumentException("Course marker identities must contain 1–256 UTF-8 bytes.");
        }

        public void ValidateArena(in ArenaDefinition arena)
        {
            var bounds = ArenaCollision.ResolveBlastLines(in arena);
            bool Inside(ArcadePoint p, ArcadePoint half)
                => p.X - half.X >= bounds.KillMinX && p.X + half.X <= bounds.KillMaxX
                    && p.Y - half.Y > bounds.KillHeight && p.Y + half.Y < bounds.KillTop
                    && p.Z - half.Z >= bounds.KillMinZ && p.Z + half.Z <= bounds.KillMaxZ;
            foreach (var target in Targets)
                if (!Inside(target.Position, new(target.Radius, target.Radius, target.Radius)))
                    throw new ArgumentException("Course target is outside the stage's blast bounds: " + target.Id);
            foreach (var checkpoint in Checkpoints)
                if (!Inside(checkpoint.Volume.Center, checkpoint.Volume.HalfSize) || !Inside(checkpoint.Respawn, default))
                    throw new ArgumentException("Course checkpoint is outside the stage's blast bounds: " + checkpoint.Id);
            if (!Inside(Finish.Center, Finish.HalfSize)) throw new ArgumentException("Course finish is outside the stage's blast bounds.");
        }
    }
}
