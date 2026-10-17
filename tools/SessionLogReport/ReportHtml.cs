using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace SlopArena.SessionLogReport;

/// <summary>Self-contained HTML rendering of a <see cref="ReportModel"/> (no external assets).</summary>
internal static class ReportHtml
{
    private static readonly string[] Directions = { "short", "past", "under", "over", "left", "right" };
    private static readonly string[] Axes = { "depth", "lateral", "vertical" };

    internal static string Render(ReportModel model, string generatedUtc)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>SLOPArena practice session report</title><style>").Append(Css).Append("</style></head><body>");
        html.Append("<h1>Practice session report</h1>");
        html.Append(P($"{model.Sessions.Count} session(s), {model.Sessions.Sum(x => x.Minutes):F1} min of play. Generated {E(generatedUtc)}."));
        html.Append(P("Swings are the player's accepted attacks with a target selected or within 12 m. "
            + "A whiff is a swing whose hitboxes never touched the target; its gap is the closest the hitbox surface came "
            + "to the target's body. Directions are from the attacker: <b>short</b> = target beyond the hitbox, "
            + "<b>past</b> = target nearer than or behind it, <b>under</b> = target above it (the attack went under), "
            + "<b>over</b> = target below. Camera axes split the same gap into screen <b>depth</b>, <b>lateral</b> and "
            + "<b>vertical</b>, using the camera at the moment of the press."));
        foreach (var note in model.Notes) html.Append("<p class=\"note\">").Append(E(note)).Append("</p>");

        Section(html, "Sessions");
        Table(html, new[] { "File", "Started (UTC)", "Mode", "Arena", "Player", "Opponents", "Minutes", "Controller time", "Swings", "Complete" },
            model.Sessions.Select(s => new[]
            {
                E(s.File), E(s.Started), E(s.Mode), E(s.Arena), E(s.Player), E(s.Opponents), F(s.Minutes, 1),
                Pct(s.GamepadShare), s.Swings.ToString(CultureInfo.InvariantCulture), s.Truncated ? "no" : "yes",
            }));

        Section(html, "Overall and by device");
        OutcomeTable(html, new[] { model.Overall }.Concat(model.ByDevice).Concat(model.AttackerHeight));

        Section(html, "By move");
        OutcomeTable(html, model.Moves);

        Section(html, "Whiff direction");
        html.Append(P("Share of whiffs per dominant direction. Many <b>short</b> or <b>past</b> misses point at distance "
            + "(depth) misjudgment; <b>under</b>/<b>over</b> at height/timing; left/right at aim."));
        DirectionTable(html, new[] { model.Overall }.Concat(model.ByDevice).Concat(model.Moves).Where(x => x.Whiffs > 0),
            Directions, row => row.MissDirections);
        DirectionTable(html, new[] { model.Overall }.Concat(model.ByDevice).Where(x => x.Whiffs > 0),
            Axes, row => row.CameraAxes);

        Section(html, "Target motion");
        html.Append(P("What the target was doing at the closest approach. Lower contact against rising/falling targets "
            + "points at reading jump arcs."));
        OutcomeTable(html, model.TargetMotion);

        Section(html, "Camera angle to the target");
        html.Append(P("Angle between the camera's view and the attacker→target line at the press. Near 0° the target is "
            + "straight into the screen, where distance is hardest to see."));
        Table(html, new[] { "Angle", "Measured", "Contact rate", "Whiffs", "Depth-axis whiffs" },
            model.CameraAngles.Select(a => new[]
            {
                E(a.Label), a.Measured.ToString(CultureInfo.InvariantCulture), Bar(a.ContactRate, a.Measured),
                a.Whiffs.ToString(CultureInfo.InvariantCulture),
                a.Whiffs == 0 ? "–" : Pct(a.DepthMisses / (double)a.Whiffs),
            }));

        Section(html, "Camera handling");
        html.Append(P("Opponent visibility uses the watched fighter (your target, or the nearest one) against the rendered "
            + "camera. Saturated = right stick at ≥95% deflection; \"while lost\" = the opponent was off-screen or at the "
            + "screen edge during those frames. Hits taken from off-screen count damaging hits whose attacker was "
            + "off-screen when it pressed."));
        Table(html, new[] { "Device", "Minutes", "Opponent off-screen", "At screen edge", "Mouse °/s", "Stick °/s",
            "Stick saturated", "Saturated while lost", "Hits taken", "From off-screen" },
            model.Devices.Select(d => new[]
            {
                E(d.Device), F(d.Minutes, 1), Pct(d.OffscreenShare), Pct(d.EdgeShare), F(d.MouseDegreesPerSecond, 0),
                F(d.StickDegreesPerSecond, 0), Pct(d.StickSaturatedShare), Pct(d.SaturatedWhileTargetLostShare),
                d.HitsTaken.ToString(CultureInfo.InvariantCulture),
                d.HitsTaken == 0 ? "–" : $"{d.HitsTakenFromOffscreen} ({Pct(d.HitsTakenFromOffscreen / (double)d.HitsTaken)})",
            }));

        Section(html, "Where whiffs missed");
        html.Append(P("Each dot is one whiff's closest gap, in the attacker's frame (metres). Orange = controller, "
            + "blue = keyboard and mouse; hollow = aerial."));
        html.Append("<div class=\"plots\">");
        Scatter(html, "Top view (side → , forward ↑)", model.MissPoints, p => p.Side, p => p.Forward);
        Scatter(html, "Side view (forward → , up ↑)", model.MissPoints, p => p.Forward, p => p.Up);
        html.Append("</div>");
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void OutcomeTable(StringBuilder html, IEnumerable<OutcomeRow> rows)
        => Table(html, new[] { "", "Presses", "Accepted", "Hits", "Blocked", "Whiffs", "Contact rate", "Median whiff gap",
            $"Near misses (≤ {SessionAnalysis.NearMissMeters:0.0} m)", "No target", "No hitbox" },
            rows.Select(r => new[]
            {
                E(r.Label), N(r.Presses), N(r.Accepted), N(r.Hits), N(r.Blocked), N(r.Whiffs),
                Bar(r.ContactRate, r.Measured),
                r.MedianWhiffGap is double gap ? F(gap, 2) + " m" : "–",
                r.Whiffs == 0 ? "–" : $"{r.NearMisses} ({Pct(r.NearMisses / (double)r.Whiffs)})",
                N(r.NoTarget), N(r.NoHitbox),
            }));

    private static void DirectionTable(StringBuilder html, IEnumerable<OutcomeRow> rows, string[] keys,
        Func<OutcomeRow, IReadOnlyDictionary<string, int>> counts)
    {
        var list = rows.ToList();
        if (list.Count == 0) { html.Append(P("No whiffs recorded.")); return; }
        Table(html, new[] { "" }.Concat(keys).ToArray(), list.Select(row =>
        {
            var values = counts(row);
            int total = values.Values.Sum();
            return new[] { E(row.Label) }.Concat(keys.Select(key =>
                total == 0 || !values.TryGetValue(key, out int count) ? "–" : Pct(count / (double)total))).ToArray();
        }));
    }

    private static void Scatter(StringBuilder html, string title, List<MissPoint> points,
        Func<MissPoint, float> x, Func<MissPoint, float> y)
    {
        const float Size = 300f, Range = 3f;
        float Map(float v) => Math.Clamp((v + Range) / (2 * Range), 0f, 1f) * Size;
        html.Append("<figure><figcaption>").Append(E(title)).Append("</figcaption>")
            .Append(FormattableString.Invariant($"<svg viewBox=\"0 0 {Size} {Size}\" width=\"{Size}\" height=\"{Size}\" role=\"img\">"))
            .Append("<rect width=\"100%\" height=\"100%\" class=\"plot-bg\"/>");
        for (int m = -2; m <= 2; m++)
        {
            float g = Map(m);
            html.Append(FormattableString.Invariant(
                $"<line x1=\"{g:F1}\" y1=\"0\" x2=\"{g:F1}\" y2=\"{Size}\" class=\"{(m == 0 ? "axis" : "grid")}\"/>"));
            html.Append(FormattableString.Invariant(
                $"<line x1=\"0\" y1=\"{g:F1}\" x2=\"{Size}\" y2=\"{g:F1}\" class=\"{(m == 0 ? "axis" : "grid")}\"/>"));
        }
        foreach (var point in points.Take(4000))
        {
            string css = (point.Gamepad ? "pad" : "kbm") + (point.Air ? " air" : "");
            html.Append(FormattableString.Invariant(
                $"<circle cx=\"{Map(x(point)):F1}\" cy=\"{Size - Map(y(point)):F1}\" r=\"3\" class=\"{css}\"/>"));
        }
        html.Append("</svg><div class=\"legend\">±3 m, grid every metre</div></figure>");
    }

    private static void Table(StringBuilder html, string[] headers, IEnumerable<string[]> rows)
    {
        html.Append("<div class=\"scroll\"><table><thead><tr>");
        foreach (var header in headers) html.Append("<th>").Append(header).Append("</th>");
        html.Append("</tr></thead><tbody>");
        bool any = false;
        foreach (var row in rows)
        {
            any = true;
            html.Append("<tr>");
            foreach (var cell in row) html.Append("<td>").Append(cell).Append("</td>");
            html.Append("</tr>");
        }
        if (!any) html.Append($"<tr><td colspan=\"{headers.Length}\">No data.</td></tr>");
        html.Append("</tbody></table></div>");
    }

    private static string Bar(double rate, int measured)
        => measured == 0 ? "–" : FormattableString.Invariant(
            $"<span class=\"bar\"><span style=\"width:{rate * 100:F0}%\"></span></span> {rate * 100:F0}% <span class=\"muted\">of {measured}</span>");

    private static void Section(StringBuilder html, string title) => html.Append("<h2>").Append(E(title)).Append("</h2>");
    private static string P(string text) => "<p>" + text + "</p>";
    private static string E(string text) => WebUtility.HtmlEncode(text ?? "");
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string F(double value, int decimals) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    private static string Pct(double share) => (share * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";

    private const string Css = """
        body{font:15px/1.45 system-ui,sans-serif;margin:24px auto;max-width:1100px;padding:0 16px;color:#222;background:#fbfaf7}
        h1{font-size:24px;margin:0 0 8px}h2{font-size:18px;margin:28px 0 8px;border-bottom:1px solid #ddd;padding-bottom:4px}
        p{max-width:860px}.note{background:#fff4d6;padding:8px 12px;border-radius:6px}
        .scroll{overflow-x:auto}table{border-collapse:collapse;margin:8px 0;min-width:520px}
        th,td{padding:5px 9px;border-bottom:1px solid #e6e3dc;text-align:left;white-space:nowrap}
        th{font-weight:600;background:#f1efe9}.muted{color:#888;font-size:12px}
        .bar{display:inline-block;width:70px;height:9px;background:#e6e3dc;border-radius:5px;vertical-align:middle;overflow:hidden}
        .bar span{display:block;height:100%;background:#3d7c5a}
        .plots{display:flex;flex-wrap:wrap;gap:24px}figure{margin:0}figcaption{font-weight:600;margin-bottom:4px}
        .plot-bg{fill:#fff}.grid{stroke:#eee}.axis{stroke:#bbb}.legend{color:#888;font-size:12px}
        circle.kbm{fill:#2f6fb0;fill-opacity:.55}circle.pad{fill:#d9822b;fill-opacity:.6}
        circle.air{fill-opacity:0;stroke-width:1.4}circle.kbm.air{stroke:#2f6fb0}circle.pad.air{stroke:#d9822b}
        """;
}
