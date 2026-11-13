using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared;

internal static class Program
{
    public static int Main(string[] args)
    {
        string folder = args[0];
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(folder, "package.json")),
            File.ReadAllText(Path.Combine(folder, "character.json")), CharacterCookProfile.Workshop);
        if (compiled.HasErrors) throw new Exception(string.Join("\n", compiled.Diagnostics));
        var package = compiled.CookedPackage;
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));
        var m = manifest.RootElement;
        var result = CharacterPackageAssembler.Assemble(new CharacterPackageAssemblyInput(package.Metadata.PackageId,
            package.Metadata.Version, m.GetProperty("creator").GetString(), m.GetProperty("license").GetString(), m.GetProperty("attribution").GetString(),
            m.GetProperty("authoringSchemaVersion").GetUInt16(), package.Metadata.CookedSchemaVersion, package.Metadata.RuntimeApiMin, package.Metadata.RuntimeApiMax,
            m.GetProperty("sourceHash").GetString(), Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements,
            "portable-1", "portable", 3, "SKEL", 1, 60, Array.Empty<CharacterDiagnostic>(), package.CanonicalBytes,
            File.ReadAllBytes(Path.Combine(folder, "poses.bin")), File.ReadAllBytes(Path.Combine(folder, "client.bindings")), package));
        if (!result.IsValid) throw new Exception(string.Join("\n", result.Diagnostics));
        if (!result.RuntimeBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "character.runtime.json")))) throw new Exception("Runtime bytes differ.");
        if (!result.ManifestBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "manifest.json")))) throw new Exception("Manifest bytes differ.");
        Console.WriteLine(result.CookedContentHash + " " + result.PackageHash);
        return 0;
    }
}
