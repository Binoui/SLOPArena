using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record MatchContentHandleRecord(ContentHandle Handle, CharacterClass Selector, MatchContentIdentity Identity, string DisplayName);

public sealed class MatchContentHandleMap
{
    public const ushort CatalogSchemaVersion = 1;
    public const ushort CurrentSchemaVersion = 2;
    public ushort SchemaVersion { get; }
    public IReadOnlyList<MatchContentHandleRecord> Entries { get; }
    public MatchArenaRecord? Arena { get; }
    public MatchContentHandleMap(ushort schemaVersion, IReadOnlyList<MatchContentHandleRecord> entries, MatchArenaRecord? arena = null)
    {
        if (schemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (arena != null && schemaVersion != CurrentSchemaVersion) throw new ArgumentException("Arena identity requires match content schema 2.", nameof(schemaVersion));
        var copy = entries.OrderBy(x => x.Handle.Value).ToList();
        if (copy.Any(x => !x.Handle.IsValid) || copy.Select(x => x.Handle.Value).Distinct().Count() != copy.Count)
            throw new ArgumentException("Handle map contains an invalid or duplicate handle.", nameof(entries));
        SchemaVersion = schemaVersion;
        Entries = new ReadOnlyCollection<MatchContentHandleRecord>(copy);
        Arena = arena;
    }
    public MatchContentHandleMap WithArena(MatchArenaRecord arena) => new(CurrentSchemaVersion, Entries, arena ?? throw new ArgumentNullException(nameof(arena)));
    public MatchContentHandleRecord? Resolve(ContentHandle handle) => Entries.FirstOrDefault(x => x.Handle == handle);
}

public static class MatchContentHandleMapCodec
{
    public static string Serialize(MatchContentHandleMap map)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        using var stream = new System.IO.MemoryStream(); using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject(); writer.WriteNumber("schemaVersion", map.SchemaVersion); writer.WritePropertyName("entries"); writer.WriteStartArray();
        foreach (var x in map.Entries.OrderBy(x => x.Handle.Value))
        {
            writer.WriteStartObject(); writer.WriteNumber("handle", x.Handle.Value); writer.WriteString("selector", x.Selector.ToString()); writer.WritePropertyName("identity"); writer.WriteStartObject(); writer.WriteString("packageId", x.Identity.PackageId); writer.WriteString("version", x.Identity.Version); writer.WriteString("sourceHash", x.Identity.SourceHash); writer.WriteString("cookedContentHash", x.Identity.CookedContentHash); writer.WriteString("packageHash", x.Identity.PackageHash); writer.WriteEndObject(); writer.WriteString("displayName", x.DisplayName); writer.WriteEndObject();
        }
        writer.WriteEndArray();
        if (map.Arena is { } arena)
        {
            writer.WritePropertyName("arena"); writer.WriteStartObject();
            writer.WriteString("name", arena.Name); writer.WriteString("digest", arena.Digest); writer.WriteEndObject();
        }
        writer.WriteEndObject(); writer.Flush(); return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static bool TryParse(string json, out MatchContentHandleMap? map)
    {
        map = null; try { using var doc = JsonDocument.Parse(json); return TryParse(doc.RootElement, out map); } catch { return false; }
    }
    public static bool TryParse(JsonElement element, out MatchContentHandleMap? map)
    {
        map = null;
        try
        {
            if (element.ValueKind != JsonValueKind.Object) return false;
            bool hasArena = element.TryGetProperty("arena", out var arenaElement);
            if (!HasOnly(element, hasArena ? new[] { "schemaVersion", "entries", "arena" } : new[] { "schemaVersion", "entries" })) return false;
            if (!element.TryGetProperty("schemaVersion", out var sv) || !sv.TryGetUInt16(out var schema) || (schema != MatchContentHandleMap.CatalogSchemaVersion && schema != MatchContentHandleMap.CurrentSchemaVersion) || !element.TryGetProperty("entries", out var array) || array.ValueKind != JsonValueKind.Array) return false;
            MatchArenaRecord? arena = null;
            if (hasArena)
            {
                if (schema != MatchContentHandleMap.CurrentSchemaVersion || !HasOnly(arenaElement, "name", "digest")) return false;
                arena = new MatchArenaRecord(Required(arenaElement, "name"), Required(arenaElement, "digest"));
            }
            var entries = new List<MatchContentHandleRecord>(); var handles = new HashSet<ushort>(); var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array.EnumerateArray())
            {
                if (!HasOnly(item, "handle", "selector", "identity", "displayName")) return false;
                if (item.ValueKind != JsonValueKind.Object || !TryGetUShort(item,"handle",out var value) || value == 0 || !handles.Add(value) || !item.TryGetProperty("selector",out var selector) || selector.ValueKind != JsonValueKind.String || !TrySelector(selector.GetString()!,out var cls) || !item.TryGetProperty("displayName",out var display) || display.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(display.GetString())) return false;
                if (!item.TryGetProperty("identity",out var id) || id.ValueKind != JsonValueKind.Object) return false;
                if (!HasOnly(id, "packageId", "version", "sourceHash", "cookedContentHash", "packageHash")) return false;
                string package = Required(id,"packageId"), version = Required(id,"version"), source = Required(id,"sourceHash"), cooked = Required(id,"cookedContentHash"), packageHash = Required(id,"packageHash");
                if (!MatchContentCatalogBuilder.IsStablePackageId(package) || string.IsNullOrWhiteSpace(version) || !MatchContentCatalogBuilder.IsSha(source) || !MatchContentCatalogBuilder.IsSha(cooked) || !MatchContentCatalogBuilder.IsSha(packageHash) || !identities.Add(package+"\n"+version+"\n"+source+"\n"+cooked+"\n"+packageHash)) return false;
                entries.Add(new MatchContentHandleRecord(new ContentHandle(value), cls, new MatchContentIdentity(package,version,source,cooked,packageHash), display.GetString()!));
            }
            map = new MatchContentHandleMap(schema, entries, arena); return true;
        }
        catch { map = null; return false; }
    }
    public static bool TryParse(JsonElement element, MatchContentCatalog catalog, out MatchContentHandleMap? map)
    {
        if (!TryParse(element, out map) || catalog == null || map == null) return false;
        foreach (var record in map.Entries)
        {
            var entry = catalog.Resolve(record.Handle);
            if (entry == null || entry.LegacySelector != record.Selector || entry.Identity != record.Identity || entry.DisplayName != record.DisplayName) { map = null; return false; }
        }
        return map.Entries.Count == catalog.Entries.Count;
    }
    private static bool TryGetUShort(JsonElement e,string n,out ushort v){v=0;return e.TryGetProperty(n,out var p)&&p.TryGetUInt16(out v);}
    private static string Required(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw new InvalidDataException();
    private static bool HasOnly(JsonElement e,params string[] fields){if(e.ValueKind!=JsonValueKind.Object)return false;var allowed=new HashSet<string>(fields,StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!allowed.Contains(p.Name)||!seen.Add(p.Name))return false;return seen.Count==allowed.Count;}
    private static bool TrySelector(string value,out CharacterClass cls){cls=value switch{"Manki"=>CharacterClass.Manki,"manki"=>CharacterClass.Manki,"FightGuy"=>CharacterClass.FightGuy,"fightguy"=>CharacterClass.FightGuy,"Wibou"=>CharacterClass.Wibou,"wibou"=>CharacterClass.Wibou,"Bonk"=>CharacterClass.Bonk,"bonk"=>CharacterClass.Bonk,"Bashie"=>CharacterClass.Bashie,"bashie"=>CharacterClass.Bashie,"Salamander"=>CharacterClass.Salamander,"salamander"=>CharacterClass.Salamander,"Nyx"=>CharacterClass.Nyx,"nyx"=>CharacterClass.Nyx,_=>CharacterClass.None};return cls!=CharacterClass.None;}
}
