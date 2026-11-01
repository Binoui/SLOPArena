using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

internal static partial class ComboRouteReport
{
    private sealed record SearchNode(Replay Replay, Outcome Outcome, string Di, string Grammar, int Drift);
    private static readonly string[] GroundNormals = { "ground.1", "ground.2", "ground.3" };
    private static readonly string[] AirNormals = { "air.1", "air.2", "air.3" };
    private static readonly (float X, float Z)[] DriftInputs = { (0f, 0f), (0f, 1f), (-.5f, .8660254f), (.5f, .8660254f) };
    private static readonly int[] GroundOffsets = { 0, 1, 3, 6, 12, 24 };
    private static readonly int[] AirOffsets = { 0, 2, 5, 9, 14, 20 };
    private static readonly int[] LandingOffsets = { 0, 2, 5, 9, 16 };

    internal static SearchReport Search(Context context, SearchOptions options)
    {
        options.Validate();
        var report = new SearchReport
        {
            Options = options,
            Grammar = new[] { "ground starter", "ground -> ground", "ground -> ground -> explicit jump -> air",
                "ground -> ground -> air -> fast-fall -> landing -> ground" },
            Limitations = new[]
            {
                "Finite staged prefix search, not exhaustive input-space search; no optimum or impossibility claim.",
                $"At most {options.PrefixWidth} frontier prefixes per DI direction, diversified by starter slot.",
                "Flat declared arena, mirror FightGuy, fixed camera yaw and always-on target lock; not production-stage KO balance.",
                "Prescribed neutral/forward/left-oblique/right-oblique drift; short/full jump holds 2/8 ticks.",
                "Fast-fall witnesses require an observed post-tick latch; a latch that starts and ends on the same landing tick is not proven by this observer.",
                "Ground offsets 0,1,3,6,12,24; air offsets 0,2,5,9,14,20; landing offsets 0,2,5,9,16.",
                "DI axes use initial +Z away/-Z in and world X left/right; held only during observed hitstop/hitstun.",
                "Discovery counts include jump/fast-fall preparations and prefix observations; measurements have a separate hard budget.",
                "Rank: connected actions, damage, shorter conversion, stable ID. This is not a balance score.",
            },
        };
        var roots = new List<SearchNode>();
        var grounded = new List<SearchNode>();
        var aerials = new List<SearchNode>();
        var retained = new List<SearchNode>();
        var perDi = new Dictionary<string, SearchNode>(StringComparer.Ordinal);
        int used = 0, measured = 0;
        int rootLimit = Math.Max(1, options.Budget / 5);
        int groundLimit = Math.Max(rootLimit, options.Budget * 2 / 5);
        int airLimit = Math.Max(groundLimit, options.Budget * 4 / 5);
        Seed();
        GroundStage();
        AirStage();
        LandingStage();
        var selected = new List<SearchNode>(retained);
        foreach (string di in options.DiDirections)
        {
            if (perDi.TryGetValue(di, out var node))
            {
                if (!selected.Any(n => n.Replay.Id == node.Replay.Id)) selected.Add(node);
                report.DiConditionedBest[di] = node.Replay.Id;
            }
            else report.DiConditionedBest[di] = null;
        }
        selected.Sort(CompareNodes);
        foreach (var node in selected)
        {
            node.Replay.ExpectedOutcomeHash = OutcomeHash(node.Outcome);
            report.Witnesses.Add(new Witness { Id = node.Replay.Id, Di = node.Di, Grammar = node.Grammar,
                Replay = node.Replay, Outcome = node.Outcome });
        }
        // Robustness receives first access to the measurement cap, then timing refinements.
        CompareFixedDi();
        MeasureTiming();
        report.CandidatesRun = used;
        report.MeasurementsRun = measured;
        report.BudgetExhausted = used >= options.Budget;
        report.MeasurementBudgetExhausted = measured >= options.MeasurementBudget;
        foreach (string stage in new[] { "starter", "grounded-followup", "jump-aerial", "fastfall-landing" })
            if (!report.GrammarAttempts.ContainsKey(stage))
                report.Limitations = report.Limitations.Append($"No '{stage}' execution fit the available frontier/horizon/budget.").ToArray();
        return report;

        void Seed()
        {
            foreach (string slot in GroundNormals)
            for (int drift = 0; drift < DriftInputs.Length; drift++)
            foreach (int pct in options.Percents)
            foreach (float distance in options.Distances)
            foreach (string di in options.DiDirections)
            {
                if (used >= rootLimit || used >= options.Budget) return;
                var replay = Fresh(context, options.Horizon, pct, distance);
                AddAction(replay, 0, slot);
                SetDrift(replay, 0, Math.Min(45, options.Horizon), drift);
                if (Evaluate(replay, di, "starter", drift, rootLimit, out var node)) AddFrontier(roots, node!);
            }
        }
        void GroundStage()
        {
            var frontier = Frontier(roots);
            foreach (int offset in GroundOffsets)
            foreach (var parent in frontier)
            foreach (string slot in GroundNormals)
            {
                if (used >= groundLimit || used >= options.Budget) return;
                int anchor = NextOrdinaryTick(parent);
                int tick = anchor + offset;
                if (!Fits(parent.Replay, tick)) { Exclude("ground-outside-horizon"); continue; }
                var replay = Extend(parent.Replay);
                AddAction(replay, tick, slot);
                SetDrift(replay, parent.Replay.Actions[^1].Tick + 1, options.Horizon, parent.Drift);
                if (Evaluate(replay, parent.Di, "grounded-followup", parent.Drift, groundLimit, out var node)) AddFrontier(grounded, node!);
            }
        }
        void AirStage()
        {
            var preparations = new List<SearchNode>();
            // Keep failed two-ground prefixes in the bounded frontier too: a later
            // experiment can change the explicit movement, but never substitute a
            // one-ground route for the declared two-ground grammar.
            foreach (var parent in Frontier(grounded))
            {
                if (!Evaluate(parent.Replay, parent.Di, "observe-ground-prefix", parent.Drift, airLimit,
                    out var observed, captureFrames: true, retain: false)) return;
                int release = ReleasedGroundTick(observed!);
                foreach (int delay in new[] { 0, 6 })
                foreach (int hold in new[] { 2, 8 })
                {
                    if (used >= airLimit || used >= options.Budget) break;
                    int jumpTick = release + delay;
                    if (jumpTick <= parent.Replay.Actions[^1].Tick || jumpTick >= options.Horizon - 1)
                    { Exclude("jump-outside-horizon"); continue; }
                    var replay = Extend(parent.Replay);
                    SetJump(replay, jumpTick, hold);
                    SetDrift(replay, jumpTick, options.Horizon, parent.Drift);
                    if (Evaluate(replay, parent.Di, "jump-preparation", parent.Drift, airLimit, out var prep, retain: false))
                        preparations.Add(prep!);
                }
            }
            foreach (int offset in AirOffsets)
            foreach (var prep in preparations)
            foreach (string slot in AirNormals)
            {
                if (used >= airLimit || used >= options.Budget) return;
                int takeoff = prep.Outcome.TakeoffTicks.FirstOrDefault(t => t > prep.Replay.Actions[^1].Tick, -1);
                int tick = (takeoff >= 0 ? takeoff + 1 : prep.Replay.Actions[^1].Tick + 12) + offset;
                if (!Fits(prep.Replay, tick)) { Exclude("air-outside-horizon"); continue; }
                var replay = Extend(prep.Replay);
                AddAction(replay, tick, slot);
                if (Evaluate(replay, prep.Di, "jump-aerial", prep.Drift, airLimit, out var node)) AddFrontier(aerials, node!);
            }
        }
        void LandingStage()
        {
            var preparations = new List<SearchNode>();
            foreach (var parent in Frontier(aerials))
            {
                if (!Evaluate(parent.Replay, parent.Di, "observe-air-prefix", parent.Drift, options.Budget,
                    out var observed, captureFrames: true, retain: false)) return;
                int lastPress = parent.Replay.Actions[^1].Tick;
                int descending = observed!.Outcome.Frames.FirstOrDefault(f => f.Tick >= lastPress
                    && !f.Attacker.Grounded && f.Attacker.Vy < 0f && f.Attacker.Hitstop == 0)?.Tick ?? -1;
                foreach (int offset in new[] { 0, 4, 8 })
                {
                    if (used >= options.Budget) break;
                    int tick = (descending >= 0 ? descending + 1 : lastPress + 12) + offset;
                    if (tick >= options.Horizon) { Exclude("fastfall-outside-horizon"); continue; }
                    var replay = Extend(parent.Replay);
                    for (int t = tick; t < options.Horizon; t++)
                    {
                        replay.AttackerInputs[t].Down = true;
                        replay.AttackerInputs[t].DownPressed = t == tick;
                    }
                    if (Evaluate(replay, parent.Di, "fastfall-preparation", parent.Drift, options.Budget,
                        out var prep, retain: false)) preparations.Add(prep!);
                }
            }
            foreach (int offset in LandingOffsets)
            foreach (var prep in preparations)
            foreach (string slot in GroundNormals)
            {
                if (used >= options.Budget) return;
                int afterAir = prep.Replay.Actions[^1].Tick;
                int landing = prep.Outcome.LandingTicks.FirstOrDefault(t => t > afterAir, -1);
                int anchor = landing >= 0 ? prep.Outcome.AttackerActionTicks.FirstOrDefault(t => t >= landing, landing) + 1 : afterAir + 24;
                int tick = anchor + offset;
                if (!Fits(prep.Replay, tick)) { Exclude("landing-outside-horizon"); continue; }
                var replay = Extend(prep.Replay);
                AddAction(replay, tick, slot);
                Evaluate(replay, prep.Di, "fastfall-landing", prep.Drift, options.Budget, out _, requireFastFall: true);
            }
        }
        bool Evaluate(Replay source, string di, string grammar, int drift, int limit, out SearchNode? node,
            bool captureFrames = false, bool retain = true, bool requireFastFall = false)
        {
            node = null;
            if (used >= limit || used >= options.Budget) return false;
            // Inputs are immutable across executions; only a generated defender
            // stream is replaced. Do not clone both full streams merely to observe.
            var replay = CopyReplay(source, copyInputs: false);
            replay.Id = $"route-{++used:D6}";
            var execution = Execute(context, replay, di, captureFrames);
            replay.DefenderInputs = execution.GeneratedDefenderInputs!;
            node = new SearchNode(replay, execution.Outcome, di, grammar, drift);
            Increment(report.GrammarAttempts, grammar);
            string condition = $"{grammar};pct={replay.InitialDefender.DamagePercent};distance={(replay.InitialDefender.PZ - replay.InitialAttacker.PZ).ToString("R", CultureInfo.InvariantCulture)};di={di};drift={drift}";
            Increment(report.ConditionAttempts, condition);
            string failure = Failure(node.Outcome);
            if (requireFastFall && node.Outcome.FastFallTicks.Count == 0) failure = "fast-fall-latch-not-observed";
            if (failure != "success") Increment(report.FailureCounts, failure);
            if (retain && failure == "success")
            {
                retained.Add(node);
                retained.Sort(CompareNodes);
                if (retained.Count > options.Keep) retained.RemoveAt(retained.Count - 1);
                if (!perDi.TryGetValue(di, out var prior) || CompareNodes(node, prior) < 0) perDi[di] = node;
            }
            return true;
        }
        void AddFrontier(List<SearchNode> nodes, SearchNode node)
        {
            nodes.Add(node);
            // Keep alternative slot paths, not just immediate high-damage finishers.
            // A bounded representative reservoir avoids retaining every failed run.
            int count = 0;
            SearchNode? worst = null;
            foreach (var existing in nodes)
                if (existing.Di == node.Di && SameSlotPath(existing.Replay, node.Replay))
                {
                    count++;
                    if (worst == null || CompareFrontier(existing, worst) > 0) worst = existing;
                }
            int paths = node.Replay.Actions.Count == 1 ? 4 : node.Replay.Actions.Count == 2 ? 16 : 64;
            if (count > Math.Max(1, options.PrefixWidth / paths)) nodes.Remove(worst!);
        }
        List<SearchNode> Frontier(List<SearchNode> nodes)
        {
            if (nodes.Count == 0) return new List<SearchNode>();
            int depth = nodes[0].Replay.Actions.Count;
            int normals = GroundNormals.Length;
            int shifts = depth >= 3 ? normals * normals : depth == 2 ? normals : 1;
            var byDirection = new List<SearchNode>[options.DiDirections.Length];
            for (int d = 0; d < byDirection.Length; d++)
            {
                var selected = new List<SearchNode>();
                var groups = new List<SearchNode[]>();
                for (int shift = 0; shift < shifts; shift++)
                for (int starter = 0; starter < GroundNormals.Length; starter++)
                {
                    string first = GroundNormals[starter];
                    string second = GroundNormals[(starter + shift % normals) % normals];
                    string third = AirNormals[(starter + shift / normals) % normals];
                    groups.Add(nodes.Where(n => n.Di == options.DiDirections[d] && n.Replay.Actions[0].Slot == first
                        && (depth < 2 || n.Replay.Actions[1].Slot == second)
                        && (depth < 3 || n.Replay.Actions[2].Slot == third))
                        .OrderBy(n => n, Comparer<SearchNode>.Create(CompareFrontier)).ToArray());
                }
                for (int round = 0; selected.Count < options.PrefixWidth; round++)
                {
                    bool any = false;
                    foreach (var group in groups)
                        if (round < group.Length && selected.Count < options.PrefixWidth)
                        { selected.Add(group[round]); any = true; }
                    if (!any) break;
                }
                byDirection[d] = selected;
            }
            var result = new List<SearchNode>();
            for (int round = 0; round < options.PrefixWidth; round++)
                foreach (var direction in byDirection)
                    if (round < direction.Count) result.Add(direction[round]);
            return result;
        }
        void Exclude(string reason) => Increment(report.FailureCounts, reason);
        void CompareFixedDi()
        {
            foreach (var witness in report.Witnesses)
            foreach (string di in options.DiDirections)
            {
                if (measured >= options.MeasurementBudget)
                { report.FixedRouteDi.Add(new DiComparison { WitnessId = witness.Id, Di = di, Complete = false }); continue; }
                var replay = CopyReplay(witness.Replay, copyInputs: false);
                replay.Id = witness.Id + "-fixed-" + di;
                var execution = Execute(context, replay, di);
                measured++;
                replay.DefenderInputs = execution.GeneratedDefenderInputs!;
                replay.ExpectedOutcomeHash = OutcomeHash(execution.Outcome);
                report.FixedRouteDi.Add(new DiComparison { WitnessId = witness.Id, Di = di,
                    Complete = true, Replay = replay, Outcome = execution.Outcome });
            }
        }
        void MeasureTiming()
        {
            foreach (var witness in report.Witnesses)
            for (int index = 0; index < witness.Replay.Actions.Count; index++)
            {
                var action = witness.Replay.Actions[index];
                int requestedLo = action.Tick - options.TimingRadius, requestedHi = action.Tick + options.TimingRadius;
                int lo = Math.Max(index == 0 ? 0 : witness.Replay.Actions[index - 1].Tick + 1, requestedLo);
                int hi = Math.Min(index + 1 < witness.Replay.Actions.Count ? witness.Replay.Actions[index + 1].Tick - 1
                    : witness.Replay.AttackerInputs.Length - 1, requestedHi);
                var measurement = new TimingMeasurement { ActionIndex = index, BaselineTick = action.Tick,
                    BoundaryClipped = lo != requestedLo || hi != requestedHi };
                for (int tick = lo; tick <= hi; tick++)
                {
                    if (measured >= options.MeasurementBudget) break;
                    var replay = CopyReplay(witness.Replay);
                    replay.Actions[index].Tick = tick;
                    replay.AttackerInputs[action.Tick].ActiveSlot = 0;
                    replay.AttackerInputs[tick].ActiveSlot = WireSlot(action.Slot);
                    var outcome = Execute(context, replay).Outcome;
                    measured++;
                    var observed = outcome.Actions.FirstOrDefault(a => a.EntityId == AttackerId && a.Tick == tick);
                    bool success = outcome.RouteSucceeded && observed?.ExpectedSlot == action.Slot
                        && observed.Status == "accepted" && observed.Contacts > 0;
                    measurement.Samples.Add(new TimingSample(tick, success,
                        success ? "complete-route" : observed?.Status ?? "not-accepted", outcome.Damage));
                }
                measurement.Complete = measurement.Samples.Count == hi - lo + 1;
                measurement.Intervals = Intervals(measurement.Samples);
                if (measurement.Samples.FirstOrDefault()?.Success == true || measurement.Samples.LastOrDefault()?.Success == true)
                    measurement.BoundaryClipped = true;
                witness.Timing.Add(measurement);
            }
        }
    }

    private static Replay Fresh(Context context, int horizon, int pct, float distance)
    {
        var map = context.Arena.Heightmap;
        float x = map.OriginX + (map.Width - 1) * map.CellSize * .5f;
        float z = map.OriginZ + (map.Height - 1) * map.CellSize * .5f;
        float surface = map.Sample(x, z);
        var attacker = new CharacterState { PX = x, PY = surface + context.Attacker.Definition.CapsuleHeight * .5f, PZ = z,
            State = ActionState.Idle, IsGrounded = true, FacingYaw = 0, JumpsLeft = 2, AirDodgesLeft = 1 };
        var defender = new CharacterState { PX = x, PY = surface + context.Defender.Definition.CapsuleHeight * .5f, PZ = z + distance,
            State = ActionState.Idle, IsGrounded = true, FacingYaw = MathF.PI, JumpsLeft = 2, AirDodgesLeft = 1,
            DamagePercent = (ushort)pct };
        var a = new InputState[horizon];
        var d = new InputState[horizon];
        for (int tick = 0; tick < horizon; tick++)
        {
            a[tick].LockMode = TargetLockMode.Always;
            a[tick].TargetEntityId = (byte)DefenderId;
            d[tick].LockMode = TargetLockMode.Always;
            d[tick].TargetEntityId = (byte)AttackerId;
        }
        return CreateReplay(context, attacker, defender, a, d, Array.Empty<PlannedAction>());
    }
    private static Replay Extend(Replay source)
    {
        var replay = CopyReplay(source, copyInputs: false);
        replay.AttackerInputs = (InputState[])source.AttackerInputs.Clone();
        return replay;
    }
    private static bool Fits(Replay replay, int tick) => tick > replay.Actions[^1].Tick && tick < replay.AttackerInputs.Length;
    private static void AddAction(Replay replay, int tick, string slot)
    {
        replay.Actions.Add(new PlannedAction { Tick = tick, Slot = slot });
        replay.AttackerInputs[tick].ActiveSlot = WireSlot(slot);
    }
    private static void SetDrift(Replay replay, int start, int end, int profile)
    {
        var drift = DriftInputs[profile];
        for (int tick = start; tick < end; tick++)
        { replay.AttackerInputs[tick].MoveX = drift.X; replay.AttackerInputs[tick].MoveY = drift.Z; }
    }
    private static void SetJump(Replay replay, int tick, int hold)
    {
        replay.AttackerInputs[tick].Jump = true;
        for (int t = tick; t < Math.Min(replay.AttackerInputs.Length, tick + hold); t++) replay.AttackerInputs[t].JumpHeld = true;
    }
    private static int NextOrdinaryTick(SearchNode node)
    {
        int last = node.Replay.Actions[^1].Tick;
        int contact = node.Outcome.LastContactTick ?? last;
        return node.Outcome.AttackerActionTicks.FirstOrDefault(t => t > last && t >= contact, last + 12);
    }
    private static int ReleasedGroundTick(SearchNode node)
    {
        int last = node.Replay.Actions[^1].Tick;
        var frame = node.Outcome.Frames.FirstOrDefault(f => f.Tick > last && f.Attacker.Grounded
            && f.Attacker.AnimLock == 0 && f.Attacker.Hitstop == 0 && f.Attacker.LandingLag == 0);
        return frame?.Tick + 1 ?? last + 24;
    }
    private static bool SameSlotPath(Replay a, Replay b)
    {
        if (a.Actions.Count != b.Actions.Count) return false;
        for (int i = 0; i < a.Actions.Count; i++)
            if (a.Actions[i].Slot != b.Actions[i].Slot) return false;
        return true;
    }
    private static int CompareNodes(SearchNode a, SearchNode b)
    {
        int order = b.Outcome.ConnectedActions.CompareTo(a.Outcome.ConnectedActions);
        if (order == 0) order = b.Outcome.Damage.CompareTo(a.Outcome.Damage);
        if (order == 0) order = a.Outcome.ConversionTicks.CompareTo(b.Outcome.ConversionTicks);
        return order == 0 ? string.CompareOrdinal(a.Replay.Id, b.Replay.Id) : order;
    }
    private static int CompareFrontier(SearchNode a, SearchNode b)
    {
        int order = b.Outcome.RouteSucceeded.CompareTo(a.Outcome.RouteSucceeded);
        return order == 0 ? CompareNodes(a, b) : order;
    }
    private static string Failure(Outcome outcome) => outcome.RouteSucceeded ? "success"
        : outcome.Actions.Any(a => a.Status == "variant-mismatch") ? "wrong-variant"
        : outcome.Actions.Any(a => a.Status == "rejected") ? "rejected"
        : outcome.Actions.Any(a => a.Status == "accepted-unobserved-variant") ? "unobserved-variant"
        : outcome.ContactCount == 0 ? "no-contact" : "incomplete-route";
    private static void Increment(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
    private static List<TimingInterval> Intervals(List<TimingSample> samples)
    {
        var intervals = new List<TimingInterval>();
        int start = -1, previous = -2;
        foreach (var sample in samples)
        {
            if (!sample.Success)
            { if (start >= 0) intervals.Add(new TimingInterval(start, previous)); start = -1; continue; }
            if (start < 0 || sample.Tick != previous + 1)
            { if (start >= 0) intervals.Add(new TimingInterval(start, previous)); start = sample.Tick; }
            previous = sample.Tick;
        }
        if (start >= 0) intervals.Add(new TimingInterval(start, previous));
        return intervals;
    }
}
