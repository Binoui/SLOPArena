using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class CourseFinishabilityTests
{
    private readonly ITestOutputHelper _output;
    public CourseFinishabilityTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in CourseFinishability.Routes)
        foreach (var fighter in RosterFighters.All())
            yield return new object[] { route.ArenaKey, fighter };
    }

    /// <summary>
    /// Courses are racing maps for any fighter: each roster fighter finishes every course with jumps, air
    /// dodges and normals only. Add a new course's ordinary route to <see cref="CourseFinishability.Routes"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void UniversalMovementAndAttacksFinish(string arenaKey, CharacterClass fighter)
    {
        var driver = new CourseFinishability(arenaKey, fighter, _output.WriteLine);
        driver.Run(CourseFinishability.Routes.Single(r => r.ArenaKey == arenaKey));
        Assert.Empty(driver.SpecialTargets);
    }

    [Fact]
    public void SoloCourseSnapshotRoundTrips()
    {
        var driver = new CourseFinishability("fightguy_pier", CharacterClass.FightGuy);
        Assert.True(driver.SnapshotRoundTrips());
    }

    public static IEnumerable<object[]> NeoMallShortcuts()
    {
        foreach (var fighter in RosterFighters.All())
        foreach (var shortcuts in new[] { (true, false), (false, true), (true, true) })
            yield return new object[] { fighter, shortcuts.Item1, shortcuts.Item2 };
    }

    [Theory]
    [MemberData(nameof(NeoMallShortcuts))]
    public void NeoMallShortcutsFinishWithPlainJumps(CharacterClass fighter, bool saleBalloon, bool serviceLadder)
    {
        var driver = new CourseFinishability("wibou_mall", fighter, _output.WriteLine);
        driver.Run(CourseFinishability.NeoMallShortcutRoute(saleBalloon, serviceLadder));
        Assert.All(driver.Inputs, input =>
        {
            Assert.Equal((byte)0, input.ActiveSlot);
            Assert.False(input.ShieldPressed);
        });
        Assert.True(driver.UsesSnapshots);
        Assert.Equal((byte)1, driver.Sim.CoursePlayers.Single(p => p.EntityId == 1).Place);
        var solo = new CourseFinishability("wibou_mall", fighter);
        for (int tick = 0; tick < 60 && !solo.State.IsGrounded; tick++) solo.Sim.Tick(new());
        Assert.True(solo.State.IsGrounded);
        foreach (var input in driver.Inputs) solo.Sim.Tick(new() { [1] = input });
        Assert.Equal((byte)0, solo.State.Deaths);
        foreach (var shortcut in new[] { saleBalloon ? 1 : 0, serviceLadder ? 2 : 0 }.Where(s => s != 0))
            AssertShortcutTakesLead(driver, shortcut);
        if (fighter == CharacterClass.FightGuy && saleBalloon && serviceLadder
            && Environment.GetEnvironmentVariable("SLOPARENA_NEO_MALL_CAPTURE_OUTPUT") is { } output)
            ExportShortcutCaptures(driver, output);
        _output.WriteLine($"SHORTCUTS S1={saleBalloon} S2={serviceLadder} {fighter}: {driver.Inputs.Count} ticks, zero falls, replay identical");
    }

    private static void AssertShortcutTakesLead(CourseFinishability driver, int shortcut)
    {
        var takeoff = driver.Landings.Single(p => p.Step == (shortcut == 1 ? new Go(.4f,-30.6f) : new Go(15,16)));
        var rejoin = driver.Landings.Single(p => p.Step == (shortcut == 1 ? new Go(12,-12) : new Go(9.6f,26.7f)));
        var course = driver.Arena.Arcade!.Course!;
        Assert.IsType<CourseRacingLine>(course.RacingLine);
        var states = new Dictionary<ulong, CharacterState>
        {
            [1] = takeoff.State,
            [2] = takeoff.State with { PX = shortcut == 1 ? -6.6f : 4, PZ = shortcut == 1 ? -12 : 15 }
        };
        var scramble = new CourseScramble(course);
        foreach (var pair in states) scramble.Register(pair.Key, pair.Value);
        var players = new[] { new CoursePlayerProgress(1, default, 0, false), new CoursePlayerProgress(2, default, 0, false) };
        scramble.Tick(players, states, Array.Empty<ulong>(), (_, _) => { });
        Assert.Equal(2ul, scramble.State.LeaderEntityId);
        foreach (var frame in driver.FrameStates.Skip(takeoff.InputCount).Take(rejoin.InputCount - takeoff.InputCount))
        {
            states[1] = frame;
            scramble.Tick(players, states, Array.Empty<ulong>(), (id, _) => Assert.NotEqual(1ul, id));
        }
        Assert.Equal(1ul, scramble.State.LeaderEntityId);
        Assert.True(scramble.Progress(1) > scramble.Progress(2));
    }

    private static void ExportShortcutCaptures(CourseFinishability driver, string output)
    {
        Directory.CreateDirectory(output);
        const int settleTicks = 20;
        File.WriteAllText(Path.Combine(output, "shortcuts.expected.json"), JsonSerializer.Serialize(driver.FrameStates.Select((state, index) => new
        {
            tick = settleTicks + index + 1, state.PX, state.PY, state.PZ, state.VX, state.VY, state.VZ,
            state.IsGrounded, state.JumpsLeft, state.State, state.StateTicks, state.JumpHeldTicks
        })));
        File.WriteAllText(Path.Combine(output, "shortcuts.inputs.json"), JsonSerializer.Serialize(
            Enumerable.Repeat(default(InputState), settleTicks).Concat(driver.Inputs).Select(input => new
        {
            ticks = 1, moveX = input.MoveX, moveY = input.MoveY, jumpPressed = input.Jump, jumpHeld = input.JumpHeld,
            facingYaw = input.FacingYaw, aimYaw = input.AimYaw
        })));
        var captures = new[]
        {
            ("s1-approach", (RouteStep)new Go(.4f,-30.6f), 38f),
            ("s1-landing", (RouteStep)new Hop(9,-19.5f,4), 218f),
            ("s2-approach", (RouteStep)new Go(15,16), 45f),
            ("s2-landing", (RouteStep)new Hop(11.5f,21.5f,15), 45f),
        }.Select(c => new { name = c.Item1, tick = settleTicks + driver.Landings.Single(p => p.Step == c.Item2).InputCount,
            camera = new { yaw = c.Item3, pitch = c.Item1 == "s2-landing" ? 45 : 35 } });
        File.WriteAllText(Path.Combine(output, "shortcuts.json"), JsonSerializer.Serialize(new
        {
            version = 1, stage = "wibou_mall", fighter = "fightguy", inputs = "shortcuts.inputs.json", captures,
            expect = new { completed = false, targets = 0, falls = 0, checkpoints = 0 }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void FreshSimulationAndCommittedPrefixAlsoFinish()
    {
        var driver = new CourseFinishability("fightguy_sanctuary", CharacterClass.FightGuy, allowSnapshots: false);
        driver.Run(CourseFinishability.Routes.Single(r => r.ArenaKey == "fightguy_sanctuary"));
        Assert.False(driver.UsesSnapshots);
    }

    [Fact]
    public void FailedHopNamesTheStepFighterAndMeasuredMiss()
    {
        var driver = new CourseFinishability("fightguy_pier", CharacterClass.FightGuy);
        var route = new CourseRoute("fightguy_pier", new RouteStep[] { new Hop(100, 100, 100) });
        string message = Assert.Throws<InvalidOperationException>(() => driver.Run(route)).Message;
        Assert.Contains("fightguy_pier/FightGuy step 0 Hop", message);
        Assert.Contains("position (", message);
        Assert.Contains("closest miss horizontal", message);
        Assert.Contains("vertical shortfall", message);
        Assert.DoesNotContain("Infinity", message);
        Assert.DoesNotContain("NaN", message);
    }
}
