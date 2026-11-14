using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The Shared body pose clock (option A): the renderer's former run cadence, reaction stretch,
/// tumble selection and transitions as deterministic per-fighter state that poses hurtboxes.
/// Uses a synthetic bake whose probe point encodes the clip and frame, never roster content.
/// </summary>
public class BodyPoseClockTests
{
    private static readonly (string Name, int Frames)[] Clips =
    {
        ("idle", 30), ("run", 20), ("jump", 12), ("fall", 16),
        ("hit_light", 24), ("hit_medium", 30), ("hit_hard", 40), ("tumble", 84),
    };

    /// <summary>The probe point's x is clip index × 100 + frame, in millimetres.</summary>
    private static BakedAnimationData Bake()
    {
        var bytes = new List<byte>();
        void U32(uint value) => bytes.AddRange(BitConverter.GetBytes(value));
        void Text(string value) { var b = Encoding.UTF8.GetBytes(value); U32((uint)b.Length); bytes.AddRange(b); }
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        U32(1);
        U32(1);
        U32((uint)Clips.Length);
        Text("probe");
        for (int clip = 0; clip < Clips.Length; clip++)
        {
            Text(Clips[clip].Name);
            U32((uint)Clips[clip].Frames);
            for (int frame = 0; frame < Clips[clip].Frames; frame++)
            {
                bytes.AddRange(BitConverter.GetBytes((clip * 100 + frame) * 0.001f));
                bytes.AddRange(BitConverter.GetBytes(0f));
                bytes.AddRange(BitConverter.GetBytes(0f));
            }
        }
        return BakedAnimationData.LoadFromBin(bytes.ToArray());
    }

    private static readonly BakedAnimationData Baked = Bake();

    private static CharacterDefinition Def() => TestHelpers.CloneDef(TestHelpers.FightGuyDef) with
    {
        IdleAnim = "idle", RunAnim = "run", JumpAnim = "jump", FallAnim = "fall",
        HitSmallAnim = "hit_light", HitMediumAnim = "hit_medium", HitHardAnim = "hit_hard", TumbleAnim = "tumble",
        CrouchAnim = "", SlideAnim = "", ShellAnim = "", FlyingAnim = "", AirDodgeAnim = "", DashAnim = "idle",
        HurtboxBoneDefs = new[] { new HurtboxBoneDef("probe", 0f, 0f, 0f, 0.1f) },
        HurtboxCapsules = null,
    };

    private static int Count(string clip) => Baked.Animations[Baked.FindAnimIndex(clip)].FrameCount;

    private static CharacterState Grounded() => new()
    {
        State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2, PY = 1f,
    };

    private static CharacterState Hit(ushort sequence, ushort hitstun, byte level = 2,
        float kvx = 0f, float kvy = 0f, bool grounded = true) => new()
    {
        State = ActionState.Hitstun, HitstunTicks = hitstun, HitstunLevel = level, HitReactionSequence = sequence,
        KVX = kvx, KVY = kvy, IsGrounded = grounded, JumpsLeft = 2, PY = 1f,
        WasAirborneDuringKnockback = !grounded,
    };

    private static (string Clip, float Frame) Step(ref BodyPoseClock clock, in CharacterState state, CharacterDefinition def)
    {
        Assert.True(BodyPoseClock.Step(ref clock, in state, def, Baked, null, out int index, out float frame));
        return (Baked.Animations[index].Name, frame);
    }

    [Fact]
    public void Reaction_StretchesOverTheHitstunItBeganWith()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        const ushort hitstun = 90;
        int frames = Count("hit_hard");
        for (int tick = 0; tick <= hitstun; tick++)
        {
            var (clip, frame) = Step(ref clock, Hit(1, (ushort)(hitstun - tick)), def);
            Assert.Equal("hit_hard", clip);
            Assert.Equal(BodyClipKind.Reaction, clock.Kind);
            float expected = Math.Min(tick * (float)frames / hitstun, frames - 1);
            TestHelpers.AssertNear(expected, frame, 0.001f);
            TestHelpers.AssertNear((float)frames / hitstun, clock.Rate, 0.0001f);
        }
        // The whole clip plays out exactly as hitstun runs out.
        TestHelpers.AssertNear(frames - 1, clock.Frame, 0.001f);
    }

    [Fact]
    public void Reaction_ClipFollowsTheHitstunLevel()
    {
        var def = Def();
        foreach (var (level, clip) in new[] { ((byte)0, "hit_light"), ((byte)1, "hit_medium"), ((byte)2, "hit_hard") })
        {
            var clock = default(BodyPoseClock);
            Assert.Equal(clip, Step(ref clock, Hit(1, 40, level), def).Clip);
        }
    }

    [Fact]
    public void Hitstop_HoldsThePoseWithoutAdvancing()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        Step(ref clock, Hit(1, 60), def);
        var (_, before) = Step(ref clock, Hit(1, 59), def);
        var frozen = Hit(1, 59);
        frozen.HitstopTicks = 5;
        for (int i = 0; i < 5; i++)
            Assert.Equal(before, Step(ref clock, frozen, def).Frame);
        var (_, after) = Step(ref clock, Hit(1, 58), def);
        TestHelpers.AssertNear(before + clock.Rate, after, 0.0001f);
    }

    [Fact]
    public void ReHit_RestartsTheReactionOnItsNewIdentity()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        for (int tick = 0; tick < 20; tick++) Step(ref clock, Hit(1, (ushort)(80 - tick)), def);
        Assert.True(clock.Frame > 0f);
        ushort segment = clock.Segment;

        // The same reaction with a changed timer is not a re-hit.
        Step(ref clock, Hit(1, 30), def);
        Assert.Equal(segment, clock.Segment);

        // A new reaction identity restarts from frame zero, stretched over its own hitstun, even on
        // the same clip and even when its hitstun is shorter.
        var (clip, frame) = Step(ref clock, Hit(2, 50), def);
        Assert.Equal("hit_hard", clip);
        Assert.Equal(0f, frame);
        Assert.NotEqual(segment, clock.Segment);
        Assert.Equal((ushort)2, clock.ReactionSequence);
        Assert.Equal((ushort)50, clock.ReactionTicks);
        TestHelpers.AssertNear((float)Count("hit_hard") / 50, clock.Rate, 0.0001f);
        TestHelpers.AssertNear(clock.Rate, Step(ref clock, Hit(2, 49), def).Frame, 0.0001f);
    }

    [Fact]
    public void ReHit_DuringHitstopRestartsWhenTheFreezeEnds()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        for (int tick = 0; tick < 10; tick++) Step(ref clock, Hit(1, (ushort)(60 - tick)), def);
        var frozen = Hit(1, 50);
        frozen.HitstopTicks = 3;
        float held = Step(ref clock, frozen, def).Frame;
        Assert.True(held > 0f);
        Assert.Equal(0f, Step(ref clock, Hit(2, 40), def).Frame);
    }

    [Fact]
    public void Run_AdvancesBySpeedOverRunSpeedWithAFloor()
    {
        var def = Def();
        float runSpeed = def.Movement.RunSpeed;
        int frames = Count("run");
        var clock = default(BodyPoseClock);
        float[] speeds = { runSpeed, runSpeed * 0.6f, runSpeed * 0.8f, BodyPoseClock.RunSpeedThreshold * 2f, runSpeed * 1.5f };
        float expected = 0f;
        for (int tick = 0; tick < 60; tick++)
        {
            float speed = speeds[tick % speeds.Length];
            var state = Grounded();
            state.State = ActionState.Run;
            state.VX = speed * 0.6f;
            state.VZ = speed * 0.8f;
            var (clip, frame) = Step(ref clock, state, def);
            Assert.Equal("run", clip);
            Assert.Equal(BodyClipKind.Run, clock.Kind);
            float rate = MathF.Max(BodyPoseClock.RunCadenceFloor, speed / runSpeed);
            // The first run tick shows frame zero; each later tick advances by that tick's cadence.
            if (tick > 0) expected = (expected + rate) % frames;
            TestHelpers.AssertNear(expected, frame, 0.002f);
            TestHelpers.AssertNear(rate, clock.Rate, 0.0001f);
        }
    }

    [Fact]
    public void Run_StartsAboveTheThresholdAndRestartsAfterIdle()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        var still = Grounded();
        still.VX = BodyPoseClock.RunSpeedThreshold * 0.5f;
        Assert.Equal("idle", Step(ref clock, still, def).Clip);
        var moving = Grounded();
        moving.VX = BodyPoseClock.RunSpeedThreshold * 1.5f;
        Assert.Equal(("run", 0f), Step(ref clock, moving, def));
        TestHelpers.AssertNear(BodyPoseClock.RunCadenceFloor, Step(ref clock, moving, def).Frame, 0.0001f);
        Assert.Equal("idle", Step(ref clock, still, def).Clip);
        Assert.Equal(("run", 0f), Step(ref clock, moving, def));
    }

    [Fact]
    public void RunLoop_HurtboxBlendsAcrossTheLoopSeam()
    {
        var def = Def();
        var state = Grounded();
        int run = Baked.FindAnimIndex("run");
        int frames = Count("run");
        float lastX = (run * 100 + frames - 1) * 0.001f, firstX = run * 100 * 0.001f;
        Assert.True(HitboxGeometry.BakedPointToWorld(state, def, Baked, run, frames - 0.5f, 0, 1f, true,
            out float looped, out _, out _));
        TestHelpers.AssertNear(state.PX + (lastX + firstX) / 2f, looped, 0.00001f);
        // A clip that ends holds its last frame instead.
        Assert.True(HitboxGeometry.BakedPointToWorld(state, def, Baked, run, frames - 0.5f, 0, 1f,
            out float held, out _, out _));
        TestHelpers.AssertNear(state.PX + lastX, held, 0.00001f);
    }

    [Fact]
    public void TumbleLaunch_RequiresHitstunAndElevation()
    {
        int min = BodyPoseClock.TumbleMinHitstunTicks;
        Assert.True(BodyPoseClock.IsTumbleLaunch(min, 1f, 1f, 0f));
        Assert.False(BodyPoseClock.IsTumbleLaunch(min - 1, 1f, 1f, 0f));
        Assert.False(BodyPoseClock.IsTumbleLaunch(min, 1f, 0f, 0f));
        Assert.False(BodyPoseClock.IsTumbleLaunch(min, 1f, -1f, 0f));
        // 15° is the boundary: just above tumbles, just below keeps the reaction.
        float tan14 = MathF.Tan(14f * MathF.PI / 180f), tan16 = MathF.Tan(16f * MathF.PI / 180f);
        Assert.True(BodyPoseClock.IsTumbleLaunch(min, 0.6f, tan16, 0.8f));
        Assert.False(BodyPoseClock.IsTumbleLaunch(min, 0.6f, tan14, 0.8f));
        Assert.True(BodyPoseClock.IsTumbleLaunch(min, 0f, 0.1f, 0f));
    }

    [Fact]
    public void StrongLaunch_SnapsThenTumblesThenLoopsUntilLanding()
    {
        var def = Def();
        var clock = default(BodyPoseClock);
        const ushort hitstun = 60;
        int tick = 0;
        for (; tick < BodyPoseClock.TumbleImpactSnapTicks; tick++)
        {
            var (clip, frame) = Step(ref clock, Hit(1, (ushort)(hitstun - tick), kvx: 2f, kvy: 10f, grounded: false), def);
            Assert.Equal("hit_hard", clip);
            Assert.Equal(BodyClipKind.ImpactSnap, clock.Kind);
            Assert.Equal(tick, frame); // the impact snap runs at clip speed
        }
        int tumbleFrames = Count("tumble");
        int tumbleTick = 0;
        for (; tick < hitstun; tick++, tumbleTick++)
        {
            var (clip, frame) = Step(ref clock, Hit(1, (ushort)(hitstun - tick), kvx: 2f, kvy: 10f, grounded: false), def);
            Assert.Equal("tumble", clip);
            Assert.Equal(tumbleTick, frame);
        }
        // Hitstun is over but the fighter is still flying and has not acted: the tumble carries on,
        // through its build-up once and then its loop.
        for (; tumbleTick < tumbleFrames + 100; tumbleTick++)
        {
            var flying = Grounded();
            flying.IsGrounded = false;
            flying.WasAirborneDuringKnockback = true;
            flying.InPostHitstunFlight = true;
            var (clip, frame) = Step(ref clock, flying, def);
            Assert.Equal("tumble", clip);
            Assert.Equal(BodyPoseClock.TumbleFrame(tumbleTick, tumbleFrames), frame);
            if (tumbleTick >= tumbleFrames)
                Assert.InRange(frame, BodyPoseClock.TumbleLoopStartFrame, tumbleFrames - 1);
        }
        Assert.Equal("idle", Step(ref clock, Grounded(), def).Clip);
    }

    [Fact]
    public void Tumble_EndsOnAJumpOrAnAction()
    {
        var def = Def();
        foreach (var act in new Func<CharacterState, CharacterState>[]
                 {
                     s => { s.JumpsLeft--; return s; },
                     s => { s.State = ActionState.AirDodgeMovement; return s; },
                 })
        {
            var clock = default(BodyPoseClock);
            for (int tick = 0; tick < 40; tick++)
                Step(ref clock, Hit(1, (ushort)(40 - tick), kvy: 10f, grounded: false), def);
            Assert.Equal(BodyClipKind.Tumble, clock.Kind);
            var flying = Grounded();
            flying.IsGrounded = false;
            flying.WasAirborneDuringKnockback = true;
            flying.InPostHitstunFlight = true;
            Step(ref clock, flying, def);
            Assert.Equal(BodyClipKind.Tumble, clock.Kind);
            Step(ref clock, act(flying), def);
            Assert.NotEqual(BodyClipKind.Tumble, clock.Kind);
        }
    }

    [Fact]
    public void WeakOrShortLaunches_KeepTheStretchedReaction()
    {
        var def = Def();
        var shallow = default(BodyPoseClock);
        Step(ref shallow, Hit(1, 60, kvx: 10f, kvy: 1f, grounded: false), def);
        Assert.Equal(BodyClipKind.Reaction, shallow.Kind);
        var brief = default(BodyPoseClock);
        Step(ref brief, Hit(1, (ushort)(BodyPoseClock.TumbleMinHitstunTicks - 1), kvy: 10f, grounded: false), def);
        Assert.Equal(BodyClipKind.Reaction, brief.Kind);
    }

    [Fact]
    public void LaunchLandingInsideHitstun_FinishesTheReactionsRecovery()
    {
        var def = Def();
        int frames = Count("hit_hard");
        foreach (int landTick in new[] { BodyPoseClock.TumbleImpactSnapTicks - 1, 30 })
        {
            var clock = default(BodyPoseClock);
            const ushort hitstun = 60;
            int tick = 0;
            for (; tick < landTick; tick++)
                Step(ref clock, Hit(1, (ushort)(hitstun - tick), kvy: 10f, grounded: false), def);
            // Grounded at the snap's end, or landing mid-tumble: the reaction's remainder fills hitstun.
            for (; tick < hitstun; tick++)
            {
                ushort left = (ushort)(hitstun - tick);
                Step(ref clock, Hit(1, left, kvy: 10f, grounded: true), def);
                if (clock.Kind == BodyClipKind.Recovery) break;
            }
            Assert.Equal(BodyClipKind.Recovery, clock.Kind);
            ushort remaining = (ushort)(hitstun - tick);
            float progress = (float)(hitstun - remaining) / hitstun;
            TestHelpers.AssertNear(progress * frames, clock.Frame, 0.001f);
            TestHelpers.AssertNear(frames * (1f - progress) / remaining, clock.Rate, 0.0001f);
            for (tick++; tick <= hitstun; tick++)
                Step(ref clock, Hit(1, (ushort)(hitstun - tick), kvy: 10f, grounded: true), def);
            TestHelpers.AssertNear(frames - 1, clock.Frame, 0.01f);
        }
    }

    [Fact]
    public void Simulation_HurtboxesTakeTheClockPose_AndSnapshotsRestoreItMidReaction()
    {
        var def = Def();
        var first = Run(def, restoreAt: null, out _);
        var restored = Run(def, restoreAt: 25, out var restoredSim);
        Assert.Equal(first.Count, restored.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Snapshot, restored[i].Snapshot);
            Assert.Equal(first[i].ProbeX, restored[i].ProbeX);
        }
        // The reaction really is mid-stretch at the restore point and its hurtbox follows the clock.
        var mid = first[25];
        Assert.Equal(BodyClipKind.Reaction, mid.Clock.Kind);
        Assert.True(mid.Clock.Frame > 0f && mid.Clock.Frame < Count("hit_hard") - 1);
        int clip = Baked.FindAnimIndex("hit_hard");
        foreach (var tick in first.Where(t => t.Clock.Kind == BodyClipKind.Reaction))
            TestHelpers.AssertNear((clip * 100 + tick.Clock.Frame) * 0.001f, tick.ProbeX, 0.00001f);
        Assert.Equal(first[^1].Snapshot, restoredSim.CaptureSnapshot().Data);
    }

    private sealed record Tick(byte[] Snapshot, BodyPoseClock Clock, float ProbeX);

    private static List<Tick> Run(CharacterDefinition def, int? restoreAt, out ServerSimulation sim)
    {
        sim = TestHelpers.MakeSim();
        var start = Grounded();
        start.PY = TestHelpers.GroundPY(def);
        start.PX = 5f;
        start.PZ = 5f;
        sim.RegisterEntity(1, def, start, Baked);
        var other = start;
        other.PX = 15f;
        sim.RegisterEntity(2, def, other, Baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = default, [2] = default };
        var ticks = new List<Tick>();
        WorldSnapshot? saved = null;
        for (int tick = 0; tick < 80; tick++)
        {
            if (tick == 5)
            {
                var hit = sim.GetState(1);
                hit.State = ActionState.Hitstun;
                hit.HitstunTicks = 60;
                hit.HitstunLevel = 2;
                hit.HitReactionSequence++;
                sim.SetState(1, hit);
            }
            if (tick == 45)
            {
                // A re-hit mid-reaction: a new identity with shorter hitstun.
                var rehit = sim.GetState(1);
                rehit.HitstunTicks = 30;
                rehit.HitReactionSequence++;
                sim.SetState(1, rehit);
            }
            if (restoreAt == tick) saved = sim.CaptureSnapshot();
            sim.Tick(inputs);
            float probe = sim.LastTickAttackEntities.Single(e => e.Id == 1).PosX - sim.GetState(1).PX;
            ticks.Add(new Tick(sim.CaptureSnapshot().Data, sim.GetBodyClock(1), probe));
        }
        if (saved == null) return ticks;
        // Rewind into a fresh simulation and replay: every later tick must match byte for byte.
        var replay = TestHelpers.MakeSim();
        replay.RegisterEntity(1, def, start, Baked);
        replay.RegisterEntity(2, def, other, Baked);
        replay.RestoreSnapshot(saved);
        ticks.RemoveRange(restoreAt!.Value, ticks.Count - restoreAt.Value);
        for (int tick = restoreAt.Value; tick < 80; tick++)
        {
            if (tick == 45)
            {
                var rehit = replay.GetState(1);
                rehit.HitstunTicks = 30;
                rehit.HitReactionSequence++;
                replay.SetState(1, rehit);
            }
            replay.Tick(inputs);
            float probe = replay.LastTickAttackEntities.Single(e => e.Id == 1).PosX - replay.GetState(1).PX;
            ticks.Add(new Tick(replay.CaptureSnapshot().Data, replay.GetBodyClock(1), probe));
        }
        sim = replay;
        return ticks;
    }
}
