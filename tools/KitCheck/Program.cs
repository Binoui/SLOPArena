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
        Directory.CreateDirectory(folder);
        var source = CharacterPackageSourceCodec.CreateAuthoringReady(id, name, "Your name", "CC0-1.0", "Original work");
        File.WriteAllText(Path.Combine(folder, "package.json"), CharacterPackageSourceCodec.SerializeManifest(source.Manifest));
        File.WriteAllText(Path.Combine(folder, "character.json"), CharacterPackageSourceCodec.SerializeCharacter(source.Character));
        return 0;
    }
    if (args is [var directory])
    {
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(directory, "package.json")),
            File.ReadAllText(Path.Combine(directory, "character.json")), CharacterCookProfile.Workshop);
        foreach (var d in result.Diagnostics)
        {
            var path = d.Path.StartsWith("manifest", StringComparison.Ordinal)
                ? "package.json" + d.Path[8..] : "character.json" + (d.Path.StartsWith("character", StringComparison.Ordinal) ? d.Path[9..] : "." + d.Path);
            Console.WriteLine($"{d.Severity} {d.Code} {path}: {d.Message}");
        }
        Console.WriteLine(result.HasErrors ? "COMPILE FAILED" : $"COMPILE OK ({result.Diagnostics.Count} diagnostics)");
        return result.HasErrors ? 1 : 0;
    }
    Console.Error.WriteLine("Usage: KitCheck <source-folder> | template <folder> <package-id> <display-name> | reference [--write] <repo-root>");
    return 2;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
