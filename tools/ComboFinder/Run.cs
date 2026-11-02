using SlopArena.Shared;
using SlopArena.Shared.AI;
namespace SlopArena.ComboFinder;

sealed class Runner
{
    const ulong A = 1, V = 2;
    readonly ArenaDefinition arena; readonly MatchContentEntry att, vic; readonly CharacterState startA, startV; readonly int seed, Seg;
    internal Runner(Content content, FighterTape a, FighterTape v, int seed = 1, int seg = 6) { arena = content.Arena; att = content.Resolve(a.Package); vic = content.Resolve(v.Package); this.seed = seed; Seg = seg; startA = Start(att, a.Feet[0], a.Feet[2], a.Yaw, a.Damage); startV = Start(vic, v.Feet[0], v.Feet[2], v.Yaw, v.Damage); }
    CharacterState Start(MatchContentEntry entry, float x, float z, float yawDeg, ushort dmg)
    {
        float feet = arena.Heightmap.Sample(x, z);
        float yaw = yawDeg * (MathF.PI * 2f / 360f);
        return new CharacterState
        {
            PX = x,
            PY = feet + entry.Definition.CapsuleHeight * 0.5f,
            PZ = z,
            FacingYaw = yaw,
            AimYaw = yaw,
            IsGrounded = true,
            JumpsLeft = entry.Definition.Movement.MaxJumps,
            AirDodgesLeft = 1,
            DamagePercent = dmg,
            MatchState = MatchState.Playing
        };
    }
    // Attacker action vocabulary: each entry fills Seg ticks of input relative to the victim.
    static readonly string[] slots = { "1", "2", "3", "A", "E", "R", "F" };
    internal static readonly HashSet<byte> normalWires = new HashSet<byte> { Slot("1"), Slot("2"), Slot("3") };
    internal static readonly List<string> actions = new List<string> { "idle", "toward", "away", "left", "right", "jump", "jumpToward", "dashToward", "dashAway", "shield", "grab", "down" };
    static Runner()
    {
        foreach (var s in slots) { actions.Add("press" + s); actions.Add("pressToward" + s); }
        foreach (var s in new[] { "1", "2", "3" }) actions.Add("hopToward" + s);
        actions.Add("aimA"); actions.Add("aimTowardA");
    }
    internal static readonly string[] moveActions = { "idle", "toward", "away", "left", "right", "jump", "jumpToward", "dashToward", "dashAway", "shield", "down" };
    // Route token -> the press actions that can start it.
    internal static IEnumerable<string> PressesFor(string token)
    {
        if (token == "beeline") return new[] { "dashToward" };
        if (token == "juggle") return new[] { "1", "2", "3" }.SelectMany(n => new[] { "press" + n, "pressToward" + n, "hopToward" + n });
        if (token == "A") return new[] { "aimA", "aimTowardA" };
        string label = token.Length == 2 && (token[0] == 'a' || token[0] == 'g') ? token[1..] : token;
        var list = new List<string> { "press" + label, "pressToward" + label };
        if (token[0] == 'a') list.Add("hopToward" + label);
        return list;
    }

    static short Wire(float deg) { float d = ((deg % 360f) + 540f) % 360f - 180f; return (short)Math.Clamp((int)MathF.Round(d * 100f), -18000, 18000); }
    internal static byte Slot(string s)
    {
        if (!CanonicalSlotProjection.TryGet(false, s, out var g)) throw new ArgumentException($"Invalid slot {s}");
        return CanonicalSlotProjection.ToWire(g);
    }

    InputState AttackerInput(string action, int k, in CharacterState self, in CharacterState target)
    {
        float dx = target.PX - self.PX, dz = target.PZ - self.PZ;
        float to = MathF.Atan2(dx, dz) * 180f / MathF.PI;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        var input = new InputState { TargetEntityId = (byte)V, AimDistance = (ushort)Math.Clamp((int)(dist * 100f), 0, 6500), AimYaw = Wire(to), FacingYaw = Wire(to) };
        void Move(float yaw) { float r = yaw * MathF.PI / 180f; input.MoveX = MathF.Sin(r); input.MoveY = MathF.Cos(r); }
        switch (action)
        {
            case "toward": Move(to); break;
            case "away": Move(to + 180f); input.AimYaw = input.FacingYaw = Wire(to); break;
            case "left": Move(to - 90f); break;
            case "right": Move(to + 90f); break;
            case "jump": input.JumpHeld = k < 4; input.Jump = k == 0; break;
            case "jumpToward": input.JumpHeld = k < 4; input.Jump = k == 0; Move(to); break;
            case "dashToward": input.DashPressed = k == 0; Move(to); break;
            case "dashAway": input.DashPressed = k == 0; Move(to + 180f); break;
            case "shield": input.ShieldHeld = true; input.ShieldPressed = k == 0; break;
            case "grab": input.GrabPressed = k == 0; break;
            case "aimA": if (k == 0) input.ActiveSlot = Slot("A"); input.IsAiming = k < 5; break;
            case "aimTowardA": if (k == 0) input.ActiveSlot = Slot("A"); input.IsAiming = k < 5; Move(to); break;
            case "down": input.Down = true; input.DownPressed = k == 0; break;
            default:
                if (action.StartsWith("hopToward")) { input.JumpHeld = k < 4; input.Jump = k == 0; Move(to); if (k == 5) input.ActiveSlot = Slot(action[9..]); }
                else if (action.StartsWith("pressToward")) { if (k == 0) input.ActiveSlot = Slot(action[11..]); Move(to); }
                else if (action.StartsWith("press") && k == 0) input.ActiveSlot = Slot(action[5..]);
                break;
        }
        return input;
    }

    internal RunTrace Run(List<string> plan, int lookahead, bool record, InputState[]? tapeA = null, InputState[]? tapeV = null)
    {
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        sim.RegisterEntity(A, att.Definition, startA, att.BakedAnimation);
        sim.RegisterEntity(V, vic.Definition, startV, vic.BakedAnimation);
        sim.SetRespawnPosition(A, startA.PX, startA.PY, startA.PZ, startA.FacingYaw);
        sim.SetRespawnPosition(V, startV.PX, startV.PY, startV.PZ, startV.FacingYaw);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var rng = new Random(seed);
        var inputs = new Dictionary<ulong, InputState>();
        var r = new RunTrace();
        int total = tapeA?.Length ?? (plan.Count * Seg + lookahead);
        var instances = new Dictionary<ulong, int>();
        var sequences = new Dictionary<byte, int>();
        for (int t = 0; t < total; t++)
        {
            var a = sim.GetState(A); var v = sim.GetState(V);
            string action = t < plan.Count * Seg ? plan[t / Seg] : "idle";
            var ia = tapeA == null ? AttackerInput(action, t % Seg, a, v) : tapeA[t];
            policy.ObserveOpponent(memory, a, sim.GetDefinition(A), att.BakedAnimation, sim.GetActiveMove(A)?.Slot);
            var iv = policy.Decide(v, vic.Definition.ForForm(v.Form.Active), rng, memory, arena, vic.BakedAnimation,
                CpuBehavior.Attack, sim.GetActiveMove(V)?.Slot, canSwapCancel: sim.CanSwapCancel(V));
            if (tapeV != null) iv = tapeV[t];
            inputs[A] = ia; inputs[V] = iv;
            if (record) { r.TapeA.Add(ia); r.TapeV.Add(iv); }
            byte aDeaths = a.Deaths, vDeaths = v.Deaths;
            sim.Tick(inputs);
            int tick = t + 1;
            var a2 = sim.GetState(A); var v2 = sim.GetState(V);
            if (a2.AttackSequence != a.AttackSequence)
            {
                sequences[a2.AttackSequence] = r.Moves.Count;
                bool air = sim.GetActiveMove(A)?.AirborneAtStart ?? !a.IsGrounded;
                r.Moves.Add(new(tick, a2.AttackSlot, !air));
            }
            foreach (var h in sim.LastTickHits)
            {
                int moveIndex = -1;
                if (h.OwnerEntityId == A && !instances.TryGetValue(h.ActivationId, out moveIndex))
                {
                    if (!sequences.TryGetValue(h.AttackSequence, out moveIndex))
                    {
                        moveIndex = r.Moves.Count;
                        r.Moves.Add(new(tick, h.AttackSlot, !h.Airborne));
                    }
                    instances[h.ActivationId] = moveIndex;
                }
                r.Hits.Add(new Hit(tick, h.OwnerEntityId, h.AttackSlot, h.Damage, h.Blocked, moveIndex));
            }
            bool victimFree = v2.HitstunTicks == 0 && !v2.InPostHitstunFlight && v2.State != ActionState.Hitstun && v2.State != ActionState.Grabbed;
            r.VictimFree.Add(victimFree);
            r.AttackerAir.Add(!a2.IsGrounded);
            r.VictimAir.Add(!v2.IsGrounded);
            if (a2.State == ActionState.HomingDashFloat && a.State != ActionState.HomingDashFloat) r.Beelines.Add(tick);
            r.AttackerPos.Add(new(a2.PX, a2.PY, a2.PZ));
            r.VictimPos.Add(new(v2.PX, v2.PY, v2.PZ));
            if (v2.State == ActionState.Attacking && v2.AttackSlot != 0 && v2.AttackSequence != v.AttackSequence)
            {
                r.VictimSwings.Add(new(tick, v2.AttackSlot));
                float d = MathF.Sqrt((a2.PX - v2.PX) * (a2.PX - v2.PX) + (a2.PZ - v2.PZ) * (a2.PZ - v2.PZ));
                if (d < 4f && !r.Hits.Any(h => h.Owner == A && !h.Blocked)) r.CloseVictimSwings++;
            }
            if (sim.LastTickHits.Any(h => h.OwnerEntityId == A && h.AttackSlot == Slot("F"))) r.ThrowEdgeDistances[tick] = EdgeDistance(v2.PX, v2.PZ, v2.PX - a2.PX, v2.PZ - a2.PZ);
            r.Ticks = tick;
            if (v2.Deaths > vDeaths) { r.Deaths.Add(new(tick, V)); r.VictimKo = tick; }
            if (a2.Deaths > aDeaths) { r.Deaths.Add(new(tick, A)); r.AttackerKo = tick; }
            if (r.VictimKo >= 0 || r.AttackerKo >= 0) break;
        }
        return r;
    }
    float EdgeDistance(float x, float z, float dx, float dz)
    {
        float len = MathF.Sqrt(dx * dx + dz * dz); if (len < 1e-4f) return 30f;
        dx /= len; dz /= len;
        for (float d = 0; d < 30f; d += 0.25f)
            if (arena.Heightmap.Sample(x + dx * d, z + dz * d) == float.MinValue) return d;
        return 30f;
    }

}
