using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The CPU walks the stages the engine lets it walk. Each case drives the real simulation with the
/// policy the way Training does (observe, decide, tick, feed back hits) against an idle target.
/// </summary>
public sealed class CpuMovementTests
{
    private const ulong Cpu = 100, Target = 1;

    internal static ArenaDefinition Stage(string key)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", key + ".arena"));
        Assert.True(arena.HasValue, key + " must ship its baked arena.");
        return arena.Value;
    }

    /// <summary>Closed boxes with the kill plane well below them, so a fall reads as a fall before it reads as a KO.</summary>
    private static ArenaDefinition Boxes(params (float, float, float, float, float, float)[] boxes)
    {
        var arena = CourseShellArena.FromBoxes(boxes, new SpawnPoint());
        arena.KillHeight = -10f;
        return arena;
    }

    /// <summary>A floor rising 0.2 m per metre toward +X (normal y 0.98) in 5 m strips, so a walk crosses triangles.</summary>
    private static ArenaDefinition TiltedFloor()
    {
        var triangles = new List<CollisionTriangle>();
        for (float x0 = -10f; x0 < 10f; x0 += 5f)
        {
            float x1 = x0 + 5f, y0 = 0.2f * (x0 + 10f), y1 = 0.2f * (x1 + 10f);
            triangles.Add(new CollisionTriangle { AX = x0, AY = y0, AZ = -10f, BX = x0, BY = y0, BZ = 10f, CX = x1, CY = y1, CZ = -10f });
            triangles.Add(new CollisionTriangle { AX = x1, AY = y1, AZ = 10f, BX = x1, BY = y1, BZ = -10f, CX = x0, CY = y0, CZ = 10f });
        }
        var arena = new ArenaDefinition
        {
            Name = "tilted", KillHeight = -10f, KillTop = 30f,
            KillMinX = -30f, KillMaxX = 30f, KillMinZ = -30f, KillMaxZ = 30f,
            MinX = -10f, MaxX = 10f, MinZ = -10f, MaxZ = 10f,
            SpawnPoints = new[] { new SpawnPoint() },
            CollisionTriangles = triangles.ToArray(),
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    internal sealed class Duel
    {
        public readonly ServerSimulation Sim;
        private readonly ArenaDefinition _arena;
        private readonly MatchContentEntry _cpu;
        private readonly BakedAnimationData? _targetBaked;
        private readonly HeuristicBotPolicy _policy = new();
        private readonly BotMemory _memory;
        private readonly Random _rng;
        private readonly Dictionary<ulong, InputState> _inputs = new();

        /// <summary>Unblocked hits this CPU landed on its target.</summary>
        public int Hits { get; private set; }

        public Duel(ArenaDefinition arena, CharacterClass cpu, float cpuX, float cpuZ, float targetX, float targetZ,
            float? cpuFloor = null, float? targetFloor = null, CpuDifficulty difficulty = CpuDifficulty.Normal, int seed = 3, bool observeBakedTarget = false)
        {
            _arena = arena;
            _cpu = BuiltInContentResolver.Resolve(cpu);
            var target = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
            _targetBaked = observeBakedTarget ? target.BakedAnimation : null;
            Sim = new ServerSimulation(arena);
            Sim.RegisterEntity(Cpu, _cpu.Definition, Standing(Cpu, _cpu.Definition, arena, cpuX, cpuZ, cpuFloor), _cpu.BakedAnimation);
            Sim.RegisterEntity(Target, target.Definition, Standing(Target, target.Definition, arena, targetX, targetZ, targetFloor),
                target.BakedAnimation);
            _memory = new BotMemory { Difficulty = difficulty };
            _rng = new Random(seed);
        }

        private static CharacterState Standing(ulong id, CharacterDefinition def, in ArenaDefinition arena, float x, float z, float? floor)
        {
            float y = floor ?? arena.Heightmap.Sample(x, z);
            return new CharacterState
            {
                EntityId = id, PX = x, PY = y + def.CapsuleHeight * 0.5f, PZ = z,
                State = ActionState.Idle, IsGrounded = true, JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
                MatchState = MatchState.Playing,
            };
        }

        public int WalkProbes => _memory.WalkProbesThisTick;
        public CharacterState Self => Sim.GetState(Cpu);
        public CharacterState Opponent => Sim.GetState(Target);
        public float Separation
        {
            get
            {
                var self = Self;
                var target = Opponent;
                return MathF.Sqrt((target.PX - self.PX) * (target.PX - self.PX) + (target.PZ - self.PZ) * (target.PZ - self.PZ));
            }
        }
        public float Feet => Self.PY - _cpu.Definition.CapsuleHeight * 0.5f;

        public void Settle(int ticks)
        {
            _inputs[Cpu] = _inputs[Target] = default;
            for (int tick = 0; tick < ticks; tick++) Sim.Tick(_inputs);
        }

        public InputState Step(InputState targetInput = default)
        {
            var self = Self;
            _policy.ObserveOpponent(_memory, Opponent, Sim.GetDefinition(Target), _targetBaked);
            var input = _policy.Decide(self, _cpu.Definition.ForForm(self.Form.Active), _rng, _memory, _arena,
                _cpu.BakedAnimation, CpuBehavior.Attack, Sim.GetActiveMove(Cpu)?.Slot, canSwapCancel: Sim.CanSwapCancel(Cpu));
            _inputs[Cpu] = input;
            _inputs[Target] = targetInput;
            Sim.Tick(_inputs);
            foreach (var hit in Sim.LastTickHits)
            {
                if (hit.OwnerEntityId != Cpu || hit.TargetEntityId != Target) continue;
                if (!hit.Blocked) Hits++;
                var after = Opponent;
                _memory.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, after, hit.HitstopTicks);
            }
            return input;
        }
    }

    [Theory]
    [InlineData(CpuDifficulty.Normal, 169, 12.5f, -5.4f)]
    [InlineData(CpuDifficulty.Hard, 169, 12.5f, -5.4f)]
    [InlineData(CpuDifficulty.Normal, 217, 22.5f, -9.4f)]
    [InlineData(CpuDifficulty.Hard, 217, 22.5f, -9.4f)]
    public void HamsterHabitatCpuDoesNotMistakeThePitRimForALanding(CpuDifficulty difficulty,
        int seed, float x, float z)
    {
        var duel = new Duel(Stage("hamster_habitat"), CharacterClass.FightGuy, x, z, -27.3f, 7.15f,
            targetFloor: 0f, difficulty: difficulty, seed: seed);
        duel.Settle(6);
        for (int tick = 0; tick < 360; tick++)
        {
            duel.Step();
            Assert.True(duel.Self.Deaths == 0,
                $"fell into the pit at tick {tick}, from ({x}, {z}), seed {seed}, {difficulty}");
        }
    }

    [Theory]
    [InlineData("hamster_habitat", CpuDifficulty.Normal, 249, 28.5f, -5.3999996f, -27.3f, 7.15f)]
    [InlineData("industrial_rooftop", CpuDifficulty.Normal, 99, 0f, 20f, 12f, -5f)]
    [InlineData("industrial_rooftop", CpuDifficulty.Normal, 123, 4f, 16f, 12f, -5f)]
    [InlineData("log_off", CpuDifficulty.Normal, 171, 16.5f, -2.88f, 7f, -5f)]
    [InlineData("log_off", CpuDifficulty.Hard, 1, -17.5f, -2.88f, -7f, 5f)]
    public void CpuFinishesItsClimbInsteadOfCancellingIntoTheVoid(string stage, CpuDifficulty difficulty,
        int seed, float x, float z, float targetX, float targetZ)
    {
        var duel = new Duel(Stage(stage), CharacterClass.FightGuy, x, z, targetX, targetZ,
            targetFloor: 0f, difficulty: difficulty, seed: seed);
        duel.Settle(6);
        for (int tick = 0; tick < 360; tick++)
        {
            duel.Step();
            Assert.True(duel.Self.Deaths == 0, $"{stage}, {difficulty}, seed {seed}, death at tick {tick}");
        }
    }

    [Theory]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void HamsterLateralChaseRecoversInsteadOfAttackingAboveAFalseFloor(CpuDifficulty difficulty)
    {
        var arena = Stage("hamster_habitat");
        float cpuFloor = arena.Heightmap.Sample(0f, -6f), targetFloor = arena.Heightmap.Sample(0f, 0f);
        for (int seed = 1; seed <= 32; seed++)
        {
            var duel = new Duel(arena, CharacterClass.FightGuy, 0f, -6f, 0f, 0f,
                cpuFloor: cpuFloor > float.MinValue ? cpuFloor : 0f,
                targetFloor: targetFloor > float.MinValue ? targetFloor : 0f,
                difficulty: difficulty, seed: seed, observeBakedTarget: true);
            var targetStart = duel.Opponent with { FacingYaw = MathF.PI };
            duel.Sim.SetState(Target, targetStart);
            duel.Settle(6);
            for (int tick = 0; tick < 900; tick++)
            {
                duel.Step(new InputState { MoveX = tick / 47 % 2 == 0 ? 1f : -1f, FaceToCamera = true, AimYaw = 18000 });
                Assert.True(duel.Self.Deaths == 0, $"{difficulty} seed {seed} died at tick {tick}");
                if (duel.Opponent.Deaths > 0 || duel.Opponent.DamagePercent > 80)
                    duel.Sim.SetState(Target, targetStart);
            }
        }
    }

    [Fact]
    public void DenseStageWalkSearchHasAFixedPerTickProbeBudget()
    {
        var arena = Stage("hamster_habitat");
        for (float x = 8.5f; x <= 16.5f; x += 2f)
        {
            var duel = new Duel(arena, CharacterClass.FightGuy, x, -5.4f, -27.3f, 7.15f,
                targetFloor: 0f, difficulty: CpuDifficulty.Hard);
            duel.Settle(6);
            for (int tick = 0; tick < 240; tick++)
            {
                duel.Step();
                Assert.True(duel.WalkProbes <= 2,
                    $"x={x}, tick={tick}, used {duel.WalkProbes} probes in one CPU tick");
            }
        }
    }

    [Fact]
    public void LogOffRimCpuReachesAPlayerOnTheStumpTop()
    {
        var duel = new Duel(Stage("log_off"), CharacterClass.FightGuy, -14.5f, 1.4f, 0.3f, -1.6f);
        float closest = duel.Separation;
        for (int tick = 0; tick < 300; tick++)
        {
            duel.Step();
            closest = MathF.Min(closest, duel.Separation);
            Assert.Equal(0, duel.Self.Deaths);
        }
        Assert.True(closest <= 3f, $"closest approach {closest:F2} m from ({duel.Self.PX:F2}, {duel.Self.PZ:F2})");
    }

    [Fact]
    public void CpuWalksUpATiltedFloorToItsTarget()
    {
        var duel = new Duel(TiltedFloor(), CharacterClass.FightGuy, -6f, 0f, 4f, 0f, cpuFloor: 0.8f, targetFloor: 2.8f);
        float closest = duel.Separation;
        for (int tick = 0; tick < 300; tick++)
        {
            duel.Step();
            closest = MathF.Min(closest, duel.Separation);
            Assert.Equal(0, duel.Self.Deaths);
        }
        Assert.True(closest <= 3f, $"closest approach {closest:F2} m from ({duel.Self.PX:F2}, {duel.Self.PZ:F2})");
    }

    [Fact]
    public void SlopPitCpuStepsDownFromThePlatformToATargetOnTheFloor()
    {
        var duel = new Duel(Stage("slop_pit"), CharacterClass.FightGuy, 5.6f, -5.5f, 10.1f, -11.9f);
        float start = duel.Separation;
        bool arrived = false;
        for (int tick = 0; tick < 300 && !arrived; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
            arrived = duel.Self.IsGrounded && duel.Feet < 0.6f && duel.Separation <= start * 0.5f;
        }
        Assert.True(arrived, $"after 300 ticks at ({duel.Self.PX:F2}, {duel.Feet:F2}, {duel.Self.PZ:F2}), {duel.Separation:F2} m from the target");
    }

    [Fact]
    public void CpuDoesNotWalkIntoAGapBetweenTwoFloors()
    {
        var arena = Boxes((-10f, -1f, -5f, -1.5f, 0f, 5f), (1.5f, -1f, -5f, 10f, 0f, 5f));
        var duel = new Duel(arena, CharacterClass.FightGuy, -5f, 0f, 5f, 0f);
        for (int tick = 0; tick < 300; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
            Assert.True(duel.Self.PX < -1.2f, $"tick {tick}: walked into the gap at x {duel.Self.PX:F2}");
        }
    }

    [Fact]
    public void CpuClimbsOntoABlockItsTargetStandsOn()
    {
        var arena = Boxes((-10f, -1f, -6f, 10f, 0f, 6f), (2f, 0f, -3f, 8f, 1.2f, 3f));
        var duel = new Duel(arena, CharacterClass.FightGuy, -4f, 0f, 6f, 0f);
        bool onTop = false;
        for (int tick = 0; tick < 300 && !onTop; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
            onTop = duel.Self.IsGrounded && duel.Feet > 1.1f;
        }
        Assert.True(onTop, $"after 300 ticks at ({duel.Self.PX:F2}, {duel.Feet:F2}, {duel.Self.PZ:F2})");
    }

    [Fact]
    public void SlopPitCpuClimbsOntoThePlatformItsTargetStandsOn()
    {
        var duel = new Duel(Stage("slop_pit"), CharacterClass.FightGuy, 3f, -12f, 3f, -4f);
        bool onTop = false;
        for (int tick = 0; tick < 300 && !onTop; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
            onTop = duel.Self.IsGrounded && duel.Feet > 1f;
        }
        Assert.True(onTop, $"after 300 ticks at ({duel.Self.PX:F2}, {duel.Feet:F2}, {duel.Self.PZ:F2})");
    }

    [Fact]
    public void TrainingCpuApproachesAndHitsATargetNearTheEdge()
    {
        var duel = new Duel(Stage("training"), CharacterClass.FightGuy, 4f, 0f, 13f, 0f);
        for (int tick = 0; tick < 300 && duel.Hits == 0; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
        }
        Assert.True(duel.Hits > 0, $"no hit in 300 ticks; ended {duel.Separation:F2} m away at ({duel.Self.PX:F2}, {duel.Self.PZ:F2})");
    }

    [Fact]
    public void CpuGoesAroundAHoleBetweenItAndItsTarget()
    {
        var arena = Boxes((-10f, -1f, -10f, 10f, 0f, -1.5f), (-10f, -1f, 1.5f, 10f, 0f, 10f),
            (-10f, -1f, -1.5f, -1.5f, 0f, 1.5f), (1.5f, -1f, -1.5f, 10f, 0f, 1.5f));
        var duel = new Duel(arena, CharacterClass.FightGuy, -5f, 0f, 5f, 0f);
        for (int tick = 0; tick < 300; tick++)
        {
            duel.Step();
            Assert.Equal(0, duel.Self.Deaths);
            Assert.True(duel.Feet > -0.3f, $"tick {tick}: fell into the hole at ({duel.Self.PX:F2}, {duel.Feet:F2}, {duel.Self.PZ:F2})");
        }
    }
}
