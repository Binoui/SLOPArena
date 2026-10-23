using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SlopArena.Shared;

public readonly record struct SlotAddress(string Id, bool IsAirborne, string InputLabel, byte Ordinal);

public static class CanonicalSlotProjection
{
    private static readonly byte[] WireSlots =
    {
        AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
        AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
    };

    private static readonly IReadOnlyList<SlotAddress> AllSlots = new ReadOnlyCollection<SlotAddress>(
        Enumerable.Range(0, 16)
            .Select(ordinal =>
            {
                var isAirborne = ordinal >= 8;
                var inputLabel = new[] { "1", "2", "3", "4", "A", "E", "R", "F" }[ordinal % 8];
                return new SlotAddress($"{(isAirborne ? "air" : "ground")}.{inputLabel}", isAirborne, inputLabel, (byte)ordinal);
            })
            .ToArray());

    private static readonly IReadOnlyDictionary<string, SlotAddress> ById =
        AllSlots.ToDictionary(slot => slot.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<(bool IsAirborne, string InputLabel), SlotAddress> ByInput =
        AllSlots.ToDictionary(slot => (slot.IsAirborne, slot.InputLabel));

    public static IReadOnlyList<SlotAddress> All => AllSlots;

    public static bool TryGet(byte wireSlot, bool airborne, out SlotAddress address)
    {
        int index = Array.IndexOf(WireSlots, wireSlot);
        address = index >= 0 ? AllSlots[index + (airborne ? 8 : 0)] : default;
        return index >= 0;
    }

    public static byte ToWire(SlotAddress address)
        => TryGet(address.Id, out var canonical) && canonical == address
            ? WireSlots[canonical.Ordinal % 8]
            : throw new ArgumentException("A canonical slot address is required.", nameof(address));

    public static bool TryGet(string canonicalId, out SlotAddress address)
    {
        if (!string.IsNullOrEmpty(canonicalId) && ById.TryGetValue(canonicalId, out address))
            return true;

        address = default;
        return false;
    }

    public static bool TryGet(bool isAirborne, string inputLabel, out SlotAddress address)
    {
        if (!string.IsNullOrEmpty(inputLabel) && ByInput.TryGetValue((isAirborne, inputLabel), out address))
            return true;

        address = default;
        return false;
    }
}
