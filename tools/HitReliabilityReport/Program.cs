using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Shared.Tests")]

namespace SlopArena.HitReliabilityReport;

/// <summary>
/// Measures how forgiving each admitted normal (or selected special) is against standing roster
/// targets, from the cooked content the game runs: clip playback speed, active ticks that touch a
/// nearby target, real-sim reach bands for ground moves, how far below or above its feet an aerial
/// reaches, where around the attacker each hit of a multi-hit ground move lands, and how much of a
/// bone or weapon swing the engine leaves untested between ticks. Use it to compare characters and
/// to check a re-authored attack after its cook.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/HitReliabilityReport -- [--char all|fightguy|manki|wibou|bonk|bashie|salamander|nyx]
               [--slots 1,3,4] [--form 0|1] [--content-root content-cooked] [--out artifacts/hit-reliability]

        Writes <char>.md and <char>.json per character. Targets are idle, standing FightGuy
        (medium) and Manki (small); measurements read the admitted content-cooked roster.
        Ground moves also get an arc map (target feet height x distance, behind to ahead).
        --slots picks canonical slots (1-4, A, E, R, F), ground and air; the default is the normals.
        """;

    private static readonly Dictionary<string, CharacterClass> Characters = new(StringComparer.Ordinal)
    {
        ["fightguy"] = CharacterClass.FightGuy, ["manki"] = CharacterClass.Manki,
        ["wibou"] = CharacterClass.Wibou, ["bonk"] = CharacterClass.Bonk,
        ["bashie"] = CharacterClass.Bashie,
        ["salamander"] = CharacterClass.Salamander,
        ["nyx"] = CharacterClass.Nyx,
    };

    private static int Main(string[] args)
    {
        string selector = "all";
        byte form = 0;
        string cookedRoot = "content-cooked";
        string output = Path.Combine("artifacts", "hit-reliability");
        byte[] wires = Reliability.NormalWires;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h": Console.WriteLine(Usage); return 0;
                case "--char" when i + 1 < args.Length: selector = args[++i].ToLowerInvariant(); break;
                case "--content-root" when i + 1 < args.Length: cookedRoot = args[++i]; break;
                case "--form" when i + 1 < args.Length:
                    if (!byte.TryParse(args[++i], out form) || form > 1)
                    { Console.Error.WriteLine("form must be 0 or 1."); return 2; }
                    break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--slots" when i + 1 < args.Length:
                    if (Reliability.ParseSlots(args[++i]) is not { } parsed)
                    {
                        Console.Error.WriteLine($"Unknown slots '{args[i]}'.\n{Usage}");
                        return 2;
                    }
                    wires = parsed;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.\n{Usage}");
                    return 2;
            }
        }
        if (selector != "all" && !Characters.ContainsKey(selector))
        {
            Console.Error.WriteLine($"Unknown character '{selector}'.\n{Usage}");
            return 2;
        }

        var arena = Reliability.FlatArena();
        var targets = new[] { CharacterClass.FightGuy, CharacterClass.Manki }.Select(c => BuiltInContentResolver.Resolve(c, cookedRoot)).ToArray();
        Directory.CreateDirectory(output);
        foreach (var (name, cls) in Characters.Where(x => selector == "all" || x.Key == selector))
        {
            var attacker = BuiltInContentResolver.Resolve(cls, cookedRoot);
            if (form != 0)
            {
                if (attacker.Definition.AlternateForm == null)
                { Console.Error.WriteLine($"{name} has no alternate form."); return 2; }
                attacker = new MatchContentEntry(attacker.Handle, attacker.LegacySelector, attacker.Identity,
                    attacker.DisplayName + " form " + form, attacker.Definition.ForForm(form), attacker.BakedAnimation,
                    attacker.CookedCharacterPackage);
            }
            var moves = Reliability.Measure(attacker, targets, arena, wires);
            string markdown = Markdown(attacker, moves);
            File.WriteAllText(Path.Combine(output, name + ".md"), markdown);
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                attacker.Identity, generatedUtc = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture), moves,
                // The idle targets' contact volumes at the origin facing the attacker: start xyz, end xyz, radius
                // (x right, y up from the floor, z toward the attacker's front), for tools that test other arcs.
                targets = targets.ToDictionary(t => t.Definition.Class.ToString(),
                    t => Reliability.Volumes(t, arena).Select(v => new[] { v.PosX - Reliability.OriginMeters, v.PosY,
                        v.PosZ - Reliability.OriginMeters, v.EndX - Reliability.OriginMeters, v.EndY,
                        v.EndZ - Reliability.OriginMeters, v.Radius }).ToList()),
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            int flagged = moves.Count(x => x.Flags.Count > 0);
            Console.WriteLine($"{name}: {moves.Count} moves, {flagged} flagged → {Path.GetFullPath(Path.Combine(output, name + ".md"))}");
        }
        return 0;
    }

    internal static string Markdown(MatchContentEntry attacker, IReadOnlyList<MoveReliability> moves)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {attacker.DisplayName} hit reliability").AppendLine();
        md.AppendLine($"Package `{attacker.Identity.PackageId}` {attacker.Identity.Version}, cooked `{attacker.Identity.CookedContentHash[..12]}`. "
            + "Idle standing targets; ground reach comes from the real simulation (5 cm steps), aerial and sampling "
            + "columns from the cooked hitbox geometry with the attacker fixed in place.").AppendLine();

        md.AppendLine("## Ground moves").AppendLine();
        md.AppendLine($"| Move | Playback | Active ticks | Touch at {Reliability.ContactProbeMeters:F0} m | Reach vs FightGuy | Reach vs Manki | At 30° | Flags |");
        md.AppendLine("|---|---:|---:|---:|---|---|---|---|");
        foreach (var move in moves.Where(x => !x.Air))
        {
            string Band(string target, float bearing) => Bands(move.Reach.FirstOrDefault(x => x.Target == target && x.BearingDegrees == bearing));
            bool same = move.Reach.Where(x => x.BearingDegrees == 0f).All(zero =>
                Bands(zero) == Bands(move.Reach.FirstOrDefault(x => x.Target == zero.Target && x.BearingDegrees == 30f)));
            md.AppendLine(FormattableString.Invariant($"| {move.Slot} {move.Name} | {move.PlaybackSpeed:F2}× | {move.ActiveTicks} | {move.ContactTicksAtProbe} | ")
                + $"{Band("FightGuy", 0f)} | {Band("Manki", 0f)} | {(same ? "same" : "differs")} | {string.Join("; ", move.Flags)} |");
        }
        md.AppendLine();

        md.AppendLine("## Aerial moves").AppendLine();
        md.AppendLine("Target feet height relative to the attacker's feet that any active tick can touch (searched −3 to +2 m).").AppendLine();
        md.AppendLine("| Move | Playback | Active ticks | vs FightGuy | vs Manki | Flags |");
        md.AppendLine("|---|---:|---:|---|---|---|");
        foreach (var move in moves.Where(x => x.Air))
        {
            string Band(string target)
            {
                var band = move.Vertical.FirstOrDefault(x => x.Target == target);
                return band?.Lowest == null ? "never" : FormattableString.Invariant($"{band.Lowest:+0.0;-0.0;0} to {band.Highest:+0.0;-0.0;0} m");
            }
            md.AppendLine(FormattableString.Invariant($"| {move.Slot} {move.Name} | {move.PlaybackSpeed:F2}× | {move.ActiveTicks} | ")
                + $"{Band("FightGuy")} | {Band("Manki")} | {string.Join("; ", move.Flags)} |");
        }
        md.AppendLine();

        var multi = moves.Where(x => x.Hits.Count > 0).ToList();
        if (multi.Count > 0)
        {
            var columns = multi[0].Hits[0].Touched.Select(x => (x.Target, x.Distance)).ToList();
            md.AppendLine("## Multi-hit coverage").AppendLine();
            md.AppendLine($"Bearings (of {Reliability.CoverageBearings}, 15° apart) at which each hit of a multi-hit ground move "
                + "touches a standing target, from the poses the engine tests, with the attacker fixed and the target never "
                + "knocked away. A hit with zero everywhere cannot land on that target.").AppendLine();
            md.AppendLine("| Move | Ticks | Damage | Radius | "
                + string.Join(" | ", columns.Select(c => FormattableString.Invariant($"{c.Target} {c.Distance:0.0} m"))) + " |");
            md.AppendLine("|---|---|---:|---:|" + string.Concat(columns.Select(_ => "---:|")));
            foreach (var move in multi)
                foreach (var hit in move.Hits)
                    md.AppendLine(FormattableString.Invariant($"| {move.Slot} | {hit.FirstTick}–{hit.LastTick} | {hit.Damage:0.#} | {hit.Radius:F2} | ")
                        + string.Join(" | ", hit.Touched.Select(x => x.Bearings.ToString(CultureInfo.InvariantCulture))) + " |");
            md.AppendLine();
        }

        var arcs = moves.Where(x => x.Arc.Count > 0).ToList();
        if (arcs.Count > 0)
        {
            md.AppendLine("## Arc (ground moves)").AppendLine();
            md.AppendLine("Side view of where an idle target is touched by any active tick, from the cooked hitbox geometry with "
                + "the attacker fixed: rows are the target's feet height above the floor (3 m at the top, 0 = standing), "
                + "columns its distance along the attacker's facing from 2.5 m behind to 3.5 m ahead, 25 cm apart. "
                + "`#` touched, `.` not, `|` the attacker's own column.").AppendLine();
            foreach (var move in arcs)
                foreach (var arc in move.Arc)
                {
                    md.AppendLine($"{move.Slot} {move.Name} vs {arc.Target}:").AppendLine().AppendLine("```");
                    int zero = arc.Distances.FindIndex(d => Math.Abs(d) < 1e-3f);
                    for (int r = 0; r < arc.Rows.Count; r++)
                    {
                        var row = arc.Rows[r].ToCharArray();
                        if (zero >= 0 && row[zero] == '.') row[zero] = '|';
                        md.AppendLine(FormattableString.Invariant($"{arc.Heights[r],4:0.00} {new string(row)}"));
                    }
                    md.AppendLine(FormattableString.Invariant($"     {"-2.5",-10}{"0",-10}{"+2.5",-5}")).AppendLine("```").AppendLine();
                }
        }

        md.AppendLine("## Bone and weapon sampling").AppendLine();
        md.AppendLine("Grid positions (0.5–3 m ahead, ±40°) where a standing FightGuy-sized target lies in the swing's path "
            + "between ticks but no tested pose touches it. Tested poses are each tick's pose plus the swept poses the "
            + "engine tests between active ticks, so Untested should stay near zero.").AppendLine();
        md.AppendLine("| Move | Anchor | Radius | Max tip step / tick | Target feet | Swept | Untested |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|");
        foreach (var move in moves)
            foreach (var row in move.Sampling)
                md.AppendLine(FormattableString.Invariant(
                    $"| {move.Slot} | `{row.Anchor}` | {row.Radius:F2} | {row.MaxTipStepPerTick:F2} m | {row.TargetFeetHeight:+0.0;-0.0;0} m | {row.SweptPositions} | {(row.SweptPositions == 0 ? "–" : $"{row.LostShare * 100:F0}%")} |"));
        return md.ToString();
    }

    private static string Bands(ReachBand? band)
        => band == null || band.Intervals.Count == 0 ? "never"
            : string.Join(", ", band.Intervals.Select(x => FormattableString.Invariant($"{x.From:F2}–{x.To:F2} m")));
}
