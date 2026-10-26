using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TargetedLeapPoseTests
{
    private const ushort FlightTicks = 73;
    private const string LandingId = "presentation.test.landing";
    private static readonly Dictionary<ulong, InputState> Released = new() { [1] = default };

    [Theory]
    [InlineData(-2f, false)]
    [InlineData(0f, false)]
    [InlineData(2f, false)]
    [InlineData(-2f, true)]
    [InlineData(0f, true)]
    [InlineData(2f, true)]
    public void ApproachFinishesTheFlightPoseBeforeActualLandingWithoutChangingTheLeap(float floor, bool triangles)
    {
        var arena = Arena(floor, triangles);
        var (sim, def) = Create(arena, landingClip: true);
        var (control, _) = Create(arena, landingClip: false);
        Launch(sim);
        Launch(control);
        var airborne = new List<CharacterState>();
        int landed = -1, ended = -1;
        ServerSimulation? restored = null;
        for (int tick = 0; tick < 180; tick++)
        {
            sim.Tick(Released);
            control.Tick(Released);
            var state = sim.GetState(1);
            var baseline = control.GetState(1);
            Assert.Equal(baseline.PX, state.PX);
            Assert.Equal(baseline.PY, state.PY);
            Assert.Equal(baseline.PZ, state.PZ);
            Assert.Equal(baseline.VY, state.VY);
            Assert.Equal(baseline.IsGrounded, state.IsGrounded);
            Assert.Equal(baseline.State, state.State);
            Assert.Equal(baseline.AnimLockTicks, state.AnimLockTicks);
            Assert.Equal(control.Resolver.GetActiveHitboxes().Count, sim.Resolver.GetActiveHitboxes().Count);
            if (restored != null)
            {
                restored.Tick(Released);
                Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), "Approach changed after rollback.");
            }
            if (tick == 50)
            {
                restored = TestHelpers.MakeSim(arena);
                restored.RegisterEntity(1, def, TestHelpers.PlayerState());
                restored.RestoreSnapshot(sim.CaptureSnapshot());
            }
            if (!state.IsGrounded && state.State == ActionState.Attacking)
            {
                airborne.Add(state);
                Assert.Equal((byte)0, state.ComboStage);
                Assert.Empty(sim.Resolver.GetActiveHitboxes());
            }
            foreach (var effect in sim.GetPresentationEvents(clear: true))
                if (effect.PresentationId == LandingId)
                {
                    Assert.Equal(-1, landed);
                    landed = tick;
                    Assert.True(state.IsGrounded);
                    Assert.Equal((byte)1, state.ComboStage);
                    Assert.Equal((ushort)0, state.AttackElapsedTicks);
                }
            if (landed >= 0 && ended < 0 && state.State != ActionState.Attacking)
                ended = tick;
        }
        Assert.NotEmpty(airborne);
        Assert.InRange(airborne[^1].AttackElapsedTicks, FlightTicks - 2, FlightTicks - 1);
        Assert.True(landed > 0);
        Assert.Equal(24, ended - landed);
        if (floor > 0)
            Assert.True(airborne.Any(s => s.AttackElapsedTicks > landed), "Raised ground must bring the swing forward.");
        if (floor < 0)
            Assert.Contains(airborne.Skip(FlightTicks), s => s.AttackElapsedTicks < FlightTicks - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VoidDoesNotAnticipateAnImaginaryLanding(bool triangles)
    {
        var (sim, _) = Create(Arena(float.MinValue, triangles), landingClip: true);
        Launch(sim);
        for (int tick = 0; tick < 121; tick++)
        {
            sim.Tick(Released);
            var state = sim.GetState(1);
            if (state.State == ActionState.Attacking)
            {
                Assert.Equal((byte)0, state.ComboStage);
                Assert.True(state.AttackElapsedTicks < FlightTicks - 1);
            }
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == LandingId);
        }
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static (ServerSimulation Sim, CharacterDefinition Def) Create(ArenaDefinition arena, bool landingClip)
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { Gravity = 26f } };
        def = def with { Movement = def.Movement with { MaxFallSpeed = 30f } };
        string[] clips = landingClip ? new[] { "anim.test.flight", "anim.test.landing" } : new[] { "anim.test.flight" };
        var hitbox = new CookedHitbox(AuthoringHitboxShape.Sphere, 2f, 0f, 0f, 0f, 0f, 0f, 0f,
            null, null, 1f, 45f, 0f, 0f, 3, 3, true, 0);
        var parameters = new CookedTargetedLeapCapabilityParameters(0, 120, 1f, 12f, 16f, 0, 24, hitbox,
            LandingPresentationId: LandingId);
        var slot = new CookedSlotDefinition(4, "ground.E", false, "Leap", "", "", AuthoringAbilityBehavior.AimedProjectile,
            AuthoringAimMode.GroundCursor, 0, true, false, new CookedTimeline(new[]
            {
                new CookedStage(FlightTicks, 0, 0, 0, 0, clips, new CookedTimelineOperation[]
                {
                    new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                        CharacterPackageCompiler.TargetedLeapCapabilityId, "1", parameters),
                }),
            }));
        var slots = def.CookedSlots!.ToArray();
        slots[4] = slot;
        def = def with { CookedSlots = slots };
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 10f) with { PY = TestHelpers.GroundPY(def) });
        return (sim, def);
    }

    private static void Launch(ServerSimulation sim)
    {
        for (int tick = 0; tick < 12; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { ActiveSlot = tick == 0 ? (byte)4 : (byte)0, IsAiming = true, AimDistance = 800 },
            });
        sim.Tick(Released);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
    }

    private static ArenaDefinition Arena(float floor, bool triangles)
    {
        var arena = TestHelpers.TestArena();
        for (int z = 12; z < arena.Heightmap.Height; z++)
            for (int x = 0; x < arena.Heightmap.Width; x++)
                arena.Heightmap.Data[z * arena.Heightmap.Width + x] = floor;
        if (triangles)
        {
            var surfaces = new List<CollisionTriangle>();
            AddFloor(0f, 0f, 12f);
            if (floor > float.MinValue) AddFloor(floor, 12f, 200f);
            arena.CollisionTriangles = surfaces.ToArray();
            void AddFloor(float y, float fromZ, float toZ)
            {
                surfaces.Add(new CollisionTriangle { AX = 0f, AY = y, AZ = fromZ,
                    BX = 0f, BY = y, BZ = toZ, CX = 200f, CY = y, CZ = fromZ });
                surfaces.Add(new CollisionTriangle { AX = 200f, AY = y, AZ = fromZ,
                    BX = 0f, BY = y, BZ = toZ, CX = 200f, CY = y, CZ = toZ });
            }
        }
        return arena;
    }
}
