using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Facing model (ADR-0017 / issue #126) — the unlocked-mode rules the persistent
/// target lock (ADR-0018) overrides: air facing is sticky (no velocity re-facing),
/// ground facing follows movement, and LMB snaps facing to the camera azimuth at the
/// input gate. Golden scenarios pin snap-then-normal and both rejection gates;
/// behavioral tests cover air drift and the same facing seams with angle asserts.
/// The lock tests live in <see cref="TargetLockTests"/>.
/// </summary>
public class FacingSnapTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;
    private static float Gpy => Def.CapsuleHeight * 0.5f;

    // ────────────────────────── Kit regression (issue #126) ──────────────────────────

    [Fact]
    public void FightGuy_SnapThenNormal_HitsAlongSnappedFacing()
    {
        // Kit-specific #126 regression: snap to -Z, then verify FightGuy's normal
        // attack keeps that facing and connects with the target placed along -Z.
        var fg = TestHelpers.FightGuyDef;
        float fgGpy = TestHelpers.GroundPY(fg);
        // Grid-center positions: the arena heightmap spans [0,200]² — a -Z lunge from
        // the origin would exit the grid (no surface → airborne).
        AssertScenario(new KitScenario
        {
            Name = "Facing Snap Then Normal",
            Def = fg,
            Setup = () => TestHelpers.PlayerState(100f, 100f) with { PY = fgGpy, FacingYaw = 0f },
            Inputs = new InputSequence()
                .Set(0, new InputState { FaceToCamera = true, AimYaw = 18000 })
                .Set(1, new InputState { ActiveSlot = AbilitySlots.Normal1 }),
            Assert = player => TestHelpers.AssertNear(MathF.PI, player.FacingYaw, 1e-3f),
            NpcSetup = () => TestHelpers.NpcState(100f, 98.7f) with { PY = fgGpy },
            NpcAssert = npc => Assert.True(npc.DamagePercent > 0),
            NpcDef = fg,
            TotalTicks = 60,
        });
    }


    // ────────────────────────── Behavioral ──────────────────────────

    [Fact]
    public void AirDrift_DoesNotReface()
    {
        // Sticky air facing: drift must never re-face the fighter mid-air. Assert within
        // the airborne window (the player falls to the ground in ~4 ticks; on the ground
        // facing legitimately follows movement again).
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = MathF.PI / 4f };
        sim.RegisterEntity(1, Def, player);

        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = 1f } } }); // drifting +X

        var state = sim.GetState(1);
        Assert.False(state.IsGrounded, "still airborne");
        Assert.True(state.VX > 0.1f, "drift happened");
        TestHelpers.AssertNear(MathF.PI / 4f, state.FacingYaw, 1e-3f);
    }

    [Fact]
    public void GroundMovement_StillFacesVelocity()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { MoveY = 1f } } }); // walk +Z

        var state = sim.GetState(1);
        TestHelpers.AssertNear(0f, state.FacingYaw, 1e-3f); // +Z = yaw 0
    }

    [Fact]
    public void AirSnap_FacesCameraAzimuth()
    {
        // LMB snap works mid-air against sticky facing: facing = camera azimuth instantly.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });

        var state = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-4f);
    }


    [Fact]
    public void Snap_Rejected_MidAttack()
    {
        // Attack animation lock rejects the snap (the "not attacking" gate); facing stays
        // on the attack's tracking target, not the camera.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.Normal1 } } });
        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } }); // LMB mid-attack

        var state = sim.GetState(1);
        Assert.True(state.AnimLockTicks > 0);
        Assert.True(MathF.Abs(state.FacingYaw) < 1f, $"snap must be rejected mid-attack, facing was {state.FacingYaw}");
    }

    [Fact]
    public void Snap_Rejected_InHitstun()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with
        {
            PY = Gpy, FacingYaw = 0f,
            State = ActionState.Hitstun, HitstunTicks = 10, KVY = 3f,
        };
        sim.RegisterEntity(1, Def, player);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });

        var state = sim.GetState(1);
        Assert.True(MathF.Abs(state.FacingYaw) < 1e-3f, $"snap must be rejected in hitstun, facing was {state.FacingYaw}");
    }

    [Fact]
    public void GroundSnap_IsOneTickTurnaround()
    {
        // Ground snap wins the tick it is pressed (instant turnaround for poke spacing),
        // then the next tick's movement re-faces the walk direction.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState(50f, 50f) with { PY = Gpy, FacingYaw = MathF.PI }; // facing -Z; grid-center so -Z walk stays in-grid
        sim.RegisterEntity(1, Def, player);

        // Walking -Z (facing -Z already) + snap to +Z on the same tick
        sim.Tick(new() { { 1, new InputState { MoveY = -1f, FaceToCamera = true, AimYaw = 0 } } });
        var snapped = sim.GetState(1);
        TestHelpers.AssertNear(0f, snapped.FacingYaw, 1e-4f); // snap wins this tick

        // Next tick, still walking: ground movement re-faces -Z
        sim.Tick(new() { { 1, new InputState { MoveY = -1f } } });
        TestHelpers.AssertNear(MathF.PI, sim.GetState(1).FacingYaw, 1e-3f);
    }
}
