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
        var compiled = CharacterPackageCompiler.CompilePortable(File.ReadAllText(Path.Combine(folder, "package.json")),
            File.ReadAllText(Path.Combine(folder, "character.json")));
        if (compiled.HasErrors) throw new Exception(string.Join("\n", compiled.Diagnostics));
        var package = compiled.CookedPackage;
        if (BitConverter.SingleToInt32Bits(package.Definition.Movement.AirAccelBase) != 0x3f800001
            || BitConverter.SingleToInt32Bits(package.Definition.Movement.AirFloatGravity) != 0)
            throw new Exception("Portable midpoint rounding or negative zero differs.");
        var admission = typeof(CharacterPackageCompiler).Assembly.GetType("SlopArena.Shared.FighterItemAdmission")
            .GetMethod("ReadLocalItem", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { folder });
        var diagnostics = (System.Collections.Generic.IReadOnlyList<CharacterDiagnostic>)admission.GetType().GetProperty("Diagnostics").GetValue(admission);
        if (diagnostics.Count != 1 || diagnostics[0].Code != "item.pose-bake.unavailable")
            throw new Exception("Expected the pose bake admission gate: " + string.Join(";", diagnostics));
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
