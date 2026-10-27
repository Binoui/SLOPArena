using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.MovementReport;

internal static partial class Program
{
    private static int ReportGroundSpeed(List<CharacterDefinition> defs, string[] args, string? jsonDir, string? outPath)
    {
        int repeats = int.Parse(ParseArg(args, "--repeats") ?? "5", CultureInfo.InvariantCulture);
        if (repeats < 1 || repeats > 100) throw new ArgumentOutOfRangeException(nameof(repeats));
        var rows = new List<MovementProbe.GroundSpeedMetrics>();
        var baseline = new Dictionary<string, string>();
        for (int run = 0; run < repeats; run++)
        foreach (var def in defs)
        {
            var measured = MovementProbe.MeasureGroundSpeed(def, FlatArena());
            string json = JsonSerializer.Serialize(measured, JsonOpts);
            if (run == 0) { baseline.Add(def.DisplayName, json); rows.Add(measured); }
            else if (baseline[def.DisplayName] != json)
                throw new InvalidOperationException($"Non-deterministic {def.DisplayName}, run {run + 1}.");
            if (jsonDir != null)
            {
                Directory.CreateDirectory(jsonDir);
                File.WriteAllText(Path.Combine(jsonDir, $"{def.Class}-run{run + 1}.json"), json);
            }
        }

        string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        var sb = new StringBuilder("# Universal ground movement measurements\n\n");
        sb.AppendLine($"ServerSimulation at 60 Hz. {repeats} interleaved repetitions per admitted fighter. Repeated JSON curves are identical, with zero metric spread. Each probe uses one fighter on a large flat floor and rejects missing cruise, missing stops, failed reversals, airborne samples, or deaths.\n");
        sb.AppendLine("Tick zero is standstill. Tick 1 sets speed to 40% of RunSpeed. The next 20 ticks add 3% of RunSpeed each, reaching full speed at completed tick 21, or 350 ms. Ramp distance includes that first full-speed tick. Held distances include exactly 6, 12, or 18 ticks. Total distances include every release tick through standstill. Stop and reversal start after 60 held ticks.\n");
        sb.AppendLine("| Fighter | Run m/s | First tick m/s | Full speed ticks / ms | Ramp m | 0.1 s held / total m | 0.2 s held / total m | 0.3 s held / total m | Run stop ticks / m | Reversal first m/s / ticks | Reversal displacement m |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Character} | {F(r.RunSpeed)} | {F(r.FirstTickSpeed)} | {r.TimeToRunTicks} / {r.TimeToRunTicks / 60f * 1000f:F1} | {F(r.RampDistance)} | {F(r.Tap6.HeldDistance)} / {F(r.Tap6.TotalDistance)} | {F(r.Tap12.HeldDistance)} / {F(r.Tap12.TotalDistance)} | {F(r.Tap18.HeldDistance)} / {F(r.Tap18.TotalDistance)} | {r.RunStop.StopTicks} / {F(r.RunStop.StopDistance)} | {F(r.Reversal.FirstReverseSpeed)} / {r.Reversal.RecoveryTicks} | {F(r.Reversal.DisplacementToCruise)} |");
        sb.AppendLine("\nRunSpeed bounds held speed. Release removes RunSpeed/20 along the velocity vector each tick, so the full-speed stop distance is 19*RunSpeed/120 metres. Turns retain current speed and continue the ramp, with a 40% floor. Full-speed reversal therefore flips on tick 1. Half-tilt curves use the same rule and match full tilt. No ramp counter or Rush window is used. Dash overspeed follows the prior coast behavior until it reaches the run cap.\n");
        sb.AppendLine("These measurements are simulation ticks and metres. They exclude hardware deadzones, input/render latency, walls, slopes, combat, and human feel. They are not wall-clock performance benchmarks.\n");
        if (outPath != null)
        {
            File.WriteAllText(outPath, sb.ToString());
            File.WriteAllText(Path.ChangeExtension(outPath, ".json"), JsonSerializer.Serialize(rows, JsonOpts));
        }
        else Console.WriteLine(sb.ToString());
        return 0;
    }
}
