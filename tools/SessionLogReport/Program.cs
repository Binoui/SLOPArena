using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared.Telemetry;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Shared.Tests")]

namespace SlopArena.SessionLogReport;

/// <summary>
/// Turns local practice session logs (Settings → Gameplay → Record practice session log) into one
/// self-contained HTML page: contact rate and whiff direction per move and device, misses against
/// the camera's angle to the target, opponent visibility and camera effort, and hits taken from
/// off-screen. Reads files only; nothing is uploaded.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/SessionLogReport -- [files-or-directories...]
               [--last N] [--out report.html] [--json summary.json]

        Without paths, reads the game's session log folder for this user
        (Linux ~/.config/unity3d/SLOPArena/SLOPArena/SessionLogs,
         Windows %USERPROFILE%\AppData\LocalLow\SLOPArena\SLOPArena\SessionLogs).
        --last N   only the N most recently modified sessions
        --out      HTML destination (default artifacts/session-report/index.html)
        --json     also write the aggregated model as JSON
        """;

    private static int Main(string[] args)
    {
        var inputs = new List<string>();
        string output = Path.Combine("artifacts", "session-report", "index.html");
        string? json = null;
        int? last = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return 0;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--json" when i + 1 < args.Length: json = args[++i]; break;
                case "--last" when i + 1 < args.Length && int.TryParse(args[i + 1], out int count) && count > 0:
                    last = count; i++; break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown or incomplete option '{arg}'.\n{Usage}");
                        return 2;
                    }
                    inputs.Add(arg);
                    break;
            }
        }
        if (inputs.Count == 0) inputs.Add(DefaultDirectory());

        var files = Discover(inputs);
        if (last is int keep) files = files.OrderByDescending(File.GetLastWriteTimeUtc).Take(keep).ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No *{SessionLogFormat.FileSuffix} files found in: {string.Join(", ", inputs)}");
            Console.Error.WriteLine("Turn on Settings → Gameplay → Record practice session log, then play Training or Solo.");
            return 1;
        }

        var sessions = new List<SessionLogData>();
        foreach (var file in files.OrderBy(File.GetLastWriteTimeUtc))
        {
            try { sessions.Add(SessionLogReader.ReadFile(file)); }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is JsonException)
            {
                Console.Error.WriteLine($"skipped {file}: {exception.Message}");
            }
        }
        if (sessions.Count == 0) return 1;

        var model = SessionAnalysis.Analyze(sessions);
        Write(output, ReportHtml.Render(model, DateTime.UtcNow.ToString("u")));
        Console.WriteLine($"sessions={sessions.Count} swings={model.Overall.Presses} whiffs={model.Overall.Whiffs} contact={model.Overall.ContactRate:P0}");
        Console.WriteLine($"wrote {Path.GetFullPath(output)}");
        if (json != null)
        {
            Write(json, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            Console.WriteLine($"wrote {Path.GetFullPath(json)}");
        }
        return 0;
    }

    internal static string DefaultDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root;
        if (OperatingSystem.IsWindows())
            root = Path.Combine(home, "AppData", "LocalLow");
        else if (OperatingSystem.IsMacOS())
            root = Path.Combine(home, "Library", "Application Support");
        else
            root = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(home, ".config"), "unity3d");
        return Path.Combine(root, "SLOPArena", "SLOPArena", "SessionLogs");
    }

    private static List<string> Discover(IEnumerable<string> inputs)
    {
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (File.Exists(input)) files.Add(Path.GetFullPath(input));
            else if (Directory.Exists(input))
                files.AddRange(Directory.EnumerateFiles(input, "*" + SessionLogFormat.FileSuffix).Select(Path.GetFullPath));
        }
        return files.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Write(string path, string contents)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, contents);
    }
}
