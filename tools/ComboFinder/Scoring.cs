using SlopArena.Shared;
using SlopArena.Shared.AI;
namespace SlopArena.ComboFinder;

using static SlopArena.ComboFinder.Runner;
static class Scoring
{
    const ulong A = 1, V = 2;
    internal static (double score, string why) Score(RunTrace r) { var e = Evaluate(r); return (e.score, e.why); }
    /// <summary>The attacker's own travel in metres and idle grounded ticks over the scored string, for reports.</summary>
    internal static (double travel, int planted) Movement(RunTrace r) { var e = Evaluate(r); return (e.travel, e.planted); }
    static (double score, string why, double travel, int planted) Evaluate(RunTrace r)
    {
        double s = -0.05 * r.Ticks;
        var mine = r.Hits.Where(h => h.Owner == A && !h.Blocked).ToList();
        // One entry per attacker move that connected, in order: (first hit tick, last hit tick, slot, grounded, damage).
        var landed = mine.GroupBy(h => h.Move).Select(g => (first: g.Min(h => h.Tick), last: g.Max(h => h.Tick), slot: g.First().Slot,
            ground: g.Key >= 0 && g.Key < r.Moves.Count && r.Moves[g.Key].ground, dmg: g.Sum(h => h.Damage), tier: g.Max(h => h.Tier))).OrderBy(x => x.first).ToList();
        double dmg = mine.Sum(h => h.Damage);
        s += 10 * landed.Count + 0.5 * dmg;
        // No normal links into another in today's frame data, so a string is pressure: each landed move follows the
        // last within 70 ticks and the victim lands nothing in between.
        int bestLen = 0, bestStart = 0, start = 0;
        for (int i = 0; i < landed.Count; i++)
        {
            if (i > 0)
            {
                int gapFrom = landed[i - 1].last, gapTo = landed[i].first;
                bool punished = r.Hits.Any(h => h.Owner == V && !h.Blocked && h.Tick > gapFrom && h.Tick < gapTo);
                if (gapTo - gapFrom > 70 || punished) start = i;
            }
            if (i - start + 1 > bestLen) { bestLen = i - start + 1; bestStart = start; }
        }
        var str = landed.Skip(bestStart).Take(bestLen).ToList();
        int beaten = str.Count > 1 ? r.VictimSwings.Count(w => w.tick > str[0].first && w.tick < str[^1].last) : 0;
        s += 15 * Math.Min(4, beaten);
        var kinds = str.Select(x => (x.slot, x.ground)).ToList();
        // Owner taste (2026-11-03): a variety of moves, normals usually flow better, special-only strings are fine.
        // These are scores, not rules. A light special reads as a miss on screen, so it costs points.
        bool koFollows = r.VictimKo >= 0 && str.Count > 0 && r.VictimKo - str[^1].last < 120;
        int normals = kinds.Count(k => normalWires.Contains(k.slot));
        int lightSpecials = str.Count(x => !normalWires.Contains(x.slot) && x.tier == 0);
        int distinct = kinds.Distinct().Count();
        s += 35 * normals + 20 * (kinds.Count - normals) + 25 * distinct - 60 * lightSpecials;
        for (int i = 1; i < kinds.Count; i++) if (kinds[i] == kinds[i - 1]) s -= 35;
        // Movement (owner, 2026-11-04): the attacker should move like a player, not stand and let the CPU walk in. From
        // the approach before the first hit to the last hit, reward the attacker's own travel, the ground it closes
        // on the victim, and air time. The share of that time spent standing idle costs points; a share, so a long
        // string costs no more than a short one.
        double travel = 0, closed = 0; int air = 0, planted = 0, window = 0;
        if (str.Count > 0)
        {
            for (int t = Math.Max(1, str[0].first - 60); t < str[^1].last && t < r.AttackerPos.Count; t++)
            {
                window++;
                var (x0, _, z0) = r.AttackerPos[t - 1]; var (x1, _, z1) = r.AttackerPos[t]; var (vx, _, vz) = r.VictimPos[t - 1];
                double sx = x1 - x0, sz = z1 - z0, dx = vx - x0, dz = vz - z0, d = Math.Sqrt(dx * dx + dz * dz);
                travel += Math.Sqrt(sx * sx + sz * sz);
                if (d > 1e-3) closed += Math.Max(0, (sx * dx + sz * dz) / d);
                if (r.AttackerAir[t]) air++;
                if (r.AttackerPlanted[t]) planted++;
            }
            s += Math.Min(150, 3 * travel) + Math.Min(80, 4 * closed) + Math.Min(60, air * 0.4) - 200.0 * planted / Math.Max(1, window);
            if (kinds.Any(k => !k.ground) && kinds.Any(k => k.ground)) s += 30;
        }
        int taken = r.Hits.Count(h => h.Owner == V && !h.Blocked);
        int blocked = r.Hits.Count(h => h.Owner == V && h.Blocked);
        s -= 120 * taken; s += 8 * Math.Min(2, blocked) + 10 * Math.Min(3, r.CloseVictimSwings);
        if (r.AttackerKo >= 0) s -= 3000;
        if (r.VictimKo >= 0)
        {
            s += 300;
            if (koFollows) s += 250 + 30 * str.Count;
        }
        string name(byte slot, bool g) => (g ? "g" : "a") + (normalWires.Contains(slot) ? "N" + slot : "S" + slot);
        return (s, $"string={bestLen} normals={normals} distinct={distinct} light={lightSpecials} beaten={beaten} [{string.Join(" ", kinds.Select(k => name(k.slot, k.ground)))}] moves={landed.Count} dmg={dmg:F0} taken={taken} blocked={blocked} whiffs={r.CloseVictimSwings} ko={r.VictimKo}", travel, planted);
    }

    internal static (double score, string why, int progress) RouteScore(RunTrace r, string[] route)
    {
        var mine = r.Hits.Where(h => h.Owner == A && !h.Blocked).ToList();
        var events = mine.GroupBy(h => h.Move).Select(g => (first: g.Min(h => h.Tick), last: g.Max(h => h.Tick), slot: g.First().Slot,
            ground: g.Key >= 0 && g.Key < r.Moves.Count && r.Moves[g.Key].ground, juggle: g.Any(h => h.Tick - 2 >= 0 && h.Tick - 2 < r.VictimAir.Count && r.VictimAir[h.Tick - 2]),
            beeline: false)).ToList();
        events.AddRange(r.Beelines.Select(t => (first: t, last: t, slot: (byte)0, ground: false, juggle: false, beeline: true)));
        events = events.OrderBy(e => e.first).ToList();
        bool Matches(string token, (int first, int last, byte slot, bool ground, bool juggle, bool beeline) e)
        {
            if (token == "beeline") return e.beeline;
            if (e.beeline) return false;
            if (token == "juggle") return normalWires.Contains(e.slot);
            if (token.Length == 2 && (token[0] == 'a' || token[0] == 'g')) return e.slot == Slot(token[1..]) && e.ground == (token[0] == 'g');
            return e.slot == Slot(token);
        }
        int step = 0, juggles = 0, jugglesHere = 0, lastTick = -1, progress = 0; double s = 0; var path = new List<string>();
        foreach (var e in events)
        {
            if (step >= route.Length) break;
            if (lastTick >= 0)
            {
                bool punished = r.Hits.Any(h => h.Owner == V && !h.Blocked && h.Tick > lastTick && h.Tick < e.first);
                if (punished || e.first - lastTick > 90) break;
                int gap = e.first - lastTick; if (gap > 50) s -= 2 * (gap - 50);
            }
            // A juggle token takes one to three normals; then the route moves on.
            if (route[step] == "juggle" && jugglesHere > 0 && step + 1 < route.Length && Matches(route[step + 1], e)) { step++; jugglesHere = 0; }
            if (!Matches(route[step], e)) { if (e.beeline) continue; break; }
            path.Add(e.beeline ? "beeline" : (e.ground ? "g" : "a") + (normalWires.Contains(e.slot) ? "N" : "S") + e.slot + (e.juggle ? "*" : ""));
            if (e.juggle) juggles++;
            // Kill setup: F should throw toward a nearby edge so the finisher happens off stage.
            if (route[step] == "F" && e.first - 1 < r.AttackerPos.Count)
            {
                float edge = r.ThrowEdgeDistances[e.first];
                s += 160 - 12 * edge; path.Add($"edge{edge:F1}m");
            }
            lastTick = e.last; progress++;
            if (route[step] == "juggle") { jugglesHere++; if (jugglesHere >= 3) { step++; jugglesHere = 0; } }
            else step++;
        }
        bool complete = step >= route.Length || (step == route.Length - 1 && route[^1] == "juggle" && jugglesHere > 0);
        int taken = r.Hits.Count(h => h.Owner == V && !h.Blocked);
        s += 200 * progress + 25 * juggles - 400 * taken - 0.05 * r.Ticks;
        if (r.AttackerKo >= 0) s -= 3000;
        bool killed = complete && r.VictimKo >= 0 && r.VictimKo - lastTick < 120;
        if (killed) s += 1500;
        return (s, $"route {progress} steps [{string.Join(" ", path)}] complete={complete} juggles={juggles} taken={taken} ko={r.VictimKo}", killed ? 1 : 0);
    }

}
