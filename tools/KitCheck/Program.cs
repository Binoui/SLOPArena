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
    if (args.Length == 1 || args is ["moves", _] or ["cook", _] or ["cook", _, _])
    {
        string directory = args.Length > 1 ? args[1] : args[0];
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(directory, "package.json")),
            File.ReadAllText(Path.Combine(directory, "character.json")), CharacterCookProfile.Workshop);
        var diagnostics = result.Diagnostics.ToList();
        if (args[0] == "cook" || File.Exists(Path.Combine(directory, "cook.json")))
            diagnostics.AddRange(FighterCookDocument.CheckFolder(directory, result, args.Length == 3 ? args[2] : null));
        foreach (var d in diagnostics)
        {
            Console.WriteLine(LocalFighterWorkspace.DiagnosticText(d));
        }
        bool failed = diagnostics.Any(d => d.Severity == CharacterDiagnosticSeverity.Error);
        Console.WriteLine(failed ? "COMPILE FAILED" : $"COMPILE OK ({diagnostics.Count} diagnostics)");
        if (!failed && args is ["moves", _]) MovesReport.Write(result.CookedPackage!, Console.Out);
        return failed ? 1 : 0;
    }
    Console.Error.WriteLine("Usage: KitCheck <source-folder> | cook <source-folder> [glb] | moves <source-folder> | template <folder> <package-id> <display-name> | reference [--write] <repo-root>");
    return 2;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
