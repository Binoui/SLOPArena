using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Fight Night Pier (docs/design/stages/fightguy_pier.design.md) driven by the cooked FightGuy kit
/// on the baked arena. Protects the course's promises: the specials clear their shapes (one
/// Cyclone Kick sweeps a row, one Rising Dragon pops a stack, Ki Shot reaches the far balloons, one
/// Fist of Fury breaks a bonus target), jumps and punches alone also finish, the old pier gap
/// needs Jump → R or the pilings and the wheel tower needs Jump → E or the gondola pods.
/// Inputs are ordinary semantic inputs; the times are evidence for these inputs, not records.
/// Set SLOPARENA_COURSE_SHELL to a <c>layout.py --unity-shell</c> document to calibrate an
/// unbaked layout (CourseShellArena).
/// </summary>
public sealed class FightGuyPierRouteTests
{
    private readonly ITestOutputHelper _output;
    public FightGuyPierRouteTests(ITestOutputHelper output) => _output = output;

    internal const int TargetCount = 28;

    internal sealed class Run
    {
        public readonly ServerSimulation Sim;
        public readonly ArenaDefinition Arena;
        public readonly CharacterDefinition Def;
        public readonly List<CharacterCourseEvent> Events = new();
        public readonly HashSet<byte> UsedSlots = new();
        public readonly float Body;

        public static ArenaDefinition LoadArena()
        {
            string? shell = Environment.GetEnvironmentVariable("SLOPARENA_COURSE_SHELL");
            if (!string.IsNullOrEmpty(shell)) return CourseShellArena.FromShellFile(shell);
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "arenas"))) root = root.Parent;
            Assert.NotNull(root);
            return ArenaBinaryFormat.LoadFromFile(Path.Combine(root!.FullName, "data", "arenas", "fightguy_pier.arena"))!.Value;
        }

        public Run(float? x = null, float? y = null, float? z = null, float yaw = 0f)
        {
            Arena = LoadArena();
            var entry = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
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
            if (x is float px)
            {
                var s = S; s.PX = px; s.PY = y!.Value + Body; s.PZ = z!.Value; s.VX = s.VY = s.VZ = 0f;
                s.FacingYaw = yaw * MathF.PI / 180f; s.IsGrounded = true; Sim.SetState(1, s); Wait(10);
            }
        }

        /// <summary>Optional step log (calibration).</summary>
        public Action<string>? Log;
        public CharacterState S => Sim.GetState(1);
        public string Where() { var s = S; return $"{s.PX:F2},{s.PY - Body:F2},{s.PZ:F2} grounded={s.IsGrounded} deaths={s.Deaths} state={s.State} slot={s.AttackSlot}"; }
        public CourseTargetDefinition Target(string id) => Arena.Arcade!.Course!.Targets.First(t => t.Id == id);
        public int Index(string id) { var list = Arena.Arcade!.Course!.Targets; for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i; return -1; }
        public bool Broken(string id) => Sim.IsCourseTargetBroken(Index(id));
        public bool AllBroken(params string[] ids) => ids.All(Broken);

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
                throw new Exception($"Fell into the sea: last grounded at {_lastGround}, last airborne at {_lastAir}");
            if (s.IsGrounded) _lastGround = new(s.PX, s.PY - Body, s.PZ); else _lastAir = new(s.PX, s.PY - Body, s.PZ);
        }
        public void Wait(int ticks) { for (int i = 0; i < ticks; i++) Tick(default); }
        void Stop() { Tick(new() { ShieldHeld = true, ShieldPressed = true }); Wait(12); }

        public static short Yaw(float dx, float dz) => (short)MathF.Round(MathF.Atan2(dx, dz) * 18000f / MathF.PI);
        public InputState Toward(float x, float z)
        {
            var s = S; var v = new Vector2(x - s.PX, z - s.PZ); float l = v.Length(); if (l > .01f) v /= l;
            float strength = Math.Clamp(l * 1.3f, 0, 1);
            short yaw = Yaw(v.X, v.Y);
            return new() { MoveX = v.X * strength, MoveY = v.Y * strength, FacingYaw = yaw, AimYaw = yaw };
        }
        InputState Facing(float x, float z) { var s = S; short yaw = Yaw(x - s.PX, z - s.PZ); return new() { FacingYaw = yaw, AimYaw = yaw }; }

        public void Walk(float x, float z)
        {
            for (int i = 0; i < 900; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (Sim.Course!.Value.Ended || d < .35f) { Stop(); Log?.Invoke($"t{Inputs.Count} walk {x},{z} -> {Where()}"); return; }
                Tick(Toward(x, z));
            }
            throw new Exception($"walk stalled toward {x},{z} at {Where()}");
        }

        /// <summary>Jump toward (x, z) and land grounded near it; optional second jump, short hop or running start.</summary>
        public void Jump(float x, float z, int secondJumpTick = -1, bool shortHop = false, int runUp = 0)
        {
            for (int i = 0; i < runUp; i++) Tick(Toward(x, z));
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 220; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (Sim.Course!.Value.Ended) return;
                if (i > 12 && s.IsGrounded && d < 1.2f) { Stop(); Log?.Invoke($"t{Inputs.Count} jump {x},{z} -> {Where()}"); return; }
                var input = Toward(x, z); input.Jump = i == 0 || i == secondJumpTick; input.JumpHeld = i == 0 || (!shortHop && i < 55);
                Tick(input);
            }
            throw new Exception($"jump toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Jump toward (x, z), Cyclone Kick at <paramref name="rTick"/> and drift to land near it.</summary>
        public void JumpCyclone(float x, float z, int rTick, int runUp = 6)
        {
            for (int i = 0; i < runUp; i++) Tick(Toward(x, z));
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 260; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > rTick + 62 && s.IsGrounded && d < 3f) { Stop(); return; }
                var input = Toward(x, z); input.Jump = i == 0; input.JumpHeld = i < 30;
                if (i == rTick) input.ActiveSlot = AbilitySlots.R;
                Tick(input);
            }
            throw new Exception($"jump R toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Jump, Rising Dragon at <paramref name="eTick"/>, then drift to land near (x, z).</summary>
        public void JumpRising(float x, float z, int eTick, bool drift = true)
        {
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 240; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > eTick + 30 && s.IsGrounded && d < 2f) { Stop(); return; }
                var input = drift || i > eTick + 24 ? Toward(x, z) : Facing(x, z); input.Jump = i == 0; input.JumpHeld = i < 30;
                if (i == eTick) input.ActiveSlot = AbilitySlots.E;
                Tick(input);
            }
            throw new Exception($"jump E toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Press a slot and confirm its ability started (a press during a recovery is ignored).</summary>
        public void Press(byte slot, InputState held)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var press = held; press.ActiveSlot = slot;
                Tick(press);
                if (S.AttackSlot == slot) return;
            }
            throw new Exception($"slot {slot} did not start at {Where()}");
        }

        public void WaitReady(byte slot)
        {
            for (int i = 0; i < 900 && (S.GetCooldown(slot) > 0 || S.AttackSlot != 0 || !S.IsGrounded); i++) Tick(default);
        }

        void WaitIdle() { for (int i = 0; i < 300 && (S.AttackSlot != 0 || !S.IsGrounded); i++) Tick(default); }

        /// <summary>Face (x, z) by tilting the stick that way for a moment (an idle fighter turns with movement).</summary>
        public void TurnTo(float x, float z)
        {
            for (int i = 0; i < 30; i++)
            {
                var face = Facing(x, z);
                float want = face.FacingYaw * MathF.PI / 18000f, delta = MathF.Abs(MathF.IEEERemainder(S.FacingYaw - want, 2 * MathF.PI));
                if (delta < .03f) break;
                var step = Toward(x, z); step.MoveX *= .25f; step.MoveY *= .25f;
                Tick(step);
            }
            Stop();
        }

        /// <summary>A ground Cyclone Kick toward (x, z); it must break <paramref name="ids"/>.</summary>
        public void Cyclone(float x, float z, params string[] ids)
        {
            WaitReady(AbilitySlots.R);
            TurnTo(x, z);
            Press(AbilitySlots.R, Facing(x, z));
            WaitIdle();
            if (!AllBroken(ids)) throw new Exception($"Cyclone Kick missed {string.Join(",", ids.Where(t => !Broken(t)))} at {Where()}");
        }

        /// <summary>A Rising Dragon in place (facing (x, z)); it must pop <paramref name="ids"/>.</summary>
        public void Rising(float x, float z, params string[] ids)
        {
            WaitReady(AbilitySlots.E);
            TurnTo(x, z);
            Press(AbilitySlots.E, Facing(x, z));
            WaitIdle();
            if (!AllBroken(ids)) throw new Exception($"Rising Dragon missed {string.Join(",", ids.Where(t => !Broken(t)))} at {Where()}");
        }

        /// <summary>A Ki Shot locked on one balloon, like the client's aim assist.</summary>
        public void KiShot(string id)
        {
            var target = Target(id).Position;
            for (int attempt = 0; attempt < 3 && !Broken(id); attempt++)
            {
                WaitReady(AbilitySlots.A);
                var s = S; short yaw = Yaw(target.X - s.PX, target.Z - s.PZ);
                byte alias = Sim.CourseTargetInputAlias(Index(id));
                for (int i = 0; i < 23; i++)
                    Tick(new() { ActiveSlot = i == 0 ? AbilitySlots.A : (byte)0, IsAiming = true, FacingYaw = yaw, AimYaw = yaw, TargetEntityId = alias });
                for (int i = 0; i < 110 && !Broken(id); i++) Tick(new() { FacingYaw = yaw, AimYaw = yaw, TargetEntityId = alias });
            }
            if (!Broken(id)) throw new Exception($"Ki Shot missed {id} from {Where()}");
        }

        /// <summary>Stand <paramref name="range"/> m from a target, facing it.</summary>
        public void Approach(string id, float range, Vector2? from = null)
        {
            var target = Target(id).Position;
            var s = S; var away = from ?? new Vector2(s.PX - target.X, s.PZ - target.Z);
            if (away.Length() < .1f) away = new(0, -1);
            away = Vector2.Normalize(away) * range;
            Walk(target.X + away.X, target.Z + away.Y);
        }

        /// <summary>One Fist of Fury into a bonus target: its eight hits break it.</summary>
        public void Fury(string id, float range = 1.2f, Vector2? from = null)
        {
            Approach(id, range, from);
            WaitReady(AbilitySlots.F);
            var target = Target(id).Position;
            TurnTo(target.X, target.Z);
            Press(AbilitySlots.F, Facing(target.X, target.Z));
            WaitIdle();
            if (!Broken(id)) throw new Exception($"Fist of Fury left {id} with {Sim.CourseTargetHitsLeft(Index(id))} hits at {Where()}");
        }

        /// <summary>Ground attacks (straight punch by default) until the target breaks.</summary>
        public void Punch(string id, byte slot = AbilitySlots.Normal1, float range = 1.0f, Vector2? from = null)
        {
            Approach(id, range, from);
            var target = Target(id).Position;
            for (int attempt = 0; attempt < 14 && !Broken(id); attempt++)
            {
                WaitIdle();
                var face = Facing(target.X, target.Z);
                face.TargetEntityId = Sim.CourseTargetInputAlias(Index(id));
                for (int i = 0; i < 40 && !Broken(id); i++) { var input = face; if (i == 0) input.ActiveSlot = slot; Tick(input); }
            }
            if (!Broken(id)) throw new Exception($"could not punch {id} from {Where()}");
        }

        /// <summary>Jump toward (x, z), air attack at <paramref name="attackTick"/> and land near it.</summary>
        public void JumpAttack(float x, float z, int attackTick, int secondJumpTick = -1, byte slot = AbilitySlots.Normal1)
        {
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 220; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > 12 && s.IsGrounded && d < 1.2f) { Stop(); return; }
                var input = Toward(x, z); input.Jump = i == 0 || i == secondJumpTick; input.JumpHeld = i < 55;
                if (i == attackTick) input.ActiveSlot = slot;
                Tick(input);
            }
            throw new Exception($"jump attack toward {x},{z} did not land at {Where()}");
        }

        /// <summary>Jump in place (or with a second jump) and air attack at <paramref name="attackTick"/> until it breaks.</summary>
        public void JumpPunch(string id, int attackTick = 14, int secondJumpTick = -1, byte slot = AbilitySlots.Normal1,
            float range = 0.9f, Vector2? from = null)
        {
            Approach(id, range, from);
            var target = Target(id).Position;
            for (int attempt = 0; attempt < 3 && !Broken(id); attempt++)
            {
                WaitIdle();
                var face = Facing(target.X, target.Z);
                for (int i = 0; i < 120; i++)
                {
                    var input = face; input.Jump = i == 0 || i == secondJumpTick; input.JumpHeld = i < 40;
                    if (i == attackTick) input.ActiveSlot = slot;
                    Tick(input);
                    if (i > 12 && S.IsGrounded) break;
                }
            }
            if (!Broken(id)) throw new Exception($"could not jump-punch {id} from {Where()}");
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
                AbilitySlots.Normal1 => "1", AbilitySlots.Normal2 => "3", AbilitySlots.Normal3 => "4",
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
            Assert.Equal(TargetCount, Sim.Course.Value.BrokenTargets);
            Assert.Equal((byte)0, S.Deaths);
            for (int i = 0; i < 3; i++)
                Assert.Contains(Events, e => e.Kind == CharacterCourseEventKind.CheckpointSaved && e.Index == i);
            Assert.InRange(Sim.Course.Value.ElapsedTicks, 1ul, 180ul * 60);
        }
    }

    // ------------------------------------------------------------------ the specials, where each one is fastest

    static void MidwayBySpecials(Run r)
    {
        r.Fury("car", 1.6f, new(-1, 0));                                // the car bash by the gate
        r.Walk(3.2f, -36.5f);
        r.Cyclone(3.2f, -20f, "gallery_1", "gallery_2", "gallery_3", "gallery_4", "gallery_5"); // the shooting gallery row
        r.Walk(-3.6f, -24.9f);
        r.Rising(-3.6f, -20f, "striker_1", "striker_2", "striker_3");  // the High Striker stack
        r.Walk(-4.5f, -28f);
        r.KiShot("dunk");                                               // the dunk tank out at sea
        r.Walk(-7f, -9f);                                               // checkpoint 1
    }

    static void OldPierBySpecials(Run r)
    {
        r.Walk(-6.5f, -9f);
        r.JumpCyclone(-26f, -9f, rTick: 18, runUp: 12);                            // over the gap through the balloon line
        Assert.True(r.AllBroken("line_1", "line_2", "line_3", "line_4"),
            "the line: " + string.Join(",", new[] { "line_1", "line_2", "line_3", "line_4" }.Where(t => !r.Broken(t))) + " at " + r.Where());
        r.Fury("pinata", 1.4f, new(1, 0));
        r.Walk(-29f, -5.2f);
        r.Jump(-30f, 2.5f, runUp: 4);                                   // old deck B
        r.KiShot("bell");                                               // the bell buoy out at sea
        r.Walk(-31f, 5.4f);
        r.Jump(-31f, 12.5f, secondJumpTick: 18, runUp: 3);                                  // old deck C
        r.Walk(-32f, 14.2f);
        r.Jump(-34f, 20.5f, runUp: 3);                                  // old deck D
        r.Walk(-33.5f, 21f);
        r.Jump(-33f, 26.5f, runUp: 2);                                  // old pier head
        r.Walk(-30f, 31.5f);                                            // checkpoint 2
    }

    static void WheelBySpecials(Run r)
    {
        r.Walk(-26.5f, 29f);
        r.JumpCyclone(-9f, 29f, rTick: 16, runUp: 4);                   // over the ticket booth through its pair
        Assert.True(r.AllBroken("booth_1", "booth_2"), "the booth pair: " + r.Where());
        r.Walk(-4.4f, 27f);
        r.JumpRising(-1.5f, 27f, eTick: 6, drift: false);               // up the tower through its stack
        Assert.True(r.S.PY - r.Body > 8.5f, "on the tower: " + r.Where());
        Assert.True(r.AllBroken("tower_1", "tower_2", "tower_3"),
            "the tower stack: " + string.Join(",", new[] { "tower_1", "tower_2", "tower_3" }.Where(t => !r.Broken(t))) + " at " + r.Where());
        r.Fury("punch", 1.3f, new(-1, 0));                              // the punch machine
    }

    static void CoasterBySpecials(Run r)
    {
        r.Walk(2.6f, 27f);
        r.Jump(6f, 27f, shortHop: true);                                // lift hill, checkpoint 3
        r.Walk(7.6f, 26f);
        r.Jump(14.5f, 26f, runUp: 3);                                   // the drop
        r.Walk(16.1f, 26f);
        r.Jump(21.5f, 24f, runUp: 3);                                   // the long straight
        r.Walk(22.0f, 24.5f);
        r.Cyclone(22.0f, 40f, "coaster_1", "coaster_2", "coaster_3", "coaster_4");
        r.Walk(21.5f, 39.4f);
        r.Jump(22f, 45f, runUp: 3);                                     // coaster end
        r.KiShot("prize");                                              // the prize buoy
        r.Walk(20.4f, 45f);
        r.Jump(14f, 45f, runUp: 3);                                     // pier head
        r.Fury("bag", 1.3f, new(0, 1));                                 // the champ's heavy bag
        r.Walk(10.6f, 48f);
        r.Jump(7f, 48f);                                                // into the ring
        r.Finish(5f, 48f);
    }

    // ------------------------------------------------------------------ jumps and punches only

    static void MidwayByPunching(Run r)
    {
        r.Punch("car", range: 1.6f, from: new(-1, 0));
        foreach (var id in new[] { "gallery_1", "gallery_2", "gallery_3", "gallery_4", "gallery_5" })
            r.Punch(id, range: 1.0f, from: new(-1, 0));
        r.Punch("striker_1", range: 0.9f, from: new(0, -1));
        r.JumpPunch("striker_2", attackTick: 12, from: new(0, -1));
        r.JumpPunch("striker_3", attackTick: 30, secondJumpTick: 18, from: new(0, -1));
        r.Walk(-5.5f, -30f);
        r.Jump(-10.5f, -30f, secondJumpTick: 18);                                 // dunk buoy
        r.Walk(-10.2f, -30f);
        r.Jump(-16.5f, -30f, runUp: 1);                                 // the dunk tank
        r.Punch("dunk", range: 1.0f, from: new(1, 0));
        r.Walk(-15.5f, -30f);
        r.Jump(-10.5f, -30f, secondJumpTick: 18, runUp: 2);
        r.Walk(-10.8f, -30f);
        r.Jump(-5f, -30f, runUp: 1);
        r.Walk(-7f, -9f);                                               // checkpoint 1
    }

    static void OldPierByJumping(Run r)
    {
        r.Walk(-9.6f, -9f);
        r.Jump(-12.8f, -10.0f);                                        // piling 1
        r.JumpPunch("line_1", attackTick: 12, range: 0.75f, from: new(0, -1));
        r.Jump(-15.4f, -8.0f);                                         // piling 2
        r.JumpPunch("line_2", attackTick: 12, range: 0.75f, from: new(0, 1));
        r.Jump(-18.0f, -10.0f);                                        // piling 3
        r.JumpPunch("line_3", attackTick: 12, range: 0.75f, from: new(0, -1));
        r.Jump(-21.4f, -9f);                                           // the old landing
        r.JumpPunch("line_4", attackTick: 12, range: 0.8f, from: new(-1, 0));
        r.Punch("pinata", range: 1.4f, from: new(1, 0));
        r.Walk(-29f, -5.2f);
        r.Jump(-30f, 2.5f, runUp: 4);                                   // old deck B
        r.Walk(-35.4f, 1.6f);
        r.Jump(-42f, -0.5f, runUp: 1);                                  // stepping buoy
        r.Walk(-42.6f, -0.5f);
        r.Jump(-48.5f, -0.5f, runUp: 2);                                // bell buoy
        r.Punch("bell", range: 0.9f, from: new(1, 0));
        r.Walk(-47.6f, -0.5f);
        r.Jump(-42f, -0.5f, secondJumpTick: 16, runUp: 1);
        r.Walk(-41.6f, -0.5f);
        r.Jump(-34.5f, 2f, secondJumpTick: 16, runUp: 1);
        r.Walk(-31f, 5.4f);
        r.Jump(-31f, 12.5f, secondJumpTick: 18, runUp: 3);                                  // old deck C
        r.Walk(-32f, 14.2f);
        r.Jump(-34f, 20.5f, runUp: 3);                                  // old deck D
        r.Walk(-33.5f, 21f);
        r.Jump(-33f, 26.5f, runUp: 2);                                  // old pier head
        r.Walk(-30f, 31.5f);                                            // checkpoint 2
    }

    static void WheelByClimbing(Run r)
    {
        r.Walk(-24.6f, 29f);
        r.Jump(-18.5f, 29f, runUp: 2);                                  // ticket raft
        r.Punch("booth_1", range: 0.9f, from: new(0, -1));
        r.Punch("booth_2", range: 0.9f, from: new(0, 1));
        r.Walk(-15.6f, 29f);
        r.Jump(-10.5f, 29f, runUp: 2);                                  // wheel deck
        r.JumpPunch("tower_1", attackTick: 8, range: 0.7f, from: new(-1, 0));
        r.JumpPunch("tower_2", attackTick: 30, secondJumpTick: 18, range: 0.7f, from: new(-1, 0));
        r.Walk(-7.6f, 30.2f);
        r.Jump(-6.5f, 32f);                                             // gondola 1
        r.Walk(-5.8f, 32.6f);
        r.Jump(-3f, 33.6f);                                             // gondola 2
        r.Walk(-2.3f, 33.6f);
        r.Jump(0.5f, 33.6f);                                            // gondola 3
        r.Walk(1.2f, 33.2f);
        r.Jump(4f, 32f);                                                // gondola 4
        r.Walk(3.6f, 31.3f);
        r.Jump(1.5f, 28.5f);                                            // the tower top
        r.Punch("tower_3", AbilitySlots.Normal1, range: 1.2f, from: new(1, 0));
        r.Punch("punch", range: 1.3f, from: new(-1, 0));
    }

    static void CoasterByJumping(Run r)
    {
        r.Walk(2.6f, 27f);
        r.Jump(6f, 27f, shortHop: true);                                // lift hill, checkpoint 3
        r.Walk(7.6f, 26f);
        r.Jump(14.5f, 26f, runUp: 3);                                   // the drop
        r.Walk(16.1f, 26f);
        r.Jump(21.5f, 24f, runUp: 3);                                   // the long straight
        foreach (var id in new[] { "coaster_1", "coaster_2", "coaster_3", "coaster_4" })
            r.Punch(id, range: 0.7f, from: new(0, -1));
        r.Walk(21.5f, 39.4f);
        r.Jump(22f, 45f, runUp: 3);                                     // coaster end
        r.Walk(21.6f, 47.2f);
        r.Jump(21.5f, 51.6f, runUp: 2);                                 // prize buoy
        r.Punch("prize", range: 0.9f, from: new(0, -1));
        r.Walk(20.7f, 52f);
        r.Jump(14f, 50f, secondJumpTick: 16, runUp: 1);                 // pier head
        r.Punch("bag", range: 1.3f, from: new(0, 1));
        r.Walk(10.6f, 48f);
        r.Jump(7f, 48f);                                                // into the ring
        r.Finish(5f, 48f);
    }

    [Fact]
    public void TheSpecialsClearTheirShapesAndFinish()
    {
        var r = new Run();
        void Mark(string leg) => _output.WriteLine($"{leg} until tick {r.Inputs.Count}");
        if (Environment.GetEnvironmentVariable("SLOPARENA_ROUTE_LOG") == "1") r.Log = _output.WriteLine;
        MidwayBySpecials(r); Mark("midway");
        OldPierBySpecials(r); Mark("old pier");
        WheelBySpecials(r); Mark("wheel");
        CoasterBySpecials(r);
        r.AssertCompleted();
        foreach (var slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
            Assert.Contains(slot, r.UsedSlots);
        _output.WriteLine($"specials: {r.Sim.Course!.Value.ElapsedTicks} ticks");
        // The replay scenario's capture ticks follow these events.
        foreach (var e in r.Log != null ? r.Events : new())
            r.Log!($"t{e.ElapsedTicks} {e.Kind} {(e.Index >= 0 && e.Kind is CharacterCourseEventKind.TargetBroken or CharacterCourseEventKind.TargetHit
                ? r.Arena.Arcade!.Course!.Targets[e.Index].Id : e.Index.ToString())}");
        // Refresh the replay fixture after a deliberate layout or kit change:
        // SLOPARENA_EXPORT_COURSE_INPUTS=$PWD/scenarios/arcade dotnet test ... --filter TheSpecialsClearTheirShapesAndFinish
        string? export = Environment.GetEnvironmentVariable("SLOPARENA_EXPORT_COURSE_INPUTS");
        if (!string.IsNullOrEmpty(export))
            File.WriteAllText(Path.Combine(export, "fightguy-pier-specials.inputs.json"), r.ReplaySteps());
    }

    /// <summary>The 6 m wheel tower: a double jump and Rising Dragon alone fall short; Jump → E makes it.</summary>
    [Theory]
    [InlineData("double jump", false)]
    [InlineData("E alone", false)]
    [InlineData("jump then E", true)]
    [InlineData("double jump then E", true)]
    public void TheWheelTowerNeedsJumpThenRisingDragonOrThePods(string attempt, bool reaches)
    {
        var r = new Run(-4.4f, 3.0f, 27f, 90f);
        bool onTower = false;
        for (int i = 0; i < 180; i++)
        {
            var input = new InputState { MoveX = 1f, FacingYaw = Run.Yaw(1, 0), AimYaw = Run.Yaw(1, 0) };
            switch (attempt)
            {
                case "double jump": input.Jump = i == 0 || i == 22; input.JumpHeld = i < 40; break;
                case "E alone": if (i == 0) input.ActiveSlot = AbilitySlots.E; break;
                case "jump then E": input.Jump = i == 0; input.JumpHeld = i < 24; if (i == 8) input.ActiveSlot = AbilitySlots.E; break;
                case "double jump then E": input.Jump = i == 0 || i == 22; input.JumpHeld = i < 40; if (i == 40) input.ActiveSlot = AbilitySlots.E; break;
            }
            r.Tick(input);
            onTower |= r.S.IsGrounded && r.S.PY - r.Body > 8.9f;
            if (onTower) break;
        }
        Assert.Equal(reaches, onTower);
    }

    /// <summary>The pier's collapsed middle (20 m) is not a shortcut: a Cyclone Kick drops into the sea.</summary>
    [Theory]
    [InlineData(14)]
    [InlineData(24)]
    [InlineData(34)]
    public void TheCollapsedMiddleStopsACycloneKick(int rTick)
    {
        var r = new Run(0f, 3.0f, -4.6f, 0f);
        var ex = Assert.Throws<Exception>(() =>
        {
            for (int i = 0; i < 6; i++) r.Tick(r.Toward(0f, 20f));
            for (int i = 0; i < 300; i++)
            {
                var input = r.Toward(0f, 20f); input.Jump = i == 0; input.JumpHeld = i < 30;
                if (i == rTick) input.ActiveSlot = AbilitySlots.R;
                r.Tick(input);
                Assert.False(r.S.IsGrounded && i > 12 && r.S.PZ > 10f, "landed across the collapse: " + r.Where());
            }
        });
        Assert.Contains("Fell into the sea", ex.Message);
    }

    /// <summary>Each bonus target takes eight hits: one Fist of Fury from beside it.</summary>
    [Theory]
    [InlineData("car", 10.4f, 3.0f, -43f, 90f)]
    [InlineData("pinata", -28.6f, 3.0f, -9.5f, 270f)]
    [InlineData("punch", 0.2f, 9.0f, 27f, 90f)]
    [InlineData("bag", 13f, 3.0f, 43.3f, 180f)]
    public void OneFistOfFuryBreaksEachBonusTarget(string id, float x, float y, float z, float yaw)
    {
        var r = new Run(x, y, z, yaw);
        Assert.Equal(8, r.Target(id).Hits);
        r.Fury(id, (new Vector2(r.Target(id).Position.X - x, r.Target(id).Position.Z - z)).Length(),
            new Vector2(x - r.Target(id).Position.X, z - r.Target(id).Position.Z));
        Assert.True(r.Broken(id));
        Assert.Contains(AbilitySlots.F, r.UsedSlots);
        foreach (var slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R }) Assert.DoesNotContain(slot, r.UsedSlots);
    }

    [Fact]
    public void JumpsAndPunchesAloneFinishTheCourse()
    {
        var r = new Run();
        MidwayByPunching(r);
        OldPierByJumping(r);
        WheelByClimbing(r);
        CoasterByJumping(r);
        r.AssertCompleted();
        foreach (var slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
            Assert.DoesNotContain(slot, r.UsedSlots);
        _output.WriteLine($"jumps and punches: {r.Sim.Course!.Value.ElapsedTicks} ticks");
    }
}
