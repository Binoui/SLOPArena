using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Holding shield is no longer safe against a CPU: it walks in, grabs and throws.</summary>
public sealed class CpuGrabTests
{
    private const ulong Cpu = 100, Target = 1;

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    [InlineData(CharacterClass.Bashie)]
    [InlineData(CharacterClass.Salamander)]
    [InlineData(CharacterClass.Nyx)]
    public void EveryFighterGrabsAndThrowsAShieldingTarget(CharacterClass fighter)
    {
        var arena = BashieShellTests.Floor();
        var cpu = BuiltInContentResolver.Resolve(fighter);
        var target = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(Cpu, cpu.Definition, BashieShellTests.Standing(0f, 0f) with
        {
            EntityId = Cpu, PY = cpu.Definition.CapsuleHeight * 0.5f, JumpsLeft = cpu.Definition.Movement.MaxJumps,
        }, cpu.BakedAnimation);
        sim.RegisterEntity(Target, target.Definition, BashieShellTests.Standing(0f, 4f) with
        {
            EntityId = Target, PY = target.Definition.CapsuleHeight * 0.5f, FacingYaw = MathF.PI,
        }, target.BakedAnimation);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var rng = new Random(11);
        var inputs = new Dictionary<ulong, InputState>();
        var seen = new List<ActionState>();
        bool thrown = false;
        for (int tick = 0; tick < 900 && !thrown; tick++)
        {
            var self = sim.GetState(Cpu);
            policy.ObserveOpponent(memory, sim.GetState(Target), sim.GetDefinition(Target));
            memory.ChooseOpponent(self);
            inputs[Cpu] = policy.Decide(self, cpu.Definition.ForForm(self.Form.Active), rng, memory, arena,
                cpu.BakedAnimation, CpuBehavior.Attack, sim.GetActiveMove(Cpu)?.Slot,
                canSwapCancel: sim.CanSwapCancel(Cpu));
            inputs[Target] = new InputState { ShieldHeld = true };
            sim.Tick(inputs);
            foreach (var hit in sim.LastTickHits)
                if (!hit.Blocked && hit.OwnerEntityId == Cpu)
                {
                    var victim = sim.GetState(hit.TargetEntityId);
                    memory.RecordOpponentHit(hit.AttackSlot, !victim.IsGrounded, victim, victim.HitstunTicks, hit.HitstopTicks);
                }
            var state = sim.GetState(Cpu).State;
            if (state is ActionState.GrabAttempt or ActionState.Grabbing or ActionState.Throwing
                && (seen.Count == 0 || seen[^1] != state))
                seen.Add(state);
            thrown = seen.Contains(ActionState.Throwing) && sim.GetState(Target).State != ActionState.Grabbed
                && sim.GetState(Target).HitstunTicks > 0;
        }
        int attempt = seen.IndexOf(ActionState.GrabAttempt);
        int grabbing = seen.IndexOf(ActionState.Grabbing);
        int throwing = seen.IndexOf(ActionState.Throwing);
        Assert.True(attempt >= 0 && grabbing > attempt && throwing > grabbing && thrown,
            $"{fighter}: grab states {string.Join(" > ", seen)}, partner thrown {thrown}");
    }
}
