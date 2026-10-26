using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        var baseline = new Dictionary<(string, GroundSpeedModel), string>();
        for (int run = 0; run < repeats; run++)
        foreach (var def in defs)
        foreach (GroundSpeedModel model in Enum.GetValues<GroundSpeedModel>())
        {
            var measured = MovementProbe.MeasureGroundSpeed(def, FlatArena(), GroundSpeedTuning.For(model));
            string json = JsonSerializer.Serialize(measured, JsonOpts);
            var key = (def.DisplayName, model);
            if (run == 0) { baseline.Add(key, json); rows.Add(measured); }
            else if (baseline[key] != json) throw new InvalidOperationException($"Non-deterministic {key}, run {run + 1}.");
            if (jsonDir != null)
            {
                Directory.CreateDirectory(jsonDir);
                File.WriteAllText(Path.Combine(jsonDir, $"{def.Class}-{model}-run{run + 1}.json"), json);
            }
        }

        string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        var sb = new StringBuilder("# Training ground-speed prototype comparison\n\n");
        sb.AppendLine($"Measured with ServerSimulation at 60 Hz. {repeats} interleaved runs per fighter and model. All repeated sample curves are byte-identical JSON. There were no missing samples, deaths, airborne samples, or timeouts. The repeat range of every metric is zero.\n");
        sb.AppendLine("Distances include the first movement tick from the initial standstill. Held distance ends before release. Total distance includes all release braking. Stop distance starts at the last held sample and includes the first release tick. Time to cruise counts completed ticks, so instant speed is 1 tick. Half tilt means post-deadzone magnitude 0.5.\n");
        sb.AppendLine("| Fighter | Model | Run m/s | Half tilt m/s | Cruise ticks | Full tap 0.1 s held / total m | Full tap 0.2 s held / total m | Half tilt stop ticks / m | Run stop ticks / m |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Character} | {r.Model} | {F(r.RunSpeed)} | {F(r.HalfTiltSpeed)} | {r.TimeToRunTicks} | {F(r.Tap6.HeldDistance)} / {F(r.Tap6.TotalDistance)} | {F(r.Tap12.HeldDistance)} / {F(r.Tap12.TotalDistance)} | {r.WalkStop.StopTicks} / {F(r.WalkStop.StopDistance)} | {r.RunStop.StopTicks} / {F(r.RunStop.StopDistance)} |");
        sb.AppendLine("\n| Fighter | Model | Half tilt tap 0.1 s held / total m | Half tilt tap 0.2 s held / total m |");
        sb.AppendLine("|---|---|---:|---:|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Character} | {r.Model} | {F(r.HalfTiltTap6.HeldDistance)} / {F(r.HalfTiltTap6.TotalDistance)} | {F(r.HalfTiltTap12.HeldDistance)} / {F(r.HalfTiltTap12.TotalDistance)} |");
        sb.AppendLine("\nEach distance is bounded by the sampled velocity integral at 1/60 s per tick. The curves record the actual limiter. Current and Tiers run reach the authored cap on tick 1. Tiers walk reaches 0.4 times that cap on tick 1. Progressive defaults to 40% of the run cap on its first tick, then adds 3% of that cap per tick. Hybrid adds one third of the walk cap or one sixth of the run cap per tick. Walk release removes one third of the walk cap per tick. Progressive release removes one twentieth of the run cap per tick. Other ordinary run release removes 0.6 m/s per tick. Current, Tiers run, and Hybrid run stop immediately on release while their Rush window remains open.\n");
        sb.AppendLine("The old movement report excluded the first held sample from ramp distance and the first released sample from stop distance. This comparison uses explicit tick-zero and release boundaries. Its Current stop distances therefore include that previously omitted release tick.\n");
        sb.AppendLine("This measures game distance and simulation ticks, not wall-clock performance or physical controller travel. It excludes rendering latency, stick hardware/deadzone calibration, slopes, collisions, combat, and human feel. Current and Progressive have no walk tier, so their half-tilt stop is another run stop. The owner must compare feel in Training.\n");
        string markdown = sb.ToString();
        if (outPath != null) File.WriteAllText(outPath, markdown);
        else Console.WriteLine(markdown);
        return 0;
    }
    private static int ReportProgressive(List<CharacterDefinition> defs, string[] args, string? jsonDir, string? outPath)
    {
        int repeats = int.Parse(ParseArg(args, "--repeats") ?? "5", CultureInfo.InvariantCulture);
        if (repeats < 1 || repeats > 100) throw new ArgumentOutOfRangeException(nameof(repeats));
        var rows = new List<MovementProbe.ProgressiveMetrics>();
        var baseline = new Dictionary<(string, int, GroundSpeedCurve, float), string>();
        var variants = (from ramp in new[] { 10, 20, 30 }
                        from curve in new[] { GroundSpeedCurve.Linear, GroundSpeedCurve.SlowStart }
                        select GroundSpeedTuning.Progressive(ramp, curve, startRatio: 0f))
            .Concat(from ramp in new[] { 12, 20 }
                    from start in new[] { 0f, 0.4f, 0.5f }
                    select GroundSpeedTuning.Progressive(ramp, startRatio: start)).Distinct().ToArray();
        for (int run = 0; run < repeats; run++)
        foreach (var def in defs)
        foreach (var tuning in variants)
        {
            var measured = MovementProbe.MeasureProgressive(def, FlatArena(), tuning);
            string json = JsonSerializer.Serialize(measured, JsonOpts);
            var key = (def.DisplayName, tuning.RampTicks, tuning.Curve, tuning.StartRatio);
            if (run == 0) { baseline.Add(key, json); rows.Add(measured); }
            else if (baseline[key] != json) throw new InvalidOperationException($"Non-deterministic {key}, run {run + 1}.");
            if (jsonDir != null)
            {
                Directory.CreateDirectory(jsonDir);
                File.WriteAllText(Path.Combine(jsonDir, $"{def.Class}-{tuning.RampTicks}T-{tuning.Curve}-start{tuning.StartRatio * 100f:F0}-run{run + 1}.json"), json);
            }
        }
        string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        var sb = new StringBuilder("# Progressive Training tuning measurements\n\n");
        sb.AppendLine($"ServerSimulation at 60 Hz. {repeats} interleaved repetitions per fighter, ramp, curve, and start speed. Every repeated JSON curve is identical. All metric ranges are zero. Probes reject missing cruise, failed reversals, missing stops, airborne samples, and deaths. Each probe has one fighter on a flat floor. No combat contacts can occur.\n");
        sb.AppendLine("With StartRatio greater than zero, tick 1 sets speed to S = StartRatio * RunSpeed. Each later Linear tick adds H/RampTicks, where H = RunSpeed - S. Full speed arrives at tick RampTicks + 1. StartRatio zero preserves the previous ramp exactly and reaches full speed on tick RampTicks. SlowStart adds (H/RampTicks) * 1.6094379f * (0.25f + max(0, v-S)/H) to retained speed v. It uses forward Euler integration over the remaining speed span. It takes one additional tick at the measured ramps. A speed below S jumps exactly to S on that tick without an additional increment. Reversal applies TurnaroundKeep first and uses the same floor. No elapsed state is stored. The clamp only removes floating-point residue within one millionth of RunSpeed. No runtime log or exp is used.\n");
        sb.AppendLine("| Fighter | Start | Ramp | Curve | First tick m/s | Full speed ticks / ms | Ramp m | 0.1 s tap held / stopped m | 0.2 s tap held / stopped m | 0.3 s tap held / stopped m | Stop ticks / m | Reverse keep 100% first m/s / cruise ticks | Reverse keep 50% first m/s / cruise ticks |");
        sb.AppendLine("|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Character} | {r.Tuning.StartRatio * 100f:F0}% | {r.Tuning.RampTicks} | {r.Tuning.Curve} | {F(r.RunCurve[1].Speed)} | {r.TimeToRunTicks} / {F(r.TimeToRunTicks * 1000f / 60f)} | {F(r.RampDistance)} | {F(r.Tap6.HeldDistance)} / {F(r.Tap6.TotalDistance)} | {F(r.Tap12.HeldDistance)} / {F(r.Tap12.TotalDistance)} | {F(r.Tap18.HeldDistance)} / {F(r.Tap18.TotalDistance)} | {r.RunStop.StopTicks} / {F(r.RunStop.StopDistance)} | {F(r.Reverse100.FirstReverseSpeed)} / {r.Reverse100.RecoveryTicks} | {F(r.Reverse50.FirstReverseSpeed)} / {r.Reverse50.RecoveryTicks} |");
        sb.AppendLine("\nTick zero records standstill. Ramp distance includes the tick that first reaches full speed. Tap held distances include exactly 6, 12, or 18 movement ticks. Stopped totals include every release tick through standstill. Cruise stop starts at the last held sample after 60 held ticks and includes the first release tick. Reversal starts at full speed after 60 held ticks. Its first tick applies retention, then jumps to the start floor if below it, otherwise accelerates toward the new direction. Cruise ticks count from that first opposing input. Full reversal curves include signed displacement.\n");
        sb.AppendLine("The speed cap and per-tick acceleration bound ramp time and ramp distance. Linear adds the remaining speed span divided by RampTicks after the start. SlowStart begins at 0.4023595 times that increment, then rises with normalized speed across the remaining span. The sampled velocity integral at 1/60 second bounds distance. Release removes RunSpeed/20 per tick along the velocity vector, so cruise stop is 20 ticks and 19*RunSpeed/120 metres. Keep 100% flips at full speed in one tick. Keep 50% flips at half speed plus acceleration when that retained speed meets the start floor. Otherwise it flips at the start floor. JSON retains every sample used by each number.\n");
        sb.AppendLine("These are simulation durations and metres, not wall-clock performance. Tilt is full processed input. Hardware deadzones, input/render latency, terrain, collisions, abilities, and human feel are outside this measurement. StopTicks is 20 and other parameters use their requested defaults. Online, Arcade, Tutorial, and replay paths keep Current.\n");
        string markdown = sb.ToString();
        if (outPath != null)
        {
            File.WriteAllText(outPath, markdown);
            File.WriteAllText(Path.ChangeExtension(outPath, ".json"), JsonSerializer.Serialize(rows, JsonOpts));
        }
        else Console.WriteLine(markdown);
        return 0;
    }

}
