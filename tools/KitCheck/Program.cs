using SlopArena.Shared;
using SlopArena.KitCheck;

try
{
    if (args is ["reference", "--write", var writeRoot])
    {
        var path = Path.Combine(writeRoot, "docs/characters/workshop-authoring-reference.md");
        var document = File.ReadAllText(path);
        var start = document.IndexOf(AuthoringReference.Start, StringComparison.Ordinal);
        var end = document.IndexOf(AuthoringReference.End, StringComparison.Ordinal);
        if (start < 0 || end < start) throw new ArgumentException("Generated contract markers are missing.");
        File.WriteAllText(path, document[..start] + AuthoringReference.Generate() + document[(end + AuthoringReference.End.Length)..].TrimStart('\r', '\n'));
        return 0;
    }
    if (args is ["reference"] or ["reference", _])
    {
        Console.Write(AuthoringReference.Generate());
        return 0;
    }
    if (args is ["template", var folder, var id, var name])
    {
        KitTemplate.Write(folder, id, name);
        return 0;
    }
    if (args.Length == 1 || args is ["moves", _])
    {
        string directory = args[^1];
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(directory, "package.json")),
            File.ReadAllText(Path.Combine(directory, "character.json")), CharacterCookProfile.Workshop);
        foreach (var d in result.Diagnostics)
        {
            Console.WriteLine(LocalFighterWorkspace.DiagnosticText(d));
        }
        Console.WriteLine(result.HasErrors ? "COMPILE FAILED" : $"COMPILE OK ({result.Diagnostics.Count} diagnostics)");
        if (!result.HasErrors && args.Length == 2) MovesReport.Write(result.CookedPackage!, Console.Out);
        return result.HasErrors ? 1 : 0;
    }
    Console.Error.WriteLine("Usage: KitCheck <source-folder> | moves <source-folder> | template <folder> <package-id> <display-name> | reference [--write] <repo-root>");
    return 2;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
