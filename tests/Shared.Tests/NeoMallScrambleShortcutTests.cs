using System.Numerics;
using System.Text.Json;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NeoMallScrambleShortcutTests
{
    [Fact]
    public void CornerHopBypassesCheckpointAndTakesLead()
    {
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(CourseFinishability.Root, "data/arenas/wibou_mall.arena"))!.Value;
        var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var sim = new ServerSimulation(arena, new CourseMatchRule(CourseFormat.Scramble));
        CharacterState Start(ulong id, float x, float z) => new()
        {
            EntityId = id, PX = x, PY = 4 + entry.Definition.CapsuleHeight * .5f, PZ = z, IsGrounded = true,
            JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1
        };
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            sim.RegisterEntity(id, entry.Definition, Start(id, id == 1 ? 12 : 7, -12), entry.BakedAnimation);
            var spawn = arena.SpawnPoints[0]; sim.SetRespawnPosition(id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        }
        sim.BeginOnlineCourse();
        for (int i = 0; i < 8; i++)
        {
            var t = arena.Arcade!.Course!.Targets[i];
            for (int hit = 0; hit < t.Hits; hit++)
            {
                sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = sim.CurrentTick + 10,
                    X = t.Position.X, Y = t.Position.Y, Z = t.Position.Z, Radius = .2f,
                    Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 1 });
                sim.Tick(new());
            }
        }
        sim.SetState(1, Start(1, 12, -12)); sim.SetState(2, Start(2, 7, -12)); sim.Tick(new());
        Assert.Equal(0, sim.CoursePlayers.First().Course.SavedCheckpoint);
        Assert.Equal(-1, sim.CoursePlayers.Last().Course.SavedCheckpoint);
        Assert.Equal(1ul, sim.Scramble!.Value.LeaderEntityId);
        var initial = sim.CaptureSnapshot();
        sim.RestoreSnapshot(initial);
        Assert.Equal(initial.Data, sim.CaptureSnapshot().Data);
        var steps = new List<object>();
        var target = new Vector2(9, -6);
        for (int tick = 0; tick < 120; tick++)
        {
            var state = sim.GetState(2); var delta = target - new Vector2(state.PX, state.PZ);
            var desired = delta * 4f;
            float speed = entry.Definition.Movement.AirSpeedMax;
            if (desired.Length() > speed) desired = Vector2.Normalize(desired) * speed;
            Vector2 stick = state.IsGrounded ? delta.Length() < .2f ? Vector2.Zero : Vector2.Normalize(delta) : desired / speed;
            var input = new InputState { MoveX = stick.X, MoveY = stick.Y, Jump = tick == 0, JumpHeld = tick < 40 };
            steps.Add(new { ticks = 1, moveX = input.MoveX, moveY = input.MoveY, jumpPressed = input.Jump, jumpHeld = input.JumpHeld });
            sim.Tick(new() { [1] = default, [2] = input });
        }
        Assert.True(sim.GetState(2).IsGrounded);
        Assert.Equal((byte)0, sim.GetState(2).Deaths);
        Assert.Equal(-1, sim.CoursePlayers.Last().Course.SavedCheckpoint);
        Assert.Equal(2ul, sim.Scramble.Value.LeaderEntityId);
        Assert.Equal((ushort)0, sim.CoursePlayers.Last().LeashTicks);
        Assert.InRange(sim.GetState(2).PZ, -6.5f, -5.5f);
        if (Environment.GetEnvironmentVariable("SLOPARENA_SCRAMBLE_PROBE_OUTPUT") is { } output)
        {
            output = Path.GetFullPath(output, CourseFinishability.Root);
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "initial.snapshot"), initial.Data);
            File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new Dictionary<string, object>
                { ["1"] = new[] { new { ticks = 120 } }, ["2"] = steps }));
            File.WriteAllText(Path.Combine(output, "expected.json"), JsonSerializer.Serialize(new
                { initialTick = initial.Tick, finalTick = sim.CurrentTick, leader = sim.Scramble.Value.LeaderEntityId,
                    players = sim.CoursePlayers, p2 = sim.GetState(2) }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        }
    }
}
