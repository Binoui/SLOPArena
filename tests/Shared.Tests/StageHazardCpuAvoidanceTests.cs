using SlopArena.Shared.AI;
using SlopArena.Shared.StageHazards;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class StageHazardCpuAvoidanceTests(ITestOutputHelper output)
{
    [Fact]
    public void NormalCpuDoesNotReturnToLiveLaneWhileChasingTargetInsideIt()
        => Run(123, 0, assertNoReturn: true);

    [Fact]
    public void NormalCpuEscapesWhenOnlyItsHurtCapsuleOverlapsLaneEdge()
        => Run(123, 1, assertNoReturn: true);

    [Fact]
    public void NormalCpuAvoidsManySeededSlamsFromInsideAndNearLanes()
    {
        int slams = 0;
        for (uint seed = 0; seed < 48; seed++)
            for (int placement = 0; placement < 3; placement++)
            { Run(seed, placement, assertNoReturn: true); slams++; }
        output.WriteLine($"Seeded slams: {slams}; CPU hazard hits: 0.");
    }

    private static void Run(uint seed, int placement, bool assertNoReturn)
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(StageHazardOffParityProbe.Root,
            "data/arenas/splash_deck.arena"))!.Value;
        var family = new KrakenHazard(arena.HazardAnchors!);
        var action = family.Plan(seed, 0, family.NextLock(seed, 0, 1));
        var def = TestHelpers.FightGuyDef;
        float right = action.Attacks.Max(a => a.Definition.X + a.Definition.Radius);
        float leftEdge = action.Attacks.Min(a => a.Definition.X - a.Definition.Radius);
        float center = (right + leftEdge) / 2;
        // Use the deck-facing edge, not open water beyond an outer lane.
        float offset = placement == 1 ? def.CapsuleRadius * .5f : def.CapsuleRadius + .3f;
        float x = placement == 0 ? center : center > 0 ? leftEdge - offset : right + offset;
        var sim = new ServerSimulation(arena, NoWinMatchRule.ForPractice(StageHazardMode.On), stageHazardSeed: seed);
        CharacterState State(ulong id, float px, float z) => new()
        {
            EntityId = id, PX = px, PY = def.CapsuleHeight / 2, PZ = z,
            IsGrounded = true, State = ActionState.Idle, MatchState = MatchState.Playing,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1
        };
        sim.RegisterEntity(1, def, State(1, 0, 7));
        sim.RegisterEntity(2, def, State(2, center, -7));
        var inputs = new Dictionary<ulong, InputState>();
        while (sim.CurrentTick < action.Window.WarningTick) sim.Tick(inputs);
        sim.SetState(1, State(1, x, 7));
        bool Overlaps(CharacterState state) => ServerSimulation.BuildEntitiesFromState(state, def, null!, "idle", 0, 1)
            .Any(body => action.Attacks.Any(a => SpellResolver.CapsuleCollision(a.Definition, body,
                out _, out _, out _, out _, out _, out _, out _)));
        if (placement == 1)
        { Assert.True(x > right || x < leftEdge); Assert.True(Overlaps(sim.GetState(1)), "Edge case must overlap with centre outside."); }
        var memory = new BotMemory { Difficulty = CpuDifficulty.Normal };
        var policy = new HeuristicBotPolicy(); var rng = new Random(42);
        bool escaped = false;
        uint end = action.Attack.EndTick + (uint)BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks;
        while (sim.CurrentTick < end)
        {
            memory.ObserveStageHazards(sim.StageHazards);
            policy.ObserveOpponent(memory, sim.GetState(2), def);
            var self = sim.GetState(1); memory.ChooseOpponent(self);
            inputs[1] = policy.Decide(self, def, rng, memory, arena);
            sim.Tick(inputs);
            bool overlaps = Overlaps(sim.GetState(1));
            var post = sim.GetState(1);
            float left = action.Attacks.Min(a => a.Definition.X - a.Definition.Radius);
            if (!overlaps && (post.PX < left - def.CapsuleRadius || post.PX > right + def.CapsuleRadius)
                && memory.VisibleHazards?.Cues.Count > 0) escaped = true;
            if (assertNoReturn && escaped && sim.CurrentTick < action.Attack.EndTick)
                Assert.False(overlaps, $"seed={seed}, placement={placement}, re-entered at {sim.CurrentTick}");
            Assert.DoesNotContain(sim.LastTickHits, hit => hit.OwnerEntityId == StageHazardOwner.EntityId && hit.TargetEntityId == 1 && !hit.Blocked);
            if (sim.CurrentTick == action.Window.ImpactTick) Assert.False(overlaps, $"seed={seed}, placement={placement}, still inside at impact");
        }
        Assert.True(escaped); Assert.Equal(0, sim.GetState(1).DamagePercent);
    }
}
