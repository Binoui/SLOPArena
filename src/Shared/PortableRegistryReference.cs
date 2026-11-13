using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace SlopArena.Shared;

internal static class PortableRegistryReference
{
    private static readonly Regex SemVer = new(@"^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$", RegexOptions.CultureInvariant);
    private static readonly Regex Id = new(@"^slop\.(anim|vfx|sfx)\.[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*\.v[1-9][0-9]{0,5}$", RegexOptions.CultureInvariant);
    internal static bool Version(string version) => version.Length <= 32 && SemVer.IsMatch(version);
    internal static void Validate(JsonElement reference, string family)
    {
        PortableFighterBindings.Object(reference, "id", "version");
        string id = reference.GetProperty("id").GetString() ?? "", version = reference.GetProperty("version").GetString() ?? "";
        if (id.Length > 256 || !Id.IsMatch(id) || !id.StartsWith("slop." + family + ".", System.StringComparison.Ordinal)
            || !Version(version) || id.Split('.').Last().Substring(1) != version.Split('.')[0])
            throw new InvalidDataException("Invalid engine registry ID or exact version; contract major must match .vN.");
    }
}
