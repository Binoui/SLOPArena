using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// HITSTUN RE-HIT ANIMATION RESTART CONTRACT
/// ═══════════════════════════════════════════════════════════════════════
///
/// The victim's hitstun clip must restart every time a NEW hit connects —
/// even when the new hit's StunTicks is SHORTER than the remaining hitstun.
///
/// Why that matters: ResolveHits overwrites HitstunTicks with the new hit's
/// raw StunTicks ("preserve the existing StunTicks override", ServerSimulation
/// + Simulation.cs), so a re-hit while the victim is mid-hitstun can send
/// HitstunTicks DOWN (e.g. 32 → 16). PlayerRenderer's re-hit detection
/// ("ticks went up OR first entry", `state.HitstunTicks >= _lastState.HitstunTicks`)
/// therefore misses shorter-stun follow-ups: the hitstun clip plays once and
/// never restarts for the rest of the string.
///
/// The production presentation policy uses an authoritative reaction sequence.
/// Repeated and skipped network snapshots must not restart the same reaction.
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class HitstunRehitAnimationTests
{
    // ── Renderer re-hit detection mirrors ──

    /// Current PlayerRenderer logic (pre-fix): restart on first entry or when
    /// HitstunTicks goes UP (>=). Misses re-hits that send it DOWN.
    private static bool LegacyDetectRehit(ActionState lastAnimState, CharacterState last, CharacterState cur)
        => lastAnimState != ActionState.Hitstun || cur.HitstunTicks >= last.HitstunTicks;

    /// Replicates PlayerRenderer.ApplyServerState/UpdateAnimationState's
    /// hitstun path: freeze ticks (HitstopTicks > 0) change nothing
    /// animation-wise, hitstun ticks are compared against the previous applied
    /// state (which still advances through the freeze).
    private sealed class Tracker
    {
        public ActionState LastAnimState = ActionState.Idle;
        public CharacterState LastState = default;
        public int LegacyRestarts;
        public int FixedRestarts;
        public ushort PresentedSequence;

        public void Apply(CharacterState s)
        {
            if (s.HitstopTicks > 0)
            {
                LastState = s; // freeze: no animation change, but _lastState advances
                return;
            }
            if (s.State == ActionState.Hitstun)
            {
                if (LegacyDetectRehit(LastAnimState, LastState, s)) LegacyRestarts++;
                if (HitstunPresentation.ShouldRestart(LastAnimState, PresentedSequence, s))
                {
                    FixedRestarts++;
                    PresentedSequence = s.HitReactionSequence;
                }
                LastAnimState = ActionState.Hitstun;
            }
            else
            {
                LastAnimState = s.State;
            }
            LastState = s;
        }
    }

    [Fact]
    public void NetworkSnapshotsRepeatAndSkipWithoutRestartingButRealRehitRestarts()
    {
        var tracker = new Tracker();
        CharacterState Decode(ushort timer, ushort sequence, ushort freeze = 0)
        {
            var state = new CharacterState { State = ActionState.Hitstun,
                HitstunTicks = timer, HitReactionSequence = sequence, HitstopTicks = freeze };
            var writer = new SnapshotWriter();
            SnapshotCodecs.Write(writer, state);
            var reader = new SnapshotReader(writer.ToArray());
            var restored = SnapshotCodecs.ReadCharacterState(reader);
            Assert.True(reader.AtEnd);
            return restored;
        }

        tracker.Apply(Decode(30, 1));
        for (int i = 0; i < 8; i++) tracker.Apply(Decode(30, 1));
        tracker.Apply(Decode(26, 1));
        tracker.Apply(Decode(20, 1));
        Assert.Equal(1, tracker.FixedRestarts);
        tracker.Apply(Decode(12, 2, freeze: 3));
        Assert.Equal(1, tracker.FixedRestarts);
        tracker.Apply(Decode(12, 2));
        tracker.Apply(Decode(12, 2));
        Assert.Equal(2, tracker.FixedRestarts);
        tracker.Apply(Decode(12, 3)); // Same duration is still a new reaction.
        Assert.Equal(3, tracker.FixedRestarts);
    }

    // ── Fixture: NPC at origin, hits come from spawned hitboxes ──

    private static ServerSimulation SimWithNpcAtOrigin(out CharacterState npc)
    {
        var sim = TestHelpers.MakeSim();
        npc = TestHelpers.NpcState(0f, 0f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        TestHelpers.RegisterNpc(sim, TestHelpers.EngineDef, npc);
        return sim;
    }

    /// One-shot hitbox centered on a state's position (damage 4, diagonal launch, no owner —
    /// receiver-only hitstop). The base knockback is explicit because current ADR-0019 derives
    /// hitstun from launch magnitude; StunTicks is only a nonzero gate.
    private static Hitbox HitAt(CharacterState at, ushort stun, float baseKnockback = 100f)
        => new()
        {
            X = at.PX, Y = at.PY, Z = at.PZ,
            EndX = at.PX, EndY = at.PY, EndZ = at.PZ,
            Radius = 2f, Shape = HitboxShape.Sphere,
            Damage = 4f,
            BaseKnockback = baseKnockback, KnockbackGrowth = 5f, KnockbackAngle = 45,
            StunTicks = stun,
            DurationTicks = 1,
            OwnerId = 0,
        };

    private static void Tick(ServerSimulation sim, Tracker tracker)
    {
        sim.Tick(new() { { 100, default } });
        tracker.Apply(sim.GetState(100));
    }

    /// Tick until the victim is in hitstun with no freeze — an episode boundary
    /// (first entry, or the tick a queued re-hit launch is applied at freeze expiry).
    private static void TickUntilHitstun(ServerSimulation sim, Tracker tracker)
    {
        for (int i = 0; i < 300; i++)
        {
            Tick(sim, tracker);
            var s = sim.GetState(100);
            if (s.State == ActionState.Hitstun && s.HitstopTicks == 0 && s.HitstunTicks > 0)
                return;
        }
        Assert.Fail("victim never entered hitstun");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Single hit: entry restarts once, the countdown must stay quiet
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleHit_RestartsOnce_ThenCountsDownQuietly()
    {
        var sim = SimWithNpcAtOrigin(out var npc);
        sim.Resolver.Spawn(HitAt(npc, stun: 32));

        var tracker = new Tracker();
        bool wasInHitstun = false;
        for (int i = 0; i < 300; i++)
        {
            Tick(sim, tracker);
            var s = sim.GetState(100);
            if (s.State == ActionState.Hitstun) wasInHitstun = true;
            if (wasInHitstun && s.HitstunTicks == 0) break;
        }

        // Exactly one restart (the entry) — the 32-tick countdown must not
        // re-trigger the clip.
        Assert.Equal(1, tracker.FixedRestarts);
        Assert.Equal(1, tracker.LegacyRestarts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Re-hit with SHORTER stun while the victim is mid-hitstun — the bug
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RehitWithShorterStun_MustRestartAnimation()
    {
        var sim = SimWithNpcAtOrigin(out var npc);
        sim.Resolver.Spawn(HitAt(npc, stun: 32)); // big hit
        var tracker = new Tracker();

        // Phase 1: entry + 8 countdown ticks — victim mid-hitstun, not frozen.
        TickUntilHitstun(sim, tracker);
        for (int i = 0; i < 8; i++) Tick(sim, tracker);
        ushort preRehitStun = sim.GetState(100).HitstunTicks;
        Assert.Equal(1, tracker.FixedRestarts);
        Assert.True(preRehitStun > 16, $"need >16 remaining before the re-hit, got {preRehitStun}");
        // Phase 2: a weaker knockback hit lands while the victim is still stunned. Under
        // ADR-0019 this produces the shorter derived hitstun; StunTicks remains the gate.
        sim.Resolver.Spawn(HitAt(sim.GetState(100), stun: 16, baseKnockback: 10f));
        TickUntilHitstun(sim, tracker);
        ushort postRehitStun = sim.GetState(100).HitstunTicks;

        Assert.True(postRehitStun < preRehitStun,
            $"expected the re-hit to lower HitstunTicks ({preRehitStun} → {postRehitStun})");

        // Contract: the victim's clip must restart on the re-hit.
        Assert.Equal(2, tracker.FixedRestarts);
        // Legacy PlayerRenderer condition misses it — this is the reported bug
        // ("hit animation only plays 1 time" when re-hitting).
        Assert.Equal(1, tracker.LegacyRestarts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Re-hit with LONGER stun — control case (legacy already works here)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RehitWithLongerStun_RestartsAnimation()
    {
        var sim = SimWithNpcAtOrigin(out var npc);
        sim.Resolver.Spawn(HitAt(npc, stun: 32));
        var tracker = new Tracker();

        TickUntilHitstun(sim, tracker);
        for (int i = 0; i < 8; i++) Tick(sim, tracker);

        // Stronger follow-up (stun 40 > remaining) — both old and new logic see it.
        sim.Resolver.Spawn(HitAt(sim.GetState(100), stun: 40));
        TickUntilHitstun(sim, tracker);

        Assert.Equal(2, tracker.FixedRestarts);
        Assert.Equal(2, tracker.LegacyRestarts);
    }
}
