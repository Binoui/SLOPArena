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
        yield return new object[] { "slop_pit", new[] { CharacterClass.Wibou, CharacterClass.Bonk }, 17 };
        yield return new object[] { "table_manners", new[] { CharacterClass.Manki, CharacterClass.Wibou }, 13 };
        yield return new object[] { "table_manners", new[] { CharacterClass.Bonk, CharacterClass.FightGuy }, 14 };
        yield return new object[] { "last_stop", new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk }, 17 };
        yield return new object[] { "slop_pit", new[] { CharacterClass.Manki, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.FightGuy }, 16 };
    }

    [Theory]
    [InlineData("skate", 24)]
    [InlineData("coast", 22)]
    [InlineData("jet", 3)]
    [InlineData("dive", 3)]
    public void BashieShellRestoredMidPhaseReplaysIdentically(string phase, int captureTick)
    {
        ServerSimulation Make() => phase switch
        {
            "skate" => BashieShellTests.Sim(BashieShellTests.Running(11.5f), BashieShellTests.Dummy2(0f, 4f)),
            "dive" => BashieShellTests.Sim(BashieShellTests.Airborne(4f)),
            _ => BashieShellTests.Sim(),
        };
        InputState Input(int tick) => tick != 1 ? default : new InputState
        {
            ActiveSlot = phase is "skate" or "dive" ? AbilitySlots.R : AbilitySlots.E,
            MoveY = phase == "jet" ? 1f : 0f,
        };
        var continuous = Make();
        for (int tick = 1; tick <= captureTick; tick++) BashieShellTests.Step(continuous, Input(tick));
        var captured = BashieShellTests.State(continuous);
        Assert.Equal(phase is "skate" or "coast" ? ActionState.Shell : ActionState.Attacking, captured.State);
        if (phase == "skate")
            Assert.Contains(continuous.Resolver.GetActiveHitboxes(), h => h.OwnerWindow && h.HitEntities!.Count == 1);

        var restored = Make();
        restored.RestoreSnapshot(continuous.CaptureSnapshot());
        for (int tick = captureTick + 1; tick <= captureTick + 60; tick++)
        {
            BashieShellTests.Step(continuous, Input(tick));
            BashieShellTests.Step(restored, Input(tick));
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"{phase} diverged at tick {tick}");
        }
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
        var driver = new ScriptedFight(seed, ids);
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

            bool due = --nextRestore == 0;
            if (due || coverage.WantsRestore)
            {
                // A brand-new simulation, registered in reverse order, takes over from the snapshot.
                restored = NewMatch(arena, roster, reverseRegistration: true);
                restored.RestoreSnapshot(expected);
                coverage.Restored();
                if (due) nextRestore = rng.Next(1, 41);
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

    /// <summary>
    /// A fight that reaches every state the snapshot must carry by script, not by luck: drills between the first two
    /// fighters (attacker and defender swap each round) for hits, a held shield under attack, a grab and its throw,
    /// air dodges, aimed specials and a walk off the stage, with short FightDriver stretches between them for variety
    /// (any further fighters always free-fight). A bot-like random fight met AssertMeaningful only by luck, so a
    /// content change (2026-10-22: Sir Bonk's Barn Door and specials) failed it with every restore still matching.
    /// </summary>
    internal sealed class ScriptedFight
    {
        private enum Drill { Free, Hits, Specials, Shield, Grab, AirDodge, Aim, WalkOff }
        private static readonly Drill[] Round =
            { Drill.Free, Drill.Hits, Drill.Specials, Drill.Shield, Drill.Grab, Drill.AirDodge, Drill.Aim, Drill.Free, Drill.Specials, Drill.Grab };
        private static readonly byte[] Normals = { AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4 };
        private static readonly byte[] Aimed = { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F };
        private const int FreeTicks = 120, CapTicks = 420, WalkOffAfter = 1200;
        private readonly FightDriver _free;
        private readonly ulong[] _ids;
        private int _step, _round, _tick, _count, _wait, _total;
        private bool _walked, _seen, _finishing;
        private float _lastDistance = float.MaxValue;
        private int _climb;
        private Drill _drill = Drill.Free;

        public ScriptedFight(int seed, ulong[] ids)
        {
            _free = new FightDriver(seed, ids);
            _ids = ids;
        }

        public void Next(ServerSimulation sim, Dictionary<ulong, InputState> inputs)
        {
            _free.Next(sim, inputs); // fighters beyond the first two keep these
            _total++;
            ulong a = _ids[_round % 2 == 0 ? 0 : 1], d = _ids[_round % 2 == 0 ? 1 : 0];
            var me = sim.GetState(a);
            var them = sim.GetState(d);
            if (_drill == Drill.Free)
            {
                if (++_tick >= FreeTicks) Advance();
                return;
            }
            if (_drill == Drill.Grab)
                foreach (ulong id in _ids)
                    if (id != a && id != d) inputs[id] = new InputState { ShieldHeld = true };
            // On another level the attacker keeps walking at the defender: up a ledge with Climb, or off one to drop down.
            bool level = MathF.Abs(them.PY - me.PY) < 0.5f;
            var attack = Toward(me, them, !level ? 0f : _drill == Drill.Grab ? 0.8f : 1.1f);
            var hold = Toward(them, me, float.MaxValue); // the defender faces the attacker and stays put
            Climb(me, them, ref attack);
            bool ready = me.State is ActionState.Idle or ActionState.Run && me.IsGrounded;
            bool close = level && Distance(me, them) <= (_drill == Drill.Grab ? 1.0f : 1.4f);
            switch (_drill)
            {
                case Drill.Hits: // every normal, three times, at close range on a standing target
                case Drill.Specials: // every special, twice, tapped (an aimed one fires straight away)
                case Drill.Shield: // the normals into a held shield
                    if (_drill == Drill.Shield) hold.ShieldHeld = true;
                    var slots = _drill == Drill.Specials ? Aimed : Normals;
                    int presses = slots.Length * (_drill == Drill.Hits ? 3 : 2);
                    if (_count < presses && ready && close && them.State is not ActionState.Hitstun && --_wait <= 0)
                    {
                        attack.ActiveSlot = slots[_count % slots.Length];
                        _wait = 4;
                        if (++_count >= presses) Advance(after: 90);
                    }
                    break;
                case Drill.Grab: // grab when adjacent; the hold runs out into the throw
                    if (me.State is ActionState.Grabbing or ActionState.Throwing) _seen = true;
                    else if (_seen && ready) { Advance(); break; }
                    else if (ready && close)
                    {
                        attack.MoveX = attack.MoveY = 0f;
                        attack.FaceToCamera = true;
                        float facingDot = MathF.Cos(me.FacingYaw - attack.AimYaw * (MathF.PI / 18000f));
                        if (MathF.Abs(me.VX) + MathF.Abs(me.VZ) <= 0.001f && facingDot > .999f && --_wait <= 0)
                        {
                            attack.GrabPressed = true;
                            _wait = 20;
                        }
                    }
                    break;
                case Drill.AirDodge: // jump, then dodge sideways in the air
                    attack.MoveX = attack.MoveY = 0f;
                    if (ready && --_wait <= 0) { attack.Jump = true; _wait = 8; }
                    else if (_count < 2 && !me.IsGrounded && me.State is not (ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery) && --_wait <= 0)
                    {
                        attack.ShieldPressed = true;
                        attack.MoveX = 1f;
                        _wait = 40;
                        if (++_count >= 2) Advance(after: 60);
                    }
                    break;
                case Drill.Aim: // hold each aimed special for 40 ticks, then release it
                    attack.MoveX = attack.MoveY = 0f;
                    if (_wait > 0) { _wait--; attack.ActiveSlot = Aimed[_count % Aimed.Length]; attack.IsAiming = _wait > 0; }
                    else if (ready)
                    {
                        if (_count >= Aimed.Length) { Advance(after: 30); break; }
                        attack.ActiveSlot = Aimed[_count % Aimed.Length];
                        attack.IsAiming = true;
                        _wait = 40;
                        _count++;
                    }
                    break;
                case Drill.WalkOff: // walk straight off the stage into the blast zone, once
                    attack = new InputState { MoveX = 1f, AimYaw = 9000, FacingYaw = 9000 };
                    break;
            }
            if (Environment.GetEnvironmentVariable("SNAPSHOT_DRILL_TRACE") == "1" && _total % 20 == 0)
                Console.WriteLine($"t{_total} {_drill} a{a} {me.State} ({me.PX:F1},{me.PY:F1},{me.PZ:F1}) d{d} {them.State} ({them.PX:F1},{them.PY:F1},{them.PZ:F1}) dist {Distance(me, them):F2}");
            if (_drill != Drill.Free && ++_tick >= CapTicks) Advance();
            inputs[a] = attack;
            inputs[d] = hold;
        }

        /// <summary>The way to the defender can be a ledge up (table_manners' tables): jump, and keep the jump held,
        /// while the attacker stops closing in or the defender stands higher.</summary>
        private void Climb(in CharacterState me, in CharacterState them, ref InputState input)
        {
            if (_drill is Drill.Free or Drill.WalkOff or Drill.AirDodge) return;
            float distance = Distance(me, them);
            if (_total % 15 == 0)
            {
                bool stuck = distance > 1.5f && distance > _lastDistance - 0.1f;
                bool above = them.PY - me.PY > 0.6f && distance < 4f;
                if ((stuck || above) && me.IsGrounded) _climb = 14;
                else if ((stuck || above) && !me.IsGrounded && me.JumpsLeft > 0) { input.Jump = true; _climb = 14; }
                _lastDistance = distance;
            }
            if (_climb > 0)
            {
                if (_climb == 14 && me.IsGrounded) input.Jump = true;
                input.JumpHeld = true;
                _climb--;
            }
        }

        private void Advance(int after = 0)
        {
            if (after > 0) // let the last action play out
            {
                if (!_finishing) _tick = Math.Max(_tick, CapTicks - after);
                _finishing = true;
                return;
            }
            _tick = _count = _wait = 0;
            _seen = _finishing = false;
            if (!_walked && _total >= WalkOffAfter) { _walked = true; _drill = Drill.WalkOff; return; }
            if (++_step >= Round.Length) { _step = 0; _round++; }
            _drill = Round[_step];
        }

        private static float Distance(in CharacterState a, in CharacterState b)
            => MathF.Sqrt((b.PX - a.PX) * (b.PX - a.PX) + (b.PZ - a.PZ) * (b.PZ - a.PZ));

        /// <summary>Walk toward the other fighter until within `range`, facing and aiming at them.</summary>
        private static InputState Toward(in CharacterState me, in CharacterState them, float range)
        {
            float dx = them.PX - me.PX, dz = them.PZ - me.PZ, d = MathF.Sqrt(dx * dx + dz * dz);
            short yaw = (short)Math.Clamp(MathF.Atan2(dx, dz) * 180f / MathF.PI * 100f, -18000f, 18000f);
            var input = new InputState { AimYaw = yaw, FacingYaw = yaw, AimDistance = (ushort)Math.Min(6500f, d * 100f) };
            if (d > range && d > 1e-3f) { input.MoveX = dx / d; input.MoveY = dz / d; }
            return input;
        }
    }

    /// <summary>Guards against a vacuous fight: restore points must land during every state that holds runtime the
    /// snapshot has to carry. A restore is forced while a fighter is in a state that has had fewer than RestoresEach
    /// restores, so the check asks only that the script reaches each state, not how long a move or a hitbox lasts.</summary>
    private sealed class Coverage
    {
        private const int RestoresEach = 3;
        private static readonly string[] Wanted =
        {
            "ability", "capability", "hitbox", "hitstop", nameof(ActionState.Hitstun), nameof(ActionState.Shielding),
            nameof(ActionState.Aiming), nameof(ActionState.Grabbing), nameof(ActionState.Grabbed),
            nameof(ActionState.Throwing), nameof(ActionState.Thrown), nameof(ActionState.AirDodgeMovement),
        };
        public int Restores;
        private int _deaths, _hits;
        private readonly HashSet<string> _now = new();
        private readonly Dictionary<string, int> _restoredDuring = new();

        public void Observe(ServerSimulation sim, ulong[] ids)
        {
            _now.Clear();
            foreach (ulong id in ids)
            {
                var state = sim.GetState(id);
                _now.Add(state.State.ToString());
                if (sim.GetActiveAbility(id) is { } ability)
                {
                    _now.Add("ability");
                    if (ability is Abilities.CookedTimelineAbility { RunningCapabilities: > 0 })
                        _now.Add("capability");
                }
                if (state.HitstopTicks > 0) _now.Add("hitstop");
            }
            if (sim.Resolver.GetActiveHitboxes().Count > 0) _now.Add("hitbox");
            _deaths += sim.LastTickDeaths.Count;
            _hits += sim.LastTickHits.Count;
        }

        public bool WantsRestore => _now.Any(x => Wanted.Contains(x) && _restoredDuring.GetValueOrDefault(x) < RestoresEach);

        public void Restored()
        {
            Restores++;
            foreach (string x in _now) _restoredDuring[x] = _restoredDuring.GetValueOrDefault(x) + 1;
        }

        public override string ToString()
            => $"restores {Restores}, hits {_hits}, deaths {_deaths}; restored during "
               + string.Join(", ", Wanted.Select(x => $"{x} {_restoredDuring.GetValueOrDefault(x)}"));

        public void AssertMeaningful()
        {
            if (Environment.GetEnvironmentVariable("SNAPSHOT_DRILL_TRACE") == "1") Console.WriteLine($"coverage: {this}");
            Assert.True(Restores >= 100, $"restores {Restores}");
            Assert.True(_hits >= 5, $"hits {_hits}: {this}");
            Assert.True(_deaths >= 1, $"deaths {_deaths}: {this}");
            foreach (string x in Wanted)
                Assert.True(_restoredDuring.GetValueOrDefault(x) >= RestoresEach, $"never restored {RestoresEach} times during {x}: {this}");
        }
    }
}
