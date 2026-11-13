using System.Globalization;
using System.Text;
using System.Text.Json;
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
        text.AppendLine("`Required` means omission is an error. Optional fields show a writable JSON default or an instruction to omit the field. Empty text and null are different. A null omission default does not permit a literal JSON null unless the type column explicitly says so. An optional containing object can be omitted; its required children apply only when that object is present.\n");
        text.AppendLine("The range column gives storage limits, enum tokens, and shared scalar rules. A ushort stores 0 through 65535, a byte 0 through 255, and an integer -2147483648 through 2147483647. Shared scalar rules refine these storage limits at the compiler validation step. Successful cooking also requires the gameplay and lifecycle constraints described below the tables. Those coupled constraints remain in the existing compiler and are explicitly documented by hand.\n");
        AppendRosterAndTraining(text);
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
                string DefaultText() => field.HasWritableDefault ? "`" + field.DefaultJson + "`" : "Omit; no writable JSON default";
                var requirement = field.Required ? "Required" : field.RequiredWhen != null
                    ? "Required when " + field.RequiredWhen.Replace(":absent", " is absent").Replace(":zero", " is zero or absent")
                        + (field.ForbiddenWhen != null ? "; forbidden when " + field.ForbiddenWhen.Replace(":present", " is present") : "; otherwise " + DefaultText())
                    : DefaultText();
                var range = FieldRange(field, rule);
                if (schema.ConstraintAnchor is { } constraint) range += " [Relationships](#" + constraint + ").";
                text.AppendLine($"| `{field.Name}` | {type} | {requirement} | {field.Unit} | {range} | {field.Description} | `{field.MissingCode}`, `{field.InvalidCode}`{(field.ConstraintCode != null ? ", `" + field.ConstraintCode + "`" : "")} |");
            }
            text.AppendLine();
        }
        text.AppendLine("### cook.json\n\nGenerated from `FighterCookDocument.Fields`, the schema used by the game and KitCheck. Unknown fields and duplicate keys are errors. Null is allowed only where explicitly listed.\n\n| Field | Type | Required | Meaning |\n| --- | --- | --- | --- |");
        foreach (var field in FighterCookDocument.Fields)
            text.AppendLine($"| `{field.Name}` | {field.Type} | {(field.Required ? "Yes" : "No")} | {field.Description} |");
        text.AppendLine("\nOnly declared files are read as payloads. Unreferenced PNG and JPEG files are ignored. The optional standard bone map is `humanoid.json`, limited to 64 KiB. Image filenames must end in .png or .jpg. Animation IDs must match the kit exactly. `KitCheck cook <source-folder> [glb]` checks this schema and, when a GLB exists or is supplied, its clip names. Icons and portraits follow the form counts and canonical IDs of portable bindings.\n");
        text.AppendLine("### Bone identifiers\n");
        text.AppendLine(string.Join(", ", CharacterBoneRegistry.HumanoidPoints.Select(x => "`" + x.Id + "`")) + ". `bone.spine` also resolves to `bone.spine2`.\n");
        text.AppendLine(End);
        return text.ToString().Replace("\r\n", "\n");
    }
    private static string FieldRange(CharacterSourceField field, CharacterSourceRange? rule)
    {
        if (field.ExpectedJson != null) return "Exactly `" + field.ExpectedJson + "`";
        if (field.AllowedValues.Count > 0) return string.Join(", ", field.AllowedValues.Select(x => "`" + x + "`"));
        string Bounds(double? min, double? max, bool exclusive) =>
            (min.HasValue ? (exclusive ? "greater than " : "at least ") + Number(min) : "no lower gameplay limit")
            + (max.HasValue ? ", at most " + Number(max) : ", no upper gameplay limit");
        string range = field.GameplayMinimum.HasValue || field.GameplayMaximum.HasValue
            ? Bounds(field.GameplayMinimum, field.GameplayMaximum, field.ExclusiveGameplayMinimum)
            : rule != null ? Bounds(rule.Minimum, rule.Maximum, rule.ExclusiveMinimum)
            : field.Minimum.HasValue || field.Maximum.HasValue ? $"{Number(field.Minimum)} to {Number(field.Maximum)}"
            : field.Type == "number" ? "Finite 32-bit number; no additional scalar limit"
            : field.Type == "string" ? "Text; naming and reference rules apply"
            : field.Type == "boolean" ? "`true` or `false`" : "Child fields and array budgets apply";
        if ((field.GameplayMinimum.HasValue || field.GameplayMaximum.HasValue || rule != null) && field.Minimum.HasValue && field.Maximum.HasValue)
            range += $"; storage {Number(field.Minimum)} to {Number(field.Maximum)}";
        if (field.WholeNumber) range += "; whole numbers only";
        foreach (var context in field.ContextRanges)
            range += "; " + context.Context + ": " + Bounds(context.Minimum, context.Maximum, context.ExclusiveMinimum);
        return range;
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(AuthoringReference).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing reference input " + name);
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    private static void AppendRosterAndTraining(StringBuilder text)
    {
        text.AppendLine("### Roster and Training reference points\n\nGenerated from the committed roster, its cooked fighters, the Training arena, and simulation constants. These are reference values, not compiler balance limits.\n");
        text.AppendLine("| Built-in fighter | Weight |\n| --- | --- |");
        using var roster = JsonDocument.Parse(Resource("SlopArena.KitCheck.RosterManifest"));
        foreach (var entry in roster.RootElement.GetProperty("entries").EnumerateArray().OrderBy(x => x.GetProperty("packageId").GetString(), StringComparer.Ordinal))
        {
            string id = entry.GetProperty("packageId").GetString()!;
            using var fighter = JsonDocument.Parse(Resource("SlopArena.KitCheck.Roster." + id + "/character.runtime.json"));
            text.AppendLine("| " + id + " | " + fighter.RootElement.GetProperty("character").GetProperty("weight").GetSingle().ToString(CultureInfo.InvariantCulture) + " |");
        }
        var arena = ArenaBinaryFormat.Deserialize(Resource("SlopArena.KitCheck.TrainingArena")) ?? throw new InvalidDataException("Training arena is invalid.");
        var lines = ArenaCollision.ResolveBlastLines(arena);
        text.AppendLine("\n| Training world coordinate | Blast line in meters |\n| --- | --- |");
        foreach (var (name, value) in new[] { ("Minimum X", lines.KillMinX), ("Maximum X", lines.KillMaxX), ("Minimum Z", lines.KillMinZ), ("Maximum Z", lines.KillMaxZ), ("Bottom Y", lines.KillHeight), ("Top Y", lines.KillTop) })
            text.AppendLine("| " + name + " | " + value.ToString(CultureInfo.InvariantCulture) + " |");
        text.AppendLine($"\nLaunch pop lasts {Simulation.KnockbackPopTicks} ticks for upward launches. Flight gravity is {Simulation.FlightGravity.ToString(CultureInfo.InvariantCulture)} m/s². Neutral flight braking is {Simulation.FlightFriction.ToString(CultureInfo.InvariantCulture)} m/s²; opposing input can raise it to {Simulation.HeldFlightBrake.ToString(CultureInfo.InvariantCulture)} m/s². Settled crouch multiplies ordinary launch velocity by {DownActionTuning.Default.CrouchLaunchMultiplier.ToString(CultureInfo.InvariantCulture)} without changing hitstun.\n");
    }
    private static string Number(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unbounded";
    private static string Anchor(string id) => "schema-" + id.Replace('.', '-');
    private static string Token(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
