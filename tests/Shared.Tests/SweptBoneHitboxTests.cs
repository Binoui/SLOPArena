using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tracked bone hitboxes run on the attack's clip clock (fractional baked frames) and, from
/// their second active tick, also test the path the weapon swept since the previous tick.
/// The attacker's own hurtboxes use the same clip clock.
/// </summary>
public class SweptBoneHitboxTests
{
    private const float BladeLength = 1.5f;
    private const float BladeRadius = 0.1f;
    private static readonly float[] Hilt = { 0f, 0.2f, 0.3f };

    // ground.1 "swing": 20 frames over 20 ticks (1×). The blade waits behind-right, then
    // turns 60° per frame across the front: 150° → 90°, 30°, −30°, −90° (0° = forward).
    private const int SwingTrigger = 6, SwingDuration = 4;

    private static float SwingAngle(int frame) => frame switch
    {
        <= 5 => 150f,
        6 => 90f,
        7 => 30f,
        8 => -30f,
        _ => -90f,
    };

    private static readonly string[] Points =
    {
        "mixamorig:Hips", "mixamorig:Head", "_weapon_hilt", "_weapon_tip", BakedAnimationData.HipsOffsetPointName,
    };

    private static float[] Pose(float bladeDegrees, float headX = 0f)
    {
        var frame = new float[Points.Length * 3];
        frame[3] = headX; frame[4] = 0.6f;                      // Head
        Array.Copy(Hilt, 0, frame, 6, 3);                       // _weapon_hilt
        float radians = bladeDegrees * MathF.PI / 180f;
        frame[9] = Hilt[0] + BladeLength * MathF.Sin(radians);  // _weapon_tip
        frame[10] = Hilt[1];
        frame[11] = Hilt[2] + BladeLength * MathF.Cos(radians);
        return frame;                                           // hips and _hips_offset stay at the origin
    }

    private static BakedAnimationData Bake() => new()
    {
        BoneNames = Points,
        Animations = new[]
        {
            new BakedAnimationData.BakedAnim
            {
                Name = "swing", FrameCount = 20,
                Frames = Enumerable.Range(0, 20).Select(f => Pose(SwingAngle(f))).ToArray(),
            },
            // ground.2 "slow": 10 frames stretched over 20 ticks; the head walks 10 cm right per frame.
            new BakedAnimationData.BakedAnim
            {
                Name = "slow", FrameCount = 10,
                Frames = Enumerable.Range(0, 10).Select(f => Pose(0f, headX: 0.1f * f)).ToArray(),
            },
            // ground.3 "flick": 40 frames over 20 ticks (2×); odd frames flick the blade to 40°.
            new BakedAnimationData.BakedAnim
            {
                Name = "flick", FrameCount = 40,
                Frames = Enumerable.Range(0, 40).Select(f => Pose(f % 2 == 1 ? 40f : 0f)).ToArray(),
            },
        },
    };

    private static CookedSlotDefinition Slot(int ordinal, string id, string animation, params CookedTimelineOperation[] operations)
        => new(ordinal, id, false, "Swept hitbox test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(20, 0, 0, 0, 0, new[] { animation }, operations) }));

    private static CookedTimelineOperation Blade(ushort trigger, ushort duration) =>
        new CookedSpawnHitboxOperation(trigger, AuthoringUnit.Meters,
            new CookedHitbox(AuthoringHitboxShape.Capsule, BladeRadius, 0f, 0f, 0f, 0f, 0f, 0f,
                "_weapon_hilt", "_weapon_tip", 10f, 45f, 5f, 2f, 10, duration, true, 0));

    private static AbilitySpec Spec(string animation) => new()
    {
        Stages = new[] { new AttackStage { DurationTicks = 20 } },
        AnimationNames = new[] { animation },
    };

    private static CharacterDefinition Def()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = Slot(0, "ground.1", "swing", Blade(SwingTrigger, SwingDuration));
        slots[1] = Slot(1, "ground.2", "slow");
        slots[2] = Slot(2, "ground.3", "flick");
        def.CookedSlots = slots;
        def.Slot1 = Spec("swing");
        def.Slot2 = Spec("slow");
        def.Slot3 = Spec("flick");
        def.HurtboxBoneDefs = new[]
        {
            new HurtboxBoneDef("mixamorig:Hips", 0f, 0f, 0f, 0.26f),
            new HurtboxBoneDef("mixamorig:Head", 0f, 0f, 0f, 0.22f),
        };
        return def;
    }

    private static readonly HitboxEvent BladeEvent = new()
    {
        Shape = HitboxShape.Capsule, Radius = BladeRadius, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip",
    };

    /// <summary>Swing ground.1 at an idle synthetic opponent standing one metre from the hilt at <paramref name="degrees"/>.</summary>
    private static (ServerSimulation Sim, List<SpellResolver.HitResult> Hits, List<(Hitbox Box, SpellResolver.EntityData Target)> Tested)
        SwingAt(float degrees, float reach = 1f)
    {
        var def = Def();
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, attacker, Bake());

        float radians = degrees * MathF.PI / 180f;
        var opponentDef = TestHelpers.EngineDef;
        var opponent = TestHelpers.NpcState(Hilt[0] + reach * MathF.Sin(radians), Hilt[2] + reach * MathF.Cos(radians));
        opponent.PY = TestHelpers.GroundPY(opponentDef);
        sim.RegisterEntity(100, opponentDef, opponent);

        var hits = new List<SpellResolver.HitResult>();
        var tested = new List<(Hitbox, SpellResolver.EntityData)>();
        for (int tick = 0; tick < 20; tick++)
        {
            sim.Tick(new() { { 1, tick == 0 ? TestHelpers.Input(activeSlot: AbilitySlots.Slot1) : default }, { 100, default } });
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100));
            var body = sim.LastTickAttackEntities.First(e => e.Id == 100 && !e.ShieldSurface);
            foreach (var hb in sim.Resolver.LastTickCheckedHitboxes.Where(h => h.OwnerId == 1))
                tested.Add((hb, body));
        }
        return (sim, hits, tested);
    }

    private static bool Touches(Hitbox hb, SpellResolver.EntityData target)
        => SpellResolver.SurfaceGap(in hb, target, out _, out _, out _) <= 0f;

    [Fact]
    public void SweptBlade_HitsAnOpponentBetweenTwoTestedPoses()
    {
        // Straight ahead: the 30° and −30° poses pass 10 cm to either side; only the arc
        // between them crosses the opponent.
        var (_, hits, tested) = SwingAt(0f);

        var hit = Assert.Single(hits);
        // Tested on every active tick, and on every tick of the freeze its hit causes (ADR-0012).
        Assert.Equal(SwingDuration + hit.HitstopTicks, tested.Count);
        Assert.DoesNotContain(tested, x => Touches(x.Box, x.Target));
    }

    [Fact]
    public void FirstActiveTick_DoesNotSweepTheArcBeforeTheWindow()
    {
        // 120° is only crossed between the last wind-up frame (150°) and the first active
        // pose (90°); the window starts at its first tick, not half a tick earlier.
        var (_, hits, tested) = SwingAt(120f);

        Assert.Null(tested[0].Box.Sweep);
        Assert.Empty(hits);
    }

    [Fact]
    public void SweptBlade_StillHitsEachOpponentOncePerWindow()
    {
        // Close in front, several poses on several ticks overlap the opponent.
        var (_, hits, tested) = SwingAt(0f, reach: 0.5f);

        Assert.True(tested.Count(x => Touches(x.Box, x.Target)) >= 2);
        Assert.Single(hits);
    }

    [Fact]
    public void ResolvePositions_InterpolatesTheBladeRigidlyBetweenFrames()
    {
        var def = Def();
        var baked = Bake();
        var state = new CharacterState { PY = TestHelpers.GroundPY(def) };
        // Halfway between frame 7 (30°) and frame 8 (−30°).
        HitboxGeometry.ResolvePositionsAt(state, BladeEvent, baked, def, new[] { "swing" }, 0, 2, false, 7.5f,
            out float hx, out float hy, out float hz, out float tx, out float ty, out float tz);

        TestHelpers.AssertNear(Hilt[0], hx, 0.0001f);
        TestHelpers.AssertNear(state.PY + Hilt[1], hy, 0.0001f);
        TestHelpers.AssertNear(Hilt[2], hz, 0.0001f);
        // The tip is straight ahead at full blade length, not on the 0.87 m chord.
        TestHelpers.AssertNear(0f, tx, 0.0001f);
        TestHelpers.AssertNear(hy, ty, 0.0001f);
        TestHelpers.AssertNear(Hilt[2] + BladeLength, tz, 0.0001f);
    }

    [Fact]
    public void ResolvePositions_UsesTheExactClipTime()
    {
        var def = Def();
        var baked = Bake();
        // ground.3 plays 40 frames over 20 ticks: elapsed 3 is frame 6, a 0° frame.
        var state = new CharacterState { PY = TestHelpers.GroundPY(def), AttackElapsedTicks = 3 };
        HitboxGeometry.ResolvePositions(state, BladeEvent, baked, def, new[] { "flick" }, 0, 7, false,
            out _, out _, out _, out float tx, out _, out float tz);
        TestHelpers.AssertNear(0f, tx, 0.0001f);
        TestHelpers.AssertNear(Hilt[2] + BladeLength, tz, 0.0001f);

        // ground.2 plays 10 frames over 20 ticks: elapsed 3 is frame 1.5, between two baked frames.
        Assert.Equal(1.5f, HitboxGeometry.AttackFrame(3, 10, 20));
        Assert.True(HitboxGeometry.BakedPointToWorld(state, def, baked, 1, 1.5f, 1, 1f, out float headX, out _, out _));
        TestHelpers.AssertNear(0.15f, headX, 0.0001f);
    }

    [Fact]
    public void Sweep_FollowsTheClipThroughFramesBetweenTicks()
    {
        var def = Def();
        var baked = Bake();
        var owner = new CharacterState { PY = TestHelpers.GroundPY(def), AttackElapsedTicks = 4 };
        HitboxGeometry.ResolvePositions(owner, BladeEvent, baked, def, new[] { "flick" }, 0, 7, false,
            out float x, out float y, out float z, out float ex, out float ey, out float ez);
        var current = new HitboxPose { X = x, Y = y, Z = z, EndX = ex, EndY = ey, EndZ = ez };
        // Both tick poses (frames 6 and 8) point straight ahead; frame 7 flicks to 40°.
        var from = new HitboxSweepOrigin { PY = owner.PY, AttackElapsedTicks = 3, Pose = current };

        var poses = HitboxGeometry.SweepPoses(BladeEvent, baked, def, new[] { "flick" }, 0, 7, false,
            in from, in owner, in current);

        Assert.NotNull(poses);
        float widest = poses!.Max(p => MathF.Atan2(p.EndX - p.X, p.EndZ - p.Z) * 180f / MathF.PI);
        TestHelpers.AssertNear(40f, widest, 0.01f);
    }

    [Fact]
    public void Sweep_SkipsAttackClockSeeksAndStageChanges()
    {
        var def = Def();
        var baked = Bake();
        var owner = new CharacterState { PY = TestHelpers.GroundPY(def), AttackElapsedTicks = 9 };
        var current = new HitboxPose { Z = 1f, EndZ = 2f };
        var seek = new HitboxSweepOrigin { PY = owner.PY, AttackElapsedTicks = 5, Pose = new HitboxPose { X = 2f, EndX = 3f } };
        Assert.Null(HitboxGeometry.SweepPoses(BladeEvent, baked, def, new[] { "swing" }, 0, 2, false, in seek, in owner, in current));

        var nextStage = seek;
        nextStage.AttackElapsedTicks = 8;
        nextStage.ComboStage = 1;
        Assert.Null(HitboxGeometry.SweepPoses(BladeEvent, baked, def, new[] { "swing" }, 0, 2, false, in nextStage, in owner, in current));
    }

    [Fact]
    public void AttackHurtboxes_FollowTheClipClockOnSlowClips()
    {
        // ground.2 stretches 10 frames over 20 ticks. Past elapsed 10 a per-entity frame
        // counter would wrap to the opening pose; the body must keep following the clip.
        var def = Def();
        var sim = TestHelpers.MakeSim();
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, attacker, Bake());

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot2) } });
        for (int tick = 1; tick < 18; tick++)
        {
            var state = sim.GetState(1);
            Assert.Equal(ActionState.Attacking, state.State);
            var head = sim.LastTickAttackEntities.Single(e => e.Id == 1 && MathF.Abs(e.Radius - 0.22f) < 1e-4f);
            float frame = HitboxGeometry.AttackFrame(state.AttackElapsedTicks, 10, 20);
            TestHelpers.AssertNear(state.PX + 0.1f * frame, head.PosX, 0.0001f);
            sim.Tick(new() { { 1, default } });
        }
    }
}
