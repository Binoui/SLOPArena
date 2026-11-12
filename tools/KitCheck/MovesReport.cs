using System.Globalization;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.KitCheck;

public enum MoveContactKind { Ordinary, Capture }
public sealed record MoveContact(string Path, float Damage, sbyte Angle, float BaseKnockback, float Growth, ushort StunGate, ushort FixedStun, MoveContactKind Kind = MoveContactKind.Ordinary);
public sealed record MoveOutcome(float Speed, ushort Hitstun);

public static class MovesReport
{
    public static MoveOutcome Evaluate(MoveContact hit, ushort percentBefore)
    {
        if (hit.Kind == MoveContactKind.Capture) return new MoveOutcome(0f, 0);
        var state = new CharacterState { DamagePercent = percentBefore };
        state.DamagePercent += (ushort)hit.Damage;
        if (state.DamagePercent > 999) state.DamagePercent = 999;
        Simulation.ApplyKnockback(ref state, 0f, 1f, hit.Angle, hit.BaseKnockback, hit.Growth, hit.Damage, hit.StunGate, 100f);
        Simulation.ApplyFixedHitstun(ref state, hit.FixedStun, hit.StunGate);
        return new MoveOutcome(MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ), state.HitstunTicks);
    }

    public static IEnumerable<MoveContact> Contacts(JsonElement value, string path = "character", string? capabilityId = null)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("capabilityId", out var capability)) capabilityId = capability.GetString();
            if (value.TryGetProperty("damage", out var damage) && value.TryGetProperty("angle", out var angle)
                && value.TryGetProperty("baseKnockback", out var basis) && value.TryGetProperty("knockbackGrowth", out var growth)
                && value.TryGetProperty("stunTicks", out var stun))
                yield return new MoveContact(path, damage.GetSingle(), (sbyte)angle.GetSingle(), basis.GetSingle(), growth.GetSingle(), stun.GetUInt16(),
                    value.TryGetProperty("fixedHitstunTicks", out var fixedStun) ? fixedStun.GetUInt16() : (ushort)0,
                    path.EndsWith(".capture", StringComparison.Ordinal) || capabilityId == CharacterPackageCompiler.TetherCapabilityId && path.EndsWith(".projectile", StringComparison.Ordinal)
                        ? MoveContactKind.Capture : MoveContactKind.Ordinary);
            else if (capabilityId == CharacterPackageCompiler.TetherCapabilityId && value.TryGetProperty("damage", out damage) && value.TryGetProperty("catchTicks", out _))
                yield return new MoveContact(path, damage.GetSingle(), 0, 0, 0, 0, 0, MoveContactKind.Capture);
            foreach (var property in value.EnumerateObject())
                foreach (var hit in Contacts(property.Value, path + "." + property.Name, capabilityId)) yield return hit;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in value.EnumerateArray())
            {
                string key = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String ? id.GetString()! : index.ToString(CultureInfo.InvariantCulture);
                foreach (var hit in Contacts(item, path + "[" + key + "]", capabilityId)) yield return hit;
                index++;
            }
        }
    }

    public static void Write(CookedCharacterPackage package, TextWriter output)
    {
        using var document = JsonDocument.Parse(package.CanonicalBytes);
        output.WriteLine("Weight 100, default combat tuning. Percent columns are BEFORE the hit. Speed is m/s; stun is ticks.");
        output.WriteLine("Single ordinary contact estimates. Charge tiers, captures, held hits and capability hooks may replace these results; verify those moves in play.");
        output.WriteLine("Hitbox | Damage | 0% speed/stun | 100% speed/stun | 150% speed/stun");
        foreach (var hit in Contacts(document.RootElement.GetProperty("character")))
        {
            string Cell(ushort percent)
            {
                if (hit.Kind == MoveContactKind.Capture) return "hold; no ordinary launch";
                var outcome = Evaluate(hit, percent);
                return outcome.Speed.ToString("0.###", CultureInfo.InvariantCulture) + "/" + outcome.Hitstun;
            }
            output.WriteLine(hit.Path + " | " + hit.Damage.ToString("0.###", CultureInfo.InvariantCulture) + " | " + Cell(0) + " | " + Cell(100) + " | " + Cell(150));
        }
    }
}
