using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// World Snapshot completeness (ADR-0039, plan phase 1). A simulation restored from a
/// snapshot must behave exactly like the simulation that produced it: any runtime field the
/// snapshot misses makes the restored copy diverge within a few ticks, which these fights
/// detect byte for byte.
/// </summary>
public class WorldSnapshotTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static IEnumerable<object[]> Fights()
    {
        yield return new object[] { "slop_pit", new[] { CharacterClass.FightGuy, CharacterClass.Manki }, 11 };
        yield return new object[] { "slop_pit", new[] { CharacterClass.Wibou, CharacterClass.Bonk }, 12 };
        yield return new object[] { "table_manners", new[] { CharacterClass.Manki, CharacterClass.Wibou }, 13 };
        yield return new object[] { "table_manners", new[] { CharacterClass.Bonk, CharacterClass.FightGuy }, 14 };
        yield return new object[] { "last_stop", new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk }, 15 };
        yield return new object[] { "slop_pit", new[] { CharacterClass.Manki, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.FightGuy }, 16 };
    }

    [Theory]
    [MemberData(nameof(Fights))]
    public void FreshSimulationsRestoredMidFightMatchTheContinuousOneEveryTick(
        string arenaName, CharacterClass[] roster, int seed)
    {
        var arena = LoadArena(arenaName);
        var continuous = NewMatch(arena, roster, reverseRegistration: false);
        var uncaptured = NewMatch(arena, roster, reverseRegistration: false);
        var restored = NewMatch(arena, roster, reverseRegistration: true);
        restored.RestoreSnapshot(continuous.CaptureSnapshot());

        var ids = Enumerable.Range(1, roster.Length).Select(i => (ulong)i).ToArray();
        var driver = new FightDriver(seed, ids);
        var rng = new Random(seed * 7919);
        var inputs = new Dictionary<ulong, InputState>();
        var coverage = new Coverage();
        int nextRestore = rng.Next(1, 41);
        const int Ticks = 3000;
        for (int tick = 1; tick <= Ticks; tick++)
        {
            driver.Next(continuous, inputs);
            continuous.Tick(new Dictionary<ulong, InputState>(inputs));
            uncaptured.Tick(new Dictionary<ulong, InputState>(inputs));
            restored.Tick(new Dictionary<ulong, InputState>(inputs));
            coverage.Observe(continuous, ids);

            var expected = continuous.CaptureSnapshot();
            var actual = restored.CaptureSnapshot();
            if (!expected.ContentEquals(actual))
                Assert.Fail(DescribeDivergence(tick, continuous, restored, ids, expected, actual));

            if (--nextRestore == 0)
            {
                // A brand-new simulation, registered in reverse order, takes over from the snapshot.
                restored = NewMatch(arena, roster, reverseRegistration: true);
                restored.RestoreSnapshot(expected);
                coverage.Restores++;
                nextRestore = rng.Next(1, 41);
            }
        }

        Assert.True(uncaptured.CaptureSnapshot().ContentEquals(continuous.CaptureSnapshot()),
            "Capturing a snapshot every tick changed the simulation.");
        coverage.AssertMeaningful();
    }

    [Fact]
    public void RegistrationOrderDoesNotChangeTheSimulation()
    {
        var arena = LoadArena("slop_pit");
        var roster = new[] { CharacterClass.FightGuy, CharacterClass.Wibou, CharacterClass.Bonk };
        var ascending = NewMatch(arena, roster, reverseRegistration: false);
        var descending = NewMatch(arena, roster, reverseRegistration: true);
        var ids = new ulong[] { 1, 2, 3 };
        var driver = new FightDriver(21, ids);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 900; tick++)
        {
            driver.Next(ascending, inputs);
            ascending.Tick(new Dictionary<ulong, InputState>(inputs));
            descending.Tick(new Dictionary<ulong, InputState>(inputs));
        }
        Assert.True(ascending.CaptureSnapshot().ContentEquals(descending.CaptureSnapshot()));
    }

    [Fact]
    public void MalformedSnapshotLeavesTheSimulationUntouched()
    {
        var arena = LoadArena("slop_pit");
        var roster = new[] { CharacterClass.FightGuy, CharacterClass.Manki };
        var sim = NewMatch(arena, roster, reverseRegistration: false);
        var driver = new FightDriver(5, new ulong[] { 1, 2 });
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 300; tick++)
        {
            driver.Next(sim, inputs);
            sim.Tick(inputs);
        }
        var before = sim.CaptureSnapshot();
        var other = NewMatch(arena, roster, reverseRegistration: false);
        for (int tick = 0; tick < 120; tick++)
        {
            driver.Next(other, inputs);
            other.Tick(inputs);
        }
        var data = other.CaptureSnapshot().Data;

        var truncated = new WorldSnapshot(data.AsSpan(0, data.Length - 3).ToArray());
        Assert.Throws<InvalidDataException>(() => sim.RestoreSnapshot(truncated));
        var trailing = new WorldSnapshot(data.Concat(new byte[] { 1 }).ToArray());
        Assert.Throws<InvalidDataException>(() => sim.RestoreSnapshot(trailing));
        Assert.True(sim.CaptureSnapshot().ContentEquals(before));
    }

    [Fact]
    public void SnapshotOfDifferentFightersIsRejected()
    {
        var arena = LoadArena("slop_pit");
        var two = NewMatch(arena, new[] { CharacterClass.FightGuy, CharacterClass.Manki }, false);
        var three = NewMatch(arena, new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Bonk }, false);
        Assert.Throws<InvalidOperationException>(() => two.RestoreSnapshot(three.CaptureSnapshot()));
    }

    [Fact]
    public void UnsupportedFormatVersionIsRejected()
    {
        var sim = NewMatch(LoadArena("slop_pit"), new[] { CharacterClass.Bonk, CharacterClass.Wibou }, false);
        var data = sim.CaptureSnapshot().Data;
        data[0] ^= 0x7F;
        Assert.Throws<InvalidDataException>(() => new WorldSnapshot(data));
    }

    internal static ArenaDefinition LoadArena(string name)
        => ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data", "arenas", name + ".arena"))
            ?? throw new InvalidOperationException($"Arena {name} could not be loaded.");

    /// <summary>A PvP simulation with fighters 1..N on the arena's spawn points, as the GameHost sets it up.</summary>
    internal static ServerSimulation NewMatch(ArenaDefinition arena, CharacterClass[] roster, bool reverseRegistration)
    {
        var sim = new ServerSimulation(arena, new StockMatchRule(250));
        var order = Enumerable.Range(0, roster.Length).ToList();
        if (reverseRegistration) order.Reverse();
        foreach (int slot in order)
        {
            var content = BuiltInContentResolver.Resolve(roster[slot]);
            var spawn = arena.SpawnPoints[slot % arena.SpawnPoints.Length];
            var state = new CharacterState
            {
                PX = spawn.X + (slot / arena.SpawnPoints.Length) * 1.5f,
                PY = spawn.Y,
                PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = content.Definition.Movement.MaxJumps,
                AirDodgesLeft = 1,
            };
            sim.RegisterEntity((ulong)(slot + 1), content.Definition, state, content.BakedAnimation);
        }
        return sim;
    }

    private static string DescribeDivergence(int tick, ServerSimulation expected, ServerSimulation actual,
        ulong[] ids, WorldSnapshot expectedSnapshot, WorldSnapshot actualSnapshot)
    {
        var lines = new List<string> { $"Restored simulation diverged at tick {tick}." };
        foreach (ulong id in ids)
        {
            var a = expected.GetState(id);
            var b = actual.GetState(id);
            foreach (var field in typeof(CharacterState).GetFields())
            {
                object? x = field.GetValue(a), y = field.GetValue(b);
                if (!Equals(x, y))
                    lines.Add($"  fighter {id} {field.Name}: continuous={x} restored={y} (state {a.State}, ability {expected.GetActiveAbility(id)?.GetType().Name ?? "none"})");
            }
        }
        var x1 = expectedSnapshot.Data;
        var y1 = actualSnapshot.Data;
        int first = 0;
        while (first < Math.Min(x1.Length, y1.Length) && x1[first] == y1[first]) first++;
        lines.Add($"  snapshot bytes {x1.Length} vs {y1.Length}, first difference at {first}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Plausible fighting inputs: chase, attack with every slot, aim holds, jumps,
    /// shields, air dodges, grabs, crouches and target lock.</summary>
    internal sealed class FightDriver
    {
        private static readonly byte[] Slots =
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        };
        private readonly Random _rng;
        private readonly ulong[] _ids;
        private readonly int[] _aimHold, _shieldHold, _jumpHold;

        public FightDriver(int seed, ulong[] ids)
        {
            _rng = new Random(seed);
            _ids = ids;
            _aimHold = new int[ids.Length];
            _shieldHold = new int[ids.Length];
            _jumpHold = new int[ids.Length];
        }

        public void Next(ServerSimulation sim, Dictionary<ulong, InputState> inputs) => Next(sim.GetState, inputs);

        /// <summary>Inputs from the fighters' states as one viewer sees them (a client's prediction).</summary>
        public void Next(Func<ulong, CharacterState> stateOf, Dictionary<ulong, InputState> inputs)
        {
            inputs.Clear();
            for (int i = 0; i < _ids.Length; i++)
            {
                var me = stateOf(_ids[i]);
                ulong targetId = 0;
                float best = float.MaxValue, dx = 0f, dz = 0f;
                foreach (ulong other in _ids)
                {
                    if (other == _ids[i]) continue;
                    var them = stateOf(other);
                    float ox = them.PX - me.PX, oz = them.PZ - me.PZ;
                    float d = ox * ox + oz * oz;
                    if (d < best) { best = d; targetId = other; dx = ox; dz = oz; }
                }
                float distance = MathF.Sqrt(best);
                var input = new InputState { TargetEntityId = (byte)targetId };
                if (distance > 1.3f)
                {
                    input.MoveX = dx / distance;
                    input.MoveY = dz / distance;
                }
                else if (_rng.Next(3) == 0)
                {
                    input.MoveX = (float)(_rng.NextDouble() * 2 - 1);
                    input.MoveY = (float)(_rng.NextDouble() * 2 - 1);
                }
                short yaw = (short)Math.Clamp(MathF.Atan2(dx, dz) * 180f / MathF.PI * 100f, -18000f, 18000f);
                input.AimYaw = yaw;
                input.FacingYaw = yaw;
                input.AimPitch = (short)_rng.Next(-1500, 1500);
                input.AimDistance = (ushort)Math.Min(6500f, distance * 100f);

                if (_aimHold[i] > 0)
                {
                    _aimHold[i]--;
                    input.IsAiming = _aimHold[i] > 0;
                }
                else if (_rng.Next(distance < 3f ? 3 : 10) == 0)
                {
                    byte slot = Slots[_rng.Next(Slots.Length)];
                    input.ActiveSlot = slot;
                    if (slot is AbilitySlots.A or AbilitySlots.E or AbilitySlots.R or AbilitySlots.F && _rng.Next(2) == 0)
                    {
                        _aimHold[i] = _rng.Next(6, 45);
                        input.IsAiming = true;
                    }
                }
                if (_rng.Next(35) == 0)
                {
                    input.Jump = true;
                    _jumpHold[i] = _rng.Next(1, 14);
                }
                if (_jumpHold[i] > 0)
                {
                    input.JumpHeld = true;
                    _jumpHold[i]--;
                }
                if (_shieldHold[i] == 0 && _rng.Next(60) == 0)
                    _shieldHold[i] = _rng.Next(4, 32);
                if (_shieldHold[i] > 0)
                {
                    input.ShieldHeld = true;
                    _shieldHold[i]--;
                }
                if (!me.IsGrounded && _rng.Next(50) == 0)
                    input.ShieldPressed = true;
                if (_rng.Next(70) == 0)
                    input.GrabPressed = true;
                if (_rng.Next(45) == 0)
                {
                    input.Down = true;
                    input.DownPressed = true;
                }
                if (_rng.Next(400) == 0)
                    input.ToggleLock = true;
                inputs[_ids[i]] = input;
            }
        }
    }

    /// <summary>Guards against a vacuous fight: the restore points must land during the
    /// states that hold runtime the snapshot has to carry. Thresholds sit well below what
    /// every fight above produces.</summary>
    private sealed class Coverage
    {
        public int Restores;
        private int _abilityTicks, _capabilityTicks, _hitstopTicks, _hitboxTicks, _deaths, _hits;
        private readonly Dictionary<ActionState, int> _states = new();

        public void Observe(ServerSimulation sim, ulong[] ids)
        {
            foreach (ulong id in ids)
            {
                var state = sim.GetState(id);
                _states[state.State] = _states.GetValueOrDefault(state.State) + 1;
                if (sim.GetActiveAbility(id) is { } ability)
                {
                    _abilityTicks++;
                    if (ability is Abilities.CookedTimelineAbility { RunningCapabilities: > 0 })
                        _capabilityTicks++;
                }
                if (state.HitstopTicks > 0) _hitstopTicks++;
            }
            if (sim.Resolver.GetActiveHitboxes().Count > 0) _hitboxTicks++;
            _deaths += sim.LastTickDeaths.Count;
            _hits += sim.LastTickHits.Count;
        }

        public void AssertMeaningful()
        {
            Assert.True(Restores >= 100, $"restores {Restores}");
            Assert.True(_abilityTicks >= 1000, $"ability ticks {_abilityTicks}");
            Assert.True(_capabilityTicks >= 500, $"capability ticks {_capabilityTicks}");
            Assert.True(_hitboxTicks >= 500, $"hitbox ticks {_hitboxTicks}");
            Assert.True(_hits >= 12, $"hits {_hits}");
            Assert.True(_hitstopTicks >= 100, $"hitstop ticks {_hitstopTicks}");
            Assert.True(_deaths >= 1, $"deaths {_deaths}");
            foreach (var (state, minimum) in new[]
            {
                (ActionState.Hitstun, 100), (ActionState.Shielding, 100), (ActionState.Aiming, 100),
                (ActionState.Grabbed, 20), (ActionState.Thrown, 40), (ActionState.AirDodgeMovement, 20),
            })
                Assert.True(_states.GetValueOrDefault(state) >= minimum, $"{state} ticks {_states.GetValueOrDefault(state)}");
        }
    }
}
