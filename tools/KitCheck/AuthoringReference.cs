using System.Globalization;
using System.Text;
using SlopArena.Shared;

namespace SlopArena.KitCheck;

public static class AuthoringReference
{
    public const string Start = "<!-- BEGIN GENERATED CONTRACT -->";
    public const string End = "<!-- END GENERATED CONTRACT -->";

    public static string Generate()
    {
        var text = new StringBuilder(Start + "\n\n## Source contract\n\n");
        text.AppendLine("Generated from the public `CharacterSourceSchema` definitions used by the Shared source reader. Regenerate with `dotnet kitcheck/KitCheck.dll reference --write <repository-root>`.\n");
        text.AppendLine("`Required` means omission is an error. Optional fields show their JSON default. Empty text and null are different. A null omission default does not permit a literal JSON null unless the type column explicitly says so. An optional containing object can be omitted; its required children apply only when that object is present.\n");
        text.AppendLine("The range column gives storage limits, enum tokens, and shared scalar rules. A ushort stores 0 through 65535, a byte 0 through 255, and an integer -2147483648 through 2147483647. Shared scalar rules refine these storage limits at the compiler validation step. Successful cooking also requires the gameplay and lifecycle constraints described below the tables. Those coupled constraints remain in the existing compiler and are explicitly documented by hand.\n");
        text.AppendLine("### Canonical move grid\n");
        text.AppendLine(string.Join(", ", CharacterPackageCompiler.CanonicalSlotIds.Select(x => "`" + x + "`")) + ".\n");
        text.AppendLine("### Public capabilities\n\n| ID | Version | Parameter schema |\n| --- | --- | --- |");
        foreach (var schema in CharacterSourceSchema.Objects.Where(x => x.CapabilityId != null))
            text.AppendLine($"| `{schema.CapabilityId}` | `{schema.CapabilityVersion}` | [{schema.Title}](#{Anchor(schema.Id)}) |");
        foreach (var feature in CharacterSourceSchema.Features)
            text.AppendLine($"| `{feature.Id}` | `{feature.Version}` | {feature.Description} |");
        text.AppendLine("\n### Budgets\n\n| Limit | Value |\n| --- | --- |");
        foreach (var limit in typeof(CookedBudget).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(x => x.IsLiteral))
            text.AppendLine($"| {limit.Name} | {limit.GetRawConstantValue()} |");
        text.AppendLine($"\nEvery operation costs {CharacterSourceSchema.OperationEntryCost} operation entry. Additional counters are listed with each operation and capability schema below. Parameter counters add to the enclosing startCapability counters.\n");
        text.AppendLine("\n### Operation contracts\n\nEach operation has a required `kind`, zero-based stage `tick`, and `unit`. Same-tick entries run in authored order.\n");
        foreach (var schema in CharacterSourceSchema.Objects)
        {
            text.AppendLine($"<a id=\"{Anchor(schema.Id)}\"></a>\n\n### {schema.Title}\n");
            text.AppendLine($"Location: `{schema.Path}`.\n");
            if (schema.OperationUnit is { } unit)
                text.AppendLine($"Operation unit: `{Token(unit.ToString())}`.\n");
            if (schema.OperationKind != null || schema.CapabilityId != null)
                text.AppendLine($"Additional budget counters: {schema.HitboxCost} hitboxes, {schema.ProjectileCost} projectiles, {schema.CapabilityCost} capability starts."
                    + (schema.ConditionalHitboxField is { } h ? $" Add one hitbox when `{h}` is present." : "")
                    + (schema.ProjectileUnlessField is { } p ? $" Add one projectile when `{p}` is absent." : "") + "\n");
            text.AppendLine("| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |\n| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var field in schema.Fields)
            {
                var type = field.ChildSchemaId is { } child && child is not ("operation" or "capability")
                    ? $"{field.Type} of [{CharacterSourceSchema.Object(child).Title}](#{Anchor(child)})" : field.Type;
                if (field.AllowsNull) type += " or null";
                var rule = field.RangeRuleId != null ? CharacterSourceSchema.Range(field.RangeRuleId) : null;
                var requirement = field.Required ? "Required" : field.RequiredWhen != null ? "Required when " + field.RequiredWhen.Replace(":absent", " is absent").Replace(":zero", " is zero or absent") + "; otherwise `" + field.DefaultJson + "`" : "`" + field.DefaultJson + "`";
                var range = field.ExpectedJson != null ? "Exactly `" + field.ExpectedJson + "`" : field.GameplayMinimum.HasValue || field.GameplayMaximum.HasValue ? (field.GameplayMinimum.HasValue ? (field.ExclusiveGameplayMinimum ? "greater than " : "at least ") + Number(field.GameplayMinimum) : "") + (field.GameplayMaximum.HasValue ? "; at most " + Number(field.GameplayMaximum) : "") : rule != null ? (rule.ExclusiveMinimum ? "greater than " : "at least ") + Number(rule.Minimum) + (rule.Maximum.HasValue ? ", at most " + Number(rule.Maximum) : "") : field.AllowedValues.Count > 0 ? string.Join(", ", field.AllowedValues.Select(x => "`" + x + "`"))
                    : field.Minimum.HasValue || field.Maximum.HasValue ? $"{Number(field.Minimum)} to {Number(field.Maximum)}"
                    : field.Type == "number" ? "32-bit float; see gameplay constraints" : "See meaning and gameplay constraints";
                text.AppendLine($"| `{field.Name}` | {type} | {requirement} | {field.Unit} | {range} | {field.Description} | `{field.MissingCode}`, `{field.InvalidCode}`{(field.ConstraintCode != null ? ", `" + field.ConstraintCode + "`" : "")} |");
            }
            text.AppendLine();
        }
        text.AppendLine("### Bone identifiers\n");
        text.AppendLine(string.Join(", ", CharacterBoneRegistry.HumanoidPoints.Select(x => "`" + x.Id + "`")) + ". `bone.spine` also resolves to `bone.spine2`.\n");
        text.AppendLine(End);
        return text.ToString().Replace("\r\n", "\n");
    }
    private static string Number(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unbounded";
    private static string Anchor(string id) => "schema-" + id.Replace('.', '-');
    private static string Token(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
