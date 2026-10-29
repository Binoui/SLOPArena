using System;

namespace SlopArena.Shared;

/// <summary>
/// A two-form fighter's live form and the other form's cooldown bank (ADR-0051). Fighters without forms keep the
/// default forever: form 0 and an empty bank.
/// </summary>
public struct FormState
{
    /// <summary>0 plays the package's top-level kit, 1 its alternateForm.</summary>
    public byte Active;
    /// <summary>The inactive form's cooldowns, counting down like the live ones. F has none: one clock serves both.</summary>
    public ushort Bank1, Bank3, Bank4, BankA, BankE, BankR;
}

public static class Forms
{
    private static readonly byte[] BankedSlots =
        { AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4, AbilitySlots.A, AbilitySlots.E, AbilitySlots.R };

    /// <summary>Slots whose cooldown belongs to one form.</summary>
    public static bool IsBanked(byte slot) => Array.IndexOf(BankedSlots, slot) >= 0;

    /// <summary>The inactive form's clock for a banked slot.</summary>
    public static ushort GetBanked(in FormState form, byte slot) => slot switch
    {
        AbilitySlots.Slot1 => form.Bank1,
        AbilitySlots.Slot3 => form.Bank3,
        AbilitySlots.Slot4 => form.Bank4,
        AbilitySlots.A => form.BankA,
        AbilitySlots.E => form.BankE,
        AbilitySlots.R => form.BankR,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), "F and the retired slots have no bank."),
    };

    private static void SetBanked(ref FormState form, byte slot, ushort ticks)
    {
        switch (slot)
        {
            case AbilitySlots.Slot1: form.Bank1 = ticks; break;
            case AbilitySlots.Slot3: form.Bank3 = ticks; break;
            case AbilitySlots.Slot4: form.Bank4 = ticks; break;
            case AbilitySlots.A: form.BankA = ticks; break;
            case AbilitySlots.E: form.BankE = ticks; break;
            case AbilitySlots.R: form.BankR = ticks; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot), "F and the retired slots have no bank.");
        }
    }

    /// <summary>A finished move's cooldown goes to the form it started in: live when that form is active, else its bank.</summary>
    internal static void SetCooldown(ref CharacterState s, byte slot, byte moveForm, ushort ticks)
    {
        if (moveForm != s.Form.Active && IsBanked(slot)) SetBanked(ref s.Form, slot, ticks);
        else s.SetCooldown(slot, ticks);
    }

    internal static void TickBank(ref FormState form)
    {
        foreach (byte slot in BankedSlots)
        {
            ushort ticks = GetBanked(form, slot);
            if (ticks > 0) SetBanked(ref form, slot, (ushort)(ticks - 1));
        }
    }

    /// <summary>
    /// The swapForm operation. Her percent, position, velocity, air time, float window and fast fall carry over; the
    /// next form's movement applies from the next tick and her jumps left are clamped to its maxJumps. The live
    /// cooldowns and the bank trade places. A fighter without forms is unchanged.
    /// </summary>
    internal static void Swap(ref CharacterState s, CharacterDefinition definition)
    {
        var next = definition.ForForm((byte)(s.Form.Active ^ 1));
        if (next.Form == s.Form.Active) return;
        foreach (byte slot in BankedSlots)
        {
            ushort live = s.GetCooldown(slot);
            s.SetCooldown(slot, GetBanked(s.Form, slot));
            SetBanked(ref s.Form, slot, live);
        }
        s.Form.Active = next.Form;
        if (s.JumpsLeft > next.Movement.MaxJumps) s.JumpsLeft = next.Movement.MaxJumps;
    }
}
