using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public readonly record struct CourseFinishResult(ulong EntityId, ulong FinishTicks, ushort BrokenTargets,
    ushort TotalTargets, byte Place, bool Withdrawn, ushort Points = 0);
public sealed record CourseRunResult(CourseFormat Format, bool Completed, ulong ElapsedTicks,
    IReadOnlyList<CourseFinishResult> Players)
{
    public bool Cooperative => Format == CourseFormat.Coop;
    public CourseRunResult(bool cooperative, bool completed, ulong elapsedTicks, IReadOnlyList<CourseFinishResult> players)
        : this(cooperative ? CourseFormat.Coop : CourseFormat.Race, completed, elapsedTicks, players) { }
    public IEnumerable<CourseFinishResult> RankedPlayers => Players.OrderByDescending(p => p.Points)
        .ThenBy(p => p.Place == 0 ? int.MaxValue : p.Place).ThenBy(p => p.EntityId);
    public byte PlaceFor(ulong id) => Format == CourseFormat.Scramble
        ? (byte)(1 + Players.Count(p => p.Points > Players.Single(x => x.EntityId == id).Points))
        : Players.Single(p => p.EntityId == id).Place;
    public static CourseRunResult From(ServerSimulation simulation)
    {
        var state = simulation.OnlineCourse ?? throw new InvalidOperationException("No online course.");
        bool completed = simulation.CoursePlayers.Any(x => x.Place != 0);
        return new(state.Format, completed, state.ElapsedTicks,
            Array.AsReadOnly(simulation.CoursePlayers.Select(x => new CourseFinishResult(x.EntityId,
                state.Cooperative && completed && !x.Withdrawn ? state.ElapsedTicks : x.Place != 0 ? x.Course.ElapsedTicks : 0,
                (ushort)x.Course.BrokenTargets, (ushort)x.Course.TotalTargets,
                state.Cooperative && completed && !x.Withdrawn ? (byte)1 : x.Place, x.Withdrawn, x.Points)).ToArray()));
    }
}
