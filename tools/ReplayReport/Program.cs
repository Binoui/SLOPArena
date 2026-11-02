using System.Globalization;
using System.Text.Json;
using SlopArena.ReplayReport;

try
{
    if (args.Length == 0) throw new ArgumentException("Usage: ReplayReport <dir> [--since yyyy-MM-dd] [--out report.md] [--json report.json]");
    DateTime? since = null;
    string? markdown = null, json = null;
    for (int i = 1; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length) throw new ArgumentException("Missing option value");
        switch (args[i])
        {
            case "--since": since = DateTime.ParseExact(args[i + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal); break;
            case "--out": markdown = args[i + 1]; break;
            case "--json": json = args[i + 1]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    var report = PatternReport.Read(args[0], since);
    string text = report.Markdown();
    Console.Write(text);
    if (markdown != null) File.WriteAllText(markdown, text);
    if (json != null) File.WriteAllText(json, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return report.Failures.Count == 0 ? 0 : 1;
}
catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
