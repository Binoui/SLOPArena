using SlopArena.Shared;
using static SlopArena.ComboFinder.Runner;
namespace SlopArena.ComboFinder;

static class Replay
{
    internal static string Label(byte slot, bool ground = true)
    {
        foreach (var s in new[] { "1", "2", "3", "A", "E", "R", "F" })
            if (Slot(s) == slot) return normalWires.Contains(slot) ? (ground ? "g" : "a") + s : s;
        return "slot" + slot;
    }
    internal static Timeline Timeline(RunTrace r)
    {
        var moves = r.Hits.Where(h => h.Owner == 1 && !h.Blocked).GroupBy(h => h.Move).Select(g =>
        {
            bool ground = g.Key >= 0 && g.Key < r.Moves.Count && r.Moves[g.Key].ground;
            return new LandedMove(g.Min(h => h.Tick), g.Max(h => h.Tick), Label(g.First().Slot, ground), !ground,
                g.Sum(h => h.Damage), g.Any(h => h.Tick >= 2 && r.VictimAir[h.Tick - 2]), g.Max(h => h.Tier));
        }).OrderBy(m => m.Tick).ToArray();
        int start = 0, bestStart = 0, bestLen = 0;
        for (int i = 0; i < moves.Length; i++)
        {
            if (i > 0 && (moves[i].Tick - moves[i - 1].LastTick > 70 || r.Hits.Any(h => h.Owner == 2 && !h.Blocked && h.Tick > moves[i - 1].LastTick && h.Tick < moves[i].Tick))) start = i;
            if (i - start + 1 > bestLen) { bestStart = start; bestLen = i - start + 1; }
        }
        return new(r.Ticks, r.VictimKo, bestLen == 0 ? new(0, 0) : new(moves[bestStart].Tick, moves[bestStart + bestLen - 1].LastTick),
            moves, r.VictimSwings.Select(w => new VictimSwing(w.tick, Label(w.slot).TrimStart('g'))).ToArray(), r.Hits.Count(h => h.Owner == 2 && !h.Blocked),
            r.Hits.Select(h => new RawHit(h.Tick, (int)h.Owner, h.Slot, h.Damage, h.Blocked)).ToArray());
    }
    internal static string Tape(List<InputState> tape)
    {
        byte[] bytes = new byte[tape.Count * InputState.Size];
        for (int i = 0; i < tape.Count; i++) tape[i].Write(bytes.AsSpan(i * InputState.Size, InputState.Size));
        return Convert.ToBase64String(bytes);
    }
    internal static InputState[] Decode(string tape, string path)
    {
        byte[] bytes = Convert.FromBase64String(tape);
        if (bytes.Length == 0 || bytes.Length % InputState.Size != 0) throw new ArgumentException("Invalid tape byte length");
        var inputs = new InputState[bytes.Length / InputState.Size];
        for (int i = 0; i < inputs.Length; i++)
        {
            try { inputs[i] = InputState.Deserialize(bytes.AsSpan(i * InputState.Size, InputState.Size)); }
            catch (InvalidDataException ex) { throw new ArgumentException($"{path} tick {i + 1}: {ex.Message}", ex); }
        }
        return inputs;
    }
    internal static RunTrace Run(Content content, ComboFile combo, System.Text.StringBuilder? exact = null)
    {
        if (combo.Version != 1) throw new ArgumentException("version differs");
        if (combo.Fighters.Length != 2 || combo.Fighters[0].Id != 1 || combo.Fighters[1].Id != 2 || combo.Fighters[0].Role != "attacker" || combo.Fighters[1].Role != "victim")
            throw new ArgumentException("fighters must be attacker 1 and victim 2");
        var stamp = content.Stamp(combo.Fighters);
        if (stamp.Protocol != combo.Content.Protocol) throw new ArgumentException("content.protocol differs");
        if (stamp.StageHash != combo.Content.StageHash) throw new ArgumentException("content.stageHash differs");
        if (stamp.Packages.Count != combo.Content.Packages.Count) throw new ArgumentException("content.packages count differs");
        foreach (var p in stamp.Packages)
            if (!combo.Content.Packages.TryGetValue(p.Key, out var hash) || hash != p.Value) throw new ArgumentException($"content.packages.{p.Key} differs");
        var a = Decode(combo.Fighters[0].Tape, "fighters[0].tape"); var v = Decode(combo.Fighters[1].Tape, "fighters[1].tape");
        if (a.Length != v.Length || a.Length != combo.Timeline.Ticks) throw new ArgumentException("tape length differs from timeline.ticks");
        var r = new Runner(content, combo.Fighters[0], combo.Fighters[1]).Run(new(), 0, false, a, v, exact);
        var actual = System.Text.Json.JsonSerializer.SerializeToNode(Timeline(r), Json.Options)!;
        var expected = System.Text.Json.JsonSerializer.SerializeToNode(combo.Timeline, Json.Options)!;
        string? difference = Difference(expected, actual, "timeline");
        if (difference != null) throw new ArgumentException(difference);
        return r;
    }
    static string? Difference(System.Text.Json.Nodes.JsonNode? expected, System.Text.Json.Nodes.JsonNode? actual, string path)
    {
        if (System.Text.Json.Nodes.JsonNode.DeepEquals(expected, actual)) return null;
        if (expected is System.Text.Json.Nodes.JsonObject obj && actual is System.Text.Json.Nodes.JsonObject other)
        {
            foreach (var p in obj) { var d = Difference(p.Value, other[p.Key], path + "." + p.Key); if (d != null) return d; }
        }
        if (expected is System.Text.Json.Nodes.JsonArray arr && actual is System.Text.Json.Nodes.JsonArray arr2)
        {
            if (arr.Count != arr2.Count) return $"{path}.length differs: expected {arr.Count}, actual {arr2.Count}";
            for (int i = 0; i < arr.Count; i++) { var d = Difference(arr[i], arr2[i], $"{path}[{i}]"); if (d != null) return d; }
        }
        return $"{path} differs: expected {expected}, actual {actual}";
    }
    internal static string Summary(SearchOptions o, Timeline t) => $"{o.Attacker} vs {o.Victim}, {o.Stage}: {string.Join(", ", t.Moves.Select(m => m.Label))}, "
        + (t.Ko < 0 ? "no KO" : $"KO at {t.Ko / 60.0:F1} s") + $", {t.Taken} hit(s) taken";
}
