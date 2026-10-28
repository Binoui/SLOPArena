using System.Numerics;
using SlopArena.Shared.Abilities;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Kaboom Quarry (docs/design/stages/manki_quarry.design.md) driven by the cooked Manki kit
/// on the actual baked arena. Protects the course's promises: shooting from the route clears
/// every crate, so does jumping out to each island and punching, the Blast Face needs Jump → E
/// (or the ordinary benches), and one blast breaks every crate it reaches. Inputs are ordinary
/// semantic inputs; the times are evidence for these inputs, not records.
/// </summary>
public sealed class MankiQuarryRouteTests
{
    private readonly ITestOutputHelper _output;
    public MankiQuarryRouteTests(ITestOutputHelper output) => _output = output;

    private const float BombLaunch = 30f * MathF.PI / 180f;

    internal sealed class Run
    {
        public readonly ServerSimulation Sim;
        public readonly ArenaDefinition Arena;
        public readonly CharacterDefinition Def;
        public readonly List<CharacterCourseEvent> Events = new();
        public readonly HashSet<byte> UsedSlots = new();
        public readonly float Body;

        public Run()
        {
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "arenas"))) root = root.Parent;
            Assert.NotNull(root);
            Arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root!.FullName, "data", "arenas", "manki_quarry.arena"))!.Value;
            var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
            Def = entry.Definition; Body = Def.CapsuleHeight * .5f;
            Sim = new ServerSimulation(Arena, NoWinMatchRule.Instance);
            var spawn = Arena.SpawnPoints[0];
            Sim.RegisterEntity(1, Def, new()
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
                JumpsLeft = Def.Movement.MaxJumps, AirDodgesLeft = 1,
            }, entry.BakedAnimation);
            Sim.SetRespawnPosition(1, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            Sim.BeginCourseAttempt(1); Sim.Tick(new());
            Wait(2);
        }

        public CharacterState S => Sim.GetState(1);
        public string Where() { var s = S; return $"{s.PX:F2},{s.PY:F2},{s.PZ:F2} grounded={s.IsGrounded} deaths={s.Deaths} state={s.State}"; }
        public CourseTargetDefinition Target(string id) => Arena.Arcade!.Course!.Targets.First(t => t.Id == id);
        public int Index(string id) { var list = Arena.Arcade!.Course!.Targets; for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i; return -1; }
        public bool Broken(string id) => Sim.IsCourseTargetBroken(Index(id));

        private Vector3 _lastGround, _lastAir;
        /// <summary>Every input after Go, for a replay fixture (scenarios/arcade).</summary>
        public readonly List<InputState> Inputs = new();
        public void Tick(InputState input)
        {
            byte deaths = S.Deaths;
            Inputs.Add(input);
            Sim.Tick(new() { [1] = input });
            var s = S;
            if (s.AttackSlot != 0) UsedSlots.Add(s.AttackSlot);
            Events.AddRange(Sim.LastTickCourseEvents);
            if (s.Deaths != deaths)
                throw new Exception($"Fell into the quarry: last grounded at {_lastGround}, last airborne at {_lastAir}");
            if (s.IsGrounded) _lastGround = new(s.PX, s.PY, s.PZ); else _lastAir = new(s.PX, s.PY, s.PZ);
        }
        public void Wait(int ticks) { for (int i = 0; i < ticks; i++) Tick(default); }
        void Stop() { Tick(new() { ShieldHeld = true, ShieldPressed = true }); Wait(12); }

        static short Yaw(float dx, float dz) => (short)MathF.Round(MathF.Atan2(dx, dz) * 18000f / MathF.PI);
        InputState Toward(float x, float z)
        {
            var s = S; var v = new Vector2(x - s.PX, z - s.PZ); float l = v.Length(); if (l > .01f) v /= l;
            float strength = Math.Clamp(l * 1.3f, 0, 1);
            short yaw = Yaw(v.X, v.Y);
            return new() { MoveX = v.X * strength, MoveY = v.Y * strength, FacingYaw = yaw, AimYaw = yaw };
        }

        public void Walk(float x, float z)
        {
            for (int i = 0; i < 600; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (Sim.Course!.Value.Ended || d < .35f) { Stop(); return; }
                Tick(Toward(x, z));
            }
            throw new Exception($"walk stalled toward {x},{z} at {Where()}");
        }

        /// <summary>Jump toward (x, z) and land grounded near it; optional second jump, short hop or running start.</summary>
        public void Jump(float x, float z, int secondJumpTick = -1, bool shortHop = false, int runUp = 0)
        {
            for (int i = 0; i < runUp; i++) Tick(Toward(x, z));
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 200; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > 12 && s.IsGrounded && d < 1.2f) { Stop(); return; }
                var input = Toward(x, z); input.Jump = i == 0 || i == secondJumpTick; input.JumpHeld = i == 0 || (!shortHop && i < 55);
                Tick(input);
            }
            throw new Exception($"jump toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Jump, fire E (Jetpack Boost) at <paramref name="eTick"/> and drift to land near (x, z).</summary>
        public void Jetpack(float x, float z, int eTick)
        {
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 240; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > eTick + 30 && s.IsGrounded && d < 1.6f) { Stop(); return; }
                var input = Toward(x, z);
                input.Jump = i == 0 || i > eTick + 3 && i < eTick + 39 && (i - eTick - 3) % 12 == 0;
                input.JumpHeld = i < 24;
                if (i == eTick + 40) input.ActiveSlot = AbilitySlots.E;
                if (i == eTick) input.ActiveSlot = AbilitySlots.E;
                Tick(input);
            }
            throw new Exception($"jetpack toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Press a slot and confirm its ability started (a press during a recovery is ignored).</summary>
        void Press(byte slot, InputState held)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var press = held; press.ActiveSlot = slot;
                Tick(press);
                if (S.AttackSlot == slot) return;
            }
            throw new Exception($"slot {slot} did not start at {Where()}");
        }

        void WaitReady(byte slot)
        {
            for (int i = 0; i < 400 && (S.GetCooldown(slot) > 0 || S.AttackSlot != 0 || !S.IsGrounded); i++) Tick(default);
        }

        bool WaitBreak(IEnumerable<string> ids, int ticks)
        {
            for (int i = 0; i < ticks; i++) { if (ids.All(Broken)) return true; Tick(default); }
            return ids.All(Broken);
        }

        /// <summary>
        /// A Round Bomb aimed like the client's ground cursor: at a point on a surface, sending the
        /// foot-height distance whose arc passes through it (CombatMath.LobDistanceThrough).
        /// </summary>
        public void Bomb(float x, float y, float z, params string[] crates)
        {
            WaitReady(AbilitySlots.A);
            var s = S; float horizontal = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
            float sent = CombatMath.LobDistanceThrough(horizontal, y - (s.PY + MankiRoundBomb.LaunchOffsetY), BombLaunch,
                -Body - MankiRoundBomb.LaunchOffsetY);
            Assert.True(sent <= 12f, $"bomb at {x},{y},{z} needs a {sent:F2} m throw from {Where()}");
            short yaw = Yaw(x - s.PX, z - s.PZ);
            ushort distance = (ushort)MathF.Round(sent * 100f);
            Press(AbilitySlots.A, new() { IsAiming = true, AimYaw = yaw, FacingYaw = yaw, AimDistance = distance });
            for (int i = 1; i < 12; i++)
                Tick(new() { IsAiming = true, AimYaw = yaw, FacingYaw = yaw, AimDistance = distance });
            if (!WaitBreak(crates, 400)) throw new Exception($"bomb at {x},{y},{z} missed {string.Join(",", crates.Where(c => !Broken(c)))} from {Where()}");
        }

        /// <summary>Press Bazooka with the course crate selected as the soft target.</summary>
        public void Rocket(float x, float y, float z, params string[] crates)
        {
            WaitReady(AbilitySlots.R);
            byte target = Sim.CourseTargetInputAlias(Index(crates[0]));
            short yaw = Yaw(x - S.PX, z - S.PZ);
            Press(AbilitySlots.R, new() { TargetEntityId = target, FacingYaw = yaw });
            for (int i = 1; i < 12; i++) Tick(new() { TargetEntityId = target, FacingYaw = yaw });
            if (!WaitBreak(crates, 120)) throw new Exception($"rocket at {x},{y},{z} missed {string.Join(",", crates.Where(c => !Broken(c)))} from {Where()}");
        }

        /// <summary>Walk up to a crate on the island underfoot and punch (ground 1) until it breaks.</summary>
        public void Punch(string crate)
        {
            var target = Target(crate);
            for (int attempt = 0; attempt < 6 && !Broken(crate); attempt++)
            {
                var s = S; var away = new Vector2(s.PX - target.Position.X, s.PZ - target.Position.Z);
                if (away.Length() < .1f) away = new(0, -1);
                away = Vector2.Normalize(away) * 1.1f;
                Walk(target.Position.X + away.X, target.Position.Z + away.Y);
                WaitReady(AbilitySlots.Slot1);
                s = S; short yaw = Yaw(target.Position.X - s.PX, target.Position.Z - s.PZ);
                for (int i = 0; i < 40 && !Broken(crate); i++)
                    Tick(new() { ActiveSlot = i == 0 ? AbilitySlots.Slot1 : (byte)0, FacingYaw = yaw, AimYaw = yaw,
                        TargetEntityId = Sim.CourseTargetInputAlias(Index(crate)) });
            }
            if (!Broken(crate)) throw new Exception($"could not punch {crate} from {Where()}");
        }

        public void Finish(float x, float z)
        {
            Walk(x, z);
            for (int i = 0; i < 30 && !Sim.Course!.Value.Ended; i++) Tick(default);
        }

        /// <summary>
        /// The recorded inputs in the replay runner's semantic-step format (docs/testing/course-replay.md).
        /// Identical consecutive held inputs merge; ticks with a press or slot stay single.
        /// </summary>
        public string ReplaySteps()
        {
            static string Slot(byte slot) => slot switch
            {
                AbilitySlots.Slot1 => "1", AbilitySlots.Slot3 => "3", AbilitySlots.Slot4 => "4",
                AbilitySlots.A => "A", AbilitySlots.E => "E", AbilitySlots.R => "R", AbilitySlots.F => "F",
                _ => throw new InvalidOperationException("Unexpected slot " + slot),
            };
            static string Step(InputState i, int ticks)
            {
                var f = new List<string> { "\"ticks\":" + ticks };
                string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (i.MoveX != 0) f.Add("\"moveX\":" + F(i.MoveX));
                if (i.MoveY != 0) f.Add("\"moveY\":" + F(i.MoveY));
                if (i.Jump) f.Add("\"jumpPressed\":true");
                if (i.JumpHeld) f.Add("\"jumpHeld\":true");
                if (i.ShieldPressed) f.Add("\"shieldPressed\":true");
                if (i.ShieldHeld) f.Add("\"shieldHeld\":true");
                if (i.ActiveSlot != 0) f.Add("\"slot\":\"" + Slot(i.ActiveSlot) + "\"");
                if (i.IsAiming) f.Add("\"isAiming\":true");
                f.Add("\"facingYaw\":" + i.FacingYaw);
                f.Add("\"aimYaw\":" + i.AimYaw);
                if (i.AimPitch != 0) f.Add("\"aimPitch\":" + i.AimPitch);
                if (i.AimDistance != 0) f.Add("\"aimDistance\":" + i.AimDistance);
                if (i.TargetEntityId != 0) f.Add("\"targetEntityId\":\"" + i.TargetEntityId + "\"");
                return "  {" + string.Join(",", f) + "}";
            }
            static bool Single(InputState i) => i.Jump || i.ShieldPressed || i.ActiveSlot != 0;
            var steps = new List<string>();
            for (int k = 0; k < Inputs.Count;)
            {
                int n = 1;
                if (!Single(Inputs[k]))
                    while (k + n < Inputs.Count && Inputs[k + n].Equals(Inputs[k])) n++;
                steps.Add(Step(Inputs[k], n));
                k += n;
            }
            return "[\n" + string.Join(",\n", steps) + "\n]\n";
        }

        public void AssertCompleted()
        {
            Assert.Equal(CharacterCoursePhase.Completed, Sim.Course!.Value.Phase);
            Assert.Equal(18, Sim.Course.Value.BrokenTargets);
            Assert.Equal((byte)0, S.Deaths);
            for (int i = 0; i < 3; i++)
                Assert.Contains(Events, e => e.Kind == CharacterCourseEventKind.CheckpointSaved && e.Index == i);
            Assert.InRange(Sim.Course.Value.ElapsedTicks, 1ul, 150ul * 60);
        }
    }

    // ------------------------------------------------------------------ the loop's legs

    static void BlastRangeByShooting(Run r)
    {
        r.Walk(-17f, -33f);
        r.Bomb(-17f, 1f, -24.5f, "crate_01");                 // near islet: the first bomb
        r.Walk(-25f, -35.5f);
        r.Rocket(-25f, 4.3f, -45f, "crate_02");               // raised sea stack: the first rocket
        r.Walk(-31f, -32.5f);
        r.Bomb(-31f, 1f, -22f, "crate_03", "crate_04");       // one blast, both twins
        r.Walk(-36f, -35.5f);
        r.Rocket(-36f, 2.1f, -50.5f, "crate_05");             // far buoy
        r.Walk(-42f, -30f);                                   // checkpoint 1
    }

    static void PontoonsByShooting(Run r)
    {
        r.Walk(-42f, -27.6f);
        r.Jump(-42f, -23.5f, shortHop: true);                 // pontoon 1: short hop
        r.Bomb(-49f, 1f, -16.1f, "crate_07", "crate_08", "crate_09"); // one blast, the trio
        r.Walk(-40.9f, -22.4f);
        r.Jump(-35f, -14f, secondJumpTick: 22);               // pontoon 2: the double-jump gap
        r.Rocket(-30f, 1.5f, -11f, "crate_06");               // low raft
        r.Walk(-36.1f, -12.9f);
        r.Jump(-41f, -8f);                                    // pontoon 3: rising diagonal
        r.Walk(-41.6f, -6.9f);
        r.Jump(-44f, -1f);                                    // pontoon 4: drop
        r.Walk(-44f, 0.1f);
        r.Jump(-44f, 4.5f, shortHop: true);                   // North Bench
        r.Walk(-36f, 4.5f);
        r.Bomb(-33f, 4f, 0f, "crate_10");                     // high stack (bomb lands on its top)
        r.Walk(-26f, 7f);                                     // checkpoint 2
    }

    static void BaseCrates(Run r)
    {
        r.Walk(-8f, 8f);
        r.Bomb(-4f, 1f, 1.5f, "crate_12", "crate_13");        // the twins below the face
    }

    static void ClimbBenches(Run r)
    {
        r.Walk(-17.6f, 15f);
        r.Jump(-21.5f, 15f);                                  // bench 1
        r.Walk(-22.6f, 16.4f);
        r.Jump(-25f, 22f, secondJumpTick: 20);                // bench 2: double jump
        r.Walk(-23.9f, 23.1f);
        r.Jump(-20f, 29f);                                    // bench 3
        r.Walk(-18.9f, 29f);
        r.Jump(-14.5f, 29f);                                  // Upper Bench
    }

    static void UpperBenchCrates(Run r)
    {
        r.Walk(-7f, 30.5f);
        r.Rocket(-7f, 5.6f, 37f, "crate_11");                 // north pillar
        r.Walk(0.5f, 30.5f);
        r.Bomb(6f, 5.5f, 35f, "crate_14");                    // east rock
        r.Walk(1f, 24f);                                      // checkpoint 3
    }

    static void ConveyorRunByShooting(Run r)
    {
        r.Walk(1.6f, 21.5f);
        r.Jump(8f, 19f);                                      // crusher roof
        r.Walk(10.6f, 16.4f);
        r.Jump(16f, 9f);                                      // conveyor gantry
        r.Rocket(30f, 4.1f, 2f, "crate_16");                  // far stack
        r.Walk(14.8f, 6.9f);
        r.Jump(13f, -3f, runUp: 4);                           // hopper
        r.Bomb(8f, .6f, -13f, "crate_15");                    // dive-bomb the low islet
        r.Walk(14.1f, -5.6f);
        r.Jump(20f, -14f, runUp: 4);                          // dump truck
        r.Rocket(24f, .6f, -24f, "crate_17", "crate_18");     // one rocket blast, both raft crates
        r.Walk(18.4f, -15.6f);
        r.Jump(15f, -22f);                                    // gravel pile
        r.Walk(15f, -23.4f);
        r.Jump(14f, -30f);                                    // loading dock
        r.Finish(10f, -34f);
    }

    // ------------------------------------------------------------------ the same loop, jumping out to every island

    static void BlastRangeByJumping(Run r)
    {
        r.Walk(-17f, -32.3f);
        r.Jump(-17f, -24.6f);                                 // hop islet
        r.Punch("crate_01");
        r.Walk(-17f, -23.3f);
        r.Jump(-17f, -33f, runUp: 8);
        r.Walk(-25f, -33.6f);
        r.Jump(-25f, -45f, secondJumpTick: 22, runUp: 10);    // sea stack: running double jump up
        r.Punch("crate_02");
        r.Walk(-25f, -45.8f);
        r.Jump(-25f, -34.5f, secondJumpTick: 24, runUp: 6);
        r.Walk(-31f, -32.2f);
        r.Jump(-31f, -22.4f, secondJumpTick: 22);             // twin islet
        r.Punch("crate_03"); r.Punch("crate_04");
        r.Walk(-31f, -20.8f);
        r.Jump(-31f, -33f, secondJumpTick: 22, runUp: 8);
        r.Walk(-36f, -35.9f);
        r.Jump(-36f, -42.5f);                                 // south stone
        r.Walk(-36f, -43.4f);
        r.Jump(-36f, -50.3f);                                 // far buoy
        r.Punch("crate_05");
        r.Walk(-36f, -51.6f);
        r.Jump(-36f, -42.5f, runUp: 6);
        r.Walk(-36f, -43.5f);
        r.Jump(-36f, -35f, runUp: 4);
        r.Walk(-42f, -30f);                                   // checkpoint 1
    }

    static void PontoonsByJumping(Run r)
    {
        r.Walk(-42f, -27.6f);
        r.Jump(-42f, -23.5f, shortHop: true);                 // pontoon 1
        r.Walk(-43.0f, -22.5f);
        r.Jump(-48f, -17f);                                   // trio block
        r.Punch("crate_07"); r.Punch("crate_08"); r.Punch("crate_09");
        r.Walk(-47.3f, -17.6f);
        r.Jump(-42f, -23.5f);                                 // back to pontoon 1
        r.Walk(-40.9f, -22.4f);
        r.Jump(-35f, -14f, secondJumpTick: 22);               // pontoon 2
        r.Walk(-34.0f, -13.0f);
        r.Jump(-30f, -11f);                                   // low raft
        r.Punch("crate_06");
        r.Walk(-29.4f, -12.0f);
        r.Jump(-35f, -14f);                                   // back up to pontoon 2
        r.Walk(-36.1f, -12.9f);
        r.Jump(-41f, -8f);                                    // pontoon 3
        r.Walk(-41.6f, -6.9f);
        r.Jump(-44f, -1f);                                    // pontoon 4
        r.Walk(-44f, 0.1f);
        r.Jump(-44f, 4.5f, shortHop: true);                   // North Bench
        r.Walk(-33f, 3.6f);
        r.Jump(-33f, 0f, secondJumpTick: 18);                 // high stack: double jump up
        r.Punch("crate_10");
        r.Walk(-33f, 0.6f);
        r.Jump(-33f, 5f);
        r.Walk(-26f, 7f);                                     // checkpoint 2
    }

    static void BaseCratesByJumping(Run r)
    {
        r.Walk(-5f, 6.6f);
        r.Jump(-4f, 2.1f);                                    // base islet
        r.Punch("crate_12"); r.Punch("crate_13");
        r.Walk(-4.6f, 0f);
        r.Jump(-6f, 9f, runUp: 6);
    }

    static void UpperBenchCratesByJumping(Run r)
    {
        r.Walk(-7f, 31.4f);
        r.Jump(-7f, 37f);                                     // north pillar: drop down
        r.Punch("crate_11");
        r.Walk(-7f, 35.9f);
        r.Jump(-7f, 30.5f, secondJumpTick: 18);               // and back up
        r.Walk(1.4f, 31.4f);
        r.Jump(6f, 35f);                                      // east rock
        r.Punch("crate_14");
        r.Walk(5f, 34f);
        r.Jump(0f, 30f);
        r.Walk(1f, 24f);                                      // checkpoint 3
    }

    static void ConveyorRunByJumping(Run r)
    {
        r.Walk(1.6f, 21.5f);
        r.Jump(8f, 19f);                                      // crusher roof
        r.Walk(10.6f, 16.4f);
        r.Jump(16f, 9f);                                      // conveyor gantry
        r.Walk(17.4f, 6.6f);
        r.Jump(24f, 4f);                                      // east stone
        r.Walk(24.8f, 3.6f);
        r.Jump(30f, 2f);                                      // far stack
        r.Punch("crate_16");
        r.Walk(29f, 2.6f);
        r.Jump(24f, 4f);
        r.Walk(23.2f, 4.6f);
        r.Jump(16.5f, 7.5f, secondJumpTick: 20);              // back up to the gantry
        r.Walk(14.8f, 6.9f);
        r.Jump(13f, -3f, runUp: 4);                           // hopper
        r.Walk(12f, -5.4f);
        r.Jump(8.4f, -12.4f);                                 // low islet
        r.Punch("crate_15");
        r.Walk(9f, -13.9f);
        r.Jump(14.5f, -21.5f, secondJumpTick: 20);            // gravel pile
        r.Walk(16.2f, -22f);
        r.Jump(23f, -24f);                                    // twin raft
        r.Punch("crate_17"); r.Punch("crate_18");
        r.Walk(25.8f, -24f);
        r.Jump(15f, -22f, secondJumpTick: 22, runUp: 6);      // back to the gravel pile
        r.Walk(15f, -23.4f);
        r.Jump(14f, -30f);                                    // loading dock
        r.Finish(10f, -34f);
    }


    [Fact]
    public void ShootingFromTheRouteClearsEveryCrateOnTheOrdinaryClimb()
    {
        var r = new Run();
        BlastRangeByShooting(r);
        PontoonsByShooting(r);
        BaseCrates(r);
        ClimbBenches(r);
        UpperBenchCrates(r);
        ConveyorRunByShooting(r);
        r.AssertCompleted();
        Assert.DoesNotContain(AbilitySlots.E, r.UsedSlots);
        _output.WriteLine($"shooting, ordinary climb: {r.Sim.Course!.Value.ElapsedTicks} ticks");
    }

    [Fact]
    public void JumpThenJetpackSkipsTheBenches()
    {
        var r = new Run();
        void Mark(string leg) => _output.WriteLine($"{leg} until tick {r.Inputs.Count}");
        BlastRangeByShooting(r); Mark("blast range");
        PontoonsByShooting(r); Mark("pontoons");
        BaseCrates(r);
        r.Walk(-10f, 18.6f); Mark("jetpack pad");
        r.Jetpack(-10f, 23.5f, eTick: 22);                    // straight up the Blast Face
        Assert.True(r.S.PY > 7f, "landed on the Upper Bench: " + r.Where());
        Mark("upper bench");
        UpperBenchCrates(r); Mark("checkpoint 3");
        ConveyorRunByShooting(r);
        r.AssertCompleted();
        Assert.Contains(AbilitySlots.E, r.UsedSlots);
        _output.WriteLine($"shooting, jetpack: {r.Sim.Course!.Value.ElapsedTicks} ticks");
        // Refresh the replay fixture after a deliberate layout or kit change:
        // SLOPARENA_EXPORT_COURSE_INPUTS=scenarios/arcade dotnet test ... --filter JumpThenJetpackSkipsTheBenches
        string? export = Environment.GetEnvironmentVariable("SLOPARENA_EXPORT_COURSE_INPUTS");
        if (!string.IsNullOrEmpty(export))
            File.WriteAllText(Path.Combine(export, "manki-quarry-jetpack.inputs.json"), r.ReplaySteps());
    }

    [Fact]
    public void JumpingOutToEveryIslandClearsTheCourseWithoutProjectiles()
    {
        var r = new Run();
        BlastRangeByJumping(r);
        PontoonsByJumping(r);
        BaseCratesByJumping(r);
        ClimbBenches(r);
        UpperBenchCratesByJumping(r);
        ConveyorRunByJumping(r);
        r.AssertCompleted();
        Assert.DoesNotContain(AbilitySlots.A, r.UsedSlots);
        Assert.DoesNotContain(AbilitySlots.R, r.UsedSlots);
        Assert.DoesNotContain(AbilitySlots.E, r.UsedSlots);
        _output.WriteLine($"jumping out, punching: {r.Sim.Course!.Value.ElapsedTicks} ticks");
    }

    /// <summary>The 5.5 m Blast Face: a double jump (3.5 m) and E alone (4.6 m) fall short; Jump → E (6.7 m) makes it.</summary>
    [Theory]
    [InlineData("double jump", false)]
    [InlineData("E alone", false)]
    [InlineData("double jump then E", true)]
    [InlineData("jump then E", true)]
    public void BlastFaceNeedsTheJetpackOrTheBenches(string attempt, bool reaches)
    {
        var r = new Run();
        var s = r.S; s.PX = -10f; s.PY = 1f + r.Body; s.PZ = 18.6f; s.VX = s.VY = s.VZ = 0f; s.IsGrounded = true;
        r.Sim.SetState(1, s); r.Wait(20);
        bool onBench = false;
        for (int i = 0; i < 180; i++)
        {
            var input = new InputState { MoveY = 1f };
            switch (attempt)
            {
                case "double jump": input.Jump = i == 0 || i == 22; input.JumpHeld = i < 40; break;
                case "E alone": if (i == 0) input.ActiveSlot = AbilitySlots.E; break;
                case "double jump then E": input.Jump = i == 0 || i == 22; input.JumpHeld = i < 40; if (i == 40) input.ActiveSlot = AbilitySlots.E; break;
                case "jump then E": input.Jump = i == 0; input.JumpHeld = i < 24; if (i == 22) input.ActiveSlot = AbilitySlots.E; break;
            }
            int ignition = attempt == "jump then E" ? 22 : attempt == "double jump then E" ? 40 : -1;
            if (ignition >= 0)
            {
                input.Jump |= i > ignition + 3 && i < ignition + 39 && (i - ignition - 3) % 12 == 0;
                if (i == ignition + 40) input.ActiveSlot = AbilitySlots.E;
            }
            r.Tick(input);
            onBench |= r.S.IsGrounded && r.S.PY > 7f;
            if (onBench) break;
        }
        Assert.Equal(reaches, onBench);
    }
}
