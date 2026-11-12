using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace SlopArena.Shared;

/// <summary>A source field's JSON contract. Gameplay constraints involving several fields remain compiler-owned.</summary>
public sealed class CharacterSourceField
{
    public string Name { get; }
    public string Type { get; }
    public bool Required { get; }
    public string? RequiredWhen { get; }
    public string? DefaultJson { get; }
    public bool AllowsNull { get; }
    public string? ExpectedJson { get; }
    public string? ConstraintCode { get; }
    public string Unit { get; }
    public string Description { get; }
    public string? ChildSchemaId { get; }
    public string MissingCode { get; }
    public string InvalidCode { get; }
    public double? GameplayMinimum { get; }
    public double? GameplayMaximum { get; }
    public bool ExclusiveGameplayMinimum { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public IReadOnlyList<string> AllowedValues { get; }
    public string? RangeRuleId { get; }

    internal CharacterSourceField(XElement field)
    {
        string RequiredAttribute(string name) => (string?)field.Attribute(name)
            ?? throw new InvalidOperationException("Source schema field lacks " + name + ".");
        Name = RequiredAttribute("name"); Type = RequiredAttribute("type");
        Required = bool.Parse(RequiredAttribute("required"));
        RequiredWhen = (string?)field.Attribute("requiredWhen");
        DefaultJson = (string?)field.Attribute("default");
        AllowsNull = (bool?)field.Attribute("allowsNull") ?? false;
        ExpectedJson = (string?)field.Attribute("expected");
        ConstraintCode = (string?)field.Attribute("constraintCode");
        Unit = RequiredAttribute("unit"); Description = RequiredAttribute("description");
        if (string.IsNullOrWhiteSpace(Description)) throw new InvalidOperationException("Undocumented source field " + Name);
        ChildSchemaId = (string?)field.Attribute("child");
        MissingCode = RequiredAttribute("missingCode"); InvalidCode = RequiredAttribute("invalidCode");
        Minimum = Number("min"); Maximum = Number("max");
        GameplayMinimum = Number("gameMin"); GameplayMaximum = Number("gameMax");
        ExclusiveGameplayMinimum = (bool?)field.Attribute("exclusiveGameMin") ?? false;
        RangeRuleId = (string?)field.Attribute("rangeRule");
        AllowedValues = Array.AsReadOnly(((string?)field.Attribute("values") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        double? Number(string name) => field.Attribute(name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : null;
        if (Required == (DefaultJson != null)) throw new InvalidOperationException("Source field must be required or have a default: " + Name);
    }

    public bool IsRequired(JsonElement containingObject)
        => Required || RequiredWhen != null && Condition(containingObject.TryGetProperty(RequiredWhen.Split(':')[0], out var value), value);

    internal bool IsRequired(IReadOnlyDictionary<string, JsonElement> properties)
        => Required || RequiredWhen != null && Condition(properties.TryGetValue(RequiredWhen.Split(':')[0], out var value), value);

    private bool Condition(bool present, JsonElement value) => RequiredWhen!.Split(':')[1] switch
    {
        "absent" => !present,
        "zero" => !present || value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number == 0,
        _ => throw new InvalidOperationException("Unknown source field condition " + RequiredWhen),
    };

    public ushort ExpectedUnsignedInteger => ushort.Parse(ExpectedJson ?? throw new InvalidOperationException("Field has no expected integer."), CultureInfo.InvariantCulture);

    public bool IsOutsideGameplay(double value)
        => GameplayMinimum.HasValue && (ExclusiveGameplayMinimum ? value <= GameplayMinimum.Value : value < GameplayMinimum.Value)
            || GameplayMaximum.HasValue && value > GameplayMaximum.Value;

    public bool IsWithinGameplay(double value)
        => (!GameplayMinimum.HasValue || (ExclusiveGameplayMinimum ? value > GameplayMinimum.Value : value >= GameplayMinimum.Value))
            && (!GameplayMaximum.HasValue || value <= GameplayMaximum.Value);

    public bool AcceptsNumber(double value) => (!Minimum.HasValue || value >= Minimum.Value) && (!Maximum.HasValue || value <= Maximum.Value);

    internal JsonElement DefaultValue()
    {
        using var document = JsonDocument.Parse(DefaultJson ?? "null");
        return document.RootElement.Clone();
    }

    internal bool AcceptsType(JsonElement value) => AllowsNull && value.ValueKind == JsonValueKind.Null || Type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "enum" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out _),
        "ushort" or "byte" or "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && AcceptsNumber(number),
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        _ => throw new InvalidOperationException("Unknown source field type " + Type),
    };
}

public sealed class CharacterSourceObject
{
    public string Id { get; }
    public string Title { get; }
    public string Path { get; }
    public string? OperationKind { get; }
    public string? CapabilityId { get; }
    public string? CapabilityVersion { get; }
    public AuthoringUnit? OperationUnit { get; }
    public int HitboxCost { get; }
    public int ProjectileCost { get; }
    public int CapabilityCost { get; }
    public string? ConditionalHitboxField { get; }
    public string? ProjectileUnlessField { get; }
    public IReadOnlyList<CharacterSourceField> Fields { get; }
    private readonly IReadOnlyDictionary<string, CharacterSourceField> _fields;

    internal CharacterSourceObject(XElement element)
    {
        Id = (string)element.Attribute("id")!; Title = (string)element.Attribute("title")!;
        Path = (string)element.Attribute("path")!;
        OperationKind = (string?)element.Attribute("operation");
        HitboxCost = (int?)element.Attribute("hitboxCost") ?? 0;
        ProjectileCost = (int?)element.Attribute("projectileCost") ?? 0;
        CapabilityCost = (int?)element.Attribute("capabilityCost") ?? 0;
        ConditionalHitboxField = (string?)element.Attribute("conditionalHitbox");
        ProjectileUnlessField = (string?)element.Attribute("projectileUnless");
        CapabilityId = (string?)element.Attribute("capability"); CapabilityVersion = (string?)element.Attribute("version");
        if (element.Attribute("unit") is { } unit) OperationUnit = (AuthoringUnit)Enum.Parse(typeof(AuthoringUnit), unit.Value, true);
        Fields = Array.AsReadOnly(element.Elements("field").Select(x => new CharacterSourceField(x)).ToArray());
        _fields = new ReadOnlyDictionary<string, CharacterSourceField>(Fields.ToDictionary(x => x.Name, StringComparer.Ordinal));
    }

    public CharacterSourceField? FindField(string name) => _fields.TryGetValue(name, out var field) ? field : null;
    public CharacterSourceField Field(string name) => FindField(name) ?? throw new ArgumentException("Unknown field " + Id + "." + name);
}

public sealed class CharacterSourceRange
{
    public string Id { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public bool ExclusiveMinimum { get; }
    internal CharacterSourceRange(XElement element)
    {
        Id = (string)element.Attribute("id")!;
        Minimum = Parse("min"); Maximum = Parse("max");
        ExclusiveMinimum = (bool?)element.Attribute("exclusiveMin") ?? false;
        double? Parse(string name) => string.IsNullOrEmpty((string?)element.Attribute(name)) ? null
            : double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);
    }
    // Separate from finite checks so existing diagnostic multiplicity is preserved for typed input.
    public bool IsOutside(double value) => Minimum.HasValue && (ExclusiveMinimum ? value <= Minimum.Value : value < Minimum.Value)
        || Maximum.HasValue && value > Maximum.Value;
}

public sealed record CharacterSourceFeature(string Id, string Version, string Description);

/// <summary>Read-only Workshop source definitions shared by the codec, compiler, reference and future editor clients.</summary>
public static class CharacterSourceSchema
{
    public static IReadOnlyList<CharacterSourceObject> Objects { get; }
    public static int OperationEntryCost { get; }
    public static IReadOnlyList<CharacterSourceFeature> Features { get; }
    public static IReadOnlyList<CharacterSourceRange> Ranges { get; }
    private static readonly IReadOnlyDictionary<string, CharacterSourceObject> ById;
    private static readonly IReadOnlyDictionary<string, CharacterSourceObject> ByOperation;
    private static readonly IReadOnlyDictionary<string, CharacterSourceObject> ByCapability;
    private static readonly IReadOnlyDictionary<string, CharacterSourceRange> ByRange;

    static CharacterSourceSchema()
    {
        using var stream = typeof(CharacterSourceSchema).Assembly.GetManifestResourceStream("SlopArena.Shared.CharacterSourceSchema.xml")
            ?? throw new InvalidOperationException("Embedded source schema is missing.");
        var root = XDocument.Load(stream).Root!;
        OperationEntryCost = (int)root.Attribute("operationEntryCost")!;
        Ranges = Array.AsReadOnly(root.Elements("range").Select(x => new CharacterSourceRange(x)).ToArray());
        Objects = Array.AsReadOnly(root.Elements("object").Select(x => new CharacterSourceObject(x)).ToArray());
        Features = Array.AsReadOnly(root.Elements("feature").Select(x => new CharacterSourceFeature(
            (string)x.Attribute("id")!, (string)x.Attribute("version")!, (string)x.Attribute("description")!)).ToArray());
        ById = new ReadOnlyDictionary<string, CharacterSourceObject>(Objects.ToDictionary(x => x.Id, StringComparer.Ordinal));
        ByOperation = new ReadOnlyDictionary<string, CharacterSourceObject>(Objects.Where(x => x.OperationKind != null).ToDictionary(x => x.OperationKind!, StringComparer.Ordinal));
        ByCapability = new ReadOnlyDictionary<string, CharacterSourceObject>(Objects.Where(x => x.CapabilityId != null).ToDictionary(x => x.CapabilityId!, StringComparer.Ordinal));
        ByRange = new ReadOnlyDictionary<string, CharacterSourceRange>(Ranges.ToDictionary(x => x.Id, StringComparer.Ordinal));
        foreach (var field in Objects.SelectMany(x => x.Fields))
            if (field.ChildSchemaId is { } child && child is not ("operation" or "capability") && !ById.ContainsKey(child))
                throw new InvalidOperationException("Unknown child source schema " + child);
    }

    internal static (int Hitboxes, int Projectiles) CapabilityBudget(TypedCapabilityParameters parameters)
    {
        string? id = parameters switch
        {
            TargetedLeapCapabilityParameters => "cap.targeted-leap.v2",
            SteeredChargeCapabilityParameters => "cap.steered-charge.v2",
            ThrustCapabilityParameters => "cap.thrust.v1",
            DashStrikeCapabilityParameters => "cap.dash-strike.v1",
            FlashCutCapabilityParameters => "cap.flash-cut.v1",
            CommandGrabCapabilityParameters => "cap.command-grab.v1",
            ChargedProjectileCapabilityParameters => "cap.charged-projectile.v1",
            TetherCapabilityParameters => "cap.tether.v1",
            _ => null,
        };
        if (id == null) return (0, 0);
        var schema = Object(id);
        bool Present(string field) => field switch
        {
            "trailHitbox" => parameters is ThrustCapabilityParameters { TrailHitbox: not null },
            "atTarget" => parameters is TetherCapabilityParameters { AtTarget: not null },
            _ => throw new InvalidOperationException("Unknown conditional budget field " + field),
        };
        return (schema.HitboxCost + (schema.ConditionalHitboxField is { } h && Present(h) ? 1 : 0),
            schema.ProjectileCost + (schema.ProjectileUnlessField is { } p && !Present(p) ? 1 : 0));
    }

    public static CharacterSourceRange Range(string id) => ByRange.TryGetValue(id, out var range) ? range : throw new ArgumentException("Unknown source range " + id);
    public static CharacterSourceObject Object(string id) => ById.TryGetValue(id, out var schema) ? schema : throw new ArgumentException("Unknown source schema " + id);
    public static CharacterSourceObject? Operation(string kind) => ByOperation.TryGetValue(kind, out var schema) ? schema : null;
    public static CharacterSourceObject? Capability(string id) => id != null && ByCapability.TryGetValue(id, out var schema) ? schema : null;
    public static bool IsPublicCapability(string id, string version) => Capability(id) is { } schema && schema.CapabilityVersion == version || Features.Any(x => x.Id == id && x.Version == version);
}
