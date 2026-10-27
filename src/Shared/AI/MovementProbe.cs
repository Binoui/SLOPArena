using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Movement data sheet probe (issue #150): drives a character through the REAL
    /// ServerSimulation with scripted inputs (hold run, shield, full jump, double jump,
    /// fast fall, drift, stop) and samples position/velocity per tick. Every metric is
    /// derived from the samples — no formulas re-derived from authored constants, so the
    /// report reflects actual server-authoritative behavior (acceleration curves, float windows, caps).
    ///
    /// Pure deterministic function of (def, arena): no statics, no RNG. Two calls with
    /// the same inputs yield identical samples. Mirrors SelfPlayMatch's "scenario in
    /// Shared, tool renders, tests assert" split.
    /// </summary>
    public static partial class MovementProbe
    {
        // ── Model ──────────────────────────────────────────────────────────────

        public sealed record MovementSample(int Tick, float PosX, float PosY, float Speed, float Vy, bool IsGrounded, ActionState State);

        public sealed record RunMetrics(float MaxSpeed, int TimeToMaxTicks, float DistanceToMax, string Note,
            MovementSample[] Curve);

        public sealed record ShieldMetrics(int StopTicks, float StopDistance, int HoldTicks,
            MovementSample[] Curve);

        /// <summary>Shared shape for the three jump probes: apex / airtime / horizontal distance.</summary>
        public sealed record JumpMetrics(float ApexHeight, int TimeToApexTicks, int AirtimeTicks,
            float HorizontalDistance, float DriftSpeedMax, MovementSample[] Curve);

        public sealed record FallMetrics(float MaxFallSpeed, int TimeToMaxFallTicks, int DescentTicks,
            MovementSample[] NaturalCurve, float FastFallSpeed, int FastFallReachTicks, int FastFallDescentTicks,
            MovementSample[] FastFallCurve, int FastFallFromJumpTicks);

        public sealed record StopMetrics(int StopTicks, float StopDistance, MovementSample[] Curve);

        public sealed record ReversalMetrics(int ReversalTicks, float Displacement, MovementSample[] Curve);

        public sealed record CharacterMovement(string Character, MovementStats Authored, RunMetrics Run,
            ShieldMetrics Shield, JumpMetrics Jump, JumpMetrics RunningJump, JumpMetrics DoubleJump,
            JumpMetrics ShortHop, FallMetrics Fall, StopMetrics Stop, ReversalMetrics Reversal);

        // ── Scenarios ──────────────────────────────────────────────────────────

        /// <summary>Measure every movement scenario for one character on a flat arena.</summary>
        public static CharacterMovement Measure(CharacterDefinition def, ArenaDefinition arena)
        {
            float groundY = def.CapsuleHeight * 0.5f;
            MovementStats m = def.Movement;

            // Run: hold right from standstill.
            var run = RunSim(def, arena, groundY, t => Input(right: true), 60);
            float runMax = Max(run, s => s.Speed);
            int runTimeToMax = First(run, s => s.Speed >= runMax * 0.999f, 0);
            float runDistToMax = run[runTimeToMax].PosX;
            string runNote = "";
            var runMetrics = new RunMetrics(runMax, runTimeToMax, runDistToMax, runNote, run.ToArray());


            // Shield: reach cruise, then raise shield and hold it for 30 ticks.
            var shield = RunSim(def, arena, groundY,
                t => t < 30 ? Input(right: true) : Input(shield: true, shieldPressed: t == 30), 60);
            int shieldStart = First(shield, s => s.State == ActionState.Shielding, 30);
            var shieldMetrics = new ShieldMetrics(shieldStart - 29,
                shield[shieldStart].PosX - shield[29].PosX,
                Count(shield, s => s.Tick >= shieldStart && s.State == ActionState.Shielding),
                shield.ToArray());

            // Full jump from standstill, stick held right (drift flight) — one sim gives
            // apex/airtime/horizontal AND the air-drift speed cap. Drift is the max speed
            // while AIRBORNE only — the curve continues past landing, where ground acceleration would pollute the cap.
            var jump = RunSim(def, arena, groundY, JumpHold(right: true), 300);
            int jumpTakeoff = First(jump, s => !s.IsGrounded, 0);
            int jumpLanding = FirstLanding(jump, jumpTakeoff);
            int jumpApex = ArgMax(jump, s => s.PosY);
            float driftMax = 0f;
            for (int i = jumpTakeoff; i < jumpLanding; i++)
                if (jump[i].Speed > driftMax) driftMax = jump[i].Speed;
            var jumpMetrics = new JumpMetrics(
                jump[jumpApex].PosY - groundY, jumpApex - jumpTakeoff, jumpLanding - jumpTakeoff,
                jump[jumpLanding].PosX - jump[jumpTakeoff].PosX, driftMax, jump.ToArray());

            // Apex tick of a straight (no-stick) jump — reused to branch the double-jump
            // scenario (identical inputs → identical apex).
            var straight = RunSim(def, arena, groundY, JumpHold(right: false), 300);
            int straightTakeoff = First(straight, s => !s.IsGrounded, 0);
            int straightApex = ArgMax(straight, s => s.PosY);

            // Running jump: reach cruise first (30 ticks), then full jump, stick held.
            var running = RunSim(def, arena, groundY,
                t => t < 30 ? Input(right: true)
                    : t < 30 + 10 ? Input(jump: t == 30, jumpHeld: true, right: true)
                    : Input(right: true), 300);
            int runTakeoff = First(running, s => !s.IsGrounded, 0);
            int runLanding = FirstLanding(running, runTakeoff);
            int runApex = ArgMax(running, s => s.PosY);
            var runningJump = new JumpMetrics(
                running[runApex].PosY - groundY, runApex - runTakeoff, runLanding - runTakeoff,
                running[runLanding].PosX - running[runTakeoff].PosX, Max(running, s => s.Speed), running.ToArray());

            // Double jump: at the straight jump's apex, one jump edge (keeps right hold).
            var dbl = RunSim(def, arena, groundY,
                t => t < straightApex ? JumpHold(right: true)(t)
                    : t == straightApex ? Input(jump: true, right: true)
                    : t < straightApex + 10 ? Input(jumpHeld: true, right: true)
                    : Input(right: true), 300);
            int dblTakeoff = First(dbl, s => !s.IsGrounded, 0);
            int dblLanding = FirstLanding(dbl, dblTakeoff);
            int dblApex = ArgMax(dbl, s => s.PosY);
            var doubleJump = new JumpMetrics(
                dbl[dblApex].PosY - groundY, dblApex - dblTakeoff, dblLanding - dblTakeoff,
                dbl[dblLanding].PosX - dbl[dblTakeoff].PosX, Max(dbl, s => s.Speed), dbl.ToArray());

            // Fall / fast fall: a full jump only falls ~0.3s — far short of the ~1.3s of
            // gravity needed to reach MaxFallSpeed. Measure the fall regime with a long
            // drop instead: spawn airborne at 50 m, AirTimeTicks past the float window so
            // full Gravity applies from the first tick. Natural drop vs hold-Down fast fall.
            var natural = DropSim(def, arena, groundY, down: false);
            var fast = DropSim(def, arena, groundY, down: true);
            float maxFall = -Min(natural, s => s.Vy);
            int timeToMaxFall = First(natural, s => s.Vy <= -m.MaxFallSpeed * 0.999f, natural.Count - 1);
            int naturalLanding = FirstLanding(natural, 0);
            int fastLanding = FirstLanding(fast, 0);
            // Request fast fall once the real pre-step state is descending.
            var jumpFast = RunSim(def, arena, groundY,
                JumpHold(right: false), 300, fastFall: true);
            int jumpFastLanding = FirstLanding(jumpFast, straightTakeoff);
            var fallMetrics = new FallMetrics(maxFall, timeToMaxFall, naturalLanding,
                natural.ToArray(), -Min(fast, s => s.Vy),
                First(fast, s => s.Vy <= -m.FastFallSpeed * 0.999f, fast.Count - 1),
                fastLanding, fast.ToArray(), jumpFastLanding - straightApex);

            // Short hop: press + release inside the short-hop window (tick-0 press only,
            // no JumpHeld sustain) — squat expiry applies ShortHopForce.
            var shortHop = RunSim(def, arena, groundY,
                t => t == 0 ? Input(right: true, jump: true, jumpHeld: true) : Input(right: true), 200);
            int shTakeoff = First(shortHop, s => !s.IsGrounded, 0);
            int shLanding = FirstLanding(shortHop, shTakeoff);
            int shApex = ArgMax(shortHop, s => s.PosY);
            float shDrift = 0f;
            for (int i = shTakeoff; i < shLanding; i++)
                if (shortHop[i].Speed > shDrift) shDrift = shortHop[i].Speed;
            var shortHopMetrics = new JumpMetrics(
                shortHop[shApex].PosY - groundY, shApex - shTakeoff, shLanding - shTakeoff,
                shortHop[shLanding].PosX - shortHop[shTakeoff].PosX, shDrift, shortHop.ToArray());

            // Stop: cruise 30 ticks, release everything.
            var stop = RunSim(def, arena, groundY, t => t < 30 ? Input(right: true) : default, 90);
            int stopTick = First(stop, s => s.Tick >= 30 && s.Speed < 0.01f, 30);
            var stopMetrics = new StopMetrics(stopTick - 29, stop[stopTick].PosX - stop[29].PosX, stop.ToArray());

            // Reversal: cruise right 30 ticks, then full opposite input.
            var rev = RunSim(def, arena, groundY, t => t < 30 ? Input(right: true) : Input(left: true), 90);
            int revDone = First(rev, s => s.Tick >= 30 && s.Speed >= m.RunSpeed * 0.99f, 89);
            var reversalMetrics = new ReversalMetrics(revDone - 29, Math.Abs(rev[revDone].PosX - rev[29].PosX),
                rev.ToArray());

            return new CharacterMovement(def.DisplayName, m, runMetrics, shieldMetrics, jumpMetrics,
                runningJump, doubleJump, shortHopMetrics, fallMetrics, stopMetrics, reversalMetrics);
        }

        // ── Sim driving ────────────────────────────────────────────────────────

        private static InputState Input(bool right = false, bool left = false, bool jump = false,
            bool jumpHeld = false, bool down = false, bool shield = false, bool shieldPressed = false) => new()
        {
            MoveX = right ? 1f : left ? -1f : 0f,
            Jump = jump,
            JumpHeld = jumpHeld,
            Down = down,
            ShieldHeld = shield,
            ShieldPressed = shieldPressed,
        };

        /// <summary>Full-jump input plan: jump edge + held on tick 0, held through the squat
        /// window (10 ticks), then stick-only. Releasing JumpHeld inside ShortHopWindowTicks
        /// would yield a SHORT hop — holding past it forces the full JumpForce.</summary>
        private static Func<int, InputState> JumpHold(bool right) => t =>
            t == 0 ? Input(right: right, jump: true, jumpHeld: true)
            : t < 10 ? Input(right: right, jumpHeld: true)
            : Input(right: right);

        private static List<MovementSample> RunSim(CharacterDefinition def, ArenaDefinition arena,
            float groundY, Func<int, InputState> inputFor, int ticks, bool fastFall = false)
        {
            var sim = new ServerSimulation(arena);
            var state = new CharacterState
            {
                PX = 0f, PY = groundY, PZ = 0f,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = def.Movement.MaxJumps,
                AirDodgesLeft = 1,
                FacingYaw = 0,
            };
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState>();
            var samples = new List<MovementSample>(ticks);
            bool downPressed = false;
            for (int t = 0; t < ticks; t++)
            {
                inputs[1] = inputFor(t);
                if (fastFall)
                {
                    var input = inputs[1];
                    input.DownPressed = !downPressed && !sim.GetState(1).IsGrounded && sim.GetState(1).VY < 0f;
                    downPressed |= input.DownPressed;
                    input.Down = downPressed;
                    inputs[1] = input;
                }
                sim.Tick(inputs);
                var s = sim.GetState(1);
                samples.Add(new MovementSample(t, s.PX, s.PY,
                    MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ), s.VY, s.IsGrounded, s.State));            }
            return samples;
        }

        private static List<MovementSample> DropSim(CharacterDefinition def, ArenaDefinition arena,
            float groundY, bool down)
        {
            var sim = new ServerSimulation(arena);
            var state = new CharacterState
            {
                PX = 0f, PY = DropAltitude, PZ = 0f,
                VY = 0f,
                State = ActionState.Idle,
                IsGrounded = false,
                // Past the float window: full Gravity applies from the first tick.
                AirTimeTicks = def.Movement.FloatWindowTicks,
                JumpsLeft = def.Movement.MaxJumps,
                AirDodgesLeft = 1,
                FacingYaw = 0,
            };
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState>();
            var samples = new List<MovementSample>(DropTicks);
            bool downPressed = false;
            for (int t = 0; t < DropTicks; t++)
            {
                var input = down ? Input(down: true) : default;
                input.DownPressed = down && !downPressed && sim.GetState(1).VY < 0f;
                downPressed |= input.DownPressed;
                inputs[1] = input;
                sim.Tick(inputs);
                var s = sim.GetState(1);
                samples.Add(new MovementSample(t, s.PX, s.PY,
                    MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ), s.VY, s.IsGrounded, s.State));
            }
            return samples;
        }

        /// <summary>Drop altitude in meters — long enough to reach MaxFallSpeed (needs
        /// ~MaxFallSpeed/Gravity = 1.3s of gravity; 50 m takes ~2s).</summary>
        public const float DropAltitude = 50f;
        private const int DropTicks = 300;

        // ── Sample derivation (no Linq — Shared targets netstandard2.1) ────────

        private static float Max(List<MovementSample> samples, Func<MovementSample, float> f)
        {
            float m = float.MinValue;
            foreach (var s in samples) { float v = f(s); if (v > m) m = v; }
            return m;
        }

        private static float Min(List<MovementSample> samples, Func<MovementSample, float> f)
        {
            float m = float.MaxValue;
            foreach (var s in samples) { float v = f(s); if (v < m) m = v; }
            return m;
        }

        private static int First(List<MovementSample> samples, Func<MovementSample, bool> pred, int fallback)
        {
            for (int i = 0; i < samples.Count; i++) if (pred(samples[i])) return i;
            return fallback;
        }

        private static int Count(List<MovementSample> samples, Func<MovementSample, bool> pred)
        {
            int n = 0;
            foreach (var s in samples) if (pred(s)) n++;
            return n;
        }

        private static int ArgMax(List<MovementSample> samples, Func<MovementSample, float> f)
        {
            int best = 0;
            for (int i = 1; i < samples.Count; i++) if (f(samples[i]) > f(samples[best])) best = i;
            return best;
        }

        private static int ArgMin(List<MovementSample> samples, Func<MovementSample, float> f)
        {
            int best = 0;
            for (int i = 1; i < samples.Count; i++) if (f(samples[i]) < f(samples[best])) best = i;
            return best;
        }

        private static int FirstLanding(List<MovementSample> samples, int afterTick)
        {
            for (int i = afterTick; i < samples.Count; i++)
                if (samples[i].IsGrounded) return i;
            return samples.Count - 1;
        }
    }
}
