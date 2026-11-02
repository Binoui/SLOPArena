using System.Globalization;
using System.Text;
using SlopArena.Shared.Replays;

namespace SlopArena.ReplayReport;

public sealed record MoveKey(string Package, string Form, string Move);
public sealed class MoveCount(MoveKey key)
{
    public MoveKey Key { get; } = key;
    public int Activations { get; set; }
    public int Landed { get; set; }
    public int Blocked { get; set; }
    public double Damage { get; set; }
}
public sealed record Ko(string Match, uint Tick, string Victim, string CreditedFighter,
    string Package, string Form, string Move, ReplayVector Position, string Boundary);
public sealed record Ending(string Match, string Reason, double Seconds, string[] Participants, string[] Winners, bool Draw);
public sealed record OpenFailure(string File, string Reason);

public sealed class PatternReport
{
    public int Files { get; private set; }
    public double Minutes { get; private set; }
    public List<string> Builds { get; } = new();
    public List<uint> Protocols { get; } = new();
    public List<OpenFailure> Failures { get; } = new();
    public List<MoveCount> Moves { get; } = new();
    public List<Ko> KOs { get; } = new();
    public List<Ending> Endings { get; } = new();

    public static PatternReport Read(string directory, DateTime? since = null)
    {
        var report = new PatternReport();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".slopreplay", StringComparison.Ordinal) || p.EndsWith(".slopreplay.part", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            if (since != null && File.GetLastWriteTimeUtc(path) < since.Value) continue;
            report.Files++;
            try
            {
                using var input = File.OpenRead(path);
                var opened = ReplayReader.Open(input);
                if (opened.Failure != null) throw new InvalidDataException(opened.Failure.ToString());
                using var reader = opened.Reader!;
                // Keep each file transactional: invalid View data must not contribute half a match.
                var moves = new Dictionary<MoveKey, MoveCount>();
                var activations = new HashSet<(ulong, ulong)>();
                var kos = new List<Ko>();
                string Fighter(ulong id)
                {
                    var p = reader.Header.Roster.FirstOrDefault(p => p.EntityId == id);
                    return p == null ? "unknown" : p.ParticipantProvider + ":" + p.ParticipantSubject + " (" + p.Package.PackageId + ")";
                }
                MoveCount Move(string package, string form, string name)
                {
                    var key = new MoveKey(package, form, name);
                    if (!moves.TryGetValue(key, out var count)) moves[key] = count = new(key);
                    return count;
                }
                void Activation(ulong entity, ulong activation, string package, string form, string name)
                {
                    if (activation != 0 && name.Length > 0 && activations.Add((entity, activation)))
                        Move(package, form, name).Activations++;
                }
                double seconds = 0;
                if (reader.Range is ReplayRange range)
                {
                    seconds = ((double)range.Last - range.First + 1) / ReplayHeader.TickRate;
                    for (uint tick = range.First; ; tick++)
                    {
                        var read = reader.ReadView(tick);
                        if (read.Failure != null) throw new InvalidDataException(read.Failure.ToString());
                        var view = read.Tick!;
                        foreach (var f in view.Fighters)
                            Activation(f.EntityId, f.ActivationId, f.PackageId, f.MoveFormId, f.MoveName);
                        foreach (var shape in view.Shapes)
                            Activation(shape.OwnerId, shape.ActivationId, shape.PackageId, shape.FormId, shape.MoveName);
                        foreach (var hit in view.Hits.Where(h => h.MoveName.Length > 0))
                        {
                            Activation(hit.OwnerId, hit.ActivationId, hit.PackageId, hit.FormId, hit.MoveName);
                            var count = Move(hit.PackageId, hit.FormId, hit.MoveName);
                            if (hit.Blocked) count.Blocked++; else count.Landed++;
                            count.Damage += hit.AppliedDamage;
                        }
                        foreach (var death in view.Deaths)
                            kos.Add(new(reader.Header.MatchId, tick, Fighter(death.EntityId), Fighter(death.KillerId),
                                death.LastHitPackageId, death.LastHitFormId,
                                death.LastHitMoveName.Length == 0 ? "unknown" : death.LastHitMoveName,
                                death.Position, death.BoundaryId));
                        if (tick == range.Last) break;
                    }
                }
                report.Minutes += seconds / 60;
                if (!report.Builds.Contains(reader.Header.Build.Build)) report.Builds.Add(reader.Header.Build.Build);
                if (!report.Protocols.Contains(reader.Header.Build.Protocol)) report.Protocols.Add(reader.Header.Build.Protocol);
                foreach (var count in moves.Values)
                {
                    var total = report.Moves.FirstOrDefault(m => m.Key == count.Key);
                    if (total == null) report.Moves.Add(count);
                    else { total.Activations += count.Activations; total.Landed += count.Landed; total.Blocked += count.Blocked; total.Damage += count.Damage; }
                }
                report.KOs.AddRange(kos);
                var conclusion = reader.Conclusion;
                report.Endings.Add(new(reader.Header.MatchId, conclusion?.Reason.ToString().ToLowerInvariant() ?? "partial",
                    seconds, reader.Header.Roster.Select(p => Fighter(p.EntityId)).ToArray(),
                    conclusion?.Result?.WinnerIds.Select(Fighter).ToArray() ?? Array.Empty<string>(), conclusion?.Result?.Draw ?? false));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            { report.Failures.Add(new(path, e.Message)); }
        }
        return report;
    }

    static string E(object value) => (value.ToString() ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    public string Markdown()
    {
        var s = new StringBuilder();
        s.AppendLine("# Replay pattern report").AppendLine().AppendLine("## Files").AppendLine();
        s.AppendLine($"Files: {Files}; total minutes: {N(Minutes)}; failed: {Failures.Count}.");
        s.AppendLine($"Builds: {E(string.Join(", ", Builds.Order()))}. Protocols: {string.Join(", ", Protocols.Order())}.");
        foreach (var f in Failures) s.AppendLine($"- {E(f.File)}: {E(f.Reason)}");
        s.AppendLine().AppendLine("## Moves").AppendLine();
        s.AppendLine("| Package | Form | Move (ranked by activations) | Activations | Landed | Blocked | Damage |");
        s.AppendLine("|---|---|---|---:|---:|---:|---:|");
        foreach (var m in Moves.OrderBy(m => m.Key.Package).ThenByDescending(m => m.Activations).ThenBy(m => m.Key.Form).ThenBy(m => m.Key.Move))
            s.AppendLine($"| {E(m.Key.Package)} | {E(m.Key.Form)} | {E(m.Key.Move)} | {m.Activations} | {m.Landed} | {m.Blocked} | {N(m.Damage)} |");
        s.AppendLine().AppendLine("## KOs").AppendLine().AppendLine($"Total: {KOs.Count}.");
        s.AppendLine().AppendLine("| Match | Tick | Victim | Credited fighter | Last-hit move | Pre-respawn position | Boundary |");
        s.AppendLine("|---|---:|---|---|---|---|---|");
        foreach (var k in KOs) s.AppendLine($"| {E(k.Match)} | {k.Tick} | {E(k.Victim)} | {E(k.CreditedFighter)} | {E(k.Move)} | {N(k.Position.X)}, {N(k.Position.Y)}, {N(k.Position.Z)} | {E(k.Boundary)} |");
        s.AppendLine().AppendLine("By move:");
        foreach (var g in KOs.GroupBy(k => new MoveKey(k.Package, k.Form, k.Move)).OrderByDescending(g => g.Count()))
            s.AppendLine($"- {E(g.Key.Package)} / {E(g.Key.Form)} / {E(g.Key.Move)}: {g.Count()}");
        s.AppendLine().AppendLine("By credited fighter:");
        foreach (var g in KOs.GroupBy(k => k.CreditedFighter).OrderByDescending(g => g.Count())) s.AppendLine($"- {E(g.Key)}: {g.Count()}");
        s.AppendLine().AppendLine("## Endings").AppendLine();
        foreach (var g in Endings.GroupBy(e => e.Reason).OrderBy(g => g.Key)) s.AppendLine($"- {g.Key}: {g.Count()}");
        s.AppendLine().AppendLine("| Match | Reason | Seconds | Winners |").AppendLine("|---|---|---:|---|");
        foreach (var e in Endings) s.AppendLine($"| {E(e.Match)} | {e.Reason} | {N(e.Seconds)} | {E(string.Join(", ", e.Winners))} |");
        s.AppendLine().AppendLine("Win rates (finished matches with a recorded result; draws count as zero wins):");
        var finished = Endings.Where(e => e.Reason == "finished" && (e.Winners.Length > 0 || e.Draw)).ToArray();
        foreach (string fighter in finished.SelectMany(e => e.Participants).Distinct().Order())
        {
            var matches = finished.Where(e => e.Participants.Contains(fighter)).ToArray();
            s.AppendLine($"- {E(fighter)}: {matches.Count(e => !e.Draw && e.Winners.Contains(fighter))}/{matches.Length}");
            foreach (string opponent in matches.SelectMany(e => e.Participants).Where(p => p != fighter).Distinct().Order())
            {
                var pair = matches.Where(e => e.Participants.Contains(opponent)).ToArray();
                s.AppendLine($"  - vs {E(opponent)}: {pair.Count(e => !e.Draw && e.Winners.Contains(fighter))}/{pair.Length}");
            }
        }
        return s.ToString();
    }
}
