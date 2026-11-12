using SlopArena.Shared;

namespace SlopArena.KitCheck;

public static class KitTemplate
{
    public static void Write(string folder, string id, string name)
    {
        var source = CharacterPackageSourceCodec.CreateAuthoringReady(id, name, "Your name", "CC0-1.0", "Original work");
        var validation = CharacterPackageCompiler.Compile(source, CharacterCookProfile.Workshop);
        if (validation.HasErrors)
            throw new ArgumentException(string.Join(Environment.NewLine, validation.Diagnostics.Select(d => d.Code + " " + d.Path + ": " + d.Message)));
        var files = new[]
        {
            (Name: "package.json", Json: CharacterPackageSourceCodec.SerializeManifest(source.Manifest)),
            (Name: "character.json", Json: CharacterPackageSourceCodec.SerializeCharacter(source.Character)),
        };
        foreach (var file in files)
            if (File.Exists(Path.Combine(folder, file.Name)))
                throw new IOException("Refusing to overwrite " + Path.Combine(folder, file.Name));
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            using var stream = new FileStream(Path.Combine(folder, file.Name), FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(file.Json);
        }
    }
}
