namespace SlopArena.Shared.Tests;

/// <summary>
/// Base class for kit regression tests.
/// Runs scenarios and checks their final player state.
/// </summary>
public abstract class KitScenarioTests
{
    /// <summary>
    /// Run a scenario and call its Assert on the final state.
    /// </summary>
    protected static void AssertScenario(KitScenario scenario)
    {
        var (state, _, _, _) = ScenarioRunner.Run(scenario);
        scenario.Assert(state);
    }
}
