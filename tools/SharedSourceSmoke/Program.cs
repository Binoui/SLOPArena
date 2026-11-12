using System.Text.Json;
using SlopArena.Shared;

var root = args.Single();
Console.WriteLine($"Embedded schema: {CharacterSourceSchema.Objects.Count} objects");
var folder = Path.Combine(root, "content-cooked/fightguy");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
string ReadManifestProperty(string name) => manifest.RootElement.GetProperty(name).GetString()!;
var requirement = new MatchContentPackageRequirement(ReadManifestProperty("packageId"), ReadManifestProperty("version"), ReadManifestProperty("cookedContentHash"), ReadManifestProperty("packageHash"));
var loaded = CookedCharacterPackageLoader.LoadDirectory(folder, requirement);
foreach (var diagnostic in loaded.Diagnostics) Console.WriteLine(diagnostic);
if (!loaded.IsValid) return 1;
Console.WriteLine("Cooked fighter loaded: " + loaded.Package!.Metadata.PackageId);
return 0;
