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
/// Measures how forgiving each admitted normal is against standing roster targets, from the
/// cooked content the game runs: clip playback speed, active ticks that touch a nearby target,
/// real-sim reach bands for ground normals, how far below or above its feet an aerial reaches,
/// and how much of a bone or weapon swing passes between tested ticks. Use it to compare
/// characters and to check a re-authored attack after its cook.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/HitReliabilityReport -- [--char all|fightguy|manki|wibou|bonk]
               [--out artifacts/hit-reliability]

        Writes <char>.md and <char>.json per character. Targets are idle, standing FightGuy
        (medium) and Manki (small); measurements read the admitted content-cooked roster.
        """;

    private static readonly Dictionary<string, CharacterClass> Characters = new(StringComparer.Ordinal)
    {
        ["fightguy"] = CharacterClass.FightGuy, ["manki"] = CharacterClass.Manki,
        ["wibou"] = CharacterClass.Wibou, ["bonk"] = CharacterClass.Bonk,
    };

    private static int Main(string[] args)
    {
        string selector = "all";
        string output = Path.Combine("artifacts", "hit-reliability");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h": Console.WriteLine(Usage); return 0;
                case "--char" when i + 1 < args.Length: selector = args[++i].ToLowerInvariant(); break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
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
        var targets = new[] { CharacterClass.FightGuy, CharacterClass.Manki }.Select(c => BuiltInContentResolver.Resolve(c)).ToArray();
        Directory.CreateDirectory(output);
        foreach (var (name, cls) in Characters.Where(x => selector == "all" || x.Key == selector))
        {
            var attacker = BuiltInContentResolver.Resolve(cls);
            var moves = Reliability.Measure(attacker, targets, arena);
            string markdown = Markdown(attacker, moves);
            File.WriteAllText(Path.Combine(output, name + ".md"), markdown);
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                attacker.Identity, generatedUtc = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture), moves,
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            int flagged = moves.Count(x => x.Flags.Count > 0);
            Console.WriteLine($"{name}: {moves.Count} normals, {flagged} flagged → {Path.GetFullPath(Path.Combine(output, name + ".md"))}");
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

        md.AppendLine("## Ground normals").AppendLine();
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

        md.AppendLine("## Aerial normals").AppendLine();
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

        md.AppendLine("## Bone and weapon sampling").AppendLine();
        md.AppendLine("Grid positions (0.5–3 m ahead, ±40°) where a standing FightGuy-sized target lies in the swing's path "
            + "between ticks but is never touched by a tested tick.").AppendLine();
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
