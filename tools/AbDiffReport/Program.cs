using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using MoveData = SlopArena.MoveDataReport.Program;
using SelfPlay = SlopArena.SelfPlayReport.Program;

namespace SlopArena.AbDiffReport;

/// <summary>
/// Tuning A/B diff report (issue #149): runs the move-data analysis (frame data +
/// trajectories + kill% + true-combo graph) and the seeded self-play telemetry under TWO
/// named tuning profiles in one process, then diffs the structured JSON trees. The same
/// seed runs on both sides, so match-level variance cancels — the remaining delta is the
/// tuning effect. Emits a lossless diff JSON (both full reports + the diff tree), a
/// self-contained HTML report (move-data deltas, combo links gained/lost, telemetry
/// side-by-side) and a markdown summary. Reuses the MoveDataReport + SelfPlayReport
/// engines (InternalsVisibleTo) and the Shared TuningProfiles/TuningDiff tables.
///
/// Usage: dotnet run --project tools/AbDiffReport -- --char fightguy --cand stun16kv11
///        [--base base] [--pcts 0,30,60,90,120,150] [--matches 20] [--seed 20260817]
///        [--di] [--no-graph] [--json ab.json] [--html ab.html] [--out report.md]
/// Profiles: base | old | stunx18 | kv70 | stun16kv11 | floor30 | melee | melee-hot | melee-soft
/// (see Shared TuningProfiles).
/// </summary>
internal static class Program
{
    private const string ToolVersion = "1.0.0";
    private const int DefaultMatches = 20;
    private const int DefaultSeed = 20260817;
    private static readonly int[] DefaultPcts = { 0, 30, 60, 90, 120, 150 };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static int Main(string[] args)
    {
        string charName = ParseArg(args, "--char") ?? "fightguy";
        string baseProfile = ParseArg(args, "--base") ?? "base";
        string? candProfile = ParseArg(args, "--cand");
        int matches = ParseInt(args, "--matches", DefaultMatches);
        int seed = ParseInt(args, "--seed", DefaultSeed);
        int[] pcts = ParsePcts(args) ?? DefaultPcts;
        bool di = args.Contains("--di");
        bool graph = !args.Contains("--no-graph");
        string? jsonPath = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        string? outPath = ParseArg(args, "--out");

        if (candProfile == null)
        {
            Console.Error.WriteLine("--cand <profile> is required (a diff needs two tunings; " +
                "pick from base|old|stunx18|kv70|stun16kv11|floor30)");
            return 1;
        }
        foreach (var p in new[] { baseProfile, candProfile })
            if (!TuningProfiles.TryGet(p, out _))
            {
                Console.Error.WriteLine($"unknown tuning profile '{p}' " +
                    $"(expected one of: {string.Join(", ", TuningProfiles.Profiles.Select(x => x.Name))})");
                return 1;
            }

        CharacterClass cls = charName.ToLowerInvariant() switch
        {
            "fightguy" or "fg" => CharacterClass.FightGuy,
            "wibou" => CharacterClass.Wibou,
            "manki" => CharacterClass.Manki,
            _ => throw new ArgumentException($"unknown character: {charName}"),
        };
        var entry = BuiltInContentResolver.Resolve(cls);
        var def = entry.Definition;
        if (def.Class == CharacterClass.None)
        {
            Console.Error.WriteLine($"Unknown character '{charName}'.");
            return 1;
        }

        string commit = CurrentCommit();
        var baked = entry.BakedAnimation;
        var hits = MoveData.CollectHits(def);
        var arena = SelfPlay.BuildKillArena();

        // Independent immutable settings let both sides share a process without switching globals.
        var baseTuning = TuningProfiles.Get(baseProfile);
        var candTuning = TuningProfiles.Get(candProfile);
        var baseMove = MoveData.BuildReport(def, hits, pcts, baked, graph, di, combatTuning: baseTuning);
        var baseRecords = new List<MatchRecord>(matches);
        for (int m = 0; m < matches; m++) baseRecords.Add(SelfPlayMatch.Run(def, arena, seed + m, baked, combatTuning: baseTuning));
        var baseTelemetry = SelfPlay.Aggregate(entry, baseRecords, seed);
        Console.Error.WriteLine($"baseline {TuningProfiles.Describe(baseProfile)} — " +
            $"move report + {matches} matches (seed {seed}) done");

        var candMove = MoveData.BuildReport(def, hits, pcts, baked, graph, di, combatTuning: candTuning);
        var candRecords = new List<MatchRecord>(matches);
        for (int m = 0; m < matches; m++) candRecords.Add(SelfPlayMatch.Run(def, arena, seed + m, baked, combatTuning: candTuning));
        var candTelemetry = SelfPlay.Aggregate(entry, candRecords, seed);
        Console.Error.WriteLine($"candidate {TuningProfiles.Describe(candProfile)} done");

        // ── Structured diff (no physics re-run; deterministic) ──
        var baseTree = JsonNode.Parse(JsonSerializer.Serialize(baseMove, JsonOpts))!;
        var candTree = JsonNode.Parse(JsonSerializer.Serialize(candMove, JsonOpts))!;
        var baseTel = JsonNode.Parse(JsonSerializer.Serialize(baseTelemetry, JsonOpts))!;
        var candTel = JsonNode.Parse(JsonSerializer.Serialize(candTelemetry, JsonOpts))!;
        var moveDiff = TuningDiff.Compute(baseTree, candTree);
        var telDiff = TuningDiff.Compute(baseTel, candTel);

        var doc = new JsonObject
        {
            ["toolVersion"] = ToolVersion,
            ["generatedAt"] = Now(),
            ["character"] = def.DisplayName,
            ["baseline"] = Meta(baseProfile, seed, matches, pcts, commit, baseMove.GeneratedAt, baseTelemetry.GeneratedAt),
            ["candidate"] = Meta(candProfile, seed, matches, pcts, commit, candMove.GeneratedAt, candTelemetry.GeneratedAt),
            ["diff"] = new JsonObject
            {
                ["moveData"] = NodeOf(moveDiff),
                ["telemetry"] = NodeOf(telDiff),
            },
            // Lossless: the full reports on both sides, so any diff is reproducible
            // without re-running the sims.
            ["baselineMoveData"] = baseTree,
            ["candidateMoveData"] = candTree,
            ["baselineTelemetry"] = baseTel,
            ["candidateTelemetry"] = candTel,
        };

        string json = JsonSerializer.Serialize(doc, JsonOpts);

        var moveRows = CollectMoveRows(moveDiff["moves"], pcts);
        var comboRows = graph ? CollectComboRows(moveDiff["trueCombos"], pcts) : new List<ComboRow>();
        var stats = CollectStats(telDiff);
        var usage = CollectUsage(telDiff["perMove"]);
        var curves = CollectCurves(baseTree, candTree, pcts); // per-move flight arcs, both sides

        if (jsonPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            File.WriteAllText(jsonPath, json);
            Console.Error.WriteLine($"wrote {jsonPath}");
        }
        if (htmlPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(htmlPath))!);
            File.WriteAllText(htmlPath, BuildHtml(def.DisplayName, baseProfile, candProfile, commit, seed, matches,
                pcts, moveRows, comboRows, stats, usage, curves));
            Console.Error.WriteLine($"wrote {htmlPath}");
        }
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, BuildMarkdown(def.DisplayName, baseProfile, candProfile, commit, seed, matches,
                pcts, moveRows, comboRows, stats, usage));
            Console.Error.WriteLine($"wrote {outPath}");
        }
        if (jsonPath == null && htmlPath == null && outPath == null)
            Console.WriteLine(BuildMarkdown(def.DisplayName, baseProfile, candProfile, commit, seed, matches,
                pcts, moveRows, comboRows, stats, usage));
        return 0;
    }

    // ── Diff walkers (shared by HTML + markdown) ─────────────────────────────

    private sealed record TrajCell(int Pct, double? KvB, double? KvC, double? StunB, double? StunC,
        double? AdvB, double? AdvC, double? ApexB, double? ApexC);
    private sealed record MoveRow(string Label, int HitIndex, double? KillB, double? KillC,
        List<TrajCell> Cells, bool AnyChange);

    private sealed record EdgeCell(string? OldVerdict, string? NewVerdict, double? TightB, double? TightC);
    private sealed record ComboRow(string Move, string State, List<(string FollowUp, List<EdgeCell> Cells)> FollowUps,
        int Gained, int Lost);

    private sealed record StatRow(string Key, double? Base, double? Cand, double? Delta);
    private sealed record UsageRow(string Label, string Ability, double? SwingsB, double? SwingsC,
        double? HitsB, double? HitsC, double? WhiffsB, double? WhiffsC);

    /// <summary>Base- or candidate-side value of an element field (see TuningDiffNode.Field).</summary>
    private static double? NumOf(TuningDiffNode el, string field, bool candidate)
    {
        JsonNode? n;
        switch (el.Kind)
        {
            case TuningDiffKind.Added:
                n = candidate ? (el.NewValue as JsonObject)?[field] : null;
                break;
            case TuningDiffKind.Removed:
                n = candidate ? null : (el.OldValue as JsonObject)?[field];
                break;
            case TuningDiffKind.Unchanged:
                n = ((el.NewValue ?? el.OldValue) as JsonObject)?[field];
                break;
            default:
                var child = el[field];
                n = child == null ? null : candidate ? child.NewValue ?? child.OldValue : child.OldValue ?? child.NewValue;
                break;
        }
        return n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    }

    private static string? StrOf(TuningDiffNode el, string field, bool candidate)
    {
        JsonNode? n = el.Kind switch
        {
            TuningDiffKind.Added => candidate ? (el.NewValue as JsonObject)?[field] : null,
            TuningDiffKind.Removed => candidate ? null : (el.OldValue as JsonObject)?[field],
            TuningDiffKind.Unchanged => ((el.NewValue ?? el.OldValue) as JsonObject)?[field],
            _ => el[field] is { } child
                ? candidate ? child.NewValue ?? child.OldValue : child.OldValue ?? child.NewValue
                : null,
        };
        return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    }

    private static List<MoveRow> CollectMoveRows(TuningDiffNode? moves, int[] pcts)
    {
        var rows = new List<MoveRow>();
        if (moves == null || moves.Children == null) return rows;
        foreach (var kv in moves.Children)
        {
            var el = kv.Value;
            string label = StrOf(el, "label", true) ?? kv.Key;
            int hitIndex = (int)Math.Round(NumOf(el, "hitIndex", true) ?? 0);
            double? killB = NumOf(el, "killPct", false);
            double? killC = NumOf(el, "killPct", true);

            var cells = new List<TrajCell>();
            var trajs = el["trajectories"];
            if (trajs != null && trajs.Children != null)
                foreach (var tk in trajs.Children)
                {
                    var t = tk.Value;
                    int pct = (int)Math.Round(NumOf(t, "pct", true) ?? 0);
                    cells.Add(new TrajCell(pct,
                        NumOf(t, "kv", false), NumOf(t, "kv", true),
                        NumOf(t, "stun", false), NumOf(t, "stun", true),
                        NumOf(t, "adv", false), NumOf(t, "adv", true),
                        NumOf(t, "apex", false), NumOf(t, "apex", true)));
                }
            // Order cells by the report's percent list (trajectories keyed by pct come in
            // any stable order; the report table is percent-ascending).
            cells = cells.OrderBy(c => Array.IndexOf(pcts, c.Pct)).ToList();

            bool any = el.Kind != TuningDiffKind.Unchanged
                || cells.Any(c => c.KvB != c.KvC || c.StunB != c.StunC || c.AdvB != c.AdvC || c.ApexB != c.ApexC)
                || killB != killC;
            rows.Add(new MoveRow(label, hitIndex, killB, killC, cells, any));
        }
        return rows;
    }

    private static List<ComboRow> CollectComboRows(TuningDiffNode? starters, int[] pcts)
    {
        var rows = new List<ComboRow>();
        if (starters == null || starters.Children == null) return rows;
        foreach (var sk in starters.Children)
        {
            var s = sk.Value;
            string move = StrOf(s, "move", true) ?? sk.Key;
            string state = StrOf(s, "state", true) ?? "";
            var edges = s["edges"];
            var byFu = new List<(string FollowUp, List<EdgeCell> Cells)>();
            int gained = 0, lost = 0;
            if (edges != null && edges.Children != null)
            {
                foreach (var fu in edges.Children.GroupBy(e => StrOf(e.Value, "followUp", true) ?? "?"))
                {
                    var cells = new List<EdgeCell>();
                    foreach (var p in pcts)
                    {
                        TuningDiffNode? e = fu.Select(x => x.Value).FirstOrDefault(x => Math.Round(NumOf(x, "pct", true) ?? -1) == p);
                        if (e == null) { cells.Add(new EdgeCell(null, null, null, null)); continue; }
                        string? oldV = StrOf(e, "verdict", false);
                        string? newV = StrOf(e, "verdict", true);
                        double? tb = NumOf(e, "tightness", false), tc = NumOf(e, "tightness", true);
                        if (oldV != "true" && newV == "true") gained++;
                        if (oldV == "true" && newV != "true") lost++;
                        cells.Add(new EdgeCell(oldV, newV, tb, tc));
                    }
                    byFu.Add((fu.Key, cells));
                }
            }
            rows.Add(new ComboRow(move, state, byFu, gained, lost));
        }
        return rows;
    }

    private static readonly string[] StatKeys =
    {
        "hitRate", "whiffRate", "avgComboLen", "maxComboLen",
        "damagePerMatch", "damagePerStock", "totalDeaths", "creditedDeaths", "uncreditedDeaths",
        "winsA", "winsB", "draws",
        "avgDurationTicks", "maxDurationTicks",
        "totalSwings", "totalHits", "totalWhiffs", "totalDamage",
    };

    private static List<StatRow> CollectStats(TuningDiffNode tel)
    {
        var list = new List<StatRow>();
        foreach (var k in StatKeys)
        {
            var n = tel[k];
            if (n == null) continue;
            double? b = NumberOrNull(n.OldValue);
            double? c = NumberOrNull(n.NewValue);
            list.Add(new StatRow(k, b, c, b != null && c != null ? c - b : null));
        }
        return list;
    }

    private static double? NumberOrNull(JsonNode? value)
        => value is JsonValue json && json.TryGetValue<double>(out var number) ? number : null;

    private static List<UsageRow> CollectUsage(TuningDiffNode? perMove)
    {
        var list = new List<UsageRow>();
        if (perMove == null || perMove.Children == null) return list;
        foreach (var kv in perMove.Children)
        {
            var el = kv.Value;
            list.Add(new UsageRow(
                StrOf(el, "label", true) ?? kv.Key,
                StrOf(el, "ability", true) ?? "",
                NumOf(el, "swings", false), NumOf(el, "swings", true),
                NumOf(el, "hits", false), NumOf(el, "hits", true),
                NumOf(el, "whiffs", false), NumOf(el, "whiffs", true)));
        }
        return list;
    }

    // ── Knockback-shape curves (base vs candidate arc overlay) ───────────────

    private sealed record ArcPoint(double Travel, double Height);
    private sealed record MoveCurves(string Label, int HitIndex, int Pct, double? KvB, double? KvC,
        ArcPoint[] Base, ArcPoint[] Cand);

    /// <summary>Walk both full MoveData reports (already embedded in the diff JSON) and pair up each
    /// move &times; %'s flight-arc points so the HTML can overlay base vs candidate knockback shapes.
    /// The per-tick points (height vs horizontal travel) are what make the launch feel visible.</summary>
    private static List<MoveCurves> CollectCurves(JsonNode? baseMove, JsonNode? candMove, int[] pcts)
    {
        var result = new List<MoveCurves>();
        if (baseMove?["moves"] is not JsonArray bArr || candMove?["moves"] is not JsonArray cArr) return result;
        int n = Math.Min(bArr.Count, cArr.Count);
        for (int i = 0; i < n; i++)
        {
            if (bArr[i] is not JsonObject bm || cArr[i] is not JsonObject cm) continue;
            string label = Str(bm["label"]) ?? "?";
            int hitIndex = (int)Math.Round(Num(bm["hitIndex"]) ?? 0);
            if (bm["trajectories"] is not JsonArray bt || cm["trajectories"] is not JsonArray ct) continue;
            foreach (var btj in bt)
            {
                if (btj is not JsonObject btr) continue;
                int pct = (int)Math.Round(Num(btr["pct"]) ?? -1);
                if (Array.IndexOf(pcts, pct) < 0) continue;
                JsonObject? ctr = null;
                foreach (var ctj in ct)
                    if (ctj is JsonObject o && (int)Math.Round(Num(o["pct"]) ?? -1) == pct) { ctr = o; break; }
                var bpts = ReadArc(btr["points"]);
                var cpts = ReadArc(ctr?["points"]);
                if (bpts.Length == 0 && cpts.Length == 0) continue;
                result.Add(new MoveCurves(label, hitIndex, pct, Num(btr["kv"]), Num(ctr?["kv"]), bpts, cpts));
            }
        }
        return result;
    }

    private static ArcPoint[] ReadArc(JsonNode? arr)
    {
        if (arr is not JsonArray a || a.Count == 0) return Array.Empty<ArcPoint>();
        var list = new List<ArcPoint>(a.Count);
        foreach (var e in a)
            if (e is JsonObject o)
                list.Add(new ArcPoint(Num(o["travel"]) ?? 0, Num(o["height"]) ?? 0));
        return list.ToArray();
    }

    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Side-by-side knockback arc: base (solid blue) vs candidate (dashed orange), common scale,
    /// apex dots. Height vs horizontal travel — reads the launch feel (pop vs glide, off-stage send).</summary>
    private static string ArcOverlaySvg(ArcPoint[] b, ArcPoint[] c, double maxTravel, double maxHeight)
    {
        const int W = 150, H = 110, PAD = 8;
        double tw = maxTravel > 0.01 ? maxTravel : 1, th = maxHeight > 0.01 ? maxHeight : 1;
        double sx = (W - 2 * PAD) / tw, sy = (H - 2 * PAD) / th;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"arc\">");
        sb.Append($"<line x1=\"{PAD}\" y1=\"{H - PAD}\" x2=\"{W - PAD}\" y2=\"{H - PAD}\" stroke=\"#888\" stroke-width=\"1\"/>");
        sb.Append(Poly(c, sx, sy, "#e07b3a", "1.4", "3,2")); // candidate: dashed orange (drawn first, under base)
        sb.Append(Poly(b, sx, sy, "#1a5fd0", "1.6", null));  // base: solid blue
        sb.Append(Dot(b, sx, sy, "#d23"));
        sb.Append(Dot(c, sx, sy, "#e07b3a"));
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Poly(ArcPoint[] pts, double sx, double sy, string color, string width, string? dash)
    {
        if (pts.Length == 0) return "";
        const int H = 110, PAD = 8;
        var sb = new StringBuilder();
        sb.Append($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"{width}\"{(dash != null ? $" stroke-dasharray=\"{dash}\"" : "")} points=\"");
        foreach (var p in pts) sb.Append($"{F(PAD + p.Travel * sx)},{F(H - PAD - p.Height * sy)} ");
        sb.Append("\"/>");
        return sb.ToString();
    }

    private static string Dot(ArcPoint[] pts, double sx, double sy, string color)
    {
        if (pts.Length == 0) return "";
        const int H = 110, PAD = 8;
        var apex = pts.OrderByDescending(p => p.Height).First();
        return $"<circle cx=\"{F(PAD + apex.Travel * sx)}\" cy=\"{F(H - PAD - apex.Height * sy)}\" r=\"2.5\" fill=\"{color}\"/>";
    }

    // ── HTML report ──────────────────────────────────────────────────────────

    private static string BuildHtml(string character, string baseProfile, string candProfile, string commit,
        int seed, int matches, int[] pcts, List<MoveRow> moves, List<ComboRow> combos,
        List<StatRow> stats, List<UsageRow> usage, List<MoveCurves> curves)
    {
        int linksGained = combos.Sum(c => c.Gained), linksLost = combos.Sum(c => c.Lost);
        int movesChanged = moves.Count(m => m.AnyChange);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(character)} tuning A/B diff</title><style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;color:#1c1e21;}h1{font-size:22px;}h2{margin-top:32px;border-bottom:1px solid #ddd;padding-bottom:4px;}h3{margin-top:20px;font-size:15px;}.meta{color:#666;font-size:13px;margin-bottom:16px;max-width:900px;line-height:1.5;}");
        sb.AppendLine("table.heat{border-collapse:collapse;font-size:13px;margin:6px 0 14px;}table.heat td,table.heat th{border:1px solid #ccc;padding:3px 8px;text-align:center;white-space:nowrap;}table.heat td.move,table.heat th.move{text-align:left;}table.heat th{background:#f2f3f5;}");
        sb.AppendLine(".legend{font-size:12px;color:#444;margin:6px 0 16px;max-width:900px;line-height:1.5;}");
        sb.AppendLine(".d{font-weight:600;}.dpos{color:#1a7f37;}.dneg{color:#c0392b;}.dzero{color:#999;}");
        sb.AppendLine(".gained{background:#d9f2d9;font-weight:600;}.lost{background:#fdecea;font-weight:600;}.shift{background:#fcf3d4;}.none{color:#999;}");
        sb.AppendLine(".summary td{padding:4px 14px 4px 0;font-size:13px;}.big{font-size:17px;font-weight:600;}.badge{font-size:11px;padding:1px 6px;border-radius:9px;margin-left:6px;}.badge.g{background:#d9f2d9;color:#1a7f37;}.badge.r{background:#fdecea;color:#c0392b;}");
        sb.AppendLine(".arcs{display:flex;flex-wrap:wrap;gap:12px;margin:6px 0 20px;}.move{margin-bottom:22px;}figure{margin:0;text-align:center;font-size:11px;color:#555;width:150px;}svg.arc{border:1px solid #eee;background:#fafbfc;display:block;}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>{Escape(character)} — tuning A/B diff</h1>");
        sb.AppendLine($"<div class=\"meta\">Tool {Escape(ToolVersion)} &middot; generated {Escape(Now())} UTC &middot; commit <code>{Escape(commit)}</code> &middot; " +
            $"seed {seed} (same on both sides, so match variance cancels) &middot; {matches} self-play matches &middot; percents {string.Join(", ", pcts)}.<br>" +
            $"Baseline: <b>{Escape(baseProfile)}</b> — {Escape(TuningProfiles.Describe(baseProfile))} &middot; Candidate: <b>{Escape(candProfile)}</b> — {Escape(TuningProfiles.Describe(candProfile))}." +
            " Both runs use the real ServerSimulation; the diff walks the structured JSON (no physics re-run).</div>");

        // 0 — summary
        sb.AppendLine("<h2>Summary</h2>");
        sb.AppendLine("<table class=\"summary\"><tbody>");
        sb.AppendLine($"<tr><td>move data</td><td class=\"big\">{movesChanged}/{moves.Count}</td><td>moves changed (KV / stun / adv / apex / kill%)</td></tr>");
        if (combos.Count > 0)
            sb.AppendLine($"<tr><td>combo links</td><td class=\"big\"><span class=\"badge g\">+{linksGained} gained</span><span class=\"badge r\">-{linksLost} lost</span></td><td>true-combo links across all starters &times; hit states &times; percents</td></tr>");
        int statsMoved = stats.Count(s => s.Delta != null && s.Delta != 0);
        sb.AppendLine($"<tr><td>telemetry</td><td class=\"big\">{statsMoved}/{stats.Count}</td><td>stats moved (hit rate, combos, damage, wins, duration)</td></tr>");
        sb.AppendLine("</tbody></table>");

        if (movesChanged == 0 && linksGained == 0 && linksLost == 0 && statsMoved == 0)
            sb.AppendLine("<div class=\"legend\"><b>No differences</b> — the two profiles produce identical outputs for this character at these settings.</div>");

        // A — move-data deltas
        sb.AppendLine("<h2>Move-data diff</h2>");
        sb.AppendLine("<div class=\"legend\">Per move &times; victim %: base &rarr; candidate for knockback velocity (KV, m/s), hitstun (ticks), frame advantage (ticks) and apex (m); " +
            "kill% at move level (lowest % at which the launch crosses a blast line). Cell shows <code>base &rarr; cand</code> with the delta colored: green = higher in candidate, red = lower. Gray = unchanged.</div>");
        if (moves.Count == 0) sb.AppendLine("<div class=\"legend\">No moves in the diff.</div>");
        foreach (var m in moves)
        {
            sb.AppendLine($"<h3>{Escape(DisplayLabel(m))}{ChangedBadge(m.AnyChange)}</h3>");
            sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">%</th><th>KV</th><th>stun</th><th>adv</th><th>apex</th></tr></thead><tbody>");
            foreach (var c in m.Cells)
            {
                sb.AppendLine($"<tr><td class=\"move\">{c.Pct}%</td>" +
                    $"{DeltaCell(c.KvB, c.KvC)}{DeltaCell(c.StunB, c.StunC)}{DeltaCell(c.AdvB, c.AdvC)}{DeltaCell(c.ApexB, c.ApexC)}</tr>");
            }
            if (m.Cells.Count == 0)
                sb.AppendLine($"<tr><td class=\"move\" colspan=\"5\">{(m.KillB == null && m.KillC == null ? "no trajectory data" : "no trajectory rows")}</td></tr>");
            sb.AppendLine("</tbody></table>");
            if (m.KillB != null || m.KillC != null)
            {
                string kb = m.KillB == null ? "&mdash;" : F(m.KillB.Value);
                string kc = m.KillC == null ? "&mdash;" : F(m.KillC.Value);
                string kd = m.KillB != null && m.KillC != null ? $" <span class=\"d {DeltaClass(m.KillC.Value - m.KillB.Value)}\">({Sign(m.KillC.Value - m.KillB.Value)})</span>" : "";
                sb.AppendLine($"<div class=\"legend\">kill%: {kb} &rarr; {kc}{kd}</div>");
            }
        }

        // A2 — knockback shape overlay (base vs candidate arcs)
        if (curves.Count > 0)
        {
            sb.AppendLine("<h2>Knockback shape — base vs candidate</h2>");
            sb.AppendLine("<div class=\"legend\">Flight arc (height vs horizontal travel) at each victim %. " +
                "Base = <span style=\"color:#1a5fd0\">solid blue</span>, candidate = <span style=\"color:#e07b3a\">dashed orange</span>, dots = apex. " +
                "Common scale per move so the two curves compare directly — this is the launch feel: pop vs glide, how far it sends, how long it stays air.</div>");
            foreach (var grp in curves.GroupBy(c => (c.Label, c.HitIndex)))
            {
                var row = grp.ToList();
                var all = row.SelectMany(r => r.Base.Concat(r.Cand)).ToList();
                if (all.Count == 0) continue;
                double mt = all.Max(p => p.Travel), mh = all.Max(p => p.Height);
                string label = grp.Key.HitIndex > 0 ? $"{grp.Key.Label} (hit {grp.Key.HitIndex + 1})" : grp.Key.Label;
                sb.AppendLine($"<div class=\"move\"><strong>{Escape(label)}</strong><div class=\"arcs\">");
                foreach (var cv in row.OrderBy(r => Array.IndexOf(pcts, r.Pct)))
                {
                    string kv = cv.KvB != null && cv.KvC != null ? $" &middot; KV {F(cv.KvB.Value)}&rarr;{F(cv.KvC.Value)}" : "";
                    sb.AppendLine($"<figure>{ArcOverlaySvg(cv.Base, cv.Cand, mt, mh)}<figcaption>{cv.Pct}%{kv}</figcaption></figure>");
                }
                sb.AppendLine("</div></div>");
            }
        }

        // B — true-combo graph diff
        if (combos.Count > 0)
        {
            sb.AppendLine("<h2>True-combo graph diff</h2>");
            sb.AppendLine("<div class=\"legend\">Verdict transitions per starter &times; follow-up &times; victim %: " +
                "<span class=\"gained\">T gained</span> (was not a true combo, now lands in hitstun), <span class=\"lost\">T lost</span> (was true, now doesn't), " +
                "<span class=\"shift\">F/T shift</span> (landed-after-stun &harr; never, or true &harr; landed-after-stun), plain = verdict unchanged. " +
                "Tightness = window in ticks (stun &minus; attacker budget); <code>b &rarr; c</code> shows the shift when both sides were true.</div>");
            foreach (var s in combos)
            {
                int g = s.Gained, l = s.Lost;
                string badge = g > 0 || l > 0 ? $" <span class=\"badge g\">+{g}</span><span class=\"badge r\">-{l}</span>" : "";
                sb.AppendLine($"<h3>{Escape(s.Move)} — {Escape(s.State)} hit{badge}</h3>");
                sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">follow-up</th>");
                foreach (var p in pcts) sb.AppendLine($"<th>{p}%</th>");
                sb.AppendLine("</tr></thead><tbody>");
                foreach (var (fu, cells) in s.FollowUps)
                {
                    sb.AppendLine($"<tr><td class=\"move\">{Escape(fu)}</td>");
                    foreach (var c in cells) sb.AppendLine(ComboCell(c));
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
        }

        // C — telemetry side-by-side
        sb.AppendLine("<h2>Self-play telemetry diff</h2>");
        sb.AppendLine("<div class=\"legend\">Same seed on both sides &rarr; the delta is the tuning effect, not match variance. " +
            "Green = candidate higher, red = lower. Hit rate / whiff rate are percentages; combos in hits; damage per match and per stock lost.</div>");
        sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">stat</th><th>base</th><th>candidate</th><th>&Delta;</th></tr></thead><tbody>");
        foreach (var s in stats)
            sb.AppendLine($"<tr><td class=\"move\">{Escape(StatLabel(s.Key))}</td><td>{StatVal(s.Key, s.Base)}</td><td>{StatVal(s.Key, s.Cand)}</td>{StatDeltaCell(s.Key, s.Base, s.Cand)}</tr>");
        sb.AppendLine("</tbody></table>");

        if (usage.Count > 0)
        {
            sb.AppendLine("<h3>Per-move usage</h3>");
            sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">move</th><th>swings b &rarr; c</th><th>hits b &rarr; c</th><th>whiffs b &rarr; c</th><th>hit% b &rarr; c</th></tr></thead><tbody>");
            foreach (var u in usage)
            {
                double? pctB = u.SwingsB > 0 ? 100 * u.HitsB / u.SwingsB : 0;
                double? pctC = u.SwingsC > 0 ? 100 * u.HitsC / u.SwingsC : 0;
                sb.AppendLine($"<tr><td class=\"move\">{Escape(u.Label)} {Escape(u.Ability)}</td>" +
                    $"{DeltaCell(u.SwingsB, u.SwingsC)}{DeltaCell(u.HitsB, u.HitsC)}{DeltaCell(u.WhiffsB, u.WhiffsC)}{DeltaCell(pctB, pctC)}</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string ChangedBadge(bool any)
        => any ? "" : " <span class=\"none\">(unchanged)</span>";

    private static string ComboCell(EdgeCell c)
    {
        if (c.OldVerdict == null && c.NewVerdict == null) return "<td class=\"none\">&ndash;</td>";
        string cls = "";
        if (c.OldVerdict != "true" && c.NewVerdict == "true") cls = "gained";
        else if (c.OldVerdict == "true" && c.NewVerdict != "true") cls = "lost";
        else if (c.OldVerdict != c.NewVerdict) cls = "shift";
        string text = $"{ShortVerdict(c.OldVerdict)} &rarr; {ShortVerdict(c.NewVerdict)}";
        if (c.TightB != null && c.TightC != null && c.OldVerdict == "true" && c.NewVerdict == "true")
        {
            double d = c.TightC.Value - c.TightB.Value;
            text += $" <span class=\"d {DeltaClass(d)}\">({Sign(d)})</span>";
        }
        string extra = c.TightB != null && c.TightC != null
            ? $" title=\"tight {F(c.TightB.Value)} &rarr; {F(c.TightC.Value)}\""
            : "";
        return $"<td{(cls.Length > 0 ? $" class=\"{cls}\"" : "")}{extra}>{text}</td>";
    }

    private static string ShortVerdict(string? v) => v switch
    {
        "true" => "T",
        "false" => "F",
        "never" => "-",
        _ => "&ndash;",
    };

    private static string DeltaCell(double? b, double? c)
    {
        if (b == null && c == null) return "<td class=\"none\">&ndash;</td>";
        if (b == null) return $"<td class=\"gained\">{F(c!.Value)}</td>";
        if (c == null) return $"<td class=\"lost\">{F(b.Value)}</td>";
        string arrow = b.Value == c.Value ? F(b.Value) : $"{F(b.Value)} &rarr; {F(c.Value)}";
        double d = c.Value - b.Value;
        string delta = d == 0 ? "" : $" <span class=\"d {DeltaClass(d)}\">({Sign(d)})</span>";
        return $"<td>{arrow}{delta}</td>";
    }

    private static string DeltaCell2(double? b, double? c)
    {
        if (b == null || c == null) return "<td class=\"none\">&ndash;</td>";
        double d = c.Value - b.Value;
        return d == 0 ? "<td class=\"none\">0</td>"
            : $"<td class=\"d {DeltaClass(d)}\">{Sign(d)}</td>";
    }

    private static string DeltaClass(double d) => d > 0 ? "dpos" : d < 0 ? "dneg" : "dzero";
    private static string Sign(double d) => d > 0 ? $"+{F(d)}" : F(d);
    private static string NumCell(double? v) => v == null ? "&ndash;" : F(v.Value);

    /// <summary>Rates are stored as 0..1 fractions — show them as percentages everywhere.</summary>
    private static bool IsRate(string key) => key is "hitRate" or "whiffRate";

    private static string StatVal(string key, double? v)
        => v == null ? "&ndash;" : IsRate(key) ? $"{v.Value * 100:0.#}%" : F(v.Value);

    private static string StatDeltaCell(string key, double? b, double? c)
    {
        if (b == null || c == null) return "<td class=\"none\">&ndash;</td>";
        double d = c.Value - b.Value;
        if (d == 0) return "<td class=\"none\">0</td>";
        return $"<td class=\"d {DeltaClass(d)}\">{(IsRate(key) ? Sign(d * 100) + "pp" : Sign(d))}</td>";
    }

    private static string StatLabel(string key) => key switch
    {
        "hitRate" => "hit rate",
        "whiffRate" => "whiff rate",
        "avgComboLen" => "avg combo length",
        "maxComboLen" => "max combo length",
        "damagePerMatch" => "damage / match",
        "damagePerStock" => "damage per stock lost",
        "totalDeaths" => "total deaths",
        "creditedDeaths" => "credited deaths",
        "uncreditedDeaths" => "uncredited deaths",
        "winsA" => "wins (bot A)",
        "winsB" => "wins (bot B)",
        "draws" => "draws",
        "avgDurationTicks" => "avg match duration (s)",
        "maxDurationTicks" => "max match duration (s)",
        "totalSwings" => "total swings",
        "totalHits" => "total hits",
        "totalWhiffs" => "total whiffs",
        "totalDamage" => "total damage",
        _ => key,
    };

    private static string DisplayLabel(MoveRow m)
        => m.HitIndex > 0 ? $"{m.Label} (hit {m.HitIndex + 1})" : m.Label;

    // ── Markdown summary ─────────────────────────────────────────────────────

    private static string BuildMarkdown(string character, string baseProfile, string candProfile, string commit,
        int seed, int matches, int[] pcts, List<MoveRow> moves, List<ComboRow> combos,
        List<StatRow> stats, List<UsageRow> usage)
    {
        int linksGained = combos.Sum(c => c.Gained), linksLost = combos.Sum(c => c.Lost);
        int movesChanged = moves.Count(m => m.AnyChange);
        var sb = new StringBuilder();
        sb.AppendLine($"# {character} — tuning A/B diff: {baseProfile} vs {candProfile}");
        sb.AppendLine();
        sb.AppendLine($"Tool {ToolVersion} · commit `{commit}` · seed {seed} (same both sides) · {matches} matches · percents {string.Join(", ", pcts)}.");
        sb.AppendLine($"Baseline **{baseProfile}**: {TuningProfiles.Describe(baseProfile)}. Candidate **{candProfile}**: {TuningProfiles.Describe(candProfile)}.");
        sb.AppendLine();
        sb.AppendLine($"- **Move data**: {movesChanged}/{moves.Count} moves changed.");
        if (combos.Count > 0)
            sb.AppendLine($"- **Combo links**: **+{linksGained} gained, -{linksLost} lost** (true-combo edges across starters × hit states × %).");
        int statsMoved = stats.Count(s => s.Delta != null && s.Delta != 0);
        sb.AppendLine($"- **Telemetry**: {statsMoved}/{stats.Count} stats moved (same seed → tuning effect only).");
        sb.AppendLine();

        sb.AppendLine("## Move-data diff");
        sb.AppendLine();
        sb.AppendLine("| move | % | KV b→c | stun b→c | adv b→c | apex b→c | kill% b→c |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var m in moves)
            foreach (var c in m.Cells)
            {
                string kill = m.KillB == null && m.KillC == null ? "" :
                    m.KillB == null ? $"—→{F(m.KillC!.Value)}" :
                    m.KillC == null ? $"{F(m.KillB.Value)}→—" :
                    $"{F(m.KillB.Value)}→{F(m.KillC.Value)}";
                sb.AppendLine($"| {Escape(DisplayLabel(m))} | {c.Pct}% | {Pair(c.KvB, c.KvC)} | {Pair(c.StunB, c.StunC)} | {Pair(c.AdvB, c.AdvC)} | {Pair(c.ApexB, c.ApexC)} | {kill} |");
            }
        sb.AppendLine();

        if (combos.Count > 0)
        {
            sb.AppendLine("## True-combo graph diff");
            sb.AppendLine();
            sb.AppendLine("Verdict per starter × follow-up × %: `-` never connected, `F` landed after stun, `T` true combo. `a→b` = transition.");
            sb.AppendLine();
            foreach (var s in combos)
            {
                sb.AppendLine($"### {s.Move} — {s.State} hit (+{s.Gained}/-{s.Lost})");
                sb.AppendLine();
                sb.AppendLine("| follow-up | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
                sb.AppendLine("|---" + string.Join("", pcts.Select(_ => "|---")) + "|");
                foreach (var (fu, cells) in s.FollowUps)
                {
                    var parts = cells.Select(c => c.OldVerdict == null && c.NewVerdict == null ? "—"
                        : $"{ShortVerdict(c.OldVerdict)}→{ShortVerdict(c.NewVerdict)}" + (c.TightB != null && c.TightC != null && c.OldVerdict == "true" && c.NewVerdict == "true" ? $" ({Sign(c.TightC.Value - c.TightB.Value)})" : ""));
                    sb.AppendLine($"| {Escape(fu)} | {string.Join(" | ", parts)} |");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Self-play telemetry diff");
        sb.AppendLine();
        sb.AppendLine("| stat | base | candidate | Δ |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in stats)
            sb.AppendLine($"| {StatLabel(s.Key)} | {MdStatVal(s.Key, s.Base)} | {MdStatVal(s.Key, s.Cand)} | {MdStatDelta(s.Key, s.Base, s.Cand)} |");
        if (usage.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("| move | swings b→c | hits b→c | whiffs b→c | hit% b→c |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var u in usage)
            {
                double? pctB = u.SwingsB > 0 ? 100 * u.HitsB / u.SwingsB : 0;
                double? pctC = u.SwingsC > 0 ? 100 * u.HitsC / u.SwingsC : 0;
                sb.AppendLine($"| {Escape(u.Label)} {Escape(u.Ability)} | {Pair(u.SwingsB, u.SwingsC)} | {Pair(u.HitsB, u.HitsC)} | {Pair(u.WhiffsB, u.WhiffsC)} | {Pair(pctB, pctC)} |");
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Pair(double? b, double? c)
        => b == null && c == null ? "—" : b == null ? $"—→{F(c!.Value)}" : c == null ? $"{F(b.Value)}→—" : F(b.Value) == F(c.Value) ? F(b.Value) : $"{F(b.Value)}→{F(c.Value)}";

    private static string MdStatVal(string key, double? v)
        => v == null ? "—" : IsRate(key) ? $"{v.Value * 100:0.#}%" : F(v.Value);

    private static string MdStatDelta(string key, double? b, double? c)
    {
        if (b == null || c == null) return "—";
        double d = c.Value - b.Value;
        return d == 0 ? "0" : IsRate(key) ? $"{Sign(d * 100)}pp" : Sign(d);
    }

    // ── Output doc + helpers ─────────────────────────────────────────────────

    private static JsonObject Meta(string profile, int seed, int matches, int[] pcts, string commit,
        string moveGeneratedAt, string telemetryGeneratedAt) => new()
    {
        ["profile"] = profile,
        ["description"] = TuningProfiles.Describe(profile),
        ["commit"] = commit,
        ["seed"] = seed,
        ["matches"] = matches,
        ["percents"] = new JsonArray(pcts.Select(p => (JsonNode)p).ToArray()),
        ["moveDataGeneratedAt"] = moveGeneratedAt,
        ["telemetryGeneratedAt"] = telemetryGeneratedAt,
    };

    private static JsonNode NodeOf(TuningDiffNode n) => JsonNode.Parse(JsonSerializer.Serialize(n, JsonOpts))!;

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string CurrentCommit()
    {
        try
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                {
                    RedirectStandardOutput = true, UseShellExecute = false,
                },
            };
            p.Start();
            string sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return p.ExitCode == 0 && sha.Length == 40 ? sha : "unknown";
        }
        catch { return "unknown"; }
    }

    private static string? ParseArg(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int ParseInt(string[] args, string key, int fallback)
    {
        var s = ParseArg(args, key);
        return s != null && int.TryParse(s, out int v) ? v : fallback;
    }

    private static int[]? ParsePcts(string[] args)
    {
        int i = Array.IndexOf(args, "--pcts");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1].Split(',').Select(int.Parse).ToArray();
    }

    private static string Escape(string s) => WebUtility.HtmlEncode(s);

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
