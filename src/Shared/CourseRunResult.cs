using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public readonly record struct CourseFinishResult(ulong EntityId, ulong FinishTicks, ushort BrokenTargets,
    ushort TotalTargets, byte Place, bool Withdrawn);
public sealed record CourseRunResult(bool Cooperative, bool Completed, ulong ElapsedTicks,
    IReadOnlyList<CourseFinishResult> Players)
{
    public static CourseRunResult From(ServerSimulation simulation)
    {
        var state = simulation.OnlineCourse ?? throw new InvalidOperationException("No online course.");
        bool completed = simulation.CoursePlayers.Any(x => x.Place != 0);
        return new(state.Cooperative, completed, state.ElapsedTicks,
            Array.AsReadOnly(simulation.CoursePlayers.Select(x => new CourseFinishResult(x.EntityId,
                state.Cooperative && completed && !x.Withdrawn ? state.ElapsedTicks : x.Place != 0 ? x.Course.ElapsedTicks : 0,
                (ushort)x.Course.BrokenTargets, (ushort)x.Course.TotalTargets,
                state.Cooperative && completed && !x.Withdrawn ? (byte)1 : x.Place, x.Withdrawn)).ToArray()));
    }
}
