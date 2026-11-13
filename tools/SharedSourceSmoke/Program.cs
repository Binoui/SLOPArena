using SlopArena.Shared;

var root = args.Single();
Console.WriteLine($"Embedded schema: {CharacterSourceSchema.Objects.Count} objects");
var roster = BuiltInRosterManifestCodec.Load(Path.Combine(root, "content-cooked/roster/manifest.json"));
foreach (var entry in roster.Entries)
{
    var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(root, "content-cooked", entry.PackageId), entry.Requirement);
    foreach (var diagnostic in loaded.Diagnostics) Console.WriteLine(diagnostic);
    if (!loaded.IsValid) return 1;
    Console.WriteLine("Cooked fighter loaded: " + loaded.Package!.Metadata.PackageId);
}
return 0;
