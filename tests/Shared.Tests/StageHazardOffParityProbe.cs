using System.Security.Cryptography;
using System.Text.Json;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public class StageHazardOffParityProbe
{
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal static string[] Fight(ArenaDefinition arena)
    {
        var def = TestHelpers.FightGuyDef;
        var baked = TestHelpers.LoadBakedData(def);
        var sim = new ServerSimulation(arena, new StockMatchRule(99));
        var memories = new[] { new BotMemory(), new BotMemory() };
        var policy = new HeuristicBotPolicy(); var rng = new Random(6201);
        for (ulong id = 1; id <= 2; id++)
        {
            var p = arena.SpawnPoints[(int)(id - 1) % arena.SpawnPoints.Length];
            sim.RegisterEntity(id, def, new CharacterState { EntityId = id, PX = p.X, PY = p.Y + def.CapsuleHeight / 2,
                PZ = p.Z, IsGrounded = true, State = ActionState.Idle, MatchState = MatchState.Playing,
                JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1 }, baked);
        }
        var hashes = new string[1200]; var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < hashes.Length; tick++)
        {
            for (ulong id = 1; id <= 2; id++)
            {
                var m = memories[id - 1]; var self = sim.GetState(id);
                policy.ObserveOpponent(m, sim.GetState(3 - id), def, baked, sim.GetActiveMove(3 - id)?.Slot);
                m.ChooseOpponent(self);
                inputs[id] = policy.Decide(self, def, rng, m, arena, baked, activeMove: sim.GetActiveMove(id)?.Slot,
                    canSwapCancel: sim.CanSwapCancel(id));
            }
            sim.Tick(inputs);
            hashes[tick] = Convert.ToHexString(SHA256.HashData(sim.CaptureSnapshot().Data.AsSpan(4)));
        }
        return hashes;
    }
    [Fact]
    public void CaptureOrCompareBaseline()
    {
        if (Environment.GetEnvironmentVariable("STAGE_HAZARD_CAPTURE_BASELINE") != "1"
            && Environment.GetEnvironmentVariable("STAGE_HAZARD_VERIFY_BASELINE") != "1") return;
        string path = Environment.GetEnvironmentVariable("STAGE_HAZARD_BASELINE_PATH")
            ?? Path.Combine(Root, "tmp/stage-hazards/impl/off-parity-baseline.json");
        var actual = new Dictionary<string, string[]>();
        foreach (var name in new[] { "splash_deck", "last_stop", "after_hours", "slop_pit" })
            actual[name] = Fight(ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, $"data/arenas/{name}.arena"))!.Value);
        if (Environment.GetEnvironmentVariable("STAGE_HAZARD_CAPTURE_BASELINE") == "1")
            File.WriteAllText(path, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            var expected = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))!;
            foreach (var entry in actual) Assert.Equal(expected[entry.Key], entry.Value);
        }
    }
}
