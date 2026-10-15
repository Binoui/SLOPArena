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

        public ArcadeStageDefinition Compile()
        {
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
    }
}
