using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class MayhemTallWaveTests
{
    private readonly ITestOutputHelper _output;
    public MayhemTallWaveTests(ITestOutputHelper output) => _output = output;
    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void NoSingleJumpTimingClearsTallWave(string fighter)
    {
        foreach (var tier in Enum.GetValues<SpeedTier>())
        {
            var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Jump, tier, count: 2, roster: new[] { fighter }, twist: new(3));
            var plan = MayhemWorld.Plan(sim);
            MayhemDirectorTests.RunTo(sim, plan.PlayStart(episode) - 1);
            var initial = sim.CaptureSnapshot();
            for (uint press = 0; press < 110; press++)
            {
                sim.RestoreSnapshot(initial);
                while (sim.CurrentTick < plan.Deadline(episode))
                {
                    uint tick = sim.CurrentTick + 1;
                    sim.Tick(new() { [1] = new() { Jump = tick == plan.PlayStart(episode) + press, JumpHeld = true } });
                }
                Assert.Equal(MayhemVerdict.Failed, MayhemWorld.View(sim).Contestants[0].Verdict);
            }
            _output.WriteLine($"{fighter} tier {tier} single timing sweep failed 110/110");
        }
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void DoubleJumpClearsTallWaveAndOneJumpFails(string fighter)
    {
        foreach (var tier in Enum.GetValues<SpeedTier>())
        foreach (bool doubleJump in new[] { false, true })
        {
            var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Jump, tier, count: 2, roster: new[] { fighter }, twist: new(3));
            var cpu = new MayhemCpu(0);
            int presses = 0;
            float peak = 0;
            var jumps = new List<string>();
            while (sim.CurrentTick < MayhemWorld.Plan(sim).Deadline(episode))
            {
                var input = cpu.Decide(sim, 1);
                if (input.Jump)
                {
                    presses++;
                    if (!doubleJump && presses > 1) input.Jump = false;
                    else jumps.Add($"{sim.CurrentTick - MayhemWorld.Plan(sim).PlayStart(episode)}");
                }
                sim.Tick(new() { [1] = input });
                var state = sim.GetState(1);
                peak = MathF.Max(peak, state.PY - sim.GetDefinition(1).CapsuleHeight * .5f);
            }
            var row = MayhemWorld.View(sim).Contestants[0];
            _output.WriteLine($"{fighter} tier {tier} double={doubleJump} peak={peak:F3} presses=[{string.Join(",", jumps)}] verdict={row.Verdict} fail={row.Failure?.Tick - MayhemWorld.Plan(sim).PlayStart(episode)}");
            Assert.Equal(doubleJump ? MayhemVerdict.Passed : MayhemVerdict.Failed, row.Verdict);
        }
    }
}
