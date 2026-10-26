using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.MovementReport;

/// <summary>
/// Movement data sheet (issue #150): measures run / shield / jump / double jump / short hop /
/// air drift / fall / fast fall / stop / reversal for every character from the REAL
/// ServerSimulation (MovementProbe) and renders a side-by-side comparison table +
/// per-character curves + stage-relative reads + a Melee reference comparison.
/// Lossless per-character JSON (full tick-by-tick sample curves) + self-contained HTML +
/// markdown, consistent with the move-data / self-play report pattern. No new physics —
/// every number comes out of the sim. Stage-relative rows use the real baked arenas
/// (data/arenas/*.arena). Melee reference: docs/research/melee-movement-audit.md.
/// </summary>
internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true, // MovementStats is a field-based struct
    };

    /// <summary>Human reaction time (s) — the reference for "reactable" verdicts.</summary>
    private const float ReactionTime = 0.25f;

    private static int Main(string[] args)
    {
        string charName = ParseArg(args, "--char") ?? "all";
        string stageName = ParseArg(args, "--stage") ?? "colosseum";
        string? jsonDir = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        string? outPath = ParseArg(args, "--out");

        var defs = new List<CharacterDefinition>();
        if (charName == "all" && (args.Contains("--ground-speed") || args.Contains("--progressive-tuning")))
        {
            var roster = BuiltInRosterManifestCodec.Load(Path.Combine("content-cooked", "roster", "manifest.json"));
            foreach (var entry in roster.Entries) defs.Add(BuiltInContentResolver.Resolve(entry.Selector).Definition);
        }
        else if (charName == "all")
        {
            foreach (CharacterClass c in Enum.GetValues(typeof(CharacterClass)))
                if (c != CharacterClass.None) defs.Add(BuiltInContentResolver.Resolve(c).Definition);
        }
        else
        {
            defs.Add(ResolveCharacter(charName));
        }

        if (args.Contains("--progressive-tuning")) return ReportProgressive(defs, args, jsonDir, outPath);
        if (args.Contains("--ground-speed")) return ReportGroundSpeed(defs, args, jsonDir, outPath);

        var (_, stageWidth) = LoadStage(stageName);
        // The probe always runs on a flat arena: it measures MOVEMENT, and real stages
        // have elevation (a landed character would run up terrain and pollute apex/stop
        // metrics). The baked stage contributes its width for stage-relative reads only.
        var arena = FlatArena();
        var measured = new List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)>();
        foreach (var def in defs) measured.Add((def, MovementProbe.Measure(def, arena)));

        if (jsonDir != null)
        {
            Directory.CreateDirectory(jsonDir);
            foreach (var (def, m) in measured)
                File.WriteAllText(Path.Combine(jsonDir, $"movement-{def.Class.ToString().ToLowerInvariant()}.json"),
                    JsonSerializer.Serialize(m, JsonOpts));
        }

        if (htmlPath != null)
            File.WriteAllText(htmlPath, BuildHtml(measured, stageName, stageWidth));

        string md = BuildMarkdown(measured, stageName, stageWidth);
        if (outPath != null) File.WriteAllText(outPath, md);
        else Console.WriteLine(md);
        return 0;
    }

    // ── CLI ───────────────────────────────────────────────────────────────────

    private static string? ParseArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key) return args[i + 1];
        return null;
    }

    private static CharacterDefinition ResolveCharacter(string which)
    {
        var cls = which.ToLowerInvariant() switch
        {
            "fightguy" => CharacterClass.FightGuy,
            "manki" => CharacterClass.Manki,
            "wibou" => CharacterClass.Wibou,
            _ => throw new ArgumentException($"unknown character: {which} (expected one of: fightguy, manki, wibou, all)"),
        };
        return BuiltInContentResolver.Resolve(cls).Definition;
    }

    /// <summary>Load a real baked stage for stage-relative reads. Falls back to the flat
    /// probe arena + a colosseum-sized width when the file is missing (still deterministic).</summary>
    private static (ArenaDefinition? Arena, float Width) LoadStage(string name)
    {
        string path = Path.Combine("data", "arenas", name + ".arena");
        if (File.Exists(path))
        {
            var arena = ArenaBinaryFormat.Deserialize(File.ReadAllBytes(path));
            if (arena.HasValue && arena.Value.MaxX > arena.Value.MinX)
                return (arena.Value, arena.Value.MaxX - arena.Value.MinX);
        }
        Console.Error.WriteLine($"warning: stage '{name}' not found at {path} — using flat arena, width 30.6 m (colosseum)");
        return (null, 30.6f);
    }

    private static ArenaDefinition FlatArena()
    {
        const int w = 200, h = 200;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "movement-probe",
            DisplayName = "Movement Probe Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f },
        };
    }

    // ── Markdown ──────────────────────────────────────────────────────────────

    private static string BuildMarkdown(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all,
        string stageName, float stageWidth)
    {
        var sb = new StringBuilder();
        string generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        sb.AppendLine("# Movement data sheet — measured from the real sim (issue #150)");
        sb.AppendLine();
        sb.AppendLine($"> Generated {generated} · scripted inputs on the real ServerSimulation (60 Hz tick). ");
        sb.AppendLine("> Run: hold right from standstill. Shield: raise at cruise and hold. Jump: full jump (held past the short-hop ");
        sb.AppendLine("> window). Short hop: press + release inside the window. Double jump: jump edge at first apex. ");
        sb.AppendLine("> Drift: stick held through the full hop. Fall: spawned airborne at 50 m (float window skipped). ");
        sb.AppendLine("> Reversal: cruise right, then full opposite input (pivot skid + re-accel). Stop: release at cruise. ");
        sb.AppendLine($"> Stage-relative rows use the real baked <b>{stageName}</b> arena ({stageWidth:F1} m wide). ");
        sb.AppendLine("> Values are measured (effective behavior, incl. rush kick-off, pivot skids, caps), not authored constants.");
        sb.AppendLine();

        // Side-by-side comparison
        sb.AppendLine("## Comparison");
        sb.AppendLine();
        sb.AppendLine("| metric | " + string.Join(" | ", all.Select(x => x.Def.DisplayName)) + " |");
        sb.AppendLine("|" + string.Concat(all.Select(_ => "---|")));
        foreach (var row in Rows(all, stageWidth))
        {
            sb.Append("| ").Append(row.Label).Append(" | ");
            sb.Append(string.Join(" | ", row.Values));
            sb.AppendLine(" |");
        }
        sb.AppendLine();

        sb.AppendLine(BuildRosterReadMarkdown(all, stageWidth));
        sb.AppendLine(BuildMeleeMarkdown(all));
        sb.AppendLine();

        foreach (var (def, m) in all)
        {
            sb.AppendLine($"## {def.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"- **Run**: {m.Run.MaxSpeed:F1} m/s (authored {m.Authored.RunSpeed:F0})"
                + (m.Run.Note.Length > 0 ? $" — {m.Run.Note}" : $"; time-to-max {m.Run.TimeToMaxTicks + 1} ticks, {m.Run.DistanceToMax:F2} m"));
            sb.AppendLine($"- **Shield**: stops in {m.Shield.StopTicks} tick(s), {m.Shield.StopDistance:F2} m brake distance; held for {m.Shield.HoldTicks} ticks");
            sb.AppendLine($"- **Jump**: apex {m.Jump.ApexHeight:F2} m at {m.Jump.TimeToApexTicks} ticks, airtime {m.Jump.AirtimeTicks / 60f:F2} s, "
                + $"full-hop drift {m.Jump.HorizontalDistance:F2} m = {Pct(m.Jump.HorizontalDistance / stageWidth)}; running jump carries {m.RunningJump.HorizontalDistance:F2} m");
            sb.AppendLine($"- **Short hop**: apex {m.ShortHop.ApexHeight:F2} m, airtime {m.ShortHop.AirtimeTicks / 60f:F2} s "
                + $"(authored force {m.Authored.ShortHopForce:F1} vs jump {m.Authored.JumpForce:F1} = ratio {m.Authored.ShortHopForce / m.Authored.JumpForce:F2}; Melee ~0.58)");
            sb.AppendLine($"- **Double jump**: second apex {m.DoubleJump.ApexHeight:F2} m, total airtime {m.DoubleJump.AirtimeTicks / 60f:F2} s");
            sb.AppendLine($"- **Air drift**: speed cap {m.Jump.DriftSpeedMax:F1} m/s (authored {m.Authored.AirSpeedMax:F1}; "
                + $"{Pct(m.Jump.DriftSpeedMax / m.Run.MaxSpeed)} of run speed)");
            sb.AppendLine($"- **Fall** (50 m drop): max {m.Fall.MaxFallSpeed:F0} m/s (authored {m.Authored.MaxFallSpeed:F0}), reached "
                + $"{m.Fall.TimeToMaxFallTicks / 60f:F2} s into the drop, descent {m.Fall.DescentTicks / 60f:F2} s; "
                + $"fast fall {m.Fall.FastFallSpeed:F0} m/s (authored {m.Authored.FastFallSpeed:F0}), "
                + $"from jump apex {m.Fall.FastFallFromJumpTicks / 60f:F2} s (natural {m.Jump.AirtimeTicks / 60f:F2} s full hop)");
            sb.AppendLine($"- **Reversal** (cruise → opposite cruise): {m.Reversal.ReversalTicks / 60f:F2} s, "
                + $"{m.Reversal.Displacement:F2} m covered (pivot skid + re-accel)");
            sb.AppendLine($"- **Stop** (cruise → standstill): {m.Stop.StopTicks / 60f:F2} s, {m.Stop.StopDistance:F2} m");
            sb.AppendLine();
        }
        // The comparison rows are shared with the HTML renderer; strip the styling spans.
        return sb.ToString().Replace("<span class=\"note\">(", "(").Replace(")</span>", ")");
    }

    private static string Pct(float f) => $"{f * 100f:F0}%";

    // ── Roster read (decision section) ────────────────────────────────────────

    private static string BuildRosterReadMarkdown(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all,
        float stageWidth)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Roster read");
        sb.AppendLine();
        sb.AppendLine("What the numbers mean, per character (computed from the measured values above):");
        sb.AppendLine();
        foreach (var line in RosterLines(all))
            sb.AppendLine("- " + line);
        sb.AppendLine();
        sb.AppendLine($"- **Too fast?** Run crosses {stageWidth:F0} m in "
            + string.Join(" / ", all.Select(x => $"{x.Def.DisplayName} {stageWidth / x.M.Run.MaxSpeed:F2} s")) + ". "
            + "Full-hop airtime is " + string.Join(" / ", all.Select(x => $"{x.M.Jump.AirtimeTicks / 60f:F2} s")) + " "
            + $"vs ~{ReactionTime} s reaction — reactable but tight (2-3×). Fast-fall from jump apex: "
            + string.Join(" / ", all.Select(x => $"{x.M.Fall.FastFallFromJumpTicks / 60f:F2} s")) + " — under reaction, "
            + "so a fast-fall landing cannot be reacted to; reads must come from the jump start, not the landing.");
        var broken = all.Where(x => x.M.ShortHop.ApexHeight <= 0.01f).Select(x => x.Def.DisplayName).ToList();
        if (broken.Count > 0)
            sb.AppendLine($"- **Broken short hop: {string.Join(", ", broken)}** — the short-hop impulse rises under the "
                + "0.10 m PlatformLandTolerance on the first airborne tick (no upward-velocity gate in the non-hitstun "
                + "ground snap), so the sim snaps the character back down and the hop never leaves the ground. "
                + "Fix candidates: raise the impulse above ~6.7 m/s, or add the hitstun-branch's `VY <= 0` gate to the snap.");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Deterministic per-character reads: rank each metric, surface the standout
    /// and the weakest for every character.</summary>
    private static List<string> RosterLines(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        var lines = new List<string>();
        foreach (var (def, m) in all)
        {
            var s = new List<string>();
            var w = new List<string>();
            void Rank(string metric, Func<(CharacterDefinition Def, MovementProbe.CharacterMovement M), float> f)
            {
                var best = all.OrderByDescending(f).First().M;
                var worst = all.OrderBy(f).First().M;
                if (ReferenceEquals(best, m)) s.Add(metric);
                if (ReferenceEquals(worst, m)) w.Add(metric);
            }
            Rank("highest jump", x => x.M.Jump.ApexHeight);
            Rank("fastest run", x => x.M.Run.MaxSpeed);
            Rank("longest airtime", x => x.M.DoubleJump.AirtimeTicks);
            Rank("largest air drift", x => x.M.Jump.DriftSpeedMax);
            Rank("safest stop", x => -x.M.Stop.StopDistance);
            var airGround = m.Jump.DriftSpeedMax / m.Run.MaxSpeed;
            var minAir = all.OrderBy(x => x.M.Jump.DriftSpeedMax / x.M.Run.MaxSpeed).First().M;
            if (ReferenceEquals(minAir, m)) w.Add("most ground-dominant (lowest air/run)");
            string read = $"**{def.DisplayName}**: run {m.Run.MaxSpeed:F0} m/s, shield stops in {m.Shield.StopTicks} tick(s) "
                + $"and holds for {m.Shield.HoldTicks} ticks, jump {m.Jump.ApexHeight:F2} m, "
                + $"air/run {Pct(airGround)}, stop {m.Stop.StopDistance:F1} m.";
            if (s.Count > 0) read += $" Best at: {string.Join(", ", s)}.";
            if (w.Count > 0) read += $" Weakest at: {string.Join(", ", w)}.";
            lines.Add(read);
        }
        return lines;
    }

    // ── Melee comparison ──────────────────────────────────────────────────────

    private static string BuildMeleeMarkdown(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        var rows = MeleeRows(all);
        var sb = new StringBuilder();
        sb.AppendLine("## Melee comparison");
        sb.AppendLine();
        sb.AppendLine("Melee values: docs/research/melee-movement-audit.md (SSBWiki \\[community\\]; derived frame counts transfer 1:1 — 60 f/s = 60 ticks/s). Absolute speeds don't transfer (u/f vs m/s), timings and ratios do.");
        sb.AppendLine();
        sb.AppendLine("| metric | SLOPArena (measured) | Melee reference | read |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Label} | {r.Sa} | {r.Melee} | {r.Read} |");
        sb.AppendLine();
        return sb.ToString();
    }

    private sealed record MeleeRow(string Label, string Sa, string Melee, string Read);

    private static List<MeleeRow> MeleeRows(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        static string Rng(string fmt, IEnumerable<float> vs) => string.Join("–", vs.Select(v => string.Format(CultureInfo.InvariantCulture, fmt, v)));
        var fh = all.Select(x => (float)x.M.Jump.AirtimeTicks / 60f).ToArray();
        var sh = all.Select(x => (float)x.M.ShortHop.AirtimeTicks / 60f).ToArray();
        var ff = all.Select(x => x.M.Fall.FastFallSpeed / x.M.Fall.MaxFallSpeed).ToArray();
        var ar = all.Select(x => x.M.Jump.DriftSpeedMax / x.M.Run.MaxSpeed).ToArray();
        var stop = all.Select(x => (float)x.M.Stop.StopTicks / 60f).ToArray();
        var rev = all.Select(x => (float)x.M.Reversal.ReversalTicks / 60f).ToArray();
        var sq = all.Select(x => (float)x.M.Authored.JumpSquatTicks).ToArray();
        var ratio = all.Select(x => x.M.Authored.ShortHopForce / x.M.Authored.JumpForce).ToArray();
        return new List<MeleeRow>
        {
            new("Jump squat", Rng("{0:F0} t", sq), "3–8 f (Fox 3, Marth 4, Puff 5, Bowser 8)", "in Melee range"),
            new("Full-hop airtime", Rng("{0:F2} s", fh), "Fox ~33 f, Marth 57–59 f", "FG/Wibou ≈ Fox-fast, Manki ≈ Marth"),
            new("Short-hop airtime", Rng("{0:F2} s", sh), "Fox ~19 f, Marth 36–38 f", "short hop in the fast band"),
            new("Short/full jump force", Rng("{0:F2}", ratio), "≈ 0.58 (derived)", "Melee-shaped (0.7 was the pre-audit value)"),
            new("Fast fall / fall", Rng("{0:F2}", ff), "1.14–1.26 (Fox 3.4/2.8 … Puff 1.6/1.3)", "Melee-shaped, adopted (audit §3.4)"),
            new("Air speed / run", Rng("{0:F2}", ar), "0.38 Fox – 0.5 Marth – 1.23 Puff", "upper-mid band — air slower than ground, Melee norm"),
            new("Stop from run", Rng("{0:F2} s", stop), "Fox 27.5 f, Marth 30 f, Puff 12 f", "SLOPA brakes faster than Fox/Marth"),
            new("Reversal (cruise→cruise)", Rng("{0:F2} s", rev), "direction-change pivot", "SLOPA pivot 2–3× slower"),
        };
    }

    // ── HTML ──────────────────────────────────────────────────────────────────

    private static string BuildHtml(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all,
        string stageName, float stageWidth)
    {
        var sb = new StringBuilder();
        string generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Movement data sheet</title><style>");
        sb.AppendLine("body{background:#0d1117;color:#c9d1d9;font-family:ui-monospace,Menlo,Consolas,monospace;margin:2rem auto;max-width:1100px;padding:0 1rem}");
        sb.AppendLine("h1{color:#f0f6fc}h2{margin-top:2.5rem;color:#58a6ff;border-bottom:1px solid #30363d;padding-bottom:.3rem}");
        sb.AppendLine("h3{color:#f0f6fc;font-size:.85rem;margin:0 0 .4rem}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;margin:1rem 0;font-size:.85rem}");
        sb.AppendLine("th,td{border:1px solid #30363d;padding:.4rem .6rem;text-align:right}th{background:#161b22;color:#f0f6fc}");
        sb.AppendLine("td:first-child{text-align:left;color:#f0f6fc;white-space:nowrap}");
        sb.AppendLine(".meta{color:#8b949e;font-size:.85rem;line-height:1.5}.note{color:#f0883e}.verdict{background:#161b22;border:1px solid #30363d;border-radius:6px;padding:.8rem 1rem;margin:1rem 0;line-height:1.6}");
        sb.AppendLine(".curves{display:flex;flex-wrap:wrap;gap:1rem}.curve{background:#161b22;border:1px solid #30363d;padding:.6rem}");
        sb.AppendLine("svg text{fill:#8b949e;font-size:9px}svg line{stroke:#30363d}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Movement data sheet</h1>");
        sb.AppendLine($"<div class=\"meta\">Generated {generated} &middot; scripted inputs on the real ServerSimulation (60 Hz). ");
        sb.AppendLine("Run: hold right &middot; Shield: raise at cruise and hold &middot; Jump: full jump &middot; Short hop: press + release &middot; ");
        sb.AppendLine("Double jump: edge at first apex &middot; Fall: 50 m drop (natural / fast fall) &middot; Reversal: cruise → opposite input &middot; Stop: release.<br>");
        sb.AppendLine($"Stage-relative rows: real baked <b>{stageName}</b> ({stageWidth:F1} m wide). Measured <em>effective</em> behavior — not authored constants; authored in <span class=\"note\">orange</span>.</div>");

        // Side-by-side comparison
        sb.AppendLine("<h2>Comparison</h2><table><tr><th>metric</th>");
        foreach (var (def, _) in all) sb.Append($"<th>{Escape(def.DisplayName)}</th>");
        sb.AppendLine("</tr>");
        foreach (var row in Rows(all, stageWidth))
        {
            sb.Append($"<tr><td>{Escape(row.Label)}</td>");
            foreach (var v in row.Values) sb.Append($"<td>{v}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");

        // Roster read + Melee comparison
        sb.AppendLine("<h2>Roster read</h2><div class=\"verdict\">");
        foreach (var line in RosterLines(all)) sb.AppendLine($"<p>{line}</p>");
        sb.AppendLine($"<p><b>Too fast?</b> Run crosses {stageWidth:F0} m in "
            + string.Join(" / ", all.Select(x => $"{Escape(x.Def.DisplayName)} {stageWidth / x.M.Run.MaxSpeed:F2} s")) + ". "
            + $"Full-hop airtime {string.Join(" / ", all.Select(x => $"{x.M.Jump.AirtimeTicks / 60f:F2} s"))} vs ~{ReactionTime} s reaction "
            + "— reactable but tight. Fast-fall from jump apex "
            + string.Join(" / ", all.Select(x => $"{x.M.Fall.FastFallFromJumpTicks / 60f:F2} s")) + " — under reaction: "
            + "a fast-fall landing cannot be reacted to; reads come from the jump start, not the landing.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Melee comparison</h2>");
        sb.AppendLine("<div class=\"meta\">Melee values: docs/research/melee-movement-audit.md (SSBWiki [community]; derived frame counts transfer 1:1 — 60 f/s = 60 ticks/s). Absolute speeds don't transfer (u/f vs m/s), timings and ratios do.</div>");
        sb.AppendLine("<table><tr><th>metric</th><th>SLOPArena (measured)</th><th>Melee reference</th><th>read</th></tr>");
        foreach (var r in MeleeRows(all))
            sb.AppendLine($"<tr><td>{Escape(r.Label)}</td><td>{r.Sa}</td><td style=\"text-align:left\">{Escape(r.Melee)}</td><td style=\"text-align:left\">{Escape(r.Read)}</td></tr>");
        sb.AppendLine("</table>");

        foreach (var (def, m) in all)
        {
            float groundY = def.CapsuleHeight * 0.5f;
            sb.AppendLine($"<h2>{Escape(def.DisplayName)}</h2>");
            sb.AppendLine("<table><tr><th>metric</th><th>value</th></tr>");
            Add(sb, "Run max speed", $"{m.Run.MaxSpeed:F1} m/s <span class=\"note\">(authored {m.Authored.RunSpeed:F0})</span>");
            Add(sb, "Run time-to-max", m.Run.Note.Length > 0 ? "instant — tick 1 <span class=\"note\">(rush kick-off, no ramp)</span>"
                : $"{m.Run.TimeToMaxTicks + 1} ticks, {m.Run.DistanceToMax:F2} m");
            Add(sb, "Shield", $"brakes in {m.Shield.StopTicks} tick(s) &middot; {m.Shield.StopDistance:F2} m &middot; held {m.Shield.HoldTicks} ticks");
            Add(sb, "Jump", $"apex {m.Jump.ApexHeight:F2} m at {m.Jump.TimeToApexTicks} ticks &middot; airtime {m.Jump.AirtimeTicks / 60f:F2} s &middot; "
                + $"full-hop drift {m.Jump.HorizontalDistance:F2} m = {Pct(m.Jump.HorizontalDistance / stageWidth)} of stage");
            Add(sb, "Short hop", $"apex {m.ShortHop.ApexHeight:F2} m &middot; airtime {m.ShortHop.AirtimeTicks / 60f:F2} s &middot; "
                + $"drift {m.ShortHop.HorizontalDistance:F2} m <span class=\"note\">(force {m.Authored.ShortHopForce:F1} vs jump {m.Authored.JumpForce:F1})</span>");
            Add(sb, "Running jump", $"full hop from cruise: {m.RunningJump.HorizontalDistance:F2} m, apex {m.RunningJump.ApexHeight:F2} m");
            Add(sb, "Double jump", $"second apex {m.DoubleJump.ApexHeight:F2} m &middot; total airtime {m.DoubleJump.AirtimeTicks / 60f:F2} s");
            Add(sb, "Air drift cap", $"{m.Jump.DriftSpeedMax:F1} m/s <span class=\"note\">(authored {m.Authored.AirSpeedMax:F1})</span> = {Pct(m.Jump.DriftSpeedMax / m.Run.MaxSpeed)} of run");
            Add(sb, "Fall (50 m drop)", $"max {m.Fall.MaxFallSpeed:F0} m/s <span class=\"note\">(authored {m.Authored.MaxFallSpeed:F0})</span> &middot; "
                + $"time-to-max {m.Fall.TimeToMaxFallTicks / 60f:F2} s &middot; descent {m.Fall.DescentTicks / 60f:F2} s");
            Add(sb, "Fast fall", $"{m.Fall.FastFallSpeed:F0} m/s <span class=\"note\">(authored {m.Authored.FastFallSpeed:F0})</span> &middot; "
                + $"from jump apex {m.Fall.FastFallFromJumpTicks / 60f:F2} s (natural full-hop fall ~{(m.Jump.AirtimeTicks - m.Jump.TimeToApexTicks) / 60f:F2} s)");
            Add(sb, "Reversal", $"{m.Reversal.ReversalTicks / 60f:F2} s &middot; {m.Reversal.Displacement:F2} m covered (pivot skid + re-accel; no rush flip on 180°)");
            Add(sb, "Stop", $"{m.Stop.StopTicks / 60f:F2} s &middot; {m.Stop.StopDistance:F2} m");
            sb.AppendLine("</table>");

            sb.AppendLine("<div class=\"curves\">");
            Chart(sb, "Run — speed vs tick", ToPts(m.Run.Curve, c => c.Speed), null);
            Chart(sb, "Shield — speed vs tick", ToPts(m.Shield.Curve, c => c.Speed), null);
            Chart(sb, "Jump — height vs tick", ToPts(m.Jump.Curve, c => c.PosY - groundY), null);
            Chart(sb, "Short hop — height vs tick", ToPts(m.ShortHop.Curve, c => c.PosY - groundY), null);
            Chart(sb, "Double jump — height vs tick", ToPts(m.DoubleJump.Curve, c => c.PosY - groundY), null);
            Chart(sb, "Fall — fall speed vs tick (natural / fast fall)", ToPts(m.Fall.NaturalCurve, c => c.Vy), ToPts(m.Fall.FastFallCurve, c => c.Vy));
            Chart(sb, "Reversal — speed vs tick (skid + re-accel)", ToPts(m.Reversal.Curve, c => c.Speed), null);
            Chart(sb, "Stop — speed vs tick", ToPts(m.Stop.Curve, c => c.Speed), null);
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><td>{Escape(label)}</td><td>{value}</td></tr>");

    private static (int Tick, float V)[] ToPts(MovementProbe.MovementSample[] curve, Func<MovementProbe.MovementSample, float> v)
    {
        var pts = new (int, float)[curve.Length];
        for (int i = 0; i < curve.Length; i++) pts[i] = (curve[i].Tick, v(curve[i]));
        return pts;
    }

    private static void Chart(StringBuilder sb, string title, (int Tick, float V)[] a, (int Tick, float V)[]? b)
    {
        const int w = 300, h = 130;
        int xMax = 1; float yMin = 0f, yMax = 1f;
        void Fold((int Tick, float V)[] pts)
        {
            foreach (var p in pts)
            {
                if (p.Tick > xMax) xMax = p.Tick;
                if (p.V < yMin) yMin = p.V;
                if (p.V > yMax) yMax = p.V;
            }
        }
        Fold(a); if (b != null) Fold(b);
        if (yMax - yMin < 0.5f) yMax = yMin + 0.5f;
        float Y(float v) => h - 14f - ((v - yMin) / (yMax - yMin)) * (h - 24f);

        sb.AppendLine($"<div class=\"curve\"><h3>{title}</h3><svg viewBox=\"0 0 {w} {h}\">");
        sb.AppendLine($"<line x1=\"8\" y1=\"{Y(0f)}\" x2=\"{w - 8}\" y2=\"{Y(0f)}\"/><line x1=\"8\" y1=\"12\" x2=\"8\" y2=\"{h - 12}\"/>");
        sb.AppendLine($"<text x=\"6\" y=\"{Y(yMax) - 3}\" text-anchor=\"end\">{yMax:F1}</text>");
        sb.AppendLine($"<text x=\"6\" y=\"{Y(yMin) + 10}\" text-anchor=\"end\">{yMin:F1}</text>");
        sb.AppendLine($"<text x=\"{w - 10}\" y=\"{h - 3}\" text-anchor=\"end\">{xMax} ticks</text>");
        Polyline(sb, a, "#58a6ff");
        if (b != null) Polyline(sb, b, "#f0883e");
        sb.AppendLine("</svg></div>");

        void Polyline(StringBuilder sb, (int Tick, float V)[] pts, string color)
        {
            if (pts.Length == 0) return;
            var path = new StringBuilder();
            for (int i = 0; i < pts.Length; i++)
            {
                float x = 8f + (pts[i].Tick / (float)xMax) * (w - 16f);
                float y = h - 14f - ((pts[i].V - yMin) / (yMax - yMin)) * (h - 24f);
                path.Append(i == 0 ? $"M{x:F1} {y:F1}" : $" L{x:F1} {y:F1}");
            }
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>");
        }
    }

    // ── Comparison rows ───────────────────────────────────────────────────────

    private sealed record Row(string Label, string[] Values);

    private static List<Row> Rows(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all, float stageWidth)
    {
        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        static string F0(float v) => v.ToString("F0", CultureInfo.InvariantCulture);
        static string Note(MovementProbe.CharacterMovement m, float authored) =>
            $"<span class=\"note\">({F0(authored)})</span>";

        var rows = new List<Row>();
        void Row_(string label, Func<(CharacterDefinition Def, MovementProbe.CharacterMovement M), string> f) =>
            rows.Add(new Row(label, all.Select(f).ToArray()));

        Row_("Run max speed (m/s)", x => $"{F1(x.M.Run.MaxSpeed)} {Note(x.M, x.M.Authored.RunSpeed)}");
        Row_("Run time-to-max", x => x.M.Run.TimeToMaxTicks <= 2 ? "instant <span class=\"note\">(rush kick-off)</span>"
            : $"{x.M.Run.TimeToMaxTicks + 1} ticks");
        Row_("Run cross-stage time (s)", x => $"{stageWidth / x.M.Run.MaxSpeed:F2}");
        Row_("Shield stop ticks", x => $"{x.M.Shield.StopTicks}");
        Row_("Shield hold ticks", x => $"{x.M.Shield.HoldTicks}");
        Row_("Jump squat (ticks)", x => $"{x.M.Authored.JumpSquatTicks}");
        Row_("Rush redirect window (ticks)", x => $"{x.M.Authored.RushTicks} <span class=\"note\">(rush, on standstill / redirect)</span>");
        Row_("Jump apex (m)", x => F(x.M.Jump.ApexHeight));
        Row_("Jump time-to-apex (ticks)", x => $"{x.M.Jump.TimeToApexTicks}");
        Row_("Jump airtime (s)", x => $"{x.M.Jump.AirtimeTicks / 60f:F2}");
        Row_("Full-hop drift (m)", x => F(x.M.Jump.HorizontalDistance));
        Row_("Full hop % of stage", x => $"{Pct(x.M.Jump.HorizontalDistance / stageWidth)}");
        Row_("Running jump distance (m)", x => F(x.M.RunningJump.HorizontalDistance));
        Row_("Short hop apex (m)", x => x.M.ShortHop.ApexHeight <= 0.01f
            ? "0.00 <span class=\"note\">(BROKEN — land-tolerance snap: 6 m/s impulse rises 0.09 m/tick &lt; 0.10 m PlatformLandTolerance, sim snaps it back; short hop never leaves the ground)</span>"
            : F(x.M.ShortHop.ApexHeight));
        Row_("Short hop airtime (s)", x => x.M.ShortHop.ApexHeight <= 0.01f
            ? "—"
            : $"{x.M.ShortHop.AirtimeTicks / 60f:F2}");
        Row_("Double-jump apex (m)", x => F(x.M.DoubleJump.ApexHeight));
        Row_("Double-jump airtime (s)", x => $"{x.M.DoubleJump.AirtimeTicks / 60f:F2}");
        Row_("Air drift cap (m/s)", x => $"{F1(x.M.Jump.DriftSpeedMax)} {Note(x.M, x.M.Authored.AirSpeedMax)}");
        Row_("Air / run speed", x => $"{Pct(x.M.Jump.DriftSpeedMax / x.M.Run.MaxSpeed)}");
        Row_("Fall max speed (m/s)", x => $"{F0(x.M.Fall.MaxFallSpeed)} {Note(x.M, x.M.Authored.MaxFallSpeed)}");
        Row_("Fast fall (m/s)", x => $"{F0(x.M.Fall.FastFallSpeed)} {Note(x.M, x.M.Authored.FastFallSpeed)}");
        Row_("Fast fall from jump apex (s)", x => $"{x.M.Fall.FastFallFromJumpTicks / 60f:F2}");
        Row_("Reversal time (s)", x => $"{x.M.Reversal.ReversalTicks / 60f:F2}");
        Row_("Reversal distance (m)", x => F(x.M.Reversal.Displacement));
        Row_("Stop time (s)", x => $"{x.M.Stop.StopTicks / 60f:F2}");
        Row_("Stop distance (m)", x => F(x.M.Stop.StopDistance));
        return rows;
    }

    private static string Escape(string s) => WebUtility.HtmlEncode(s);
}
