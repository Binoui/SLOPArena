using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;
using SlopArena.Shared.AI;

// Issue #149: the tuning A/B diff tool (SlopArena.AbDiffReport) reuses this tool's self-play
// aggregation (Aggregate / SampleEnvelope / whiff accumulation) without duplicating it.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SlopArena.AbDiffReport")]

namespace SlopArena.SelfPlayReport;

/// <summary>
/// Self-play telemetry (issue #148): runs N deterministic bot-vs-bot matches on the real
/// ServerSimulation (Shared/AI), aggregates hit/whiff/combo/damage stats, samples each
/// normal's deterministic reach envelope (threat zone, entity-relative, from real hitboxes),
/// and accumulates character-relative whiff spots. Emits lossless JSON (gitignored) + a
/// self-contained HTML visual report + a markdown summary.
///
/// Usage: dotnet run --project tools/SelfPlayReport -- [--matches N] [--seed S]
///        [--char fightguy|wibou] [--difficulty Easy|Normal|Hard]
///        [--json report.json] [--html report.html] [--out report.md]
/// </summary>
internal static class Program
{
    internal const int GridCell = 25; // 0.25 m per cell (in cm to stay int)
    // Forward axis includes a BEHIND-the-attacker band (negative forward) — whiffs cluster at
    // point-blank (-1..+0.25 m), so forward 0 must sit inside the view with left space, not at
    // the drawing's left edge. Range is narrow (not -4..16) so the melee data fills the graph.
    internal const float GridFwdMin = -2f, GridFwdMax = 6f;    // forward axis (facing-frame +Z)
    internal const float GridHtMin = -1f, GridHtMax = 8f;      // height axis
    internal const int GridFwdCells = (int)((GridFwdMax - GridFwdMin) * 4) + 1; // 33
    internal const int GridHtCells = (int)((GridHtMax - GridHtMin) * 4) + 1;    // 37

    // ── Report model ───────────────────────────────────────────────────────
    internal sealed record EnvDisc(float RelZ, float RelY, float Radius);
    internal sealed record MoveEnvelope(string Label, string Ability, int Slot, bool Air, float Reach, EnvDisc[] Discs);
    internal sealed record MoveStats(string Label, string Ability, int Swings, int Hits, int Whiffs, float Damage);
    internal sealed record WhiffCell(int Gx, int Gy, int Count);
    internal sealed record ReportData(string Character, string Difficulty, string GeneratedAt, int Matches, int Seed,
        int AvgDurationTicks, int MaxDurationTicks,
        float HitRate, float WhiffRate, float AvgComboLen, int MaxComboLen,
        int TrueComboCount, int PressureStringCount,
        float DamagePerMatch, float? DamagePerStock,
        int TotalDeaths, int CreditedDeaths, int UncreditedDeaths,
        int WinsA, int WinsB, int Draws,
        MoveStats[] PerMove, MoveEnvelope[] Envelope, WhiffCell[] WhiffGrid,
        float EnvelopeMaxReach, float EnvelopeMaxHeight, float[] Silhouette,
        int TotalSwings, int TotalHits, int TotalWhiffs, float TotalDamage,
        int TotalActionAttempts, int TotalAcceptedActions, int TotalConfirmedHits);

    internal static readonly byte[] SlotBytes = { AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4 };

    internal static int Main(string[] args)
    {
        int matches = ParseInt(args, "--matches", 20);
        int seed = ParseInt(args, "--seed", 20260817);
        string charName = ParseArg(args, "--char") ?? "fightguy";
        string difficultyName = ParseArg(args, "--difficulty") ?? "normal";
        CpuDifficulty difficulty = difficultyName.ToLowerInvariant() switch
        {
            "easy" => CpuDifficulty.Easy,
            "normal" => CpuDifficulty.Normal,
            "hard" => CpuDifficulty.Hard,
            _ => throw new ArgumentException($"Unknown CPU difficulty '{difficultyName}'. Use Easy, Normal, or Hard."),
        };
        string? jsonPath = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        string? outPath = ParseArg(args, "--out") ?? $"docs/generated/{charName}-selfplay.md";

        CharacterClass cls = charName.ToLowerInvariant() switch
        {
            "fightguy" or "fg" => CharacterClass.FightGuy,
            "wibou" => CharacterClass.Wibou,
            "manki" => CharacterClass.Manki,
            "bonk" => CharacterClass.Bonk,
            _ => CharacterClass.None,
        };
        if (cls == CharacterClass.None)
        {
            Console.Error.WriteLine($"Unknown character '{charName}'.");
            return 1;
        }
        var entry = BuiltInContentResolver.Resolve(cls);
        var def = entry.Definition;
        if (def.Class == CharacterClass.None)
        {
            Console.Error.WriteLine($"Unable to resolve character '{charName}'.");
            return 1;
        }
        Console.WriteLine($"Resolved character: {def.DisplayName} ({def.Class})");
        Console.WriteLine($"Difficulty: {BotDifficultyProfile.DisplayName(difficulty)}  Seed: {seed}");

        var arena = BuildKillArena();
        var baked = entry.BakedAnimation;

        var records = new List<MatchRecord>(matches);
        for (int m = 0; m < matches; m++)
        {
            var rec = SelfPlayMatch.Run(def, arena, seed + m, baked, difficulty: difficulty);
            records.Add(rec);
        }

        var report = Aggregate(entry, records, seed, difficulty);

        Console.WriteLine(BuildMarkdown(report));

        if (jsonPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        if (htmlPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(htmlPath))!);
            File.WriteAllText(htmlPath, BuildHtml(report));
        }
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, BuildMarkdown(report));
        }
        return 0;
    }

    // ── Aggregation ─────────────────────────────────────────────────────────

    internal static ReportData Aggregate(MatchContentEntry entry, List<MatchRecord> records, int seed,
        CpuDifficulty difficulty = CpuDifficulty.Normal)
    {
        var def = entry.Definition;
        var perMove = new Dictionary<string, MoveStats>();
        string Key(bool air, byte slotByte) => $"{(air ? "a" : "g")}{SlotOf(slotByte)}";
        int totalSwings = 0, totalHits = 0, totalWhiffs = 0;
        float totalDamage = 0f;
        int totalActionAttempts = 0, totalAcceptedActions = 0, totalConfirmedHits = 0;
        int totalDeaths = 0, creditedDeaths = 0, uncreditedDeaths = 0;
        int totalComboLen = 0, comboCount = 0, maxComboLen = 0;
        int trueComboCount = 0, pressureStringCount = 0;
        int winsA = 0, winsB = 0, draws = 0;
        long durationSum = 0; int maxDuration = 0;

        foreach (var r in records)
        {
            durationSum += r.DurationTicks;
            maxDuration = Math.Max(maxDuration, r.DurationTicks);
            if (r.TimedOut) draws++;
            else if (r.WinnerEntityId == SelfPlayMatch.EntityA) winsA++;
            else if (r.WinnerEntityId == SelfPlayMatch.EntityB) winsB++;
            else draws++;
            totalActionAttempts += r.ActionAttempts.Count;
            totalAcceptedActions += r.AcceptedActions.Count;
            totalConfirmedHits += r.Hits.Count;
            totalDeaths += r.Deaths.Count;
            creditedDeaths += r.Deaths.Count(d => d.KillerEntityId != 0);
            uncreditedDeaths += r.Deaths.Count(d => d.KillerEntityId == 0);
            foreach (var hit in r.Hits)
                totalDamage += hit.Damage;

            foreach (var sw in r.Swings)
            {
                if (!sw.Accepted) continue;
                string k = Key(sw.Air, sw.ActiveSlot);
                totalSwings++;
                if (sw.Connected) totalHits++; else totalWhiffs++;
                if (!perMove.TryGetValue(k, out var ms))
                    ms = new MoveStats(k, AbilityName(def, sw.ActiveSlot, sw.Air), 0, 0, 0, 0);
                perMove[k] = ms with
                {
                    Swings = ms.Swings + 1,
                    Hits = ms.Hits + (sw.Connected ? 1 : 0),
                    Whiffs = ms.Whiffs + (sw.Connected ? 0 : 1),
                };
            }
            foreach (var hit in r.Hits)
            {
                float damage = hit.Damage;
                string k = Key(hit.Air, hit.AttackSlot);
                if (perMove.TryGetValue(k, out var ms))
                    perMove[k] = ms with { Damage = ms.Damage + damage };
            }
            foreach (var c in r.Combos)
            {
                totalComboLen += c.Hits;
                comboCount++;
                maxComboLen = Math.Max(maxComboLen, c.Hits);
                if (c.IsTrueCombo) trueComboCount++;
                if (c.IsPressureString) pressureStringCount++;
            }
        }

        int matchCount = records.Count;
        float hitRate = totalSwings > 0 ? totalHits / (float)totalSwings : 0f;
        float whiffRate = totalSwings > 0 ? totalWhiffs / (float)totalSwings : 0f;
        float avgCombo = comboCount > 0 ? totalComboLen / (float)comboCount : 0f;
        float dmgPerMatch = matchCount > 0 ? totalDamage / matchCount : 0f;
        float? dmgPerStock = totalDeaths > 0 ? totalDamage / totalDeaths : null;

        // Damage is the sum of authoritative resolved HitEvent values. It persists across
        // respawns; final CharacterState damage is only the current stock's percent.
        var envelope = SampleEnvelope(entry);
        var (whiffGrid, silhouette) = AccumulateWhiffs(records, envelope);
        var perMoveArray = perMove.Values.OrderBy(m => m.Label, StringComparer.Ordinal).ToArray();
        float maxReach = envelope.Length > 0 ? envelope.Max(e => e.Reach) : 0f;
        float maxHeight = envelope.Length > 0 ? envelope.Max(e => e.Discs.Length > 0 ? e.Discs.Max(d => d.RelY + d.Radius) : 0f) : 0f;

        return new ReportData(
            def.DisplayName, BotDifficultyProfile.DisplayName(difficulty),
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
            matchCount, seed, (int)(matchCount > 0 ? durationSum / matchCount : 0), maxDuration,
            hitRate, whiffRate, avgCombo, maxComboLen, trueComboCount, pressureStringCount,
            dmgPerMatch, dmgPerStock,
            totalDeaths, creditedDeaths, uncreditedDeaths,
            winsA, winsB, draws, perMoveArray, envelope, whiffGrid,
            maxReach, maxHeight, silhouette,
            totalSwings, totalHits, totalWhiffs, totalDamage,
            totalActionAttempts, totalAcceptedActions, totalConfirmedHits);
    }

    internal static string AbilityName(CharacterDefinition def, byte activeSlot, bool air)
        => def.GetCookedSlotAbility(activeSlot, air)?.Name ?? "-";

    internal static string SlotOf(byte activeSlot) => CanonicalSlotProjection.TryGet(activeSlot, false, out var address) ? address.InputLabel : "?";

    // ── Reach envelope (deterministic threat zone, sampled from real hitboxes) ──

    /// <summary>Sample each normal's active hitboxes from a real sim run (attacker at origin,
    /// facing +Z, no opponent) so the envelope captures lunge, bones, capsules — the actual
    /// threat zone the game produces. Facing +Z ⇒ world == facing frame.</summary>
    internal static MoveEnvelope[] SampleEnvelope(MatchContentEntry entry)
    {
        var def = entry.Definition;
        var result = new List<MoveEnvelope>();
        var baked = entry.BakedAnimation;
        foreach (bool air in new[] { false, true })
        {
            for (int i = 0; i < SlotBytes.Length; i++)
            {
                byte slot = SlotBytes[i];
                var spec = def.GetCookedSlotAbility(slot, air);
                if (spec == null || spec.Timeline.Stages == null || spec.Timeline.Stages.Count == 0) continue;

                var sim = new ServerSimulation(BuildKillArena());
                float gpy = def.CapsuleHeight * 0.5f;
                var atk = new CharacterState
                {
                    EntityId = 1, PX = 0f, PY = air ? gpy + 1.2f : gpy, PZ = 0f,
                    State = ActionState.Idle, IsGrounded = !air,
                    JumpsLeft = 2, AirDodgesLeft = 1, FacingYaw = 0f,
                    MatchState = MatchState.Playing,
                };
                sim.RegisterEntity(1, def, atk, baked);
                var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slot } };

                var discs = new List<EnvDisc>();
                for (int t = 0; t < 160; t++)
                {
                    sim.Tick(inputs);
                    inputs[1] = default;
                    foreach (var hb in sim.Resolver.GetActiveHitboxes())
                    {
                        if (hb.OwnerId != 1) continue;
                        float endZ = hb.EndX * 0f + hb.Z; // sphere: center; capsule: treat start as representative
                        discs.Add(new EnvDisc(hb.Z, hb.Y, hb.Radius));
                        _ = endZ;
                    }
                }
                float reach = discs.Count > 0 ? discs.Max(d => d.RelZ + d.Radius) : 0f;
                result.Add(new MoveEnvelope($"{(air ? "a" : "g")}{i + 1}", spec.Name, i + 1, air, reach, discs.ToArray()));
            }
        }
        return result.ToArray();
    }

    /// <summary>Accumulate whiff swings onto a side-view (forward × height) grid, normalized to the
    /// attacker's facing frame. Also returns the threat-zone silhouette (per height band, the max
    /// forward reach among the envelope discs) so "whiffed past reach" vs "inside reach" separate.</summary>
    internal static (WhiffCell[] Grid, float[] Silhouette) AccumulateWhiffs(List<MatchRecord> records, MoveEnvelope[] envelope)
    {
        var counts = new int[GridFwdCells * GridHtCells];
        foreach (var r in records)
            foreach (var sw in r.Swings)
            {
                if (!sw.Accepted || sw.Connected) continue;
                int gx = (int)((sw.RelForward - GridFwdMin) * 4);
                int gy = (int)((sw.RelHeight - GridHtMin) * 4);
                if (gx < 0 || gx >= GridFwdCells || gy < 0 || gy >= GridHtCells) continue;
                counts[gy * GridFwdCells + gx]++;
            }

        var cells = new List<WhiffCell>();
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0)
                cells.Add(new WhiffCell(i % GridFwdCells, i / GridFwdCells, counts[i]));

        // Silhouette: per height band, max(relZ + radius) among envelope discs.
        var sil = new float[GridHtCells];
        foreach (var e in envelope)
            foreach (var d in e.Discs)
            {
                int band = (int)((d.RelY - GridHtMin) * 4);
                if (band < 0 || band >= GridHtCells) continue;
                float fwd = d.RelZ + d.Radius;
                if (fwd > sil[band]) sil[band] = fwd;
            }
        return (cells.ToArray(), sil);
    }

    // ── Markdown ───────────────────────────────────────────────────────────

    internal static string BuildMarkdown(ReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {r.Character} — self-play telemetry");
        sb.AppendLine($"Generated {r.GeneratedAt} · {r.Matches} seeded {r.Difficulty} bot-vs-bot matches on the real ServerSimulation (issue #148). ");
        sb.AppendLine($"Seed {r.Seed} · avg {r.AvgDurationTicks / 60f:F1}s, max {r.MaxDurationTicks / 60f:F1}s · wins {r.WinsA}–{r.WinsB}, draws {r.Draws}.");
        sb.AppendLine($"- **Actions**: attempted inputs {r.TotalActionAttempts}, accepted actions {r.TotalAcceptedActions}, confirmed hits {r.TotalConfirmedHits}.");
        sb.AppendLine($"- **Hit rate** {r.HitRate * 100f:F1}% ({r.TotalHits}/{r.TotalSwings} swings) — **whiff rate** {r.WhiffRate * 100f:F1}% ({r.TotalWhiffs}/{r.TotalSwings}).");
        sb.AppendLine($"- **Combos**: avg length {r.AvgComboLen:F2}, max {r.MaxComboLen}; true combos {r.TrueComboCount}, pressure strings {r.PressureStringCount} (classified by defender action availability).");
        sb.AppendLine($"- **Damage**: {DamageMetric(r.TotalDamage)} cumulative, {DamageMetric(r.DamagePerMatch)} per match, {DamageMetric(r.DamagePerStock)} damage per stock lost.");
        sb.AppendLine($"- **Deaths**: {r.TotalDeaths} total ({r.CreditedDeaths} credited, {r.UncreditedDeaths} uncredited).");
        sb.AppendLine();
        sb.AppendLine("| move | swings | hits | whiffs | hit% |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var m in r.PerMove)
            sb.AppendLine($"| {m.Label} {m.Ability} | {m.Swings} | {m.Hits} | {m.Whiffs} | {(m.Swings > 0 ? m.Hits * 100f / m.Swings : 0):F0}% |");
        sb.AppendLine();
        sb.AppendLine("### Reach envelope (deterministic threat zone)");
        sb.AppendLine("Entity-relative forward reach sampled from real sim hitboxes (includes lunge):");
        sb.AppendLine();
        sb.AppendLine("| move | reach (m) |");
        sb.AppendLine("|---|---|");
        foreach (var e in r.Envelope)
            sb.AppendLine($"| {e.Label} {e.Ability} | {e.Reach:F2} |");
        sb.AppendLine();
        sb.AppendLine("### Whiff spots");
        sb.AppendLine($"Side-view (forward × height) grid, opponent position relative to the attacker at whiffed swings. " +
            $"Character's max reach is {r.EnvelopeMaxReach:F2} m; the silhouette (per-height max reach) overlays the heatmap in HTML. " +
            $"{r.TotalWhiffs} total whiffs. " +
            $"Whiffs inside the silhouette = timing/placement (skill); beyond it = spacing (out of reach).");
        return sb.ToString();
    }

    // ── HTML ───────────────────────────────────────────────────────────────

    internal static string BuildHtml(ReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(r.Character)} self-play telemetry</title><style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;color:#1c1e21;}h1{font-size:22px;}h2{margin-top:32px;border-bottom:1px solid #ddd;padding-bottom:4px;}.meta{color:#666;font-size:13px;margin-bottom:16px;max-width:880px;line-height:1.4;}");
        sb.AppendLine("table.heat{border-collapse:collapse;font-size:13px;}table.heat td,table.heat th{border:1px solid #ccc;padding:3px 8px;text-align:center;}table.heat td.move,table.heat th.move{text-align:left;white-space:nowrap;}");
        sb.AppendLine(".envs{display:flex;flex-wrap:wrap;gap:14px;margin:6px 0 20px;}figure{margin:0;text-align:center;font-size:11px;color:#555;}svg.env{border:1px solid #eee;background:#fafbfc;display:block;}.legend{font-size:12px;color:#444;margin:6px 0 16px;max-width:900px;line-height:1.5;}");
        sb.AppendLine(".heat-svg{border:1px solid #eee;background:#fafbfc;max-width:100%;height:auto;}</style></head><body>");
        sb.AppendLine($"<h1>{Escape(r.Character)} — self-play telemetry</h1>");
        sb.AppendLine($"<div class=\"meta\">Generated {Escape(r.GeneratedAt)} &middot; {r.Matches} seeded {Escape(r.Difficulty)} bot-vs-bot matches on the real ServerSimulation (issue #148). " +
            $"Seed {r.Seed} &middot; avg {r.AvgDurationTicks / 60f:F1}s, max {r.MaxDurationTicks / 60f:F1}s &middot; wins {r.WinsA}–{r.WinsB}, draws {r.Draws}. " +
            "Same seed &rarr; bit-identical match.</div>");

        // 1 — stats
        sb.AppendLine("<h2>Match stats</h2>");
        sb.AppendLine("<div class=\"legend\">The empirical skill-vs-game read: what actually connects under the current tuning. " +
            "Attempted inputs are recorded before simulation; accepted actions come from authoritative activation; " +
            "confirmed hits come from resolver outcomes. True combos have no ordinary defender action opportunity; " +
            "pressure strings contain an actionable gap.</div>");
        sb.AppendLine("<table class=\"heat\"><tbody>");
        sb.AppendLine($"<tr><td class=\"move\">actions</td><td>{r.TotalActionAttempts} attempted / {r.TotalAcceptedActions} accepted / {r.TotalConfirmedHits} confirmed hits</td><td class=\"move\">hit rate</td><td><b>{r.HitRate * 100f:F1}%</b></td></tr>");
        sb.AppendLine($"<tr><td class=\"move\">whiff rate</td><td><b>{r.WhiffRate * 100f:F1}%</b></td><td class=\"move\">combos</td><td>avg {r.AvgComboLen:F2}, max {r.MaxComboLen}; true {r.TrueComboCount}, pressure {r.PressureStringCount}</td></tr>");
        sb.AppendLine($"<tr><td class=\"move\">damage</td><td>{DamageMetric(r.TotalDamage)} cumulative, {DamageMetric(r.DamagePerMatch)}/match, {DamageMetric(r.DamagePerStock)} damage per stock lost</td><td class=\"move\">deaths</td><td>{r.TotalDeaths} total ({r.CreditedDeaths} credited, {r.UncreditedDeaths} uncredited)</td></tr>");
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">move</th><th>swings</th><th>hits</th><th>whiffs</th><th>hit%</th></tr></thead><tbody>");
        foreach (var m in r.PerMove)
            sb.AppendLine($"<tr><td class=\"move\">{Escape(m.Label)} {Escape(m.Ability)}</td><td>{m.Swings}</td><td>{m.Hits}</td><td>{m.Whiffs}</td><td>{(m.Swings > 0 ? m.Hits * 100f / m.Swings : 0):F0}%</td></tr>");
        sb.AppendLine("</tbody></table>");

        // 2 — reach envelope gallery
        sb.AppendLine("<h2>Reach envelope (threat zone)</h2>");
        sb.AppendLine($"<div class=\"legend\">Deterministic per-move threat zone, entity-relative side view (forward &rarr; +Z, height &uarr;), sampled from the <b>real sim hitboxes</b> (lunge, bones, capsules included). " +
            $"Common scale: forward {F(r.EnvelopeMaxReach)} m &times; height {F(r.EnvelopeMaxHeight)} m. Red line = forward reach. This is the kit's answer to “where is my threat zone”.</div>");
        sb.AppendLine("<div class=\"envs\">");
        foreach (var e in r.Envelope)
        {
            sb.AppendLine(EnvFigure(e, r));
        }
        sb.AppendLine("</div>");

        // 3 — whiff-spot heatmap
        sb.AppendLine("<h2>Whiff spots</h2>");
        sb.AppendLine("<div class=\"legend\">Opponent position <b>relative to the attacker</b> (normalized into the facing frame) at every swing that did <b>not</b> connect, accumulated across all matches — the empirical half the issue's world-space heatmap couldn't answer. " +
            "Cell color = whiff density. The <b>green silhouette</b> is the character's deterministic threat zone (max forward reach per height band): whiffs <b>inside</b> it are timing/placement (skill); whiffs <b>beyond</b> it are spacing (the opponent stood past your reach).</div>");
        sb.AppendLine(WhiffSvg(r));

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    internal static string EnvFigure(MoveEnvelope e, ReportData r)
    {
        const int W = 200, H = 150, PAD = 20;
        // Forward axis spans a BEHIND band (origin + negative-RelZ side hits) out to the max
        // reach, on a common scale so figures stay comparable. Previously the origin sat at the
        // left edge, clipping any hitbox offset behind the attacker (a3 High Kick's disc was
        // fully off-screen at cx=-22). Same for height (allow discs slightly below the origin).
        float fwdLo = -1f;
        float fwdHi = Math.Max(0.1f, r.EnvelopeMaxReach) + 0.3f;   // margin so a max-reach disc's right edge isn't clipped
        float hLo = -0.5f;
        float hHi = Math.Max(0.1f, r.EnvelopeMaxHeight) + 0.3f;    // margin so the tallest disc's top isn't clipped
        float sx = (W - 2 * PAD) / (fwdHi - fwdLo);
        float sy = (H - 2 * PAD) / (hHi - hLo);
        var sb = new StringBuilder();
        sb.Append($"<figure><div class=\"env-title\">{Escape(e.Label)} {Escape(e.Ability)} &middot; reach {F(e.Reach)}m</div>");
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"env\">");
        // forward axis baseline at height 0, origin tick at forward 0
        float y0 = H - PAD - (0f - hLo) * sy;
        sb.Append($"<line x1=\"{PAD}\" y1=\"{Fi(y0)}\" x2=\"{W - PAD}\" y2=\"{Fi(y0)}\" stroke=\"#999\" stroke-width=\"1\"/>");
        float x0 = PAD + (0f - fwdLo) * sx;
        sb.Append($"<line x1=\"{Fi(x0)}\" y1=\"{Fi(y0)}\" x2=\"{Fi(x0)}\" y2=\"{Fi(y0 + 5)}\" stroke=\"#999\" stroke-width=\"1\"/>");
        sb.Append($"<text x=\"{Fi(x0)}\" y=\"{Fi(y0 + 13)}\" font-size=\"8\" fill=\"#999\" text-anchor=\"middle\">0</text>");
        foreach (var d in e.Discs)
        {
            float cx = PAD + (d.RelZ - fwdLo) * sx;
            float cy = H - PAD - (d.RelY - hLo) * sy;
            float rad = Math.Max(1.5f, d.Radius * sx);
            sb.Append($"<circle cx=\"{Fi(cx)}\" cy=\"{Fi(cy)}\" r=\"{Fi(rad)}\" fill=\"#1a5fd0\" fill-opacity=\"0.28\" stroke=\"#1a5fd0\" stroke-opacity=\"0.5\" stroke-width=\"0.8\"/>");
        }
        float reachX = PAD + (e.Reach - fwdLo) * sx;
        sb.Append($"<line x1=\"{Fi(reachX)}\" y1=\"{PAD}\" x2=\"{Fi(reachX)}\" y2=\"{H - PAD}\" stroke=\"#d23\" stroke-width=\"1.4\" stroke-dasharray=\"3,3\"/>");
        sb.Append("</svg></figure>");
        return sb.ToString();
    }

    internal static string WhiffSvg(ReportData r)
    {
        const int W = 760, H = 420, PAD = 36;
        // Map the grid RANGE (not absolute forward/height) onto the drawing so forward 0 sits
        // inside the view with a BEHIND band to its left. Before, coordinates were absolute
        // (x = PAD + forward*sx): forward 0 pinned to the left edge, so behind-attacker whiffs
        // were clipped and the point-blank data shrank to a corner sliver.
        float sx = (W - 2 * PAD) / (GridFwdMax - GridFwdMin);
        float sy = (H - 2 * PAD) / (GridHtMax - GridHtMin);
        int maxCount = r.WhiffGrid.Length > 0 ? r.WhiffGrid.Max(c => c.Count) : 1;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"heat-svg\">");
        // axes + grid labels
        sb.Append($"<line x1=\"{PAD}\" y1=\"{H - PAD}\" x2=\"{W - PAD}\" y2=\"{H - PAD}\" stroke=\"#666\" stroke-width=\"1.2\"/>");
        sb.Append($"<text x=\"{W - PAD}\" y=\"{H - 4}\" font-size=\"10\" fill=\"#666\" text-anchor=\"end\">forward (m)</text>");
        sb.Append($"<text x=\"6\" y=\"{PAD - 8}\" font-size=\"10\" fill=\"#666\">height (m)</text>");
        for (float t = 0; t <= GridFwdMax; t += 2f)
        {
            float x = PAD + (t - GridFwdMin) * sx;
            sb.Append($"<line x1=\"{Fi(x)}\" y1=\"{PAD}\" x2=\"{Fi(x)}\" y2=\"{H - PAD}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{Fi(x)}\" y=\"{H - PAD + 14}\" font-size=\"10\" fill=\"#999\" text-anchor=\"middle\">{Fi(t)}</text>");
        }
        for (float h = 0; h <= GridHtMax; h += 1f)
        {
            float y = H - PAD - (h - GridHtMin) * sy;
            sb.Append($"<line x1=\"{PAD}\" y1=\"{Fi(y)}\" x2=\"{W - PAD}\" y2=\"{Fi(y)}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{PAD - 5}\" y=\"{Fi(y + 3)}\" font-size=\"10\" fill=\"#999\" text-anchor=\"end\">{Fi(h)}</text>");
        }
        // whiff density cells (red, opacity ∝ count) — coordinates relative to grid min
        foreach (var c in r.WhiffGrid)
        {
            float x = PAD + (c.Gx * 0.25f) * sx;
            float y = H - PAD - (c.Gy * 0.25f) * sy - 0.25f * sy;
            float w = 0.25f * sx, h = 0.25f * sy;
            float alpha = 0.08f + 0.82f * (c.Count / (float)maxCount);
            sb.Append($"<rect x=\"{Fi(x)}\" y=\"{Fi(y)}\" width=\"{Fi(w)}\" height=\"{Fi(h)}\" fill=\"#e74c3c\" fill-opacity=\"{alpha:0.00}\"/>");
        }
        // threat-zone silhouette (green, per-height max reach) — relative to grid min
        var pts = new List<string>();
        for (int gy = 0; gy < GridHtCells; gy++)
        {
            if (r.Silhouette[gy] <= 0f) continue;
            float x = PAD + (r.Silhouette[gy] - GridFwdMin) * sx;
            float y = H - PAD - ((gy + 0.5f) * 0.25f) * sy;
            pts.Add($"{Fi(x)},{Fi(y)}");
        }
        if (pts.Count > 1)
            sb.Append($"<polyline fill=\"none\" stroke=\"#2ecc71\" stroke-width=\"2\" points=\"{string.Join(" ", pts)}\"/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    // ── Arena ──────────────────────────────────────────────────────────────

    /// <summary>Crossroads-style 60×60 flat proxy (top +20, sides ±40, bottom −10) so KOs
    /// actually end matches. Deterministic, self-contained (no stage data dependency).</summary>
    internal static ArenaDefinition BuildKillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy (Crossroads-style 60x60)",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            // Origin at -30 so the 60×60 floor covers [-30,30] — matches the bounds. (The
            // move-data tool never cared: it spawns a single entity at the origin. Self-play
            // spawns at x=±12, which is OFF a floor that starts at x=0 — those bots fell through.)
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }


    // ── CLI helpers ────────────────────────────────────────────────────────

    internal static string? ParseArg(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    internal static int ParseInt(string[] args, string key, int fallback)
    {
        var s = ParseArg(args, key);
        return s != null && int.TryParse(s, out int v) ? v : fallback;
    }
    internal static string DamageMetric(float? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a";

    internal static string Escape(string s) => WebUtility.HtmlEncode(s);

    internal static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string Fi(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
