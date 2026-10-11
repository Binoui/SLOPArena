using Xunit;

namespace SlopArena.Shared.Tests;

public class KnockbackFlightTests
{
    private readonly CharacterDefinition _def = TestHelpers.EngineDef;
    private ArenaDefinition _arena = TestHelpers.TestArena();

    private CharacterState Launch(sbyte angle, float force, ushort lockTicks, float altitude = 0f)
    {
        var state = TestHelpers.PlayerState(x: 50f, z: 50f);
        state.PY = TestHelpers.GroundPY(_def) + altitude;
        state.IsGrounded = altitude == 0f;
        Simulation.ApplyKnockbackForce(ref state, 0f, 1f, angle, force, 1);
        if (lockTicks > 0) state.HitstunTicks = lockTicks;
        else { state.HitstunTicks = 0; state.State = ActionState.Idle; }
        return state;
    }

    private void Tick(ref CharacterState state, InputState input = default)
        => Simulation.SimulateTick(ref state, _def, input, _arena, out _, out _,
            DownActionTuning.Default, false);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void VerticalArcDoesNotDependOnHitstunDuration(int shortLock)
    {
        var shortState = Launch(30, 20f, (ushort)shortLock, altitude: 20f);
        var longState = Launch(30, 20f, 60, altitude: 20f);

        for (int tick = 0; tick < 35; tick++)
        {
            Tick(ref shortState);
            Tick(ref longState);
            TestHelpers.AssertNear(longState.PY, shortState.PY, 0.0001f);
            TestHelpers.AssertNear(longState.VY, shortState.VY, 0.0001f);
        }

        Assert.Equal(ActionState.Hitstun, longState.State);
        Assert.Equal(0, shortState.HitstunTicks);
        Assert.True(longState.VY < 10f);
    }

    [Fact]
    public void WeakHitKeepsSmallPopAndCanLandBeforeItsLockExpires()
    {
        var state = TestHelpers.PlayerState(x: 50f, z: 50f);
        float ground = state.PY = TestHelpers.GroundPY(_def);
        // FightGuy's 0% Low Kick, after its four damage is applied.
        state.DamagePercent = 4;
        Simulation.ApplyKnockback(ref state, 0f, 1f, 8, 4f, 20f, 4f, 14, 100f);
        ushort lockTicks = state.HitstunTicks;
        float horizontalImpulse = state.KVZ;
        float apex = 0f;
        bool landedWhileLocked = false;

        for (int tick = 0; tick < lockTicks; tick++)
        {
            Tick(ref state, TestHelpers.Input(jump: true));
            apex = MathF.Max(apex, state.PY - ground);
            if (state.HitstunTicks > 0)
            {
                Assert.Equal(ActionState.Hitstun, state.State);
                Assert.Equal(horizontalImpulse, state.VZ);
                if (state.IsGrounded) landedWhileLocked = true;
            }
        }

        Assert.InRange(apex, 0.03f, 0.08f);
        Assert.True(landedWhileLocked);
        Assert.Equal(0, state.HitstunTicks);
    }

    [Fact]
    public void DiagonalSendRetainsSameTravelAndHeadingAsAxisAlignedSend()
    {
        var axis = Launch(30, 20f, 12, altitude: 20f);
        var diagonal = axis;
        diagonal.KVX = axis.KVZ * 0.6f;
        diagonal.KVZ = axis.KVZ * 0.8f;

        for (int tick = 0; tick < 50; tick++)
        {
            Tick(ref axis);
            Tick(ref diagonal);
            float dx = diagonal.PX - 50f;
            float dz = diagonal.PZ - 50f;
            TestHelpers.AssertNear(axis.PZ - 50f, MathF.Sqrt(dx * dx + dz * dz), 0.0001f);
            TestHelpers.AssertNear(diagonal.VZ * 0.75f, diagonal.VX, 0.0001f);
        }
    }

    [Fact]
    public void LandingOnTriangleFloorPreservesHitstunAndHorizontalCarry()
    {
        _arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 0, AY = 0, AZ = 0, BX = 0, BY = 0, BZ = 200,
                CX = 200, CY = 0, CZ = 0 },
            new CollisionTriangle { AX = 200, AY = 0, AZ = 200, BX = 200, BY = 0, BZ = 0,
                CX = 0, CY = 0, CZ = 200 },
        };
        _arena.MaxX = _arena.MaxZ = 200f;
        _arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in _arena);
        var state = Launch(8, 4.28f, 40);
        float initialHorizontal = state.KVZ;

        for (int tick = 0; tick < 20; tick++) Tick(ref state);

        Assert.True(state.IsGrounded);
        Assert.Equal(ActionState.Hitstun, state.State);
        Assert.Equal(20, state.HitstunTicks);
        TestHelpers.AssertNear(TestHelpers.GroundPY(_def), state.PY, 0.01f);
        Assert.Equal(initialHorizontal, state.VZ);
        Assert.Equal(0f, state.VY);
    }

    [Fact]
    public void StrongFinisherHasBoundedHeightAndStillCarriesOutward()
    {
        var state = TestHelpers.PlayerState(x: 50f, z: 50f);
        float ground = state.PY = TestHelpers.GroundPY(_def);
        state.DamagePercent = 134;
        Simulation.ApplyKnockback(ref state, 0f, 1f, 28, 9f, 42f, 14f, 28, 100f);
        ushort lockTicks = state.HitstunTicks;
        float apex = 0f;
        int landedTick = 0;

        for (int tick = 1; tick <= 180; tick++)
        {
            Tick(ref state);
            apex = MathF.Max(apex, state.PY - ground);
            if (state.IsGrounded && state.HitstunTicks == 0)
            {
                landedTick = tick;
                break;
            }
        }

        Assert.Equal(48, lockTicks);
        Assert.InRange(apex, 2f, 4f);
        Assert.InRange(state.PZ - 50f, 18f, 28f);
        Assert.InRange(landedTick, 60, 100);
    }

    [Fact]
    public void HitstunExpiryDoesNotRestartPopOrApplyGravityTwice()
    {
        var state = Launch(30, 20f, 4, altitude: 10f);
        float upwardImpulse = state.KVY;
        for (int tick = 0; tick < 4; tick++) Tick(ref state);

        Assert.Equal(0, state.HitstunTicks);
        TestHelpers.AssertNear(upwardImpulse, state.VY, 0.0001f);
        Tick(ref state);
        Assert.Equal(5, state.AirTimeTicks);
        TestHelpers.AssertNear(upwardImpulse - Simulation.FlightGravity * Simulation.TickDt,
            state.VY, 0.0001f);
    }

    [Theory]
    [InlineData(-45, 12f)]
    [InlineData(-90, 25f)]
    public void SpikesDescendImmediatelyWithoutGravitySlowingTheirImpulse(int angle, float force)
    {
        var state = Launch((sbyte)angle, force, 60, altitude: 20f);
        float initialVertical = state.KVY;
        float initialHeight = state.PY;
        Tick(ref state);

        Assert.True(state.PY < initialHeight);
        Assert.True(state.VY <= initialVertical);
    }

    [Fact]
    public void HitstopDoesNotConsumePopWindow()
    {
        var state = Launch(45, 12f, 30, altitude: 10f);
        state.HitstopTicks = 6;
        state.QueuedKVOverride = true;
        state.QueuedKVY = state.KVY;
        state.QueuedKVZ = state.KVZ;
        state.QueuedKBStun = 1;
        float launchY = state.PY;
        float impulse = state.KVY;

        for (int tick = 0; tick < 6; tick++) Tick(ref state);
        Assert.Equal(launchY, state.PY);
        Assert.Equal(0, state.AirTimeTicks);
        for (int tick = 0; tick < 4; tick++) Tick(ref state);
        Assert.Equal(4, state.AirTimeTicks);
        TestHelpers.AssertNear(impulse, state.VY, 0.0001f);
    }

    [Fact]
    public void LockFreeLaunchAlsoReceivesPopAndGravity()
    {
        var state = Launch(30, 12f, 0, altitude: 10f);
        float upwardImpulse = state.KVY;
        for (int tick = 0; tick < 4; tick++) Tick(ref state);
        TestHelpers.AssertNear(upwardImpulse, state.VY, 0.0001f);
        Tick(ref state);
        Assert.True(state.VY < upwardImpulse);
        Assert.Equal(0, state.HitstunTicks);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ReconciliationPreservesFlightClock(int replayFromTick)
    {
        var authoritative = Launch(30, 20f, 24, altitude: 10f);
        for (int tick = 0; tick < replayFromTick; tick++) Tick(ref authoritative);
        var replay = authoritative;
        replay.AirTimeTicks = 0;
        byte[] buffer = new byte[CharacterStatePacket.Size];
        CharacterStatePacket.FromState(authoritative, (uint)replayFromTick).Serialize(buffer);
        CharacterStatePacket.Deserialize(buffer).ApplyTo(ref replay);

        for (int tick = 0; tick < 30; tick++)
        {
            Tick(ref authoritative);
            Tick(ref replay);
            Assert.Equal(authoritative.PY, replay.PY);
            Assert.Equal(authoritative.VY, replay.VY);
            Assert.Equal(authoritative.AirTimeTicks, replay.AirTimeTicks);
        }
    }
}
