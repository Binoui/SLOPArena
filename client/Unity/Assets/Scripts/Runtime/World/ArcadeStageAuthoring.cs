using System;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Editor cook input only. The simulation consumes the embedded baked section.</summary>
    [DisallowMultipleComponent]
    public sealed class ArcadeStageAuthoring : MonoBehaviour
    {
        public string ProfileId = "phase1_ball_lab";
        public uint GameplayRevision = 1;
        public string[] AllowedPackageIds = { "fightguy", "manki", "wibou", "bonk" };
        public Transform BallSpawn;
        public Vector3 InitialVelocity = new(3f, 4f, 2f);
        public float Radius = .32f;
        public float Gravity = 12f;
        [Range(0f, 1f)] public float Restitution = .95f;
        public float LaunchScale = .3f;
        [Range(0f, 5f)] public float LinearDrag;
        [Header("Optional Slopball challenge")]
        public bool Slopball;
        public float NetZ;
        public float NetHalfThickness = .05f;
        public float NetTop = 1.1f;
        public float FloorY;
        public uint Seed = 1337;
        public ushort ServeGapTicks = 45;
        public ushort EasyFlightTicks = 108;
        public ushort HardFlightTicks = 72;
        public ushort FlightJitterTicks = 6;
        public uint RampReturns = 40;
        public float LandingHalfWidth = 2.2f;
        public float LandingMinZ = -5f;
        public float LandingMaxZ = -3f;
        [Min(0f)] public float SpawnHalfWidth;
        [Min(0f)] public float SpawnHalfDepth;
        [Header("Optional Character Course (instead of a ball)")]
        public bool CharacterCourse;
        [Serializable] public sealed class Target
        {
            public string Id;
            public Transform Marker;
            public float Radius = .65f;
        }
        [Serializable] public sealed class Checkpoint
        {
            public string Id;
            public Transform Marker;
            public Vector3 HalfSize = new(2f, 2f, 2f);
            public Transform Respawn;
            public ushort RequiredTargetCount;
        }
        public Target[] Targets = Array.Empty<Target>();
        public Checkpoint[] Checkpoints = Array.Empty<Checkpoint>();
        public Transform Finish;
        public Vector3 FinishHalfSize = new(2f, 2f, 2f);

        public ArcadeStageDefinition Compile()
        {
            if (CharacterCourse)
            {
                if (Slopball || BallSpawn != null || Targets == null || Checkpoints == null)
                    throw new ArgumentException("A Course cannot also define a ball/Slopball profile.");
                var targets = new CourseTargetDefinition[Targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    var target = Targets[i] ?? throw new ArgumentException("Missing course target.");
                    targets[i] = new(target.Id, Point(target.Marker), target.Radius);
                }
                var checkpoints = new CourseCheckpointDefinition[Checkpoints.Length];
                for (int i = 0; i < checkpoints.Length; i++)
                {
                    var cp = Checkpoints[i] ?? throw new ArgumentException("Missing course checkpoint.");
                    checkpoints[i] = new(cp.Id, new(Point(cp.Marker), new(cp.HalfSize.x, cp.HalfSize.y, cp.HalfSize.z)),
                        Point(cp.Respawn), cp.Respawn.eulerAngles.y * Mathf.Deg2Rad, cp.RequiredTargetCount);
                }
                return ArcadeStageDefinition.ForCourse(ProfileId, GameplayRevision, AllowedPackageIds,
                    new(targets, checkpoints, new(Point(Finish), new(FinishHalfSize.x, FinishHalfSize.y, FinishHalfSize.z))));
            }
            if (BallSpawn == null || !BallSpawn.IsChildOf(transform)
                || BallSpawn.childCount != 0 || BallSpawn.GetComponents<Component>().Length != 1)
                throw new ArgumentException("Arcade BallSpawn must be an empty child marker under AuthoringAids.");
            Vector3 position = BallSpawn.position;
            return new ArcadeStageDefinition(ProfileId, GameplayRevision, AllowedPackageIds,
                new ArcadeBallDefinition(new(position.x, position.y, position.z),
                    new(InitialVelocity.x, InitialVelocity.y, InitialVelocity.z),
                    Radius, Gravity, Restitution, LaunchScale, LinearDrag),
                Slopball ? new SlopballDefinition(NetZ, NetHalfThickness, NetTop, FloorY, Seed,
                    ServeGapTicks, EasyFlightTicks, HardFlightTicks, FlightJitterTicks, RampReturns,
                    LandingHalfWidth, LandingMinZ, LandingMaxZ, SpawnHalfWidth, SpawnHalfDepth) : null);
        }

        private ArcadePoint Point(Transform marker)
        {
            if (marker == null || !marker.IsChildOf(transform) || marker.childCount != 0
                || marker.GetComponents<Component>().Length != 1)
                throw new ArgumentException("Course markers must be empty children under AuthoringAids.");
            var p = marker.position; return new(p.x, p.y, p.z);
        }
    }
}
