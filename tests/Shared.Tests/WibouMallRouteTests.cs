using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Neo Mall (docs/design/stages/wibou_mall.design.md) driven by the cooked Wibou kit on the baked
/// arena. Protects the course's promises: the current kit clears the route (Quick Draw and normals clear a row,
/// one Shuriken Toss fans through three agents, Rising Slash cuts the tower's lip, a close kunai volley and
/// normals break a boss; the flash is fighter-only), jumps and slashes alone also finish, the closed skybridge needs E or
/// the hanging planters and the elevator tower needs Jump, jump, E or the hanging signs.
/// Inputs are ordinary semantic inputs; the times are evidence for these inputs, not records.
/// Set SLOPARENA_COURSE_SHELL to a <c>layout.py --unity-shell</c> document to calibrate an
/// unbaked layout (CourseShellArena).
/// </summary>
public sealed class WibouMallRouteTests
{
    private readonly ITestOutputHelper _output;
    public WibouMallRouteTests(ITestOutputHelper output) => _output = output;

    internal const int TargetCount = 31;
    internal const byte BossHits = 5;

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
            return ArenaBinaryFormat.LoadFromFile(Path.Combine(root!.FullName, "data", "arenas", "wibou_mall.arena"))!.Value;
        }

        public Run(float? x = null, float? y = null, float? z = null, float yaw = 0f)
        {
            Arena = LoadArena();
            var entry = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
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
                s.FacingYaw = yaw * MathF.PI / 180f; s.AimYaw = s.FacingYaw; s.IsGrounded = true; Sim.SetState(1, s); Wait(10);
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
        public int HitsLeft(string id) => Sim.CourseTargetHitsLeft(Index(id));
        string Missing(params string[] ids) => string.Join(",", ids.Where(t => !Broken(t)));

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
                throw new Exception($"Fell to the ground floor: last grounded at {_lastGround}, last airborne at {_lastAir}");
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

        /// <summary>
        /// Jump toward (x, z) (a second jump at <paramref name="secondJumpTick"/> when set), Rising Slash at
        /// <paramref name="eTick"/> aimed that way, then steer to land within <paramref name="land"/> m of it.
        /// </summary>
        public void JumpRising(float x, float z, int eTick, int secondJumpTick = -1, int runUp = 0, float land = 3f)
        {
            for (int i = 0; i < runUp; i++) Tick(Toward(x, z));
            Assert.True(S.IsGrounded, "Grounded takeoff required: " + Where());
            for (int i = 0; i < 260; i++)
            {
                var s = S; float d = Vector2.Distance(new(s.PX, s.PZ), new(x, z));
                if (i > eTick + 20 && s.IsGrounded && d < land) { Stop(); Log?.Invoke($"t{Inputs.Count} jump E {x},{z} -> {Where()}"); return; }
                var input = Toward(x, z); input.Jump = i == 0 || i == secondJumpTick; input.JumpHeld = i < 60;
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

        /// <summary>Use fixed Quick Draw reach, then ordinary slashes for the rest of the row.</summary>
        public void QuickDrawRow(float x, float z, params string[] ids)
        {
            WaitReady(AbilitySlots.R);
            TurnTo(x, z);
            Press(AbilitySlots.R, Facing(x, z));
            WaitIdle();
            foreach (string id in ids)
                if (!Broken(id)) Slash(id);
            Assert.True(AllBroken(ids), "Quick Draw and normals missed " + Missing(ids));
        }

        /// <summary>Shuriken Toss locked on <paramref name="lockId"/>, like the client's lock-on; it must break <paramref name="ids"/>.</summary>
        public void Kunai(string lockId, params string[] ids)
        {
            var target = Target(lockId).Position;
            WaitReady(AbilitySlots.A);
            TurnTo(target.X, target.Z);
            var s = S; short yaw = Yaw(target.X - s.PX, target.Z - s.PZ);
            byte alias = Sim.CourseTargetInputAlias(Index(lockId));
            for (int i = 0; i < 24; i++)
                Tick(new() { ActiveSlot = i == 0 ? AbilitySlots.A : (byte)0, FacingYaw = yaw, AimYaw = yaw, TargetEntityId = alias });
            for (int i = 0; i < 90 && !AllBroken(ids.Length > 0 ? ids : new[] { lockId }); i++)
                Tick(new() { FacingYaw = yaw, AimYaw = yaw, TargetEntityId = alias });
            if (ids.Length > 0 && !AllBroken(ids)) throw new Exception($"Shuriken Toss missed {Missing(ids)} from {Where()}");
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

        /// <summary>A close kunai volley then normals into a boss: three hits, then slashes for the rest.</summary>
        public void Boss(string id, float range = 1.6f, Vector2? from = null)
        {
            Approach(id, range, from);
            int before = HitsLeft(id);
            Kunai(id);
            int volley = before - HitsLeft(id);
            if (volley != 3) throw new Exception($"the kunai volley landed {volley} hit(s) on {id} from {Where()}");
            Slash(id, range: range, from: from);
            Log?.Invoke($"t{Inputs.Count} boss {id} -> {Where()}");
        }

        /// <summary>Ground attacks (quick slash by default) until the target breaks.</summary>
        public void Slash(string id, byte slot = AbilitySlots.Slot1, float range = 1.0f, Vector2? from = null)
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
            if (!Broken(id)) throw new Exception($"could not slash {id} from {Where()}");
        }

        /// <summary>Jump in place (or with a second jump) and air attack at <paramref name="attackTick"/> until it breaks.</summary>
        public void JumpSlash(string id, int attackTick = 14, int secondJumpTick = -1, byte slot = AbilitySlots.Slot1,
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
            if (!Broken(id)) throw new Exception($"could not jump-slash {id} from {Where()}");
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
            Assert.Equal(TargetCount, Sim.Course.Value.BrokenTargets);
            Assert.Equal((byte)0, S.Deaths);
            for (int i = 0; i < 3; i++)
                Assert.Contains(Events, e => e.Kind == CharacterCourseEventKind.CheckpointSaved && e.Index == i);
            Assert.InRange(Sim.Course.Value.ElapsedTicks, 1ul, 180ul * 60);
        }
    }

    static readonly string[] Row = { "row_1", "row_2", "row_3", "row_4" };
    static readonly string[] Podium = { "podium_1", "podium_2", "podium_3" };
    static readonly string[] Bridge3 = { "bridge3_1", "bridge3_2", "bridge3_3", "bridge3_4" };
    static readonly string[] Food = { "food_1", "food_2", "food_3", "food_4" };
    static readonly string[] Bridge4 = { "bridge4_1", "bridge4_2", "bridge4_3", "bridge4_4" };
    static readonly string[] Peek = { "peek_1", "peek_2", "peek_3" };

    // ------------------------------------------------------------------ the specials, where each one is fastest

    static void KioskRowBySpecials(Run r)
    {
        r.Walk(-10.5f, -36.6f);
        r.QuickDrawRow(-10.5f, -20f, Row);                             // the gallery row
        r.Walk(-9.0f, -34.0f);
        r.Kunai("podium_2", Podium);                                    // the fashion podium fan
        r.Boss("tubeman", 1.6f, new(0, -1));                            // the inflatable tube man
        r.Walk(-6.5f, -12f);
        r.JumpRising(12f, -12f, eTick: 8, land: 2.5f);                  // E over the closed skybridge, checkpoint 1
    }

    static void EscalatorsBySpecials(Run r)
    {
        r.Walk(9.0f, 3.0f);
        r.Slash("escalator_1", range: 0.8f, from: new(0, -1));
        r.Walk(9.0f, 7.2f);
        r.Jump(9.0f, 10.4f, runUp: 4);                                            // over the missing steps
        r.Slash("escalator_2", range: 0.6f, from: new(0, -1));
        r.Slash("escalator_3", range: 0.8f, from: new(0, -1));
        r.Walk(9.0f, 14.2f);
        r.Boss("agent", 1.6f, new(-1, 0));                              // the department store's giant mannequin
        r.Walk(4.6f, 15.0f);
        r.QuickDrawRow(-8f, 15f, Bridge3);                             // the level-3 bridge row
        r.Walk(-11f, 15f);                                              // checkpoint 2
    }

    static void FoodCourtBySpecials(Run r)
    {
        r.Walk(-11f, 16.4f);
        r.QuickDrawRow(-11f, 30f, Food);                               // the food court row
        r.Boss("hotdog", 1.6f, new(1, 0));                              // the hot dog mascot
        r.Walk(-9.6f, 31.2f);
        r.Kunai("peek_2", Peek);                                        // the agents peeking over level 4
        r.Walk(-8.5f, 23f);
        r.JumpRising(4f, 23f, eTick: 34, secondJumpTick: 20, land: 6f); // Jump, jump, E up the elevator tower
        Assert.True(r.S.PY - r.Body > SpecL4 - 0.1f, "on level 4: " + r.Where());
        Assert.True(r.AllBroken("lip_1", "lip_2"), "the lip: " + r.Where());
    }

    static void LevelFourBySpecials(Run r)
    {
        r.Walk(-0.2f, 23f);
        r.QuickDrawRow(12f, 23f, Bridge4);                             // the top bridge row
        r.Boss("cop", 1.6f, new(0, -1));                                // the mall cop's cardboard cutout
        r.Finish(12f, 42f);
    }

    // ------------------------------------------------------------------ jumps and slashes only

    static void KioskRowBySlashing(Run r)
    {
        foreach (var id in Row) r.Slash(id, range: 1.0f, from: new(1, 0));
        r.Walk(-8.6f, -34f);
        r.Jump(-2.0f, -34f, secondJumpTick: 18);                        // over the railing onto the fashion podium
        foreach (var id in Podium) r.Slash(id, range: 1.0f, from: new(-1, 0));
        r.Walk(-2.6f, -34f);
        r.Jump(-9.8f, -34f, secondJumpTick: 18);                        // back over the railing
        r.Slash("tubeman", range: 1.6f, from: new(0, -1));
        r.Walk(-6.6f, -12f);
        r.Jump(-2.3f, -12f);                                            // planter
        r.Walk(-1.9f, -12f);
        r.Jump(2.3f, -12f);                                             // planter
        r.Walk(2.7f, -12f);
        r.Jump(7.0f, -12f);                                             // east stub
        r.Walk(12f, -12f);                                              // checkpoint 1
    }

    static void EscalatorsBySlashing(Run r)
    {
        r.Walk(9.0f, 3.0f);
        r.Slash("escalator_1", range: 0.8f, from: new(0, -1));
        r.Walk(9.0f, 7.2f);
        r.Jump(9.0f, 10.4f, runUp: 4);
        r.Slash("escalator_2", range: 0.6f, from: new(0, -1));
        r.Slash("escalator_3", range: 0.8f, from: new(0, -1));
        r.Walk(9.0f, 14.2f);
        r.Slash("agent", range: 1.6f, from: new(-1, 0));
        foreach (var id in Bridge3) r.Slash(id, range: 1.0f, from: new(1, 0));
        r.Walk(-11f, 15f);                                              // checkpoint 2
    }

    static void FoodCourtByClimbing(Run r)
    {
        foreach (var id in Food) r.Slash(id, range: 1.0f, from: new(0, -1));
        r.Slash("hotdog", range: 1.6f, from: new(1, 0));
        r.Walk(-8.6f, 27f);
        r.Jump(-6.8f, 27f);                                             // menu sign
        r.Jump(-5.2f, 29.2f);                                           // speaker
        r.Jump(-2.8f, 27f);                                             // menu sign
        r.Jump(-2.8f, 23.6f);                                           // the top bridge
        r.Walk(-7.3f, 23f);                                             // checkpoint 3 on the tower top
        r.Slash("lip_1", range: 1.2f, from: new(1, 0));
        r.Slash("lip_2", range: 1.2f, from: new(1, 0));
    }

    static void LevelFourBySlashing(Run r)
    {
        foreach (var id in Bridge4) r.Slash(id, range: 1.0f, from: new(-1, 0));
        foreach (var id in Peek) r.Slash(id, range: 1.0f, from: new(1, 0));
        r.Slash("cop", range: 1.6f, from: new(0, -1));
        r.Finish(12f, 42f);
    }

    const float SpecL4 = 15f;

    [Fact]
    public void TheSpecialsClearTheirShapesAndFinish()
    {
        var r = new Run();
        void Mark(string leg) => _output.WriteLine($"{leg} until tick {r.Inputs.Count}");
        if (Environment.GetEnvironmentVariable("SLOPARENA_ROUTE_LOG") == "1") r.Log = _output.WriteLine;
        KioskRowBySpecials(r); Mark("kiosk row");
        EscalatorsBySpecials(r); Mark("escalators");
        FoodCourtBySpecials(r); Mark("food court");
        LevelFourBySpecials(r);
        foreach (var e in r.Events.Where(e => e.Kind == CharacterCourseEventKind.CheckpointSaved))
            _output.WriteLine($"checkpoint {e.Index} saved at t{e.ElapsedTicks}");
        r.AssertCompleted();
        foreach (var slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R })
            Assert.Contains(slot, r.UsedSlots);
        Assert.DoesNotContain(AbilitySlots.F, r.UsedSlots);
        _output.WriteLine($"specials: {r.Sim.Course!.Value.ElapsedTicks} ticks");
        foreach (var e in r.Log != null ? r.Events : new())
            r.Log!($"t{e.ElapsedTicks} {e.Kind} {(e.Index >= 0 && e.Kind is CharacterCourseEventKind.TargetBroken or CharacterCourseEventKind.TargetHit
                ? r.Arena.Arcade!.Course!.Targets[e.Index].Id : e.Index.ToString())}");
        // Refresh the replay fixture after a deliberate layout or kit change:
        // SLOPARENA_EXPORT_COURSE_INPUTS=$PWD/scenarios/arcade dotnet test ... --filter TheSpecialsClearTheirShapesAndFinish
        string? export = Environment.GetEnvironmentVariable("SLOPARENA_EXPORT_COURSE_INPUTS");
        if (!string.IsNullOrEmpty(export))
            File.WriteAllText(Path.Combine(export, "wibou-mall-specials.inputs.json"), r.ReplaySteps());
    }

    [Fact]
    public void JumpsAndSlashesAloneFinishTheCourse()
    {
        var r = new Run();
        if (Environment.GetEnvironmentVariable("SLOPARENA_ROUTE_LOG") == "1") r.Log = _output.WriteLine;
        KioskRowBySlashing(r);
        EscalatorsBySlashing(r);
        FoodCourtByClimbing(r);
        LevelFourBySlashing(r);
        r.AssertCompleted();
        foreach (var slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
            Assert.DoesNotContain(slot, r.UsedSlots);
        _output.WriteLine($"jumps and slashes: {r.Sim.Course!.Value.ElapsedTicks} ticks");
    }

    /// <summary>The skybridge's missing middle: a double jump falls short; E crosses from the stub.</summary>
    [Theory]
    [InlineData("double jump", false)]
    [InlineData("E", true)]
    [InlineData("jump then E", true)]
    public void TheClosedSkybridgeNeedsEOrThePlanters(string attempt, bool crosses)
    {
        var r = new Run(-6.5f, 4.0f, -12f, 90f);
        bool across = false;
        try
        {
            for (int i = 0; i < 240 && !across; i++)
            {
                var input = r.Toward(12f, -12f);
                switch (attempt)
                {
                    case "double jump": input.Jump = i == 0 || i == 20; input.JumpHeld = i < 60; break;
                    case "E": if (i == 0) input.ActiveSlot = AbilitySlots.E; break;
                    case "jump then E": input.Jump = i == 0; input.JumpHeld = i < 30; if (i == 8) input.ActiveSlot = AbilitySlots.E; break;
                }
                r.Tick(input);
                across = r.S.IsGrounded && r.S.PX > 6f;
            }
        }
        catch (Exception e) when (e.Message.StartsWith("Fell")) { }
        Assert.Equal(crosses, across);
    }

    /// <summary>The 6 m elevator tower: a double jump, E alone and Jump → E fall short; Jump, jump, E makes it.</summary>
    [Theory]
    [InlineData("double jump", false)]
    [InlineData("E alone", false)]
    [InlineData("jump then E", false)]
    [InlineData("jump, jump, E", true)]
    public void TheElevatorTowerNeedsJumpJumpEOrTheSigns(string attempt, bool reaches)
    {
        var r = new Run(-8.5f, 9.0f, 23f, 90f);
        bool up = false;
        for (int i = 0; i < 200 && !up; i++)
        {
            var input = new InputState { MoveX = 1f, FacingYaw = Run.Yaw(1, 0), AimYaw = Run.Yaw(1, 0) };
            switch (attempt)
            {
                case "double jump": input.Jump = i == 0 || i == 20; input.JumpHeld = i < 60; break;
                case "E alone": if (i == 0) input.ActiveSlot = AbilitySlots.E; break;
                case "jump then E": input.Jump = i == 0; input.JumpHeld = i < 30; if (i == 10) input.ActiveSlot = AbilitySlots.E; break;
                case "jump, jump, E": input.Jump = i == 0 || i == 20; input.JumpHeld = i < 60; if (i == 34) input.ActiveSlot = AbilitySlots.E; break;
            }
            r.Tick(input);
            up = r.S.IsGrounded && r.S.PY - r.Body > SpecL4 - 0.1f;
        }
        Assert.Equal(reaches, up);
    }

    /// <summary>Each boss takes five hits: a close kunai volley lands three and normals the rest.</summary>
    [Theory]
    [InlineData("tubeman", -12f, 4f, -20.6f)]
    [InlineData("agent", 11.9f, 9f, 15f)]
    [InlineData("hotdog", -11.4f, 9f, 28.5f)]
    [InlineData("cop", 12.5f, 15f, 35.9f)]
    public void AKunaiVolleyAndNormalsBreakEachBoss(string id, float x, float y, float z)
    {
        var r = new Run(x, y, z, 0f);
        Assert.Equal(BossHits, r.Target(id).Hits);
        var p = r.Target(id).Position;
        r.Boss(id, new Vector2(p.X - x, p.Z - z).Length(), new Vector2(x - p.X, z - p.Z));
        Assert.True(r.Broken(id));
        Assert.Contains(AbilitySlots.A, r.UsedSlots);
        Assert.DoesNotContain(AbilitySlots.F, r.UsedSlots);
    }
}
