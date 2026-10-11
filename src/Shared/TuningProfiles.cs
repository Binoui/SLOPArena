using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

/// <summary>
/// Named knockback-tuning profiles (issue #149). One table shared by the diagnostic tools
/// (MoveDataReport --kbm, AbDiffReport --base/--cand) and the tests, so an A/B diff always
/// compares the same tuning vocabulary. Resolving a profile returns immutable match-owned
/// settings; "base" is the shipped tuning, the rest are lab candidates for feel comparison.
/// </summary>
public static class TuningProfiles
{
    /// <summary>One named tuning. Description documents the hitstun formula it produces.</summary>
    public readonly record struct Profile(string Name, string Description, CombatTuning Tuning);

    public static IReadOnlyList<Profile> Profiles { get; } = Array.AsReadOnly(new[]
    {
        new Profile("base",       "shipped (melee-soft, #149) — stun 0.45×mag, KV×0.17", CombatTuning.Default),
        new Profile("old",        "pre-adoption — stun 0.5×mag, KV×0.14",        new CombatTuning(0.14f, 0.5f)),
        new Profile("stunx18",    "hitstun ×1.8 — stun 0.9×mag, KV×0.14",        new CombatTuning(0.14f, 0.9f)),
        new Profile("kv70",       "travel −50% — stun 0.5×mag, KV×0.10",         new CombatTuning(0.10f, 0.5f)),
        new Profile("stun16kv11", "Melee-ish ratio — stun 0.8×mag, KV×0.11",     new CombatTuning(0.11f, 0.8f)),
        new Profile("floor30",    "Melee +30 floor — stun 0.5×(mag+30), KV×0.14", new CombatTuning(0.14f, 0.5f, 30f)),
        // Melee-shaped family (feel pass 2026-08-18): hitstun = 0.4×mag (Melee coefficient,
        // no floor) so 0% hits barely stun (no true combos at 0%) and combos emerge with %;
        // KV raised ~1.7× so launches read as a pop (≈ run-speed order) instead of a glide.
        new Profile("melee",      "Melee shape — stun 0.4×mag, KV×0.19",        new CombatTuning(0.19f, 0.4f)),
        new Profile("melee-hot",  "Melee shape, hotter — stun 0.4×mag, KV×0.22", new CombatTuning(0.22f, 0.4f)),
        new Profile("melee-soft", "Melee shape, softer — stun 0.45×mag, KV×0.17", CombatTuning.Default),
    });

    /// <summary>Resolve a profile by name (case-insensitive). Unknown names return false and shipped defaults.</summary>
    public static bool TryGet(string name, out CombatTuning tuning)
    {
        var p = Find(name);
        tuning = p?.Tuning ?? CombatTuning.Default;
        return p != null;
    }

    /// <summary>Resolve a profile by name; throws <see cref="ArgumentException"/> when unknown.</summary>
    public static CombatTuning Get(string name)
        => (Find(name) ?? throw new ArgumentException(
            $"unknown tuning profile '{name}' (expected one of: {string.Join(", ", Profiles.Select(x => x.Name))})")).Tuning;

    /// <summary>Human-readable summary of what a profile does (for tool log lines).</summary>
    public static string Describe(string name)
    {
        var p = Find(name);
        return p != null
            ? $"{p.Value.Name} — {p.Value.Description}"
            : $"unknown profile '{name}'";
    }

    private static Profile? Find(string name)
    {
        foreach (var p in Profiles)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}
