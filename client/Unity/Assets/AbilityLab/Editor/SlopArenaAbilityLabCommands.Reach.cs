using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace SlopArena.EditorTools;

public static partial class SlopArenaAbilityLabCommands
{
    [CliCommand("sloparena.lab.reach", "Measure the selected live draft against FightGuy and Manki using the HitReliabilityReport sweep.",
        MainThreadRequired = true, Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Reach(
        [CliArg("action", "Canonical move ID, such as ground.1 or air.R. Defaults to the selected move.", Required = false, DefaultValue = "")] string action = "",
        [CliArg("output", "Optional report JSON path under tmp/ or .ability-lab-cache/. Existing files are rejected.", Required = false, DefaultValue = "")] string output = "")
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out string modeError)) return Failure("lab.mode.unsupported", modeError, window);
        if (window == null || !window.CommandWorkspace.HasPackage)
            return Failure("workspace.missing", "Open a Character Package with sloparena.lab.open first.", window);
        string? path = null;
        if (!string.IsNullOrEmpty(output))
        {
            try
            {
                path = ResolveReachOutput(output);
            }
            catch (Exception ex) { return Failure("reach.output.invalid", ex.Message, window); }
        }
        try
        {
            var observation = window.RunReachForCommand(string.IsNullOrEmpty(action) ? window.CurrentReachAction : action);
            string json = observation.Document.ToJson();
            if (path != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write));
                file.Write(json);
            }
            var result = Snapshot(window);
            // JRaw preserves the Shared writer's timestamp, tuples, number formatting and property order.
            result.Reach = new JRaw(json);
            result.ReachElapsedMs = observation.ElapsedMs;
            return result;
        }
        catch (AbilityLabReachException ex) { return Failure(ex.Code, ex.Message, window); }
        catch (Exception ex) { return Failure("reach.measure.failed", ex.Message, window); }
    }

    private static string ResolveReachOutput(string output)
    {
        string normalized = output.Replace('\\', '/');
        if (Path.IsPathRooted(output) || !(normalized.StartsWith("tmp/", StringComparison.Ordinal) ||
            normalized.StartsWith(".ability-lab-cache/", StringComparison.Ordinal)) ||
            !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || normalized.Split('/').Contains(".."))
            throw new ArgumentException("Use a repository-relative .json path below tmp/ or .ability-lab-cache/.");
        string root = RepositoryRoot();
        string path = Path.GetFullPath(Path.Combine(root, normalized));
        for (string? current = path; current != null && current != root; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reach output cannot traverse symbolic links.");
        }
        if (File.Exists(path)) throw new ArgumentException("Reach output already exists. Choose a fresh path.");
        return path;
    }
}
